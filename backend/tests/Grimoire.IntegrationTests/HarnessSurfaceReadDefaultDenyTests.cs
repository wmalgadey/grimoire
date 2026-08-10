using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.HarnessSurfaces;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T066 (022-align-wiki-structure, US3, ADR-023, SC-010) — with nothing configured
/// (default <c>HarnessSurfaceReadOptions</c>, all four booleans <c>false</c>), a scripted
/// agent run attempting <c>list_files("tasks")</c> and <c>read_file("conversations/x.md")</c>
/// are both denied with reason <c>harness_surface_not_granted</c>, each recorded as a
/// <see cref="DeniedActionRecord"/>, and the run reaches a normal terminal state — not a
/// crash (FR-016: the denial rides the existing guarded-tool funnel, "run continues").
///
/// Mirrors <c>QueryWriteScopeDenialTests</c>'/<c>RemediationTargetPathTests</c>' shape: a
/// scripted <see cref="FakeModelClient"/> drives a real <see cref="GuardedToolExecutor"/>
/// over a real temp-directory wiki root — hermetic (Constitution Principle II), no live
/// LLM call. <see cref="HarnessSurfaceReadScope.ResolveDeniedSubtreePaths"/> is the exact
/// production mapping every agent's <c>AgentHost</c> applies at startup (Phase 5's central
/// wiring), invoked here directly with an empty granted-surface list — the deny-by-default
/// posture (FR-015).
/// </summary>
public class HarnessSurfaceReadDefaultDenyTests
{
    [Fact]
    public async Task ListFilesOnReservedSurface_IsDenied_HarnessSurfaceNotGranted_RunContinues()
    {
        var (executor, wikiRoot) = await BuildExecutorAsync(grantedSurfaces: []);

        try
        {
            var fakeModel = new FakeModelClient([
                FakeModelClient.ListFilesTurn("t1", "tasks"),
                FakeModelClient.FinalTurn("I could not list the tasks surface; here is what the wiki itself contains."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: ToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "What tasks are currently running?")],
                "turn-deny-list-tasks",
                CancellationToken.None);

            // FR-016: the run reached a normal terminal state, not a crash.
            Assert.Equal("I could not list the tasks surface; here is what the wiki itself contains.", result.Narrative);
            Assert.Equal(2, fakeModel.CallCount);

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("list_files", denial.Action);
            Assert.Equal("harness_surface_not_granted", denial.Reason);
            Assert.Equal("tasks", denial.RequestedTarget);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task ReadFileOnReservedSurface_IsDenied_HarnessSurfaceNotGranted_RunContinues()
    {
        var (executor, wikiRoot) = await BuildExecutorAsync(grantedSurfaces: []);
        var conversationRecordPath = Path.Combine(wikiRoot, "conversations", "x.md");
        Directory.CreateDirectory(Path.GetDirectoryName(conversationRecordPath)!);
        await File.WriteAllTextAsync(conversationRecordPath, "---\nconversation_id: x\n---\n");

        try
        {
            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("t1", "conversations/x.md"),
                FakeModelClient.FinalTurn("I could not read that conversation record; here is what the wiki itself contains."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: ToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "What did an earlier conversation say?")],
                "turn-deny-read-conversation",
                CancellationToken.None);

            Assert.Equal("I could not read that conversation record; here is what the wiki itself contains.", result.Narrative);
            Assert.Equal(2, fakeModel.CallCount);

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("read_file", denial.Action);
            Assert.Equal("harness_surface_not_granted", denial.Reason);
            Assert.Equal("conversations/x.md", denial.RequestedTarget);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task BothDenials_AreRecordedIndependently_AndOrdinaryWikiContentRemainsReadable()
    {
        var (executor, wikiRoot) = await BuildExecutorAsync(grantedSurfaces: []);
        var articlePath = Path.Combine(wikiRoot, "tech", "kubernetes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(articlePath)!);
        await File.WriteAllTextAsync(articlePath, "---\ntitle: Kubernetes\n---\n\nBody.\n");

        try
        {
            var fakeModel = new FakeModelClient([
                FakeModelClient.ListFilesTurn("t1", "tasks"),
                FakeModelClient.ReadFileTurn("t2", "conversations/x.md"),
                FakeModelClient.ReadFileTurn("t3", "tech/kubernetes.md"),
                FakeModelClient.FinalTurn("Two harness surfaces were off-limits; the wiki content itself was readable."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: ToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "Summarize everything you can find.")],
                "turn-deny-both-plus-ordinary-read",
                CancellationToken.None);

            Assert.Equal("Two harness surfaces were off-limits; the wiki content itself was readable.", result.Narrative);
            Assert.Equal(4, fakeModel.CallCount);

            Assert.Equal(2, executor.Denials.Count);
            Assert.All(executor.Denials, d => Assert.Equal("harness_surface_not_granted", d.Reason));

            // SC-010: ordinary wiki content is unaffected by the harness-surface denial —
            // the third tool call (a real article, not a reserved surface) succeeded. Its
            // result appears in the FOURTH call's conversation (call index 3): each call's
            // own conversation carries the results of every tool use requested in the
            // PRIOR call, not the one it itself is about to request.
            var fourthCall = fakeModel.Calls[3];
            var toolResults = fourthCall.Conversation.SelectMany(m => m.ContentBlocks).OfType<ConversationToolResultBlock>().ToList();
            Assert.Contains(toolResults, r => r.ToolUseId == "t3" && !r.IsError && r.Content.Contains("Body."));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── shared setup ───────────────────────────────────────────────────────────────────

    private static async Task<(GuardedToolExecutor Executor, string WikiRoot)> BuildExecutorAsync(IReadOnlyList<string> grantedSurfaces)
    {
        var root = Path.Combine(Path.GetTempPath(), $"harness-surface-default-deny-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);

        // Every shipped policy grants the whole content root on read ("." →
        // wiki-root-with-trailing-separator, ADR-023's Context section) — reproduced
        // directly here rather than loading a real policy.json, since this test's subject
        // is the denied-subtree narrowing, not policy-file parsing (already covered by
        // PolicyLoader's own tests).
        var readPrefixes = new[] { wikiRoot + Path.DirectorySeparatorChar };
        var deniedReadSubtrees = HarnessSurfaceReadScope.ResolveDeniedSubtreePaths(wikiRoot, grantedSurfaces);

        var policy = new SafetyPolicy(
            wikiRoot,
            readPrefixes,
            writeRules: [],
            deniedReadSubtrees: deniedReadSubtrees);

        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            policy,
            journal,
            wikiRoot,
            taskId: "turn-harness-surface-default-deny",
            registry: ToolRegistry.Default);

        await Task.CompletedTask;
        return (executor, wikiRoot);
    }

    private static void CleanUp(string wikiRoot)
    {
        var root = Path.GetDirectoryName(wikiRoot)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

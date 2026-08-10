using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.HarnessSurfaces;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T068 (022-align-wiki-structure, US3, ADR-023, research.md R6, quickstart scenario 5) —
/// a remediation message turn succeeds under the all-denied default, because the
/// proposal's context (title, description, attached context, prior conversation) reaches
/// the agent via the Hub-injected kickoff message
/// (<c>Grimoire.LintAgent.Program.MessageTurnIntentHandler</c>'s <c>KickoffMessageTemplate</c>),
/// never through a guarded <c>read_file</c> call. This is the "asymmetry" ADR-023's
/// Consequences section documents explicitly: the Hub decides what to put in front of the
/// agent (ADR-018's Hub-injected remediation context); the guarded boundary governs only
/// what the agent may reach on its own — so denying <c>remediation-tasks/</c> does not
/// break message-turn mode.
///
/// Mirrors the message-turn mode's exact policy-narrowing chain
/// (<c>Grimoire.AgentRuntime.Host.AgentHost</c> applies
/// <see cref="SafetyPolicy.WithDeniedReadSubtrees"/> first, then
/// <c>MessageTurnIntentHandler</c> applies <see cref="SafetyPolicy.WithNoWriteAccess"/> on
/// top) and the kickoff-message shape, without spawning a real process — hermetic, no live
/// LLM call (Constitution Principle II).
/// </summary>
public class RemediationUnaffectedByReadScopeTests
{
    private const string KickoffMessageTemplate =
        "You are running in MESSAGE-TURN MODE.\n\n" +
        "Title: {0}\n" +
        "Description: {1}\n" +
        "Human's message: {2}\n";

    [Fact]
    public async Task MessageTurn_SucceedsUnderAllDeniedDefault_BecauseContextArrivesViaKickoffMessage_NotAGuardedRead()
    {
        var (executor, wikiRoot) = await BuildMessageTurnExecutorAsync(grantedSurfaces: []);

        try
        {
            // Exactly ADR-018's shape: proposal title/description arrive as plain text in
            // the kickoff message the Hub built from its own RemediationTaskRecordStore
            // read — the agent process itself never touches remediation-tasks/ to answer.
            var kickoffMessage = string.Format(
                KickoffMessageTemplate,
                "Fix broken wikilink",
                "The article `tech/kubernetes.md` links to a slug that no longer exists.",
                "Is this proposal still relevant?");

            var fakeModel = new FakeModelClient([
                FakeModelClient.FinalTurn("Yes, the broken link is still present based on the description above."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: ToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test remediation message-turn agent.",
                [new ConversationMessage("user", kickoffMessage)],
                "task-remediation-message-turn",
                CancellationToken.None);

            // The turn completed successfully purely from the kickoff message content —
            // zero tool calls were needed, so zero denials, even though every reserved
            // surface (including remediation-tasks/ itself) is denied by default.
            Assert.Equal("Yes, the broken link is still present based on the description above.", result.Narrative);
            Assert.Equal(1, fakeModel.CallCount);
            Assert.Empty(executor.Denials);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task MessageTurn_AttemptedReadOfDeniedRemediationTasksSurface_IsDenied_ButTheTurnStillCompletes()
    {
        var (executor, wikiRoot) = await BuildMessageTurnExecutorAsync(grantedSurfaces: []);

        try
        {
            var remediationTaskRecordPath = Path.Combine(wikiRoot, "remediation-tasks", "task-1.md");
            Directory.CreateDirectory(Path.GetDirectoryName(remediationTaskRecordPath)!);
            await File.WriteAllTextAsync(remediationTaskRecordPath, "---\ntask_id: task-1\n---\n");

            var kickoffMessage = string.Format(
                KickoffMessageTemplate,
                "Fix broken wikilink",
                "The article `tech/kubernetes.md` links to a slug that no longer exists.",
                "Is this proposal still relevant?");

            // Reinforces the asymmetry concretely: even if the agent tries to reach for
            // its own task record directly (redundant, since the same content already
            // rode the kickoff message), the guarded read is denied — and the run still
            // reaches a normal terminal state (FR-016), demonstrating the two mechanisms
            // (Hub-injected context vs. the guarded tool boundary) are independent.
            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("t1", "remediation-tasks/task-1.md"),
                FakeModelClient.FinalTurn("I could not re-read my own task record, but the description above already told me: yes, still relevant."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: ToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test remediation message-turn agent.",
                [new ConversationMessage("user", kickoffMessage)],
                "task-remediation-message-turn-with-read-attempt",
                CancellationToken.None);

            Assert.Equal(
                "I could not re-read my own task record, but the description above already told me: yes, still relevant.",
                result.Narrative);
            Assert.Equal(2, fakeModel.CallCount);

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("read_file", denial.Action);
            Assert.Equal("harness_surface_not_granted", denial.Reason);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task MessageTurn_WriteAttempt_IsDenied_OutOfScope_NotHarnessSurfaceNotGranted()
    {
        // Sanity: WithNoWriteAccess (applied on top of the read-scope narrowing, matching
        // MessageTurnIntentHandler's exact chain) still governs writes — the two
        // narrowings are independent and neither masks the other's denial reason.
        var (executor, wikiRoot) = await BuildMessageTurnExecutorAsync(grantedSurfaces: []);

        try
        {
            var fakeModel = new FakeModelClient([
                FakeModelClient.WriteFileTurn("t1", "tech/kubernetes.md", "vandalized content"),
                FakeModelClient.FinalTurn("I did not write anything — this mode is read-only."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: ToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test remediation message-turn agent.",
                [new ConversationMessage("user", "Please fix it directly.")],
                "task-remediation-message-turn-write-attempt",
                CancellationToken.None);

            Assert.Equal("I did not write anything — this mode is read-only.", result.Narrative);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal("write_file", denial.Action);
            Assert.Equal("out_of_scope", denial.Reason);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── shared setup ───────────────────────────────────────────────────────────────────

    private static async Task<(GuardedToolExecutor Executor, string WikiRoot)> BuildMessageTurnExecutorAsync(
        IReadOnlyList<string> grantedSurfaces)
    {
        var root = Path.Combine(Path.GetTempPath(), $"remediation-unaffected-by-read-scope-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);
        Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
        await File.WriteAllTextAsync(Path.Combine(wikiRoot, "tech", "kubernetes.md"), "---\ntitle: Kubernetes\n---\n\nBody.\n");

        // Same chain AgentHost + MessageTurnIntentHandler apply in production: the
        // read-scope narrowing (ADR-023) first, then the write-scope narrowing (ADR-018)
        // on top — proves the two compose without either masking the other.
        var readPrefixes = new[] { wikiRoot + Path.DirectorySeparatorChar };
        var writeRules = new[] { new WriteRule(wikiRoot + Path.DirectorySeparatorChar) };
        var deniedReadSubtrees = HarnessSurfaceReadScope.ResolveDeniedSubtreePaths(wikiRoot, grantedSurfaces);

        var basePolicy = new SafetyPolicy(wikiRoot, readPrefixes, writeRules, deniedReadSubtrees);
        var messageTurnPolicy = basePolicy.WithNoWriteAccess();

        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            messageTurnPolicy,
            journal,
            wikiRoot,
            taskId: "task-remediation-message-turn",
            registry: ToolRegistry.Default);

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

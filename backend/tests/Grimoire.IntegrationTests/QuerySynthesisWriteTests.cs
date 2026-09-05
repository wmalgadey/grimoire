using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.Hub.QueryConversations;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.QueryAgent;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T018/T019 (012-query-synthesis-writes, US1, SC-002) — the Query agent can preserve a
/// Synthesis as a new wiki page, update the index and log in the same turn, and the
/// turn's persistent record lists exactly the page it created.
/// </summary>
public class QuerySynthesisWriteTests
{
    // ── T018: end-to-end through the real GuardedToolExecutor/policy/guard stack ──────

    [Fact]
    public async Task ScriptedSynthesisTurn_CreatesPageThenIndexThenLog_AllSucceed_OnlyThePageIsCreateOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"query-synthesis-write-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        var techDir = Path.Combine(wikiRoot, "tech", "concepts");
        Directory.CreateDirectory(techDir);
        var indexPath = Path.Combine(wikiRoot, "index.md");
        var logPath = Path.Combine(wikiRoot, "log.md");
        await File.WriteAllTextAsync(indexPath, "# Wiki Index\n\n- [[credential-scoping]] — existing page.\n");
        await File.WriteAllTextAsync(logPath, "## 2026-07-30\n\n* **Ingest**: existing entry.\n");

        try
        {
            var repoRoot = FindRepositoryRoot();
            var policyPath = Path.Combine(repoRoot, "backend", "src", "Grimoire.QueryAgent", "Instructions", "policy.json");
            Assert.True(File.Exists(policyPath), $"Expected repo file not found: {policyPath}");

            var loader = new PolicyLoader(wikiRoot);
            var loadResult = await loader.LoadAsync(policyPath, CancellationToken.None);
            Assert.True(loadResult.IsFirst(out var loadedPolicy));

            var journal = new WriteJournal();
            var writeLocksDir = Path.Combine(root, "write-locks");
            var executor = new GuardedToolExecutor(
                loadedPolicy.Policy,
                journal,
                wikiRoot,
                taskId: "turn-synthesis-1",
                registry: QueryToolRegistry.Default,
                writeLocksDir: writeLocksDir);

            const string newPageRelativePath = "tech/concepts/single-composition-point.md";
            const string newPageContent = """
                ---
                type: Concept
                title: Single Composition Point
                description: Credential scoping and runtime-path resolution both funnel through one composition point.
                timestamp: 2026-07-30T00:00:00Z
                tags:
                  - source-type/synthesis
                  - concept/Single-Composition-Point
                confidence: medium
                confidence_reason: "Inferred from two independent architecture pages; no explicit cross-reference exists yet."
                review_date: 2027-01-30
                ---

                Both [[credential-scoping]] and [[runtime-paths]] resolve their respective concerns at a
                single, explicit composition point rather than through ambient discovery.
                """;
            const string updatedIndex = "# Wiki Index\n\n- [[credential-scoping]] — existing page.\n- [[concepts/single-composition-point]] — Synthesis: credential scoping and runtime paths share a pattern.\n";
            const string updatedLog = "## 2026-07-30\n\n* **Synthesis**: created [[concepts/single-composition-point]] — query: \"How do these relate?\"\n* **Ingest**: existing entry.\n";

            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("t1", "index.md"),
                FakeModelClient.ReadFileTurn("t2", "log.md"),
                FakeModelClient.WriteFileTurn("t3", newPageRelativePath, newPageContent),
                FakeModelClient.WriteFileTurn("t4", "index.md", updatedIndex),
                FakeModelClient.WriteFileTurn("t5", "log.md", updatedLog),
                FakeModelClient.FinalTurn(
                    "Credential scoping and runtime-path resolution both funnel through one composition " +
                    "point. I've saved this as a new page, [[concepts/single-composition-point]]."),
            ]);

            var loop = new AgentLoop(fakeModel, executor, registry: QueryToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "How do our credential-scoping decisions relate to the runtime-path decisions?")],
                "turn-synthesis-1",
                CancellationToken.None);

            Assert.Empty(executor.Denials);
            Assert.Contains("single-composition-point", result.Narrative);

            var canonicalPagePath = Path.GetFullPath(Path.Combine(wikiRoot, newPageRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var canonicalIndexPath = Path.GetFullPath(indexPath);
            var canonicalLogPath = Path.GetFullPath(logPath);

            Assert.Equal(
                new[] { canonicalPagePath, canonicalIndexPath, canonicalLogPath },
                executor.TouchedPaths);

            // Only the page matched a create-only rule — index.md/log.md are plain
            // read-write targets even though this run wrote them (ADR-015).
            Assert.Equal([canonicalPagePath], executor.CreatedPaths);

            Assert.True(File.Exists(canonicalPagePath));
            Assert.Contains("source-type/synthesis", await File.ReadAllTextAsync(canonicalPagePath));

            // RunCompletionMetadata.CreatedArtifacts (T025) is populated verbatim from
            // GuardedToolExecutor.CreatedPaths by Grimoire.QueryAgent/Program.cs.
            var metadata = new RunCompletionMetadata(CreatedArtifacts: executor.CreatedPaths);
            Assert.Equal([canonicalPagePath], metadata.CreatedArtifacts);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // ── T019: created_pages: in the Conversation Record's turn bookkeeping ────────────

    [Fact]
    public async Task CompletedTurn_WithCreatedPages_RecordsThemInTheConversationRecord()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(
            client, "c-synthesis-created", "How do our credential-scoping decisions relate to the runtime-path decisions?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "Saved as [[concepts/single-composition-point]]." });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);

        // The agent process reports the canonical (absolute) path it wrote, exactly like
        // GuardedToolExecutor.CreatedPaths (data-model.md "Run Completion Metadata") — the
        // Hub converts it to wiki-root-relative before it reaches the record (ADR-015).
        var canonicalCreatedPage = Path.Combine(root, "wiki", "tech", "concepts", "single-composition-point.md");
        handle.EmitEvent("completed", turnId, new
        {
            summary = "Saved as [[concepts/single-composition-point]].",
            createdPages = new[] { canonicalCreatedPage },
        });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-synthesis-created");

        Assert.Equal(["tech/concepts/single-composition-point.md"], recordedTurn.CreatedPagesOrEmpty);
    }

    [Fact]
    public async Task CompletedTurn_ThatCreatesNothing_RecordsCreatedPagesAsExplicitEmptyList()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(
            client, "c-synthesis-none", "What does the Credential Scoping page say?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "It says the key is scoped to the child process." });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);
        handle.EmitEvent("completed", turnId, new { summary = "It says the key is scoped to the child process." });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-synthesis-none");

        Assert.Empty(recordedTurn.CreatedPagesOrEmpty);

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-synthesis-none");
        var content = await File.ReadAllTextAsync(recordPath);
        Assert.Contains("created_pages: []", content, StringComparison.Ordinal);
    }

    private static async Task<QueryRecordedTurn> ReadSingleTurnAsync(string root, string conversationId)
    {
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor(conversationId);

        // 019-fast-test-tier (ADR-021 edge case: genuine race surfaced by suite
        // parallelization, fixed at the root): File.Exists becoming true does not mean the
        // write has finished — poll for a successful structured parse too, not just
        // existence, to avoid reading mid-write content under heavy concurrent-suite load.
        QueryConversationRecordParseResult.Parsed? parsed = null;
        await PollAsync.WaitAsync(
            async () =>
            {
                if (!File.Exists(recordPath))
                {
                    return false;
                }

                return QueryConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)) is QueryConversationRecordParseResult.Parsed { Turns.Count: >= 1 } p
                    && (parsed = p) is not null;
            },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to exist and parse with at least one turn within 10s.");

        return Assert.Single(parsed!.Turns);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src", "Grimoire.QueryAgent", "Instructions")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}

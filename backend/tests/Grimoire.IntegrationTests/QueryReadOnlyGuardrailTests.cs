using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.QueryAgent;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T068 (US4, SC-002/FR-011/FR-012) — a scripted out-of-scope `read_file` request is
/// denied by the Query policy, recorded with a reason, and the run continues with
/// allowed actions afterward; confirms zero wiki writes occur across the scenario (no
/// `write_file` tool exists at all for the Query agent to even attempt calling, per T021).
/// </summary>
public class QueryReadOnlyGuardrailTests
{
    [Fact]
    public async Task OutOfScopeReadFile_IsDenied_RecordedWithReason_RunContinues_NoWritesEverOccur()
    {
        var root = Path.Combine(Path.GetTempPath(), $"query-readonly-guardrail-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var pagesDir = Path.Combine(wikiDir, "pages");
        Directory.CreateDirectory(pagesDir);
        await File.WriteAllTextAsync(Path.Combine(pagesDir, "adr.md"), "# ADR notes");

        var queryRunsDir = Path.Combine(root, "data", "query-runs");
        Directory.CreateDirectory(queryRunsDir);
        var outOfScopeFile = Path.Combine(queryRunsDir, "other-conversation.md");
        await File.WriteAllTextAsync(outOfScopeFile, "someone else's turn");

        try
        {
            // Mirrors data/agents/query/policy.json: read is scoped to pages/, index.md,
            // log.md; write is empty — no write rule exists at all for Query.
            var policy = new SafetyPolicy(
                wikiDir,
                readPrefixes: [Path.Combine(wikiDir, "pages") + Path.DirectorySeparatorChar],
                writePrefixes: []);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, wikiDir, taskId: "turn-guardrail-1", registry: QueryToolRegistry.Default);

            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("tool-1", "../data/query-runs/other-conversation.md"),
                FakeModelClient.ReadFileTurn("tool-2", "pages/adr.md"),
                FakeModelClient.FinalTurn("The wiki covers ADR notes. I could not access an out-of-scope file.")]);

            var loop = new AgentLoop(fakeModel, executor, registry: QueryToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "What does the wiki cover, and what's in the query-runs history?")],
                "turn-guardrail-1",
                CancellationToken.None);

            Assert.Equal("The wiki covers ADR notes. I could not access an out-of-scope file.", result.Narrative);

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("read_file", denial.Action);
            Assert.False(string.IsNullOrWhiteSpace(denial.Reason));

            // The run continued and completed the allowed read afterward (SC-002/FR-012):
            // denied tool-call turn, allowed tool-call turn, final narrative turn.
            Assert.Equal(3, fakeModel.CallCount);

            // No write tool exists for QueryToolRegistry at all (FR-011 structural half).
            Assert.DoesNotContain(QueryToolRegistry.Default.Tools, t => t.Name == "write_file");
            Assert.Empty(journal.JournaledPaths);
            Assert.Empty(executor.TouchedPaths);
            Assert.Equal("someone else's turn", await File.ReadAllTextAsync(outOfScopeFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeniedAction_ReportedOnTerminalEvent_IsWrittenToTheFinalizedQueryRunArtifact()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-denial-artifact/turns", new { prompt = "What's in the query-runs history?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("completed", turnId, new
        {
            summary = "I could not access an out-of-scope file.",
            deniedActions = new[]
            {
                new
                {
                    action = "read_file",
                    requestedTarget = "../data/query-runs/other-conversation.md",
                    canonicalTarget = "/data/query-runs/other-conversation.md",
                    reason = "out_of_scope",
                    turn = 1,
                }
            }
        });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        Grimoire.Hub.QueryDispatch.QueryTurnState? turn = null;
        var coordinator = host.Services.GetRequiredService<Grimoire.Hub.QueryDispatch.QueryRunCoordinator>();
        while (DateTime.UtcNow < deadline)
        {
            turn = coordinator.GetTurn(turnId);
            if (turn is { Status: Grimoire.Hub.QueryDispatch.QueryTurnStatus.Completed })
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.NotNull(turn);
        Assert.Equal(Grimoire.Hub.QueryDispatch.QueryTurnStatus.Completed, turn!.Status);

        var resolvedPaths = host.Services.GetRequiredService<Grimoire.Hub.Runtime.Paths.ResolvedGrimoirePaths>();
        var artifactPath = resolvedPaths.QueryRunArtifactPathFor("c-denial-artifact", turnId);

        Assert.True(File.Exists(artifactPath));
        var artifact = await File.ReadAllTextAsync(artifactPath);
        Assert.Contains("denied_actions:", artifact);
        Assert.Contains("action: read_file", artifact);
        Assert.Contains("../data/query-runs/other-conversation.md", artifact);
        Assert.Contains("reason: \"out_of_scope\"", artifact);
    }
}

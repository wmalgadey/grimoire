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
/// allowed actions afterward; confirms zero wiki writes occur across this read-only
/// scenario. Since ADR-015 (012-query-synthesis-writes) Query's tool registry legitimately
/// includes `write_file` (scoped by policy, not by omission) — this test's scripted
/// conversation simply never calls it.
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

        // 011-query-conversations (T019 cutover): the out-of-scope harness location the
        // agent must not read is now the Conversation Record store, not query-runs.
        var conversationsDir = Path.Combine(root, "data", "conversations");
        Directory.CreateDirectory(conversationsDir);
        var outOfScopeFile = Path.Combine(conversationsDir, "other-conversation.md");
        await File.WriteAllTextAsync(outOfScopeFile, "someone else's conversation");

        try
        {
            // This scenario only exercises read-scope denial (SC-002/FR-012); write rules
            // are deliberately left empty here so any accidental write attempt is denied
            // out_of_scope rather than silently succeeding — data/agents/query/policy.json
            // itself does declare write rules since ADR-015 (012-query-synthesis-writes).
            var policy = new SafetyPolicy(
                wikiDir,
                readPrefixes: [Path.Combine(wikiDir, "pages") + Path.DirectorySeparatorChar],
                writePrefixes: []);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, wikiDir, taskId: "turn-guardrail-1", registry: QueryToolRegistry.Default);

            var fakeModel = new FakeModelClient([
                FakeModelClient.ReadFileTurn("tool-1", "../data/conversations/other-conversation.md"),
                FakeModelClient.ReadFileTurn("tool-2", "pages/adr.md"),
                FakeModelClient.FinalTurn("The wiki covers ADR notes. I could not access an out-of-scope file.")]);

            var loop = new AgentLoop(fakeModel, executor, registry: QueryToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "What does the wiki cover, and what's in the conversation history?")],
                "turn-guardrail-1",
                CancellationToken.None);

            Assert.Equal("The wiki covers ADR notes. I could not access an out-of-scope file.", result.Narrative);

            var denial = Assert.Single(executor.Denials);
            Assert.Equal("read_file", denial.Action);
            Assert.False(string.IsNullOrWhiteSpace(denial.Reason));

            // The run continued and completed the allowed read afterward (SC-002/FR-012):
            // denied tool-call turn, allowed tool-call turn, final narrative turn.
            Assert.Equal(3, fakeModel.CallCount);

            // The scripted conversation never called write_file — zero writes occurred,
            // even though the tool is now legitimately in Query's registry (ADR-015).
            Assert.Empty(journal.JournaledPaths);
            Assert.Empty(executor.TouchedPaths);
            Assert.Equal("someone else's conversation", await File.ReadAllTextAsync(outOfScopeFile));
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
    public async Task DeniedAction_ReportedOnTerminalEvent_IsWrittenToTheConversationRecord()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-denial-record/turns", new { prompt = "What's in the conversation history?" });
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
                    requestedTarget = "../data/conversations/other-conversation.md",
                    canonicalTarget = "/data/conversations/other-conversation.md",
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

        // 011-query-conversations (T019 cutover): the denial is recorded in the turn's
        // bookkeeping block of the Conversation Record (SC-002).
        var resolvedPaths = host.Services.GetRequiredService<Grimoire.Hub.Runtime.Paths.ResolvedGrimoirePaths>();
        var recordPath = resolvedPaths.ConversationRecordPathFor("c-denial-record");
        var recordDeadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(recordPath) && DateTime.UtcNow < recordDeadline)
        {
            await Task.Delay(20);
        }

        Assert.True(File.Exists(recordPath));
        var parsed = Assert.IsType<Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed>(
            Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)));
        var recordedTurn = Assert.Single(parsed.Turns);
        var denial = Assert.Single(recordedTurn.DeniedActions);
        Assert.Equal("read_file", denial.Action);
        Assert.Equal("../data/conversations/other-conversation.md", denial.RequestedTarget);
        Assert.Equal("/data/conversations/other-conversation.md", denial.CanonicalTarget);
        Assert.Equal("out_of_scope", denial.Reason);
        Assert.Equal(1, denial.Turn);
    }
}

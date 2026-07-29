using System.Net;
using System.Net.Http.Json;
using Grimoire.Hub.QueryConversations;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T014 (011-query-conversations, US1, SC-005 in-memory path) — the prior-turn context
/// handed to the launcher port on a follow-up submission tuple-equals
/// (<c>position</c>, <c>prompt</c>, <c>answer</c>, <c>state</c>) the turns parsed from
/// the Conversation Record with the contract parser — including a prior interrupted
/// turn whose partial answer must appear in both. The browser-supplied
/// <c>priorTurns</c> mechanism from 008 is retired (its propagation assertions were
/// deleted here); the record is the single context source (ADR-014). The 008
/// one-active-turn (409) and cross-conversation independence contracts are unchanged.
/// </summary>
public class QueryFollowUpContextTests
{
    [Fact]
    public async Task FollowUpSubmission_ReceivesPriorTurns_TupleEqualToTheParsedRecord_IncludingInterruptedPartial()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        // Turn 1: completes with a full answer.
        var turn1 = await ConversationRecordLifecycleTests.RunScriptedTurnAsync(
            client, launcher, handleIndex: 0, "c-followup",
            prompt: "What does ADR-004 decide?",
            answerChunks: ["ADR-004 decides..."],
            terminalExtra: new { summary = "done" });

        // Turn 2: interrupted with a partial answer.
        var turn2 = await ConversationRecordLifecycleTests.SubmitAsync(client, "c-followup", "What about the runtime paths?");
        launcher.Handles[1].EmitEvent("started", turn2);
        launcher.Handles[1].EmitEvent("answer_chunk", turn2, new { text = "The runtime paths are resolved " });
        await ConversationRecordLifecycleTests.WaitForAnswerAsync(client, turn2);
        (await client.PostAsync($"/api/query-turns/{turn2}/interrupt", content: null)).EnsureSuccessStatusCode();
        await ConversationRecordLifecycleTests.WaitForStateAsync(client, turn2, "interrupted");

        // Wait for both terminal turns to be recorded before the follow-up submits.
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-followup");
        await ConversationRecordLifecycleTests.WaitUntilAsync(() => Task.FromResult(
            File.Exists(recordPath) &&
            ConversationRecordFormat.Parse(File.ReadAllText(recordPath)) is ConversationRecordParseResult.Parsed { Turns.Count: 2 }));

        // Turn 3: the follow-up — capture the QueryAgentRequest handed to the launcher.
        await ConversationRecordLifecycleTests.SubmitAsync(client, "c-followup", "How do those two relate?");

        var request = launcher.QueryRequests[^1];
        Assert.Equal("How do those two relate?", request.Prompt);

        var parsed = Assert.IsType<ConversationRecordParseResult.Parsed>(
            ConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)));
        var expectedContext = parsed.Turns.Select(t => t.ToPriorTurn()).ToList();

        Assert.Equal(expectedContext, request.PriorTurns);

        // Explicit spot checks: the interrupted turn's partial answer appears in both.
        Assert.Equal(2, request.PriorTurns.Count);
        Assert.Equal(1, request.PriorTurns[0].Position);
        Assert.Equal("ADR-004 decides...", request.PriorTurns[0].Answer);
        Assert.Equal("completed", request.PriorTurns[0].State);
        Assert.Equal(2, request.PriorTurns[1].Position);
        Assert.Equal("The runtime paths are resolved ", request.PriorTurns[1].Answer);
        Assert.Equal("interrupted", request.PriorTurns[1].State);
        _ = turn1;
    }

    [Fact]
    public async Task PostTurn_OnConversationWithAlreadyRunningTurn_Returns409Conflict()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var client = host.GetTestClient();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-onerunning/turns", new { prompt = "First question?" });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-onerunning/turns", new { prompt = "Second question while the first is still running?" });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Single(launcher.QueryRequests);
    }

    [Fact]
    public async Task PostTurn_OnDifferentConversation_WhileAnotherIsRunning_Succeeds()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var client = host.GetTestClient();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-a/turns", new { prompt = "Question in conversation A?" });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-b/turns", new { prompt = "Question in conversation B?" });

        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Equal(2, launcher.QueryRequests.Count);
    }
}

using System.Net;
using System.Net.Http.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T059 (US3, FR-008/FR-009) — <c>priorTurns</c> supplied on a submission (including a
/// <c>state: "interrupted"</c> entry with partial answer text) are forwarded verbatim to
/// the spawned <c>Grimoire.QueryAgent</c> process's request (the actual message-scaffold
/// formatting happens inside that process, T062 — this test verifies the Hub-side
/// forwarding contract); a second submission on a conversation with an already-`running`
/// turn returns `409 Conflict` (FR-008 server-side guard).
/// </summary>
public class QueryFollowUpContextTests
{
    [Fact]
    public async Task PostTurn_WithPriorTurns_ForwardsThemVerbatim_IncludingAnInterruptedPartialAnswer()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var client = host.GetTestClient();

        var priorTurns = new object[]
        {
            new { position = 1, prompt = "What does ADR-004 decide?", answer = "ADR-004 decides...", state = "completed" },
            new { position = 2, prompt = "What about the runtime paths?", answer = "The runtime paths are resolved ", state = "interrupted" },
        };

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-followup/turns",
            new { prompt = "How do those two relate?", priorTurns });
        response.EnsureSuccessStatusCode();

        var request = Assert.Single(launcher.QueryRequests);
        Assert.Equal(2, request.PriorTurns.Count);

        Assert.Equal(1, request.PriorTurns[0].Position);
        Assert.Equal("What does ADR-004 decide?", request.PriorTurns[0].Prompt);
        Assert.Equal("ADR-004 decides...", request.PriorTurns[0].Answer);
        Assert.Equal("completed", request.PriorTurns[0].State);

        Assert.Equal(2, request.PriorTurns[1].Position);
        Assert.Equal("What about the runtime paths?", request.PriorTurns[1].Prompt);
        Assert.Equal("The runtime paths are resolved ", request.PriorTurns[1].Answer);
        Assert.Equal("interrupted", request.PriorTurns[1].State);

        Assert.Equal("How do those two relate?", request.Prompt);
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

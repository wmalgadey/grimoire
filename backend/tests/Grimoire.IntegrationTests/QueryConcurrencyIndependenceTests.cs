using System.Net;
using System.Net.Http.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T076 (Phase 7, SC-006/FR-017) — <c>IngestRunCoordinator</c> and <c>QueryRunCoordinator</c>
/// run concurrently against their respective <see cref="FakeAgentProcessLauncher"/>
/// requests with no shared lock/slot; submissions beyond <c>QueryConcurrencyLimit</c>
/// (default 3) are rejected immediately with 503, never queued.
/// </summary>
public class QueryConcurrencyIndependenceTests
{
    [Fact]
    public async Task QuerySubmission_IsAcceptedImmediately_WhileAnIngestRunIsStillInProgress()
    {
        var sharedLauncher = new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(600));
        using var ingestFixture = new IngestSubmissionPipelineFixture(launcher: sharedLauncher);
        using var queryHost = await QueryTurnSubmissionApiTests.BuildHostAsync(sharedLauncher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var queryClient = queryHost.GetTestClient();

        var ingestStarted = DateTimeOffset.UtcNow;
        await ingestFixture.Coordinator.EnqueueAsync("task-independence", Path.Combine(ingestFixture.Root, "a.md"), null);

        // Query dispatch must not wait on Ingest's single-slot queue: submitting and
        // getting a 202 back should take well under Ingest's 600ms simulated run.
        var response = await queryClient.PostAsJsonAsync(
            "/api/query-conversations/c-independence/turns", new { prompt = "What decisions exist?" });
        var queryAccepted = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(
            (queryAccepted - ingestStarted) < TimeSpan.FromMilliseconds(400),
            $"Query submission took {(queryAccepted - ingestStarted).TotalMilliseconds}ms — " +
            "it appears to have waited on Ingest's run slot instead of dispatching independently.");

        Assert.Single(sharedLauncher.QueryRequests);
        Assert.Single(sharedLauncher.Requests);
    }

    [Fact]
    public async Task SubmissionsBeyondTheConcurrencyLimit_AreRejectedImmediately_WithoutQueuing()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot(), concurrencyLimit: 3);
        var client = host.GetTestClient();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/query-conversations/c-limit-{i}/turns", new { prompt = $"Question {i}?" });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(
            "/api/query-conversations/c-limit-overflow/turns", new { prompt = "One too many?" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("query_concurrency_limit_reached", body.GetProperty("reason").GetString());

        // The 4th submission was rejected outright, never dispatched to the launcher.
        Assert.Equal(3, launcher.QueryRequests.Count);
    }
}

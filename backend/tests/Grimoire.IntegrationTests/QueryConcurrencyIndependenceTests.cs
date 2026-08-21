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
    /// <summary>
    /// #156 — the discriminating fact is not how long the POST took but whether Ingest was
    /// still holding its run slot when the query was accepted and dispatched, and that is
    /// observable directly. The previous shape timed the submission against a 400ms
    /// wall-clock budget and failed unrelated pull requests on a loaded runner, reporting
    /// "it appears to have waited on Ingest's run slot" on a measurement (446ms, against a
    /// 600ms simulated Ingest run) that proved the opposite. The go-silent launcher removes
    /// the race outright: the Ingest run starts, emits `started`, and then stays silent, so
    /// it holds the slot for the whole test with no duration to outrun.
    /// </summary>
    [Fact]
    public async Task QuerySubmission_IsAcceptedAndDispatched_WhileIngestStillHoldsItsRunSlot()
    {
        var sharedLauncher = new FakeAgentProcessLauncher { GoSilentIngestLaunches = 1 };
        using var ingestFixture = new IngestSubmissionPipelineFixture(launcher: sharedLauncher);
        using var queryHost = await QueryTurnSubmissionApiTests.BuildHostAsync(sharedLauncher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var queryClient = queryHost.GetTestClient();

        await ingestFixture.Coordinator.EnqueueAsync("task-independence", Path.Combine(ingestFixture.Root, "a.md"), null);
        Assert.Equal("task-independence", ingestFixture.Coordinator.RunningTaskId);

        var response = await queryClient.PostAsJsonAsync(
            "/api/query-conversations/c-independence/turns", new { prompt = "What decisions exist?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(sharedLauncher.QueryRequests);
        Assert.Single(sharedLauncher.Requests);

        // The slot was never free in between: the query was dispatched alongside the Ingest
        // run, not after it. Read last so a slot released mid-test cannot be missed.
        Assert.Equal("task-independence", ingestFixture.Coordinator.RunningTaskId);
    }

    // TimingDependent (ADR-021 FR-005): the three admitted runs hold their slots only for as
    // long as the launcher's simulated 5s run, so the fourth submission has to be made while
    // that is still true. Unlike the budget removed above, nothing here asserts a duration —
    // the marker records that the setup is time-based, not that the assertion is.
    [Trait("TimingDependent", "true")]
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
        Assert.Equal("query_concurrency_limit_reached", body.GetProperty("code").GetString());

        // The 4th submission was rejected outright, never dispatched to the launcher.
        Assert.Equal(3, launcher.QueryRequests.Count);
    }
}

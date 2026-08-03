using System.Diagnostics;
using System.Net.Http.Json;
using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T028 (US1, SC-003 harness half) — using <see cref="FakeAgentProcessLauncher"/>'s
/// scripted <c>answer_chunk</c> delta timing, asserts the deltas reach the
/// <see cref="QueryLifecyclePublisher"/>/<see cref="QueryLifecycleHub"/> in emission
/// order within budget. This is event-plumbing latency (Hub-internal), not end-to-end
/// LLM wall-clock — the fake removes the model call entirely.
/// </summary>
public class QueryAnswerStreamingTests
{
    [Fact]
    public async Task ScriptedAnswerChunks_ArriveAtSignalRClients_InOrder_WithinBudget()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks =
            [
                ("The wiki ", TimeSpan.Zero),
                ("describes three ", TimeSpan.FromMilliseconds(50)),
                ("decisions.", TimeSpan.FromMilliseconds(50)),
            ],
        };

        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var testServer = host.GetTestServer();
        var client = host.GetTestClient();

        var received = new List<QueryAnswerChunkEvent>();
        var arrivalElapsed = new List<TimeSpan>();
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/query-lifecycle", options =>
            {
                options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
            })
            .Build();

        var stopwatch = new Stopwatch();

        connection.On<QueryAnswerChunkEvent>("queryAnswerChunk", chunk =>
        {
            arrivalElapsed.Add(stopwatch.Elapsed);
            received.Add(chunk);
            if (received.Count >= 3)
            {
                allReceived.TrySetResult();
            }
        });

        await connection.StartAsync();

        // SC-003: first/subsequent answer content must be visible within 2s (p95) of the
        // agent producing it. The fake removes model wall-clock entirely, so the budget
        // is measured from submission (production begins immediately on dispatch, T032).
        stopwatch.Start();
        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-stream/turns", new { prompt = "What decisions does the wiki cover?" });
        response.EnsureSuccessStatusCode();

        // 019-fast-test-tier (ADR-021 R4): allReceived is signaled thread-safely from the
        // SignalR callback (TrySetResult); poll its completion instead of a raw
        // Task.WhenAny(..., Task.Delay(...)) bound so the wait routes through PollAsync.
        await PollAsync.WaitAsync(
            () => allReceived.Task.IsCompleted,
            TimeSpan.FromSeconds(5),
            "Expected 3 answer chunks to arrive via SignalR within 5s.");

        Assert.Equal(3, received.Count);
        Assert.Equal([1, 2, 3], received.Select(c => c.Sequence));
        Assert.Equal("The wiki ", received[0].Text);
        Assert.Equal("describes three ", received[1].Text);
        Assert.Equal("decisions.", received[2].Text);
        Assert.True(received.Select(c => c.TurnId).Distinct().Count() == 1);

        Assert.All(arrivalElapsed, elapsed => Assert.True(
            elapsed < TimeSpan.FromSeconds(2),
            $"SC-003 budget exceeded: a chunk arrived {elapsed.TotalMilliseconds:0}ms after submission (budget: 2000ms)."));
    }
}

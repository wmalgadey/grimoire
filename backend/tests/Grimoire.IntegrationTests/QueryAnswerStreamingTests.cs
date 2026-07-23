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
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/query-lifecycle", options =>
            {
                options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
            })
            .Build();

        connection.On<QueryAnswerChunkEvent>("queryAnswerChunk", chunk =>
        {
            received.Add(chunk);
            if (received.Count >= 3)
            {
                allReceived.TrySetResult();
            }
        });

        await connection.StartAsync();

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-stream/turns", new { prompt = "What decisions does the wiki cover?" });
        response.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(allReceived.Task, completed);

        Assert.Equal(3, received.Count);
        Assert.Equal([1, 2, 3], received.Select(c => c.Sequence));
        Assert.Equal("The wiki ", received[0].Text);
        Assert.Equal("describes three ", received[1].Text);
        Assert.Equal("decisions.", received[2].Text);
        Assert.True(received.Select(c => c.TurnId).Distinct().Count() == 1);
    }
}

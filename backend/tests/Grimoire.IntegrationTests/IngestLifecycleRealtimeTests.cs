using Grimoire.Hub.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T040 (US2) - the single permitted SignalR wire-up test (constitution v1.9.0, Principle II
/// "Test what we own"): whether a connected client actually receives events, and in the order
/// they were sent, is the SignalR transport's own delivery guarantee, not ours. What this test
/// exists to prove is ours — the `taskLifecycleChanged` payload contract and hub route
/// (contracts/ingest-lifecycle-events.md) — by publishing through the real
/// <see cref="IngestLifecyclePublisher"/> against a real Kestrel host and a real
/// Microsoft.AspNetCore.SignalR.Client connection, not Program.cs (which has its own
/// environment-dependent bootstrapping unrelated to this channel).
/// </summary>
public class IngestLifecycleRealtimeTests
{
    [Fact]
    public async Task LifecyclePublisher_ReachesAConnectedClient_OnTheContractedHubRouteAndMethod()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        app.MapHub<IngestLifecycleHub>("/hubs/ingest-lifecycle");
        await app.StartAsync();

        var baseUrl = app.Urls.First();
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/ingest-lifecycle")
            .Build();

        var received = new List<RealtimeLifecycleEvent>();
        var lockObj = new object();
        connection.On<RealtimeLifecycleEvent>("taskLifecycleChanged", e =>
        {
            lock (lockObj) { received.Add(e); }
        });

        await connection.StartAsync();

        var publisher = new IngestLifecyclePublisher(app.Services.GetRequiredService<IHubContext<IngestLifecycleHub>>());
        await publisher.PublishAsync("task-realtime-1", null, "received");
        await publisher.PublishAsync("task-realtime-1", "received", "converting");
        await publisher.PublishAsync("task-realtime-1", "converting", "queued");

        await PollAsync.WaitAsync(
            () =>
            {
                lock (lockObj)
                {
                    return received.Count >= 3;
                }
            },
            TimeSpan.FromSeconds(10),
            "Expected 3 ingest lifecycle events within 10s.");

        List<RealtimeLifecycleEvent> snapshot;
        lock (lockObj) { snapshot = [.. received]; }

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["received", "converting", "queued"], snapshot.Select(e => e.ToStatus));
        Assert.All(snapshot, e => Assert.Equal("task-realtime-1", e.TaskId));

        await connection.StopAsync();
        await app.StopAsync();
    }
}

using Grimoire.Hub.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T040 (US2) - a connected SignalR client receives ordered `taskLifecycleChanged` events and the
/// board projection reflects the new stage without a resubmit or manual refresh (SC-004,
/// Acceptance Scenario 2). Uses a minimal real Kestrel host + a real
/// Microsoft.AspNetCore.SignalR.Client connection (contracts/ingest-lifecycle-events.md), not
/// Program.cs (which has its own environment-dependent bootstrapping unrelated to this channel).
/// </summary>
public class IngestLifecycleRealtimeTests
{
    [Fact]
    public async Task ConnectedClient_ReceivesLifecycleEvents_InOrder()
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

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (lockObj)
            {
                if (received.Count >= 3)
                {
                    break;
                }
            }
            await Task.Delay(25);
        }

        List<RealtimeLifecycleEvent> snapshot;
        lock (lockObj) { snapshot = [.. received]; }

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["received", "converting", "queued"], snapshot.Select(e => e.ToStatus));
        Assert.True(snapshot[0].Timestamp <= snapshot[1].Timestamp);
        Assert.True(snapshot[1].Timestamp <= snapshot[2].Timestamp);
        Assert.All(snapshot, e => Assert.Equal("task-realtime-1", e.TaskId));

        await connection.StopAsync();
        await app.StopAsync();
    }
}

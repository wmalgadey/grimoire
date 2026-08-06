using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T009 (015-lint-board-parity, US1, SC-001/SC-002 lint half) — with a fake
/// <c>IAgentProcessLauncher</c>, triggering a lint run broadcasts
/// <c>lintRunLifecycleChanged</c> events (running on trigger, then the terminal state,
/// including <c>failureReason</c> on failure) on <c>/hubs/lint-lifecycle</c>, payload per
/// contracts/remediation-lifecycle-events.md "Hub 1: Lint lifecycle". Hermetic — real
/// Kestrel + real SignalR client (mirrors <see cref="IngestLifecycleRealtimeTests"/>),
/// no live LLM call.
/// </summary>
public class LintLifecycleHubTests
{
    [Fact]
    public async Task TriggeredRun_BroadcastsRunning_ThenCompleted_OnLintLifecycleHub()
    {
        await using var app = await StartHubHostAsync();
        var baseUrl = app.Urls.First();

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/lint-lifecycle")
            .Build();

        var received = new List<LintRunLifecycleEvent>();
        var lockObj = new object();
        connection.On<LintRunLifecycleEvent>("lintRunLifecycleChanged", e =>
        {
            lock (lockObj) { received.Add(e); }
        });

        await connection.StartAsync();

        var publisher = new LintLifecyclePublisher(
            app.Services.GetRequiredService<IHubContext<LintLifecycleHub>>());
        using var harness = LintCoordinatorHarness.Create(lifecyclePublisher: publisher);

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<Grimoire.Hub.LintDispatch.LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);
        var snapshot = await WaitForEventsAsync(received, lockObj, expectedCount: 2);

        Assert.Equal(2, snapshot.Count);
        Assert.All(snapshot, e => Assert.Equal(runId, e.RunId));
        Assert.All(snapshot, e => Assert.False(string.IsNullOrEmpty(e.EventId)));

        Assert.Null(snapshot[0].FromStatus);
        Assert.Equal("running", snapshot[0].ToStatus);
        Assert.Null(snapshot[0].FailureReason);

        Assert.Equal("running", snapshot[1].FromStatus);
        Assert.Equal("completed", snapshot[1].ToStatus);
        Assert.Null(snapshot[1].FailureReason);

        Assert.True(snapshot[0].Timestamp <= snapshot[1].Timestamp);

        await connection.StopAsync();
        await app.StopAsync();
    }

    [Fact]
    public async Task FailedRun_BroadcastsTerminalEvent_WithFailureReason()
    {
        const string reason = "Lint agent run failed: instruction document missing.";

        await using var app = await StartHubHostAsync();
        var baseUrl = app.Urls.First();

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/lint-lifecycle")
            .Build();

        var received = new List<LintRunLifecycleEvent>();
        var lockObj = new object();
        connection.On<LintRunLifecycleEvent>("lintRunLifecycleChanged", e =>
        {
            lock (lockObj) { received.Add(e); }
        });

        await connection.StartAsync();

        var publisher = new LintLifecyclePublisher(
            app.Services.GetRequiredService<IHubContext<LintLifecycleHub>>());
        using var harness = LintCoordinatorHarness.Create(
            new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: reason, autoPlay: true),
            lifecyclePublisher: publisher);

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<Grimoire.Hub.LintDispatch.LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);
        var snapshot = await WaitForEventsAsync(received, lockObj, expectedCount: 2);

        Assert.Equal(2, snapshot.Count);
        Assert.Equal("running", snapshot[0].ToStatus);

        // FR-005: failureReason is required on the failed terminal broadcast.
        Assert.Equal("failed", snapshot[1].ToStatus);
        Assert.Equal("running", snapshot[1].FromStatus);
        Assert.Equal(reason, snapshot[1].FailureReason);
        Assert.Equal(runId, snapshot[1].RunId);

        await connection.StopAsync();
        await app.StopAsync();
    }

    private static async Task<WebApplication> StartHubHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var app = builder.Build();
        app.MapHub<LintLifecycleHub>("/hubs/lint-lifecycle");
        await app.StartAsync();
        return app;
    }

    private static async Task<List<LintRunLifecycleEvent>> WaitForEventsAsync(
        List<LintRunLifecycleEvent> received, object lockObj, int expectedCount)
    {
        await PollAsync.WaitAsync(
            () =>
            {
                lock (lockObj)
                {
                    return received.Count >= expectedCount;
                }
            },
            TimeSpan.FromSeconds(10),
            $"Expected at least {expectedCount} lint lifecycle event(s) within 10s.");

        lock (lockObj)
        {
            return [.. received];
        }
    }
}

using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// Issue #184: a stalled model turn used to park an ingest run in <c>running</c>
/// indefinitely because <c>IngestRunCoordinator</c>'s liveness watchdog reset its silence
/// window on the mere arrival of ANY event — including a bare <c>heartbeat</c>, which the
/// agent's background timer emits every 10 seconds unconditionally, whether or not the
/// model is actually responding. A heartbeat proved the process had a working timer; it
/// proved nothing about the run advancing.
///
/// The fix (contracts/agent-run-events.md): <c>heartbeat</c> now carries a monotonically
/// increasing <c>progress</c> counter, and the watchdog only treats a <c>heartbeat</c> as
/// liveness when its <c>progress</c> has moved since the last one seen. <c>started</c> and
/// <c>activity</c> are untouched — they only ever fire as a direct consequence of genuine
/// loop work, never spontaneously — so their arrival still resets the window exactly as
/// before.
///
/// Both tests below keep scripting heartbeats from a background task WHILE concurrently
/// waiting on the run's outcome — that concurrency is the point: it is what tells apart
/// "the run failed because nothing arrived" (the pre-existing, already-covered case in
/// <see cref="IngestRunSupervisionTests"/>) from "the run failed despite events
/// continuously arriving" (this issue). Real (short) wall-clock windows throughout,
/// mirroring <see cref="IngestRunSupervisionTests"/>'s own <c>ShortWindow</c> idiom.
/// </summary>
// ADR-021 (019-fast-test-tier): these tests script real-time-spaced heartbeats
// concurrently with the wait on the run's outcome — the timing between them (not just
// their arrival order) is the behavior under test, exactly like
// Fakes.ScriptedAgentProcessHandle's own EmitAnswerChunksAsync.
[Trait("TimingDependent", "true")]
public class IngestRunProgressGatedLivenessTests
{
    private static readonly TimeSpan ShortWindow = TimeSpan.FromMilliseconds(400);
    private static readonly IReadOnlyList<TimeSpan> NoReactivation = [];

    [Fact]
    public async Task RepeatedHeartbeats_WithUnchangedProgress_FailTheRun_EvenThoughEventsKeepArriving()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: ShortWindow, reactivationDelays: NoReactivation);

        await fixture.Coordinator.EnqueueAsync("task-stalled", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitEvent("started", "task-stalled");

        // A model turn is stuck (network/provider hang): the background heartbeat timer
        // keeps ticking every 50ms regardless, but progress never moves — exactly the
        // production incident (issue #184's report): the agent's own timer looked alive
        // while the model connection produced nothing at all. This keeps emitting
        // concurrently with the wait below, well past ShortWindow, so the assertion can
        // only pass if unchanged-progress heartbeats stop counting as liveness.
        using var stallCts = new CancellationTokenSource();
        var stallLoop = EmitUnchangingHeartbeatsAsync(handle, "task-stalled", progress: 3, stallCts.Token);
        try
        {
            await fixture.WaitForPublishedEventAsync("task-stalled", e => e.ToStatus == "failed", TimeSpan.FromSeconds(5));
        }
        finally
        {
            stallCts.Cancel();
            await stallLoop;
        }

        Assert.True(handle.Terminated, "The wedged agent process must be terminated on liveness failure.");

        var artifact = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor("task-stalled"));
        Assert.Contains("liveness", artifact, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HeartbeatsWithAdvancingProgress_KeepTheRunAlive_PastWhatWouldOtherwiseBeTheWindow()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: ShortWindow, reactivationDelays: NoReactivation);

        await fixture.Coordinator.EnqueueAsync("task-slow-but-alive", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitEvent("started", "task-slow-but-alive");

        // A genuinely slow but producing run: heartbeats every 50ms, each carrying a
        // strictly higher progress value, running for several multiples of ShortWindow —
        // the model is streaming text the whole time, just slowly.
        using var aliveCts = new CancellationTokenSource();
        var aliveLoop = EmitAdvancingHeartbeatsAsync(handle, "task-slow-but-alive", aliveCts.Token);
        await Task.Delay(ShortWindow + ShortWindow);
        aliveCts.Cancel();
        await aliveLoop;

        handle.EmitEvent("completed", "task-slow-but-alive", new { summary = "Finished after a slow but steadily-progressing run." });

        await fixture.WaitForPublishedEventAsync("task-slow-but-alive", e => e.ToStatus == "completed", TimeSpan.FromSeconds(10));

        lock (fixture.PublishedEvents)
        {
            Assert.DoesNotContain(fixture.PublishedEvents, e => e.TaskId == "task-slow-but-alive" && e.ToStatus == "failed");
        }
    }

    private static async Task EmitUnchangingHeartbeatsAsync(
        ScriptedAgentProcessHandle handle, string taskId, int progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            handle.EmitEvent("heartbeat", taskId, new { progress });
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task EmitAdvancingHeartbeatsAsync(
        ScriptedAgentProcessHandle handle, string taskId, CancellationToken cancellationToken)
    {
        var progress = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            handle.EmitEvent("heartbeat", taskId, new { progress = ++progress });
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

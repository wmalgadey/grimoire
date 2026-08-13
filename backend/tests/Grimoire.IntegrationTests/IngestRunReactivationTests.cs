using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.OperationalState;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T012 (023-task-ui-improvements, US2 / FR-007, FR-008, SC-005, ADR-025): liveness silence
/// is no longer immediately terminal. It is recorded as a distinct history entry and followed
/// by bounded automatic reactivation on an increasing backoff; only after the attempts are
/// exhausted does the run take the existing final-failure path.
///
/// Deterministic throughout (ADR-021): backoff runs on a <see cref="FakeTimeProvider"/>, the
/// agent process is the existing hand-rolled port fake in go-silent mode, and every assertion
/// reads state back out — history rows, queue state, published events (Principle II).
/// </summary>
public class IngestRunReactivationTests
{
    private static readonly TimeSpan LivenessWindow = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset Epoch = new(2026, 8, 13, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LivenessExpiry_RecordsInterruption_WithoutFailingTheTask_AndHoldsTheRunSlot()
    {
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = int.MaxValue };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);

        await fixture.Coordinator.EnqueueAsync("task-silent", Path.Combine(fixture.Root, "src.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-next", Path.Combine(fixture.Root, "src2.md"), null);
        await WaitForLaunchesAsync(launcher, 1);

        time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
        await WaitForHistoryAsync(fixture, "task-silent", h => h.Any(e => e.Status == IngestHistoryStatuses.LivenessInterrupted));

        var history = await fixture.Repository.GetStatusHistoryAsync("task-silent");
        var interruption = Assert.Single(history, e => e.Status == IngestHistoryStatuses.LivenessInterrupted);
        Assert.Contains("attempt 1", interruption.Detail);

        // FR-007: not a failure — and not an opaque one either.
        Assert.DoesNotContain(history, e => e.Status == "failed");
        lock (fixture.PublishedEvents)
        {
            Assert.DoesNotContain(fixture.PublishedEvents, e => e.TaskId == "task-silent" && e.ToStatus == "failed");
        }

        // The run slot stays held: the queue neither advances nor reorders while a
        // reactivation is pending (ADR-008's single-slot FIFO model is undisturbed).
        Assert.Equal("task-silent", fixture.Coordinator.RunningTaskId);
        Assert.Equal(["task-next"], (await fixture.Repository.GetQueuedAsync()).Select(q => q.TaskId));
        Assert.Single(launcher.Handles);

        // The interrupted process is terminated rather than left running (FR-007).
        Assert.True(launcher.Handles[0].Terminated);

        // Observability contract: WARN with task_id, attempt, next_delay_seconds.
        var entry = Assert.Single(fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.liveness_interrupted");
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("task-silent", entry.Fields["task_id"]);
        Assert.Equal(1, entry.Fields["attempt"]);
        Assert.Equal(10L, entry.Fields["next_delay_seconds"]);
    }

    [Fact]
    public async Task Backoff_RelaunchesOnTheIncreasingSchedule_RecordingReactivatedAndRunning()
    {
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = int.MaxValue };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);

        await fixture.Coordinator.EnqueueAsync("task-backoff", Path.Combine(fixture.Root, "src.md"), null);
        await WaitForLaunchesAsync(launcher, 1);

        foreach (var (attempt, delay) in new[] { (1, 10), (2, 30), (3, 90) })
        {
            time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
            await WaitForHistoryAsync(fixture, "task-backoff",
                h => h.Count(e => e.Status == IngestHistoryStatuses.LivenessInterrupted) == attempt);

            // One second short of the scheduled delay: still no re-launch.
            time.Advance(TimeSpan.FromSeconds(delay - 1));
            Assert.Equal(attempt, launcher.Handles.Count);

            time.Advance(TimeSpan.FromSeconds(1));
            await WaitForLaunchesAsync(launcher, attempt + 1);

            await WaitForHistoryAsync(fixture, "task-backoff",
                h => h.Count(e => e.Status == IngestHistoryStatuses.Reactivated) == attempt);

            var reactivationEntry = (await fixture.Repository.GetStatusHistoryAsync("task-backoff"))
                .Last(e => e.Status == IngestHistoryStatuses.Reactivated);
            Assert.Contains($"attempt {attempt}", reactivationEntry.Detail);
        }

        var history = await fixture.Repository.GetStatusHistoryAsync("task-backoff");
        // Every reactivation is followed by a `running` entry: the path reads as a loop.
        var statuses = history.Select(e => e.Status).ToList();
        for (var i = 0; i < statuses.Count - 1; i++)
        {
            if (statuses[i] == IngestHistoryStatuses.Reactivated)
            {
                Assert.Equal("running", statuses[i + 1]);
            }
        }

        Assert.Equal(3, statuses.Count(s => s == IngestHistoryStatuses.Reactivated));
        Assert.DoesNotContain("failed", statuses);
    }

    [Fact]
    public async Task FourthSilence_ExhaustsAttempts_FailsFinally_WritesArtifact_AndAdvancesTheQueue()
    {
        var time = new FakeTimeProvider(Epoch);
        var launcher = new FakeAgentProcessLauncher(autoPlay: false) { GoSilentIngestLaunches = 4 };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);

        await fixture.Coordinator.EnqueueAsync("task-exhausted", Path.Combine(fixture.Root, "src.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-after", Path.Combine(fixture.Root, "src2.md"), null);
        await WaitForLaunchesAsync(launcher, 1);

        await DriveToExhaustionAsync(fixture, launcher, time, "task-exhausted");

        var history = await fixture.Repository.GetStatusHistoryAsync("task-exhausted");
        Assert.Equal("failed", history[^1].Status);
        Assert.Equal(3, history.Count(e => e.Status == IngestHistoryStatuses.Reactivated));
        // SC-005: every interruption is recorded — including the last one, which is exactly
        // the transition that would otherwise read as an unexplained jump to `failed`.
        Assert.Equal(4, history.Count(e => e.Status == IngestHistoryStatuses.LivenessInterrupted));
        Assert.Contains(
            "no attempts remaining",
            history.Last(e => e.Status == IngestHistoryStatuses.LivenessInterrupted).Detail);

        // The Hub writes the failure artifact itself (the agent never got to).
        var artifact = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor("task-exhausted"));
        Assert.Contains("status: failed", artifact, StringComparison.Ordinal);
        Assert.Contains("liveness", artifact, StringComparison.OrdinalIgnoreCase);

        // The pre-existing liveness_failed event keeps its meaning: emitted only at final failure.
        var livenessFailed = Assert.Single(
            fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.liveness_failed");
        Assert.Equal(LogLevel.Error, livenessFailed.Level);

        var exhausted = Assert.Single(
            fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.reactivation_exhausted");
        Assert.Equal(LogLevel.Error, exhausted.Level);
        Assert.Equal("task-exhausted", exhausted.Fields["task_id"]);
        Assert.Equal(3, exhausted.Fields["attempts"]);

        // Only now does the queue advance to the next task.
        await fixture.WaitForPublishedEventAsync("task-after", e => e.ToStatus == "running");
        Assert.Equal("task-after", fixture.Coordinator.RunningTaskId);
    }

    [Fact]
    public async Task ReactivatedRun_ThatCompletes_EndsTheLoop_AndRecordsCompletion()
    {
        var time = new FakeTimeProvider(Epoch);
        // Silent once, then a normal run on the first reactivation.
        var launcher = new FakeAgentProcessLauncher(autoPlay: true) { GoSilentIngestLaunches = 1 };
        using var fixture = new IngestSubmissionPipelineFixture(
            launcher: launcher, livenessWindow: LivenessWindow, timeProvider: time);

        await fixture.Coordinator.EnqueueAsync("task-recovers", Path.Combine(fixture.Root, "src.md"), null);
        await WaitForLaunchesAsync(launcher, 1);

        time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
        await WaitForHistoryAsync(fixture, "task-recovers",
            h => h.Any(e => e.Status == IngestHistoryStatuses.LivenessInterrupted));

        time.Advance(TimeSpan.FromSeconds(10));
        await fixture.WaitForPublishedEventAsync("task-recovers", e => e.ToStatus == "completed");

        var statuses = (await fixture.Repository.GetStatusHistoryAsync("task-recovers")).Select(e => e.Status).ToList();
        Assert.Equal("completed", statuses[^1]);
        Assert.DoesNotContain("failed", statuses);
        Assert.Null(fixture.Coordinator.RunningTaskId);
    }

    /// <summary>
    /// Drives one task through all three reactivations and the final, exhausting silence.
    /// Shared by the exhaustion test and the observability contract tests (T016/T018).
    /// </summary>
    internal static async Task DriveToExhaustionAsync(
        IngestSubmissionPipelineFixture fixture, FakeAgentProcessLauncher launcher, FakeTimeProvider time, string taskId)
    {
        foreach (var (attempt, delay) in new[] { (1, 10), (2, 30), (3, 90) })
        {
            time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
            await WaitForHistoryAsync(fixture, taskId,
                h => h.Count(e => e.Status == IngestHistoryStatuses.LivenessInterrupted) == attempt);

            time.Advance(TimeSpan.FromSeconds(delay));
            await WaitForLaunchesAsync(launcher, attempt + 1);
        }

        time.Advance(LivenessWindow + TimeSpan.FromSeconds(1));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed", TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Waits until <paramref name="expected"/> agent launches exist AND supervision has
    /// attached to each of them. Both halves matter: the launch alone does not arm the
    /// liveness watchdog, and advancing virtual time before it is armed would advance past
    /// a timer that does not exist yet.
    /// </summary>
    internal static Task WaitForLaunchesAsync(FakeAgentProcessLauncher launcher, int expected) =>
        PollAsync.WaitAsync(
            () =>
            {
                lock (launcher.Handles)
                {
                    return launcher.Handles.Count >= expected
                        && launcher.Handles.Take(expected).All(h => h.ReadLoopAttached);
                }
            },
            TimeSpan.FromSeconds(15),
            () => $"Expected {expected} supervised agent launches, saw {launcher.Handles.Count}.");

    internal static Task WaitForHistoryAsync(
        IngestSubmissionPipelineFixture fixture, string taskId,
        Func<IReadOnlyList<IngestStatusHistoryEntry>, bool> predicate)
    {
        var lastSeen = Array.Empty<string>();
        return PollAsync.WaitAsync(
            async () =>
            {
                var history = await fixture.Repository.GetStatusHistoryAsync(taskId);
                lastSeen = [.. history.Select(e => e.Status)];
                return predicate(history);
            },
            TimeSpan.FromSeconds(15),
            () => $"Status history for '{taskId}' never matched. Saw: {string.Join(" -> ", lastSeen)}");
    }
}

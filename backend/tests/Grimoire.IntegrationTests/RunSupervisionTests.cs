using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T034 (US4) — supervision state machine (ADR-008, FR-020/FR-022, SC-009): event
/// silence beyond the liveness window is the sole failure authority; terminal events
/// end supervision; late and malformed input never breaks a run.
/// </summary>
public class RunSupervisionTests
{
    private static readonly TimeSpan ShortWindow = TimeSpan.FromMilliseconds(400);

    [Fact]
    public async Task EventSilence_BeyondLivenessWindow_FailsRun_TerminatesProcess_AndAdvancesQueue()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: ShortWindow);

        await fixture.Coordinator.EnqueueAsync("task-silent", Path.Combine(fixture.Root, "src.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-next", Path.Combine(fixture.Root, "src2.md"), null);

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", "task-silent");
        // ... then silence: no heartbeat, no terminal event.

        await fixture.WaitForPublishedEventAsync("task-silent", e => e.ToStatus == "failed", TimeSpan.FromSeconds(10));

        Assert.True(handle.Terminated, "The leftover agent process must be terminated on liveness failure.");

        // Hub-written failure artifact carries a liveness reason (FR-020).
        var artifact = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor("task-silent"));
        Assert.Contains("status: failed", artifact, StringComparison.Ordinal);
        Assert.Contains("liveness", artifact, StringComparison.OrdinalIgnoreCase);

        // The queue advances automatically to the next task (FR-019).
        await fixture.WaitForPublishedEventAsync("task-next", e => e.ToStatus == "running", TimeSpan.FromSeconds(10));
        Assert.Equal(2, launcher.Handles.Count);

        // Observability contract: ingest.run.liveness_failed (ERROR) with mandatory fields.
        var livenessEntry = Assert.Single(
            fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.liveness_failed");
        Assert.Equal(LogLevel.Error, livenessEntry.Level);
        Assert.Equal("task-silent", livenessEntry.Fields["task_id"]);
        Assert.True(livenessEntry.Fields.ContainsKey("seconds_since_last_event"));
        Assert.True(livenessEntry.Fields.ContainsKey("liveness_window_seconds"));
    }

    [Fact]
    public async Task TerminalCompletedEvent_EndsSupervision_AndPublishesCompleted()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: TimeSpan.FromSeconds(30));

        await fixture.Coordinator.EnqueueAsync("task-ok", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitEvent("started", "task-ok");
        handle.EmitEvent("completed", "task-ok", new { summary = "All done." });

        await fixture.WaitForPublishedEventAsync("task-ok", e => e.ToStatus == "completed", TimeSpan.FromSeconds(10));
        Assert.Null(fixture.Coordinator.RunningTaskId);
    }

    [Fact]
    public async Task TerminalFailedEvent_PublishesFailure_WithReasonFromEvent()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: TimeSpan.FromSeconds(30));

        await fixture.Coordinator.EnqueueAsync("task-fail", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitEvent("started", "task-fail");
        handle.EmitEvent("failed", "task-fail", new { reason = "Turn cap of 50 exceeded. Rolled back." });

        await fixture.WaitForPublishedEventAsync(
            "task-fail",
            e => e.ToStatus == "failed" && e.FailureReason == "Turn cap of 50 exceeded. Rolled back.",
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LateEvents_AfterTerminalState_AreRecorded_ButChangeNothing()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: TimeSpan.FromSeconds(30));

        await fixture.Coordinator.EnqueueAsync("task-late", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitEvent("started", "task-late");
        handle.EmitEvent("completed", "task-late", new { summary = "Done." });
        await fixture.WaitForPublishedEventAsync("task-late", e => e.ToStatus == "completed", TimeSpan.FromSeconds(10));

        // Late activity after the terminal state (FR-022): diagnostic log, no snapshot,
        // no realtime publish, no state change.
        handle.EmitEvent("activity", "task-late", new { modelTurns = 9, toolCalls = 9, currentAction = "model_turn" });

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline &&
               !fixture.CoordinatorLogger.Entries.Any(e => e.EventName == "ingest.run.late_event"))
        {
            await Task.Delay(25);
        }

        var lateEntry = Assert.Single(fixture.CoordinatorLogger.Entries, e => e.EventName == "ingest.run.late_event");
        Assert.Equal(LogLevel.Warning, lateEntry.Level);
        Assert.Equal("task-late", lateEntry.Fields["task_id"]);
        Assert.Equal("activity", lateEntry.Fields["event_type"]);

        Assert.Null(fixture.Coordinator.GetActivity("task-late"));
        lock (fixture.PublishedActivity)
        {
            Assert.DoesNotContain(fixture.PublishedActivity, a => a.Method == "runActivityChanged");
        }
    }

    [Fact]
    public async Task MalformedStdoutLines_AreSkipped_AndNeverFailTheRun()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: TimeSpan.FromSeconds(30));

        await fixture.Coordinator.EnqueueAsync("task-noise", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitLine("Determining projects to restore...");
        handle.EmitLine("{not valid json");
        handle.EmitLine("{\"type\":\"unknown-event\",\"taskId\":\"task-noise\"}");
        handle.EmitEvent("started", "task-noise");
        handle.EmitEvent("completed", "task-noise", new { summary = "Done despite noise." });

        await fixture.WaitForPublishedEventAsync("task-noise", e => e.ToStatus == "completed", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PipeCloseWithoutTerminalEvent_DoesNotTransition_UntilLivenessWindowFires()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: ShortWindow);

        await fixture.Coordinator.EnqueueAsync("task-crash", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);

        handle.EmitEvent("started", "task-crash");
        // Hard crash: the stdout pipe closes without a terminal event.
        handle.ClosePipe();

        // Per ADR-008 the pipe close itself is not a transition — the liveness window is.
        await fixture.WaitForPublishedEventAsync("task-crash", e => e.ToStatus == "failed", TimeSpan.FromSeconds(10));

        var artifact = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor("task-crash"));
        Assert.Contains("liveness", artifact, StringComparison.OrdinalIgnoreCase);
    }
}

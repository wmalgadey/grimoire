using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.TaskArtifact;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T035 (US4) — persistent FIFO run queue (ADR-008, FR-019/FR-021, SC-008/SC-010):
/// exactly one agent at a time, FIFO auto-advance, restart pauses the queue until an
/// explicit resume/re-trigger, re-trigger of a non-queued task is refused.
/// </summary>
public class RunQueueTests
{
    [Fact]
    public async Task ThreeEnqueues_RunOneAtATime_InFifoOrder_WithAutoAdvance()
    {
        var launcher = new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(150));
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);

        await fixture.Coordinator.EnqueueAsync("task-a", Path.Combine(fixture.Root, "a.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-b", Path.Combine(fixture.Root, "b.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-c", Path.Combine(fixture.Root, "c.md"), null);

        // While task-a runs, b and c hold FIFO positions 1 and 2.
        var positions = await fixture.Coordinator.GetQueuePositionsAsync();
        if (positions.Count == 2)
        {
            Assert.Equal(1, positions["task-b"]);
            Assert.Equal(2, positions["task-c"]);
        }

        await fixture.WaitForPublishedEventAsync("task-c", e => e.ToStatus == "completed", TimeSpan.FromSeconds(15));

        // FIFO start order and single-slot execution (SC-008): windows never overlap.
        Assert.Equal(["task-a", "task-b", "task-c"], launcher.Requests.Select(r => r.TaskId).ToArray());
        var windows = launcher.RunWindows.OrderBy(w => w.Started).ToList();
        Assert.Equal(3, windows.Count);
        for (var i = 1; i < windows.Count; i++)
        {
            Assert.True(windows[i].Started >= windows[i - 1].Finished,
                $"Run {i} started before run {i - 1} finished — two agents ran concurrently.");
        }

        Assert.Empty(await fixture.Coordinator.GetQueuePositionsAsync());
    }

    [Fact]
    public async Task RestartWithQueuedTasks_PausesQueue_UntilExplicitResume()
    {
        var launcher = new FakeAgentProcessLauncher();
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);

        // Simulate tasks accepted just before a Hub shutdown: rows exist, nothing started.
        await fixture.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await fixture.Coordinator.EnqueueAsync("task-r1", Path.Combine(fixture.Root, "r1.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-r2", Path.Combine(fixture.Root, "r2.md"), null);
        Assert.Empty(launcher.Handles);

        // "Restart": a fresh coordinator over the same persistent store (SC-010).
        var restartedLauncher = new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(50));
        var restarted = new IngestRunCoordinator(
            fixture.Repository,
            restartedLauncher,
            fixture.Publisher,
            new HubTaskArtifactWriter(),
            fixture.ContentPaths);
        await restarted.InitializeAsync();

        // Queue survived and is paused; nothing starts automatically (FR-021).
        Assert.True(await restarted.IsQueuePausedAsync());
        var positions = await restarted.GetQueuePositionsAsync();
        Assert.Equal(1, positions["task-r1"]);
        Assert.Equal(2, positions["task-r2"]);
        await Task.Delay(200);
        Assert.Empty(restartedLauncher.Requests);

        // Explicit whole-queue resume re-arms FIFO processing.
        await restarted.ResumeAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && restartedLauncher.RunWindows.Count < 2)
        {
            await Task.Delay(25);
        }

        Assert.Equal(["task-r1", "task-r2"], restartedLauncher.Requests.Select(r => r.TaskId).ToArray());
        Assert.False(await restarted.IsQueuePausedAsync());
    }

    [Fact]
    public async Task RetriggerSingleTask_AfterRestart_ReArmsProcessing_WithoutQueueJumping()
    {
        var launcher = new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(50));
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);

        await fixture.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await fixture.Coordinator.EnqueueAsync("task-p1", Path.Combine(fixture.Root, "p1.md"), null);
        await fixture.Coordinator.EnqueueAsync("task-p2", Path.Combine(fixture.Root, "p2.md"), null);
        Assert.Empty(launcher.Requests);

        // Re-trigger the SECOND task: processing resumes, but FIFO order is preserved —
        // task-p1 still starts first (spec edge case: no queue jumping).
        var retriggered = await fixture.Coordinator.RetriggerAsync("task-p2");
        Assert.True(retriggered);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && launcher.RunWindows.Count < 2)
        {
            await Task.Delay(25);
        }

        Assert.Equal(["task-p1", "task-p2"], launcher.Requests.Select(r => r.TaskId).ToArray());
    }

    [Fact]
    public async Task Retrigger_OfNonQueuedTask_IsRefused()
    {
        var launcher = new FakeAgentProcessLauncher();
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);

        Assert.False(await fixture.Coordinator.RetriggerAsync("never-queued"));
    }
}

using Grimoire.Hub.IngestDispatch;
using Grimoire.IntegrationTests.Fakes;
using Xunit;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #146: the queue-drained signal <c>ingest-resume</c> supervises on must not become true
/// before every task it covers has actually run.
///
/// <para>
/// <c>IngestRunCoordinator.TryStartNextAsync</c> removes the next task's queued row and
/// claims the agent slot inside one lock. A caller that reads <c>RunningTaskId</c> and the
/// queue as two separate observations can interleave between them and see neither — the
/// slot still free because the previous run released it, and the row already gone because
/// the next run claimed it — which reads as a drained queue at the exact moment the next
/// task is starting. The CLI then tallied a task that had not run as failed, reporting a
/// clean two-task run as "1 processed, 1 failed" and reddening
/// <c>Deterministic Backend Gates</c> on pull requests touching nothing nearby.
/// </para>
/// </summary>
[Trait("TimingDependent", "true")]
public sealed class IngestQueueDrainSignalTests
{
    /// <summary>
    /// The invariant the tally depends on: whenever the coordinator reports the queue
    /// drained, every task that was queued has run to a terminal artifact.
    ///
    /// <para>
    /// The handoff window this guards is shorter than any sleep worth writing — polling it
    /// even at 1ms misses the interleaving entirely — so the observer samples as tightly as
    /// it can, on <b>its own thread</b> rather than from the thread pool. That distinction
    /// is not incidental: an unthrottled pool-based poll starves whatever xUnit is running
    /// in parallel and makes neighbouring timing-sensitive tests fail, which is a
    /// self-inflicted flake rather than a finding.
    /// </para>
    ///
    /// <para>
    /// Hence an explicit <see cref="Thread"/> blocking on each probe, and not
    /// <c>Task.Factory.StartNew(async …, TaskCreationOptions.LongRunning)</c>: with an
    /// async delegate only the code before the first <c>await</c> runs on the dedicated
    /// thread, and every continuation after it lands back on the pool — which is the very
    /// thing being avoided. The test still awaits rather than <c>Join</c>s, so waiting for
    /// the poller does not tie up a pool thread either.
    /// </para>
    ///
    /// <para>
    /// Being a race, this detects a regression probabilistically rather than on every run —
    /// it caught the pre-fix predicate on every probe attempt, but one green run of this
    /// test alone is not proof the ordering holds. Its job is to make an unnoticed
    /// reintroduction fail loudly and often in CI, not to stand in for the reasoning
    /// documented on <c>IsQueueDrainedAsync</c> itself.
    /// </para>
    /// </summary>
    [Fact]
    public async Task QueueDrainSignal_IsNeverReportedWhileAnIngestTaskIsStillBeingHandedOff()
    {
        // Zero simulated run duration: the handoff between runs is the window under test,
        // so back-to-back runs exercise it as hard as possible.
        using var harness = HubCliIngestTestHarness.Create(new FakeAgentProcessLauncher());
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);

        var taskIds = new[]
        {
            "2026-08-21-ingest-drainrace-a",
            "2026-08-21-ingest-drainrace-b",
            "2026-08-21-ingest-drainrace-c",
        };
        foreach (var taskId in taskIds)
        {
            await harness.EnqueueAsync(taskId);
        }

        await harness.Coordinator.ResumeAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var drained = new TaskCompletionSource();
        var poller = new Thread(() =>
        {
            try
            {
                while (!harness.Coordinator.IsQueueDrainedAsync(timeout.Token).GetAwaiter().GetResult())
                {
                    timeout.Token.ThrowIfCancellationRequested();
                }

                drained.SetResult();
            }
            catch (Exception ex)
            {
                drained.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "ingest-drain-signal-poller",
        };

        poller.Start();
        await drained.Task;

        // The launcher only records a request when a run actually started, so a task
        // missing here is one the drain signal ran ahead of.
        Assert.Equal(
            taskIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            harness.Launcher.Requests.Select(r => r.TaskId).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        // And the artifact the tally reads is terminal for all of them — the count
        // IngestResumeCommand prints is derived from exactly this.
        foreach (var taskId in taskIds)
        {
            var projection = await harness.Store.GetByTaskIdAsync(harness.ContentPaths.TasksDir, taskId);
            Assert.NotNull(projection);
            Assert.Equal("completed", projection!.Column);
        }
    }
}

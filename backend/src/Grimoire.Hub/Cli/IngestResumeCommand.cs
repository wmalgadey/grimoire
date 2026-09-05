using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Settings for <see cref="IngestResumeCommand"/> (018-hub-cli-commands T029,
/// contracts/cli-commands.md "ingest-resume"): no options beyond the inherited ADR-009
/// path switches.
/// </summary>
public sealed class IngestResumeSettings : HubPathSettings
{
}

/// <summary>
/// Resumes the ingest queue via <see cref="IngestRunCoordinator.ResumeAsync"/> — the same
/// in-process call the HTTP <c>/resume</c> endpoint makes
/// (<c>IngestSubmissionEndpoints.PostResumeAsync</c>) — and supervises every task queued
/// or running at the moment of resume until the whole queue drains (018-hub-cli-commands
/// T029, contracts/cli-commands.md "ingest-resume", FR-005/SC-005).
///
/// Idempotent and always exits 0 (per-task failures are queue state, not a CLI operation
/// failure — the contract lists them on stderr as part of the queue's own outcome, not as
/// the command's own outcome the way <c>ingest-retrigger</c>'s single-task exit code
/// does). The set of tasks tracked for the final processed/failed tally is captured
/// BEFORE <see cref="IngestRunCoordinator.ResumeAsync"/> runs — the currently queued task
/// ids plus (idempotency case) any task already running — so a task enqueued by some
/// other actor after this command starts does not skew the tally it reports, even though
/// the drain-wait below still waits for the queue to be fully idle either way.
/// </summary>
public sealed class IngestResumeCommand : AsyncCommand<IngestResumeSettings>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IngestRunCoordinator _coordinator;
    private readonly IngestKanbanBoardProjectionStore _store;
    private readonly IngestContentPaths _contentPaths;
    private readonly CliStatusRenderer _status;
    private readonly TextWriter _stdout;

    // See LintRunCommand's identical attribute for why this is required (018-hub-cli-commands
    // T036 quickstart validation finding): disambiguates ActivatorUtilities.CreateInstance
    // between this constructor and the test seam below.
    [ActivatorUtilitiesConstructor]
    public IngestResumeCommand(
        IngestRunCoordinator coordinator, IngestKanbanBoardProjectionStore store, IngestContentPaths contentPaths)
        : this(coordinator, store, contentPaths, new CliStatusRenderer(), Console.Out)
    {
    }

    /// <summary>Test seam: inject a status renderer / stdout writer instead of the real stderr/stdout streams.</summary>
    public IngestResumeCommand(
        IngestRunCoordinator coordinator, IngestKanbanBoardProjectionStore store, IngestContentPaths contentPaths,
        CliStatusRenderer status, TextWriter stdout)
    {
        _coordinator = coordinator;
        _store = store;
        _contentPaths = contentPaths;
        _status = status;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, IngestResumeSettings settings, CancellationToken cancellationToken)
    {
        var trackedTaskIds = (await _coordinator.GetQueuePositionsAsync(cancellationToken)).Keys.ToHashSet();
        if (_coordinator.RunningTaskId is { } runningTaskId)
        {
            trackedTaskIds.Add(runningTaskId);
        }

        var queuedTasks = await _coordinator.ResumeAsync(cancellationToken);
        _status.WriteLine($"Ingest queue resumed: {queuedTasks} task(s) queued.");

        await WaitForQueueDrainAsync(cancellationToken);

        var (processed, failed) = await TallyOutcomesAsync(trackedTaskIds, cancellationToken);

        _stdout.WriteLine($"Ingest queue drained: {processed} task(s) processed, {failed} failed.");
        return (int)CliExitCode.Success;
    }

    /// <summary>
    /// Polls <see cref="IngestRunCoordinator.IsQueueDrainedAsync"/> until the queue is
    /// idle — the whole-queue-drained signal this command supervises, as opposed to
    /// <c>ingest-retrigger</c>'s single-task terminal wait.
    ///
    /// <para>
    /// The coordinator evaluates both halves of that question under its own slot lock, and
    /// that is load-bearing rather than tidiness: reading <c>RunningTaskId</c> and the
    /// queue separately from here let the two observations interleave with the handoff
    /// between one run and the next, so the command could report the queue drained while
    /// the last task was still starting (#146).
    /// </para>
    /// </summary>
    private async Task WaitForQueueDrainAsync(CancellationToken cancellationToken)
    {
        while (!await _coordinator.IsQueueDrainedAsync(cancellationToken))
        {
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Reads each tracked task's final Task Artifact-backed column to produce the
    /// processed/failed counts contracts/cli-commands.md "ingest-resume" requires. A task
    /// whose projection is unreadable or not "completed" counts as failed.
    ///
    /// <para>
    /// That branch is reachable, and describing it as unreachable is what made #146 look
    /// intentional. The drain wait above proves every tracked task left the running/queued
    /// state; it does not prove the Hub finished its own terminal bookkeeping. On the
    /// success path the agent writes its `completed` artifact before emitting the terminal
    /// event, so the projection is durable well before the slot is released — but a run the
    /// Hub itself failed (liveness exhaustion) has its failure artifact written after the
    /// release, so its projection may still read `running` here. It counts as failed
    /// either way, which is the right outcome; the count does not depend on winning that
    /// race, and no other tracked task can be mid-flight (IsQueueDrainedAsync).
    /// </para>
    /// </summary>
    private async Task<(int Processed, int Failed)> TallyOutcomesAsync(
        IReadOnlySet<string> trackedTaskIds, CancellationToken cancellationToken)
    {
        var processed = 0;
        var failed = 0;

        foreach (var taskId in trackedTaskIds)
        {
            var projection = await _store.GetByTaskIdAsync(_contentPaths.TasksDir, taskId, cancellationToken);
            if (projection?.Column == "completed")
            {
                processed++;
            }
            else
            {
                failed++;
            }
        }

        return (processed, failed);
    }
}

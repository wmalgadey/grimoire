using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Settings for <see cref="IngestRetriggerCommand"/> (018-hub-cli-commands T028,
/// contracts/cli-commands.md "ingest-retrigger"): a single required, non-empty
/// <c>--task-id</c>. Mirrors <see cref="RemediationTaskSettings"/>'s
/// <c>isRequired: true</c> + <see cref="Validate"/> combo (FR-009) — Spectre performs
/// this check before <c>ExecuteAsync</c> runs, so a missing/blank value never contacts
/// the store.
/// </summary>
public sealed class IngestRetriggerSettings : HubPathSettings
{
    [CommandOption("--task-id <ID>", isRequired: true)]
    public string? TaskId { get; set; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(TaskId)
            ? ValidationResult.Error("--task-id is required and must not be empty.")
            : ValidationResult.Success();
}

/// <summary>
/// Re-arms a queued ingest task via <see cref="IngestRunCoordinator.RetriggerAsync"/> —
/// the same in-process call the HTTP <c>/retrigger</c> endpoint makes
/// (<c>IngestSubmissionEndpoints.PostRetriggerAsync</c>) — and supervises the retriggered
/// processing to its terminal state (018-hub-cli-commands T028,
/// contracts/cli-commands.md "ingest-retrigger", FR-005/SC-005).
///
/// Mirrors the HTTP handler's exact two-step flow: a <see cref="KanbanBoardProjectionStore"/>
/// lookup FIRST for the not-found case (the projection is the only place a task's current
/// column is known before the coordinator is asked to retrigger it), THEN the coordinator
/// call for the not-in-queue conflict — which needs the column looked up in step one to
/// report it (<c>{column}</c> in the contract's conflict message).
///
/// Unlike the remediation transition commands' in-memory/repository-row terminal read
/// side, an ingest task's durable terminal status lives in its Task Artifact markdown
/// file (written by the agent subprocess itself, never by the Hub for a genuine
/// completed/failed outcome) — <see cref="KanbanBoardProjectionStore"/> is the read model
/// over that file, so it is what <see cref="WaitForTerminalAsync"/> polls, mirroring the
/// established polling idiom (<see cref="LintRunCommand.WaitForTerminalAsync"/> /
/// <see cref="RemediationAuthorizeCommand.WaitForTerminalAsync"/>) against a different
/// backing store.
/// </summary>
public sealed class IngestRetriggerCommand : AsyncCommand<IngestRetriggerSettings>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IngestRunCoordinator _coordinator;
    private readonly KanbanBoardProjectionStore _store;
    private readonly ContentRootPaths _contentPaths;
    private readonly CliStatusRenderer _status;
    private readonly TextWriter _stdout;

    // See LintRunCommand's identical attribute for why this is required (018-hub-cli-commands
    // T036 quickstart validation finding): disambiguates ActivatorUtilities.CreateInstance
    // between this constructor and the test seam below.
    [ActivatorUtilitiesConstructor]
    public IngestRetriggerCommand(
        IngestRunCoordinator coordinator, KanbanBoardProjectionStore store, ContentRootPaths contentPaths)
        : this(coordinator, store, contentPaths, new CliStatusRenderer(), Console.Out)
    {
    }

    /// <summary>Test seam: inject a status renderer / stdout writer instead of the real stderr/stdout streams.</summary>
    public IngestRetriggerCommand(
        IngestRunCoordinator coordinator, KanbanBoardProjectionStore store, ContentRootPaths contentPaths,
        CliStatusRenderer status, TextWriter stdout)
    {
        _coordinator = coordinator;
        _store = store;
        _contentPaths = contentPaths;
        _status = status;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, IngestRetriggerSettings settings, CancellationToken cancellationToken)
    {
        var taskId = settings.TaskId!;

        var projection = await _store.GetByTaskIdAsync(_contentPaths.TasksDir, taskId, cancellationToken);
        if (projection is null)
        {
            _stdout.WriteLine($"Task '{taskId}' was not found.");
            return (int)CliExitCode.NotFound;
        }

        var retriggered = await _coordinator.RetriggerAsync(taskId, cancellationToken);
        if (!retriggered)
        {
            _stdout.WriteLine($"Ingest task {taskId} is not in the queue ({projection.Column}).");
            return (int)CliExitCode.StateConflict;
        }

        _status.WriteLine($"Ingest task {taskId} retriggered.");

        var finalProjection = await WaitForTerminalAsync(taskId, cancellationToken);

        if (finalProjection.Column == "failed")
        {
            _stdout.WriteLine($"Ingest task {taskId} failed.");
            return (int)CliExitCode.OperationFailed;
        }

        _stdout.WriteLine($"Ingest task {taskId} completed.");
        return (int)CliExitCode.Success;
    }

    /// <summary>
    /// Polls the Task Artifact-backed projection for <paramref name="taskId"/> until it
    /// reaches a terminal column (<c>completed</c>/<c>failed</c>) — the durable read-side
    /// contract for an ingest task's outcome, mirroring the test suite's own
    /// <c>IngestSubmissionPipelineFixture.WaitForStatusAsync</c> polling idiom.
    /// </summary>
    private async Task<KanbanBoardProjection> WaitForTerminalAsync(string taskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var projection = await _store.GetByTaskIdAsync(_contentPaths.TasksDir, taskId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Ingest task {taskId} was retriggered but its task artifact is no longer readable.");

            if (projection.Column is "completed" or "failed")
            {
                return projection;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}

using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Authorizes a proposed remediation task via
/// <see cref="RemediationTaskTransitionService.AuthorizeAsync"/> — the same in-process
/// call the HTTP <c>/authorize</c> endpoint now makes (018-hub-cli-commands T023,
/// contracts/cli-commands.md "remediation-authorize", FR-005/SC-005) — including its
/// eager <see cref="RemediationRunCoordinator.TryStartNextAsync"/> dispatch kick.
///
/// Unlike dismiss/withdraw, authorize can start agent work: if the remediation execution
/// queue is not paused, the just-authorized task either starts executing immediately (if
/// the single execution slot is free) or waits its FIFO turn behind whatever is currently
/// executing — either way the CLI supervises <b>this task's own row</b> to a terminal
/// execution outcome before exiting (ADR-020's blocking model), printing the "authorized
/// at" line to stderr as status before the wait (mirrors <see cref="LintRunCommand"/>'s
/// unconditional pre-wait status line). If the queue <i>is</i> paused (fresh-process/
/// restart semantics, ADR-018), the transition itself is the whole outcome — the command
/// exits immediately after printing the same line to stdout, identical to the HTTP flow
/// in the same state.
/// </summary>
public sealed class RemediationAuthorizeCommand : AsyncCommand<RemediationTaskSettings>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly RemediationTaskTransitionService _service;
    private readonly RemediationRunCoordinator _coordinator;
    private readonly OperationalStateRepository _repository;
    private readonly CliStatusRenderer _status;
    private readonly TextWriter _stdout;

    public RemediationAuthorizeCommand(
        RemediationTaskTransitionService service, RemediationRunCoordinator coordinator, OperationalStateRepository repository)
        : this(service, coordinator, repository, new CliStatusRenderer(), Console.Out)
    {
    }

    /// <summary>Test seam: inject a status renderer / stdout writer instead of the real stderr/stdout streams.</summary>
    public RemediationAuthorizeCommand(
        RemediationTaskTransitionService service, RemediationRunCoordinator coordinator, OperationalStateRepository repository,
        CliStatusRenderer status, TextWriter stdout)
    {
        _service = service;
        _coordinator = coordinator;
        _repository = repository;
        _status = status;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, RemediationTaskSettings settings, CancellationToken cancellationToken)
    {
        var taskId = settings.TaskId!;
        var result = await _service.AuthorizeAsync(taskId, cancellationToken);

        switch (result)
        {
            case RemediationTransitionResult.NotFound:
                _stdout.WriteLine($"Remediation task '{taskId}' was not found.");
                return (int)CliExitCode.NotFound;

            case RemediationTransitionResult.Conflict conflict:
                _stdout.WriteLine($"Remediation task {taskId} is not proposed (current state: {conflict.CurrentState}).");
                return (int)CliExitCode.StateConflict;

            case RemediationTransitionResult.Ok ok:
                return await HandleAuthorizedAsync(ok, cancellationToken);

            default:
                throw new InvalidOperationException($"Unhandled {nameof(RemediationTransitionResult)}: {result.GetType()}.");
        }
    }

    private async Task<int> HandleAuthorizedAsync(RemediationTransitionResult.Ok ok, CancellationToken cancellationToken)
    {
        var statusLine = $"Remediation task {ok.TaskId} authorized at {ok.AuthorizedAt:O}.";

        if (await _coordinator.IsQueuePausedAsync(cancellationToken))
        {
            // Fresh-process/restart semantics (ADR-018): the queue starts paused, so no
            // execution will start on its own — the transition is the whole outcome,
            // identical to the HTTP flow in the same state.
            _stdout.WriteLine(statusLine);
            return (int)CliExitCode.Success;
        }

        _status.WriteLine(statusLine);

        var row = await WaitForTerminalAsync(ok.TaskId, cancellationToken);

        if (row.State == RemediationTaskStates.Failed)
        {
            _stdout.WriteLine($"Remediation task {ok.TaskId} failed: {row.OutcomeReason}");
            return (int)CliExitCode.OperationFailed;
        }

        var word = row.State == RemediationTaskStates.Completed ? "completed" : "not applicable";
        var reasonSuffix = string.IsNullOrEmpty(row.OutcomeReason) ? string.Empty : $": {row.OutcomeReason}";
        _stdout.WriteLine($"Remediation task {ok.TaskId} {word}{reasonSuffix}.");
        return (int)CliExitCode.Success;
    }

    /// <summary>
    /// Polls the persisted row for <paramref name="taskId"/> until it reaches one of the
    /// execution-terminal states (completed/failed/not_applicable) — the read-side
    /// contract for a task's execution outcome (no push/completion signal exists beyond
    /// the row itself), mirroring <see cref="LintRunCommand.WaitForTerminalAsync"/>'s
    /// polling idiom for the lint run's in-memory state.
    /// </summary>
    private async Task<RemediationTaskRow> WaitForTerminalAsync(string taskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var rows = await _repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
            var row = rows.FirstOrDefault(r => r.TaskId == taskId)
                ?? throw new InvalidOperationException(
                    $"Remediation task {taskId} was authorized but is no longer known to the repository.");

            if (row.State is RemediationTaskStates.Completed or RemediationTaskStates.Failed or RemediationTaskStates.NotApplicable)
            {
                return row;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}

using Grimoire.Hub.OperationalState;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// Result of a <see cref="RemediationTaskTransitionService"/> transition call
/// (018-hub-cli-commands T021, data-model.md "RemediationTaskTransitionService"): mirrors
/// the shape of the outcomes <c>RemediationTaskEndpoints</c>' authorize/dismiss/withdraw
/// handlers used to compute inline, so both the HTTP wrapper and the CLI commands can map
/// it to their own presentation (JSON body / stdout line + exit code) without
/// re-implementing the CAS/publish/metrics/log logic.
/// </summary>
public abstract record RemediationTransitionResult
{
    /// <summary>
    /// The task committed the requested transition.
    /// </summary>
    /// <param name="TaskId">The transitioned task's id.</param>
    /// <param name="NewState">The state the task committed to (<see cref="RemediationTaskStates"/>).</param>
    /// <param name="AuthorizedAt">
    /// The transition's own timestamp: <c>authorized_at</c> for Authorize, the dismiss
    /// timestamp for Dismiss (still the field the pre-extraction endpoint returned as
    /// <c>dismissedAt</c> — same value, endpoint-side field renamed), and
    /// <see langword="null"/> for Withdraw (its response carries no timestamp field,
    /// contracts/remediation-task-api.md).
    /// </param>
    /// <param name="QueuePosition">
    /// Authorize-only: the 1-based FIFO queue position captured immediately after the CAS
    /// commit — before the eager <see cref="RemediationRunCoordinator.TryStartNextAsync"/>
    /// dispatch check runs, exactly where the pre-extraction endpoint computed it, so a
    /// sole waiting task still reports position 1 even if eager dispatch immediately moves
    /// it out of the Authorized set. <see langword="null"/> for Dismiss/Withdraw.
    /// </param>
    public sealed record Ok(string TaskId, string NewState, DateTimeOffset? AuthorizedAt, int? QueuePosition = null)
        : RemediationTransitionResult;

    /// <summary>The referenced task id does not exist.</summary>
    public sealed record NotFound : RemediationTransitionResult;

    /// <summary>
    /// The task exists but is not in the state the requested transition requires.
    /// <paramref name="Reason"/> is one of <c>task_not_proposed</c>, <c>task_not_authorized</c>,
    /// <c>execution_already_started</c> (the existing reasons
    /// <c>RemediationTaskEndpoints</c> used pre-extraction).
    /// </summary>
    public sealed record Conflict(string Reason, string CurrentState) : RemediationTransitionResult;
}

/// <summary>
/// Extraction of the three human-permitted Remediation Action Task transitions
/// (018-hub-cli-commands T021, ADR-020): authorize (<c>proposed → authorized</c>), dismiss
/// (<c>proposed → dismissed</c>), and withdraw-authorization (<c>authorized → proposed</c>).
/// Moves <c>RemediationTaskEndpoints</c>' inline handler logic here <b>verbatim</b> — CAS
/// transition, lifecycle publish, metrics, log events, the dismiss outcome-record append,
/// and authorize's eager <see cref="RemediationRunCoordinator.TryStartNextAsync"/> dispatch
/// kick — so both the HTTP endpoint handlers and the CLI's
/// <c>RemediationAuthorizeCommand</c>/<c>RemediationDismissCommand</c>/
/// <c>RemediationWithdrawCommand</c> drive the exact same code (FR-005/SC-005).
///
/// <b>Deliberately does not, and must not, extend to <c>Authorized → Executing</c></b>: that
/// CAS stays exclusively inside <see cref="RemediationRunCoordinator.TryStartNextAsync"/>
/// (ADR-018) — this service only ever calls that method, never performs the Executing
/// transition itself. <c>Grimoire.ArchTests.RemediationExecutionDispatchRuleTests</c> proves
/// this service (like every other type in the namespace besides the two already
/// allow-listed coordinators) never references <c>IAgentProcessLauncher</c>.
/// </summary>
public sealed class RemediationTaskTransitionService
{
    private readonly OperationalStateRepository _repository;
    private readonly RemediationLifecyclePublisher _publisher;
    private readonly RemediationRunCoordinator _coordinator;
    private readonly RemediationTaskRecordStore _recordStore;
    private readonly ILogger<RemediationLifecyclePublisher> _logger;

    public RemediationTaskTransitionService(
        OperationalStateRepository repository,
        RemediationLifecyclePublisher publisher,
        RemediationRunCoordinator coordinator,
        RemediationTaskRecordStore recordStore,
        ILogger<RemediationLifecyclePublisher> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _coordinator = coordinator;
        _recordStore = recordStore;
        _logger = logger;
    }

    /// <summary>
    /// CAS <c>proposed → authorized</c>, stamping <c>authorized_at</c> (FIFO order
    /// authority, FR-017). Kicks the coordinator's own dispatch check afterward so an idle
    /// slot picks the task up immediately — the coordinator's CAS to <c>Executing</c> is the
    /// actual authorization gate (ADR-018); this method only ever grants eligibility.
    /// </summary>
    public async Task<RemediationTransitionResult> AuthorizeAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var row = await FindTaskAsync(taskId, cancellationToken);
        if (row is null)
        {
            return new RemediationTransitionResult.NotFound();
        }

        if (row.State != RemediationTaskStates.Proposed)
        {
            return TaskNotProposedConflict(row);
        }

        using var span = HubTracing.ActivitySource.StartActivity("hub.remediation.authorize");
        span?.SetTag("task_id", taskId);

        var authorizedAt = DateTimeOffset.UtcNow;
        var committed = await _repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Proposed, RemediationTaskStates.Authorized,
            outcomeReason: null, authorizedAt: authorizedAt, updatedAt: authorizedAt, cancellationToken);

        if (!committed)
        {
            // Lost the race — another transition (e.g. a concurrent dismiss) committed
            // first. Surface the actual current state, never silence (contract discipline).
            var current = await FindTaskAsync(taskId, cancellationToken);
            return current is null ? new RemediationTransitionResult.NotFound() : TaskNotProposedConflict(current);
        }

        HubMetrics.RecordRemediationTaskAuthorized();
        RemediationLifecycleLogEvents.LogTaskAuthorized(_logger, taskId);

        var rows = await _repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var queuePositions = RemediationTaskEndpoints.ComputeQueuePositions(rows);
        var queuePosition = queuePositions.TryGetValue(taskId, out var position) ? (int?)position : null;
        await _publisher.PublishTaskChangedAsync(
            taskId, row.RunId, fromState: RemediationTaskStates.Proposed, toState: RemediationTaskStates.Authorized,
            queuePosition: queuePosition, cancellationToken: cancellationToken);

        // Grants eligibility only — the coordinator's own CAS decides if/when it dispatches.
        await _coordinator.TryStartNextAsync(cancellationToken);

        return new RemediationTransitionResult.Ok(taskId, RemediationTaskStates.Authorized, authorizedAt, queuePosition);
    }

    /// <summary>
    /// CAS <c>proposed → dismissed</c>, terminal, no agent involvement, no wiki change.
    /// Appends the outcome entry to the task record (terminal transition, data-model.md).
    /// </summary>
    public async Task<RemediationTransitionResult> DismissAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var row = await FindTaskAsync(taskId, cancellationToken);
        if (row is null)
        {
            return new RemediationTransitionResult.NotFound();
        }

        if (row.State != RemediationTaskStates.Proposed)
        {
            return TaskNotProposedConflict(row);
        }

        var dismissedAt = DateTimeOffset.UtcNow;
        var committed = await _repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Proposed, RemediationTaskStates.Dismissed,
            outcomeReason: null, authorizedAt: null, updatedAt: dismissedAt, cancellationToken);

        if (!committed)
        {
            var current = await FindTaskAsync(taskId, cancellationToken);
            return current is null ? new RemediationTransitionResult.NotFound() : TaskNotProposedConflict(current);
        }

        try
        {
            await _recordStore.AppendOutcomeAsync(taskId, RemediationTaskStates.Dismissed, reason: null, dismissedAt, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append the dismissed outcome entry to remediation task record {TaskId}.", taskId);
        }

        HubMetrics.RecordRemediationTaskDismissed();
        RemediationLifecycleLogEvents.LogTaskDismissed(_logger, taskId);
        await _publisher.PublishTaskChangedAsync(
            taskId, row.RunId, fromState: RemediationTaskStates.Proposed, toState: RemediationTaskStates.Dismissed,
            cancellationToken: cancellationToken);

        return new RemediationTransitionResult.Ok(taskId, RemediationTaskStates.Dismissed, dismissedAt);
    }

    /// <summary>
    /// The human-initiated side of the withdrawal race (spec Edge Cases, data-model.md
    /// "Withdrawal race") — CAS <c>authorized → proposed</c>, clearing
    /// <c>authorized_at</c>. Competes directly against
    /// <see cref="RemediationRunCoordinator.TryStartNextAsync"/>'s <c>authorized →
    /// executing</c> CAS on the same persisted row; first commit wins, the loser sees the
    /// actual resulting state (research.md R5).
    /// </summary>
    public async Task<RemediationTransitionResult> WithdrawAuthorizationAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var row = await FindTaskAsync(taskId, cancellationToken);
        if (row is null)
        {
            return new RemediationTransitionResult.NotFound();
        }

        if (row.State != RemediationTaskStates.Authorized)
        {
            return WithdrawConflict(row);
        }

        var committed = await _repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Authorized, RemediationTaskStates.Proposed,
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow, cancellationToken);

        if (!committed)
        {
            // The race was lost — most likely the coordinator's CAS to Executing won.
            // Show the caller exactly what happened (contract: never silence).
            var current = await FindTaskAsync(taskId, cancellationToken);
            return current is null ? new RemediationTransitionResult.NotFound() : WithdrawConflict(current);
        }

        HubMetrics.RecordRemediationTaskWithdrawn();
        RemediationLifecycleLogEvents.LogAuthorizationWithdrawn(_logger, taskId);
        await _publisher.PublishTaskChangedAsync(
            taskId, row.RunId, fromState: RemediationTaskStates.Authorized, toState: RemediationTaskStates.Proposed,
            cancellationToken: cancellationToken);

        return new RemediationTransitionResult.Ok(taskId, RemediationTaskStates.Proposed, AuthorizedAt: null);
    }

    private async Task<RemediationTaskRow?> FindTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        var rows = await _repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        return rows.FirstOrDefault(r => r.TaskId == taskId);
    }

    private static RemediationTransitionResult.Conflict TaskNotProposedConflict(RemediationTaskRow row)
        => new("task_not_proposed", row.State);

    /// <summary>
    /// contracts/remediation-task-api.md withdraw-authorization error shapes: a task no
    /// longer <c>authorized</c> is either the CAS race loser against dispatch
    /// (<c>execution_already_started</c>, when the current state is <c>executing</c> or a
    /// terminal execution outcome) or a stale double-withdraw (<c>task_not_authorized</c>,
    /// when the current state is <c>proposed</c> or <c>dismissed</c>).
    /// </summary>
    private static RemediationTransitionResult.Conflict WithdrawConflict(RemediationTaskRow row)
    {
        if (row.State is RemediationTaskStates.Executing or RemediationTaskStates.Completed
            or RemediationTaskStates.Failed or RemediationTaskStates.NotApplicable)
        {
            return new("execution_already_started", row.State);
        }

        return new("task_not_authorized", row.State);
    }
}

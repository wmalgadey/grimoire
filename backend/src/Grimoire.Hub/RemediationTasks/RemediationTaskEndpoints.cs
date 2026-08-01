using Grimoire.Hub.OperationalState;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// HTTP endpoints for the Remediation Action Task workflow (015-lint-board-parity T024/
/// T033, contracts/remediation-task-api.md; mirrors <c>LintSubmissionEndpoints</c>'
/// Minimal-API route-group pattern). US3 shipped the read surface — list (the board's
/// initial-state recovery source for remediation entries) and detail (including
/// record-derived attached context, FR-011/FR-014). US4 (T033) adds the CAS-backed
/// authorize/dismiss/withdraw-authorization transitions; context/message join in US5
/// (T041). There is deliberately no execution endpoint — dispatch happens only via
/// <c>RemediationRunCoordinator.TryStartNextAsync</c> (ADR-018, SC-005): these endpoints
/// never reference <c>IAgentProcessLauncher</c>, only the coordinator's own
/// re-dispatch-check method.
/// </summary>
public static class RemediationTaskEndpoints
{
    public static RouteGroupBuilder MapRemediationTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{taskId}", GetDetailAsync);
        group.MapPost("/{taskId}/authorize", AuthorizeAsync);
        group.MapPost("/{taskId}/dismiss", DismissAsync);
        group.MapPost("/{taskId}/withdraw-authorization", WithdrawAuthorizationAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        OperationalStateRepository repository,
        string? runId,
        CancellationToken cancellationToken)
    {
        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var queuePositions = ComputeQueuePositions(rows);

        var tasks = rows
            .Where(row => runId is null || row.RunId == runId)
            .Select(row => ToListEntry(row, queuePositions))
            .ToList();

        return Results.Ok(new { tasks });
    }

    private static async Task<IResult> GetDetailAsync(
        string taskId,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        CancellationToken cancellationToken)
    {
        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var row = rows.FirstOrDefault(r => r.TaskId == taskId);
        if (row is null)
        {
            return Results.NotFound(new { message = $"Remediation task '{taskId}' was not found." });
        }

        // Record-derived history (FR-011/FR-014): attached context in append order,
        // readable in every state including terminal ones. A missing/unreadable record
        // yields an empty history, never a failed detail read — the SQLite row is the
        // state authority (data-model.md).
        var attachedContext = new List<object>();
        if (await recordStore.ReadAsync(taskId, cancellationToken) is RemediationTaskRecordParseResult.Parsed parsed)
        {
            foreach (var entry in parsed.Entries)
            {
                if (entry is RemediationTaskRecordEntry.Context context)
                {
                    attachedContext.Add(new { content = context.Text, attachedAt = context.AttachedAt });
                }
            }
        }

        var queuePositions = ComputeQueuePositions(rows);
        return Results.Ok(new
        {
            taskId = row.TaskId,
            runId = row.RunId,
            title = row.Title,
            description = row.Description,
            targetPath = row.TargetPath,
            state = row.State,
            proposedAt = row.ProposedAt,
            authorizedAt = row.AuthorizedAt,
            queuePosition = queuePositions.TryGetValue(row.TaskId, out var position) ? (int?)position : null,
            outcomeReason = row.OutcomeReason,
            updatedAt = row.UpdatedAt,
            attachedContext,
            // US5 (T041) introduces message turns; until then no turn can be active.
            messageTurnActive = false,
        });
    }

    /// <summary>
    /// POST /{taskId}/authorize (T033, FR-009): CAS <c>proposed → authorized</c>, stamping
    /// <c>authorized_at</c> (FIFO order authority, FR-017). Kicks the coordinator's own
    /// dispatch check afterward so an idle slot picks the task up immediately — the
    /// coordinator's CAS to <c>Executing</c> is the actual authorization gate (ADR-018);
    /// this endpoint only ever grants eligibility.
    /// </summary>
    private static async Task<IResult> AuthorizeAsync(
        string taskId,
        OperationalStateRepository repository,
        RemediationLifecyclePublisher publisher,
        RemediationRunCoordinator coordinator,
        ILogger<RemediationLifecyclePublisher> logger,
        CancellationToken cancellationToken)
    {
        var row = await FindTaskAsync(repository, taskId, cancellationToken);
        if (row is null)
        {
            return NotFound(taskId);
        }

        if (row.State != RemediationTaskStates.Proposed)
        {
            return TaskNotProposedConflict(row);
        }

        using var span = HubTracing.ActivitySource.StartActivity("hub.remediation.authorize");
        span?.SetTag("task_id", taskId);

        var authorizedAt = DateTimeOffset.UtcNow;
        var committed = await repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Proposed, RemediationTaskStates.Authorized,
            outcomeReason: null, authorizedAt: authorizedAt, updatedAt: authorizedAt, cancellationToken);

        if (!committed)
        {
            // Lost the race — another transition (e.g. a concurrent dismiss) committed
            // first. Surface the actual current state, never silence (contract discipline).
            var current = await FindTaskAsync(repository, taskId, cancellationToken);
            return current is null ? NotFound(taskId) : TaskNotProposedConflict(current);
        }

        HubMetrics.RecordRemediationTaskAuthorized();
        RemediationLifecycleLogEvents.LogTaskAuthorized(logger, taskId);

        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var queuePositions = ComputeQueuePositions(rows);
        await publisher.PublishTaskChangedAsync(
            taskId, row.RunId, fromState: RemediationTaskStates.Proposed, toState: RemediationTaskStates.Authorized,
            queuePosition: queuePositions.TryGetValue(taskId, out var position) ? position : null,
            cancellationToken: cancellationToken);

        // Grants eligibility only — the coordinator's own CAS decides if/when it dispatches.
        await coordinator.TryStartNextAsync(cancellationToken);

        return Results.Ok(new
        {
            taskId,
            state = RemediationTaskStates.Authorized,
            authorizedAt,
            queuePosition = queuePositions.TryGetValue(taskId, out var finalPosition) ? (int?)finalPosition : null,
        });
    }

    /// <summary>
    /// POST /{taskId}/dismiss (T033, FR-010): CAS <c>proposed → dismissed</c>, terminal, no
    /// agent involvement, no wiki change. Appends the outcome entry to the task record
    /// (terminal transition, data-model.md).
    /// </summary>
    private static async Task<IResult> DismissAsync(
        string taskId,
        OperationalStateRepository repository,
        RemediationLifecyclePublisher publisher,
        RemediationTaskRecordStore recordStore,
        ILogger<RemediationLifecyclePublisher> logger,
        CancellationToken cancellationToken)
    {
        var row = await FindTaskAsync(repository, taskId, cancellationToken);
        if (row is null)
        {
            return NotFound(taskId);
        }

        if (row.State != RemediationTaskStates.Proposed)
        {
            return TaskNotProposedConflict(row);
        }

        var dismissedAt = DateTimeOffset.UtcNow;
        var committed = await repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Proposed, RemediationTaskStates.Dismissed,
            outcomeReason: null, authorizedAt: null, updatedAt: dismissedAt, cancellationToken);

        if (!committed)
        {
            var current = await FindTaskAsync(repository, taskId, cancellationToken);
            return current is null ? NotFound(taskId) : TaskNotProposedConflict(current);
        }

        try
        {
            await recordStore.AppendOutcomeAsync(taskId, RemediationTaskStates.Dismissed, reason: null, dismissedAt, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to append the dismissed outcome entry to remediation task record {TaskId}.", taskId);
        }

        HubMetrics.RecordRemediationTaskDismissed();
        RemediationLifecycleLogEvents.LogTaskDismissed(logger, taskId);
        await publisher.PublishTaskChangedAsync(
            taskId, row.RunId, fromState: RemediationTaskStates.Proposed, toState: RemediationTaskStates.Dismissed,
            cancellationToken: cancellationToken);

        return Results.Ok(new { taskId, state = RemediationTaskStates.Dismissed, dismissedAt });
    }

    /// <summary>
    /// POST /{taskId}/withdraw-authorization (T033, FR-016): the human-initiated side of
    /// the withdrawal race (spec Edge Cases, data-model.md "Withdrawal race") — CAS
    /// <c>authorized → proposed</c>, clearing <c>authorized_at</c>. Competes directly
    /// against <c>RemediationRunCoordinator.TryStartNextAsync</c>'s <c>authorized →
    /// executing</c> CAS on the same persisted row; first commit wins, the loser sees the
    /// actual resulting state (research.md R5).
    /// </summary>
    private static async Task<IResult> WithdrawAuthorizationAsync(
        string taskId,
        OperationalStateRepository repository,
        RemediationLifecyclePublisher publisher,
        ILogger<RemediationLifecyclePublisher> logger,
        CancellationToken cancellationToken)
    {
        var row = await FindTaskAsync(repository, taskId, cancellationToken);
        if (row is null)
        {
            return NotFound(taskId);
        }

        if (row.State != RemediationTaskStates.Authorized)
        {
            return WithdrawConflict(row);
        }

        var committed = await repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Authorized, RemediationTaskStates.Proposed,
            outcomeReason: null, authorizedAt: null, updatedAt: DateTimeOffset.UtcNow, cancellationToken);

        if (!committed)
        {
            // The race was lost — most likely the coordinator's CAS to Executing won.
            // Show the caller exactly what happened (contract: never silence).
            var current = await FindTaskAsync(repository, taskId, cancellationToken);
            return current is null ? NotFound(taskId) : WithdrawConflict(current);
        }

        HubMetrics.RecordRemediationTaskWithdrawn();
        RemediationLifecycleLogEvents.LogAuthorizationWithdrawn(logger, taskId);
        await publisher.PublishTaskChangedAsync(
            taskId, row.RunId, fromState: RemediationTaskStates.Authorized, toState: RemediationTaskStates.Proposed,
            cancellationToken: cancellationToken);

        return Results.Ok(new { taskId, state = RemediationTaskStates.Proposed });
    }

    private static async Task<RemediationTaskRow?> FindTaskAsync(
        OperationalStateRepository repository, string taskId, CancellationToken cancellationToken)
    {
        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        return rows.FirstOrDefault(r => r.TaskId == taskId);
    }

    private static IResult NotFound(string taskId)
        => Results.NotFound(new { message = $"Remediation task '{taskId}' was not found." });

    private static IResult TaskNotProposedConflict(RemediationTaskRow row)
        => Results.Conflict(new
        {
            reason = "task_not_proposed",
            state = row.State,
            message = $"Only a proposed task can be authorized or dismissed. This task is {row.State}.",
        });

    /// <summary>
    /// contracts/remediation-task-api.md withdraw-authorization error shapes: a task no
    /// longer <c>authorized</c> is either the CAS race loser against dispatch
    /// (<c>execution_already_started</c>, when the current state is <c>executing</c> or a
    /// terminal execution outcome) or a stale double-withdraw
    /// (<c>task_not_authorized</c>, when the current state is <c>proposed</c> or
    /// <c>dismissed</c>).
    /// </summary>
    private static IResult WithdrawConflict(RemediationTaskRow row)
    {
        if (row.State is RemediationTaskStates.Executing or RemediationTaskStates.Completed
            or RemediationTaskStates.Failed or RemediationTaskStates.NotApplicable)
        {
            return Results.Conflict(new
            {
                reason = "execution_already_started",
                state = row.State,
                message = "The agent already began executing this task; it will run to a terminal outcome and can no longer be cancelled.",
            });
        }

        return Results.Conflict(new
        {
            reason = "task_not_authorized",
            state = row.State,
            message = $"Only an authorized task can have its authorization withdrawn. This task is {row.State}.",
        });
    }

    private static object ToListEntry(RemediationTaskRow row, IReadOnlyDictionary<string, int> queuePositions) => new
    {
        taskId = row.TaskId,
        runId = row.RunId,
        title = row.Title,
        description = row.Description,
        targetPath = row.TargetPath,
        state = row.State,
        proposedAt = row.ProposedAt,
        authorizedAt = row.AuthorizedAt,
        queuePosition = queuePositions.TryGetValue(row.TaskId, out var position) ? (int?)position : null,
        outcomeReason = row.OutcomeReason,
        updatedAt = row.UpdatedAt,
    };

    /// <summary>
    /// 1-based FIFO positions among tasks waiting to execute, ordered by
    /// <c>authorized_at</c> (FR-017, ADR-018 — <c>authorized_at</c> is the FIFO order
    /// authority; present only for <c>authorized</c> rows, contract `queuePosition`).
    /// Mechanical ranking of persisted rows — US4's coordinator dequeues in exactly this
    /// order.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ComputeQueuePositions(IReadOnlyList<RemediationTaskRow> rows)
        => rows
            .Where(row => row.State == RemediationTaskStates.Authorized && row.AuthorizedAt is not null)
            .OrderBy(row => row.AuthorizedAt)
            .ThenBy(row => row.TaskId, StringComparer.Ordinal)
            .Select((row, index) => (row.TaskId, Position: index + 1))
            .ToDictionary(entry => entry.TaskId, entry => entry.Position);
}

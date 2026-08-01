using Grimoire.Hub.OperationalState;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// HTTP endpoints for the Remediation Action Task workflow (015-lint-board-parity T024,
/// contracts/remediation-task-api.md; mirrors <c>LintSubmissionEndpoints</c>'
/// Minimal-API route-group pattern). US3 ships the read surface — list (the board's
/// initial-state recovery source for remediation entries) and detail (including
/// record-derived attached context, FR-011/FR-014); the authorize/dismiss/withdraw and
/// context/message transitions join in US4/US5 (T033/T041). There is deliberately no
/// execution endpoint — dispatch happens only via <c>RemediationRunCoordinator</c>
/// (ADR-018, SC-005).
/// </summary>
public static class RemediationTaskEndpoints
{
    public static RouteGroupBuilder MapRemediationTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{taskId}", GetDetailAsync);
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

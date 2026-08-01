using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.LintDispatch;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Composite board initial-state endpoint (015-lint-board-parity T012,
/// contracts/lint-board-api.md `GET /api/board`): one response carrying all board entry
/// kinds, explicitly typed via the `kind` discriminator (FR-006). The board fetches it on
/// load and on every SignalR reconnect, before resuming the lifecycle streams (research.md
/// R9). Sibling file to <see cref="IngestSubmissionEndpoints"/> so the existing ingest
/// endpoints stay byte-for-byte untouched (FR-015/SC-008): ingest entries are composed
/// from the same unchanged <see cref="KanbanBoardProjectionStore"/> +
/// <see cref="IngestRunCoordinator"/> sources as `GET /api/ingest-submissions`, carrying
/// exactly today's row field set plus `kind`. T024 (US3) adds `remediation_task` entries
/// here.
/// </summary>
public static class BoardEndpoints
{
    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetCompositeBoardAsync);
        return group;
    }

    private static async Task<IResult> GetCompositeBoardAsync(
        KanbanBoardProjectionStore store,
        ContentRootPaths contentPaths,
        IngestRunCoordinator ingestCoordinator,
        LintRunCoordinator lintCoordinator,
        CancellationToken cancellationToken)
    {
        var tasks = await store.GetAllAsync(contentPaths.TasksDir, cancellationToken);
        var queuePositions = await ingestCoordinator.GetQueuePositionsAsync(cancellationToken);

        var entries = new List<object>();
        foreach (var t in tasks)
        {
            entries.Add(new
            {
                kind = "ingest_task",
                taskId = t.TaskId,
                status = t.Column,
                title = t.Title,
                updatedAt = t.UpdatedAt,
                failureReason = t.FailureReason,
                taskLink = t.TaskLink,
                queuePosition = queuePositions.TryGetValue(t.TaskId, out var position) ? (int?)position : null,
            });
        }

        // At most the latest run appears (one active run at a time — the board shows
        // current lint status, /lint remains the historical/report view). No run ever
        // triggered ⇒ no lint_run entry; the board offers the trigger control instead
        // (US1 scenario 1).
        var latestRunId = lintCoordinator.LatestRunId;
        var run = latestRunId is null ? null : lintCoordinator.GetRun(latestRunId);
        if (run is not null)
        {
            entries.Add(new
            {
                kind = "lint_run",
                runId = run.RunId,
                status = run.Status.ToString().ToLowerInvariant(),
                triggeredAt = run.TriggeredAt,
                completedAt = run.CompletedAt,
                failureReason = run.FailureReason,
                hasFindingsReport = run.FindingsReportPath is not null,
            });
        }

        return Results.Ok(new { entries });
    }
}

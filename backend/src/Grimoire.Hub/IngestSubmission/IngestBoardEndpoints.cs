using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Composite board initial-state endpoint (015-lint-board-parity T012,
/// contracts/lint-board-api.md `GET /api/board`): one response carrying all board entry
/// kinds, explicitly typed via the `kind` discriminator (FR-006). The board fetches it on
/// load and on every SignalR reconnect, before resuming the lifecycle streams (research.md
/// R9). Sibling file to <see cref="IngestSubmissionEndpoints"/> so the existing ingest
/// endpoints stay byte-for-byte untouched (FR-015/SC-008): ingest entries are composed
/// from the same unchanged <see cref="IngestKanbanBoardProjectionStore"/> +
/// <see cref="IngestRunCoordinator"/> sources as `GET /api/ingest-submissions`, carrying
/// exactly today's row field set plus `kind`. T024 (US3) adds `remediation_task` entries
/// here.
/// </summary>
public static class IngestBoardEndpoints
{
    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetCompositeBoardAsync);
        return group;
    }

    private static async Task<IResult> GetCompositeBoardAsync(
        [FromServices] IngestKanbanBoardProjectionStore store,
        [FromServices] IngestContentPaths contentPaths,
        [FromServices] IngestRunCoordinator ingestCoordinator,
        [FromServices] LintRunCoordinator lintCoordinator,
        [FromServices] OperationalStateRepository stateRepository,
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

        // T024 (US3): remediation_task entries — the list-entry field set of
        // GET /api/remediation-tasks minus description/targetPath bulk detail (the card
        // links to the detail endpoint), including terminal tasks so outcomes stay
        // visible on the board (contracts/lint-board-api.md). Title stays the verbatim
        // agent-authored proposal title (Principle V).
        var remediationRows = await stateRepository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var remediationQueuePositions = RemediationTaskEndpoints.ComputeQueuePositions(remediationRows);
        foreach (var row in remediationRows)
        {
            entries.Add(new
            {
                kind = "remediation_task",
                taskId = row.TaskId,
                runId = row.RunId,
                title = row.Title,
                state = row.State,
                proposedAt = row.ProposedAt,
                queuePosition = remediationQueuePositions.TryGetValue(row.TaskId, out var position) ? (int?)position : null,
                outcomeReason = row.OutcomeReason,
                updatedAt = row.UpdatedAt,
            });
        }

        return Results.Ok(new { entries });
    }
}

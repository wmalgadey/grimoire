using Grimoire.Hub.ApiErrors;
using Grimoire.Hub.LintFindings;

namespace Grimoire.Hub.LintDispatch;

/// <summary>
/// HTTP endpoints for triggering a Lint Run and reading its outcome/report
/// (FR-001, mirrors <c>IngestSubmissionEndpoints</c>'s Minimal-API route-group pattern).
/// A bare trigger — no request body — since Lint takes no per-run input (FR-002).
/// </summary>
public static class LintSubmissionEndpoints
{
    public static RouteGroupBuilder MapLintRunEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", PostTriggerAsync);
        group.MapGet("/latest", GetLatestAsync);
        group.MapGet("/{runId}", GetRunAsync);
        group.MapGet("/{runId}/findings", GetFindingsAsync);
        return group;
    }

    private static async Task<IResult> PostTriggerAsync(LintRunCoordinator coordinator, CancellationToken cancellationToken)
    {
        var result = await coordinator.TriggerAsync(cancellationToken);

        return result switch
        {
            LintSubmissionResult.Accepted accepted => Results.Accepted(value: new
            {
                runId = accepted.Run.RunId,
                status = "running",
                triggeredAt = accepted.Run.TriggeredAt,
            }),
            LintSubmissionResult.Busy => ApiErrorResults.Problem(ApiErrorCatalogue.LintRunActive),
            // 015-lint-board-parity T017 (FR-004/SC-004, contracts/lint-board-api.md):
            // the second distinguishable 409 reason — never one generic "busy".
            // The unresolved ids ride along as an RFC 7807 extension member: they are structured
            // data the board genuinely consumes (LintTriggerPreconditionTests asserts them), not
            // prose, so they sit beside the five core members rather than inside the detail.
            LintSubmissionResult.Blocked blocked => ApiErrorResults.Problem(
                ApiErrorCatalogue.UnresolvedRemediationTasks,
                extensions: new Dictionary<string, object?> { ["unresolvedTaskIds"] = blocked.UnresolvedTaskIds }),
            _ => throw new InvalidOperationException($"Unknown submission result: {result.GetType().Name}"),
        };
    }

    private static Task<IResult> GetLatestAsync(LintRunCoordinator coordinator)
    {
        var runId = coordinator.LatestRunId;
        if (runId is null)
        {
            return Task.FromResult(Results.Ok(new { runId = (string?)null }));
        }

        return GetRunAsync(runId, coordinator);
    }

    private static Task<IResult> GetRunAsync(string runId, LintRunCoordinator coordinator)
    {
        var run = coordinator.GetRun(runId);
        if (run is null)
        {
            return Task.FromResult(ApiErrorResults.Problem(ApiErrorCatalogue.LintRunNotFound));
        }

        return Task.FromResult(Results.Ok(new
        {
            runId = run.RunId,
            status = run.Status.ToString().ToLowerInvariant(),
            triggeredAt = run.TriggeredAt,
            completedAt = run.CompletedAt,
            failureReason = run.FailureReason,
            hasFindingsReport = run.FindingsReportPath is not null,
        }));
    }

    private static async Task<IResult> GetFindingsAsync(string runId, FindingsReportStore store, CancellationToken cancellationToken)
    {
        var content = await store.TryReadAsync(runId, cancellationToken);
        if (content is null)
        {
            return ApiErrorResults.Problem(ApiErrorCatalogue.LintFindingsReportUnavailable);
        }

        return Results.Ok(new { runId, content });
    }
}

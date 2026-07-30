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
            LintSubmissionResult.Busy => Results.Conflict(new
            {
                reason = "lint_run_active",
                message = "A Lint Run is already active. Wait for it to finish before triggering another.",
            }),
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
            return Task.FromResult(Results.NotFound(new { message = $"Lint run '{runId}' was not found." }));
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
            return Results.NotFound(new { message = $"Findings Report for run '{runId}' is not available." });
        }

        return Results.Ok(new { runId, content });
    }
}

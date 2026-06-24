using Grimoire.Ingest.Services;

namespace Grimoire.Ingest.Api;

public static class TriggerRunEndpoint
{
    public static void MapTriggerRun(this WebApplication app)
    {
        app.MapPost("/ingest/runs", async (TriggerRunRequest? request, IngestService ingestService) =>
        {
            if (ingestService.IsRunActive)
            {
                var activeRun = ingestService.GetActiveRun()!;
                return Results.Conflict(new
                {
                    error = "RunAlreadyActive",
                    message = "An ingest run is already in progress.",
                    activeRunId = activeRun.RunId
                });
            }

            var runId = request?.RunId ?? Guid.NewGuid().ToString();
            var run = await ingestService.TriggerRunAsync(runId);

            return Results.Accepted($"/ingest/runs/{run.RunId}", new
            {
                runId = run.RunId,
                status = "Running",
                startedAt = run.StartedAt
            });
        });

        app.MapGet("/ingest/runs/{runId}", (string runId, IngestService ingestService) =>
        {
            var activeRun = ingestService.GetActiveRun();
            if (activeRun == null || activeRun.RunId != runId)
                return Results.NotFound(new { error = "RunNotFound", runId });

            return Results.Ok(new
            {
                runId = activeRun.RunId,
                status = activeRun.Status.ToString(),
                startedAt = activeRun.StartedAt,
                completedAt = activeRun.CompletedAt,
                totalFiles = activeRun.TotalFiles,
                processedCount = activeRun.ProcessedCount,
                failedCount = activeRun.FailedCount,
                skippedCount = activeRun.SkippedCount,
                totalChunks = activeRun.TotalChunks
            });
        });
    }
}

public record TriggerRunRequest(string? RunId);

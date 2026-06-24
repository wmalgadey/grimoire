using Grimoire.Api.Ingest.Persistence;

namespace Grimoire.Api.Ingest.Endpoints;

public static class GetIngestRunEndpoint
{
    public static IEndpointRouteBuilder MapGetIngestRun(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/ingest/runs/{runId}", async (
            string runId,
            IngestRepository repository) =>
        {
            var record = await repository.GetIngestRunAsync(runId);
            if (record == null)
            {
                return Results.NotFound(new
                {
                    error = "RunNotFound",
                    message = $"Run '{runId}' not found.",
                    statusCode = 404
                });
            }

            return Results.Ok(new
            {
                runId = record.RunId,
                status = record.Status,
                startedAt = record.StartedAt,
                completedAt = record.CompletedAt,
                totalFiles = record.TotalFiles,
                processedFiles = record.ProcessedFiles,
                failedFiles = record.FailedFiles,
                skippedFiles = record.SkippedFiles,
                durationMs = record.DurationMs,
                errorMessage = record.ErrorMessage,
                fileResults = record.FileResults
            });
        });

        return routes;
    }
}

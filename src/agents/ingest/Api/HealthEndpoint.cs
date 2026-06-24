using Grimoire.Ingest.Hub;
using Grimoire.Ingest.Services;

namespace Grimoire.Ingest.Api;

public static class HealthEndpoint
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/health", (IngestService ingestService, HubClient hubClient) =>
        {
            var activeRun = ingestService.GetActiveRun();

            return Results.Ok(new
            {
                status = "Healthy",
                agentId = "ingest",
                version = "1.0.0",
                checkedAt = DateTimeOffset.UtcNow,
                activeRun = activeRun == null ? null : new
                {
                    runId = activeRun.RunId,
                    status = activeRun.Status.ToString(),
                    startedAt = activeRun.StartedAt,
                    processedCount = activeRun.ProcessedCount,
                    failedCount = activeRun.FailedCount,
                    skippedCount = activeRun.SkippedCount,
                    totalFiles = activeRun.TotalFiles
                },
                hubConnected = hubClient.IsConnected
            });
        });
    }
}

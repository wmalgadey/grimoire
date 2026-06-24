using Grimoire.Api.Ingest.Persistence;
using Grimoire.Api.Ingest.Services;

namespace Grimoire.Api.Ingest.Endpoints;

public static class TriggerIngestEndpoint
{
    public static IEndpointRouteBuilder MapTriggerIngest(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ingest/trigger", async (
            TriggerRunRequest? request,
            IngestOrchestrationHandler handler,
            IngestRepository repository,
            ILogger<IngestOrchestrationHandler> logger) =>
        {
            var isRunActive = await repository.IsRunActiveAsync();
            if (isRunActive)
            {
                var activeRunId = await repository.GetActiveRunIdAsync();
                return Results.Conflict(new
                {
                    error = "RunAlreadyActive",
                    message = "An ingest run is already in progress.",
                    activeRunId
                });
            }

            try
            {
                var (runId, status, startedAt) = await handler.TriggerRunAsync(request?.RunId);
                logger.LogInformation("ingest_trigger_endpoint runId={RunId} status={Status}", runId, status);

                return Results.Accepted($"/api/ingest/runs/{runId}", new
                {
                    runId,
                    status,
                    startedAt
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ingest_trigger_endpoint_failed");
                return Results.StatusCode(503);
            }
        });

        return routes;
    }
}

public record TriggerRunRequest(string? RunId);

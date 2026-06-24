using Grimoire.Api.Ingest.Services;

namespace Grimoire.Api.Ingest.Endpoints;

public static class SubmitFeedbackEndpoint
{
    public static IEndpointRouteBuilder MapSubmitFeedback(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ingest/runs/{runId}/feedback", async (
            string runId,
            FeedbackSubmissionRequest request,
            IngestOrchestrationHandler handler,
            ILogger<IngestOrchestrationHandler> logger) =>
        {
            try
            {
                var result = await handler.SubmitFeedbackAsync(runId, request);
                logger.LogInformation("ingest_feedback_endpoint runId={RunId}", runId);
                return Results.Ok(result);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Results.NotFound(new
                {
                    error = "RequestNotFound",
                    message = "Feedback request not found."
                });
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return Results.Conflict(new
                {
                    error = "FeedbackAlreadyProvided",
                    message = "Feedback for this request has already been provided."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ingest_feedback_endpoint_failed runId={RunId}", runId);
                return Results.StatusCode(503);
            }
        });

        return routes;
    }
}

public record FeedbackSubmissionRequest(
    string RequestId,
    string FilePath,
    string Action,
    string? TagValue = null
);

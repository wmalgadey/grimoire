using Grimoire.Ingest.Cache;
using Grimoire.Ingest.Models;
using Grimoire.Ingest.Pipeline;

namespace Grimoire.Ingest.Api;

public static class FeedbackEndpoint
{
    public static void MapFeedback(this WebApplication app)
    {
        app.MapPost("/ingest/runs/{runId}/feedback", async (
            string runId,
            FeedbackRequestDto body,
            IngestCacheRepository repository,
            IngestPipeline pipeline) =>
        {
            if (!Enum.TryParse<FeedbackAction>(body.Action, ignoreCase: true, out var action))
            {
                return Results.BadRequest(new
                {
                    error = "InvalidAction",
                    message = $"Action must be one of: {string.Join(", ", Enum.GetNames<FeedbackAction>())}"
                });
            }

            var feedbackRequest = await repository.GetFeedbackRequestAsync(body.RequestId);
            if (feedbackRequest == null)
            {
                return Results.NotFound(new
                {
                    error = "FeedbackRequestNotFound",
                    requestId = body.RequestId
                });
            }

            if (feedbackRequest.ResolvedAt.HasValue)
            {
                return Results.Conflict(new
                {
                    error = "AlreadyResolved",
                    requestId = body.RequestId,
                    resolvedAt = feedbackRequest.ResolvedAt
                });
            }

            var resolvedAt = DateTimeOffset.UtcNow;
            await repository.MarkFeedbackResolvedAsync(body.RequestId, resolvedAt);

            // Persist action and tag to IngestRecord
            var record = await repository.GetRecordAsync(body.FilePath);
            if (record != null)
            {
                record.FeedbackAction = action.ToString();
                record.FeedbackTag = body.TagValue;
                await repository.SaveRecordAsync(record);
            }

            var response = new FeedbackResponse
            {
                RequestId = body.RequestId,
                FilePath = body.FilePath,
                Action = action,
                TagValue = body.TagValue,
                DecidedAt = resolvedAt
            };

            pipeline.ResolveFeedback(body.RequestId, response);

            return Results.Ok(new
            {
                requestId = response.RequestId,
                filePath = response.FilePath,
                action = response.Action.ToString(),
                tagValue = response.TagValue,
                decidedAt = response.DecidedAt
            });
        });
    }
}

public record FeedbackRequestDto(
    string RequestId,
    string FilePath,
    string Action,
    string? TagValue);

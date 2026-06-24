using Grimoire.Api.Ingest.Persistence;
using Grimoire.Api.Ingest.Services;

namespace Grimoire.Api.Ingest.Endpoints;

public static class ConversationEndpoint
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ingest/conversations/{conversationId}/messages", async (
            string conversationId,
            ConversationMessageRequest request,
            IngestOrchestrationHandler handler,
            IngestRepository repository) =>
        {
            try
            {
                var turns = await repository.GetConversationTurnsAsync(conversationId);
                if (turns.Count == 0)
                {
                    return Results.NotFound(new
                    {
                        error = "ConversationNotFound",
                        message = $"Conversation '{conversationId}' not found.",
                        statusCode = 404
                    });
                }

                var result = await handler.SubmitConversationTurnAsync(conversationId, request.Message);
                return Results.Ok(result);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Results.NotFound(new
                {
                    error = "ConversationNotFound",
                    message = $"Conversation '{conversationId}' not found.",
                    statusCode = 404
                });
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return Results.Conflict(new
                {
                    error = "ConversationDismissed",
                    message = "Conversation has been dismissed."
                });
            }
            catch
            {
                return Results.StatusCode(503);
            }
        });

        routes.MapGet("/api/ingest/conversations/{conversationId}", async (
            string conversationId,
            IngestRepository repository) =>
        {
            var turns = await repository.GetConversationTurnsAsync(conversationId);
            if (turns.Count == 0)
            {
                return Results.NotFound(new
                {
                    error = "ConversationNotFound",
                    message = $"Conversation '{conversationId}' not found.",
                    statusCode = 404
                });
            }

            var firstTurn = turns.First();
            return Results.Ok(new
            {
                conversationId,
                filePath = firstTurn.FilePath,
                turns = turns.Select(t => new
                {
                    t.TurnIndex,
                    t.Role,
                    t.Message,
                    t.CreatedAt
                }).ToList()
            });
        });

        return routes;
    }
}

public record ConversationMessageRequest(string Message);

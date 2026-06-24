using Grimoire.Ingest.Conversation;

namespace Grimoire.Ingest.Api;

public static class ConversationEndpoint
{
    public static void MapConversation(this WebApplication app)
    {
        app.MapPost("/ingest/conversations/{conversationId}/turns", async (
            string conversationId,
            ConversationTurnRequest body,
            ConversationService conversationService) =>
        {
            var conversation = conversationService.GetConversation(conversationId);
            if (conversation == null)
            {
                return Results.NotFound(new
                {
                    error = "ConversationNotFound",
                    conversationId
                });
            }

            if (conversation.DismissedAt.HasValue)
            {
                return Results.Conflict(new
                {
                    error = "ConversationDismissed",
                    conversationId,
                    dismissedAt = conversation.DismissedAt
                });
            }

            try
            {
                var turn = await conversationService.AddUserTurnAsync(conversationId, body.Message);

                return Results.Ok(new
                {
                    conversationId = turn.ConversationId,
                    turnIndex = turn.TurnIndex,
                    role = "agent",
                    message = turn.Message,
                    createdAt = turn.CreatedAt
                });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new
                {
                    error = "ConversationNotFound",
                    conversationId
                });
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new
                {
                    error = "ConversationDismissed",
                    conversationId
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "LLM call failed");
            }
        });
    }
}

public record ConversationTurnRequest(string Message);

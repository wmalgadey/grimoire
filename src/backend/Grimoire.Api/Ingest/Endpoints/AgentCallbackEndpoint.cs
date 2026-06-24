using Grimoire.Api.Ingest.Models.SignalREvents;
using Grimoire.Api.Ingest.Services;

namespace Grimoire.Api.Ingest.Endpoints;

public static class AgentCallbackEndpoint
{
    public static IEndpointRouteBuilder MapAgentCallback(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ingest/callbacks/progress", async (
            IngestProgress progress,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleProgressCallbackAsync(progress.RunId, progress);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/feedback-request", async (
            IngestFeedbackRequest feedbackRequest,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleFeedbackRequestCallbackAsync(feedbackRequest.RunId, feedbackRequest);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/conversation-opened", async (
            IngestConversationOpened payload,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleConversationOpenedCallbackAsync(payload.ConversationId, payload);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/conversation-turn", async (
            IngestConversationTurn turn,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleConversationTurnCallbackAsync(turn.ConversationId, turn);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/run-completed", async (
            IngestRunCompleted completed,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleRunCompletedCallbackAsync(completed.RunId, completed);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/log-entry", async (
            IngestLogEntry logEntry,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleLogEntryCallbackAsync(logEntry.RunId, logEntry);
            return Results.Ok();
        });

        return routes;
    }
}

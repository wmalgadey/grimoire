using Grimoire.Api.Ingest.Models.SignalREvents;
using Grimoire.Api.Ingest.Services;

namespace Grimoire.Api.Ingest.Endpoints;

public static class AgentCallbackEndpoint
{
    public static IEndpointRouteBuilder MapAgentCallback(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ingest/callbacks/progress", async (
            string runId,
            IngestProgress progress,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleProgressCallbackAsync(runId, progress);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/feedback-request", async (
            string runId,
            IngestFeedbackRequest feedbackRequest,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleFeedbackRequestCallbackAsync(runId, feedbackRequest);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/conversation-opened", async (
            string conversationId,
            IngestConversationOpened payload,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleConversationOpenedCallbackAsync(conversationId, payload);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/conversation-turn", async (
            string conversationId,
            IngestConversationTurn turn,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleConversationTurnCallbackAsync(conversationId, turn);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/run-completed", async (
            string runId,
            IngestRunCompleted completed,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleRunCompletedCallbackAsync(runId, completed);
            return Results.Ok();
        });

        routes.MapPost("/api/ingest/callbacks/log-entry", async (
            string runId,
            IngestLogEntry logEntry,
            IngestOrchestrationHandler handler) =>
        {
            await handler.HandleLogEntryCallbackAsync(runId, logEntry);
            return Results.Ok();
        });

        return routes;
    }
}

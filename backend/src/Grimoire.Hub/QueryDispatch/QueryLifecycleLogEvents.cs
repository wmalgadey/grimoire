using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.QueryDispatch;

/// <summary>
/// Hub-side structured log events for Query Turn dispatch/lifecycle (plan.md
/// Observability > Structured Log Events, 008-query-agent). Instructions-loaded events
/// are agent-side (<c>Grimoire.QueryAgent</c>'s own logger) — see that assembly's log
/// events type — since that is where instruction loading actually happens.
/// </summary>
public static class QueryLifecycleLogEvents
{
    private static readonly EventId TurnCreatedEvent = new(50, "query.turn.created");
    private static readonly EventId TurnCompletedEvent = new(51, "query.turn.completed");
    private static readonly EventId TurnInterruptedEvent = new(52, "query.turn.interrupted");
    private static readonly EventId TurnFailedEvent = new(53, "query.turn.failed");
    private static readonly EventId SubmissionRejectedEvent = new(54, "query.submission.rejected");

    public static void LogTurnCreated(ILogger logger, string conversationId, string turnId)
    {
        using var span = StartLogEventSpan("query.turn.created", "Information");
        span?.SetTag("conversation_id", conversationId);
        span?.SetTag("turn_id", turnId);

        logger.LogInformation(TurnCreatedEvent,
            "Query turn created and dispatched. conversation_id={conversation_id} turn_id={turn_id}",
            conversationId, turnId);
    }

    public static void LogTurnCompleted(ILogger logger, string turnId, long durationMs)
    {
        using var span = StartLogEventSpan("query.turn.completed", "Information");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("duration_ms", durationMs);

        logger.LogInformation(TurnCompletedEvent,
            "Query turn completed. turn_id={turn_id} duration_ms={duration_ms}",
            turnId, durationMs);
    }

    public static void LogTurnInterrupted(ILogger logger, string turnId)
    {
        using var span = StartLogEventSpan("query.turn.interrupted", "Information");
        span?.SetTag("turn_id", turnId);

        logger.LogInformation(TurnInterruptedEvent,
            "Query turn interrupted by user. turn_id={turn_id}",
            turnId);
    }

    public static void LogTurnFailed(ILogger logger, string turnId, string reason)
    {
        using var span = StartLogEventSpan("query.turn.failed", "Error");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("reason", reason);

        logger.LogError(TurnFailedEvent,
            "Query turn failed. turn_id={turn_id} reason={reason}",
            turnId, reason);
    }

    public static void LogSubmissionRejected(ILogger logger, string conversationId)
    {
        using var span = StartLogEventSpan("query.submission.rejected", "Information");
        span?.SetTag("conversation_id", conversationId);

        logger.LogInformation(SubmissionRejectedEvent,
            "Query submission rejected. conversation_id={conversation_id}",
            conversationId);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = HubTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}

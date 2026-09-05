using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.QueryConversations;

/// <summary>
/// Structured log events for the Conversation Record write/read paths (plan.md
/// ## Observability > Structured Log Events, 011-query-conversations) — extends the
/// <c>QueryLifecycleLogEvents</c> idiom: stable event names, mandatory fields, each
/// emission wrapped in a log-event span carrying the same fields (ADR-005).
/// </summary>
public static class QueryConversationRecordLogEvents
{
    private static readonly EventId RecordCreatedEvent = new(60, "query.conversation.record_created");
    private static readonly EventId TurnRecordedEvent = new(61, "query.conversation.turn_recorded");
    private static readonly EventId RecordAppendFailedEvent = new(62, "query.conversation.record_append_failed");
    private static readonly EventId ContextLoadedEvent = new(63, "query.conversation.context_loaded");
    private static readonly EventId RecordLoadFailedEvent = new(64, "query.conversation.record_load_failed");
    private static readonly EventId TrailingFragmentDroppedEvent = new(65, "query.conversation.trailing_fragment_dropped");

    public static void LogRecordCreated(ILogger logger, string conversationId, string path)
    {
        using var span = StartLogEventSpan("query.conversation.record_created", "Information");
        span?.SetTag("conversation_id", conversationId);
        span?.SetTag("path", path);

        logger.LogInformation(RecordCreatedEvent,
            "Conversation record created. conversation_id={conversation_id} path={path}",
            conversationId, path);
    }

    public static void LogTurnRecorded(ILogger logger, string conversationId, string turnId, int position, string outcome)
    {
        using var span = StartLogEventSpan("query.conversation.turn_recorded", "Information");
        span?.SetTag("conversation_id", conversationId);
        span?.SetTag("turn_id", turnId);
        span?.SetTag("position", position);
        span?.SetTag("outcome", outcome);

        logger.LogInformation(TurnRecordedEvent,
            "Conversation turn recorded. conversation_id={conversation_id} turn_id={turn_id} position={position} outcome={outcome}",
            conversationId, turnId, position, outcome);
    }

    public static void LogRecordAppendFailed(ILogger logger, string conversationId, string turnId, string reason)
    {
        using var span = StartLogEventSpan("query.conversation.record_append_failed", "Error");
        span?.SetTag("conversation_id", conversationId);
        span?.SetTag("turn_id", turnId);
        span?.SetTag("reason", reason);

        logger.LogError(RecordAppendFailedEvent,
            "Conversation record append failed (turn outcome unaffected). conversation_id={conversation_id} turn_id={turn_id} reason={reason}",
            conversationId, turnId, reason);
    }

    public static void LogContextLoaded(ILogger logger, string conversationId, int turnCount, string source)
    {
        using var span = StartLogEventSpan("query.conversation.context_loaded", "Information");
        span?.SetTag("conversation_id", conversationId);
        span?.SetTag("turn_count", turnCount);
        span?.SetTag("source", source);

        logger.LogInformation(ContextLoadedEvent,
            "Conversation context loaded. conversation_id={conversation_id} turn_count={turn_count} source={source}",
            conversationId, turnCount, source);
    }

    public static void LogRecordLoadFailed(ILogger logger, string conversationId, string reason)
    {
        using var span = StartLogEventSpan("query.conversation.record_load_failed", "Error");
        span?.SetTag("conversation_id", conversationId);
        span?.SetTag("reason", reason);

        logger.LogError(RecordLoadFailedEvent,
            "Conversation record unreadable — submission rejected fail-closed. conversation_id={conversation_id} reason={reason}",
            conversationId, reason);
    }

    /// <summary>
    /// WARN diagnostic for a dropped trailing incomplete block (crash mid-append —
    /// contract Parsing rule 4). Not a plan.md contract row; additive diagnostic only.
    /// </summary>
    public static void LogTrailingFragmentDropped(ILogger logger, string conversationId)
    {
        using var span = StartLogEventSpan("query.conversation.trailing_fragment_dropped", "Warning");
        span?.SetTag("conversation_id", conversationId);

        logger.LogWarning(TrailingFragmentDroppedEvent,
            "Conversation record had a trailing incomplete turn block; fragment dropped, complete turns recovered. conversation_id={conversation_id}",
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

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Structured log events for the ingest-submission pipeline (plan.md ## Observability >
/// Structured Log Events). Each event starts a matching Activity span tagged
/// signal_type=log/event_name/level so logs and traces correlate.
/// </summary>
public static class IngestSubmissionLogEvents
{
    private static readonly EventId SubmissionAcceptedEvent = new(20, "ingest.submission.accepted");
    private static readonly EventId UrlFetchFailedEvent = new(21, "ingest.submission.url_fetch.failed");
    private static readonly EventId ConversionCompletedEvent = new(22, "ingest.submission.conversion.completed");
    private static readonly EventId ConversionFailedEvent = new(23, "ingest.submission.conversion.failed");
    private static readonly EventId OriginalPersistedEvent = new(24, "ingest.submission.original.persisted");
    private static readonly EventId RunTriggeredEvent = new(25, "ingest.run.triggered");
    private static readonly EventId PromptConfigEvent = new(26, "ingest.submission.prompt_config");
    private static readonly EventId ConvertConfigEvent = new(27, "ingest.submission.convert_config");
    private static readonly EventId ConfigRejectedEvent = new(28, "ingest.submission.config_rejected");
    private static readonly EventId RunLivenessFailedEvent = new(29, "ingest.run.liveness_failed");
    private static readonly EventId RunLateEventEvent = new(30, "ingest.run.late_event");
    private static readonly EventId QueueEnqueuedEvent = new(31, "ingest.queue.enqueued");
    private static readonly EventId QueueAdvancedEvent = new(32, "ingest.queue.advanced");
    private static readonly EventId QueuePausedAfterRestartEvent = new(33, "ingest.queue.paused_after_restart");
    private static readonly EventId QueueResumedEvent = new(34, "ingest.queue.resumed");

    public static void LogSubmissionAccepted(ILogger logger, string taskId, string sourceKind, DateTimeOffset submittedAt)
    {
        using var span = StartLogEventSpan("ingest.submission.accepted", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("source_kind", sourceKind);
        span?.SetTag("submitted_at", submittedAt);

        logger.LogInformation(SubmissionAcceptedEvent,
            "Ingest submission accepted. task_id={task_id} source_kind={source_kind} submitted_at={submitted_at}",
            taskId, sourceKind, submittedAt);
    }

    public static void LogUrlFetchFailed(ILogger logger, string taskId, string url, string failureReason, int? httpStatus)
    {
        using var span = StartLogEventSpan("ingest.submission.url_fetch.failed", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("url", url);
        span?.SetTag("failure_reason", failureReason);
        span?.SetTag("http_status", httpStatus);

        logger.LogWarning(UrlFetchFailedEvent,
            "Ingest submission URL fetch failed. task_id={task_id} url={url} failure_reason={failure_reason} http_status={http_status}",
            taskId, url, failureReason, httpStatus);
    }

    public static void LogConversionCompleted(ILogger logger, string taskId, string sourceKind, string normalizedPath, long durationMs)
    {
        using var span = StartLogEventSpan("ingest.submission.conversion.completed", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("source_kind", sourceKind);
        span?.SetTag("normalized_path", normalizedPath);
        span?.SetTag("duration_ms", durationMs);

        logger.LogInformation(ConversionCompletedEvent,
            "Ingest submission conversion completed. task_id={task_id} source_kind={source_kind} normalized_path={normalized_path} duration_ms={duration_ms}",
            taskId, sourceKind, normalizedPath, durationMs);
    }

    public static void LogConversionFailed(ILogger logger, string taskId, string sourceKind, string failureReason)
    {
        using var span = StartLogEventSpan("ingest.submission.conversion.failed", "Error");
        span?.SetTag("task_id", taskId);
        span?.SetTag("source_kind", sourceKind);
        span?.SetTag("failure_reason", failureReason);

        logger.LogError(ConversionFailedEvent,
            "Ingest submission conversion failed. task_id={task_id} source_kind={source_kind} failure_reason={failure_reason}",
            taskId, sourceKind, failureReason);
    }

    public static void LogOriginalPersisted(ILogger logger, string taskId, string originalPath, long sizeBytes, string contentType)
    {
        using var span = StartLogEventSpan("ingest.submission.original.persisted", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("original_path", originalPath);
        span?.SetTag("size_bytes", sizeBytes);
        span?.SetTag("content_type", contentType);

        logger.LogInformation(OriginalPersistedEvent,
            "Ingest submission original artifact persisted. task_id={task_id} original_path={original_path} size_bytes={size_bytes} content_type={content_type}",
            taskId, originalPath, sizeBytes, contentType);
    }

    public static void LogRunTriggered(ILogger logger, string taskId, long queuedDurationMs)
    {
        using var span = StartLogEventSpan("ingest.run.triggered", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("queued_duration_ms", queuedDurationMs);

        logger.LogInformation(RunTriggeredEvent,
            "Ingest run triggered. task_id={task_id} queued_duration_ms={queued_duration_ms}",
            taskId, queuedDurationMs);
    }

    // --- 004-ingest-agent-systemprompt (plan.md ## Observability > Structured Log Events) ---

    public static void LogPromptConfig(ILogger logger, string taskId, string promptSource, int promptLength)
    {
        using var span = StartLogEventSpan("ingest.submission.prompt_config", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("prompt_source", promptSource);
        span?.SetTag("prompt_length", promptLength);

        logger.LogInformation(PromptConfigEvent,
            "Ingest submission prompt configuration. task_id={task_id} prompt_source={prompt_source} prompt_length={prompt_length}",
            taskId, promptSource, promptLength);
    }

    public static void LogConvertConfig(ILogger logger, string taskId, string step, bool enabled)
    {
        using var span = StartLogEventSpan("ingest.submission.convert_config", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("step", step);
        span?.SetTag("enabled", enabled);

        logger.LogInformation(ConvertConfigEvent,
            "Ingest submission convert-step configuration. task_id={task_id} step={step} enabled={enabled}",
            taskId, step, enabled);
    }

    public static void LogConfigRejected(ILogger logger, string sourceKind, string reason)
    {
        using var span = StartLogEventSpan("ingest.submission.config_rejected", "Warning");
        span?.SetTag("source_kind", sourceKind);
        span?.SetTag("reason", reason);

        logger.LogWarning(ConfigRejectedEvent,
            "Ingest submission configuration rejected before task creation. source_kind={source_kind} reason={reason}",
            sourceKind, reason);
    }

    public static void LogRunLivenessFailed(ILogger logger, string taskId, long secondsSinceLastEvent, long livenessWindowSeconds)
    {
        using var span = StartLogEventSpan("ingest.run.liveness_failed", "Error");
        span?.SetTag("task_id", taskId);
        span?.SetTag("seconds_since_last_event", secondsSinceLastEvent);
        span?.SetTag("liveness_window_seconds", livenessWindowSeconds);

        logger.LogError(RunLivenessFailedEvent,
            "Ingest run liveness window expired. task_id={task_id} seconds_since_last_event={seconds_since_last_event} liveness_window_seconds={liveness_window_seconds}",
            taskId, secondsSinceLastEvent, livenessWindowSeconds);
    }

    public static void LogRunLateEvent(ILogger logger, string taskId, string eventType)
    {
        using var span = StartLogEventSpan("ingest.run.late_event", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("event_type", eventType);

        logger.LogWarning(RunLateEventEvent,
            "Agent run event received for a task already in a terminal state; recorded, no state change. task_id={task_id} event_type={event_type}",
            taskId, eventType);
    }

    public static void LogQueueEnqueued(ILogger logger, string taskId, int queuePosition)
    {
        using var span = StartLogEventSpan("ingest.queue.enqueued", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("queue_position", queuePosition);

        logger.LogInformation(QueueEnqueuedEvent,
            "Ingest task entered the run queue. task_id={task_id} queue_position={queue_position}",
            taskId, queuePosition);
    }

    public static void LogQueueAdvanced(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("ingest.queue.advanced", "Information");
        span?.SetTag("task_id", taskId);

        logger.LogInformation(QueueAdvancedEvent,
            "Run queue advanced; starting next task. task_id={task_id}",
            taskId);
    }

    public static void LogQueuePausedAfterRestart(ILogger logger, int queuedCount)
    {
        using var span = StartLogEventSpan("ingest.queue.paused_after_restart", "Warning");
        span?.SetTag("queued_count", queuedCount);

        logger.LogWarning(QueuePausedAfterRestartEvent,
            "Hub restart found queued tasks; run queue paused until explicit resume. queued_count={queued_count}",
            queuedCount);
    }

    public static void LogQueueResumed(ILogger logger, string taskId, string scope)
    {
        using var span = StartLogEventSpan("ingest.queue.resumed", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("scope", scope);

        logger.LogInformation(QueueResumedEvent,
            "Run queue processing resumed. task_id={task_id} scope={scope}",
            taskId, scope);
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

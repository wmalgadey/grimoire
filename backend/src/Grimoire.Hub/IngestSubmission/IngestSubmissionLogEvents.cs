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
    private static readonly EventId TaskRecordServedEvent = new(35, "task_record.served");
    private static readonly EventId TaskRecordChangePublishedEvent = new(36, "task_record.change_published");
    private static readonly EventId TaskRecordWatchFailedEvent = new(37, "task_record.watch_failed");
    private static readonly EventId RunLivenessInterruptedEvent = new(38, "ingest.run.liveness_interrupted");
    private static readonly EventId RunReactivatedEvent = new(39, "ingest.run.reactivated");
    private static readonly EventId ReactivationExhaustedEvent = new(40, "ingest.run.reactivation_exhausted");
    private static readonly EventId TaskRestartedEvent = new(41, "ingest.task.restarted");
    private static readonly EventId TaskRestartRejectedEvent = new(42, "ingest.task.restart_rejected");
    private static readonly EventId SourceServedEvent = new(43, "ingest.source.served");
    private static readonly EventId RunCancelledEvent = new(44, "ingest.run.cancelled");

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
        var sanitizedSourceKind = SanitizeForLog(sourceKind);
        var sanitizedReason = SanitizeForLog(reason);

        using var span = StartLogEventSpan("ingest.submission.config_rejected", "Warning");
        span?.SetTag("source_kind", sanitizedSourceKind);
        span?.SetTag("reason", sanitizedReason);

        logger.LogWarning(ConfigRejectedEvent,
            "Ingest submission configuration rejected before task creation. source_kind={source_kind} reason={reason}",
            sanitizedSourceKind, sanitizedReason);
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

    // --- 006-hexagonal-arch-tasks-ui (plan.md ## Observability > Structured Log Events) ---

    public static void LogTaskRecordServed(ILogger logger, string taskId, string outcome, int contentLength)
    {
        using var span = StartLogEventSpan("task_record.served", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("outcome", outcome);
        span?.SetTag("content_length", contentLength);

        logger.LogInformation(TaskRecordServedEvent,
            "Task record served. task_id={task_id} outcome={outcome} content_length={content_length}",
            taskId, outcome, contentLength);
    }

    public static void LogTaskRecordChangePublished(ILogger logger, string taskId, string eventId, DateTimeOffset changedAt)
    {
        using var span = StartLogEventSpan("task_record.change_published", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("event_id", eventId);
        span?.SetTag("changed_at", changedAt);

        logger.LogInformation(TaskRecordChangePublishedEvent,
            "Task record change published. task_id={task_id} event_id={event_id} changed_at={changed_at}",
            taskId, eventId, changedAt);
    }

    public static void LogTaskRecordWatchFailed(ILogger logger, string path, string reason)
    {
        using var span = StartLogEventSpan("task_record.watch_failed", "Warning");
        span?.SetTag("path", path);
        span?.SetTag("reason", reason);

        logger.LogWarning(TaskRecordWatchFailedEvent,
            "Task record watcher failed; restarting. path={path} reason={reason}",
            path, reason);
    }

    // --- 023-task-ui-improvements (plan.md ## Observability > Structured Log Events) ---

    /// <summary>
    /// Liveness window exceeded while a reactivation attempt is still available (FR-007/FR-008).
    /// Distinct from <see cref="LogRunLivenessFailed"/>, which keeps its existing meaning and is
    /// emitted only once the bounded attempts are exhausted.
    /// </summary>
    public static void LogRunLivenessInterrupted(ILogger logger, string taskId, int attempt, long nextDelaySeconds)
    {
        using var span = StartLogEventSpan("ingest.run.liveness_interrupted", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("attempt", attempt);
        span?.SetTag("next_delay_seconds", nextDelaySeconds);

        logger.LogWarning(RunLivenessInterruptedEvent,
            "Ingest run liveness interrupted; reactivation scheduled. task_id={task_id} attempt={attempt} next_delay_seconds={next_delay_seconds}",
            taskId, attempt, nextDelaySeconds);
    }

    public static void LogRunReactivated(ILogger logger, string taskId, int attempt)
    {
        using var span = StartLogEventSpan("ingest.run.reactivated", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("attempt", attempt);

        logger.LogInformation(RunReactivatedEvent,
            "Ingest run reactivated after a liveness interruption. task_id={task_id} attempt={attempt}",
            taskId, attempt);
    }

    public static void LogReactivationExhausted(ILogger logger, string taskId, int attempts)
    {
        using var span = StartLogEventSpan("ingest.run.reactivation_exhausted", "Error");
        span?.SetTag("task_id", taskId);
        span?.SetTag("attempts", attempts);

        logger.LogError(ReactivationExhaustedEvent,
            "Ingest run reactivation attempts exhausted; failing the task. task_id={task_id} attempts={attempts}",
            taskId, attempts);
    }

    /// <summary>Issue #184 remedy (3): an operator cancelled the actively-running task.</summary>
    public static void LogRunCancelled(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("ingest.run.cancelled", "Warning");
        span?.SetTag("task_id", taskId);

        logger.LogWarning(RunCancelledEvent,
            "Ingest run cancelled by operator request. task_id={task_id}", taskId);
    }

    public static void LogTaskRestarted(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("ingest.task.restarted", "Information");
        span?.SetTag("task_id", taskId);

        logger.LogInformation(TaskRestartedEvent,
            "Failed ingest task restarted and re-queued. task_id={task_id}", taskId);
    }

    public static void LogTaskRestartRejected(ILogger logger, string taskId, string currentStatus)
    {
        using var span = StartLogEventSpan("ingest.task.restart_rejected", "Warning");
        span?.SetTag("task_id", taskId);
        span?.SetTag("current_status", currentStatus);

        logger.LogWarning(TaskRestartRejectedEvent,
            "Ingest task restart rejected. task_id={task_id} current_status={current_status}",
            taskId, currentStatus);
    }

    public static void LogSourceServed(ILogger logger, string taskId, string contentType)
    {
        using var span = StartLogEventSpan("ingest.source.served", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("content_type", contentType);

        logger.LogInformation(SourceServedEvent,
            "Ingest source content served. task_id={task_id} content_type={content_type}",
            taskId, contentType);
    }

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = HubTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }

    private static string SanitizeForLog(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}

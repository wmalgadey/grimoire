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

    private static Activity? StartLogEventSpan(string eventName, string level)
    {
        var span = HubTracing.ActivitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}

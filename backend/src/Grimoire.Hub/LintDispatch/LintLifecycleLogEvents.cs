using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.LintDispatch;

/// <summary>
/// Hub-side structured log events for Lint Run dispatch/lifecycle (plan.md
/// Observability > Structured Log Events, 013-lint-agent). Instructions-loaded events are
/// agent-side (<c>Grimoire.LintAgent.LintAgentLogEvents</c>) since that is where
/// instruction loading actually happens — mirrors <c>QueryLifecycleLogEvents</c>' split.
/// </summary>
public static class LintLifecycleLogEvents
{
    private static readonly EventId RunTriggeredEvent = new(80, "lint.run.triggered");
    private static readonly EventId RunRejectedEvent = new(81, "lint.run.rejected");
    private static readonly EventId RunCompletedEvent = new(82, "lint.run.completed");
    private static readonly EventId RunFailedEvent = new(83, "lint.run.failed");
    private static readonly EventId LifecyclePublishedEvent = new(84, "lint.lifecycle.published");

    public static void LogRunTriggered(ILogger logger, string runId)
    {
        using var span = StartLogEventSpan("lint.run.triggered", "Information");
        span?.SetTag("run_id", runId);

        logger.LogInformation(RunTriggeredEvent, "Lint run triggered and dispatched. run_id={run_id}", runId);
    }

    public static void LogRunRejected(ILogger logger)
    {
        using var span = StartLogEventSpan("lint.run.rejected", "Information");

        logger.LogInformation(RunRejectedEvent, "Lint run trigger rejected — a run is already active.");
    }

    public static void LogRunCompleted(ILogger logger, string runId, int findingsCount)
    {
        using var span = StartLogEventSpan("lint.run.completed", "Information");
        span?.SetTag("run_id", runId);
        span?.SetTag("findings_count", findingsCount);

        logger.LogInformation(RunCompletedEvent,
            "Lint run completed. run_id={run_id} findings_count={findings_count}", runId, findingsCount);
    }

    public static void LogRunFailed(ILogger logger, string runId, string reason)
    {
        using var span = StartLogEventSpan("lint.run.failed", "Error");
        span?.SetTag("run_id", runId);
        span?.SetTag("reason", reason);

        logger.LogError(RunFailedEvent, "Lint run failed. run_id={run_id} reason={reason}", runId, reason);
    }

    /// <summary>
    /// 015-lint-board-parity T011: one broadcast published on
    /// <c>/hubs/lint-lifecycle</c> — mirrors <c>ingest.lifecycle.published</c>'s
    /// per-broadcast log event (emitted by <c>LintLifecyclePublisher</c>).
    /// </summary>
    public static void LogLifecyclePublished(ILogger logger, string runId, string? fromStatus, string toStatus)
    {
        using var span = StartLogEventSpan("lint.lifecycle.published", "Information");
        span?.SetTag("run_id", runId);
        span?.SetTag("from_status", fromStatus);
        span?.SetTag("to_status", toStatus);

        logger.LogInformation(LifecyclePublishedEvent,
            "Lint lifecycle published: {run_id} {from_status} -> {to_status}", runId, fromStatus, toStatus);
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

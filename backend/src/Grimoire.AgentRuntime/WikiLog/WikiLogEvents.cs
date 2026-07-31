using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.AgentRuntime.WikiLog;

/// <summary>
/// <c>wiki.log.backstop_appended</c> (WARN; plan.md ## Observability > Structured Log
/// Events), replacing the retired <c>ingest.log.backstop_appended</c>
/// (<c>IngestAgentLogEvents</c>) now that <see cref="WikiLogAppender"/> is shared by
/// every agent process. Mirrors the <c>StartLogEventSpan</c> idiom every per-agent
/// <c>*AgentLogEvents</c> class already uses, but takes the calling agent's
/// <see cref="ActivitySource"/> as a parameter instead of owning one — the same reason
/// <see cref="WikiLogMetrics"/> takes a <see cref="System.Diagnostics.Metrics.Meter"/>
/// parameter (only the profile's own frozen source/meter names are registered with the
/// OTel providers, ADR-005/ADR-013).
/// </summary>
public static class WikiLogEvents
{
    private static readonly EventId BackstopAppendedEvent = new(1, "wiki.log.backstop_appended");

    /// <summary>
    /// Mandatory fields per plan.md: <paramref name="type"/>, <paramref name="taskIdOrRunId"/>,
    /// <paramref name="outcome"/>.
    /// </summary>
    public static void LogBackstopAppended(
        ILogger logger,
        ActivitySource activitySource,
        string type,
        string taskIdOrRunId,
        string outcome)
    {
        using var span = StartLogEventSpan(activitySource, BackstopAppendedEvent.Name ?? "wiki.log.backstop_appended", "Warning");
        span?.SetTag("type", type);
        span?.SetTag("task_id_or_run_id", taskIdOrRunId);
        span?.SetTag("outcome", outcome);

        logger.LogWarning(
            BackstopAppendedEvent,
            "Log backstop appended. type={type} task_id_or_run_id={task_id_or_run_id} outcome={outcome}",
            type,
            taskIdOrRunId,
            outcome);
    }

    private static Activity? StartLogEventSpan(ActivitySource activitySource, string eventName, string level)
    {
        var span = activitySource.StartActivity(eventName);
        span?.SetTag("signal_type", "log");
        span?.SetTag("event_name", eventName);
        span?.SetTag("level", level);
        return span;
    }
}

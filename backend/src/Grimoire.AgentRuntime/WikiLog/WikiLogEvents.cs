using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.AgentRuntime.WikiLog;

/// <summary>
/// Structured log events for the wiki activity log (plan.md ## Observability > Structured
/// Log Events). Mirrors the <c>StartLogEventSpan</c> idiom every per-agent
/// <c>*AgentLogEvents</c> class already uses, but takes the calling agent's
/// <see cref="ActivitySource"/> as a parameter instead of owning one — the same reason
/// <see cref="WikiLogMetrics"/> takes a <see cref="System.Diagnostics.Metrics.Meter"/>
/// parameter (only the profile's own frozen source/meter names are registered with the
/// OTel providers, ADR-005/ADR-013).
///
/// 025-agent-owned-log (ADR-028): <c>wiki.log.backstop_appended</c> is retired with the
/// <c>WikiLogAppender</c> backstop itself — the activity log is agent-authored wiki
/// content, so the harness no longer writes it and has nothing to report about writing it.
/// What replaces it is <c>wiki.log.change_not_logged</c>: the one diagnostic the backstop
/// carried that nothing else does — an agent changed the wiki and did not log it — now
/// derived from the harness's own record of allowed writes, never from wiki content.
/// </summary>
public static class WikiLogEvents
{
    private static readonly EventId ChangeNotLoggedEvent = new(2, "wiki.log.change_not_logged");

    /// <summary>
    /// <c>wiki.log.change_not_logged</c> (WARN). Emitted at run end when the run's allowed
    /// wiki-content writes are non-zero and the canonical activity-log path is not among
    /// its touched paths (FR-012a, SC-009). Mandatory fields per plan.md:
    /// <paramref name="type"/>, <paramref name="taskIdOrRunId"/>,
    /// <paramref name="wikiContentWrites"/>.
    /// </summary>
    public static void LogChangeNotLogged(
        ILogger logger,
        ActivitySource activitySource,
        string type,
        string taskIdOrRunId,
        int wikiContentWrites)
    {
        using var span = StartLogEventSpan(activitySource, ChangeNotLoggedEvent.Name ?? "wiki.log.change_not_logged", "Warning");
        span?.SetTag("type", type);
        span?.SetTag("task_id_or_run_id", taskIdOrRunId);
        span?.SetTag("wiki_content_writes", wikiContentWrites);

        logger.LogWarning(
            ChangeNotLoggedEvent,
            "Wiki content changed but no activity-log entry was written. type={type} task_id_or_run_id={task_id_or_run_id} wiki_content_writes={wiki_content_writes}",
            type,
            taskIdOrRunId,
            wikiContentWrites);
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

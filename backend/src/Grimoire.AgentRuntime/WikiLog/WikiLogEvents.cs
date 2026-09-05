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

    private static readonly EventId FormatDeviationEvent = new(3, "wiki.log.format_deviation");

    /// <summary>
    /// <c>wiki.log.format_deviation</c> (WARN). Emitted once per <c>log.md</c> write
    /// (either call-shape mode) whose content deviates from the activity-log format
    /// contract's expected shape — the write still commits (028-lint-at-scale US3, FR-016,
    /// SC-009, Clarifications 2026-08-27; contracts/log-prepend-write.md). Never emitted
    /// for a conforming write. Mandatory fields: <paramref name="agent"/>,
    /// <paramref name="mode"/>, <paramref name="path"/>, <paramref name="reason"/> (the
    /// comma-joined reason code(s), for a write that carried more than one),
    /// <paramref name="taskId"/>, <paramref name="turn"/> (Copilot review, PR #208: both
    /// were already available at every call site via <c>IToolCallInstrumentation</c> but
    /// went unused here, making a deviation harder to correlate to a specific run/turn than
    /// sibling events like <see cref="LogChangeNotLogged"/>).
    /// </summary>
    public static void LogFormatDeviation(
        ILogger logger,
        ActivitySource activitySource,
        string agent,
        string mode,
        string path,
        string reason,
        string taskId,
        int turn)
    {
        using var span = StartLogEventSpan(activitySource, FormatDeviationEvent.Name ?? "wiki.log.format_deviation", "Warning");
        span?.SetTag("agent", agent);
        span?.SetTag("mode", mode);
        span?.SetTag("path", path);
        span?.SetTag("reason", reason);
        span?.SetTag("task_id", taskId);
        span?.SetTag("turn", turn);

        logger.LogWarning(
            FormatDeviationEvent,
            "log.md write committed despite a format/ordering deviation. agent={agent} mode={mode} path={path} reason={reason} task_id={task_id} turn={turn}",
            agent,
            mode,
            path,
            reason,
            taskId,
            turn);
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

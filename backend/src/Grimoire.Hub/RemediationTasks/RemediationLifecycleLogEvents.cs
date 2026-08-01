using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// Hub-side structured log events for Remediation Action Task lifecycle broadcasts
/// (015-lint-board-parity T023, mirrors <c>LintLifecycleLogEvents</c>' split). The
/// plan.md ## Observability rows for the workflow transitions themselves
/// (<c>hub.remediation.task_authorized</c>/<c>task_dismissed</c>/... ) join here with
/// their US4/US5 endpoints (T033/T041); <c>hub.lint.remediation_task_proposed</c> is
/// emitted by <c>LintRunCoordinator</c>'s materialization and lives in
/// <c>LintLifecycleLogEvents</c> alongside the coordinator's other events.
/// </summary>
public static class RemediationLifecycleLogEvents
{
    private static readonly EventId LifecyclePublishedEvent = new(90, "remediation.lifecycle.published");

    /// <summary>
    /// One broadcast published on <c>/hubs/remediation-lifecycle</c> — mirrors
    /// <c>lint.lifecycle.published</c>'s per-broadcast log event (emitted by
    /// <see cref="RemediationLifecyclePublisher"/>).
    /// </summary>
    public static void LogLifecyclePublished(ILogger logger, string taskId, string? fromState, string toState)
    {
        using var span = StartLogEventSpan("remediation.lifecycle.published", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("from_state", fromState);
        span?.SetTag("to_state", toState);

        logger.LogInformation(LifecyclePublishedEvent,
            "Remediation task lifecycle published: {task_id} {from_state} -> {to_state}", taskId, fromState, toState);
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

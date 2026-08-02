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
    private static readonly EventId RunBlockedEvent = new(85, "lint.run.blocked");
    private static readonly EventId RemediationTaskProposedEvent = new(86, "hub.lint.remediation_task_proposed");

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
    /// 015-lint-board-parity T017 (FR-004/SC-004): trigger rejected because remediation
    /// action tasks from a prior run are still unresolved — sibling of
    /// <see cref="LogRunRejected"/>'s active-run rejection.
    /// </summary>
    public static void LogRunBlockedByUnresolvedTasks(ILogger logger, int unresolvedCount)
    {
        using var span = StartLogEventSpan("lint.run.blocked", "Information");
        span?.SetTag("unresolved_count", unresolvedCount);

        logger.LogInformation(RunBlockedEvent,
            "Lint run trigger blocked — {unresolved_count} remediation action task(s) from a prior run are unresolved.",
            unresolvedCount);
    }

    /// <summary>
    /// 015-lint-board-parity T022 (US3, FR-007; plan.md ## Observability > Structured Log
    /// Events): one remediation action task was created from a lint run's findings
    /// assessment — emitted per materialized proposal by
    /// <c>LintRunCoordinator.MaterializeProposedActionsAsync</c>.
    /// </summary>
    public static void LogRemediationTaskProposed(ILogger logger, string runId, string taskId)
    {
        using var span = StartLogEventSpan("hub.lint.remediation_task_proposed", "Information");
        span?.SetTag("run_id", runId);
        span?.SetTag("task_id", taskId);

        logger.LogInformation(RemediationTaskProposedEvent,
            "Remediation action task proposed. run_id={run_id} task_id={task_id}", runId, taskId);
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

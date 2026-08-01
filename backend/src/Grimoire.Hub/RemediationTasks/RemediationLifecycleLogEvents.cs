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

    private static readonly EventId TaskAuthorizedEvent = new(91, "hub.remediation.task_authorized");
    private static readonly EventId TaskDismissedEvent = new(92, "hub.remediation.task_dismissed");
    private static readonly EventId AuthorizationWithdrawnEvent = new(93, "hub.remediation.authorization_withdrawn");
    private static readonly EventId ExecutionStartedEvent = new(94, "hub.remediation.execution_started");
    private static readonly EventId ExecutionCompletedEvent = new(95, "hub.remediation.execution_completed");
    private static readonly EventId MessageRecordedEvent = new(96, "hub.remediation.message_recorded");

    /// <summary>plan.md ## Observability: a human authorizes a proposed task (T033, FR-009).</summary>
    public static void LogTaskAuthorized(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("hub.remediation.task_authorized", "Information");
        span?.SetTag("task_id", taskId);

        logger.LogInformation(TaskAuthorizedEvent, "Remediation task {task_id} authorized", taskId);
    }

    /// <summary>plan.md ## Observability: a human dismisses a proposed task (T033, FR-010).</summary>
    public static void LogTaskDismissed(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("hub.remediation.task_dismissed", "Information");
        span?.SetTag("task_id", taskId);

        logger.LogInformation(TaskDismissedEvent, "Remediation task {task_id} dismissed", taskId);
    }

    /// <summary>plan.md ## Observability: a human withdraws authorization before execution starts (T033, FR-016).</summary>
    public static void LogAuthorizationWithdrawn(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("hub.remediation.authorization_withdrawn", "Information");
        span?.SetTag("task_id", taskId);

        logger.LogInformation(AuthorizationWithdrawnEvent, "Remediation task {task_id} authorization withdrawn", taskId);
    }

    /// <summary>plan.md ## Observability: the coordinator dispatches an authorized task for execution (T032).</summary>
    public static void LogExecutionStarted(ILogger logger, string taskId)
    {
        using var span = StartLogEventSpan("hub.remediation.execution_started", "Information");
        span?.SetTag("task_id", taskId);

        logger.LogInformation(ExecutionStartedEvent, "Remediation task {task_id} execution started", taskId);
    }

    /// <summary>plan.md ## Observability: execution reaches a terminal outcome (T032). <paramref name="reason"/> is nullable except on failed/not_applicable.</summary>
    public static void LogExecutionCompleted(ILogger logger, string taskId, string outcome, string? reason)
    {
        using var span = StartLogEventSpan("hub.remediation.execution_completed", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("outcome", outcome);
        span?.SetTag("reason", reason);

        // T038: `reason` is passed through raw (not the display-friendly "n/a" fallback)
        // so the structured log field genuinely stays null for a plain `completed`
        // outcome, matching plan.md ## Observability's "reason nullable except on
        // failed/not_applicable" contract — ILogger's default formatter renders a null
        // template argument as an empty string in the human-readable message, which is
        // an acceptable trade for a correct structured field.
        logger.LogInformation(ExecutionCompletedEvent,
            "Remediation task {task_id} execution completed: {outcome} ({reason})", taskId, outcome, reason);
    }

    /// <summary>plan.md ## Observability: a task message (human or agent) is appended to the task's record (T041, FR-012).</summary>
    public static void LogMessageRecorded(ILogger logger, string taskId, string sender)
    {
        using var span = StartLogEventSpan("hub.remediation.message_recorded", "Information");
        span?.SetTag("task_id", taskId);
        span?.SetTag("sender", sender);

        logger.LogInformation(MessageRecordedEvent, "Remediation task {task_id} message recorded ({sender})", taskId, sender);
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

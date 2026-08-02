using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using IHubContext = Microsoft.AspNetCore.SignalR.IHubContext<Grimoire.Hub.RemediationTasks.RemediationLifecycleHub>;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// SignalR payload for one Remediation Action Task state transition (015-lint-board-parity
/// T023, contracts/remediation-lifecycle-events.md `remediationTaskLifecycleChanged`).
/// One event per transition, including materialization (<c>FromState: null →
/// "proposed"</c>) and queue-position changes while waiting. <c>QueuePosition</c> is
/// present only when <c>ToState = authorized</c> (FR-017); <c>OutcomeReason</c> is
/// required when <c>ToState</c> is <c>failed</c> or <c>not_applicable</c>
/// (FR-005/FR-018/SC-007). Clients apply events idempotently by (EventId, TaskId);
/// latest timestamp per task is authoritative.
/// </summary>
public sealed record RemediationTaskLifecycleEvent(
    string EventId,
    string TaskId,
    string RunId,
    string? FromState,
    string ToState,
    DateTimeOffset Timestamp,
    int? QueuePosition,
    string? OutcomeReason);

/// <summary>
/// SignalR payload for one message-turn lifecycle transition (015-lint-board-parity T041,
/// contracts/remediation-lifecycle-events.md `remediationMessageTurnChanged`). Clients
/// re-fetch <c>GET /api/remediation-tasks/{taskId}/messages</c> on <c>completed</c> to
/// render the agent's reply — the reply text itself never rides this event (Principle V:
/// the Hub only ever transports it, via the record).
/// </summary>
public sealed record RemediationMessageTurnChangedEvent(
    string EventId,
    string TaskId,
    string MessageTurnId,
    string State,
    DateTimeOffset Timestamp,
    string? FailureReason);

/// <summary>
/// Publishes Remediation Action Task lifecycle transitions to connected board clients
/// over <see cref="RemediationLifecycleHub"/> (T023, mirrors
/// <c>LintLifecyclePublisher</c>/<c>IngestLifecyclePublisher</c>). Every call emits
/// exactly one <c>remediationTaskLifecycleChanged</c> event, the
/// <c>remediation.lifecycle.published</c> structured log event, the
/// <c>hub.remediation_lifecycle_updates_total{stage}</c> counter, and the
/// <c>hub.remediation_lifecycle.publish_update</c> trace span. T041 adds
/// <see cref="PublishMessageTurnChangedAsync"/> for the sibling
/// <c>remediationMessageTurnChanged</c> channel (FR-012).
/// </summary>
public sealed class RemediationLifecyclePublisher
{
    private readonly IHubContext _hubContext;
    private readonly ILogger<RemediationLifecyclePublisher> _logger;

    public RemediationLifecyclePublisher(IHubContext hubContext, ILogger<RemediationLifecyclePublisher>? logger = null)
    {
        _hubContext = hubContext;
        _logger = logger ?? NullLogger<RemediationLifecyclePublisher>.Instance;
    }

    public async Task PublishTaskChangedAsync(
        string taskId,
        string runId,
        string? fromState,
        string toState,
        int? queuePosition = null,
        string? outcomeReason = null,
        CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.remediation_lifecycle.publish_update");
        span?.SetTag("task_id", taskId);
        span?.SetTag("stage", toState);

        var lifecycleEvent = new RemediationTaskLifecycleEvent(
            EventId: Guid.NewGuid().ToString("N"),
            TaskId: taskId,
            RunId: runId,
            FromState: fromState,
            ToState: toState,
            Timestamp: DateTimeOffset.UtcNow,
            QueuePosition: queuePosition,
            OutcomeReason: outcomeReason);

        await _hubContext.Clients.All.SendAsync("remediationTaskLifecycleChanged", lifecycleEvent, cancellationToken);

        HubMetrics.RecordRemediationLifecycleUpdate(toState);

        RemediationLifecycleLogEvents.LogLifecyclePublished(_logger, taskId, fromState, toState);
    }

    /// <summary>
    /// T041 (US5, FR-012): broadcasts one message-turn lifecycle transition
    /// (<c>running</c>/<c>completed</c>/<c>failed</c>) on
    /// <c>remediationMessageTurnChanged</c>. Root span <c>hub.remediation.message_turn</c>
    /// is started by the caller (<see cref="RemediationMessageTurnCoordinator"/>), not
    /// here — this method's own span mirrors <see cref="PublishTaskChangedAsync"/>'s
    /// per-broadcast shape.
    /// </summary>
    public async Task PublishMessageTurnChangedAsync(
        string taskId,
        string messageTurnId,
        string state,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.remediation_lifecycle.publish_update");
        span?.SetTag("task_id", taskId);
        span?.SetTag("stage", $"message_turn_{state}");

        var turnEvent = new RemediationMessageTurnChangedEvent(
            EventId: Guid.NewGuid().ToString("N"),
            TaskId: taskId,
            MessageTurnId: messageTurnId,
            State: state,
            Timestamp: DateTimeOffset.UtcNow,
            FailureReason: failureReason);

        await _hubContext.Clients.All.SendAsync("remediationMessageTurnChanged", turnEvent, cancellationToken);
    }
}

using System.Diagnostics.Metrics;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.OperationalState;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using IHubContext = Microsoft.AspNetCore.SignalR.IHubContext<Grimoire.Hub.Realtime.IngestLifecycleHub>;
using Grimoire.Hub.IngestDispatch;

namespace Grimoire.Hub.Realtime;

/// <summary>
/// Publishes Task Artifact lifecycle transitions to connected board clients over
/// <see cref="IngestLifecycleHub"/> (contracts/ingest-lifecycle-events.md). Every call emits
/// exactly one `taskLifecycleChanged` event, the `ingest.lifecycle.published` structured log
/// event (plan.md Observability), the `hub.ingest_lifecycle_updates_total` counter, and the
/// `hub.ingest_lifecycle.publish_update` trace span (child of `hub.ingest_submission.submit`).
/// </summary>
public sealed class IngestLifecyclePublisher
{
    private static readonly Counter<long> _lifecycleUpdatesTotal =
        HubMetrics.Meter.CreateCounter<long>("hub.ingest_lifecycle_updates_total",
            description: "Realtime ingest lifecycle events published");

    private readonly IHubContext _hubContext;
    private readonly OperationalStateRepository? _stateRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestLifecyclePublisher> _logger;

    public IngestLifecyclePublisher(
        IHubContext hubContext,
        ILogger<IngestLifecyclePublisher>? logger = null,
        OperationalStateRepository? stateRepository = null,
        TimeProvider? timeProvider = null)
    {
        _hubContext = hubContext;
        _stateRepository = stateRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<IngestLifecyclePublisher>.Instance;
    }

    /// <summary>
    /// Publishes one transition and — 023-task-ui-improvements T004 (FR-005, ADR-025) —
    /// records it in the append-only status history first. This method is the Hub's single
    /// choke point for ingest transitions (pipeline stages, coordinator terminal states,
    /// and the three history-only statuses alike), which makes it the one place history has
    /// to be written from: a single writer, in call order, with the agent process never
    /// touching the table (ADR-003). A failed history append MUST NOT swallow the realtime
    /// event — bookkeeping is a diagnostic, the board update is the product behavior — so
    /// the append is logged and the publish continues.
    /// </summary>
    /// <param name="historyDetail">
    /// Context for the history row when it is not simply the failure reason — e.g.
    /// "attempt 1; next retry in 10s" for a <c>liveness_interrupted</c> entry. Falls back
    /// to <paramref name="failureReason"/>, so existing callers record what they already
    /// pass without change.
    /// </param>
    public async Task PublishAsync(
        string taskId, string? fromStatus, string toStatus, string? failureReason = null,
        CancellationToken cancellationToken = default, string? historyDetail = null)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.ingest_lifecycle.publish_update");
        span?.SetTag("task_id", taskId);
        span?.SetTag("stage", toStatus);

        if (_stateRepository is not null)
        {
            try
            {
                await _stateRepository.AppendStatusHistoryAsync(
                    taskId, toStatus, _timeProvider.GetUtcNow(), historyDetail ?? failureReason, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to append status history for {task_id} ({to_stage}); the lifecycle event is still published.",
                    taskId, toStatus);
            }
        }

        var lifecycleEvent = new RealtimeLifecycleEvent(
            EventId: Guid.NewGuid().ToString("N"),
            TaskId: taskId,
            FromStatus: fromStatus,
            ToStatus: toStatus,
            Timestamp: DateTimeOffset.UtcNow,
            FailureReason: failureReason);

        await _hubContext.Clients.All.SendAsync("taskLifecycleChanged", lifecycleEvent, cancellationToken);

        _lifecycleUpdatesTotal.Add(1, new KeyValuePair<string, object?>("stage", toStatus));

        using var logSpan = HubTracing.ActivitySource.StartActivity("ingest.lifecycle.published");
        logSpan?.SetTag("signal_type", "log");
        logSpan?.SetTag("event_name", "ingest.lifecycle.published");
        logSpan?.SetTag("level", "Information");
        logSpan?.SetTag("task_id", taskId);
        logSpan?.SetTag("from_stage", fromStatus);
        logSpan?.SetTag("to_stage", toStatus);

        _logger.LogInformation(new EventId(10, "ingest.lifecycle.published"),
            "Ingest lifecycle published: {task_id} {from_stage} -> {to_stage}", taskId, fromStatus, toStatus);
    }

    /// <summary>
    /// Publishes a debounced task-record change notification (006 FR-009/FR-010,
    /// contracts/task-record-changed-event.md). Called by <c>TaskRecordWatcher</c> — a
    /// watcher-initiated root span (no ambient HTTP request to parent to), correlated to
    /// its log event and metric via <paramref name="taskId"/>/the generated event id.
    /// </summary>
    public async Task PublishTaskRecordChangedAsync(string taskId, DateTimeOffset changedAt, CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.task_record.publish_change");
        var eventId = Guid.NewGuid().ToString("N");
        span?.SetTag("task_id", taskId);
        span?.SetTag("event_id", eventId);

        var changedEvent = new TaskRecordChangedEvent(eventId, taskId, changedAt);
        await _hubContext.Clients.All.SendAsync("taskRecordChanged", changedEvent, cancellationToken);

        HubMetrics.RecordTaskRecordChangeEvent();
        IngestSubmissionLogEvents.LogTaskRecordChangePublished(_logger, taskId, eventId, changedAt);
    }

    /// <summary>
    /// Publishes a live loop-activity update for a running task
    /// (contracts/ingest-submission-api-extension.md `run_activity`, FR-018/SC-011).
    /// Loop mechanics only — no wiki-content interpretation (Principle V).
    /// </summary>
    public async Task PublishRunActivityAsync(
        string taskId, IngestDispatch.RunActivitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.All.SendAsync("runActivityChanged", new
        {
            kind = "run_activity",
            taskId,
            modelTurns = snapshot.ModelTurns,
            toolCalls = snapshot.ToolCalls,
            toolCallsByName = snapshot.ToolCallsByName,
            currentAction = snapshot.CurrentAction,
        }, cancellationToken);
    }
}

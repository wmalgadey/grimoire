using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using IHubContext = Microsoft.AspNetCore.SignalR.IHubContext<Grimoire.Hub.Realtime.IngestLifecycleHub>;

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
    private readonly ILogger<IngestLifecyclePublisher> _logger;

    public IngestLifecyclePublisher(IHubContext hubContext, ILogger<IngestLifecyclePublisher>? logger = null)
    {
        _hubContext = hubContext;
        _logger = logger ?? NullLogger<IngestLifecyclePublisher>.Instance;
    }

    public async Task PublishAsync(string taskId, string? fromStatus, string toStatus, string? failureReason = null, CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.ingest_lifecycle.publish_update");
        span?.SetTag("task_id", taskId);
        span?.SetTag("stage", toStatus);

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
    /// Publishes a live loop-activity update for a running task
    /// (contracts/ingest-submission-api-extension.md `run_activity`, FR-018/SC-011).
    /// Loop mechanics only — no wiki-content interpretation (Principle V).
    /// </summary>
    public async Task PublishRunActivityAsync(
        string taskId, AgentDispatch.RunActivitySnapshot snapshot, CancellationToken cancellationToken = default)
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

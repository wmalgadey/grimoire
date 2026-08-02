using System.Diagnostics.Metrics;
using Grimoire.Hub.LintDispatch;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using IHubContext = Microsoft.AspNetCore.SignalR.IHubContext<Grimoire.Hub.Realtime.LintLifecycleHub>;

namespace Grimoire.Hub.Realtime;

/// <summary>
/// SignalR payload for one Lint Run state transition (015-lint-board-parity,
/// contracts/remediation-lifecycle-events.md `lintRunLifecycleChanged`). One event per
/// transition (<c>running</c> on trigger, then <c>completed</c> or <c>failed</c>),
/// however the run was triggered (SC-001); <c>FailureReason</c> is required when
/// <c>ToStatus = failed</c> (FR-005). Clients apply events idempotently by
/// (EventId, RunId); latest timestamp per run is authoritative.
/// </summary>
public sealed record LintRunLifecycleEvent(
    string EventId,
    string RunId,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset Timestamp,
    string? FailureReason);

/// <summary>
/// Publishes Lint Run lifecycle transitions to connected board clients over
/// <see cref="LintLifecycleHub"/> (T011, mirrors <see cref="IngestLifecyclePublisher"/>).
/// Every call emits exactly one <c>lintRunLifecycleChanged</c> event, the
/// <c>lint.lifecycle.published</c> structured log event, the
/// <c>hub.lint_lifecycle_updates_total</c> counter, and the
/// <c>hub.lint_lifecycle.publish_update</c> trace span.
/// </summary>
public sealed class LintLifecyclePublisher
{
    private static readonly Counter<long> _lifecycleUpdatesTotal =
        HubMetrics.Meter.CreateCounter<long>("hub.lint_lifecycle_updates_total",
            description: "Realtime lint run lifecycle events published");

    private readonly IHubContext _hubContext;
    private readonly ILogger<LintLifecyclePublisher> _logger;

    public LintLifecyclePublisher(IHubContext hubContext, ILogger<LintLifecyclePublisher>? logger = null)
    {
        _hubContext = hubContext;
        _logger = logger ?? NullLogger<LintLifecyclePublisher>.Instance;
    }

    public async Task PublishRunChangedAsync(
        string runId, string? fromStatus, string toStatus, string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.lint_lifecycle.publish_update");
        span?.SetTag("run_id", runId);
        span?.SetTag("stage", toStatus);

        var lifecycleEvent = new LintRunLifecycleEvent(
            EventId: Guid.NewGuid().ToString("N"),
            RunId: runId,
            FromStatus: fromStatus,
            ToStatus: toStatus,
            Timestamp: DateTimeOffset.UtcNow,
            FailureReason: failureReason);

        await _hubContext.Clients.All.SendAsync("lintRunLifecycleChanged", lifecycleEvent, cancellationToken);

        _lifecycleUpdatesTotal.Add(1, new KeyValuePair<string, object?>("stage", toStatus));

        LintLifecycleLogEvents.LogLifecyclePublished(_logger, runId, fromStatus, toStatus);
    }
}

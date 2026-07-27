using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using IHubContext = Microsoft.AspNetCore.SignalR.IHubContext<Grimoire.Hub.Realtime.QueryLifecycleHub>;

namespace Grimoire.Hub.Realtime;

/// <summary>SignalR payload for one streamed answer delta (contracts/query-conversation-api.md).</summary>
public sealed record QueryAnswerChunkEvent(string TurnId, int Sequence, string Text);

/// <summary>SignalR payload for one Query Turn state transition (contracts/query-conversation-api.md).</summary>
public sealed record QueryTurnChangedEvent(
    string EventId,
    string TurnId,
    string FromState,
    string ToState,
    DateTimeOffset Timestamp,
    string? FailureReason);

/// <summary>
/// Publishes streamed answers and turn-state transitions to connected query clients over
/// <see cref="QueryLifecycleHub"/> — sibling to <see cref="IngestLifecyclePublisher"/>,
/// structurally independent (research.md R8).
/// </summary>
public sealed class QueryLifecyclePublisher
{
    private readonly IHubContext _hubContext;
    private readonly ILogger<QueryLifecyclePublisher> _logger;

    public QueryLifecyclePublisher(IHubContext hubContext, ILogger<QueryLifecyclePublisher>? logger = null)
    {
        _hubContext = hubContext;
        _logger = logger ?? NullLogger<QueryLifecyclePublisher>.Instance;
    }

    /// <summary>Broadcasts one streamed answer delta in <paramref name="sequence"/> order (contracts).</summary>
    public async Task PublishAnswerChunkAsync(string turnId, int sequence, string text, CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.query_lifecycle.publish_update");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("stage", "answer_chunk");

        await _hubContext.Clients.All.SendAsync(
            "queryAnswerChunk", new QueryAnswerChunkEvent(turnId, sequence, text), cancellationToken);
    }

    /// <summary>Broadcasts one turn-state transition and emits the accompanying structured log event.</summary>
    public async Task PublishTurnChangedAsync(
        string turnId, string fromState, string toState, string? failureReason, CancellationToken cancellationToken = default)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.query_lifecycle.publish_update");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("stage", toState);

        var changedEvent = new QueryTurnChangedEvent(
            EventId: Guid.NewGuid().ToString("N"),
            TurnId: turnId,
            FromState: fromState,
            ToState: toState,
            Timestamp: DateTimeOffset.UtcNow,
            FailureReason: failureReason);

        await _hubContext.Clients.All.SendAsync("queryTurnChanged", changedEvent, cancellationToken);

        using var logSpan = HubTracing.ActivitySource.StartActivity("query.lifecycle.published");
        logSpan?.SetTag("signal_type", "log");
        logSpan?.SetTag("event_name", "query.lifecycle.published");
        logSpan?.SetTag("level", "Information");
        logSpan?.SetTag("turn_id", turnId);
        logSpan?.SetTag("from_state", fromState);
        logSpan?.SetTag("to_state", toState);

        _logger.LogInformation(new EventId(55, "query.lifecycle.published"),
            "Query lifecycle published: {turn_id} {from_state} -> {to_state}", turnId, fromState, toState);
    }
}

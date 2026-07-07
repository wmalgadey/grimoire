namespace Grimoire.Hub.Realtime;

/// <summary>
/// SignalR payload for one Task Artifact lifecycle transition (data-model.md RealtimeLifecycleEvent,
/// contracts/ingest-lifecycle-events.md). Events are append-only and ordered by timestamp per
/// <see cref="TaskId"/>; clients apply them idempotently by (EventId, TaskId).
/// </summary>
public sealed record RealtimeLifecycleEvent(
    string EventId,
    string TaskId,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset Timestamp,
    string? FailureReason);

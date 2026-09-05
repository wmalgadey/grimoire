namespace Grimoire.Hub.Realtime;

/// <summary>
/// SignalR payload for one Task Artifact lifecycle transition (data-model.md IngestRealtimeLifecycleEvent,
/// contracts/ingest-lifecycle-events.md). Events are append-only and ordered by timestamp per
/// <see cref="TaskId"/>; clients apply them idempotently by (EventId, TaskId).
/// </summary>
public sealed record IngestRealtimeLifecycleEvent(
    string EventId,
    string TaskId,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset Timestamp,
    string? FailureReason);

/// <summary>
/// SignalR payload published when a task record's markdown file changes
/// (contracts/task-record-changed-event.md, ADR-008 event-shape conventions; sibling of
/// <see cref="IngestRealtimeLifecycleEvent"/> on the same <see cref="IngestLifecycleHub"/>).
/// Carries no record content — consumers refetch <c>GET
/// /api/ingest-submissions/{taskId}/task-record</c>. Debounced per <see cref="TaskId"/>
/// (300ms window) by <c>IngestTaskRecordWatcher</c>.
/// </summary>
public sealed record IngestTaskRecordChangedEvent(
    string EventId,
    string TaskId,
    DateTimeOffset ChangedAt);

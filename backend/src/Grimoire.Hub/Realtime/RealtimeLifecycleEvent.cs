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

/// <summary>
/// SignalR payload published when a task record's markdown file changes
/// (contracts/task-record-changed-event.md, ADR-008 event-shape conventions; sibling of
/// <see cref="RealtimeLifecycleEvent"/> on the same <see cref="IngestLifecycleHub"/>).
/// Carries no record content — consumers refetch <c>GET
/// /api/ingest-submissions/{taskId}/task-record</c>. Debounced per <see cref="TaskId"/>
/// (300ms window) by <c>TaskRecordWatcher</c>.
/// </summary>
public sealed record TaskRecordChangedEvent(
    string EventId,
    string TaskId,
    DateTimeOffset ChangedAt);

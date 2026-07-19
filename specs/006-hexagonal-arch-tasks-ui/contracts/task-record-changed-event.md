# Contract: `taskRecordChanged` Realtime Event

**Feature**: 006-hexagonal-arch-tasks-ui

Published on the existing SignalR hub `/hubs/ingest-lifecycle` (ADR-001, ADR-008 event
shapes; sibling of `taskLifecycleChanged` and `runActivityChanged`).

## Event: `taskRecordChanged`

```json
{
  "eventId": "b7e4c1a09f4e4d0f9a1e6c2d8b3f5a71",
  "taskId": "ingest-98e24a102de24084a924ed327a292b77",
  "changedAt": "2026-07-19T09:12:44.1230000Z"
}
```

### Semantics

- Emitted when the Hub's task-record watcher observes a change to `<TasksDir>/{taskId}.md`
  (any writer: Hub pipeline stage or agent process).
- Debounced per `taskId` (300 ms window): rapid successive writes coalesce into one event
  carrying the latest observation time.
- Carries no record content. Consumers MUST refetch
  `GET /api/ingest-submissions/{taskId}/task-record`.
- Delivery is best-effort broadcast (same guarantees as existing lifecycle events).
  Consumers MUST refetch on SignalR reconnect to resynchronize after gaps (FR-010).
- Temp files (`.*.tmp`) in `TasksDir` never produce events (writer temp-name convention).

### Observability contract

Every publish emits the `task_record.change_published` log event, increments
`hub.task_record_change_events_total`, and runs inside the
`hub.task_record.publish_change` span with `task_id` correlation.

Watcher failures (watch handle lost, IO error) emit `task_record.watch_failed` (WARN)
with `path` and `reason`; the watcher restarts itself.

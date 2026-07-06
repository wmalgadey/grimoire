# Contract: Ingest Lifecycle Events

SignalR contract for ingest lifecycle updates consumed by the Kanban board.

## Hub

- Hub route: `/hubs/ingest-lifecycle`
- Event channel: `taskLifecycleChanged`

## Event Payload

```json
{
  "eventId": "evt_01J0ABCXYZ",
  "taskId": "2026-07-06-ingest-example",
  "fromStatus": "converting",
  "toStatus": "queued",
  "timestamp": "2026-07-06T10:42:05Z",
  "failureReason": null
}
```

## Rules

- Server emits one event for each state transition.
- Clients apply events idempotently by `(eventId, taskId)`.
- Latest timestamp per `taskId` is authoritative.
- On reconnect, client refreshes via `GET /api/ingest-submissions` and then resumes stream.
- `failureReason` is required when `toStatus = failed`.

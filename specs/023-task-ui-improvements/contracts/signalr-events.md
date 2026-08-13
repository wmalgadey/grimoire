# SignalR Contract: Task Visibility & Recovery Improvements

**Feature**: `023-task-ui-improvements` | Hub: `/hubs/ingest-lifecycle` (existing)

No new event types. Deltas to existing events only.

## `taskLifecycleChanged` (existing event, extended usage)

Emitted, as today, on every board-stage transition — and now ALSO on the three history-only transitions so the detail view refreshes live:

- `liveness_interrupted` (with `attempt` in the payload detail)
- `reactivated`
- `restarted`

Contract rules:

- The event's `status` field for history-only transitions carries the history status value; **board consumers MUST ignore statuses outside the six `LifecycleStage` values for column placement** (the task's column is unchanged by history-only events — clarification 2026-08-13: no new board columns). The task stays visually in `running` during interruption/reactivation cycles and in `failed` until a restart's `queued` event arrives.
- The detail page treats any `taskLifecycleChanged` for its task as a trigger to re-fetch `statusHistory` from the detail endpoint (history is endpoint-authoritative; no history rows travel over SignalR).
- After a restart, the regular `queued` → `running` → terminal events flow exactly as for a first run.

## `runActivityChanged`, `taskRecordChanged` (existing)

Unchanged. `runActivityChanged` continues during reactivated runs (activity resets on re-launch).

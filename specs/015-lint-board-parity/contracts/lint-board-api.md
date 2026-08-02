# Contract: Lint Trigger Extension & Board Initial State

Extensions to the existing lint API (specs/013-lint-agent) and the board's
initial-state surface for feature 015. Task workflow endpoints:
[remediation-task-api.md](./remediation-task-api.md). Realtime events:
[remediation-lifecycle-events.md](./remediation-lifecycle-events.md). Entity
shapes: [data-model.md](../data-model.md).

## POST /api/lint-runs — extended trigger precondition (FR-004)

Route, method, empty request body, and `202 Accepted` response are unchanged
from 013 (`LintSubmissionEndpoints.PostTriggerAsync`). The trigger keeps its
reject-immediately shape (no queueing — research.md R2); what changes is the
precondition: a run is now also rejected while **any remediation action task
from a prior run has not reached a terminal outcome** (`completed`, `failed`,
`not_applicable`, or `dismissed`).

### Response (202 Accepted — unchanged)

```json
{
  "runId": "2026-08-01-lint-9f8e7d",
  "status": "running",
  "triggeredAt": "2026-08-01T09:00:00Z"
}
```

### Response (409 Conflict — two distinguishable reasons, SC-004)

The `reason` code tells the user *why* the trigger was blocked — never a
silent no-op, and never one generic "busy":

Existing reason, unchanged:

```json
{
  "reason": "lint_run_active",
  "message": "A Lint Run is already active. Wait for it to finish before triggering another."
}
```

New reason:

```json
{
  "reason": "unresolved_remediation_tasks",
  "message": "Remediation action tasks from the previous lint run are still unresolved. Authorize, dismiss, or wait for them to finish before starting a new run.",
  "unresolvedTaskIds": [
    "2026-08-01-remediation-a1b2c3",
    "2026-08-01-remediation-d4e5f6"
  ]
}
```

- `unresolvedTaskIds`: the blocking tasks, so the board can link straight to
  the cards that need a decision. Present only with this reason.
- When both conditions hold, `lint_run_active` wins (the run being active is
  the more immediate fact; its completion may still add proposals).
- **Trigger-at-completion race** (spec Edge Cases): accept-vs-reject is
  decided atomically against the run state's first-transition-wins arbiter
  (`LintRunState.TryTransitionTo` precedent) — the caller always gets either
  a definitive `202` (new run exists) or a definitive `409` naming the reason;
  never an ambiguous outcome. A client that saw the terminal broadcast for the
  previous run and still gets `lint_run_active` simply retries.

The frontend `lintApi.ts` `REASON_MESSAGES` map gains the
`unresolved_remediation_tasks` entry (same human-readable-mapping pattern as
`lint_run_active`).

## Lint run completion semantics (FR-007, informative)

"Completed" now means **assessment done and every proposed task on the
board**: the Hub materializes one `proposed` remediation task row per entry of
the terminal event's `proposedActions`
([remediation-lifecycle-events.md](./remediation-lifecycle-events.md)) *before*
committing and broadcasting the run's `completed` transition. Clients may
therefore fetch `GET /api/remediation-tasks?runId=...` immediately upon seeing
`completed` and rely on the full set being present. No API shape changes from
this — it is an ordering guarantee.

## GET /api/board — composite initial state (new)

The board's single initial-state/reconnect recovery source (research.md R9):
one response carrying all three entry kinds, explicitly typed. Fetched on
board load and on every SignalR reconnect, before resuming the three
lifecycle streams (same bootstrap-then-stream rule as
`createBoardLifecycleStream` today).

### Response (200 OK)

```json
{
  "entries": [
    {
      "kind": "ingest_task",
      "taskId": "2026-08-01-ingest-example",
      "status": "queued",
      "title": "Example submission",
      "updatedAt": "2026-08-01T08:59:00Z",
      "failureReason": null,
      "taskLink": "/api/ingest-submissions/2026-08-01-ingest-example"
    },
    {
      "kind": "lint_run",
      "runId": "2026-08-01-lint-9f8e7d",
      "status": "running",
      "triggeredAt": "2026-08-01T09:00:00Z",
      "completedAt": null,
      "failureReason": null,
      "hasFindingsReport": false
    },
    {
      "kind": "remediation_task",
      "taskId": "2026-08-01-remediation-a1b2c3",
      "runId": "2026-08-01-lint-9f8e7d",
      "title": "Add missing tags to runtime-paths page",
      "state": "proposed",
      "proposedAt": "2026-08-01T09:04:00Z",
      "queuePosition": null,
      "outcomeReason": null,
      "updatedAt": "2026-08-01T09:04:00Z"
    }
  ]
}
```

- `kind` is the discriminator (`ingest_task` | `lint_run` |
  `remediation_task`) enabling FR-006's at-a-glance distinguishability —
  the frontend maps kinds to distinct card components.
- `ingest_task` entries carry **exactly** the field set of today's
  `GET /api/ingest-submissions` rows (sourced verbatim from the unchanged
  `KanbanBoardProjection` store), plus the `kind` discriminator that this new
  endpoint adds to every entry.
- `lint_run` entries carry the field set of `GET /api/lint-runs/{runId}`.
  At most the latest run appears (one active run at a time; the board shows
  current lint status, the `/lint` page remains the historical/report view).
  When no lint run has ever been triggered, no `lint_run` entry is present —
  the board renders its "no lint activity yet" state with the trigger control
  (spec US1 scenario 1).
- `remediation_task` entries carry the list-entry field set of
  `GET /api/remediation-tasks` (minus `description`/`targetPath` bulk detail —
  the card links to the detail endpoint), including non-terminal **and**
  terminal tasks so outcomes stay visible on the board.

## What does NOT change (FR-015 / SC-008)

- **`GET /api/ingest-submissions`** (board rows), **`POST`** submission, and
  all ingest detail/task-record endpoints: untouched — same routes, same
  response fields, no new fields on ingest rows. Existing ingest clients and
  tests run unmodified.
- **Existing lint endpoints**: `GET /api/lint-runs/latest`,
  `GET /api/lint-runs/{runId}`, `GET /api/lint-runs/{runId}/findings` — routes
  and response shapes unchanged; they remain the recovery/report surface for
  the `/lint` page.
- **`/hubs/ingest-lifecycle`** and its `taskLifecycleChanged` /
  `runActivityChanged` / `taskRecordChanged` events: untouched (research.md
  R1 — lint and remediation get their own hubs precisely so this one is never
  modified).
- The dedicated `/lint` page may keep polling `GET /api/lint-runs/{runId}`;
  the board uses push via the new hubs instead.

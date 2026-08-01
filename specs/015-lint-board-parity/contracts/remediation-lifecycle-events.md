# Contract: Lint & Remediation Lifecycle Events

Realtime SignalR contract for the two new per-domain hubs (research.md R1,
mirrors `specs/003-ingest-intake-webui/contracts/ingest-lifecycle-events.md`),
plus the extension to the agent NDJSON event vocabulary
(`specs/agent-run-events.md`, ADR-008) that carries proposals and the
remediation execution outcome. Task/entity shapes:
[data-model.md](../data-model.md).

## Hub 1: Lint lifecycle

- Hub route: `/hubs/lint-lifecycle` (`LintLifecycleHub`, broadcast-only — no
  server-invokable methods, published to via `LintLifecyclePublisher`;
  mirrors `IngestLifecycleHub`/`IngestLifecyclePublisher`)
- Event channel: `lintRunLifecycleChanged`

### Payload

```json
{
  "eventId": "evt_01J0ABCXYZ",
  "runId": "2026-08-01-lint-9f8e7d",
  "fromStatus": "running",
  "toStatus": "completed",
  "timestamp": "2026-08-01T09:04:10Z",
  "failureReason": null
}
```

- One event per lint run state transition (`running` on trigger, then
  `completed` or `failed`), however the run was triggered — board or `/lint`
  page (spec edge case: runs triggered elsewhere appear on the board, SC-001).
- `failureReason` is required when `toStatus = failed` (FR-005).
- The `completed` event is broadcast only **after** all proposed remediation
  task rows exist (FR-007 ordering guarantee — see
  [lint-board-api.md](./lint-board-api.md)); each of those rows has already
  produced its own `remediationTaskLifecycleChanged` (`toState: "proposed"`)
  event by then.

## Hub 2: Remediation lifecycle

- Hub route: `/hubs/remediation-lifecycle` (`RemediationLifecycleHub`,
  broadcast-only, published to via `RemediationLifecyclePublisher`)
- Event channels: `remediationTaskLifecycleChanged`,
  `remediationRunActivityChanged`, `remediationMessageTurnChanged`

### `remediationTaskLifecycleChanged`

One event per task state transition, including materialization
(`fromState: null → "proposed"`), withdrawal (`"authorized" → "proposed"`),
and queue-position changes while waiting.

```json
{
  "eventId": "evt_01J0DEFXYZ",
  "taskId": "2026-08-01-remediation-a1b2c3",
  "runId": "2026-08-01-lint-9f8e7d",
  "fromState": "authorized",
  "toState": "executing",
  "timestamp": "2026-08-01T09:07:00Z",
  "queuePosition": null,
  "outcomeReason": null
}
```

- States on the wire as in [remediation-task-api.md](./remediation-task-api.md):
  `proposed`, `authorized`, `executing`, `completed`, `failed`,
  `not_applicable`, `dismissed`.
- `queuePosition`: present only when `toState = authorized` (FR-017: waiting
  tasks visibly distinct from the executing one). When the executing task
  finishes and the queue advances, each remaining waiting task gets a fresh
  `authorized → authorized` event with its updated `queuePosition`.
- `outcomeReason`: required when `toState` is `failed` or `not_applicable`
  (FR-005/FR-018/SC-007) — the agent-reported reason, verbatim.
- The withdrawal race resolves to exactly one broadcast sequence: either
  `authorized → proposed` (withdrawal won) or `authorized → executing`
  (execution won) — never both (research.md R5); the board always shows which
  side won (spec Edge Cases).

### `remediationRunActivityChanged`

Live loop activity for the currently executing task — same shape and
semantics as ingest's `runActivityChanged`
(`IngestLifecyclePublisher.PublishRunActivityAsync`): loop mechanics only,
no wiki-content interpretation (Principle V).

```json
{
  "kind": "run_activity",
  "taskId": "2026-08-01-remediation-a1b2c3",
  "modelTurns": 3,
  "toolCalls": 5,
  "toolCallsByName": { "read_page": 4, "write_page": 1 },
  "currentAction": "Re-verifying proposal against wiki/runtime-paths.md"
}
```

### `remediationMessageTurnChanged`

Lifecycle of a message turn (FR-012). Clients re-fetch
`GET /api/remediation-tasks/{taskId}/messages` on `completed` to render the
agent's reply.

```json
{
  "eventId": "evt_01J0GHIXYZ",
  "taskId": "2026-08-01-remediation-a1b2c3",
  "messageTurnId": "2026-08-01-remtask-msg-b4c5d6",
  "state": "completed",
  "timestamp": "2026-08-01T09:03:41Z",
  "failureReason": null
}
```

- `state`: `running` | `completed` | `failed`; `failureReason` required when
  `failed` (a failed turn appends no agent message — the failure is shown in
  the task's message UI, not silently swallowed).

## Rules (all channels)

- Clients apply events idempotently by `(eventId, taskId|runId)`.
- Latest `timestamp` per subject is authoritative; stale/out-of-order events
  are ignored (same rule as ingest lifecycle events).
- On reconnect, the client refreshes from `GET /api/board`
  ([lint-board-api.md](./lint-board-api.md)) — and the task detail/messages
  endpoints where open — before resuming the streams (spec edge case:
  connection drop must recover correct state, SC-002).
- The board holds three concurrent hub connections (ingest, lint,
  remediation); each follows the same client wrapper pattern as
  `ingestLifecycleClient.ts` (`lintLifecycleClient.ts`,
  `remediationLifecycleClient.ts`).
- `/hubs/ingest-lifecycle` is untouched by this feature (FR-015).

## NDJSON agent event vocabulary extension (ADR-008)

The stdout event channel keeps its `started` / `heartbeat` / `activity` /
`completed` / `failed` vocabulary and liveness-window supervision for all
three Lint-agent invocation modes (lint run, remediation execution, message
turn — research.md R8). Two optional fields are added to the `completed`
terminal event, following the `deniedActions`/`createdPages` precedent of
structured terminal metadata:

### `proposedActions` — lint-run mode terminal event (FR-007)

```json
{
  "type": "completed",
  "taskId": "2026-08-01-lint-9f8e7d",
  "timestamp": "2026-08-01T09:04:09Z",
  "summary": "Lint completed: 12 pages checked, 2 actionable findings.",
  "proposedActions": [
    {
      "title": "Add missing tags to runtime-paths page",
      "description": "The page wiki/runtime-paths.md has no tags frontmatter. Propose adding tags: [configuration, paths].",
      "targetPath": "wiki/runtime-paths.md"
    }
  ]
}
```

- `title` and `description` required per entry; `targetPath` optional. All
  three are agent-authored and harness-opaque: the Hub materializes one
  `proposed` task row per entry **verbatim** — it never filters, merges,
  rewrites, or scope-checks proposals (Principle V; an over-scope proposal
  simply fails later at the guard, research.md R7).
- Absent or empty `proposedActions` ⇒ no tasks created (a clean run — spec
  US3 scenario 2). Not an error.
- Emitted only by the lint-run mode; other modes never carry it.

### `remediationOutcome` — remediation-execution mode terminal event (FR-018, ADR-018)

```json
{
  "type": "completed",
  "taskId": "2026-08-01-remediation-a1b2c3",
  "timestamp": "2026-08-01T09:09:30Z",
  "summary": "Tags already present; proposal is moot.",
  "remediationOutcome": "not_applicable",
  "reason": "The page gained a tags list after this action was proposed; re-verification found nothing left to change."
}
```

- `remediationOutcome`: `applied` | `not_applicable`. The agent's
  execution-time re-verification judgment (FR-018) — the harness only
  transports it, never computes it.
- Hub mapping to the task's terminal state: `completed` +
  `remediationOutcome: "not_applicable"` ⇒ `not_applicable` (with `reason`,
  which is **required** for this value and surfaced as the task's
  `outcomeReason`); `completed` otherwise (field absent or `applied`) ⇒
  `completed`; `failed` event ⇒ `failed` (existing `reason` field ⇒
  `outcomeReason`), including guard-denied over-scope writes (ADR-016 reuse,
  research.md R7) and liveness-window failures.
- A `not_applicable` outcome involves **no** wiki write.

### Message-turn mode terminal event

No new field: the agent's reply travels in the existing `text` field of the
`completed` event (a message turn is a bounded single exchange, ADR-011 shape;
no `answer_chunk` streaming). The Hub appends it to the task record and
broadcasts `remediationMessageTurnChanged`.

### Backward compatibility

`AgentRunEventParser` is a tolerant parser: unknown fields are ignored, and
non-JSON/unknown-type lines never fail a run (only the liveness window does).
Both new fields are optional, so: an older Hub receiving a newer agent's
events ignores them harmlessly; a newer Hub receiving events without them
behaves as today (no proposals; execution outcome `completed`/`failed` as
before). No version negotiation is needed.

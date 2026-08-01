# Contract: Remediation Task API

HTTP endpoints for the Remediation Action Task workflow (FR-008..FR-014, FR-016,
FR-017), under ADR-018. Entity shapes and the task state machine:
[data-model.md](../data-model.md). All routes follow the existing
`/api/<resource>` Minimal-API route-group pattern (`LintSubmissionEndpoints`,
`IngestSubmissionEndpoints`); bodies are JSON, camelCase.

## Conventions

- **Task ids** are Hub-generated, opaque to clients (e.g.
  `2026-08-01-remediation-a1b2c3`), assigned when the Hub materializes a
  `Proposed` row from the lint run's `proposedActions`
  (contracts/remediation-lifecycle-events.md).
- **State values** on the wire: `proposed`, `authorized`, `executing`,
  `completed`, `failed`, `not_applicable`, `dismissed` (multi-word values are
  snake_case, matching existing reason-code style such as `lint_run_active`).
- **Errors are explicit, never silent** (SC-004 discipline applied to the whole
  workflow): every `409 Conflict` body carries a machine `reason` code, the
  task's **actual** current `state`, and a human-readable `message`. Unknown
  task ⇒ `404 Not Found` with `{ "message": "Remediation task '<id>' was not found." }`.
- **State transitions are compare-and-swap** on the persisted row
  (research.md R5, ADR-018): a transition endpoint succeeds only if the row is
  still in the expected source state at commit time. The loser of any race gets
  a `409` showing the state that actually won — the board renders that truth.

## GET /api/remediation-tasks

List all remediation action tasks — the board's initial-state recovery source
for remediation entries (spec edge case: reconnect/first-open must recover
correct state, including tasks proposed before this page load). Includes
terminal tasks; clients decide presentation.

### Query parameters

- `runId` (optional): restrict to tasks proposed by one lint run.

### Response (200 OK)

```json
{
  "tasks": [
    {
      "taskId": "2026-08-01-remediation-a1b2c3",
      "runId": "2026-08-01-lint-9f8e7d",
      "title": "Add missing tags to runtime-paths page",
      "description": "The page wiki/runtime-paths.md has no tags frontmatter. Propose adding tags: [configuration, paths].",
      "targetPath": "wiki/runtime-paths.md",
      "state": "authorized",
      "proposedAt": "2026-08-01T09:00:00Z",
      "authorizedAt": "2026-08-01T09:05:00Z",
      "queuePosition": 2,
      "outcomeReason": null,
      "updatedAt": "2026-08-01T09:05:00Z"
    }
  ]
}
```

- `title`/`description`/`targetPath`: **verbatim agent-authored proposal text**
  (Principle V — the harness never edits, filters, or rewrites it).
  `targetPath` is nullable.
- `queuePosition`: 1-based position among tasks waiting to execute; present
  only in state `authorized` (FR-017: a waiting task must be distinguishable
  from the executing one — `executing` state itself marks the active task).
- `outcomeReason`: non-null exactly when state is `failed` or `not_applicable`
  (FR-005/FR-018/SC-007); also carries nothing for `dismissed` (human decision,
  no agent reason).
- `authorizedAt`: null until first authorized; cleared back to null on
  withdrawal (it defines FIFO order per ADR-018, so only the *current*
  authorization counts).

## GET /api/remediation-tasks/{taskId}

Full task detail, including attached context (FR-011). Same fields as the list
entry, plus:

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "...": "…all list-entry fields…",
  "attachedContext": [
    {
      "content": "Use the tag taxonomy from wiki/index.md, not free-form tags.",
      "attachedAt": "2026-08-01T09:02:00Z"
    }
  ],
  "messageTurnActive": false
}
```

- `attachedContext`: append-only, in attach order; sourced from the task's
  Remediation Task Record (ADR-014 shape, see [data-model.md](../data-model.md)).
  Remains readable in every state including terminal ones (FR-014).
- `messageTurnActive`: true while a message turn (see below) is running for
  this task.

## POST /api/remediation-tasks/{taskId}/authorize

Authorize a proposed task: `proposed → authorized` (FR-009). No request body.
Authorization is the **only** thing that ever makes the task eligible for
execution dispatch (SC-005, ADR-018: dispatch precondition, not runtime check).

### Response (200 OK)

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "state": "authorized",
  "authorizedAt": "2026-08-01T09:05:00Z",
  "queuePosition": 2
}
```

### Error responses

- `409 Conflict` — task is not in `proposed`:

  ```json
  {
    "reason": "task_not_proposed",
    "state": "dismissed",
    "message": "Only a proposed task can be authorized. This task is dismissed."
  }
  ```

- `404 Not Found` — unknown task id.

## POST /api/remediation-tasks/{taskId}/dismiss

Dismiss a proposed task without the agent ever acting on it:
`proposed → dismissed` (FR-010, terminal, no wiki change, no agent
involvement). No request body.

### Response (200 OK)

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "state": "dismissed",
  "dismissedAt": "2026-08-01T09:06:00Z"
}
```

### Error responses

- `409 Conflict` — `{ "reason": "task_not_proposed", "state": "<actual>", "message": "..." }`
  (e.g. attempting to dismiss an already-authorized task; withdraw first).
- `404 Not Found` — unknown task id.

## POST /api/remediation-tasks/{taskId}/withdraw-authorization

Withdraw authorization while the task is still waiting: `authorized → proposed`
(FR-016). No request body. This is one side of the withdrawal race (spec Edge
Cases): the coordinator's `authorized → executing` transition and this
endpoint's `authorized → proposed` transition are competing compare-and-swap
commits on the same persisted row — **first commit wins** (research.md R5).

### Response (200 OK) — withdrawal won

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "state": "proposed"
}
```

The task rejoins the proposed pool; attached context and message history are
retained. Re-authorizing later assigns a **new** `authorizedAt` (it goes to the
back of the FIFO).

### Error responses

- `409 Conflict` — **the CAS race loser**: execution already started (or
  already finished) before the withdrawal committed. The body shows what
  actually happened, so the board is never left uncertain:

  ```json
  {
    "reason": "execution_already_started",
    "state": "executing",
    "message": "The agent already began executing this task; it will run to a terminal outcome and can no longer be cancelled."
  }
  ```

  `state` may also be a terminal value (`completed`, `failed`,
  `not_applicable`) when execution finished before the withdrawal arrived.

- `409 Conflict` — task is not currently authorized (e.g. a double-withdraw
  race; the first one already won):
  `{ "reason": "task_not_authorized", "state": "proposed", "message": "..." }`.
- `404 Not Found` — unknown task id.

## POST /api/remediation-tasks/{taskId}/context

Attach additional information or instructions to a task (FR-011). Allowed
**only while `proposed`** — once authorized, what was authorized is fixed;
withdraw first to add more context.

### Request

```json
{
  "content": "Use the tag taxonomy from wiki/index.md, not free-form tags."
}
```

- `content`: required, non-empty after trim, ≤ 8000 characters (same
  validation bounds as query prompts, `QuerySubmissionValidator` precedent).

### Response (200 OK)

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "attachedAt": "2026-08-01T09:02:00Z"
}
```

Appended to the task's Remediation Task Record; delivered to the execution run
as part of the ADR-007 per-run user-prompt override, and to message turns as
task context — the record is the single source, so what the human sees and
what the agent receives cannot diverge (ADR-014 rationale).

### Error responses

- `400 Bad Request` — empty/whitespace/oversized `content`; nothing attached.
- `409 Conflict` — `{ "reason": "task_not_proposed", "state": "<actual>", "message": "..." }`.
- `404 Not Found` — unknown task id.

## POST /api/remediation-tasks/{taskId}/messages

Send the agent a message about this specific task (FR-012). Non-blocking: the
Hub spawns a bounded, read-only message turn (Query-turn shape, ADR-011) whose
context is the task's finding, proposal, attached context, and prior messages
from the task record. Allowed only while `proposed` (messaging exists to steer
the proposal before authorization, spec US5).

### Request

```json
{
  "content": "Why do you think this page needs the 'configuration' tag?"
}
```

- `content`: required, non-empty after trim, ≤ 8000 characters.

### Response (202 Accepted)

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "messageTurnId": "2026-08-01-remtask-msg-b4c5d6",
  "state": "running",
  "acceptedAt": "2026-08-01T09:03:00Z"
}
```

The human message is appended to the record immediately; the agent's reply is
appended when the turn completes. Turn progress and completion are broadcast
on `remediationMessageTurnChanged`
(contracts/remediation-lifecycle-events.md) — clients re-fetch the message
history on `completed`.

### Error responses

- `400 Bad Request` — empty/whitespace/oversized `content`; no turn created.
- `409 Conflict` — `{ "reason": "message_turn_active", "state": "proposed", "message": "..." }`
  — one message turn at a time per task (mirrors
  `conversation_already_active`); the invariant that keeps the record complete
  at every accepted message.
- `409 Conflict` — `{ "reason": "task_not_proposed", "state": "<actual>", "message": "..." }`.
- `404 Not Found` — unknown task id.

## GET /api/remediation-tasks/{taskId}/messages

Message history for the task. Available in **every** state — including after a
terminal outcome, for later reference (FR-014); this endpoint never returns
409.

### Response (200 OK)

```json
{
  "taskId": "2026-08-01-remediation-a1b2c3",
  "messageTurnActive": false,
  "messages": [
    {
      "sender": "human",
      "content": "Why do you think this page needs the 'configuration' tag?",
      "timestamp": "2026-08-01T09:03:00Z"
    },
    {
      "sender": "agent",
      "content": "The page documents GrimoirePathOptions, which is configuration surface; the taxonomy in index.md files such pages under 'configuration'.",
      "timestamp": "2026-08-01T09:03:41Z"
    }
  ]
}
```

- `sender`: `human` | `agent`. Messages are in append order. A failed message
  turn appends no agent entry; the failure is surfaced via the
  `remediationMessageTurnChanged` broadcast (with reason), not silently
  dropped.
- `404 Not Found` — unknown task id. A task with no messages yet returns an
  empty `messages` array, not 404.

## Not part of this contract

- **Execution dispatch has no endpoint.** There is deliberately no
  `POST .../execute`: execution is triggered solely by the
  `RemediationRunCoordinator` dequeuing `authorized` rows in `authorizedAt`
  order (ADR-018 — SC-005 is structural because no other spawn path exists).
- **Lint-run triggering and the board initial-state response** are in
  [lint-board-api.md](./lint-board-api.md).

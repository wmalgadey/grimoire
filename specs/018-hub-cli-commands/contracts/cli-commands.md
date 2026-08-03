# CLI Command Contract: Grimoire Hub

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02, revised 2026-08-03

This is the externally observable contract of the Hub's command surface — the test
matrix for SC-002/SC-003 and the operator-facing reference. Message wording may be
polished during implementation, but each row's *distinguishability* (FR-007/FR-008),
its exit code, and the presence of the listed identifiers are binding.

## Global rules

- Commands execute **in-process against the Hub data directory** (Clarification
  2026-08-03) — no running Hub required. Every command accepts the ADR-009 path
  switches (`--base-dir`, …) exactly like the server mode.
- Commands are **blocking**: any agent work the flow spawns is supervised in the CLI
  process and awaited to its terminal state before exit. Live status/progress renders
  to **stderr**; **stdout** carries only the result contract below (FR-006).
- `--help`/`-h` anywhere in the arguments prints help and exits 0; no command runs
  (FR-011). Root help lists every command below plus the ADR-009 server path switches.
- Unknown first-argument command name → usage error naming the unknown command, exit 2.
- All commands run non-interactively (FR-012): no prompts, no confirmation.
- Error messages go to stderr.
- Exit codes: `0` success · `1` operation failed · `2` usage error · `3` not found ·
  `4` state conflict · `5` wait timeout (turn interrupted) · `130` cancelled (turn
  interrupted).

## Commands

### `lint-run`

Trigger a lint run and supervise it to completion. No arguments beyond path switches.
Flow: `LintRunCoordinator.TriggerAsync()` (acquires the exclusive `lint.pid` lock —
cross-process), then blocks until the run's terminal state.

| Outcome | Source | Output (stdout/stderr) | Exit |
| --- | --- | --- | --- |
| Run completed | terminal state `completed` | `Lint run {runId} started.` (status) … `Lint run {runId} completed. Findings report: {path}` | 0 |
| Run failed | terminal state `failed` | `Lint run {runId} failed: {failureReason}` | 1 |
| Run already active (this or another process) | in-process slot or `lint.pid` holder | `A lint run is already active.` | 4 |
| Unresolved remediation tasks | `Blocked` result | `Cannot start a lint run: {n} unresolved remediation task(s) block it: {ids}` | 4 |

### `remediation-authorize --task-id <id>`

Authorize a proposed remediation task via `RemediationTaskTransitionService` (same
flow as the endpoint, incl. eager dispatch). If the eager dispatch starts executing,
the CLI supervises the execution to its terminal state; if the remediation queue is
paused (fresh-process/restart semantics, ADR-018), the command exits after the
transition — identical to the HTTP flow in the same state.

| Outcome | Source | Output | Exit |
| --- | --- | --- | --- |
| Authorized (queue paused — no execution started) | CAS ok | `Remediation task {taskId} authorized at {authorizedAt:O}.` | 0 |
| Authorized + execution completed | terminal `completed`/`not_applicable` | `Remediation task {taskId} authorized at {authorizedAt:O}.` (status) … `Remediation task {taskId} {completed\|not applicable}{: reason}.` | 0 |
| Authorized + execution failed | terminal `failed` | `Remediation task {taskId} failed: {reason}` | 1 |
| Missing `--task-id` | — (no action) | usage error | 2 |
| Unknown task id | lookup miss | `Remediation task '{taskId}' was not found.` | 3 |
| Not in `proposed` state | `task_not_proposed` | `Remediation task {taskId} is not proposed (current state: {state}).` | 4 |

### `remediation-dismiss --task-id <id>`

Dismiss a proposed remediation task. No agent work — completes immediately.

| Outcome | Source | Output | Exit |
| --- | --- | --- | --- |
| Dismissed | CAS ok | `Remediation task {taskId} dismissed.` | 0 |
| Missing `--task-id` | — | usage error | 2 |
| Unknown task id | lookup miss | `Remediation task '{taskId}' was not found.` | 3 |
| Not in `proposed` state | `task_not_proposed` | `Remediation task {taskId} is not proposed (current state: {state}).` | 4 |

### `remediation-withdraw --task-id <id>`

Withdraw authorization (task returns to `proposed`). No agent work — completes
immediately.

| Outcome | Source | Output | Exit |
| --- | --- | --- | --- |
| Withdrawn | CAS ok | `Remediation task {taskId} authorization withdrawn (state: proposed).` | 0 |
| Missing `--task-id` | — | usage error | 2 |
| Unknown task id | lookup miss | `Remediation task '{taskId}' was not found.` | 3 |
| Execution already started (incl. lost race) | `execution_already_started` | `Remediation task {taskId} can no longer be withdrawn: execution already started (state: {state}).` | 4 |
| Not authorized | `task_not_authorized` | `Remediation task {taskId} is not authorized (current state: {state}).` | 4 |

### `ingest-retrigger --task-id <id>`

Re-arm a queued ingest task via `IngestRunCoordinator.RetriggerAsync` and supervise
the triggered processing until the task reaches a terminal state.

| Outcome | Source | Output | Exit |
| --- | --- | --- | --- |
| Retriggered + processed | terminal state | `Ingest task {taskId} retriggered.` (status) … `Ingest task {taskId} {completed\|failed}.` | 0 / 1 (failed) |
| Missing `--task-id` | — | usage error | 2 |
| Unknown task id | projection miss | `Task '{taskId}' was not found.` | 3 |
| Not in queue | `RetriggerAsync` → false | `Ingest task {taskId} is not in the queue ({column}).` | 4 |

### `ingest-resume`

Resume the ingest queue via `IngestRunCoordinator.ResumeAsync` and supervise until the
queue drains. Idempotent; prints the queued count up front.

| Outcome | Source | Output | Exit |
| --- | --- | --- | --- |
| Resumed, queue drained | queue empty | `Ingest queue resumed: {queuedTasks} task(s) queued.` (status) … `Ingest queue drained: {n} task(s) processed, {m} failed.` | 0 (also when individual tasks failed — per-task outcomes are queue state, listed on stderr) |

### `query <prompt> [--conversation-id <id>] [--timeout <seconds>]`

Submit a query turn via `QueryRunCoordinator.SubmitTurnAsync` and block until its
terminal state, streaming the accumulating answer to stderr while waiting.

- `--conversation-id` omitted → CLI generates one (`{yyyy-MM-dd}-conv-{guid}`, ≤ 40
  chars) and includes it in the header line.
- `--timeout` default 300; must be a positive integer. On expiry the CLI interrupts
  the turn (same `InterruptAsync` action as HTTP; partial answer persisted).
- Ctrl-C while waiting → same interrupt, distinct message and exit code.

| Outcome | Source | Output | Exit |
| --- | --- | --- | --- |
| Turn completed | terminal `completed` | Header: `Query turn {turnId} in conversation {conversationId}: completed` + answer text verbatim on following lines (stdout) | 0 |
| Turn failed | terminal `failed` | `Query turn {turnId} failed: {failureReason}` | 1 |
| Missing/empty prompt; malformed `--conversation-id`; non-positive `--timeout` | — (no action) | usage error | 2 |
| Concurrency limit reached | `ConcurrencyLimitReached` | `The Hub is at its query concurrency limit; try again later.` | 4 |
| Conversation already active | `ConversationAlreadyActive` | `Conversation {conversationId} already has an active turn.` | 4 |
| Conversation record unreadable | `RecordUnreadable` | `Conversation record for {conversationId} is unreadable: {reason}` | 1 |
| Wait timeout elapsed | interrupt invoked, turn `interrupted` | `Timed out after {timeout}s waiting for query turn {turnId}; the turn was interrupted and its partial answer persisted.` | 5 |
| Interrupt signal (Ctrl-C) while waiting | interrupt invoked, turn `interrupted` | `Cancelled: query turn {turnId} interrupted.` | 130 |

### `submit-source --path <path> [--source-kind <kind>]` (existing, parsing migrated)

Behavior, output (`Submitted ingest task: {taskId}`), and in-process run-to-exit
execution are unchanged; ADR-009 path switches remain accepted. Listed here because it
appears in the same root help and is dispatched by the same command framework.

## Help contract

- Root `--help`: FigletText logo, usage line, `Commands:` section listing all eight
  commands with descriptions, `Server options:` section listing every
  `PathSwitchCatalog.All` switch with description (017 parity preserved).
- `<command> --help`: that command's arguments/options with descriptions; no logo.

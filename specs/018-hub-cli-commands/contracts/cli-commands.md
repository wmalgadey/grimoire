# CLI Command Contract: Grimoire Hub

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02

This is the externally observable contract of the Hub's command surface — the test
matrix for SC-002/SC-003 and the operator-facing reference. Message wording may be
polished during implementation, but each row's *distinguishability* (FR-007/FR-008),
its exit code, and the presence of the listed identifiers are binding.

## Global rules

- `--help`/`-h` anywhere in the arguments prints help and exits 0; no command runs
  (FR-011). Root help lists every command below plus the ADR-009 server path switches.
- Unknown first-argument command name → usage error naming the unknown command, exit 2.
- All commands run non-interactively (FR-012): no prompts, no confirmation.
- Remote commands accept `--hub-url <url>` (default: `GRIMOIRE_HUB_URL` env var, else
  `http://localhost:5255`). Unreachable Hub →
  `Cannot reach the Grimoire Hub at {url}. Is the Hub running?` → exit 7.
- Success output is a single result line on stdout (FR-006); `query` appends the answer
  body after its header line (FR-017). Error messages go to stderr.
- Exit codes: `0` success · `1` operation failed · `2` usage error · `3` not found ·
  `4` state conflict · `5` wait timeout · `7` Hub unreachable · `130` cancelled.

## Commands

### `lint-run`

Trigger a new lint run. No arguments. → `POST /api/lint-runs/`

| Outcome | HTTP source | Output (stdout/stderr) | Exit |
| --- | --- | --- | --- |
| Run started | 202 | `Lint run {runId} started (status: running).` | 0 |
| Run already active | 409 `lint_run_active` | `A lint run is already active.` | 4 |
| Unresolved remediation tasks | 409 `unresolved_remediation_tasks` | `Cannot start a lint run: {n} unresolved remediation task(s) block it: {ids}` | 4 |

### `remediation-authorize --task-id <id>`

Authorize a proposed remediation task. → `POST /api/remediation-tasks/{id}/authorize`

| Outcome | HTTP source | Output | Exit |
| --- | --- | --- | --- |
| Authorized | 200 | `Remediation task {taskId} authorized at {authorizedAt:O}.` | 0 |
| Missing `--task-id` | — (no request) | usage error | 2 |
| Unknown task id | 404 | `Remediation task '{taskId}' was not found.` | 3 |
| Not in `proposed` state | 409 `task_not_proposed` | `Remediation task {taskId} is not proposed (current state: {state}).` | 4 |

### `remediation-dismiss --task-id <id>`

Dismiss a proposed remediation task. → `POST /api/remediation-tasks/{id}/dismiss`

| Outcome | HTTP source | Output | Exit |
| --- | --- | --- | --- |
| Dismissed | 200 | `Remediation task {taskId} dismissed.` | 0 |
| Missing `--task-id` | — | usage error | 2 |
| Unknown task id | 404 | `Remediation task '{taskId}' was not found.` | 3 |
| Not in `proposed` state | 409 `task_not_proposed` | `Remediation task {taskId} is not proposed (current state: {state}).` | 4 |

### `remediation-withdraw --task-id <id>`

Withdraw authorization (task returns to `proposed`).
→ `POST /api/remediation-tasks/{id}/withdraw-authorization`

| Outcome | HTTP source | Output | Exit |
| --- | --- | --- | --- |
| Withdrawn | 200 | `Remediation task {taskId} authorization withdrawn (state: proposed).` | 0 |
| Missing `--task-id` | — | usage error | 2 |
| Unknown task id | 404 | `Remediation task '{taskId}' was not found.` | 3 |
| Execution already started (incl. lost race) | 409 `execution_already_started` | `Remediation task {taskId} can no longer be withdrawn: execution already started (state: {state}).` | 4 |
| Not authorized | 409 `task_not_authorized` | `Remediation task {taskId} is not authorized (current state: {state}).` | 4 |

### `ingest-retrigger --task-id <id>`

Re-arm a queued ingest task. → `POST /api/ingest-submissions/{id}/retrigger`

| Outcome | HTTP source | Output | Exit |
| --- | --- | --- | --- |
| Retriggered | 200 | `Ingest task {taskId} retriggered.` | 0 |
| Missing `--task-id` | — | usage error | 2 |
| Unknown task id | 404 | `Task '{taskId}' was not found.` | 3 |
| Not in queue | 409 | `Ingest task {taskId} is not in the queue ({column}).` | 4 |

### `ingest-resume`

Resume the ingest queue. No arguments; idempotent. → `POST /api/ingest-queue/resume`

| Outcome | HTTP source | Output | Exit |
| --- | --- | --- | --- |
| Resumed (or already running) | 200 | `Ingest queue resumed: {queuedTasks} task(s) queued.` | 0 |

### `query <prompt> [--conversation-id <id>] [--timeout <seconds>]`

Submit a query turn and wait (blocking) for its terminal state.
→ `POST /api/query-conversations/{conversationId}/turns`, then poll
`GET /api/query-turns/{turnId}` at 1 s intervals; on interrupt signal
`POST /api/query-turns/{turnId}/interrupt`.

- `--conversation-id` omitted → CLI generates one (`{yyyy-MM-dd}-conv-{guid}`, ≤ 40
  chars) and includes it in the header line.
- `--timeout` default 300; must be a positive integer.

| Outcome | HTTP source | Output | Exit |
| --- | --- | --- | --- |
| Turn completed | 202 then polled `completed` | Header: `Query turn {turnId} in conversation {conversationId}: completed` + answer text verbatim on following lines | 0 |
| Turn failed | polled `failed` | `Query turn {turnId} failed: {failureReason}` | 1 |
| Missing/empty prompt; malformed `--conversation-id`; non-positive `--timeout` | — (no request) | usage error | 2 |
| Concurrency limit reached | 503 `query_concurrency_limit_reached` | `The Hub is at its query concurrency limit; try again later.` | 4 |
| Conversation already active | 409 `conversation_already_active` | `Conversation {conversationId} already has an active turn.` | 4 |
| Conversation record unreadable | 500 `conversation_record_unreadable` | `Conversation record for {conversationId} is unreadable: {reason}` | 1 |
| Wait timeout elapsed | — (turn left running; no interrupt sent) | `Timed out after {timeout}s waiting for query turn {turnId}; the turn is still running on the Hub.` | 5 |
| Interrupt signal (Ctrl-C) while waiting | interrupt endpoint called | `Cancelled: interrupt requested for query turn {turnId}.` | 130 |

### `submit-source --path <path> [--source-kind <kind>]` (existing, parsing migrated)

Behavior, output (`Submitted ingest task: {taskId}`), and in-process execution are
unchanged; ADR-009 path switches remain accepted. Listed here because it appears in
the same root help and is dispatched by the same command framework.

## Help contract

- Root `--help`: FigletText logo, usage line, `Commands:` section listing all eight
  commands with descriptions, `Server options:` section listing every
  `PathSwitchCatalog.All` switch with description (017 parity preserved).
- `<command> --help`: that command's arguments/options with descriptions; no logo.

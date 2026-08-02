# Feature Specification: Hub CLI Command Parity for Write Actions

**Feature Branch**: `018-hub-cli-commands`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "setze die anforderungen in https://github.com/wmalgadey/grimoire/issues/45 und seine kommentare um" (implement the requirements in GitHub issue #45 and its comments)

## Clarifications

### Session 2026-08-02

- Q: FR-013 currently excludes the interactive query-turn actions from this feature
  because turn submission is asynchronous (HTTP POST returns "Accepted" with
  state=running; the answer arrives later via the realtime hub or a follow-up GET).
  Adding a `query` CLI command means deciding its completion semantics — fire-and-forget
  submit (matching the other six commands) or block until the turn reaches a terminal
  state? → A: Block until terminal state — the CLI submits the turn and waits, printing
  the final answer or failure reason.
- Q: Blocking until the turn completes conflicts with FR-012's requirement that commands
  run unattended in scripts/cron with no hangs. What should happen if the turn never
  reaches a terminal state? → A: Configurable `--timeout` argument — the operator can
  override a bounded default wait duration; if it elapses first, the CLI reports a
  timeout and exits non-zero.
- Q: How should the `query` CLI command handle the conversation id, given the
  Conversation Record is created lazily on a conversation's first turn (no separate
  "create conversation" action exists)? → A: Optional; if `--conversation-id` is
  omitted, the CLI generates a new conversation id itself and prints it as part of the
  result.
- Q: While `query` is blocking on a turn, the operator may press Ctrl-C (or a script may
  kill the process) before the turn reaches a terminal state. Should the CLI call the
  existing interrupt action server-side before exiting? → A: Yes — on cancellation, the
  CLI calls the existing interrupt action for that turn before exiting non-zero, rather
  than leaving the turn orphaned server-side.
- Q: FR-015 requires the `query` command to default to a "fixed duration" wait timeout
  when `--timeout` is omitted, but no value is specified. What should the default be? →
  A: 5 minutes — realistic for a full agent turn while still bounding unattended
  scripts; operators needing longer pass `--timeout` explicitly.
- Q: FR-006 mandates a single human-readable result line per command, but FR-017
  requires `query` to print the turn's answer text, which is typically multi-line. How
  should `query`'s successful output be shaped? → A: Header line + answer body — the
  first line carries conversation id, turn id, and terminal state (satisfying the
  FR-006 scriptable-result-line pattern); the full answer text follows verbatim on
  subsequent lines.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Trigger a lint run from a script or terminal (Priority: P1)

An operator or automation script needs to start a lint run over the wiki without opening
the web UI — for example from a cron job, a CI step, or a terminal on the machine hosting
the Hub. Today the only way to do this is a hand-rolled HTTP call against a running Hub
instance; there is no CLI equivalent.

**Why this priority**: This is the simplest of the candidate commands (no arguments), and
lint runs are typically the entry point of the wiki-maintenance cycle — validating this
command establishes the pattern (parse → invoke existing coordinator → print result → set
exit code) that every other command in this feature follows.

**Independent Test**: Can be fully tested by invoking the new command against a running
Hub data directory with no active lint run, and observing that a lint run starts, a result
line is printed, and the process exits successfully — without needing any of the other
commands in this feature to exist.

**Acceptance Scenarios**:

1. **Given** no lint run is currently active and no remediation tasks from a prior run are
   unresolved, **When** the operator runs the lint-run command, **Then** a new lint run
   starts, the CLI prints the run's identifier and status, and the process exits
   successfully.
2. **Given** a lint run is already active, **When** the operator runs the lint-run command
   again, **Then** the CLI prints a message stating a run is already active, starts no
   second run, and exits with a non-zero status.
3. **Given** a prior lint run left unresolved remediation tasks, **When** the operator runs
   the lint-run command, **Then** the CLI prints a message identifying that unresolved
   remediation tasks are blocking a new run, starts no new run, and exits with a non-zero
   status.

---

### User Story 2 - Manage a remediation task's authorization lifecycle from the command line (Priority: P1)

An operator reviewing proposed remediation actions (per the human-authorization workflow)
needs to authorize, dismiss, or withdraw authorization for a specific remediation task
without the web UI — for example while triaging a backlog of proposals from a terminal, or
scripting bulk decisions. This is the workflow most directly blocked by the current
CLI's one-command limitation, since every individual decision otherwise requires the
frontend.

**Why this priority**: Human-authorized remediation is a core operational control point in
the wiki-maintenance loop; being unable to drive it outside the UI is the most concrete
gap named in the motivating issue.

**Independent Test**: Can be fully tested by seeding a remediation task in the "proposed"
state and running each of the three commands (authorize, dismiss, withdraw) against it in
isolation, observing the correct state transition, printed result, and exit status for
each — independent of the lint-run or ingest commands.

**Acceptance Scenarios**:

1. **Given** a remediation task exists in the "proposed" state, **When** the operator runs
   the authorize command with that task's id, **Then** the task transitions to
   "authorized", the CLI prints the task id, new state, and authorization timestamp, and
   the process exits successfully.
2. **Given** a remediation task exists in the "proposed" state, **When** the operator runs
   the dismiss command with that task's id, **Then** the task transitions to "dismissed",
   the CLI prints the task id and new state, and the process exits successfully.
3. **Given** a remediation task exists in the "authorized" state and has not started
   executing, **When** the operator runs the withdraw command with that task's id, **Then**
   the task transitions back to "proposed", the CLI prints the task id and new state, and
   the process exits successfully.
4. **Given** a remediation task id that does not exist, **When** the operator runs any of
   the three commands with that id, **Then** the CLI prints a "task not found" message and
   exits with a non-zero status, without changing any task state.
5. **Given** a remediation task is not in the state required by the command (e.g.
   authorize or dismiss called on a task that is already "authorized" or "dismissed";
   withdraw called on a task that is "proposed", "dismissed", or already executing),
   **When** the operator runs that command, **Then** the CLI prints a message identifying
   the specific conflict (including when authorization was lost to execution already
   starting) and exits with a non-zero status, without changing task state.
6. **Given** the operator omits the required task-id argument, **When** the operator runs
   any of the three commands, **Then** the CLI prints a usage error and exits with a
   non-zero status without contacting the task store.

---

### User Story 3 - Recover the ingest queue from the command line (Priority: P2)

An operator needs to re-arm a single stalled ingest task or resume the whole ingest queue
after a restart or manual pause — for example as part of an operational runbook executed
over SSH, without a browser available.

**Why this priority**: These are recovery actions used less frequently than triggering
lint runs or deciding on remediation proposals, and are valuable primarily in operational
incident scenarios rather than routine workflows.

**Independent Test**: Can be fully tested by seeding a queued ingest task and running the
retrigger command against it, and separately by running the resume command against a
queue, observing the correct outcome and exit status for each — independent of the other
commands in this feature.

**Acceptance Scenarios**:

1. **Given** an ingest task is sitting in the queue, **When** the operator runs the
   ingest-retrigger command with that task's id, **Then** the task is re-armed, the CLI
   prints the task id and confirmation, and the process exits successfully.
2. **Given** an ingest task id that is not currently queued (e.g. already running, not
   found, or already completed), **When** the operator runs the ingest-retrigger command
   with that id, **Then** the CLI prints a message identifying why the task cannot be
   retriggered and exits with a non-zero status.
3. **Given** the ingest queue exists in any state, **When** the operator runs the
   ingest-resume command, **Then** the CLI prints the number of tasks now queued and the
   process exits successfully.

---

### User Story 4 - Ask the wiki a question from the command line (Priority: P2)

An operator needs a one-shot answer to a question about the wiki's content — for example
while triaging an incident over SSH, or scripting a scheduled report — without opening the
web UI and without needing a second command to fetch the answer once it's ready.

**Why this priority**: Query is the wiki's primary read-facing interaction, but its
underlying turn submission is asynchronous by design, so it carries more implementation
weight (waiting, timeout, cancellation) than the other six commands; it is valuable
alongside them but not the simplest entry point into this feature.

**Independent Test**: Can be fully tested by running the query command with a prompt
against a running Hub data directory — with and without an existing conversation id —
and observing that a turn is submitted, the CLI waits for it to reach a terminal state,
and the final answer (or failure/timeout reason) is printed with the correct exit status,
independent of the other commands in this feature.

**Acceptance Scenarios**:

1. **Given** no `--conversation-id` is supplied, **When** the operator runs the query
   command with a prompt, **Then** the CLI generates a new conversation id, submits the
   prompt as the first turn of that conversation, waits for the turn to complete, and
   prints the conversation id, turn id, and answer, exiting successfully.
2. **Given** an existing conversation id with no active turn, **When** the operator runs
   the query command with that conversation id and a prompt, **Then** the CLI submits the
   prompt as the next turn of that conversation, waits for it to complete, and prints the
   turn id and answer, exiting successfully.
3. **Given** the turn reaches a terminal state before the wait times out, **When** that
   state is "failed", **Then** the CLI prints the recorded failure reason and exits with a
   non-zero status.
4. **Given** the turn has not reached a terminal state within the effective timeout
   (the default, or the value passed via `--timeout`), **When** the wait elapses, **Then**
   the CLI prints a message distinct from the failure and conflict messages identifying
   that the wait timed out, exits with a non-zero status, and leaves the turn running
   server-side.
5. **Given** the CLI process receives an interrupt signal (e.g. Ctrl-C) while waiting,
   **When** the signal is received, **Then** the CLI calls the existing interrupt action
   for that turn before exiting, and exits with a non-zero status.
6. **Given** a conversation already has an active turn, **When** the operator runs the
   query command against that conversation id, **Then** the CLI prints a message
   identifying that the conversation is already active, submits no new turn, and exits
   with a non-zero status.
7. **Given** the operator omits the required prompt argument or supplies an empty prompt,
   **When** the operator runs the query command, **Then** the CLI prints a usage error and
   exits with a non-zero status without submitting a turn.

---

### Edge Cases

- What happens when a required `--task-id` argument is missing, empty, or malformed? The
  CLI must reject the invocation with a usage error before making any state change (see
  User Story 2, Scenario 6).
- What happens when a task id syntactically valid but referring to no known task is
  supplied? Treated as "not found" (User Story 2, Scenario 4; User Story 3, Scenario 2).
- What happens when two operators (or a script and a UI user) race on the same task —
  e.g. one withdraws authorization while the coordinator has just started executing it?
  The command must report the specific reason it lost the race rather than a generic
  failure (User Story 2, Scenario 5).
- What happens when `--help`/`-h` is passed together with one of the new commands? Per the
  existing convention, help takes precedence and is printed before any command runs.
- What happens when an unrecognized command name is passed? The CLI must print a usage
  error identifying the unknown command rather than silently doing nothing.
- What happens when `ingest-resume` is run while the queue is not paused (already running
  normally)? The action is idempotent and reports the current queued count rather than
  failing.
- What happens when the query command's wait exceeds its timeout? The CLI reports a
  timeout distinct from a failure or conflict message, exits non-zero, and leaves the turn
  running server-side rather than cancelling it (User Story 4, Scenario 4).
- What happens when the query command's CLI process is interrupted while waiting? The CLI
  calls the existing interrupt action for that turn before exiting, rather than leaving it
  orphaned server-side (User Story 4, Scenario 5).
- What happens when a conversation the query command targets already has an active turn?
  The command reports the conflict and starts no new turn (User Story 4, Scenario 6),
  consistent with the state-conflict pattern used elsewhere in this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Hub CLI MUST provide a `lint-run` command that starts a new lint run,
  equivalent in effect to the existing "trigger lint run" HTTP action, taking no
  arguments.
- **FR-002**: The Hub CLI MUST provide `remediation-authorize`, `remediation-dismiss`, and
  `remediation-withdraw` commands, each taking a required task-id argument, equivalent in
  effect to the existing "authorize", "dismiss", and "withdraw authorization" HTTP actions
  for a remediation task.
- **FR-003**: The Hub CLI MUST provide an `ingest-retrigger` command taking a required
  task-id argument, equivalent in effect to the existing "retrigger" HTTP action for a
  queued ingest task.
- **FR-004**: The Hub CLI MUST provide an `ingest-resume` command taking no arguments,
  equivalent in effect to the existing "resume ingest queue" HTTP action.
- **FR-005**: Each command introduced by this feature MUST drive the same underlying
  coordinator/service logic already used by its corresponding HTTP endpoint. No
  wiki-maintenance or task-lifecycle decision logic may be duplicated or re-implemented
  along the CLI path.
- **FR-006**: Each command MUST print a single human-readable result line to standard
  output summarizing the outcome (at minimum: task or run identifier and resulting state,
  where applicable) and MUST exit with a status code that distinguishes success from
  failure, so the command is usable unattended in scripts. For `query`, this result line
  is the first line of output; the answer body follows it per FR-017.
- **FR-007**: When a command fails due to a state conflict (lint run already active,
  unresolved remediation tasks blocking a new run, remediation task not in the state the
  action requires, ingest task not currently queued), the CLI MUST print a message that
  identifies the specific conflict — not a generic failure — and exit with a non-zero
  status without changing any state.
- **FR-008**: When a command references a task id or run id that does not exist, the CLI
  MUST print a "not found" message distinct from a state-conflict message and exit with a
  non-zero status.
- **FR-009**: When a required argument (e.g. task-id) is missing or malformed, the CLI
  MUST reject the invocation with a usage error before attempting any action, taking no
  side effect and exiting with a non-zero status.
- **FR-010**: The `--help`/`-h` output's command listing MUST document every command
  introduced by this feature (name, purpose, and its arguments), following the existing
  single-source-of-truth convention established for the Hub CLI's other switches — a
  command's name, description, and arguments must not be maintained in more than one
  place that could drift out of sync.
- **FR-011**: `--help`/`-h` MUST continue to take precedence over command execution: if
  passed alongside any command introduced by this feature, usage text is printed and no
  command runs.
- **FR-012**: All commands introduced by this feature MUST run non-interactively (no
  prompts for confirmation or missing input), so they are usable from scripts, cron jobs,
  and other unattended automation.
- **FR-013**: The Hub CLI MUST provide a `query` command taking a required prompt argument
  and an optional conversation-id argument, equivalent in effect to the existing turn-
  submission HTTP action; if the conversation-id is omitted, the CLI MUST generate a new
  conversation id itself (no separate "create conversation" action exists to delegate to)
  and include it in the printed result.
- **FR-014**: The `query` command MUST submit the turn via the same coordinator already
  used by the HTTP turn-submission endpoint, then wait for the turn to reach a terminal
  state (completed, failed, or interrupted) before printing a result line and exiting,
  subject to the timeout in FR-015. No wiki-query or answer-generation logic may be
  duplicated or re-implemented along the CLI path (per FR-005).
- **FR-015**: The `query` command MUST accept an optional timeout argument bounding how
  long it waits for the turn to reach a terminal state, defaulting to 5 minutes when
  omitted. If the timeout elapses before a terminal state is reached, the CLI MUST
  print a message distinct from the failure and conflict messages identifying that the
  wait timed out, exit with a non-zero status, and MUST NOT cancel the turn server-side.
- **FR-016**: If the CLI process receives an interrupt signal (e.g. Ctrl-C) while the
  `query` command is waiting for a terminal state, the CLI MUST call the same interrupt
  action already used by the HTTP interrupt endpoint for that turn before exiting, and
  exit with a non-zero status.
- **FR-017**: On success, the `query` command MUST print a header result line containing
  the conversation id, turn id, and terminal state to standard output, followed by the
  turn's answer text verbatim (preserving its line breaks) on the subsequent lines, and
  exit successfully. On failure
  (concurrency limit reached, conversation already active, conversation record
  unreadable, or the turn itself reaching a "failed" state), the CLI MUST print a message
  identifying the specific reason, consistent with the FR-007/FR-008 conflict/not-found
  conventions used by the feature's other commands, and exit with a non-zero status.
- **FR-018**: When the required prompt argument is missing or empty, the `query` command
  MUST reject the invocation with a usage error before submitting any turn, taking no
  side effect and exiting with a non-zero status (per FR-009).

### Key Entities

- **Lint Run**: An existing run of the lint process over the wiki; this feature adds a way
  to start one and observe its identifier/status from the CLI, without introducing new
  lint-run data or changing its existing states.
- **Remediation Task**: An existing task proposing a wiki-content remediation action,
  carrying an authorization-lifecycle state (proposed / authorized / dismissed /
  executing / completed / failed / not-applicable per the existing human-authorization
  workflow). This feature adds CLI-driven transitions between these existing states; it
  introduces no new states.
- **Ingest Task**: An existing task tracking a wiki-source ingest; this feature adds a way
  to re-arm a single queued task or resume the queue as a whole from the CLI, without
  changing the task's existing lifecycle states.
- **Query Turn**: An existing unit of a query conversation (one prompt, one answer),
  carrying a lifecycle state (running / completed / failed / interrupted per the existing
  query-dispatch workflow). This feature adds a CLI-driven way to submit one and wait for
  its terminal state; it introduces no new turn states.
- **Conversation**: An existing sequence of query turns identified by a conversation id
  and persisted as a Conversation Record (ADR-014). This feature adds a CLI-driven way to
  either target an existing conversation id or have the CLI generate a new one; it
  introduces no new conversation data beyond what turn submission already produces.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the seven commands introduced by this feature can be run to
  completion from a terminal or script against a Hub data directory, without a running
  web UI or any hand-rolled HTTP call.
- **SC-002**: 100% of invocations that fail due to a missing/malformed argument, an
  unknown task or run id, or a state conflict produce a message identifying the specific
  reason (not a generic failure) and a non-zero exit status.
- **SC-003**: 100% of invocations that succeed produce a printed result line containing
  the affected task/run/conversation identifier and resulting state (or answer, for
  `query`), and a zero exit status.
- **SC-004**: 100% of the seven commands are listed in `--help` output with their purpose
  and required arguments, discoverable without consulting source code or the HTTP API.
- **SC-005**: For every one of the seven actions, the state change produced via its CLI
  command is indistinguishable from the state change produced via its existing HTTP
  endpoint(s) — 100% parity, no divergent behavior between the two entry points.
- **SC-006**: 100% of `query` invocations that exceed their wait timeout or are cancelled
  by an interrupt signal produce a message distinguishing "timed out" from "cancelled"
  from every other failure/conflict message, with the correct corresponding server-side
  effect (no cancellation on timeout; interrupt requested on cancellation).

## Assumptions

- The seven commands in scope are the six the motivating issue identifies as
  "script/ops-friendly one-shot actions" — `lint-run`, `ingest-retrigger`,
  `ingest-resume`, `remediation-authorize`, `remediation-dismiss`,
  `remediation-withdraw` — plus `query`, added during clarification to cover the
  turn-submission action as a blocking, wait-for-completion command (see
  Clarifications). Turn status lookup and turn interrupt remain out of scope as
  standalone CLI commands: interrupt is invoked only internally by `query` on
  cancellation (FR-016), and no separate `query-status`/`query-interrupt` command is
  introduced by this feature.
- Command and argument names follow the literal names suggested in the issue's table
  (e.g. `ingest-retrigger --task-id <id>`) unless refined during planning; this is a
  naming default, not a scope decision.
- Result output stays plain text to standard output, consistent with the existing
  `submit-source` command's convention (`Submitted ingest task: {taskId}`) and with
  feature `017-hub-help-usage`'s established CLI conventions — no JSON or other
  structured output format is introduced by this feature.
- None of the underlying HTTP endpoints this feature drives (including turn submission
  and turn interrupt) currently enforce authentication or credential checks; this feature
  does not add any. CLI commands run with the same trust level as the operator already
  has when running `submit-source` or any `--*-dir`/`--*-file` switch today — whoever can
  run the Hub binary can run these commands. Access control, if ever needed, is a
  separate concern from this feature.
- How the growing command surface is parsed and dispatched internally (continuing the
  existing hand-rolled per-flag scanning vs. adopting a CLI framework, as raised in the
  issue's addendum comment) is a technical/architectural decision left to the planning
  phase and, if it introduces a new structural boundary, an ADR — it does not change any
  requirement in this spec, since all requirements here are expressed in terms of
  observable command behavior, not parsing implementation.
- Exit code conventions (which specific non-zero codes distinguish usage errors from
  not-found from state conflicts) are left to the planning phase to define precisely; this
  spec requires only that success and failure are distinguishable and that failure
  reasons are distinguishable from one another in the printed message.

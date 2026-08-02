# Feature Specification: Hub CLI Command Parity for Write Actions

**Feature Branch**: `018-hub-cli-commands`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "setze die anforderungen in https://github.com/wmalgadey/grimoire/issues/45 und seine kommentare um" (implement the requirements in GitHub issue #45 and its comments)

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
  failure, so the command is usable unattended in scripts.
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
- **FR-013**: The interactive/streaming query actions (submitting a conversation turn,
  interrupting a turn) are explicitly out of scope for this feature; no CLI command is
  introduced for them.

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

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the six commands introduced by this feature can be run to
  completion from a terminal or script against a Hub data directory, without a running
  web UI or any hand-rolled HTTP call.
- **SC-002**: 100% of invocations that fail due to a missing/malformed argument, an
  unknown task or run id, or a state conflict produce a message identifying the specific
  reason (not a generic failure) and a non-zero exit status.
- **SC-003**: 100% of invocations that succeed produce a printed result line containing
  the affected task/run identifier and resulting state, and a zero exit status.
- **SC-004**: 100% of the six commands are listed in `--help` output with their purpose
  and required arguments, discoverable without consulting source code or the HTTP API.
- **SC-005**: For every one of the six actions, the state change produced via its CLI
  command is indistinguishable from the state change produced via its existing HTTP
  endpoint — 100% parity, no divergent behavior between the two entry points.

## Assumptions

- The six commands in scope are exactly those the motivating issue identifies as
  "script/ops-friendly one-shot actions": `lint-run`, `ingest-retrigger`,
  `ingest-resume`, `remediation-authorize`, `remediation-dismiss`,
  `remediation-withdraw`. The streaming/interactive query-turn and query-interrupt
  actions are out of scope, as the issue itself suggests.
- Command and argument names follow the literal names suggested in the issue's table
  (e.g. `ingest-retrigger --task-id <id>`) unless refined during planning; this is a
  naming default, not a scope decision.
- Result output stays plain text to standard output, consistent with the existing
  `submit-source` command's convention (`Submitted ingest task: {taskId}`) and with
  feature `017-hub-help-usage`'s established CLI conventions — no JSON or other
  structured output format is introduced by this feature.
- None of the six underlying HTTP endpoints currently enforce authentication or
  credential checks; this feature does not add any. CLI commands run with the same trust
  level as the operator already has when running `submit-source` or any `--*-dir`/
  `--*-file` switch today — whoever can run the Hub binary can run these commands.
  Access control, if ever needed, is a separate concern from this feature.
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

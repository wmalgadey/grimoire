# Feature Specification: Single Agent System Prompt & Configurable Ingest Submission

**Feature Branch**: `claude/ingest-agent-systemprompt-dclhyu`

**Created**: 2026-07-11

**Status**: Draft

**Input**: User description: "die claude.md und skill.md des ingest agent sind
irreführend und sollen durch einen einzelnen systemprompt ersetzt werden. Außerdem soll
der benutzer beim ingest (siehe spec 003 gerade in entwicklung) den user prompt selbst
definieren, bzw. anpassen können und die 'convert'-schritte (aktuell in 003 nur
markitdown) aktivieren oder deaktivieren können"

## Terminology

- **System Prompt Document**: The single, versioned instruction document that defines
  the Ingest agent's operating rules and wiki-maintenance conventions. It replaces the
  current pair of instruction files (`CLAUDE.md` + `skills/wiki-maintenance/SKILL.md`),
  whose names wrongly suggest tooling mechanics (automatic discovery, on-demand skill
  loading) that do not exist — today both files are simply concatenated verbatim into
  the agent's system prompt.
- **User Prompt**: The per-run steering message that accompanies a submitted source into
  an ingest run. Today it is fixed and system-generated; this feature makes it visible
  and editable per submission.
- **Convert Step**: A named, per-submission processing step applied to the submitted
  source before it is stored and handed to the agent. Currently there is exactly one:
  document-to-Markdown conversion (introduced in feature 003). The model must
  accommodate additional steps later.
- **Agent Run Event**: A message the running Ingest agent sends to the Hub during a
  run: lifecycle signals (`started`, `completed` with summary, `failed` with reason),
  periodic `heartbeat` liveness signals, and agent-loop activity updates (e.g., number
  of tool calls used, number of model turns, current loop action). Events describe
  loop mechanics, never wiki-content judgment.
- **Run Queue**: The persistent, first-in-first-out order of accepted submissions
  waiting for the single agent slot. Only one agent runs at any time; the queue
  advances automatically when the slot frees during normal operation.

## Clarifications

### Session 2026-07-11

- Q: How does the Hub detect an aborted agent run once it no longer blocks on the
  process return code? → A: Heartbeat events + timeout — the agent emits periodic
  heartbeats; if no event arrives within the configured liveness window, the Hub marks
  the run failed.
- Q: What happens to queued submissions when the Hub restarts? → A: The queue is
  persistent; queued tasks remain visible after a restart but are only started again
  by explicit user re-trigger (no auto-resume after restart).
- Q: Which events must the agent send to the Hub during a run? → A: Lifecycle events
  plus agent-loop activity updates — what the loop is actively doing, e.g. "x tools
  used", "x model turns", and similar loop-level actions (no content-level detail).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Maintain agent behavior in one honest place (Priority: P1)

A project maintainer wants to change how the Ingest agent behaves (for example, adjust
the supersession rules or tag taxonomy). Instead of navigating two files whose names
falsely imply Claude-Code-style mechanics, they open the one System Prompt Document,
edit it, and know with certainty that this exact text — nothing more, nothing less — is
what governs the next agent run.

**Why this priority**: The misleading file layout is an active source of wrong mental
models: it suggests that "skills" are discovered or loaded on demand when in reality
everything is concatenated into one system prompt. Every future instruction change
(the project's primary way of changing wiki behavior, per the constitution) builds on
this surface, so it must be truthful before more features stack on top of it.

**Independent Test**: Edit the System Prompt Document (e.g., add a distinctive
instruction), trigger an ingest run, and verify from the run's task artifact that
exactly the edited document (matching content hash) was loaded as the agent's system
prompt — and that the retired instruction files are no longer read at all.

**Acceptance Scenarios**:

1. **Given** a repository with the System Prompt Document in place, **When** an ingest
   run starts, **Then** the agent's system prompt consists of exactly that one
   document's content, and the task artifact records that document's path and content
   hash.
2. **Given** the retired `CLAUDE.md`/`SKILL.md` files no longer exist, **When** an
   ingest run starts, **Then** the run proceeds normally using only the System Prompt
   Document — the retired files are not required and not consulted.
3. **Given** the System Prompt Document is missing, unreadable, or effectively empty,
   **When** a run is requested, **Then** the run fails before any wiki write with a
   human-readable reason (fail-closed behavior is preserved unchanged).
4. **Given** the consolidated document, **When** sampled ingest runs are evaluated,
   **Then** the agent still follows the same wiki-maintenance conventions as before
   consolidation (exploration before writing, update-vs-create judgment, frontmatter,
   tags, confidence scoring, supersession, index/log upkeep, injection defence, final
   summary) at the evaluation thresholds already defined for those behaviors.

---

### User Story 2 - Steer an individual ingest with a custom user prompt (Priority: P2)

A user submitting a source through the ingest Web UI (feature 003) wants to steer this
particular run — for example "focus on the security-related claims and ignore the
marketing content" or "treat this as an update to the existing page on X". On the
submission form they see the default User Prompt that would be used, can edit or
replace it for this submission, and can also just leave it untouched.

**Why this priority**: Today every ingest run gets the same fixed steering text, so the
only way to influence a run is to edit the source itself. Per-run steering is the first
real interaction lever users get over agent runs, and it directly builds on the
submission surface delivered by feature 003.

**Independent Test**: Submit the same source twice — once with the default prompt, once
with an explicit steer — and verify that (a) both task artifacts record the user prompt
actually used, and (b) the steered run visibly reflects the steer in its outcome
summary.

**Acceptance Scenarios**:

1. **Given** the submission form, **When** the user opens it, **Then** the default User
   Prompt is visible and editable before submission.
2. **Given** a submission with an edited User Prompt, **When** the ingest run starts,
   **Then** the run uses the edited prompt as its steering message, and the task
   artifact records the prompt text actually used.
3. **Given** a submission where the user left the prompt untouched (or cleared it),
   **When** the ingest run starts, **Then** the default User Prompt is used and
   recorded.
4. **Given** any User Prompt content, **When** the run executes, **Then** the source
   content is still delivered to the agent separately as delimited, untrusted data, and
   all existing write-scope guardrails apply unchanged — a custom prompt can steer
   editorial focus but cannot widen what the agent may do.
5. **Given** a submission with an explicit steer in the User Prompt, **When** sampled
   steered runs are evaluated, **Then** the agent's outcome demonstrably reflects the
   steer (e.g., the run summary and touched pages match the requested focus).

---

### User Story 3 - Control convert steps per submission (Priority: P3)

A user submitting a source knows the automatic document-to-Markdown conversion is wrong
for their case — for example they are submitting a URL whose fetched content is already
clean Markdown-like text, or they want the agent to see the fetched content exactly as
retrieved. On the submission form they see which Convert Steps apply to their
submission (currently only document-to-Markdown conversion) and can enable or disable
each one for this submission.

**Why this priority**: This is a refinement of the intake pipeline from feature 003 for
users who need it; the default (all steps enabled) already serves the common case, so
this delivers value to fewer submissions than User Stories 1 and 2.

**Independent Test**: Submit the same text-based source once with conversion enabled
and once with it disabled, and verify the stored raw-source content differs
accordingly — the disabled-step submission stores the content exactly as received, and
the task artifact records the chosen step configuration in both cases.

**Acceptance Scenarios**:

1. **Given** the submission form, **When** the user prepares a submission, **Then** the
   Convert Steps applicable to that submission are visible with their default state
   (enabled), and each can be toggled for this submission only.
2. **Given** a text-based submission (Markdown file, plain text, or URL) with the
   conversion step disabled, **When** the submission is processed, **Then** the content
   is stored exactly as received (byte-identical), no conversion is attempted, and the
   ingest run uses that stored content as its input.
3. **Given** a binary-format submission (PDF or Office document), **When** the user
   attempts to disable the conversion step, **Then** the submission surface prevents or
   rejects that combination with a clear explanation that these formats require
   conversion to be usable.
4. **Given** any submission, **When** its task artifact is inspected, **Then** the
   convert-step configuration that was applied (which steps ran, which were disabled)
   is recorded on it.
5. **Given** a submission with all applicable steps enabled (or the user changed
   nothing), **When** it is processed, **Then** behavior is identical to feature 003 as
   specified — the toggles introduce no change for default submissions.

---

### User Story 4 - Non-blocking agent runs with live loop activity (Priority: P4)

A user submits several sources in a row. The Hub accepts each one immediately — it
never blocks waiting for a running agent to finish. Exactly one agent runs at a time;
the other submissions line up in a visible queue and start automatically as the slot
frees. While a run is active, the user sees what the agent loop is currently doing
("x tools used", "x model turns", current action) and the Hub notices by itself —
via missing heartbeats — when a run has died, marking it failed instead of leaving it
stuck.

**Why this priority**: This decouples submission throughput from run duration and
replaces silent hangs with detected failures. It is independent infrastructure that
can ship in any order relative to User Stories 2 and 3, but it changes how every run
is supervised, so it is sequenced after the instruction-surface and submission-surface
changes it builds on.

**Independent Test**: Submit three sources in quick succession; verify all three are
acknowledged immediately, exactly one agent process runs at any moment, the other two
tasks queue and start automatically in acceptance order, live loop-activity counters
update during each run, and killing the agent process mid-run leads to a `failed` task
within the liveness window without any user action.

**Acceptance Scenarios**:

1. **Given** a running agent, **When** a user submits another source, **Then** the
   submission is accepted immediately, its task enters the queue in `queued` state,
   and the acceptance response does not wait for the running agent.
2. **Given** a run in progress, **When** the agent loop performs work, **Then** the
   task's detail view shows current loop activity (tools used, model turns, current
   action) updating as events arrive.
3. **Given** a running agent that stops sending events (crash or hang), **When** the
   configured liveness window elapses without a heartbeat, **Then** the Hub marks the
   run `failed` with a liveness reason, terminates any leftover agent process, and
   starts the next queued task.
4. **Given** queued tasks and a completing run, **When** the agent slot frees, **Then**
   the next task starts automatically in first-in-first-out order with no user action.
5. **Given** queued tasks, **When** the Hub restarts, **Then** the queued tasks are
   still visible in `queued` state after the restart and each can be explicitly
   re-triggered by the user; none starts automatically after a restart.

---

### Edge Cases

- What happens if both the new System Prompt Document and leftover retired instruction
  files exist side by side (e.g., after an incomplete migration)? Only the System
  Prompt Document is loaded; leftover retired files are ignored. The migration itself
  removes the retired files.
- What happens if the System Prompt Document exists but is empty or whitespace-only?
  The run fails closed before any wiki write, with a human-readable reason — identical
  to today's behavior for a missing/empty instruction set.
- What happens when a user submits an extremely long User Prompt? The submission
  surface enforces a reasonable length limit and rejects the submission with a clear
  validation message before a task is created.
- What happens when the User Prompt contains instruction-like text such as "ignore your
  write restrictions"? Nothing beyond normal steering: guardrails are enforced at the
  tool boundary independent of prompt content, and the source remains delimited
  untrusted data. A prompt can never widen the agent's write scope.
- What happens when the user clears the User Prompt field entirely? The default User
  Prompt is used and recorded; an empty steering message is never sent.
- What happens when conversion is disabled for a URL submission whose fetched content
  is HTML? The fetched content is stored as received and handed to the agent as-is;
  the user has explicitly taken responsibility for input quality. The stored artifact
  and task record make the choice traceable.
- What happens to submissions created before this feature (no recorded prompt or step
  configuration)? They remain valid historical records; absence of these fields simply
  means "system defaults of their time" and is displayed as such.
- What happens when future additional Convert Steps are introduced? The per-submission
  configuration model must accommodate multiple named steps without redesign; this
  feature ships with the single existing conversion step.
- What happens when the agent process is still alive but hung (no events, no exit)?
  After the liveness window elapses, the run is marked `failed` and the Hub terminates
  the leftover process before starting the next queued task — the single-agent
  invariant must never be violated by a zombie process.
- What happens when events arrive after a run was already marked failed (late or
  out-of-order events)? They are recorded for diagnostics but change nothing: a
  terminal state is final.
- What happens when the Hub restarts while a run is active? The interrupted run is
  reconciled to `failed` (existing behavior); queued tasks remain `queued` and wait
  for explicit user re-trigger.
- What happens when a user re-triggers a queued task while another run is active?
  The task stays in the queue in its position; re-trigger after restart re-arms
  automatic processing, it does not jump the queue or start a second agent.

## Requirements *(mandatory)*

### Functional Requirements

**Single System Prompt Document**

- **FR-001**: The Ingest agent MUST receive its operating instructions from exactly one
  versioned System Prompt Document, loaded verbatim as its system prompt. The split
  `CLAUDE.md` + `SKILL.md` instruction set is retired and MUST NOT be consulted.
- **FR-002**: The System Prompt Document MUST preserve the full behavioral content
  currently spread across the two retired files (operating rules, exploration-first
  workflow, update/supersede/create judgment, page types, frontmatter standard, tag
  taxonomy, confidence scoring, supersession rules, index and log upkeep, prompt-
  injection defence, mandatory final summary). Consolidation is a restructuring, not a
  behavior change.
- **FR-003**: Loading MUST remain fail-closed: a missing, unreadable, or effectively
  empty System Prompt Document causes the run to fail before any wiki write, with a
  human-readable failure reason recorded on the task artifact.
- **FR-004**: Each ingest run's task artifact MUST record the System Prompt Document
  actually loaded for that run (its identity and content hash), preserving the
  traceability guarantee established in feature 002.
- **FR-005**: After this feature, changing wiki-maintenance behavior MUST require
  editing only the System Prompt Document — no backend change — in line with the
  constitution's agentic-core boundary.

**User-defined User Prompt**

- **FR-006**: The ingest submission surface (feature 003) MUST display the default User
  Prompt and allow the user to edit or replace it per submission before submitting.
- **FR-007**: A submission where the user leaves the prompt unchanged, or clears it,
  MUST use the system default User Prompt; submitting without touching the prompt MUST
  require no additional effort compared to feature 003.
- **FR-008**: The ingest run MUST use the submission's User Prompt as its steering
  message. The source content MUST continue to be delivered separately as delimited,
  untrusted data, and the User Prompt MUST NOT be able to alter guardrail policy, write
  scope, or the delimited delivery of source content.
- **FR-009**: The User Prompt actually used for a run MUST be recorded on the task
  artifact and visible wherever task details are shown.
- **FR-010**: The submission surface MUST enforce a reasonable maximum length for the
  User Prompt and reject oversized prompts with a clear validation message before a
  task is created.

**Configurable Convert Steps**

- **FR-011**: The submission surface MUST present the Convert Steps applicable to the
  submission (currently exactly one: document-to-Markdown conversion), each defaulting
  to enabled, and allow the user to enable or disable each step for that submission.
- **FR-012**: When the conversion step is disabled for a text-based submission
  (Markdown file, plain text, or URL content), the system MUST store the source content
  exactly as received, attempt no conversion, and use the stored content as the ingest
  run's input.
- **FR-013**: The system MUST reject combinations that would produce unusable agent
  input — specifically, disabling document-to-Markdown conversion for binary formats
  (PDF, Office documents) — with a clear, actionable message, before a task is created.
- **FR-014**: The convert-step configuration applied to a submission (which steps were
  enabled/disabled) MUST be recorded on the task artifact and visible wherever task
  details are shown.
- **FR-015**: A submission with default settings (all applicable steps enabled, default
  prompt) MUST behave exactly as specified in feature 003 — this feature adds options,
  not changes to the default path.

**Event-based agent supervision & run queue**

- **FR-016**: The Hub MUST start the Ingest agent without blocking on its process
  return code; accepting further submissions and serving status MUST NOT wait for a
  running agent to finish.
- **FR-017**: The running agent MUST send Agent Run Events to the Hub: `started` at run
  begin, periodic `heartbeat` liveness signals, agent-loop activity updates (tools
  used, model turns, current loop action), `completed` with the final summary, and
  `failed` with a human-readable reason. Events carry the task identifier and MUST NOT
  carry wiki-content judgment beyond the agent's own summary text.
- **FR-018**: The Hub MUST derive the task's lifecycle state and board/detail display
  from these events (integrating with feature 003's realtime propagation), including
  live loop-activity counters in the task detail view.
- **FR-019**: Exactly one agent MUST run at any time. Additional accepted submissions
  MUST enter the Run Queue in first-in-first-out order of acceptance and start
  automatically, without user action, when the slot frees during normal operation.
- **FR-020**: If no event (including heartbeats) arrives for a running task within the
  configured liveness window, the Hub MUST mark the run `failed` with a liveness
  reason, terminate any leftover agent process, and advance the queue.
- **FR-021**: The Run Queue MUST survive a Hub restart: queued tasks remain visible in
  `queued` state. After a restart, queued tasks MUST NOT start automatically; the user
  MUST be able to explicitly re-trigger them (per task or as a whole), after which
  normal automatic queue processing resumes.
- **FR-022**: Events arriving for a task already in a terminal state MUST be recorded
  for diagnostics and MUST NOT change the task's state.

### Key Entities

- **System Prompt Document**: The single versioned instruction document governing the
  Ingest agent. Attributes: identity/location, content, content hash per run. Replaces
  the retired two-file instruction set.
- **Ingest Submission (extended)**: The feature-003 submission, extended with two new
  user-controlled aspects: the User Prompt (text, optional — default applies when
  absent) and the Convert Step Configuration (per-step enabled/disabled choices).
- **User Prompt**: Per-submission steering text. Has a system default; the effective
  value used for a run is recorded on the Task Artifact.
- **Convert Step Configuration**: The set of named Convert Steps applicable to a
  submission and their enabled/disabled state. Currently one step
  (document-to-Markdown); the model accommodates additional named steps in the future.
- **Task Artifact (extended)**: The existing run record, extended to also record the
  effective User Prompt and the applied Convert Step Configuration, alongside the
  already-recorded instruction identity and hash.
- **Agent Run Event**: A message from the running agent to the Hub. Attributes: task
  identifier, event type (`started`, `heartbeat`, `activity`, `completed`, `failed`),
  timestamp, and type-specific payload (activity counters such as tools used and model
  turns; completion summary; failure reason).
- **Run Queue**: The persistent set of accepted-but-not-started tasks in
  first-in-first-out acceptance order. Attributes: task identifier, queue position,
  accepted-at timestamp. Survives Hub restarts; post-restart processing requires
  explicit user re-trigger.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees (100%)**

- **SC-001**: 100% of ingest runs started after this feature load the System Prompt
  Document as the agent's entire system prompt, and the content hash recorded on the
  task artifact matches the document that was on disk at run start.
- **SC-002**: 100% of runs with a missing, unreadable, or empty System Prompt Document
  fail before any wiki write, with a human-readable reason recorded.
- **SC-003**: 100% of submissions record the effective User Prompt and the applied
  convert-step configuration on their task artifact.
- **SC-004**: 100% of text-based submissions with conversion disabled store content
  byte-identical to what was received; 100% of binary-format submissions with
  conversion disabled are rejected before a task is created.
- **SC-005**: 100% of runs enforce the same guardrail policy regardless of User Prompt
  content — no sampled or tested run shows a prompt-induced widening of write scope.
- **SC-008**: 100% of submissions made while an agent is running are acknowledged
  without waiting for that run to finish, and at no point do two agent processes run
  concurrently.
- **SC-009**: 100% of runs whose events stop (crash, hang, kill) are marked `failed`
  with a liveness reason within the configured liveness window, with no task ever left
  indefinitely in `running`.
- **SC-010**: 100% of tasks queued at Hub shutdown are still visible as `queued` after
  restart and can be re-triggered by the user; none starts without that re-trigger.
- **SC-011**: Live loop-activity updates (tools used, model turns, current action) are
  visible in the task detail view within 2 seconds (p95) of the corresponding event,
  consistent with feature 003's propagation targets.

**Agent-judgment evaluation thresholds**

- **SC-006**: Consolidation causes no behavioral regression: sampled ingest runs under
  the System Prompt Document meet the same convention-adherence evaluation thresholds
  established for the agent in feature 002 (e.g., ≥ 95% of sampled pages carry required
  frontmatter and tags; update-vs-create judgment thresholds unchanged).
- **SC-007**: ≥ 90% of sampled runs submitted with an explicit, well-formed steering
  User Prompt visibly reflect that steer in the run outcome (final summary and touched
  pages match the requested focus), as judged by the evaluation rubric.

## Assumptions

- **Dependency on feature 003**: This feature extends the ingest submission surface and
  conversion pipeline specified in `specs/003-ingest-intake-webui` (currently in
  development, PR #7). It assumes 003 lands first; the submission form, lifecycle
  stages, and conversion behavior referenced here are the ones 003 defines.
- **Constitution wording**: Constitution Principle V names "agent `CLAUDE.md` /
  `SKILL.md`" as examples of versioned instruction files. A single versioned System
  Prompt Document still satisfies the principle's substance (versioned instruction
  files actually loaded into the agent's context). A PATCH-level clarification of the
  constitution's example wording may accompany this feature but is not a precondition.
- **System Prompt Document stays file-based and version-controlled**: It is edited like
  code (reviewed, versioned), not through the Web UI. Only the per-run User Prompt is
  editable in the UI. Managing the system prompt through the UI is out of scope.
- **No prompt template management**: The form is prefilled with the single system
  default; saving, naming, or sharing reusable prompt templates is out of scope. An
  edited prompt applies to that submission only.
- **Empty prompt means default**: Clearing the prompt field expresses "use the
  default", not "send an empty steering message".
- **Binary formats require conversion**: PDF and Office documents cannot skip
  document-to-Markdown conversion, since the agent consumes text; the reject-early rule
  (FR-013) encodes this.
- **Convert-step model is forward-compatible**: The per-submission step configuration
  is designed as a set of named steps so future steps can be added, but only
  document-to-Markdown ships now; no speculative steps are introduced.
- **Existing evaluation thresholds are the parity baseline**: The agent-behavior
  thresholds defined in feature 002's spec serve as the regression baseline for SC-006;
  no new convention thresholds are introduced for consolidation itself.
- **Queue order is FIFO by acceptance time**: No priorities, no reordering, no queue
  jumping; duplicates remain independent tasks (per 003).
- **Liveness window default**: 60 seconds without any event marks a run failed; the
  window and heartbeat interval are configurable. The concrete event transport is a
  design decision for the plan (and expected to revise the ADR-002 "wait for exit
  code" wording).
- **Event scope stays loop-mechanical**: Activity events report harness-observable
  loop facts (counts, current action); they never require the backend to interpret
  wiki content (Principle V).

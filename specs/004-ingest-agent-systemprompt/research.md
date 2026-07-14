# Research: Single Agent System Prompt & Configurable Ingest Submission

**Feature**: `specs/004-ingest-agent-systemprompt` | **Date**: 2026-07-11

All Technical Context unknowns are resolved below. Format per decision:
Decision / Rationale / Alternatives considered.

## R1: Location and shape of the single System Prompt Document

**Decision**: One file, `agents/ingest/system-prompt.md`, loaded verbatim as the entire
system prompt. `agents/ingest/CLAUDE.md` and `agents/ingest/skills/wiki-maintenance/SKILL.md`
are deleted in the same change; their content is merged into the new document with a
structure that reflects how it is actually consumed (one continuous instruction text
with sections, no pretend "skill" mechanics).

**Rationale**: The current layout mimics Claude Code conventions (`CLAUDE.md` +
auto-discovered `SKILL.md` files) while the loader (`InstructionSetLoader`) simply
concatenates everything into one string. The names promise progressive disclosure and
on-demand skill loading that the runtime does not perform — exactly the misleading
mental model this feature removes. A single file named for what it is (the system
prompt) makes the artifact and the mechanism identical.

**Alternatives considered**:
- *Keep two files, rename them* (e.g., `operating-rules.md` + `conventions.md`): still
  implies a structural split that has no runtime meaning; the loader would still
  concatenate. Rejected — half-measure.
- *Keep `CLAUDE.md` as the single file name*: name collides with the repository's own
  Claude Code context file convention and keeps the misleading association. Rejected.

## R2: Loader and CLI contract changes

**Decision**: Replace `InstructionSetLoader`'s directory-scan behavior with a
single-file load (`SystemPromptLoader`, same fail-closed semantics: missing/unreadable/
empty ⇒ run failure before any wiki write, SHA-256 recorded). The agent CLI replaces
`--instructions-dir` with `--system-prompt-path`. The task artifact records exactly one
instruction entry (path + SHA-256), keeping the field shape introduced in 002 (a list)
with a single element so existing artifact readers keep working.

**Rationale**: The CLI argument named after a directory of instruction files is part of
the misleading surface. A path to the one document is honest and keeps the ADR-002
child-process contract (arguments in, artifact out) intact — this is an argument
rename, not an invocation-model change.

**Alternatives considered**:
- *Keep `--instructions-dir` and look for `system-prompt.md` inside it*: preserves a
  directory indirection with only one meaningful file. Rejected — same dishonesty one
  level down. The `agents/ingest/` directory still exists (policy.json, prompts), but
  the agent should be pointed at the document it consumes.

## R3: Default User Prompt as a versioned file

**Decision**: The default user prompt (today the hardcoded scaffold text inside
`AgentLoop.BuildUserMessage`) moves into a versioned file,
`agents/ingest/default-user-prompt.md`. The agent CLI gains
`--default-user-prompt-path` (required) and `--user-prompt` (optional override string).
Effective prompt = `--user-prompt` if provided, else the file content. Missing/empty
default file with no override ⇒ fail-closed like the system prompt. The harness-owned
message scaffold (task id, source reference, `<source>`-delimited content, injection
warning) remains in backend code and always wraps the effective prompt — it is not
user-editable.

**Rationale**: Changing the default steering text is a wiki-behavior change; per
Principle V's boundary smell test it must be an instruction-file change, not a backend
change. It also gives the Hub a single source of truth to display in the submission
form (FR-006) without duplicating prompt text in frontend or Hub code. Keeping the
scaffold in code preserves the prompt-injection defence (FR-008): no user input can
remove the `<source>` delimiters or the untrusted-data framing.

**Alternatives considered**:
- *Keep the default prompt hardcoded, Hub duplicates it for display*: two copies drift;
  changing steering requires a backend release. Rejected.
- *Make the entire user message user-editable*: would let a submission remove the
  source delimiters and injection framing. Rejected — violates FR-008 and Principle V
  guardrail intent.

## R4: Transport of the effective user prompt to the agent

**Decision**: Pass the effective custom prompt as a plain CLI argument
(`--user-prompt "<text>"`), bounded by a submission-side maximum of **8,000
characters** (FR-010). The Hub records the effective prompt in the task artifact before
dispatch; the agent records it again from its own view (single writer at a time — the
agent owns the artifact during the run, per 002).

**Rationale**: 8,000 characters is far below OS argument-size limits (Linux ~128 KiB
per arg) and generous for steering text. A CLI arg keeps the ADR-002 contract file-free
for transient inputs and avoids temp-file lifecycle handling. `PastedText` already uses
this pattern.

**Alternatives considered**:
- *Temp file + `--user-prompt-path`*: adds file lifecycle/cleanup for a value that is
  small and transient. Rejected as overhead without benefit at this size bound.
- *Environment variable*: hides the value from the process argument audit trail the
  dispatcher logs; no size benefit at this bound. Rejected.

## R5: Convert-step model

**Decision**: A submission carries a convert-step configuration: a map of
`step name → enabled`. The Hub owns a static step registry; the only registered step in
this feature is `markitdown` (document-to-Markdown conversion, from 003). Per step the
registry declares: which source kinds it *applies to* (`url`, `pdf_file`,
`office_file`; not `markdown_file`, which 003 passes through untouched) and for which
kinds it is *required* (`pdf_file`, `office_file` — binary formats). Validation rejects
(HTTP 400/422, before task creation): unknown step names, and disabling a required
step. Disabling `markitdown` for a text-based submission stores the fetched/uploaded
content byte-identical as the normalized artifact (checksum over the unmodified bytes),
and the ingest run consumes it as-is.

**Rationale**: The user's framing ("convert steps, currently only markitdown") is
plural by design; a named-step map absorbs future steps without API redesign. Required
vs. applicable per source kind encodes FR-013 (binary needs conversion) declaratively.
Byte-identical pass-through gives SC-004 a crisp deterministic test.

**Alternatives considered**:
- *Single boolean `skipConversion`*: dead end the moment a second step exists; renames
  needed later. Rejected.
- *Allow disabling conversion for binary formats and store raw bytes*: the agent reads
  text; feeding it PDF bytes produces garbage runs that fail late and expensively.
  Rejected in spec (FR-013) — reject early instead.

## R6: API surface extension (extends 003 contract)

**Decision**: Extend `POST /api/ingest-submissions` with two optional fields:
`userPrompt` (string, ≤ 8,000 chars) and `convertSteps` (map step→bool). Absent fields
mean defaults (default prompt, all applicable steps enabled) — 003 requests remain
valid unchanged (FR-015). Add `GET /api/ingest-submissions/defaults` returning the
default user prompt text and the step registry (name, applies-to, required-for,
default state) so the form can render prompt and toggles from one source of truth.

**Rationale**: Optional-field extension keeps 003's contract backward compatible. A
defaults endpoint avoids embedding the prompt text or step registry in the frontend
build, so a prompt-file edit is live on next page load without a frontend release.

**Alternatives considered**:
- *Prefill prompt client-side from a bundled copy*: drifts from
  `default-user-prompt.md`. Rejected.
- *Separate endpoints per concern*: two round-trips for one form render. Rejected.

## R7: Task-artifact recording of prompt and step configuration

**Decision**: The task artifact frontmatter gains `user_prompt_source:
default | custom` and `convert_steps` (map with each applicable step's enabled state);
the artifact body gains a `## User Prompt` section containing the effective prompt text
verbatim. The board detail view (003's task detail) displays both.

**Rationale**: Frontmatter fields keep machine-readable state greppable/diffable
(ADR-003 plain-file principle); the verbatim body section satisfies FR-009's
"visible wherever task details are shown" without size-limited frontmatter strings.

**Alternatives considered**:
- *Prompt only in frontmatter*: multi-line YAML strings are noisy to diff and easy to
  malform. Rejected.
- *Prompt only in SQLite operational state*: violates ADR-003 — this is domain-facing
  run history, not transient bookkeeping. Rejected.

## R8: Evaluation approach for SC-006 (consolidation parity) and SC-007 (steering)

**Decision**: Reuse the `Grimoire.AgentEvals` harness from 002. SC-006: re-run the
existing convention-adherence and instruction-change-adoption eval suites against the
consolidated `system-prompt.md` at their existing thresholds (the eval fixtures that
edit instruction files are updated to edit the single document). SC-007: new eval set
of ≥ 10 sampled submissions pairing one source with distinct steering prompts; an
LLM-judge rubric scores whether the run summary and touched pages reflect the steer;
threshold ≥ 90%.

**Rationale**: Principle II mandates evaluation-style verification for agent judgment;
002 already established the pattern, fixtures, and thresholds — parity means literally
the same gates passing under the new instruction surface.

**Alternatives considered**:
- *Deterministic string checks on outputs for steering*: reimplements judgment as
  string matching — exactly what Principle II forbids as false coverage. Rejected.

## R9: Event transport — NDJSON over the agent's stdout

**Decision**: The agent emits Agent Run Events as newline-delimited JSON on its
**stdout**; the Hub, which already spawns and owns the child process (ADR-002), reads
the pipe line-by-line and dispatches events. Non-JSON stdout lines are tolerated and
logged as diagnostics. The agent's human-readable logging stays on stderr.

**Rationale**: Zero new infrastructure (Principle IV): no network endpoint, no auth,
no broker. It stays inside the ADR-002 child-process contract — the pipe exists
already. Hermetic tests are trivial (feed scripted lines to the parser; fake agent
executable for end-to-end dispatch tests). Credential scoping (ADR-004) is untouched.
Containerizing later still works: container runtimes expose stdout streams.

**Alternatives considered**:
- *HTTP callbacks from agent to a Hub endpoint*: introduces a network surface,
  loopback auth, and retry semantics for what is a parent↔child relationship.
  Rejected as disproportionate.
- *SignalR client inside the agent*: heaviest option; couples the agent to Hub
  hosting details. Rejected.
- *File-based event journal (Hub tails a file)*: survives a Hub restart, but the
  interrupted-run case is already reconciled to `failed` by existing ADR-003 restart
  logic, so that benefit is moot; tailing adds fsync/rotation complexity. Rejected.

## R10: Liveness supervision — event-silence window as the sole failure authority

**Decision**: The agent emits a `heartbeat` event every **10 seconds** (background
timer, independent of model latency) plus activity events as the loop works. The Hub
tracks last-event-received per running task; if **60 seconds** (configurable) pass
without any event, it marks the run `failed` with a liveness reason, terminates any
leftover process, and advances the queue. Process exit does not by itself fail the
run — a crashed process simply stops producing events, so the same single mechanism
covers crash, hang, and kill (clarification Q1: heartbeat + timeout chosen over
process-exit backstop). Terminal events (`completed`/`failed`) end supervision
immediately; late events after a terminal state are recorded and ignored (FR-022).

**Rationale**: One failure-detection authority instead of two racing ones keeps state
transitions unambiguous and matches the clarified spec. 10 s/60 s gives 6 missed
beats before failure — tolerant of GC pauses and slow filesystems, still far below
run duration.

**Alternatives considered**:
- *Process-exit backstop in addition to the window*: faster crash detection but two
  competing authorities; explicitly not chosen in clarification. Rejected.
- *Model-turn-based liveness (no timer)*: a long model call would look dead. Rejected.

## R11: Run queue — SQLite operational state with paused-after-restart flag

**Decision**: The Run Queue lives in the existing SQLite operational-state store
(ADR-003): accepted tasks get a queue row (task id, accepted-at); order is FIFO by
accepted-at. A single `queue_paused` flag is set on Hub startup when queued rows
exist (restart detected); while paused, nothing auto-starts. `POST
/api/ingest-queue/resume` (whole queue) or `POST
/api/ingest-submissions/{taskId}/retrigger` (single task) clears/paces processing per
clarification Q2 (persistent queue, explicit re-trigger after restart). During normal
operation the dispatcher starts the next queued task automatically on each terminal
event. 003's `IngestRunGate` (in-process semaphore awaiting dispatch) is replaced by
this queue-driven dispatcher; the single-agent invariant moves from "callers block on
a gate" to "one supervisor starts at most one process".

**Rationale**: Queue membership/order is operational bookkeeping, exactly what
ADR-003 assigns to SQLite; the domain-visible state remains the task artifact's
`queued` status. No new infrastructure; restart behavior is a natural flag rather
than fragile in-memory reconstruction.

**Alternatives considered**:
- *Derive the queue purely from task artifacts in `queued` state*: no explicit order
  authority (file mtimes are unreliable), and no place for the paused flag. Rejected.
- *Auto-resume after restart*: explicitly not chosen in clarification Q2. Rejected.

## R12: Loop-activity events — harness-observable counters only

**Decision**: The agent loop emits an `activity` event on each loop step with
loop-mechanical facts: model turns so far, tool calls so far (total and per tool
name), and the current action (`model_turn`, `tool_call:<tool>`, `finalizing`).
Payloads never include page content or editorial rationale; the `completed` event
carries the agent's final summary text verbatim (already harness-recorded in the task
artifact). The task detail view renders these counters live; the Kanban card stays
status-only (003 contract unchanged).

**Rationale**: Matches clarification Q3 ("x tools used, x model turns, current
action") and keeps Principle V intact: the backend transports counts, it interprets
no wiki content. AgentLoop already tracks turns/tool requests for metrics, so the
event payload reuses existing counters.

**Alternatives considered**:
- *Content-level progress ("writing page X")*: leaks content semantics into
  harness display contracts and tempts backend interpretation. Rejected for now
  (extendable later via a new event type without breaking the contract).

**Superseded note (2026-07-14 convergence check)**: The "Kanban card stays
status-only" line above was superseded during implementation — `TaskCard.svelte`
was built to render `runActivity` (model turns, tool calls, current action) for
`running` tasks too, not only the detail view. This is a stricter reading of FR-018's
"board/detail display" than R12 originally scoped, verified present in code; no
regression, just a research-vs-build drift worth recording here rather than silently
leaving the contradiction.

## R14: Board connection-health indicator (2026-07-14 clarification, FR-023/SC-012)

**Decision**: Project a small client-side state machine —
`connecting | connected | reconnecting | disconnected` — directly from the existing
`HubConnection` (`ingestLifecycleClient.ts`, ADR-001) already used for lifecycle/
run-activity events. Wire it off the SignalR client's own `onreconnecting`,
`onreconnected`, and `onclose` callbacks (only `onreconnected` was previously
consumed); expose it as `onConnectionStateChanged` from
`createIngestLifecycleClient`/`createBoardLifecycleStream`, alongside the initial
state set once `connection.start()` resolves. Render it as a persistent badge near
the page header in `frontend/src/routes/+page.svelte`, via a new
`ConnectionStatusIndicator.svelte` component.

**Rationale**: The SignalR client library already tracks this state internally and
exposes exactly these three lifecycle callbacks — no new transport, endpoint, or
backend change is needed. Keeping the projection in the existing lifecycle client
(rather than a new service) avoids a second connection object and keeps one source
of truth for connection state, consistent with how `onReconnected` already triggers a
board refresh.

**Alternatives considered**:
- *Poll a Hub health endpoint from the browser*: adds a new backend endpoint and a
  polling timer for information the client already has for free via its existing
  SignalR connection object. Rejected as redundant.
- *Derive health indirectly from lifecycle/event staleness (e.g., "no event in N
  seconds")*: conflates two different failure modes (agent liveness, which is a Hub-
  side concern per ADR-008/R10, vs. browser-to-Hub transport health) and would give
  false positives when a task is simply idle in `queued`. Rejected — connection state
  must come from the transport itself, not from event cadence.

## R13: Constitution wording touch-up

**Decision**: Propose (outside this feature's gate) a PATCH amendment via
`/speckit-constitution` changing Principle V's parenthetical example "agent `CLAUDE.md`
/ `SKILL.md`" to technology-neutral wording ("versioned instruction files, e.g. the
agent's system-prompt document"). Not a precondition for this feature: the principle's
substance (versioned instruction files loaded into the agent's context) is satisfied by
the single document.

**Rationale**: The constitution names those files as examples, not as a mandated
layout; amending examples is a PATCH-level clarification per the amendment procedure.

**Alternatives considered**:
- *Block the feature on the amendment*: inverts the dependency; the amendment is
  cosmetic. Rejected.

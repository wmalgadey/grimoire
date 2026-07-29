# Tasks: Conversation Records Replace Query-Run Artifacts

**Input**: Design documents from `/specs/011-query-conversations/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
(conversation-record-format.md, query-conversation-api.md), quickstart.md,
ADR-014 (**accepted**)

**Tests**: Required — the constitution mandates hermetic harness tests for
deterministic guarantees and Red/Green-probed structural tests for architectural
boundaries. Every success criterion in this feature (SC-001..SC-005) is a
deterministic harness guarantee: all tests below run hermetically with
`FakeAgentProcess`/`FakeModelClient` against a real temp filesystem — no live LLM
calls, no API keys. There are no agent-judgment criteria in this feature; feature
008's evaluation thresholds must keep passing unchanged (verified in the final
phase).

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P3) to
enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are exact, relative to repository root

## Path Conventions

Existing web-app split: `backend/src/`, `backend/tests/`, `frontend/src/`. This
feature adds one new Hub namespace `Grimoire.Hub.QueryConversations`
(`backend/src/Grimoire.Hub/QueryConversations/`), deletes the
`Grimoire.Hub.QueryRunArtifact` namespace, and adds the runtime location
`data/conversations/` (git-ignored) while retiring `data/query-runs/`. No new
assembly, process, or port (plan.md Structure Decision).

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove ADR-014's retired-location tripwire (ADR-009 IL-literal idiom)
is live *before* any feature code exists. This phase is first, non-negotiable,
and blocks everything else.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is
complete.

- [X] T001 Write `backend/tests/Grimoire.ArchTests/RetiredQueryRunsLocationRuleTests.cs`:
  Mono.Cecil IL-literal scan (same idiom as `RuntimePathsBoundaryRuleTests.cs`)
  asserting **no production assembly** (`Grimoire.Hub`, `Grimoire.QueryAgent`,
  `Grimoire.IngestAgent`, `Grimoire.AgentRuntime`, `Grimoire.Domain`,
  `Grimoire.EvalRunner`) contains a string literal containing the substring
  `query-runs` (SC-004 structural half, ADR-014). The current codebase has
  exactly two genuine occurrences — `GrimoirePathOptions.DefaultQueryRunsDirName`
  (`= "query-runs"`) and `Program.cs`'s `"--query-runs-dir"` CLI mapping — so the
  rule carries a **temporary, explicitly-commented allowlist** naming exactly
  those two declaring types ("cutover debt — emptied by T019"), keeping CI green
  until the US1 cutover task deletes them and empties the allowlist.
- [X] T002 Red/Green probe for T001 (controlled, per constitution Phase 0
  requirement): add a scratch class in `backend/src/Grimoire.Hub/` (outside the
  allowlisted types) containing the literal `"query-runs"`, run
  `dotnet test backend/tests/Grimoire.ArchTests` — the rule MUST fail naming the
  scratch type; delete the scratch class, run again — it MUST pass. Commit
  message documents the probe result. (A second Red/Green cycle is re-run at
  T019 when the allowlist is emptied — see that task.)
  *Probe executed 2026-07-29: Red — scratch class `Grimoire.Hub.RedGreenProbeScratch`
  with `Path.Combine("data", "query-runs", "probe.md")` made the rule fail naming
  `Grimoire.Hub: Grimoire.Hub.RedGreenProbeScratch.RetiredPath → "query-runs"`;
  Green — scratch deleted, full `Grimoire.ArchTests` suite passed (35/35). Note:
  `GrimoirePathOptions.DefaultQueryRunsDirName` is a C# const whose literal is
  compiler-inlined at the use site, so its allowlist entry names
  `GrimoirePathResolver` (the IL site); Program.cs's `"--query-runs-dir"` maps to
  the namespace-less `Program` type — two entries, as specified.*

**Definition of Done**:

- [X] Rule (T001) written and committed with the two-entry allowlist documented
- [X] Red/Green probe (T002) completed with commit message documenting the result
- [X] `Grimoire.ArchTests` passes in CI with no active violations (probe code
  removed)

**Checkpoint**: The retired location is guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Minimal repo plumbing for the new runtime location. No new projects,
packages, or infrastructure are needed (plan.md Technical Context).

- [X] T003 Add `data/conversations/` to `.gitignore` (ADR-003/ADR-009 pattern,
  next to the existing `data/query-runs/` entry, which stays until T019 retires
  it).

**Checkpoint**: Runtime location is ignored; foundational work can begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The record format (writer + parser) and the
`ConversationRecordStore` every user story depends on, plus the ADR-009 path
composition for the new location. Additive only — the retired `query-runs`
config is deleted in the US1 cutover (T019) so the build stays green throughout.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Extend `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs`
  (add `ConversationsDir` property + `DefaultConversationsDirName = "conversations"`),
  `GrimoirePathResolver.cs` (resolve beneath the data dir, report as
  `conversations_dir`, auto-create as `PathLocationKind.WritableData`),
  `ResolvedGrimoirePaths.cs` (add
  `ConversationRecordPathFor(conversationId) => Path.Combine(ConversationsDir, $"{conversationId}.md")`),
  and `backend/src/Grimoire.Hub/Program.cs` (add `"--conversations-dir"` →
  `"Grimoire:Paths:ConversationsDir"` CLI mapping) — single composition point,
  ADR-009, no ambient discovery. `QueryRunsDir`/`QueryRunArtifactPathFor` remain
  untouched until T019.
- [X] T005 [P] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/QueryRuntimePathsTests.cs`:
  `conversations_dir` resolves beneath `data/` under default layout, honors
  explicit `--base`/`--conversations-dir`/env-var (`Grimoire__Paths__ConversationsDir`)
  overrides with correct source reporting, and is auto-created — mirrors the
  existing `query_runs_dir` cases (which are rewritten to `conversations_dir`
  expectations in T019).
- [X] T006 Implement `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs`
  — the **writer** half of `contracts/conversation-record-format.md`
  (`grimoire-conversation/1`): frontmatter (`conversation_id`, `created_at`,
  `record_format`) + `# Conversation <id>` heading on create; per-turn block =
  `<!-- grimoire:turn` bookkeeping YAML mapping (all fields of data-model.md's
  Turn Bookkeeping table incl. `prompt_chars`/`answer_chars` as exact UTF-16
  code-unit lengths) + `-->` + `## Turn {position} — {state}` + `### Prompt` /
  `### Answer` bodies written verbatim. Escaping rules per contract: string
  values (failure_reason, denied-action targets/reasons, paths) as double-quoted
  JSON-escaped strings with `-->` neutralized (its `>` written as the JSON
  unicode escape `\u003e`, per the contract's escaping rules).
- [X] T007 Implement `backend/src/Grimoire.Hub/QueryConversations/RecordedTurn.cs`
  (parsed turn: full bookkeeping + prompt/answer, plus the
  `{ position, prompt, answer, state }` context projection matching the existing
  `QueryPriorTurn` shape) and the **parser** half in
  `ConversationRecordFormat.cs`: frontmatter check (`record_format` mismatch ⇒
  unreadable), sentinel scan strictly outside length-consumed body ranges,
  length-delimited body slicing (never delimiter-scanning untrusted content),
  unknown bookkeeping keys tolerated (feature 012 forward-compat),
  trailing-incomplete-block recovery (drop fragment, WARN diagnostic, file still
  readable), any other structural violation ⇒ unreadable classification (contract
  Parsing rules 1–5). Depends on T006 (shared format constants).
- [X] T008 [P] Integration tests `backend/tests/Grimoire.IntegrationTests/ConversationRecordFormatTests.cs`:
  writer→parser round-trip preserves every bookkeeping field and body verbatim;
  injection fixtures — prompt/answer bodies containing `## Turn`, `### Answer`,
  and `<!-- grimoire:turn -->` sentinels parse without forged/broken structure;
  hostile denied-action strings (embedded `-->`, quotes, newlines) cannot
  terminate the comment early and unescape back to the original values;
  `answer_chars: 0` yields an empty body; trailing partial block is dropped with
  the remaining turns intact; truncated frontmatter / malformed bookkeeping YAML
  / body shorter than declared length each classify as unreadable.
- [X] T009 Implement `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordStore.cs`
  (concrete class, directly injected — persistence exemption, Constitution I /
  ADR-010): `AppendTurnAsync` creates the file (frontmatter + first block) in a
  single write on the conversation's first terminal turn, appends one complete
  block per later terminal turn with a single append-mode write, never modifies
  recorded bytes (FR-003), takes a per-conversation lock (defense in depth,
  research.md R3); maintains the in-memory per-conversation context cache on each
  append; `LoadContextAsync` serves from cache, hydrates by parsing the record
  file on cache miss (Hub restart), returns empty context for a missing file
  (new conversation), and surfaces a fail-closed unreadable result (never partial
  context) when parsing fails (research.md R5). Uses
  `ResolvedGrimoirePaths.ConversationRecordPathFor` (T004).
- [X] T010 [P] Integration tests `backend/tests/Grimoire.IntegrationTests/ConversationRecordStoreTests.cs`:
  create-on-first-append vs. append-on-later; earlier bytes byte-identical after
  a later append (never-rewrite, FR-003); `position` strictly increasing from 1;
  cache serves loads after appends without re-reading the file; cache miss
  hydrates from disk; missing file ⇒ empty context; unreadable file ⇒ fail-closed
  result; concurrent appends to two different conversations land in their own
  files only.
- [X] T011 [P] Extend `backend/tests/Grimoire.IntegrationTests/Fakes/FakeAgentProcess.cs`
  (and `Fakes/FakeModelClient.cs` if needed) so tests can script terminal-event
  metadata deterministically: denied actions (action/requested_target/
  canonical_target/reason/turn), instruction identity + sha256, policy identity/
  version/sha256, model, turns used — plus the existing mid-stream
  termination/silence controls the restart and interruption tests reuse. Only
  extend where 008's fakes don't already support it; keep existing scripted
  behavior byte-for-byte.

**Checkpoint**: Format, parser, and store exist and are hermetically verified in
isolation. User story implementation can now begin.

---

## Phase 3: User Story 1 - Read a conversation as one document (Priority: P1) 🎯 MVP

**Goal**: Every conversation produces exactly one durable, human-readable record
containing all turns in order (SC-001); the record replaces the per-turn artifact
mechanism (cutover, SC-004) and becomes the source of follow-up context (ADR-014,
SC-005 in-memory path); the browser submits only the prompt (FR-009: zero
user-facing behavior change).

**Independent Test**: Hold a 3-turn conversation (incl. a follow-up referencing
an earlier answer) via the hermetic harness; open the single record file and
verify all turns in order with complete prompts and answers, readable as one
dialogue; verify `data/query-runs/` gained no files and the captured agent
context equals the recorded transcript.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T012 [P] [US1] Integration test `backend/tests/Grimoire.IntegrationTests/ConversationRecordLifecycleTests.cs`
  (SC-001): scripted 3-turn conversation incl. a follow-up — exactly one file at
  `<base>/data/conversations/<conversationId>.md`; frontmatter with
  `record_format: grimoire-conversation/1`; `## Turn 1..3` blocks in position
  order, each with the full prompt and answer and complete bookkeeping matching
  the scripted terminal metadata; the referenced answer sits above the follow-up
  in the same document. Two concurrent conversations (within concurrency limit 3)
  each get exactly one record containing only their own turns
  (cross-contamination check).
- [X] T013 [P] [US1] Extend `backend/tests/Grimoire.IntegrationTests/QueryTurnSubmissionApiTests.cs`
  per `contracts/query-conversation-api.md`: 202 with Hub-assigned `position`
  (recorded turns + 1) for a body containing only `prompt`; 400 for
  `conversationId` violating `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$` (path-traversal
  fixtures: `../x`, `a/b`, 65 chars, leading `-`) with no turn created; a stale
  client still sending `priorTurns` is accepted and the extra field ignored
  (record stays authoritative); 409 `conversation_already_active` and 503
  `query_concurrency_limit_reached` semantics unchanged (FR-009).
- [X] T014 [P] [US1] Rework `backend/tests/Grimoire.IntegrationTests/QueryFollowUpContextTests.cs`
  (SC-005, in-memory path): capture the `QueryAgentRequest` handed to the
  launcher port on a follow-up submission and assert its `PriorTurns`
  tuple-equal (`position`, `prompt`, `answer`, `state`) to the turns parsed from
  the record file with the contract parser — incl. a prior interrupted turn whose
  partial answer must appear in both. Delete the browser-supplied-`priorTurns`
  propagation assertions this file carried from 008 (mechanism retired).
- [X] T015 [P] [US1] Integration test (in `ConversationRecordLifecycleTests.cs`,
  T012's file) for the cutover guarantee (SC-004, runtime half): after full turn
  lifecycles (completed, interrupted, failed), `<base>/data/query-runs/` does not
  exist or contains zero files.
- [X] T016 [P] [US1] Frontend tests: extend
  `frontend/src/lib/services/querySubmissionApi.test.ts` (request body contains
  exactly `prompt` — no `priorTurns` key) and
  `frontend/src/routes/query/page.svelte.test.ts` (follow-up submission sends
  only the prompt; conversation display, context hint, new-conversation action,
  and interrupt wiring behave exactly as in 008 — FR-009).

### Implementation for User Story 1

- [X] T017 [US1] Change `backend/src/Grimoire.Hub/QueryDispatch/QueryRunCoordinator.cs`:
  inject `ConversationRecordStore` (replacing `QueryRunArtifactWriter`); in
  `FinishTurnAsync`, after the `TryTransitionTo` first-transition-wins point,
  call `AppendTurnAsync` with the turn's full terminal data (prompt, accumulated
  partial-answer buffer, state, failure reason, timestamps from
  `QueryTurnState`, instruction/policy identity, model, turns used, denied
  actions from terminal-event metadata) **wrapped in a guard**: an append failure
  is logged/counted but never alters the turn's outcome nor suppresses the
  `PublishTurnChangedAsync` broadcast — fixing in passing the current unguarded
  `_artifactWriter.WriteAsync` call (line ~306) that would skip the publish on
  throw (research.md R6, spec edge case).
- [X] T018 [US1] Change `backend/src/Grimoire.Hub/QuerySubmission/QuerySubmissionEndpoints.cs`
  and `QuerySubmissionValidator.cs`: remove `priorTurns` from the request model
  (extra fields ignored by JSON binding); add the `conversationId` regex rule
  (400 per contract; prompt rules unchanged); load prior-turn context from
  `ConversationRecordStore.LoadContextAsync` and pass it into dispatch; assign
  `position` = recorded turn count + 1; return 500
  `{ "reason": "conversation_record_unreadable" }` fail-closed (no turn created,
  no agent spawned) when the store reports the record unreadable. Register
  `ConversationRecordStore` in `backend/src/Grimoire.Hub/Program.cs` DI.
- [X] T019 [US1] Cutover deletion (FR-007/FR-008): delete
  `backend/src/Grimoire.Hub/QueryRunArtifact/QueryRunArtifactWriter.cs` and the
  `QueryRunArtifact/` directory; delete `QueryRunsDir` +
  `DefaultQueryRunsDirName` from `GrimoirePathOptions.cs`, the `query_runs_dir`
  resolution/report/auto-create from `GrimoirePathResolver.cs`,
  `QueryRunArtifactPathFor` + the `QueryRunsDir` record component from
  `ResolvedGrimoirePaths.cs`, and the `"--query-runs-dir"` CLI mapping from
  `Program.cs`; rewrite the `query_runs_dir` cases in
  `PathConfiguration/QueryRuntimePathsTests.cs` to `conversations_dir`; update
  the artifact-asserting tests in `QueryTurnSubmissionApiTests.cs`,
  `QueryInstructionLoadTests.cs`, and `QueryReadOnlyGuardrailTests.cs` to assert
  against the Conversation Record instead; remove the `data/query-runs/` line
  from `.gitignore`. Then **empty T001's allowlist** and re-run the Red/Green
  probe: with the allowlist empty and the deletions in place the rule MUST pass;
  re-add a scratch `"query-runs"` literal, it MUST fail; remove it, it MUST pass
  (commit message documents this second probe).
- [X] T020 [US1] Frontend implementation: change
  `frontend/src/lib/services/querySubmissionApi.ts` (drop `priorTurns` from the
  request body/type) and `frontend/src/routes/query/+page.svelte` (stop
  assembling `priorTurns` on submission; the client-side conversation state stays
  for on-screen display only — UI/UX unchanged, FR-009; keep the T107 context
  hint, which remains accurate under record-sourced context). Touch
  `frontend/src/lib/components/QueryConversation.svelte` only if it references
  the submission payload shape.

### Observability for User Story 1 (co-located, plan.md ## Observability)

- [X] T021 [US1] Implement `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordLogEvents.cs`
  (extends `QueryLifecycleLogEvents.cs`'s idiom) defining all five
  `query.conversation.*` events with stable names and mandatory fields, and emit
  them at their triggers: `query.conversation.record_created` (INFO,
  `conversation_id`, `path`) and `query.conversation.turn_recorded` (INFO,
  `conversation_id`, `turn_id`, `position`, `outcome`) in the store/coordinator
  append path; `query.conversation.record_append_failed` (ERROR,
  `conversation_id`, `turn_id`, `reason`) in T017's guard;
  `query.conversation.context_loaded` (INFO, `conversation_id`, `turn_count`,
  `source`) and `query.conversation.record_load_failed` (ERROR,
  `conversation_id`, `reason`) in T018's submission path.
- [X] T022 [P] [US1] Deterministic integration tests
  `backend/tests/Grimoire.IntegrationTests/QueryConversationLogEventTests.cs`
  (mirrors `QueryLifecycleLogEventTests.cs`): validate event name, level, and
  every mandatory field for `record_created`, `turn_recorded`, and
  `context_loaded`; for `record_append_failed`, use a store rigged to throw
  (e.g. record path made unwritable) and additionally assert the isolation
  guarantee — the turn still reaches its terminal state and the
  `queryTurnChanged` publish still fires. (`record_load_failed` is validated in
  US3, T036.)
- [X] T023 [US1] Add the four `query.conversation.*` metrics to
  `backend/src/Grimoire.Hub/HubMetrics.cs`:
  `query.conversation.turns_recorded_total{outcome}`,
  `query.conversation.record_append_failures_total`,
  `query.conversation.context_loads_total{source=memory|record|empty}`,
  `query.conversation.record_load_failures_total`, emitted at the same trigger
  points as T021's events, within the active span context.
- [X] T024 [P] [US1] Deterministic integration tests
  `backend/tests/Grimoire.IntegrationTests/QueryConversationMetricsTests.cs`
  (mirrors `QueryLifecycleMetricsTests.cs`): `turns_recorded_total` increments
  with the correct `outcome` label on append, `record_append_failures_total` on
  the rigged append failure, `context_loads_total` with `source=memory` (cached)
  and `source=empty` (new conversation). (`source=record` and
  `record_load_failures_total` are covered in US3, T034/T036.)
- [X] T025 [US1] Add trace spans per plan.md:
  `hub.query.load_conversation_context` (child of `hub.query.submit`; attributes
  `conversation_id`, `turn_count`, `source`) around T018's context load, and
  `hub.query.record_turn` (child of `hub.query.run_supervision` for
  supervision-detected terminals; attributes `conversation_id`, `turn_id`,
  `outcome`) around T017's append — existing OTel bootstrap pattern, logs/metrics
  of the recording path emitted within these span contexts (ADR-005).
- [X] T026 [P] [US1] Deterministic integration tests
  `backend/tests/Grimoire.IntegrationTests/QueryConversationTraceTests.cs`
  (mirrors `QueryLifecycleTraceTests.cs`, in-memory exporter per ADR-005):
  validate span names, `hub.query.submit` → `hub.query.load_conversation_context`
  and `hub.query.run_supervision` → `hub.query.record_turn` parent/child
  linkage, and `turn_id`/`conversation_id` correlation attributes shared with
  the events/metrics of the same turn.
- [X] T027 [US1] Retire the vestigial `query_agent.finalize_artifact` span:
  remove its `StartActivity` block from
  `backend/src/Grimoire.QueryAgent/Program.cs` (line ~89; stdin/scaffold contract
  otherwise untouched — ADR-012 fingerprints must not drift) and update
  `backend/tests/Grimoire.IntegrationTests/QueryLifecycleTraceTests.cs` to assert
  the span is absent from a completed turn's trace.

**Checkpoint**: User Story 1 is fully functional and independently testable — one
readable record per conversation, cutover complete, record-sourced context, prompt-only
submissions, recording/context paths fully instrumented.

---

## Phase 4: User Story 2 - Bookkeeping preserved per turn (Priority: P2)

**Goal**: Every terminal state's turn (completed, interrupted, failed) carries
its complete Turn Bookkeeping inside the record — outcome, reasons, timestamps,
instruction/policy identity + hashes, denied actions with reasons, model/loop
usage — so auditability relocates without regressing (SC-002).

**Independent Test**: Produce turns ending `completed`, `interrupted`, and
`failed` (one with a scripted denied tool action) via the hermetic harness;
verify each turn's bookkeeping block carries state, reason, instruction hash,
and the denial with its reason.

### Tests for User Story 2

- [X] T028 [P] [US2] Append-on-terminal-transition per state (SC-001/SC-003
  groundwork): extend `backend/tests/Grimoire.IntegrationTests/QueryInterruptionTests.cs`
  — user-triggered interrupt appends a block with `state: interrupted`,
  `failure_reason: null`, and the accumulated partial answer; extend
  `backend/tests/Grimoire.IntegrationTests/QueryLivenessSupervisionTests.cs`
  (fake `TimeProvider`) — liveness-silence failure appends `state: failed` with
  the human-readable reason and the partial answer produced so far; a
  failed-before-any-output turn records an empty answer body (`answer_chars: 0`).
  (`completed` appends are covered by T012.) Each terminal transition appends
  **exactly one** block (first-transition-wins: interrupt racing supervision
  yields a single block).
- [X] T029 [P] [US2] Denied-action traceability test (SC-002) in
  `backend/tests/Grimoire.IntegrationTests/ConversationRecordBookkeepingTests.cs`:
  `FakeAgentProcess` scripts terminal metadata with denials (fixture: `read_file`
  out-of-scope) — the turn's `denied_actions` list matches the scripted denials
  exactly (`action`, `requested_target`, `canonical_target`, `reason`, `turn`);
  hostile target strings containing `-->`, quotes, and newlines survive the
  escape/unescape round-trip via the contract parser and cannot terminate the
  bookkeeping comment early.
- [X] T030 [P] [US2] Bookkeeping completeness test (SC-001 field-level, in
  `ConversationRecordBookkeepingTests.cs`, T029's file): for each terminal state,
  every field of data-model.md's Turn Bookkeeping table (`turn_id`, `position`,
  `state`, `failure_reason`, `started_at`/`completed_at` from `QueryTurnState`,
  `model`, `turns_used`, `instruction_file.path`/`.sha256`,
  `policy.path`/`.version`/`.sha256`, `denied_actions`,
  `prompt_chars`/`answer_chars`) equals the scripted terminal metadata /
  submitted prompt; an instruction-load-failure turn records nullable instruction
  identity without breaking the block.

### Implementation for User Story 2

- [X] T031 [US2] Close any terminal-metadata mapping gaps T028–T030 surface in
  `backend/src/Grimoire.Hub/QueryDispatch/QueryRunCoordinator.cs` /
  `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordStore.cs`
  (denied actions preserved verbatim from the ADR-006 terminal-event metadata,
  nullable instruction identity for pre-load failures, timestamps sourced from
  `QueryTurnState`), and add the user-interruption span-parent variant:
  `hub.query.record_turn` parented by the **interrupt HTTP request root** when
  the append is triggered by `InterruptAsync` (plan.md trace table).
- [X] T032 [P] [US2] Extend `backend/tests/Grimoire.IntegrationTests/QueryConversationTraceTests.cs`
  (T026's file): on user interruption, `hub.query.record_turn` is a child of the
  interrupt HTTP request span (not `hub.query.run_supervision`), with
  `outcome=interrupted` and correlated `turn_id`/`conversation_id`.

**Checkpoint**: User Stories 1 AND 2 hold independently — the record is a
complete audit superset of the retired per-turn artifacts.

---

## Phase 5: User Story 3 - Records survive what the browser doesn't (Priority: P3)

**Goal**: Records are durable across page reloads, browser loss, and Hub
restarts (SC-003); after a restart the Hub hydrates follow-up context from the
record file with the same consistency guarantee (SC-005 from-file path); a
structurally unreadable record fails closed rather than feeding the agent
unverifiable context (FR-006).

**Independent Test**: Finish two turns, kill the agent mid-third-turn, simulate a
Hub restart (re-instantiate coordinator + store over the same base dir); verify
the record contains the two finished turns completely and the third with its
partial answer and supervision-consistent terminal state, and that a follow-up
after restart receives context equal to the parsed record.

### Tests for User Story 3

- [ ] T033 [P] [US3] Integration test `backend/tests/Grimoire.IntegrationTests/ConversationRecordDurabilityTests.cs`
  (SC-003): two finished turns + one mid-stream kill (`FakeAgentProcess`
  termination / controlled silence with fake `TimeProvider`); re-instantiate
  coordinator + store over the same temp base dir (= Hub restart); the record on
  disk contains the finished turns byte-complete and the killed turn with its
  accumulated partial answer and the terminal state/reason the existing
  supervision rules determine — never truncated or rewritten.
- [ ] T034 [P] [US3] SC-005 hydration variant (in
  `ConversationRecordDurabilityTests.cs`, T033's file): after the simulated
  restart (cold cache), submit a follow-up to the same `conversationId` — the
  captured `QueryAgentRequest.PriorTurns` tuple-equals the record parsed with
  the contract parser; `query.conversation.context_loaded` reports
  `source=record` and `query.conversation.context_loads_total{source=record}`
  increments (from-file counterpart to T014/T024).
- [ ] T035 [P] [US3] Fail-closed corrupt-record test
  `backend/tests/Grimoire.IntegrationTests/ConversationRecordFailClosedTests.cs`
  (FR-006): corrupt an existing record (truncated frontmatter; malformed
  bookkeeping YAML; body shorter than declared length) — a follow-up submission
  returns 500 `{ "reason": "conversation_record_unreadable" }`, no turn is
  created, no agent process is spawned; a **trailing** incomplete block alone is
  NOT unreadable — the submission succeeds with context = the complete recorded
  turns and a WARN diagnostic for the dropped fragment (contract Parsing rule 4
  vs. 5); starting a new conversation afterwards works normally.
- [ ] T036 [P] [US3] Failure-row observability tests (in
  `ConversationRecordFailClosedTests.cs`, T035's file, plus
  `QueryConversationLogEventTests.cs`/`QueryConversationMetricsTests.cs`
  extensions): `query.conversation.record_load_failed` emitted at ERROR with
  `conversation_id` and `reason`, and
  `query.conversation.record_load_failures_total` incremented, on the
  fail-closed rejection — completing the log/metric contract rows T022/T024
  deferred.

### Implementation for User Story 3

- [ ] T037 [US3] Close any durability/hydration gaps T033–T036 surface in
  `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordStore.cs` and
  `backend/src/Grimoire.Hub/QuerySubmission/QuerySubmissionEndpoints.cs`
  (cache hydration correctness after restart, WARN diagnostic for the trailing
  fragment, unreadable-classification propagation to the 500 path). Expected to
  be small — the store (T009) and endpoint (T018) were built with these
  semantics; this task exists so the story has an explicit implementation home
  if the tests find drift.

**Checkpoint**: All user stories are independently functional — the record is
durable, hydration-consistent, and fail-closed.

---

## Phase 6: Polish, Cross-Cutting Verification & Completeness Audit

**Purpose**: The mandatory completeness audit (Constitution Principle III/IV),
CI-enforcement confirmation for the logging/trace contracts, the feature-008
regression guarantee, and final validation. Observability implementation + tests
are co-located in Phases 3–5 above (sanctioned placement); the audit below is
what gates the DoD.

- [ ] T038 **Completeness audit** (MANDATORY, named — Constitution Principle
  III/IV): cross-reference every row of `plan.md ## Observability` against its
  implementing task and passing test — metrics
  `query.conversation.turns_recorded_total` (T023/T024),
  `record_append_failures_total` (T023/T024), `context_loads_total`
  (T023/T024/T034), `record_load_failures_total` (T023/T036); log events
  `record_created` (T021/T022), `turn_recorded` (T021/T022),
  `record_append_failed` (T021/T022), `context_loaded` (T021/T022/T034),
  `record_load_failed` (T021/T036); spans `hub.query.load_conversation_context`
  (T025/T026) and `hub.query.record_turn` incl. both parent variants
  (T025/T026, T031/T032); the retired `query_agent.finalize_artifact` span
  (T027); and the unchanged-008-signals clause (their existing tests still
  passing) — **and** every success criterion SC-001 (T012), SC-002 (T029),
  SC-003 (T033), SC-004 (T001/T019 structural + T015 runtime), SC-005
  (T014 in-memory + T034 from-file), all deterministic (no agent-judgment
  criteria exist in this spec). File any gap found as a new task in this
  tasks.md before declaring the DoD met.
- [ ] T039 Logging-contract CI enforcement (MANDATORY — Constitution Principle
  IV): confirm the new logging tests (T022, T036) run in the standard PR
  pipeline — they live in `Grimoire.IntegrationTests`, executed unfiltered by
  `.github/workflows/ci.yml`'s "Run hermetic integration tests" step; verify no
  test filter/category excludes them and document the verification in the task
  close-out (no workflow change expected).
- [ ] T040 Trace-contract CI enforcement (MANDATORY — Constitution Principle
  IV): same confirmation for the trace tests (T026, T032) under "Run hermetic
  integration tests" and for the T001 tripwire under "Run architecture tests"
  in `.github/workflows/ci.yml`.
- [ ] T041 Feature-008 evaluation regression (spec: "must still pass
  unchanged"): run `dotnet test backend/tests/Grimoire.AgentEvals
  --configuration Release` — all four query replay scenarios
  (`query-grounding-covered`, `query-grounding-uncovered`, `query-follow-up`,
  `query-read-only-decline`) report `Trusted`/`ThresholdMet` with `Skipped: 0`,
  proving the agent stdin contract and replay fingerprints did not drift
  (ADR-012, research.md R7). If a fingerprint drifts, that is a defect in this
  feature's changes — fix the drift; do NOT re-capture recordings.
- [ ] T042 [P] Annotate
  `docs/adr/ADR-011-query-agent-shared-runtime-and-concurrency-model.md` with a
  partial-supersession pointer: its
  "Persistence and conversation context" section is superseded by ADR-014;
  everything else remains in force (MADR status hygiene, Constitution
  Principle III).
- [ ] T043 Full-suite verification: `dotnet test backend/tests/Grimoire.ArchTests`,
  `backend/tests/Grimoire.Domain.UnitTests`,
  `backend/tests/Grimoire.IntegrationTests` (all green, zero skips),
  `dotnet format --verify-no-changes` on `backend/`, and the frontend gates
  (`npm run check`, lint, `npm test`, build) in `frontend/` — mirrors
  `.github/workflows/ci.yml`.
- [ ] T044 Run `specs/011-query-conversations/quickstart.md` scenarios 1–5 plus
  its Observability check against a live local Hub (manual validation; the only
  non-hermetic step, requires an API key for the live conversation), and record
  the outcome in the task close-out.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural)**: No dependencies — MUST be first; blocks everything.
- **Phase 1 (Setup)**: After Phase 0.
- **Phase 2 (Foundational)**: After Phase 1 — BLOCKS all user stories.
  Within: T004 → T005; T006 → T007 → T008; T004+T007 → T009 → T010; T011 independent.
- **Phase 3 (US1)**: After Phase 2. Within: tests T012–T016 first (failing);
  T017 → T018 → T019 (cutover last — it deletes what T017/T018 replace and
  closes T001's allowlist); T020 after T018 (API shape fixed); observability
  T021–T027 after T017/T018 (trigger points exist); T027 independent of
  T021–T026.
- **Phase 4 (US2)**: After Phase 3 (records + append path exist). T028–T030
  first; T031 → T032.
- **Phase 5 (US3)**: After Phase 3 (store + endpoint exist); independent of
  Phase 4. T033–T036 first; T037 closes gaps.
- **Phase 6 (Polish)**: After Phases 3–5. T038 gates the DoD; T041/T043/T044
  are final gates; T039/T040/T042 anytime within the phase.

### User Story Dependencies

- **US1 (P1)**: Only on Foundational. Delivers the MVP: record, cutover,
  record-sourced context, prompt-only submission.
- **US2 (P2)**: Builds on US1's append path; independently testable via its own
  per-state and denial fixtures.
- **US3 (P3)**: Builds on US1's store/endpoint; independent of US2 (can run in
  parallel with Phase 4).

### Parallel Opportunities

- Phase 2: T005 ∥ T008 ∥ T010 ∥ T011 (after their respective impl tasks).
- Phase 3: T012–T016 all parallel (different files); T022/T024/T026 parallel
  after T021/T023/T025.
- Phase 4 ∥ Phase 5: US2 and US3 touch disjoint test files and independent
  implementation seams.
- Phase 6: T039 ∥ T040 ∥ T042.

---

## Parallel Example: User Story 1

```bash
# After Phase 2, launch all US1 test tasks together (they must fail first):
Task: "T012 ConversationRecordLifecycleTests.cs — SC-001 one record, order, concurrency isolation"
Task: "T013 QueryTurnSubmissionApiTests.cs — prompt-only body, Hub position, conversationId 400, stale priorTurns"
Task: "T014 QueryFollowUpContextTests.cs — SC-005 captured PriorTurns == parsed record"
Task: "T015 cutover guarantee — zero files under data/query-runs/"
Task: "T016 frontend — request body contains only prompt"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 0 (tripwire + probe) → Phase 1 → Phase 2 (format/parser/store proven in
   isolation).
2. Phase 3 completely — this is the feature: one readable record, cutover,
   record-sourced context, prompt-only submissions, instrumented.
3. **STOP and VALIDATE**: quickstart Scenario 1 + 4; SC-001/SC-004/SC-005
   (in-memory) hold.

### Incremental Delivery

1. Add US2 → per-turn audit parity with the retired artifacts (SC-002) →
   validate quickstart Scenario 2.
2. Add US3 → durability + hydration + fail-closed (SC-003, SC-005 from-file,
   FR-006) → validate quickstart Scenarios 3 + 5.
3. Phase 6 → completeness audit, CI-enforcement confirmation, 008 eval
   regression, full gates → DoD.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- Verify story tests fail before implementing (Red → Green → Refactor).
- All tests hermetic except T044 (manual quickstart run); no test requires an
  API key or network.
- The cutover (T019) is deliberately the last implementation step of US1 so the
  build never has a window where neither persistence mechanism exists; T001's
  allowlist is emptied there, making the tripwire's guarantee unconditional
  from that commit onward.
- Feature 008 signal names, tests, and replay recordings are contract: nothing
  in this feature may rename, re-capture, or weaken them (T041 enforces).

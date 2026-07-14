# Tasks: Single Agent System Prompt & Configurable Ingest Submission

**Input**: Design documents from `/specs/004-ingest-agent-systemprompt/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R13), data-model.md,
contracts/ (`ingest-submission-api-extension.md`, `ingest-agent-cli.md`,
`agent-run-events.md`), quickstart.md. ADR-007 and ADR-008 are **Accepted**.

**Feature dependency**: implementation assumes feature 003
(`specs/003-ingest-intake-webui`) is merged — Hub `IngestSubmission/`,
`Conversion/`, `AgentDispatch/IngestRunGate`, realtime channel, and the frontend
form/board come from 003 (verified in T002).

**Tests**: required — deterministic hermetic tests per user story (Principle II),
observability contract tests and agent-behavior evals in the final phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (system prompt), US2 (user prompt), US3 (convert steps),
  US4 (events/supervision/queue)

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Guard the new ADR-008 boundary before any feature code exists.
ADR-007's enforcement is integration-level (single-document fail-closed loading,
T012/T013); the structural rule needed up front is ADR-008's non-blocking dispatch.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [X] T001 Write and verify the ADR-008 structural boundary test in
      `backend/tests/Grimoire.ArchTests/NonBlockingDispatchRuleTests.cs`: within
      `Grimoire.Hub`'s `AgentDispatch` namespace, no dispatch/scheduling code path may
      synchronously wait on agent process exit for run outcome (rule: no references to
      `Process.WaitForExit`/`WaitForExitAsync` results used as run outcome outside the
      supervisor's termination helper; implement as NetArchTest/Roslyn source check
      consistent with existing ArchTests style).
      **Red/Green probe**: add a deliberately violating `_ProbeBlockingDispatcher.cs`
      that awaits `WaitForExitAsync` to derive outcome → test MUST fail → delete probe
      → test MUST pass. Commit message documents the probe result.

**Checkpoint**: ADR-008 boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup

- [X] T002 Verify feature-003 prerequisites exist in the working tree
      (`backend/src/Grimoire.Hub/IngestSubmission/`, `Conversion/`,
      `AgentDispatch/IngestRunGate.cs`, `Realtime/`,
      `frontend/src/lib/components/SubmissionForm.svelte`); if absent, stop and
      rebase/merge feature 003 first — do not reimplement 003 surface in this feature.
- [X] T003 Confirm existing test gates are green before changes: run
      `dotnet test backend/tests/Grimoire.ArchTests backend/tests/Grimoire.Domain.UnitTests backend/tests/Grimoire.IntegrationTests`
      and frontend `bun run test` (or `npx vitest run`) as the pre-feature baseline.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: CLI/dispatch argument surface shared by US1, US2, and US4.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Rework agent CLI options in
      `backend/src/Grimoire.IngestAgent/AgentCliOptions.cs` and argument parsing in
      `backend/src/Grimoire.IngestAgent/Program.cs`: remove `--instructions-dir`; add
      required `--system-prompt-path` and `--default-user-prompt-path`, optional
      `--user-prompt` and `--heartbeat-seconds` (default 10) per
      contracts/ingest-agent-cli.md.
- [X] T005 Update the Hub dispatcher invocation in
      `backend/src/Grimoire.Hub/AgentDispatch/IngestAgentDispatcher.cs` (and
      `IngestAgentRequest.cs`) to pass the new arguments; keep ADR-004 credential
      injection unchanged.
- [X] T006 [P] Update `Grimoire.AgentEvals` invocation plumbing in
      `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` to the new CLI/loader
      surface so eval runs compile against the reworked options.

**Checkpoint**: Foundation ready — user stories can begin.

---

## Phase 3: User Story 1 — Maintain agent behavior in one honest place (Priority: P1) 🎯 MVP

**Goal**: One versioned `agents/ingest/system-prompt.md` is the agent's entire system
prompt; the misleading `CLAUDE.md`/`SKILL.md` pair is gone; fail-closed loading and
SHA-256 traceability preserved (FR-001–FR-005).

**Independent Test**: quickstart Scenarios 1–2 — marker edit provably reaches the
system prompt (hash match on the task artifact); missing/empty document fails closed
before any wiki write.

### Tests for User Story 1 (write first, must fail before implementation)

- [X] T007 [P] [US1] Extend
      `backend/tests/Grimoire.IntegrationTests/InstructionContextTests.cs`: system
      prompt delivered to `IModelClient` is byte-exact file content of a
      `system-prompt.md` fixture; task artifact records exactly one instruction entry
      whose SHA-256 matches the file (SC-001).
- [X] T008 [P] [US1] Extend
      `backend/tests/Grimoire.IntegrationTests/InstructionLoadFailureTests.cs`:
      missing, whitespace-only, and unreadable `system-prompt.md` each fail the run
      before any wiki write with a human-readable reason; leftover legacy
      `CLAUDE.md`/`skills/` files are ignored when present (SC-002; ADR-007
      integration enforcement).

### Implementation for User Story 1

- [X] T009 [US1] Author `agents/ingest/system-prompt.md` merging the full behavioral
      content of `agents/ingest/CLAUDE.md` and
      `agents/ingest/skills/wiki-maintenance/SKILL.md` (operating rules, page types,
      frontmatter, tags, confidence, supersession, index/log upkeep, injection
      defence, final summary) as one continuous instruction text; delete
      `agents/ingest/CLAUDE.md` and `agents/ingest/skills/` (FR-001, FR-002).
- [X] T010 [US1] Replace `InstructionSetLoader` with `SystemPromptLoader` in
      `backend/src/Grimoire.IngestAgent/AgentCore/SystemPromptLoader.cs` (single file,
      verbatim, fail-closed, SHA-256; delete
      `backend/src/Grimoire.IngestAgent/AgentCore/InstructionSetLoader.cs`) and wire
      it in `backend/src/Grimoire.IngestAgent/Program.cs` (FR-003).
- [X] T011 [US1] Record the single instruction entry (path + SHA-256, existing list
      shape) in `backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactDocument.cs`
      / `TaskArtifactStore.cs` (FR-004).
- [X] T012 [US1] Adapt log events `ingest.instructions.loaded` /
      `ingest.instructions.load_failed` (fields: `task_id`, `path`, `sha256`) in
      `backend/src/Grimoire.IngestAgent/IngestAgentLogEvents.cs` and the
      `ingest_agent.load_instructions` span attributes
      (`task_id`, `system_prompt_sha256`) in
      `backend/src/Grimoire.IngestAgent/Program.cs`.
- [X] T013 [US1] Deterministic log/trace contract tests for T012 in
      `backend/tests/Grimoire.IntegrationTests/ObservabilityLogTests.cs` and
      `ObservabilityTraceTests.cs` (event name/level/fields; span name, parent
      `ingest_agent.run`, correlation `task_id`).
- [X] T014 [US1] Update eval fixtures to the single document:
      `backend/tests/Grimoire.AgentEvals/InstructionChangeAdoptionEvals.cs` edits
      `system-prompt.md` instead of `SKILL.md`/`CLAUDE.md`;
      `ConventionAdherenceEvals.cs` references updated paths (thresholds unchanged).

**Checkpoint**: US1 fully functional — consolidation shippable as MVP.

---

## Phase 4: User Story 2 — Steer an individual ingest with a custom user prompt (Priority: P2)

**Goal**: Default steering text lives in `agents/ingest/default-user-prompt.md`,
is shown and editable per submission, always wrapped by the harness scaffold, and the
effective prompt is recorded on the task artifact (FR-006–FR-010).

**Independent Test**: quickstart Scenarios 3–4 — default shown and editable; custom
prompt recorded and used; empty → default; oversized rejected before task creation.

### Tests for User Story 2 (write first, must fail before implementation)

- [X] T015 [P] [US2] New integration tests in
      `backend/tests/Grimoire.IntegrationTests/UserPromptTests.cs`: effective-prompt
      resolution (override wins; absent/empty → default file; missing default +
      no override fails closed before wiki writes); scaffold always wraps the prompt
      (`<source>` delimiters + injection framing present regardless of input);
      artifact records `user_prompt_source` + `## User Prompt` verbatim (SC-003
      prompt part); adversarial prompt "ignore your write restrictions" changes no
      guardrail outcome (SC-005, scripted `FakeModelClient` out-of-scope write is
      still denied).
- [X] T016 [P] [US2] New Hub API tests in
      `backend/tests/Grimoire.IntegrationTests/SubmissionPromptApiTests.cs`:
      `GET /api/ingest-submissions/defaults` returns verbatim
      `default-user-prompt.md` content + `userPromptMaxLength` 8000 (500 with reason
      when the file is missing/empty); `POST` with >8,000-char prompt → 400
      `user_prompt_too_long` and no task created (FR-010); accepted responses carry
      `userPromptSource` per contract.

### Implementation for User Story 2

- [X] T017 [US2] Create `agents/ingest/default-user-prompt.md` with the steering text
      extracted from `AgentLoop.BuildUserMessage` (integration instruction, judgment
      framing) — scaffold parts (task id, source ref, `<source>` block, injection
      warning) stay in code.
- [X] T018 [US2] Rework `backend/src/Grimoire.IngestAgent/AgentCore/AgentLoop.cs` +
      `Program.cs`: resolve effective prompt (`--user-prompt` else default file,
      fail-closed), scaffold wraps it; record `user_prompt_source` frontmatter and
      `## User Prompt` body section via
      `backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactDocument.cs` (FR-008,
      FR-009).
- [X] T019 [US2] Emit `ingest.agent.user_prompt_resolved` (INFO; `task_id`,
      `prompt_source`, `prompt_length`) in
      `backend/src/Grimoire.IngestAgent/IngestAgentLogEvents.cs`; add `prompt_source`
      attribute to `ingest_agent.load_instructions` span.
- [X] T020 [US2] Hub: defaults endpoint + prompt intake in
      `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs`,
      validation (≤ 8,000, empty → default) in `IngestSubmissionValidator.cs`,
      pass-through to dispatcher in `IngestSubmissionPipeline.cs`; response fields per
      contracts/ingest-submission-api-extension.md (FR-006, FR-007).
- [X] T021 [US2] Hub observability: metric `wiki.ingest.user_prompt_total`
      (`source` label) in `backend/src/Grimoire.Hub/HubMetrics.cs`; log events
      `ingest.submission.prompt_config` (INFO; `task_id`, `prompt_source`,
      `prompt_length`) and `ingest.submission.config_rejected` (WARN; `source_kind`,
      `reason`) in
      `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionLogEvents.cs`.
- [X] T022 [US2] Deterministic log/metric contract tests for T019/T021 in
      `backend/tests/Grimoire.IntegrationTests/ObservabilityLogTests.cs` and
      `ObservabilityMetricsTests.cs`.
- [X] T023 [US2] Frontend: prompt editor prefilled from defaults endpoint in
      `frontend/src/lib/components/SubmissionForm.svelte` + defaults/service call in
      `frontend/src/lib/services/` (new `ingestDefaults.ts`).
- [X] T024 [P] [US2] Frontend tests in
      `frontend/src/lib/components/SubmissionForm.svelte.test.ts`: default prefill,
      edit, clear-means-default hint, max-length validation message.

**Checkpoint**: US1 + US2 independently functional.

---

## Phase 5: User Story 3 — Control convert steps per submission (Priority: P3)

**Goal**: Named convert-step configuration per submission (only `markitdown` today):
visible toggles, byte-identical pass-through when disabled, early rejection for
binary formats, configuration recorded (FR-011–FR-015).

**Independent Test**: quickstart Scenarios 5–7 — URL with conversion disabled stores
checksum-identical content; PDF with conversion disabled → 422 before task creation;
untouched defaults behave exactly like 003.

### Tests for User Story 3 (write first, must fail before implementation)

- [X] T025 [P] [US3] New integration tests in
      `backend/tests/Grimoire.IntegrationTests/ConvertStepTests.cs`: registry
      validation (unknown step 400, not-applicable 400, required-disabled 422 — all
      before task creation); disabled `markitdown` for text/URL stores byte-identical
      normalized artifact with checksum over unmodified bytes (SC-004); default
      config reproduces 003 behavior (FR-015); artifact frontmatter records
      `convert_steps` (SC-003 step part).

### Implementation for User Story 3

- [X] T026 [US3] Convert-step registry (name, appliesTo, requiredFor, defaultEnabled)
      as new `backend/src/Grimoire.Hub/IngestSubmission/ConvertStepRegistry.cs`;
      request validation in
      `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionValidator.cs`
      (FR-011, FR-013).
- [X] T027 [US3] Skip path in
      `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionPipeline.cs` +
      `backend/src/Grimoire.Hub/Conversion/SourceArtifactStore.cs`: persist content
      as received (no conversion) when disabled; record applied configuration in the
      Hub-written task artifact
      (`backend/src/Grimoire.Hub/IngestSubmission/TaskArtifactFrontmatter.cs`)
      (FR-012, FR-014).
- [X] T028 [US3] API fields `convertSteps` on POST/detail/board responses in
      `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs` per
      contract.
- [X] T029 [US3] Hub observability: metric `wiki.ingest.convert_step_disabled_total`
      (`step` label) in `backend/src/Grimoire.Hub/HubMetrics.cs`; log event
      `ingest.submission.convert_config` (INFO; `task_id`, `step`, `enabled`) in
      `IngestSubmissionLogEvents.cs`; span `ingest_submission.apply_convert_config`
      (parent: 003 submission pipeline span; attrs `task_id`, `step`, `enabled`) in
      `IngestSubmissionPipeline.cs`.
- [X] T030 [US3] Deterministic log/metric/trace contract tests for T029 in
      `backend/tests/Grimoire.IntegrationTests/ObservabilityLogTests.cs`,
      `ObservabilityMetricsTests.cs`, `ObservabilityTraceTests.cs`.
- [X] T031 [US3] Frontend: step toggles with applicability/required rules in
      `frontend/src/lib/components/SubmissionForm.svelte` (registry data from the
      defaults endpoint, T023 service).
- [X] T032 [P] [US3] Frontend tests in
      `frontend/src/lib/components/SubmissionForm.svelte.test.ts`: toggle rendering
      per source kind, required-step lock for PDF/Office with explanation.

**Checkpoint**: US1–US3 independently functional.

---

## Phase 6: User Story 4 — Non-blocking agent runs with live loop activity (Priority: P4)

**Goal**: NDJSON event channel over agent stdout; Hub supervises via liveness window
(sole failure authority); persistent FIFO queue with paused-after-restart + explicit
resume/re-trigger replaces `IngestRunGate` (FR-016–FR-022; ADR-008).

**Independent Test**: quickstart Scenarios 8–10 — three rapid submissions accepted
instantly with one process at a time; kill mid-run → failed within window and queue
advances; restart → queue intact, paused, manual resume.

### Tests for User Story 4 (write first, must fail before implementation)

- [X] T033 [US4] Test support: fake agent executable + scripted NDJSON event-stream
      fixtures in `backend/tests/Grimoire.IntegrationTests/Fakes/FakeAgentProcess.cs`
      (scriptable: event sequences, silence, malformed lines, exit-without-terminal),
      controllable clock for the supervisor.
- [X] T034 [P] [US4] Supervision tests in
      `backend/tests/Grimoire.IntegrationTests/RunSupervisionTests.cs`: silence after
      `started`/`activity` → `failed` with liveness reason at window expiry, leftover
      process terminated, queue advanced (SC-009); terminal events end supervision;
      late events recorded, state unchanged (FR-022); malformed lines skipped without
      failing the run; process exit alone does not transition the run.
- [X] T035 [P] [US4] Queue tests in
      `backend/tests/Grimoire.IntegrationTests/RunQueueTests.cs`: three rapid
      submissions → immediate acceptance, ≤ 1 concurrent process, FIFO auto-advance
      (SC-008); simulated Hub restart (host rebuild over same SQLite temp file) →
      rows intact, `queue_paused = true`, nothing starts; whole-queue resume and
      single-task re-trigger re-arm processing; retrigger 409 for non-queued tasks
      (SC-010).
- [X] T036 [P] [US4] Realtime propagation test in
      `backend/tests/Grimoire.IntegrationTests/RunActivityRealtimeTests.cs` (003
      SignalR wire pattern): scripted `activity` events reach a connected client as
      `run_activity` within 2 s p95 (SC-011).

### Implementation for User Story 4

- [X] T037 [US4] Agent event emission: new
      `backend/src/Grimoire.IngestAgent/AgentCore/RunEventEmitter.cs` (NDJSON on
      stdout: `started`, `heartbeat` timer per `--heartbeat-seconds`, `completed`
      with summary, `failed` with reason per contracts/agent-run-events.md); route all
      agent console logging to stderr in `Program.cs` (FR-017).
- [X] T038 [US4] Loop activity events: emit `activity` (modelTurns, toolCalls,
      toolCallsByName, currentAction) from
      `backend/src/Grimoire.IngestAgent/AgentCore/AgentLoop.cs` on each loop step —
      loop mechanics only (Principle V).
- [X] T039 [US4] Hub event intake: new
      `backend/src/Grimoire.Hub/AgentDispatch/RunEventReader.cs` reading the child's
      stdout line-by-line, tolerant parsing, dispatch to supervisor + realtime
      publisher; metric `wiki.ingest.run_events_total` (`event_type` label) in
      `HubMetrics.cs`; span `ingest_hub.handle_run_event` (parent
      `ingest_hub.run_supervision`; attrs `task_id`, `event_type`); log
      `ingest.run.late_event` (WARN; `task_id`, `event_type`).
- [X] T040 [US4] Run supervision: new
      `backend/src/Grimoire.Hub/AgentDispatch/RunSupervisor.cs` — lastEventAt
      tracking, configurable liveness window (default 60 s), on expiry mark run
      `failed` (liveness reason), terminate leftover process, advance queue; span
      `ingest_hub.run_supervision` (root; attrs `task_id`, `outcome`,
      `last_event_type`); log `ingest.run.liveness_failed` (ERROR; `task_id`,
      `seconds_since_last_event`, `liveness_window_seconds`); metric
      `wiki.ingest.liveness_failures_total` (FR-020).
- [X] T041 [US4] Non-blocking queue-driven dispatch: replace
      `backend/src/Grimoire.Hub/AgentDispatch/IngestRunGate.cs` with queue scheduling
      in `IngestAgentDispatcher.cs` + queue persistence (rows keyed by acceptance
      time, `queue_paused` flag, startup pause when queued rows exist) in
      `backend/src/Grimoire.Hub/OperationalState/OperationalStateRepository.cs`;
      metric `wiki.ingest.queue_depth`; logs `ingest.queue.enqueued` (INFO; `task_id`,
      `queue_position`), `ingest.queue.advanced` (INFO; `task_id`),
      `ingest.queue.paused_after_restart` (WARN; `queued_count`),
      `ingest.queue.resumed` (INFO; `task_id`, `scope`) (FR-016, FR-019, FR-021).
- [X] T042 [US4] Queue/activity API surface in
      `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs`:
      `POST /api/ingest-queue/resume`, `POST /api/ingest-submissions/{taskId}/retrigger`
      (404/409 rules), `queuePosition`/`queuePaused` on board response, `runActivity`
      on detail response; publish `run_activity` on the 003 SignalR channel via
      `backend/src/Grimoire.Hub/Realtime/IngestLifecyclePublisher.cs` (FR-018).
- [X] T043 [US4] Deterministic log/metric/trace contract tests for T039–T041 rows in
      `backend/tests/Grimoire.IntegrationTests/ObservabilityLogTests.cs`,
      `ObservabilityMetricsTests.cs`, `ObservabilityTraceTests.cs` (names, levels,
      mandatory fields; span parentage `handle_run_event` → `run_supervision`;
      `task_id` correlation).
- [X] T044 [US4] Frontend: live activity in the task detail view
      (`frontend/src/lib/components/TaskCard.svelte` / detail route), queue position
      on cards, paused banner + resume/re-trigger actions on the board route; extend
      `frontend/src/lib/services/ingestLifecycleClient.ts` for `run_activity` and the
      two new endpoints.
- [X] T045 [P] [US4] Frontend tests: activity rendering, queue position, paused
      banner + resume action in `frontend/src/lib/components/` `.test.ts` files.

**Checkpoint**: All four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns (DoD gates)

- [X] T046 Observability completeness audit (MANDATORY — Principle IV): verify every
      row of plan.md ## Observability (5 metrics, 11 log events, 4 spans) is emitted
      and covered by a deterministic test (T013, T022, T030, T043); fill any gaps in
      `backend/tests/Grimoire.IntegrationTests/Observability*.cs`. The mapping table
      below is the audit checklist.
- [X] T047 CI enforcement (MANDATORY — Principle IV): confirm the logging/trace/
      metric contract tests run in the standard PR pipeline
      (`.github/workflows/` backend job runs `Grimoire.IntegrationTests`; frontend
      job runs the new component tests — adjust for the bun toolchain if the
      workflow switched from npm); fail the build on violation.
- [ ] T048 Agent-behavior evaluation SC-006 (MANDATORY — Principles II & V): run
      `backend/tests/Grimoire.AgentEvals` convention-adherence +
      instruction-change-adoption suites against the consolidated
      `agents/ingest/system-prompt.md` at 002's thresholds; document results.
      **BLOCKED** in this environment: requires `GRIMOIRE_EVAL=1` +
      `ANTHROPIC_AUTH_TOKEN` (live model credentials), neither available here. The
      suites themselves are already wired to `system-prompt.md` (T014) and compile/
      skip correctly; only the actual sampled run + result documentation remains.
- [X] T049 Agent-behavior evaluation SC-007 (MANDATORY — Principles II & V): new
      `backend/tests/Grimoire.AgentEvals/SteeringAdoptionEvals.cs` — ≥ 10
      source/steer pairs, LLM-judge rubric scoring summary + touched pages against
      the steer, threshold ≥ 90%. Written and verified to compile + correctly skip
      without live credentials (same gating as the other 5 eval suites); actual
      sampled execution requires `GRIMOIRE_EVAL=1` + `ANTHROPIC_AUTH_TOKEN`, not
      available in this environment (see T048).
- [X] T050 [P] Update stale doc references to `CLAUDE.md`/`SKILL.md`/
      `--instructions-dir` in `specs/001-ingest-minimal/` quickstart snippets is NOT
      required (historic records); update only living docs: `README`-level run
      instructions if they mention the old CLI flags. Verified: no `README.md`
      exists in this repository; the only other hit
      (`docs/decision-context-overview.md`) is North-Star source material per
      CLAUDE.md's Document Map (audited via `/drift-check`, not a living doc) — no
      changes needed.
- [X] T051 Run quickstart.md Scenarios 1–10 end-to-end and record outcomes in the PR
      description. Scenarios 1–7: covered deterministically by
      `Grimoire.IntegrationTests` (131/131 passing) per quickstart.md's own
      "Automated verification" mapping. Scenarios 8 and 10: live-verified against a
      real running Hub (3 rapid submissions accepted immediately with 202, processed
      strictly one-at-a-time in FIFO order; a burst of 8 submissions left 2 tasks
      `queued` with correct positions across a Hub restart with `queuePaused: true`,
      and `POST /api/ingest-queue/resume` correctly re-armed automatic FIFO
      processing). Scenario 9 (liveness-window failure, live activity counters):
      covered deterministically by `RunSupervisionTests.cs` / `RunActivityRealtimeTests.cs`
      via a scripted fake agent process and controllable clock — more reliable than a
      live 60s wall-clock wait for the same assertion.
- [ ] T052 [P] Propose the Principle V example-wording PATCH via
      `/speckit-constitution` (research R13) — separate follow-up, not blocking this
      feature's DoD.

---

## Phase 8: User Story 4 addition — Board connection-health indicator (2026-07-14 clarification, FR-023/SC-012)

**Goal**: A persistent Connected/Reconnecting/Disconnected badge near the board page's
header, projected client-side from the existing SignalR connection's own lifecycle
callbacks (research R14; plan.md 2026-07-14 addition) — no new backend surface.

**Independent Test**: quickstart Scenario 11 — indicator shows Connected while the Hub
is reachable, Reconnecting while the client's automatic reconnect is in flight,
Disconnected once reconnect attempts are exhausted; no page reload required at any
transition.

### Tests for this addition (write first, must fail before implementation)

- [X] T053 [P] [US4] New frontend test
      `frontend/src/lib/components/ConnectionStatusIndicator.svelte.test.ts`: renders
      Connected/Reconnecting/Disconnected per prop state with distinct styling and a
      stable `data-testid`/`data-connection-state` attribute (mirroring
      `StatusBadge.svelte`'s pattern).
- [X] T054 [P] [US4] New frontend test
      `frontend/src/lib/services/ingestLifecycleClient.svelte.test.ts` (or extend the
      existing lifecycle-client test file): using a fake `HubConnection`-shaped double
      (no real network), assert `onConnectionStateChanged` fires `connecting` before
      `start()` resolves, `connected` once it resolves, `reconnecting` on the
      connection's `onreconnecting` callback, `connected` again on `onreconnected`, and
      `disconnected` on `onclose` (SC-012's scripted connection-lifecycle fixture per
      plan.md Test Strategy). Extended the existing
      `frontend/src/lib/services/ingestLifecycleClient.test.ts` (plain, non-`.svelte`
      test — no component rendering involved) rather than adding a new file.

### Implementation for this addition

- [X] T055 [US4] Extend `IngestLifecycleClient` in
      `frontend/src/lib/services/ingestLifecycleClient.ts`: add
      `onConnectionStateChanged(handler: (state: ConnectionState) => void): () => void`
      wired to the connection's `onreconnecting`/`onreconnected`/`onclose` callbacks
      (`onReconnected` stays as-is for the existing board-refresh behavior); emit
      `connecting` synchronously before `start()` is called and `connected`/
      `disconnected` based on whether `start()` resolves or rejects. Add the
      `ConnectionState` type (`'connecting' | 'connected' | 'reconnecting' |
      'disconnected'`) to `frontend/src/lib/types.ts`. Thread an
      `onConnectionStateChanged` option through `createBoardLifecycleStream` alongside
      the existing `onRunActivityChanged` option (FR-023, research R14).
- [X] T056 [US4] Create `frontend/src/lib/components/ConnectionStatusIndicator.svelte`:
      a small badge component taking a `state: ConnectionState` prop, labelled
      "Connected"/"Reconnecting…"/"Disconnected", color-coded consistent with
      `StatusBadge.svelte`'s `colorClasses` pattern (FR-023).
- [X] T057 [US4] Wire the indicator into `frontend/src/routes/+page.svelte`: track
      `connectionState` via the `onConnectionStateChanged` option added in T055,
      render `ConnectionStatusIndicator` near the page header (persistent, visible
      regardless of scroll position) (FR-023, SC-012).
- [X] T058 [P] Update `specs/004-ingest-agent-systemprompt/quickstart.md` Scenario 11
      manual steps to record actual outcomes once run end-to-end (mirrors T051's
      treatment of scenarios 8/10 — live-verified where a real Hub start/stop is
      needed; the deterministic state-transition assertions stay in T054).

**Checkpoint**: Board connection-health indicator functional; quickstart Scenario 11
passes; SC-012 covered by a hermetic frontend test (no backend/CI contract rows —
plan.md's 2026-07-14 Constitution re-check notes no new metric/log/span applies here).

---

## Observability contract map (audit reference for T046/T047)

| Plan row | Impl task | Test task | CI task |
|----------|-----------|-----------|---------|
| Metric `wiki.ingest.user_prompt_total` | T021 | T022 | T047 |
| Metric `wiki.ingest.convert_step_disabled_total` | T029 | T030 | T047 |
| Metric `wiki.ingest.run_events_total` | T039 | T043 | T047 |
| Metric `wiki.ingest.liveness_failures_total` | T040 | T043 | T047 |
| Metric `wiki.ingest.queue_depth` | T041 | T043 | T047 |
| Log `ingest.submission.prompt_config` | T021 | T022 | T047 |
| Log `ingest.submission.convert_config` | T029 | T030 | T047 |
| Log `ingest.submission.config_rejected` | T021 (prompt) / T026 (steps) | T022 / T025 | T047 |
| Log `ingest.instructions.loaded` (adapted) | T012 | T013 | T047 |
| Log `ingest.agent.user_prompt_resolved` | T019 | T022 | T047 |
| Log `ingest.run.liveness_failed` | T040 | T043 | T047 |
| Log `ingest.run.late_event` | T039 | T043 | T047 |
| Log `ingest.queue.enqueued` | T041 | T043 | T047 |
| Log `ingest.queue.advanced` | T041 | T043 | T047 |
| Log `ingest.queue.paused_after_restart` | T041 | T035/T043 | T047 |
| Log `ingest.queue.resumed` | T041 | T035/T043 | T047 |
| Span `ingest_agent.load_instructions` (adapted) | T012 (+T019 attr) | T013 | T047 |
| Span `ingest_submission.apply_convert_config` | T029 | T030 | T047 |
| Span `ingest_hub.run_supervision` | T040 | T043 | T047 |
| Span `ingest_hub.handle_run_event` | T039 | T043 | T047 |

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (T001)**: first task, no dependencies — guards ADR-008 before feature code
- **Phase 1 (T002–T003)**: after Phase 0; T002 gates everything on merged 003 code
- **Phase 2 (T004–T006)**: blocks all user stories (CLI surface shared by US1/US2/US4)
- **US1 (T007–T014)**: after Phase 2 — no dependency on other stories
- **US2 (T015–T024)**: after Phase 2 — touches `AgentLoop`/artifact files also touched
  by US1 (T010/T011); run after US1 or coordinate the shared files
- **US3 (T025–T032)**: after Phase 2 — independent of US1/US2 except the shared form
  service (T023); T031 depends on T023's defaults service existing
- **US4 (T033–T045)**: after Phase 2 — independent of US1–US3 content-wise;
  T038 touches `AgentLoop.cs` (also US2 T018): coordinate or sequence
- **Phase 7 (T046–T052)**: after all desired stories; T048/T049 require the full
  agent loop + instruction files
- **Phase 8 (T053–T058)**: independent of Phase 7 and of T033–T045's backend content;
  only touches `frontend/src/lib/services/ingestLifecycleClient.ts`,
  `frontend/src/lib/components/`, `frontend/src/routes/+page.svelte`, and
  `frontend/src/lib/types.ts` — safe to run any time after Phase 2 (does not depend on
  US1–US3 or on T033–T045 landing first, since it only extends the lifecycle client
  already present from feature 003)

### Within Each User Story

- Test tasks first (fail before implementation), then instruction files/models, then
  services/endpoints, then frontend, then observability contract tests

### Parallel Opportunities

- T006 parallel to T004/T005 (different projects)
- Within US1: T007 ∥ T008 (different test files); T012 ∥ T014 after T009–T011
- Within US2: T015 ∥ T016; T024 parallel to backend tasks
- Within US3: T025 first, then T026–T031 sequential (shared pipeline files), T032 ∥
- Within US4: T034 ∥ T035 ∥ T036 after T033; T037/T038 (agent) ∥ T039–T042 (hub);
  T045 ∥ backend tasks
- Within Phase 8: T053 ∥ T054 (different test files); T055 before T056/T057 (T056
  depends on the `ConnectionState` type from T055); T058 ∥ everything else
- Different stories in parallel only with coordination on `AgentLoop.cs`,
  `SubmissionForm.svelte`, `IngestSubmissionEndpoints.cs` (shared files)

---

## Implementation Strategy

**MVP first (US1 only)**: Phase 0 → 1 → 2 → US1 → validate quickstart Scenarios 1–2 →
ship. This alone removes the misleading instruction surface.

**Incremental delivery**: US2 (prompt editing) → US3 (convert toggles) → US4 (event
channel + queue; the largest slice — includes replacing `IngestRunGate`). Each story
closes with its checkpoint validation before the next begins. Phase 7 gates the DoD:
observability audit, CI enforcement, and both eval suites (SC-006 parity, SC-007
steering) must pass before the feature is DONE.

**2026-07-14 addition**: Phase 8 (T053–T058, board connection-health indicator,
FR-023/SC-012) is a small, independent, frontend-only slice — it can be implemented
at any point after Phase 2 and does not block or get blocked by Phase 7's DoD gates,
but the feature is not fully converged against the updated spec/plan until it lands.

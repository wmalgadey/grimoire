# Tasks: Unified Task Board for Lint and Agentic Remediation

**Input**: Design documents from `/specs/015-lint-board-parity/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present), ADR-018 (Accepted)

**Tests**: Included — the constitution mandates deterministic integration tests for every
harness guarantee, evaluation tests for both agent-judgment criteria (SC-006, FR-018),
and the logging/trace contract triads for every `plan.md ## Observability` row.

**Organization**: Grouped by user story (US1–US5 from spec.md) after Phase 0 (structural
enforcement), Setup, and Foundational phases.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US5

## Path Conventions

Web app per plan.md: `backend/src/`, `backend/tests/`, `frontend/src/`.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Guard ADR-018's authorization-gate boundary and ADR-010's namespace
containment before any feature code exists.

**⚠️ NON-NEGOTIABLE**: No feature implementation until Phase 0 is complete.

- [ ] T001 Write structural boundary test for ADR-010 containment of the new namespace in `backend/tests/Grimoire.ArchTests/RemediationTasksContainmentRuleTests.cs`: (a) `Grimoire.Hub.RemediationTasks` MUST NOT reference infrastructure packages other than those allowed for Hub feature namespaces (mirror the existing containment rule tests for `LintDispatch`/`QueryConversations`); (b) no namespace outside `Grimoire.Hub.OperationalState` references `Microsoft.Data.Sqlite` (existing rule stays green). Red/Green probe: add a deliberately violating probe class (e.g. direct `Microsoft.Data.Sqlite` import inside `Grimoire.Hub.RemediationTasks`), verify the test fails, delete the probe, verify it passes. Document the probe result in the commit message.
- [ ] T002 Write structural boundary test for ADR-018's authorization gate in `backend/tests/Grimoire.ArchTests/RemediationExecutionDispatchRuleTests.cs`: within `Grimoire.Hub.RemediationTasks`, only the type `RemediationRunCoordinator` may reference `Grimoire.Hub.AgentDispatch.IAgentProcessLauncher` (allow-listed-caller shape, mirroring the existing guarded-write boundary rule tests). Rule passes vacuously until the namespace exists. Red/Green probe: add a probe class in the remediation namespace that references `IAgentProcessLauncher`, verify failure, delete, verify pass. Document the probe result in the commit message.

**Checkpoint**: Structural boundaries are guarded. Feature code may begin.

---

## Phase 1: Setup

**Purpose**: Path registration and operational-state schema every later phase builds on.

- [ ] T003 Add `RemediationTasksDir` to `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs` and `GrimoirePathResolver.cs` (WritableData kind, default `<base>/remediation-tasks` as sibling of `tasks/`/`conversations/` per ADR-009/014 layout), and add `RemediationTasksDir` + `RemediationTaskRecordPathFor(string taskId)` to `backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs`, mirroring `ConversationsDir`/`ConversationRecordPathFor`. Extend the existing path-resolver tests in `backend/tests/Grimoire.IntegrationTests` accordingly.
- [ ] T004 Add the `remediation_tasks` table (schema per data-model.md: task_id PK, run_id, title, description, target_path NULL, state, proposed_at, authorized_at NULL, outcome_reason NULL, updated_at) plus a remediation queue-paused flag key to `backend/src/Grimoire.Hub/OperationalState/OperationalStateRepository.cs`, with CRUD + CAS methods: `InsertRemediationTaskAsync`, `GetRemediationTasksAsync` (all + by state), `TryTransitionRemediationTaskAsync(taskId, fromState, toState, outcomeReason, authorizedAt)` returning bool (compare-and-swap on `state`, first commit wins). Deterministic integration tests for CRUD and the CAS semantics in `backend/tests/Grimoire.IntegrationTests/RemediationTaskRepositoryTests.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, event vocabulary, and record store that US2–US5 depend on.

- [ ] T005 [P] Implement the `RemediationActionTask` entity + state machine in `backend/src/Grimoire.Hub/RemediationTasks/RemediationActionTask.cs`: states `Proposed|Authorized|Executing|Completed|Failed|NotApplicable|Dismissed`, valid edges exactly per data-model.md's transition table, first-terminal-transition-wins idempotence, `outcome_reason` mandatory for Failed/NotApplicable. Unit tests for every valid and invalid edge in `backend/tests/Grimoire.Domain.UnitTests/RemediationActionTaskStateMachineTests.cs` (complex domain invariants — justified per Principle II).
- [ ] T006 [P] Extend `backend/src/Grimoire.Hub/AgentDispatch/AgentRunEvent.cs` with optional `proposedActions` (list of `{title, description, targetPath?}`, new record `AgentRunEventProposedAction`) and optional `remediationOutcome` (`applied|not_applicable`) + reuse of `reason`, per contracts/remediation-lifecycle-events.md. Tolerant-parser tests (old fixtures still parse; new fields round-trip; malformed entries ignored) in `backend/tests/Grimoire.IntegrationTests/AgentRunEventParserTests.cs` (extend existing).
- [ ] T007 [P] Implement `RemediationTaskRecordStore` in `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskRecordStore.cs`: creates the record at materialization (frontmatter `task_id`, `run_id`, `proposed_at`, `record_format: grimoire-remediation-task/1` + proposal entry), append-only entries for context/message/outcome with `<!-- grimoire:... -->` bookkeeping comments and `*_chars` lengths per data-model.md, mirroring `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordStore.cs`. Deterministic integration tests (create, append each kind, injection-proof parsing, append-only invariant) in `backend/tests/Grimoire.IntegrationTests/RemediationTaskRecordStoreTests.cs`.
- [ ] T008 Register the new namespace's DI wiring (store, later coordinator/publisher) in `backend/src/Grimoire.Hub/Program.cs` following the existing Lint/Query composition pattern (extend as later phases add types).

**Checkpoint**: Foundation ready — user stories can begin.

---

## Phase 3: User Story 1 — See lint activity on the shared task board (P1) 🎯 MVP

**Goal**: Lint run status (none/running/completed/failed + reason) visible live on the
board, distinguishable from ingest entries.

**Independent Test**: Trigger a lint run by any existing means (`POST /api/lint-runs`)
and watch the board reflect running → terminal state without reload (quickstart.md
Scenario 1).

### Tests for User Story 1 (write first, must fail)

- [ ] T009 [P] [US1] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/LintLifecycleHubTests.cs`: with a fake `IAgentProcessLauncher`, triggering a lint run broadcasts `lintRunLifecycleChanged` (running, then terminal incl. `failureReason` on failure) on `/hubs/lint-lifecycle`, payload per contracts/remediation-lifecycle-events.md (SC-001/SC-002 lint half).
- [ ] T010 [P] [US1] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/BoardCompositeResponseTests.cs`: the composite board response (contracts/lint-board-api.md) carries the lint run as an `entryKind: "lint_run"` typed entry with state/updatedAt/failureReason/detailLink, alongside unchanged ingest entries; covers "no run ever" (no lint card, trigger offered) and the pre-existing-active-run recovery edge case.

### Implementation for User Story 1

- [ ] T011 [US1] Implement `LintLifecycleHub` (route `/hubs/lint-lifecycle`, broadcast-only) and `LintLifecyclePublisher` in `backend/src/Grimoire.Hub/Realtime/`, mirroring `IngestLifecycleHub`/`IngestLifecyclePublisher`; publish from `LintRunCoordinator.TriggerAsync` (trigger → running) and `FinishRunAsync` (terminal), map into `backend/src/Grimoire.Hub/Program.cs`.
- [ ] T012 [US1] Implement the composite board initial-state response per contracts/lint-board-api.md in `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs` (or a sibling board endpoint file per the contract): fold the latest lint run (from `LintRunCoordinator`) in as a typed `lint_run` entry; ingest entry shape byte-for-byte unchanged (FR-015).
- [ ] T013 [P] [US1] Frontend: add `lintLifecycleClient.ts` in `frontend/src/lib/services/` (mirrors `ingestLifecycleClient.ts`, consumes `/hubs/lint-lifecycle`), extend `frontend/src/lib/types.ts` with the typed board-entry shapes (`entryKind` discriminator per data-model.md).
- [ ] T014 [US1] Frontend: render the lint run as a visually distinct card on the board in `frontend/src/routes/+page.svelte` + new `frontend/src/lib/components/LintRunCard.svelte` (running/completed/failed + failureReason, FR-001/FR-005/FR-006), fed by the composite response + live lint stream; ingest rendering untouched.
- [ ] T015 [US1] Frontend component test for the lint card states (no-run/running/completed/failed) in the existing frontend test setup, `frontend/src/lib/components/LintRunCard.svelte.test.ts` (SC-003 groundwork, FR-006).

**Checkpoint**: US1 independently functional — lint visible live on the board.

---

## Phase 4: User Story 2 — Trigger a lint run from the task board (P2)

**Goal**: Start a lint run from the board; blocked triggers explain why.

**Independent Test**: From the board, click trigger → run appears in progress; trigger
again while active → clear "already active" explanation (quickstart.md Scenario 2).

### Tests for User Story 2 (write first, must fail)

- [ ] T016 [P] [US2] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/LintTriggerPreconditionTests.cs`: `POST /api/lint-runs` returns 409 with `reason: "lint_run_active"` while a run is active, and 409 with `reason: "unresolved_remediation_tasks"` + `unresolvedTaskIds` while any remediation row is `proposed|authorized|executing`; accepted (202) when neither holds, including the run-just-finished race (spec edge case: trigger at the moment a run finishes resolves to either a clean accept or a clean reject, never silence) (FR-004/SC-004).

### Implementation for User Story 2

- [ ] T017 [US2] Extend `LintRunCoordinator.TriggerAsync` in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` with the FR-004 precondition: reject (new `LintSubmissionResult.Blocked` with reason + unresolved task ids, queried from `OperationalStateRepository`) when any remediation task is unresolved; keep the existing Busy path for an active run. Update `backend/src/Grimoire.Hub/LintDispatch/LintSubmissionEndpoints.cs` to map both reasons per contracts/lint-board-api.md.
- [ ] T018 [US2] Frontend: add the trigger control to the board in `frontend/src/routes/+page.svelte` (reusing `frontend/src/lib/services/lintApi.ts` `POST /api/lint-runs`), single action from the board (SC-003); surface both 409 reasons as clear user-facing messages (never silent, SC-004).
- [ ] T019 [P] [US2] Frontend test for trigger + both blocked reasons in `frontend/src/routes/board-lint-trigger.test.ts` (or the project's established frontend test location).

**Checkpoint**: US1+US2 — board is the single place to see and start lint work.

---

## Phase 5: User Story 3 — Agent-proposed remediation tasks on the board (P3)

**Goal**: A completing lint run materializes one Proposed task card per agent-proposed
action, before the run shows completed.

**Independent Test**: Complete a (fake-agent) lint run whose terminal event carries
`proposedActions` → cards exist when the run shows completed; empty list → zero cards
(quickstart.md Scenario 3).

### Tests for User Story 3 (write first, must fail)

- [ ] T020 [P] [US3] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/RemediationProposalMaterializationTests.cs`: fake lint agent completes with N `proposedActions` → N `remediation_tasks` rows in `Proposed` + N task records exist **before** the lint run's terminal broadcast is published (FR-007 ordering); verbatim title/description (no harness rewriting, Principle V); empty/absent list → zero rows, run still completes; `remediationTaskLifecycleChanged` broadcast per new card on `/hubs/remediation-lifecycle`.
- [ ] T021 [P] [US3] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/BoardCompositeResponseTests.cs` (extend): `remediation_task` typed entries appear with proposal title, originating-run subtitle, state, detailLink; independently listable per contracts/remediation-task-api.md (list + get endpoints).

### Implementation for User Story 3

- [ ] T022 [US3] Materialize proposals in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` `FinishRunAsync`: parse `proposedActions` off the terminal event, generate task ids (`{date}-remediation-{guid}` per data-model.md), insert `Proposed` rows via `OperationalStateRepository`, create task records via `RemediationTaskRecordStore` — all before `TryTransitionTo(Completed)`/lifecycle publish. Emit span `hub.lint.propose_remediation_tasks` (parent `hub.lint.run_supervision`, attrs `run_id`, `proposed_count`), log event `hub.lint.remediation_task_proposed` (INFO, `run_id`, `task_id`) per proposal, metric `wiki.lint.remediation_tasks_proposed_total`.
- [ ] T023 [US3] Implement `RemediationLifecycleHub` (route `/hubs/remediation-lifecycle`) + `RemediationLifecyclePublisher` + `RemediationLifecycleLogEvents` in `backend/src/Grimoire.Hub/RemediationTasks/`, events per contracts/remediation-lifecycle-events.md; metric `hub.remediation_lifecycle_updates_total{stage}` in `backend/src/Grimoire.Hub/HubMetrics.cs`; wire into `Program.cs`.
- [ ] T024 [US3] Implement list/detail endpoints of contracts/remediation-task-api.md (`GET /api/remediation-tasks`, `GET /api/remediation-tasks/{taskId}` incl. record-derived history) in `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskEndpoints.cs`; fold `remediation_task` entries into the composite board response (extend T012's endpoint).
- [ ] T025 [US3] Extend `agents/lint/system-prompt.md`: findings assessment now also proposes remediation actions — instruct the agent to judge which findings are actionable (informational findings produce none) and emit one `proposedActions` entry per action on its completion event; extend `backend/src/Grimoire.LintAgent` to carry the agent's proposals onto the terminal NDJSON event (loop mechanics only — content stays agent-authored, Principle V).
- [ ] T026 [P] [US3] Frontend: `remediationApi.ts` + `remediationLifecycleClient.ts` in `frontend/src/lib/services/`, `RemediationTaskCard.svelte` in `frontend/src/lib/components/` (Proposed card: proposal text, review actions placeholder, visually distinct per FR-006), board integration in `frontend/src/routes/+page.svelte`; independent review of each card (US3 scenario 3).
- [ ] T027 [P] [US3] Deterministic observability tests in `backend/tests/Grimoire.IntegrationTests/RemediationObservabilityTests.cs`: event name/level/fields for `hub.lint.remediation_task_proposed`; span `hub.lint.propose_remediation_tasks` with parent linkage + `run_id`/`proposed_count`; metric `wiki.lint.remediation_tasks_proposed_total` (in-memory exporter, ADR-005 pattern).
- [ ] T028 [US3] Agent-behavior evaluation (SC-006) in `backend/tests/Grimoire.AgentEvals/RemediationProposalRelevanceEvalTests.cs`: sampled lint runs over seeded wiki fixtures via the ADR-012 recorded-replay harness; ≥ 90% of proposed tasks judged relevant/actionable against the human-adjudicated golden set; record fixtures per the existing eval-capture workflow.

**Checkpoint**: Lint runs produce reviewable task cards; runs complete only after cards exist.

---

## Phase 6: User Story 4 — Authorize a proposed remediation action (P4)

**Goal**: Authorize → sequential agent execution with re-verification; dismiss;
withdraw; no wiki change without authorization, ever.

**Independent Test**: Authorize a Proposed card → agent executes (fake agent in tests,
real in quickstart Scenario 4) → card shows outcome; dismiss resolves without agent;
withdraw returns to Proposed until execution starts.

### Tests for User Story 4 (write first, must fail)

- [ ] T029 [P] [US4] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/RemediationAuthorizationTests.cs`: authorize (202, `Proposed→Authorized`, `authorized_at` stamped), dismiss (`Proposed→Dismissed`, no launcher call ever), withdraw (`Authorized→Proposed`, `authorized_at` cleared), invalid-state attempts → 409 with actual state + reason, per contracts/remediation-task-api.md.
- [ ] T030 [P] [US4] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/RemediationRunCoordinatorTests.cs`: FIFO order by `authorized_at` (FR-017); exactly one `Executing` at a time; queue positions exposed; queue advances on each terminal event; `Executing→Failed` on liveness expiry and spawn failure with reason; `notApplicable` terminal event → `NotApplicable` + reason (FR-018 transport); fake launcher never invoked for any non-Authorized row (SC-005); withdrawal race — whichever CAS commits first wins, loser gets 409 `execution_already_started` (or the withdrawal wins and dispatch skips), deterministically (spec edge case).
- [ ] T031 [P] [US4] Hermetic restart-reconciliation test in `backend/tests/Grimoire.IntegrationTests/RemediationRestartReconcilerTests.cs`: `Executing` row with no live process → `Failed` + reason on startup; `Authorized` rows survive but the remediation queue starts paused (own flag, independent of the ingest flag) until resumed (ADR-003/ADR-018).

### Implementation for User Story 4

- [ ] T032 [US4] Implement `RemediationRunCoordinator` in `backend/src/Grimoire.Hub/RemediationTasks/RemediationRunCoordinator.cs` mirroring `IngestRunCoordinator`'s persisted-FIFO + `SemaphoreSlim(1,1)` shape: `TryStartNextAsync` dequeues oldest `Authorized`, CAS to `Executing` under the slot lock before spawn (the only `IAgentProcessLauncher` call site, T002's rule); ADR-008 supervision (NDJSON read loop, liveness watchdog as sole failure authority); terminal handling incl. `remediationOutcome: not_applicable`; queue advance on every terminal transition; own queue-paused flag + `ResumeAsync`; spans `hub.remediation.execution_dispatch` (root, `task_id`) and `hub.remediation.run_supervision` (child, `task_id`); logs `hub.remediation.execution_started`/`hub.remediation.execution_completed` (INFO, fields per plan.md); metrics `wiki.remediation.tasks_executed_total{outcome}`, `wiki.remediation.queue_depth`.
- [ ] T033 [US4] Implement authorize/dismiss/withdraw endpoints in `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskEndpoints.cs` per contracts/remediation-task-api.md (CAS-backed, 409 + actual state on conflict; withdraw's `execution_already_started` case); span `hub.remediation.authorize` (`task_id`); logs `hub.remediation.task_authorized`/`task_dismissed`/`authorization_withdrawn`; metrics `wiki.remediation.tasks_authorized_total`/`tasks_dismissed_total`/`tasks_withdrawn_total`; lifecycle broadcasts on every transition; outcome entry appended to the task record at terminal transitions.
- [ ] T034 [US4] Extend `backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs` for remediation rows per T031's semantics; initialize the remediation queue-paused flag in coordinator startup (mirror `IngestRunCoordinator.InitializeAsync`).
- [ ] T035 [US4] Remediation-execution invocation mode: new request record in `backend/src/Grimoire.Hub/RemediationTasks/` (task id, proposal text, attached context as ADR-007 user-prompt override, lint policy/instruction paths), plus `backend/src/Grimoire.LintAgent` execution mode — agent re-verifies the proposal against current wiki content first (FR-018, judgment in instructions), reports `remediationOutcome: not_applicable` + reason without writing when moot, otherwise applies the fix through the unchanged guarded tool boundary (ADR-006/015/016); span `hub.remediation.re_verify` (parent `hub.remediation.run_supervision`, attrs `task_id`, `still_applicable`) emitted Hub-side from the terminal event metadata.
- [ ] T036 [US4] Extend `agents/lint/system-prompt.md` (or a mode-scoped instruction section per ADR-007) with the remediation-execution instructions: re-verification judgment, apply-vs-not-applicable decision, frontmatter-only scope awareness (guard denial handling per research.md R7).
- [ ] T037 [P] [US4] Frontend: authorize/dismiss/withdraw actions on `RemediationTaskCard.svelte`; waiting-vs-executing distinction with queue position (FR-017); outcome states incl. `NotApplicable` + reason and `Failed` + reason (FR-005/FR-018/SC-007); live via `remediationLifecycleClient.ts`; component tests for all states in `frontend/src/lib/components/RemediationTaskCard.svelte.test.ts`.
- [ ] T038 [P] [US4] Deterministic observability tests (extend `backend/tests/Grimoire.IntegrationTests/RemediationObservabilityTests.cs`): log events `task_authorized`/`task_dismissed`/`authorization_withdrawn`/`execution_started`/`execution_completed` (name/level/fields); spans `hub.remediation.authorize`/`execution_dispatch`/`run_supervision`/`re_verify` (names, parentage, `task_id` correlation); metrics `tasks_authorized_total`/`tasks_dismissed_total`/`tasks_withdrawn_total`/`tasks_executed_total{outcome}`/`queue_depth`.
- [ ] T039 [US4] Agent-behavior evaluation (FR-018) in `backend/tests/Grimoire.AgentEvals/RemediationReVerificationEvalTests.cs`: fixtures pairing a recorded proposal with changed vs. unchanged wiki states; agent chooses apply vs. not-applicable correctly in ≥ 90% of sampled runs (ADR-012 replay).

**Checkpoint**: Full propose → authorize → execute loop, structurally authorization-gated.

---

## Phase 7: User Story 5 — Context and messaging on a proposed action (P5)

**Goal**: Attach context to a Proposed task; message the agent about it; history
persists past terminal outcomes.

**Independent Test**: Attach info to a Proposed card, send a message, see the agent's
reply in the task's thread; history still readable after the task resolves
(quickstart.md Scenario 5).

### Tests for User Story 5 (write first, must fail)

- [ ] T040 [P] [US5] Hermetic integration test in `backend/tests/Grimoire.IntegrationTests/RemediationTaskMessagingTests.cs`: attach context (Proposed only; 409 + reason otherwise) appends a context entry to the record and reaches the execution request's user-prompt override (FR-011); message turn (202) spawns a fake message-turn run whose reply is appended (`sender: agent`) and broadcast (`remediationMessageTurnChanged`); history endpoint returns full thread including after a terminal outcome (FR-014); prior messages are included in the next message-turn's context (record-as-context, R6).

### Implementation for User Story 5

- [ ] T041 [US5] Implement attach-context + message + history endpoints in `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskEndpoints.cs` per contracts/remediation-task-api.md; append via `RemediationTaskRecordStore`; log `hub.remediation.message_recorded` (INFO, `task_id`, `sender`); metric `hub.remediation.message_turns_total{outcome}`; span `hub.remediation.message_turn` (root, `task_id`).
- [ ] T042 [US5] Message-turn invocation mode in `backend/src/Grimoire.LintAgent` (bounded single exchange per ADR-011's Query-turn shape, read-only policy for the turn): context = proposal + finding + attached info + prior messages from the record; response returned on the terminal event; Hub appends both sides. Message-turn instruction section per ADR-007.
- [ ] T043 [P] [US5] Frontend: `TaskMessageThread.svelte` in `frontend/src/lib/components/` (thread render via `marked`+`DOMPurify` like `QueryConversation.svelte`, send box, attach-context affordance while Proposed) wired into the task detail view `frontend/src/routes/tasks/[taskId]/+page.svelte` for remediation tasks; component test in `frontend/src/lib/components/TaskMessageThread.svelte.test.ts`.
- [ ] T044 [P] [US5] Deterministic observability tests (extend `RemediationObservabilityTests.cs`): `hub.remediation.message_recorded` (name/level/fields), span `hub.remediation.message_turn` (name, `task_id`), metric `hub.remediation.message_turns_total{outcome}`.

**Checkpoint**: All five user stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T045 Observability completeness audit (MANDATORY — Constitution III/IV): cross-reference every row of plan.md ## Observability (8 metrics, 7 log events, 6 spans) against its implementing task (T022/T023/T032/T033/T041) and passing test (T027/T038/T044) — file any gap found as a new task before declaring the DoD met.
- [ ] T046 Logging + trace contract CI enforcement (MANDATORY — Constitution IV): verify `.github/workflows/ci.yml`'s standard PR pipeline runs `Grimoire.IntegrationTests` (incl. `RemediationObservabilityTests`, `LintLifecycleHubTests`) and `Grimoire.ArchTests` (T001/T002); adjust filters if any new test class is excluded.
- [ ] T047 Agent-behavior evaluation completeness audit (MANDATORY — Constitution II & V): confirm SC-006 (T028) and FR-018 (T039) evals pass at ≥ 90% via the ADR-012 harness (`.github/workflows/eval.yml` or the documented replay path); file any gap as a new task.
- [ ] T048 Ingest regression check (SC-008/FR-015): full existing `Grimoire.IntegrationTests` ingest suite + existing frontend board tests pass unmodified; diff-review that no ingest source file changed beyond the additive board-response composition (T012/T024).
- [ ] T049 [P] Run quickstart.md validation scenarios end-to-end and record outcomes in the PR description.
- [ ] T050 [P] Documentation: update `docs/adr/ADR-018-...md` cross-references if implementation deviated; note the new `remediation-tasks/` runtime directory wherever runtime layout is documented (e.g. README/ops notes if present).

---

## Dependencies & Execution Order

- **Phase 0 → Phase 1 → Phase 2** strictly sequential gates.
- **US1 (Phase 3)**: needs only Phase 0–1 (T003) plus hubs; independent of T004–T007.
- **US2 (Phase 4)**: needs T004 (unresolved-task query) + US1's board trigger surface.
- **US3 (Phase 5)**: needs T004–T008 + US1's board composition (T012).
- **US4 (Phase 6)**: needs US3 (rows to authorize) + T005 state machine + T002 rule.
- **US5 (Phase 7)**: needs US3 (Proposed cards) + T007 record store; independent of US4 except shared endpoints file (T033/T041 sequential).
- **Phase 8**: after all implemented stories.

### Parallel Opportunities

- T001 ∥ T002 (different test files); T005 ∥ T006 ∥ T007 (different files).
- Within each story: the test-first tasks marked [P] in parallel; frontend tasks ∥ backend observability-test tasks.
- After Phase 2: US1 and the backend halves of US3 can proceed in parallel; US4 and US5 can proceed in parallel after US3 (mind the shared `RemediationTaskEndpoints.cs`).

---

## Implementation Strategy

**MVP = Phase 0–3 (US1)**: lint visibility on the board — the core gap from issue #40.
Then US2 (trigger parity) → US3 (proposals) → US4 (authorize/execute) → US5
(context/messaging), validating each checkpoint independently before proceeding.

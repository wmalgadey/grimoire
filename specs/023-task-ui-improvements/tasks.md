# Tasks: Task Visibility & Recovery Improvements

**Input**: Design documents from `/specs/023-task-ui-improvements/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/http-api.md, contracts/signalr-events.md, quickstart.md — all present. ADR-025 is Accepted (gate cleared).

**Tests**: Required. All success criteria are deterministic harness guarantees (plan.md § Test Strategy); backend tests live in `backend/tests/Grimoire.IntegrationTests` (Integration tier, ADR-021), frontend tests are colocated vitest browser-mode files. Tests are written first and must fail before their implementation task.

**Traceability**: every task cites the literal `FR-###`/`SC-###` it implements or verifies; setup/cross-cutting tasks cite their phase goal explicitly.

## Phase 0: Structural Boundary Enforcement (Constitution Principle III)

**No Boundary Rule introduced by this feature** (see plan.md § Architectural Constraints & ADRs). ADR-025's Decision Outcome classifies all of its rules as **Feature-Scoped Invariants**, covered by the classicist, state-based integration tests scheduled inside the user-story phases below — never by reflection/IL-based structural tests. Existing ADR-010 containment tests in `backend/tests/Grimoire.ArchTests` remain in force unchanged; no new structural test is fabricated.

**Checkpoint**: Phase 0 satisfied by explicit declaration. Feature code may begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Test doubles and packages the story phases need (phase goal; no single FR).

- [X] T001 [P] Add `Microsoft.Extensions.Time.Testing` (FakeTimeProvider — concrete fake, not a mocking framework) to backend/tests/Grimoire.IntegrationTests/Grimoire.IntegrationTests.csproj (serves US2 backoff tests; phase goal, no single FR)
- [X] T002 [P] Extend the existing hand-rolled port fake with a configurable go-silent mode (emit initial events, then silence) in backend/tests/Grimoire.IntegrationTests/Fakes/FakeAgentProcessLauncher.cs (serves US2 liveness tests verifying FR-007/SC-005)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Durable status history storage and time abstraction — every story phase reads or writes these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Create append-only `ingest_status_history` table (`task_id`, `seq` PK pair, `status`, `entered_at`, `detail` per data-model.md §1) plus `attempt` column on `operational_task_state` (data-model.md §2), with append + ordered-read methods, in backend/src/Grimoire.Hub/OperationalState/OperationalStateRepository.cs (FR-005)
- [X] T004 Record every published board-stage transition as a history row at the lifecycle choke point in backend/src/Grimoire.Hub/Realtime/IngestLifecyclePublisher.cs (single writer; agents never touch the table) (FR-005)
- [X] T005 [P] Inject `TimeProvider` into `IngestRunCoordinator` (production: `TimeProvider.System`) via backend/src/Grimoire.Hub/HubHostComposition.cs, using it for history timestamps and (in US2) backoff scheduling (prerequisite for FR-008; phase goal)
- [X] T006 Deterministic integration test: a full lifecycle (received→converting→queued→running→completed, and the failed path) appends ordered, gapless-per-task history rows that survive `FinishRunAsync` and Hub restart, in backend/tests/Grimoire.IntegrationTests/IngestStatusHistoryTests.cs (FR-005)

**Checkpoint**: History is durably recorded for every transition — story phases can now build on it.

---

## Phase 3: User Story 1 — See status history to diagnose a failure (Priority: P1) 🎯 MVP

**Goal**: The task detail shows the full ordered status "path" so the stopping point of any failure is identifiable.

**Independent Test**: Open any task's detail view and see the ordered status sequence; for a failed task the failing entry is the last one (quickstart.md Scenario 2).

### Tests for User Story 1

- [X] T007 [P] [US1] Integration test (fail first): `GET /api/ingest-submissions/{taskId}` returns `statusHistory` ordered by `seq` with `status`/`enteredAt`/`detail` per contracts/http-api.md, and returns an empty array for a pre-feature task without history rows, in backend/tests/Grimoire.IntegrationTests/IngestTaskDetailHistoryTests.cs (FR-006, SC-004)

### Implementation for User Story 1

- [X] T008 [US1] Extend `GetTaskDetailAsync` with the `statusHistory` field read from `OperationalStateRepository` in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs (FR-006, SC-004)
- [X] T009 [P] [US1] Add `HistoryStatus`, `StatusHistoryEntry`, and `TaskDetail.statusHistory` types per data-model.md §5 in frontend/src/lib/types.ts (FR-006)
- [X] T010 [US1] Create `StatusHistoryPath.svelte` (ordered path with timestamps and detail, current/last entry highlighted, single-entry fallback from current status when history is empty) plus colocated frontend/src/lib/components/StatusHistoryPath.svelte.test.ts (FR-006, SC-004)
- [X] T011 [US1] Render the history path in the detail page and re-fetch history on `taskLifecycleChanged` for the shown task (contracts/signalr-events.md), in frontend/src/routes/tasks/[taskId]/+page.svelte with page test coverage in frontend/src/routes/tasks/[taskId]/page.test.ts (FR-006, SC-004)

**Checkpoint**: US1 fully functional — history visible for in-progress, completed, and failed tasks.

---

## Phase 4: User Story 2 — Recover from an unresponsive agent run (Priority: P1)

**Goal**: Liveness silence triggers bounded automatic reactivation with increasing backoff instead of immediate permanent failure; every step is a history entry.

**Independent Test**: Run a task against a silent agent; observe interruption → reactivation entries and, after exhaustion, final failure (quickstart.md Scenario 3).

### Tests for User Story 2

- [X] T012 [P] [US2] Integration test (fail first) with go-silent `FakeAgentProcessLauncher` + `FakeTimeProvider`: liveness expiry records `liveness_interrupted` (attempt in `detail`) without final failure; advancing virtual time through 10s/30s/90s triggers re-launches recording `reactivated`+`running`; run slot stays held (queue neither advances nor reorders); 4th silence → final `failed`, Hub failure artifact, `ingest.run.liveness_failed`, queue advances; in backend/tests/Grimoire.IntegrationTests/IngestRunReactivationTests.cs (FR-007, FR-008, SC-005)

### Implementation for User Story 2

- [X] T013 [US2] Implement reactivation in backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs: on liveness expiry terminate process, append `liveness_interrupted`, and while attempts < 3 schedule re-launch via injected `TimeProvider` after 10s/30s/90s (constructor-default constants beside `livenessWindow`), append `reactivated` on re-launch, increment `attempt`, reset counter on normal start; exhaustion runs the existing final-failure path unchanged (FR-007, FR-008, SC-005)
- [X] T014 [US2] Publish `liveness_interrupted`/`reactivated` over the existing `taskLifecycleChanged` event and make board consumers ignore non-`LifecycleStage` statuses for column placement (task stays presented in `running`) in frontend/src/lib/services/ingestLifecycleClient.ts and frontend/src/routes/+page.svelte, with a frontend test asserting no column change on those events (FR-007; contracts/signalr-events.md; supports SC-006's column-only status model)
- [X] T015 [US2] Implement log events `ingest.run.liveness_interrupted` (WARN: `task_id`, `attempt`, `next_delay_seconds`), `ingest.run.reactivated` (INFO: `task_id`, `attempt`), `ingest.run.reactivation_exhausted` (ERROR: `task_id`, `attempts`) in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionLogEvents.cs, emitted from the coordinator (plan.md ## Observability; FR-007)
- [X] T016 [P] [US2] Deterministic integration tests validating name, level, and every mandatory field of the three T015 events in backend/tests/Grimoire.IntegrationTests/IngestReactivationObservabilityTests.cs (plan.md ## Observability logging contract; SC-005)
- [X] T017 [US2] Implement metric `wiki.ingest.reactivations_total` (Counter, label `outcome` ∈ {attempted, exhausted}) in backend/src/Grimoire.Hub/HubMetrics.cs and span `ingest_hub.reactivation` (root, attrs `task_id`, `attempt`, `delay_seconds`) in backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs (plan.md ## Observability)
- [X] T018 [P] [US2] Deterministic integration tests for `wiki.ingest.reactivations_total` (declared label set only) and `ingest_hub.reactivation` (span name, root parentage, attributes) via production wiring (`AddHubTelemetry` + in-memory exporters) in backend/tests/Grimoire.IntegrationTests/IngestReactivationObservabilityTests.cs (plan.md ## Observability trace/metric contract)

**Checkpoint**: US1 + US2 work independently — transient stalls recover; exhaustion is visible in the path.

---

## Phase 5: User Story 3 — Identify a task at a glance (Priority: P2)

**Goal**: Board cards and detail show a human-readable label (content title → filename → URL → taskId), with the UID still discoverable.

**Independent Test**: Submit a file with a markdown H1 and see that heading on the board card and detail heading (quickstart.md Scenario 1 steps 1–3).

### Tests for User Story 3

- [ ] T019 [P] [US3] Integration test (fail first): file submission whose normalized markdown starts with `# Getting Started` yields `title == "Getting Started"` on board row and detail; no-H1 file falls back to the uploaded filename; URL submission falls back to the URL when no H1; task with pre-feature manifest (no `Title`/`OriginalFileName`) falls back to `taskId`; titles capped at 120 chars; in backend/tests/Grimoire.IntegrationTests/IngestTaskTitleTests.cs (FR-003, SC-003)

### Implementation for User Story 3

- [ ] T020 [US3] Persist `OriginalFileName` (from the uploaded `file.FileName`, today discarded) and extract the first ATX `# ` heading of the normalized markdown (trimmed, ≤ 120 chars) into the manifest fields per data-model.md §3, in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionPipeline.cs and the manifest type in backend/src/Grimoire.Hub/Conversion/SourceArtifactStore.cs (FR-003)
- [ ] T021 [US3] Apply the read-time fallback chain (`Title` → `OriginalFileName` → http/https `sourceRef` → `taskId`) for the board `title` in backend/src/Grimoire.Hub/IngestSubmission/KanbanBoardProjectionStore.cs and add `title` to the detail response in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs (FR-003, SC-003)
- [ ] T022 [US3] Frontend: render the title as the card's primary label with the task id as muted secondary text in frontend/src/lib/components/TaskCard.svelte, and as the detail heading with the id shown beneath (copyable) in frontend/src/routes/tasks/[taskId]/+page.svelte; extend colocated component/page tests (FR-003, FR-004, SC-003)

**Checkpoint**: Tasks are recognizable without opening them; UID remains available.

---

## Phase 6: User Story 4 — View the original source in the browser (Priority: P2)

**Goal**: Task detail links to the original source — direct for URLs, Hub-served for local files — with a clear "unavailable" state.

**Independent Test**: Click the source link on a URL task and a file task; delete a stored original and see "unavailable" (quickstart.md Scenario 1 steps 3–5).

### Tests for User Story 4

- [ ] T023 [P] [US4] Integration test (fail first): detail response `source` object per contracts/http-api.md (`kind:"url"` → submitted URL href; `kind:"file"` → source-endpoint href; missing original/manifest → `available:false`, `href:null`); `GET /api/ingest-submissions/{taskId}/source/original` streams the stored original with the manifest's `OriginalContentType` and `Content-Disposition: inline`, 404 for unknown task or missing file; endpoint accepts no path input beyond the route `taskId`; in backend/tests/Grimoire.IntegrationTests/IngestSourceContentApiTests.cs (FR-001, FR-002, SC-001, SC-002)

### Implementation for User Story 4

- [ ] T024 [US4] Add the `source` object to the detail response and implement `GET /{taskId}/source/original` (path composed exclusively from validated `taskId` via `ResolvedGrimoirePaths`/`RawStoragePaths`) in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs (FR-001, FR-002, SC-001, SC-002)
- [ ] T025 [US4] Implement log event `ingest.source.served` (INFO: `task_id`, `content_type`) in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionLogEvents.cs, metric `hub.source_content_reads_total` (Counter, label `result` ∈ {served, not_found}) in backend/src/Grimoire.Hub/HubMetrics.cs, and span `hub.ingest_source.serve` (parent: ASP.NET Core request span; attrs `task_id`, `result`) in the endpoint (plan.md ## Observability)
- [ ] T026 [P] [US4] Deterministic integration tests for the T025 signals — event name/level/fields, metric label set, span name + request-span parentage — via production wiring in backend/tests/Grimoire.IntegrationTests/IngestSourceContentObservabilityTests.cs (plan.md ## Observability logging + trace contract)
- [ ] T027 [US4] Frontend: render the source as an anchor (`target="_blank"`) when `available`, and a non-link "source unavailable" indicator otherwise, in frontend/src/lib/components/TaskRecordView.svelte and the detail page; add `TaskSourceLink` type in frontend/src/lib/types.ts; extend colocated component tests (FR-001, FR-002, SC-001, SC-002)

**Checkpoint**: Source is one click away; broken links are impossible by construction.

---

## Phase 7: User Story 5 — Restart a failed task (Priority: P2)

**Goal**: A finally-failed task restarts from the UI under the same id, race-safe, with history preserved.

**Independent Test**: Restart the failed task from Scenario 3 and watch it re-run; concurrent duplicate restarts produce exactly one winner (quickstart.md Scenario 4).

### Tests for User Story 5

- [ ] T028 [P] [US5] Integration test (fail first): restart of a `failed` task → 202 `{taskId, status:"queued"}`, history appends `restarted`+`queued` with prior failure entries retained, attempt counter reset, task re-runs through the normal lifecycle under the same id; non-failed task → 409 with reason; unknown id → 404; missing normalized source → 409; N concurrent restarts → exactly one 202, one `restarted` row, one queue insertion; in backend/tests/Grimoire.IntegrationTests/IngestTaskRestartTests.cs (FR-010, FR-011, FR-012, FR-013, SC-007, SC-008)

### Implementation for User Story 5

- [ ] T029 [US5] Implement `RestartFailedAsync` in backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs: verify current status `failed` and normalized source exists, arbitrate concurrency by CAS on persisted state under the coordinator lock (ADR-018 idiom), append `restarted`+`queued` history, reset `attempt`, write `queued` stage via the Hub artifact writer, enqueue at tail, publish lifecycle event, `TryStartNextAsync` (FR-010, FR-012, FR-013, SC-008)
- [ ] T030 [US5] Expose `POST /{taskId}/restart` (202/409/404 per contracts/http-api.md; `ingest-retrigger` untouched) in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs (FR-010, SC-007)
- [ ] T031 [US5] Implement log events `ingest.task.restarted` (INFO: `task_id`) and `ingest.task.restart_rejected` (WARN: `task_id`, `current_status`) in backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionLogEvents.cs, metric `wiki.ingest.restarts_total` (Counter, label `outcome` ∈ {accepted, rejected}) in backend/src/Grimoire.Hub/HubMetrics.cs, and span `hub.ingest_task.restart` (parent: ASP.NET Core request span; attrs `task_id`, `outcome`) in the endpoint (plan.md ## Observability)
- [ ] T032 [P] [US5] Deterministic integration tests for the T031 signals — names/levels/fields, label set, span name + request-span parentage — via production wiring in backend/tests/Grimoire.IntegrationTests/IngestRestartObservabilityTests.cs (plan.md ## Observability logging + trace contract)
- [ ] T033 [US5] Frontend: `restartTask` call in frontend/src/lib/services/ingestSubmissionsApi.ts; Restart button on the detail page shown only for `failed` status, disabled while in flight, 409 → re-fetch true state; in frontend/src/routes/tasks/[taskId]/+page.svelte with page tests (FR-010, FR-011, FR-012, SC-007)

**Checkpoint**: Failed tasks are recoverable end-to-end from the UI.

---

## Phase 8: User Story 6 — Decluttered board cards (Priority: P3)

**Goal**: No redundant status label on cards; the column alone conveys status.

**Independent Test**: View the board — cards carry no status badge; column headers still name the stage (quickstart.md Scenario 1 step 2).

### Tests for User Story 6

- [ ] T034 [P] [US6] Component tests (fail first): `TaskCard` renders no `StatusBadge`/status text, `KanbanColumn` header still names its stage; in frontend/src/lib/components/TaskCard.svelte.test.ts and frontend/src/lib/components/KanbanColumn.svelte.test.ts (FR-009, SC-006)

### Implementation for User Story 6

- [ ] T035 [US6] Remove the `StatusBadge` usage (and its import) from frontend/src/lib/components/TaskCard.svelte, leaving the `StatusBadge` component itself in place for its other call sites (FR-009, SC-006)

**Checkpoint**: All six stories independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T036 Observability completeness audit (MANDATORY — Constitution Principle III/IV): cross-reference every row of plan.md ## Observability (3 metrics, 6 log events, 3 spans) against its implementing task (T015/T017/T025/T031) and passing test (T016/T018/T026/T032), and file any gap as a new task before declaring the DoD met (phase goal — audits FR/SC coverage globally)
- [ ] T037 Logging contract CI enforcement (MANDATORY — Constitution Principle IV): verify the standard PR pipeline runs `Grimoire.IntegrationTests` including the new logging tests (T016, T026, T032) with no filter excluding them, in .github/workflows/ (or the repo's CI config) (plan.md ## Observability logging contract)
- [ ] T038 Trace contract CI enforcement (MANDATORY — Constitution Principle IV): same verification for the trace tests in T018, T026, T032 (plan.md ## Observability trace contract)
- [ ] T039 Frontend test CI enforcement: verify the PR pipeline runs `npm test` (vitest) covering T010/T011/T014/T022/T027/T033/T034 (phase goal — CI coverage for SC-003, SC-004, SC-006, SC-007)
- [ ] T040 Agent-behavior evaluation completeness audit (MANDATORY — Constitution Principles II & V): confirm and record in the audit that spec.md defines no agent-judgment success criterion (all SC-001…SC-008 are deterministic guarantees), so no evaluation test is owed by this feature (phase goal)
- [ ] T041 Run all quickstart.md validation scenarios (1–5) against a locally running Hub + frontend and record outcomes (SC-001, SC-002, SC-003, SC-004, SC-005, SC-006, SC-007, SC-008)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0**: Declaration only — done.
- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories (history table T003 → recorder T004 → test T006; T005 parallel to T004).
- **User stories (Phases 3–8)**: All depend on Phase 2. After that they are mutually independent with two soft edges: US2's T014 touches the same board files as US6's T035 (coordinate if parallel), and US3's T022 touches the same detail page as US1's T011/US5's T033 (sequence within the file).
- **Phase 9 (Polish)**: After all desired stories.

### User Story Dependencies

- **US1 (P1)**: Only Phase 2. — **US2 (P1)**: Only Phase 2 (+T001/T002 from Setup). — **US3 (P2)**: Only Phase 2. — **US4 (P2)**: Only Phase 2. — **US5 (P2)**: Only Phase 2; richer manual validation if US2 exists (produces failed tasks), but tests create failed tasks directly. — **US6 (P3)**: None beyond Phase 2.

### Within Each User Story

Tests first (must fail) → backend model/coordinator → endpoints → observability signals + their tests → frontend. Story complete before the next priority (or parallel, see below).

### Parallel Opportunities

- Phase 1: T001 ∥ T002.
- Phase 2: T005 ∥ T003/T004.
- After Phase 2: US1, US3, US4 touch disjoint backend surfaces and can run in parallel; US2 and US5 both modify `IngestRunCoordinator.cs` — do them sequentially relative to each other.
- Within stories: every task marked [P] (all fail-first test tasks, observability test tasks, and type-only tasks) parallelizes against its phase siblings.

## Parallel Example: after Phase 2, three developers

```bash
# Developer A (US1): T007 → T008 → T009 ∥ T010 → T011
# Developer B (US2, then US5 — same coordinator file): T012 → T013 → T014..T018, then T028 → T029..T033
# Developer C (US3, then US4, then US6): T019 → T020..T022, then T023 → T024..T027, then T034 → T035
```

## Implementation Strategy

**MVP first**: Phases 0–3 (Setup, Foundational, US1). That alone delivers the diagnosable status path — the direct answer to the incident that triggered this feature. **Stop and validate** via quickstart Scenario 2.

**Incremental delivery**: add US2 (recovery), then US3+US4 (identification & source access), then US5 (restart), then US6 (cosmetic). Each checkpoint is independently demonstrable; Phase 9 gates the DoD.

## Notes

- Total: 41 tasks (T001–T041). Every task carries checkbox, ID, [P] where parallelizable, [US#] in story phases, an explicit file path, and a literal FR/SC citation.
- All backend tests are classicist and state-based; the only doubles are the existing `FakeAgentProcessLauncher` port fake and `FakeTimeProvider` (ADR-021/Principle II).
- Commit after each task or logical group.

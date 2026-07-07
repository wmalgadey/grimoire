---

description: "Task list template for feature implementation"
---

# Tasks: Ingest Intake Web UI

**Input**: Design documents from `/specs/003-ingest-intake-webui/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: This feature is harness-only (Test Strategy in plan.md maps every success criterion to a
deterministic guarantee). All test tasks below are deterministic integration/contract/component
tests; no agent-behavior evaluation tasks are required (no new agent-judgment success criterion is
introduced — the existing Ingest agent's judgment is unchanged and only observed, per FR-014).

**Logging Contract (MANDATORY)**: Every Structured Log Events row in `plan.md ## Observability` is
covered by an implementation task, a deterministic integration test task, and a CI enforcement task
(T063) — see the per-row mapping in the Dependencies section.

**Trace Contract (MANDATORY)**: Every Distributed Trace Spans row in `plan.md ## Observability` is
covered by an implementation task, a deterministic integration test task, and a CI enforcement task
(T064) — see the per-row mapping in the Dependencies section.

**Organization**: Tasks are grouped by user story (US1/US2/US3, matching spec.md priorities P1/P2/P3)
to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Web application per plan.md `## Project Structure`: `backend/src/`, `backend/tests/`,
`frontend/src/`, `frontend/tests/`. Raw storage (`raw/originals/`, `raw/sources/`) lives at the
repository root, sibling to the content root, per `contracts/source-artifact-reference.md`.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify a structural boundary test before any feature code is written.

This feature substantially expands `Grimoire.Hub` (it now creates and owns the Task Artifact
through two new pre-agent stages, and auto-triggers the agent). ADR-002 fixes the Ingest agent as a
standalone console app invoked as a child process — the Hub must never gain an in-process/assembly
dependency on `Grimoire.IngestAgent`, even as its submission responsibilities grow. That boundary is
not yet guarded by an automated test (existing ArchTests only guard `Grimoire.Domain`'s
dependency-free boundary and `Grimoire.IngestAgent`'s internal write namespaces).

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [ ] T001 Write and verify a NetArchTest rule enforcing ADR-002 in
  `backend/tests/Grimoire.ArchTests/HubAgentDispatchBoundaryRuleTests.cs`: assert
  `Types.InAssembly(typeof(Grimoire.Hub.HubTracing).Assembly).ShouldNot().HaveDependencyOn("Grimoire.IngestAgent")`
  (Hub must only ever invoke the Ingest agent via the child-process dispatcher in
  `Grimoire.Hub.AgentDispatch`, never via a direct assembly/project reference)

  **Red/Green probe** (required):
  1. Write the rule as above.
  2. Temporarily add `<ProjectReference Include="..\Grimoire.IngestAgent\Grimoire.IngestAgent.csproj" />`
     to `backend/src/Grimoire.Hub/Grimoire.Hub.csproj` and add a throwaway call from
     `Grimoire.Hub` into a `Grimoire.IngestAgent` type (e.g. `Grimoire.IngestAgent.AgentCliOptions`).
  3. Run the test — it MUST fail.
  4. Revert the probe `ProjectReference` and the throwaway call.
  5. Run the test again — it MUST pass.

  **Definition of Done**:
  - [ ] Rule written and committed
  - [ ] Red/Green probe completed (commit message documents the probe result)
  - [ ] Test passes in CI with no violations (probe reverted)

**Checkpoint**: Structural boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization for the new frontend app and backend scaffolding this feature needs.

- [ ] T002 Scaffold the SvelteKit app in `frontend/` (TypeScript): `package.json`, `svelte.config.js`,
  `vite.config.ts`, `tsconfig.json`, `src/app.html`, `src/routes/+layout.svelte` — replacing the
  `frontend/README.md` placeholder note that frontend work is out of scope (ADR-001)
- [ ] T003 [P] Configure Tailwind CSS + a project design-token layer in `frontend/tailwind.config.ts`,
  `frontend/postcss.config.js`, `frontend/src/app.css` (research.md Decision 5)
- [ ] T004 [P] Configure Vitest + Testing Library in `frontend/vitest.config.ts` and
  `frontend/tests/setup.ts` (plan.md Testing)
- [ ] T005 [P] Add the MarkItDown execution-adapter configuration surface in
  `backend/src/Grimoire.Hub/Conversion/MarkItDownOptions.cs` (executable path/timeout options; no
  conversion logic yet) and document the local MarkItDown prerequisite in
  `specs/003-ingest-intake-webui/quickstart.md ## Prerequisites`
- [ ] T006 [P] Implement raw-storage path resolution in
  `backend/src/Grimoire.Hub/ContentRoot/RawStoragePaths.cs`, resolving `raw/originals/` and
  `raw/sources/` at the repository root (sibling to the content root), per
  `contracts/source-artifact-reference.md ## Path Conventions`
- [ ] T007 [P] Add the backend package references this feature needs (SignalR client test package
  for integration tests) to `backend/src/Grimoire.Hub/Grimoire.Hub.csproj` and
  `backend/tests/Grimoire.IntegrationTests/Grimoire.IntegrationTests.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entities, adapters, and plumbing that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T008 Extend `IngestTaskStatus` in `backend/src/Grimoire.Domain/Ingest/IngestTaskStatus.cs` with
  the two new pre-agent stages `Received` and `Converting` (data-model.md TaskArtifact; existing
  `Queued|Running|Completed|Failed` values and their agent-owned semantics are unchanged)
- [ ] T009 [P] Create the `IngestSubmissionKind` enum (`Url`, `MarkdownFile`, `PdfFile`, `OfficeFile`)
  in `backend/src/Grimoire.Domain/Ingest/IngestSubmissionKind.cs` (data-model.md IngestSubmission)
- [ ] T010 [P] Implement `HubTaskArtifactWriter` in `backend/src/Grimoire.Hub/TaskArtifact/HubTaskArtifactWriter.cs`:
  creates/updates the existing task-artifact markdown file (same frontmatter contract as
  `contracts/task-artifact-format.md`) for the Hub-owned `received/converting/queued/failed` stages,
  without any dependency on `Grimoire.IngestAgent` (respects the T001 boundary)
- [ ] T011 [P] Implement `SourceArtifactStore` in `backend/src/Grimoire.Hub/Conversion/SourceArtifactStore.cs`:
  persists the original payload to `raw/originals/{task_id}{ext}` and the normalized markdown to
  `raw/sources/{task_id}.md` (via T006), computes the SHA-256 checksum, and guarantees no partial
  normalized file remains after a failed write (data-model.md SourceArtifactSet)
- [ ] T012 [P] Implement `MarkItDownConverter` in `backend/src/Grimoire.Hub/Conversion/MarkItDownConverter.cs`:
  process-invocation adapter converting PDF/Office/fetched-URL content to Markdown (research.md
  Decision 1, using T005's options)
- [ ] T013 [P] Implement `UrlContentFetcher` in `backend/src/Grimoire.Hub/Conversion/UrlContentFetcher.cs`:
  fetches URL content at submission time with timeout/error classification (research.md Decision 3)
- [ ] T014 Implement `IngestLifecyclePublisher` and the SignalR hub in
  `backend/src/Grimoire.Hub/Realtime/IngestLifecyclePublisher.cs` and
  `backend/src/Grimoire.Hub/Realtime/IngestLifecycleHub.cs`, at route `/hubs/ingest-lifecycle` with
  event channel `taskLifecycleChanged`, emitting `RealtimeLifecycleEvent` payloads (data-model.md
  RealtimeLifecycleEvent; `contracts/ingest-lifecycle-events.md`) — depends on T008
- [ ] T015 Implement `KanbanBoardProjectionStore` in
  `backend/src/Grimoire.Hub/IngestSubmission/KanbanBoardProjectionStore.cs`, maintaining the
  `KanbanBoardProjection` read model from Task Artifact state (data-model.md KanbanBoardProjection)
  — depends on T008, T010
- [ ] T016 Wire the `/api/ingest-submissions` minimal-API routing group and the SignalR hub mapping
  (plumbing only, no endpoint logic yet) into `backend/src/Grimoire.Hub/Program.cs` — depends on
  T014, T015
- [ ] T017 [P] Implement frontend service adapters in `frontend/src/lib/services/ingestSubmissionsApi.ts`
  (REST client for `contracts/ingest-submission-api.md`) and
  `frontend/src/lib/services/ingestLifecycleClient.ts` (SignalR client wrapper for
  `contracts/ingest-lifecycle-events.md`)
- [ ] T018 [P] Implement shared reusable component skeletons in `frontend/src/lib/components/`:
  `StatusBadge.svelte`, `KanbanColumn.svelte`, `TaskCard.svelte` (research.md Decision 4; behavior is
  filled in by the user-story phases below)

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Submit a source and see it ingested end to end (Priority: P1) 🎯 MVP

**Goal**: A user submits one source (URL, Markdown, PDF, or Office file); the UI immediately
confirms acceptance; conversion/persistence happen automatically; the Ingest agent is triggered
automatically once `queued`; the user sees the outcome through to `completed`/`failed`.

**Independent Test**: Submit a single Markdown file through the UI and verify an acknowledgment and
a non-terminal task status appear immediately, followed by status changes through conversion,
storage, and the triggered ingest run, ending in a completed or failed outcome — without querying
the filesystem.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T019 [P] [US1] Contract test for `POST /api/ingest-submissions` (URL JSON body and multipart
  file variants, 202/400/415/422 responses) in
  `backend/tests/Grimoire.IntegrationTests/IngestSubmissionApiTests.cs`
  (`contracts/ingest-submission-api.md`)
- [ ] T020 [P] [US1] Integration test: full lifecycle `received→converting→queued→running→completed`
  proceeds automatically with no user action, using a fake dispatcher, in
  `backend/tests/Grimoire.IntegrationTests/IngestSubmissionLifecycleTests.cs` (SC-001, SC-006, SC-007)
- [ ] T021 [P] [US1] Integration test: URL submission is fetched, converted, and persisted as both
  original (`raw/originals/...`) and normalized (`raw/sources/...`) artifacts in
  `backend/tests/Grimoire.IntegrationTests/SourceArtifactPersistenceTests.cs` (SC-002)
- [ ] T022 [P] [US1] Integration test: a second submission reaching `queued` while a run is in
  progress waits and auto-triggers with no user action once the prior run reaches a terminal state,
  in `backend/tests/Grimoire.IntegrationTests/IngestQueueSerializationTests.cs` (FR-012, FR-013,
  SC-006, quickstart.md Scenario 4)
- [ ] T023 [P] [US1] Frontend component test: submitting via `SubmissionForm` shows an immediate
  acceptance message and a non-terminal task state, for both file and URL submissions, in
  `frontend/tests/SubmissionForm.test.ts` (Acceptance Scenarios 1-2)

### Implementation for User Story 1

- [ ] T024 [US1] Implement `IngestSubmissionValidator` in
  `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionValidator.cs`: enforce exactly one
  source per submission and reject unsupported kinds before task creation (FR-001, FR-003) —
  depends on T009
- [ ] T025 [US1] Implement `POST /api/ingest-submissions` in
  `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs`: validate (T024), create
  the Task Artifact at `received` (T010), return 202 Accepted immediately (FR-002, FR-006) —
  depends on T010, T024
- [ ] T026 [US1] Implement `IngestSubmissionPipeline` in
  `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionPipeline.cs`: drive
  `received→converting` (URL fetch via T013 or file conversion via T012), persist artifacts (T011),
  then `converting→queued` (FR-004, FR-005) — depends on T025, T011, T012, T013
- [ ] T027 [US1] Implement auto-trigger on `queued` by reusing the existing
  `IngestAgentDispatcher` (`backend/src/Grimoire.Hub/AgentDispatch/IngestAgentDispatcher.cs`,
  ADR-002 child-process model), respecting the single-concurrent-run constraint (FR-010, FR-013) —
  depends on T026
- [ ] T028 [US1] Wire every `IngestSubmissionPipeline` transition to `IngestLifecyclePublisher` (T014)
  so each stage change emits a realtime event — depends on T014, T026, T027
- [ ] T029 [P] [US1] Implement `SubmissionForm.svelte` in `frontend/src/lib/components/SubmissionForm.svelte`,
  posting through `ingestSubmissionsApi` (T017) and rendering immediate acceptance + non-terminal
  state — depends on T017
- [ ] T030 [US1] Implement the submission route `frontend/src/routes/+page.svelte` composing
  `SubmissionForm` with a single-task status view — depends on T029
- [ ] T031 [US1] Add validation/error-handling for unsupported formats with a clear, actionable
  rejection message and no task created (Edge Case: unsupported file type) — depends on T024, T025
- [ ] T032 [US1] Implement the `ingest.submission.accepted` (INFO) structured log event with
  mandatory fields `task_id`, `source_kind`, `submitted_at` in T025
- [ ] T033 [US1] Implement the `ingest.submission.original.persisted` (INFO) structured log event
  with mandatory fields `task_id`, `original_path`, `size_bytes`, `content_type` in T011
- [ ] T034 [US1] Implement the `ingest.submission.conversion.completed` (INFO) structured log event
  with mandatory fields `task_id`, `source_kind`, `normalized_path`, `duration_ms` in T026
- [ ] T035 [US1] Implement the `ingest.run.triggered` (INFO) structured log event with mandatory
  fields `task_id`, `queued_duration_ms` in T027
- [ ] T036 [P] [US1] Deterministic integration test validating event name, level, and mandatory
  fields for the four events in T032-T035, in
  `backend/tests/Grimoire.IntegrationTests/IngestSubmissionLogEventTests.cs`
- [ ] T037 [US1] Implement the `hub.ingest_submission.submit` (root), `hub.ingest_submission.fetch_url`,
  `hub.ingest_submission.store_original`, `hub.ingest_submission.convert_to_markdown`,
  `hub.ingest_submission.store_normalized`, and `hub.ingest_run.trigger` spans with the declared
  parent/child linkage and attributes, across T025-T027
- [ ] T038 [P] [US1] Deterministic integration test validating span name, parent/child relationship,
  and correlation attributes for the six spans in T037, in
  `backend/tests/Grimoire.IntegrationTests/IngestSubmissionTraceTests.cs`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently (MVP).

---

## Phase 4: User Story 2 - Track all submissions on a Kanban board (Priority: P2)

**Goal**: A user opens a Kanban-style board listing every submitted source grouped by its current
lifecycle stage, reflecting stage changes live.

**Independent Test**: Submit two or three sources of different kinds in sequence, then open the
board and verify each appears as its own card, correctly grouped by its current stage, with the
board reflecting stage changes as they happen.

### Tests for User Story 2

- [ ] T039 [P] [US2] Contract test for `GET /api/ingest-submissions` board projection (every task
  appears exactly once, grouped by stage) in
  `backend/tests/Grimoire.IntegrationTests/KanbanBoardApiTests.cs` (Acceptance Scenario 1)
- [ ] T040 [P] [US2] Integration test: a connected SignalR client receives ordered
  `taskLifecycleChanged` events and the board projection reflects the new stage without a resubmit
  or manual refresh, in `backend/tests/Grimoire.IntegrationTests/IngestLifecycleRealtimeTests.cs`
  (SC-004, Acceptance Scenario 2)
- [ ] T041 [P] [US2] Frontend integration test: `KanbanBoard` groups multiple in-flight tasks by
  stage and moves a card live when a stage-change event arrives, in
  `frontend/tests/KanbanBoard.test.ts`

### Implementation for User Story 2

- [ ] T042 [US2] Implement `GET /api/ingest-submissions` in `IngestSubmissionEndpoints.cs` (T025's
  file) returning the `KanbanBoardProjection` list (FR-007) — depends on T015, T025
- [ ] T043 [US2] Implement `GET /api/ingest-submissions/{taskId}` detail endpoint in the same file,
  per the detail response shape in `contracts/ingest-submission-api.md` — depends on T042
- [ ] T044 [US2] Implement idempotent client-side event application `(eventId, taskId)` and the
  reconnect-then-refresh flow in `ingestLifecycleClient.ts` (T017), per
  `contracts/ingest-lifecycle-events.md ## Rules` — depends on T017
- [ ] T045 [US2] Implement `frontend/src/routes/board/+page.svelte` composing `KanbanColumn`/`TaskCard`
  (T018) grouped by stage, backed by the board API (T042) and the realtime client (T044) (FR-007,
  FR-008, FR-011) — depends on T018, T044
- [ ] T046 [US2] Implement the `ingest.lifecycle.published` (INFO) structured log event with
  mandatory fields `task_id`, `from_stage`, `to_stage` in `IngestLifecyclePublisher` (T014)
- [ ] T047 [P] [US2] Deterministic integration test validating event name, level, and mandatory
  fields for `ingest.lifecycle.published` in
  `backend/tests/Grimoire.IntegrationTests/IngestLifecycleLogEventTests.cs`
- [ ] T048 [US2] Implement the `hub.ingest_lifecycle.publish_update` span (parent:
  `hub.ingest_submission.submit`) with `task_id`, `stage` attributes in `IngestLifecyclePublisher` (T014)
- [ ] T049 [P] [US2] Deterministic integration test validating span name, parent/child relationship,
  and correlation attributes for `hub.ingest_lifecycle.publish_update` in
  `backend/tests/Grimoire.IntegrationTests/IngestLifecycleTraceTests.cs`
- [ ] T050 [US2] Implement the `hub.ingest_lifecycle_updates_total` counter (label: `stage`)
  alongside T046

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently.

---

## Phase 5: User Story 3 - See clearly when a submission fails (Priority: P3)

**Goal**: A user submits a source that cannot be processed (unreadable document, unreachable URL,
or an agent-run failure) and the board shows the task as `failed` with a plain-language reason,
never stuck or silently dropped.

**Independent Test**: Submit a source known to fail conversion (e.g., a corrupted file or an
unreachable URL) and verify the board shows a failed task with a human-readable reason and no
stored Markdown file for that submission.

### Tests for User Story 3

- [ ] T051 [P] [US3] Integration test: a corrupted file or an unreachable URL leads to `failed` with
  a human-readable reason and no partial normalized artifact, in
  `backend/tests/Grimoire.IntegrationTests/IngestSubmissionFailureTests.cs` (SC-003, Acceptance
  Scenarios 1-2, Edge Cases)
- [ ] T052 [P] [US3] Integration test: once the triggered Ingest agent run itself fails, the board
  surfaces the agent's existing `failure_reason` unchanged, introducing no new failure semantics, in
  `backend/tests/Grimoire.IntegrationTests/IngestRunFailureVisibilityTests.cs` (Acceptance Scenario 4)
- [ ] T053 [P] [US3] Frontend component test: a failed `TaskCard` renders the reason and a link to
  the full Task Artifact, visually distinct from a completed card, within a few seconds of opening
  the board (SC-005) in `frontend/tests/TaskCard.test.ts`

### Implementation for User Story 3

- [ ] T054 [US3] Implement `ConversionFailureClassifier` in
  `backend/src/Grimoire.Hub/Conversion/ConversionFailureClassifier.cs`: maps fetch/conversion faults
  to human-readable reasons (FR-009, Edge Cases) — depends on T012, T013
- [ ] T055 [US3] Guarantee cleanup of any partial normalized artifact on the failure path in
  `SourceArtifactStore` (T011) (FR-009, SC-003)
- [ ] T056 [US3] Wire `failed` transitions — both pipeline-originated (T054) and agent-run-originated
  (passthrough of the agent's own `failure_reason`) — into `HubTaskArtifactWriter` and the board
  projection (FR-014, Acceptance Scenario 4) — depends on T010, T015, T054
- [ ] T057 [US3] Implement failed-state styling, `failure_reason` rendering, and the task-detail link
  (to `GET /api/ingest-submissions/{taskId}`, T043) in `TaskCard.svelte` (FR-014) — depends on T018, T045
- [ ] T058 [US3] Implement the `ingest.submission.url_fetch.failed` (WARN) structured log event with
  mandatory fields `task_id`, `url`, `failure_reason`, `http_status` in `UrlContentFetcher` (T013)
- [ ] T059 [US3] Implement the `ingest.submission.conversion.failed` (ERROR) structured log event
  with mandatory fields `task_id`, `source_kind`, `failure_reason` in `MarkItDownConverter`/
  `IngestSubmissionPipeline` (T012, T026)
- [ ] T060 [P] [US3] Deterministic integration test validating event name, level, and mandatory
  fields for the two events in T058-T059, in
  `backend/tests/Grimoire.IntegrationTests/IngestSubmissionFailureLogEventTests.cs`

**Checkpoint**: All three user stories should now be independently functional.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Close out the remaining Observability rows and gate everything in CI.

- [ ] T061 [P] Implement the remaining metrics not yet wired by story tasks —
  `hub.ingest_submissions_total`, `hub.ingest_submission_conversions_total`,
  `hub.ingest_submission_url_fetch_total`, `hub.ingest_submission_artifacts_persisted_total`,
  `hub.ingest_submission_queue_wait_seconds` — across T025, T026, T011, T013, T027
- [ ] T062 Observability tests: verify all 6 metrics, 7 structured log events, and 7 trace spans
  declared in `plan.md ## Observability` are emitted end-to-end, extending
  `backend/tests/Grimoire.IntegrationTests/ObservabilityMetricsTests.cs`,
  `ObservabilityLogTests.cs`, and `ObservabilityTraceTests.cs` (MANDATORY — Constitution Principle IV)
- [ ] T063 Logging contract CI enforcement: confirm the logging tests from T036, T047, and T060 run
  in the existing `dotnet test backend/tests/Grimoire.IntegrationTests` step of
  `.github/workflows/ci.yml` (MANDATORY — Constitution Principle IV)
- [ ] T064 Trace contract CI enforcement: confirm the trace tests from T038 and T049 run in the same
  existing CI step (MANDATORY — Constitution Principle IV)
- [ ] T065 [P] Add a frontend CI job to `.github/workflows/ci.yml` (install dependencies, typecheck,
  `vitest run`) so `frontend/tests/*` gate every PR
- [ ] T066 [P] Update `frontend/README.md` to remove the "frontend implementation is out of scope"
  placeholder note now that this feature implements it
- [ ] T067 Run `quickstart.md` validation end-to-end (all 4 scenarios: URL lifecycle, unsupported
  file type, conversion failure, queue serialization)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural Boundary)**: No dependencies — MUST complete first
- **Setup (Phase 1)**: Depends on Phase 0 completion
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - Can proceed in parallel if staffed, or sequentially in priority order (P1 → P2 → P3)
- **Final Phase (Polish)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) — no dependency on other stories
- **User Story 2 (P2)**: Can start after Foundational — reuses `IngestSubmissionEndpoints.cs` (T025)
  and `IngestLifecyclePublisher` (T014) from US1/Foundational but is independently testable via its
  own contract/integration/component tests
- **User Story 3 (P3)**: Can start after Foundational — reuses conversion adapters (T012/T013) and
  `HubTaskArtifactWriter`/board projection (T010/T015) but is independently testable via its own
  failure-path tests

### Logging Contract Row Mapping (Constitution Principle IV)

| Structured Log Event | Implementation | Test |
| --- | --- | --- |
| `ingest.submission.accepted` | T032 | T036 |
| `ingest.submission.url_fetch.failed` | T058 | T060 |
| `ingest.submission.conversion.completed` | T034 | T036 |
| `ingest.submission.conversion.failed` | T059 | T060 |
| `ingest.submission.original.persisted` | T033 | T036 |
| `ingest.run.triggered` | T035 | T036 |
| `ingest.lifecycle.published` | T046 | T047 |

CI enforcement for all rows: T063.

### Trace Contract Row Mapping (Constitution Principle IV)

| Distributed Trace Span | Implementation | Test |
| --- | --- | --- |
| `hub.ingest_submission.submit` | T037 | T038 |
| `hub.ingest_submission.fetch_url` | T037 | T038 |
| `hub.ingest_submission.store_original` | T037 | T038 |
| `hub.ingest_submission.convert_to_markdown` | T037 | T038 |
| `hub.ingest_submission.store_normalized` | T037 | T038 |
| `hub.ingest_lifecycle.publish_update` | T048 | T049 |
| `hub.ingest_run.trigger` | T037 | T038 |

CI enforcement for all rows: T064.

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Adapters/entities before pipeline/services; pipeline/services before endpoints; endpoints before
  frontend composition
- Story complete before moving to the next priority (if working sequentially)

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel (T003-T007)
- All Foundational tasks marked [P] can run in parallel (T009-T013, T017-T018)
- Once Foundational completes, US1/US2/US3 can start in parallel if staffed
- All tests for a user story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different developers

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Contract test for POST /api/ingest-submissions in backend/tests/Grimoire.IntegrationTests/IngestSubmissionApiTests.cs"
Task: "Integration test for full lifecycle in backend/tests/Grimoire.IntegrationTests/IngestSubmissionLifecycleTests.cs"
Task: "Integration test for URL artifact persistence in backend/tests/Grimoire.IntegrationTests/SourceArtifactPersistenceTests.cs"
Task: "Integration test for queue serialization in backend/tests/Grimoire.IntegrationTests/IngestQueueSerializationTests.cs"
Task: "Frontend component test for SubmissionForm in frontend/tests/SubmissionForm.test.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0: Structural Boundary Enforcement
2. Complete Phase 1: Setup
3. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
4. Complete Phase 3: User Story 1
5. **STOP and VALIDATE**: Run `quickstart.md` Scenario 1 (URL lifecycle) and Scenario 2 (unsupported
   file type) independently
6. Deploy/demo if ready

### Incremental Delivery

1. Complete Phase 0 + Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Complete Final Phase → CI gates the whole feature

### Parallel Team Strategy

With multiple developers:

1. Team completes Phase 0 + Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (backend pipeline + trigger)
   - Developer B: User Story 2 (board API + realtime + frontend board)
   - Developer C: User Story 3 (failure classification + frontend failed state)
3. Stories complete and integrate independently; Final Phase closes out Observability/CI

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- No agent-behavior evaluation tasks: this feature is harness-only (Constitution Principle II/V) —
  it triggers and observes the existing Ingest agent but introduces no new agent-judgment criterion
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate a story independently
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence

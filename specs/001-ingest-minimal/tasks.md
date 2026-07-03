---

description: "Task list for Ingest Minimal implementation"
---

# Tasks: Ingest Minimal

**Input**: Design documents from `/specs/001-ingest-minimal/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ingest-agent-cli.md, contracts/task-artifact-format.md, quickstart.md, docs/adr/ADR-001..005 (all Accepted)

**Tests**: Included. Constitution Principle II mandates integration tests (Testcontainers) as the primary verification mechanism for API boundaries/contracts, and plan.md's Testing section commits to specific integration tests plus one reserved unit test for the update-vs-create judgment logic.

**Organization**: Tasks are grouped by user story (spec.md P1/P2/P3) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

Per plan.md § Project Structure (Option 2, web application + standalone agent):

- `backend/src/Grimoire.Hub/` — ASP.NET Core host (Submission/, OperationalState/, AgentDispatch/)
- `backend/src/Grimoire.Domain/` — dependency-free Core Domain (Ingest/)
- `backend/src/Grimoire.IngestAgent/` — standalone console app (ADR-002)
- `backend/tests/Grimoire.ArchTests/`, `backend/tests/Grimoire.IntegrationTests/`, `backend/tests/Grimoire.Domain.UnitTests/`
- `wiki/`, `tasks/`, `log.md` — domain state at repo root, git-tracked
- `frontend/` — stack fixed by ADR-001, not scaffolded in this feature

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Guard the Domain Core dependency-free rule (Principle I) before any feature code exists.

- [ ] T001 Write and verify a NetArchTest.Rules structural boundary test in backend/tests/Grimoire.ArchTests/DomainDependencyRuleTests.cs asserting `Grimoire.Domain` does not reference `Grimoire.Hub`, `Grimoire.IngestAgent`, `Microsoft.AspNetCore.*`, or `Microsoft.Data.Sqlite`. Red/Green probe: add a temporary class in `Grimoire.Domain` that references one of the forbidden namespaces, confirm the test fails, delete the probe class, confirm the test passes. Commit message documents the probe result.

**Checkpoint**: Structural boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization per plan.md § Project Structure

- [ ] T002 Create the directory skeleton per plan.md § Project Structure: `backend/src/Grimoire.Hub/{Submission,OperationalState,AgentDispatch}`, `backend/src/Grimoire.Domain/Ingest/`, `backend/src/Grimoire.IngestAgent/`, `backend/tests/{Grimoire.ArchTests,Grimoire.IntegrationTests,Grimoire.Domain.UnitTests}/`, `frontend/` (placeholder README noting ADR-001 stack, no scaffolding), and root `wiki/`, `tasks/` directories plus an empty `log.md`
- [ ] T003 Initialize `backend/Grimoire.sln` and the six .NET 10 projects (`dotnet new classlib` for Domain, `dotnet new web` for Hub, `dotnet new console` for IngestAgent, `dotnet new xunit` for the three test projects) with project references: Hub→Domain, IngestAgent→Domain, ArchTests→Hub+Domain+IngestAgent, IntegrationTests→Hub+Domain, Domain.UnitTests→Domain
- [ ] T004 [P] Add NuGet package references: ASP.NET Core Minimal APIs + SignalR + `Microsoft.Data.Sqlite` to `backend/src/Grimoire.Hub/`; Anthropic Claude API client to `backend/src/Grimoire.IngestAgent/`; OpenTelemetry .NET SDK + OTLP exporter to both `Grimoire.Hub` and `Grimoire.IngestAgent`; `NetArchTest.Rules` to `Grimoire.ArchTests`; `Testcontainers` to `Grimoire.IntegrationTests`
- [ ] T005 [P] Add `.editorconfig` and `dotnet format`/analyzer configuration at `backend/.editorconfig`
- [ ] T006 [P] Add `.gitignore` entries for the Hub's SQLite operational-state file (ADR-003) and the local `.env` secrets file (ADR-004) at repo root

**Checkpoint**: Solution builds empty; directory layout matches plan.md.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure required by every user story — the ingest contract's own responsibilities (task artifact creation, agent spawn, operational state) apply regardless of which story is being exercised.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T007 Create dependency-free domain entities in `backend/src/Grimoire.Domain/Ingest/` (`Source`, `IngestTask`, `WikiPage`, `WikiIndexEntry`, `IngestLogEntry`) per data-model.md, including `IngestTask`'s status state machine and validation rules (`completed_at` null while non-terminal, `pages_touched` non-empty iff completed, `failure_reason` non-null iff failed)
- [ ] T008 Implement task artifact frontmatter+body reader/writer in `backend/src/Grimoire.IngestAgent/TaskArtifact/` per contracts/task-artifact-format.md, reading/writing `tasks/<YYYY-MM-DD>-ingest-<slug>.md` — depends on T007
- [ ] T009 [P] Implement the `OperationalTaskState` SQLite schema and repository in `backend/src/Grimoire.Hub/OperationalState/` per data-model.md (`task_id`, `status`, `process_id`, `updated_at`) — ADR-003
- [ ] T010 [P] Implement shared OpenTelemetry SDK bootstrap (OTLP exporter, resource attributes) in `backend/src/Grimoire.Hub/` and `backend/src/Grimoire.IngestAgent/` per ADR-005 (infra wiring only; no business metrics yet)
- [ ] T011 [P] Implement a secrets loader for `ANTHROPIC_API_KEY` from a git-ignored local file in `backend/src/Grimoire.Hub/AgentDispatch/` — ADR-004
- [ ] T012 Implement the Hub→Ingest-agent child process spawn mechanism in `backend/src/Grimoire.Hub/AgentDispatch/` per contracts/ingest-agent-cli.md (CLI args, stdin for `pasted_text`, `ANTHROPIC_API_KEY` injected only into the child process environment, exit-code handling) — depends on T009, T011
- [ ] T013 Implement the Hub `submit-source` CLI entry point in `backend/src/Grimoire.Hub/Submission/` (per quickstart.md) that creates the `OperationalTaskState` row and invokes `AgentDispatch` — depends on T009, T012

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Submit a source and get a wiki page (Priority: P1) 🎯 MVP

**Goal**: A submitted source produces (creates or updates) exactly one wiki page reflecting its content, with the original source left unmodified.

**Independent Test**: Submit one source and verify a new/updated wiki markdown page exists whose content reflects it, with no manual steps (quickstart.md Scenarios 1 & 2).

### Tests for User Story 1

- [ ] T014 [P] [US1] Unit test for the update-vs-create semantic judgment logic in backend/tests/Grimoire.Domain.UnitTests/UpdateOrCreateDecisionTests.cs (FR-012)
- [ ] T015 [P] [US1] Integration test for the git working-tree read/write contract (wiki page created/updated; source file byte-for-byte unchanged, FR-009) in backend/tests/Grimoire.IntegrationTests/WikiWriteContractTests.cs

### Implementation for User Story 1

- [ ] T016 [US1] Implement the update-vs-create decision service in backend/src/Grimoire.Domain/Ingest/ (semantic judgment informed by index.md content, FR-012) — depends on T007, T014
- [ ] T017 [US1] Implement the source reader (`file`/`url`/`pasted_text` per `--source-kind`) in backend/src/Grimoire.IngestAgent/Source/
- [ ] T018 [US1] Implement the LLM synthesis pipeline call (Anthropic Claude client) to produce wiki page content in backend/src/Grimoire.IngestAgent/Synthesis/
- [ ] T019 [US1] Implement the wiki page writer (create/update exactly one primary page under wiki/, FR-004) in backend/src/Grimoire.IngestAgent/WikiWrite/, guaranteeing the source file remains unmodified (FR-009) — depends on T016, T017, T018
- [ ] T020 [US1] Wire the end-to-end submit-source flow (Hub spawns agent → agent reads source, decides target, writes wiki page) and validate against quickstart.md Scenarios 1 & 2 — depends on T013, T019

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Observe ingest progress and outcome via a task artifact (Priority: P2)

**Goal**: A task artifact records an ingest operation's full lifecycle and outcome, readable on its own without other systems.

**Independent Test**: Submit a source and confirm a task artifact is created immediately, transitions through recognizable states, and — once finished — references touched wiki page(s) with a plain-language summary (quickstart.md Observability check + task artifact inspection).

### Tests for User Story 2

- [ ] T021 [P] [US2] Integration test for the Hub↔SQLite operational-state contract (row created on submit, updated on status change) in backend/tests/Grimoire.IntegrationTests/OperationalStateContractTests.cs
- [ ] T022 [P] [US2] Integration test for the Hub→child-process spawn contract (correct CLI args/env passed to the agent, per contracts/ingest-agent-cli.md) in backend/tests/Grimoire.IntegrationTests/AgentSpawnContractTests.cs

### Implementation for User Story 2

- [ ] T023 [US2] Implement task artifact status transitions (`queued`→`running`→`completed`) with `started_at`/`completed_at` timestamps in backend/src/Grimoire.IngestAgent/TaskArtifact/ (FR-002, FR-003) — depends on T008
- [ ] T024 [US2] Implement `pages_touched` population and the FR-011 consistency validation (no `completed` status without an existing, referenced wiki page) in backend/src/Grimoire.IngestAgent/TaskArtifact/ — depends on T019, T023
- [ ] T025 [US2] Implement human-readable narrative body generation for the task artifact (what was found, what changed, uncertainties) in backend/src/Grimoire.IngestAgent/TaskArtifact/ (FR-006) — depends on T023
- [ ] T026 [P] [US2] Implement the index.md writer (create/update an entry grouped under a semantic category heading) in backend/src/Grimoire.IngestAgent/WikiIndex/ (FR-014)
- [ ] T027 [P] [US2] Implement the log.md appender (chronological entry per operation) in backend/src/Grimoire.IngestAgent/IngestLog/ (FR-015)
- [ ] T028 [US2] Wire Hub-side `OperationalTaskState` row updates to mirror task artifact status transitions in backend/src/Grimoire.Hub/OperationalState/ (ADR-003) — depends on T009, T023

**Checkpoint**: Task artifact, index.md, and log.md are fully populated on success; US1 and US2 both independently testable.

---

## Phase 5: User Story 3 - Fail safely and visibly (Priority: P3)

**Goal**: A failed ingest leaves a task artifact clearly marked failed with a human-readable reason, and no broken or partial wiki content.

**Independent Test**: Submit an invalid/unreadable source and confirm a task artifact in a `failed` state with an explanatory reason, and that no partial wiki content results (quickstart.md Scenario 3), plus that a Hub restart mid-ingest reconciles the stuck task (quickstart.md Scenario 4).

### Tests for User Story 3

- [ ] T029 [P] [US3] Integration test for the failure path: unreadable/empty source leaves wiki/ and index.md untouched, task artifact marked failed (FR-007, FR-008) in backend/tests/Grimoire.IntegrationTests/FailureSafetyContractTests.cs
- [ ] T030 [P] [US3] Integration test for Hub restart reconciliation: a SQLite row with `status=running` is reconciled to `failed` with an interruption reason on Hub startup (FR-013) in backend/tests/Grimoire.IntegrationTests/RestartReconciliationTests.cs

### Implementation for User Story 3

- [ ] T031 [US3] Implement unreadable/empty source detection and a no-partial-write guarantee (transactional wiki/index writes) in backend/src/Grimoire.IngestAgent/Source/ and WikiWrite/ (FR-008) — depends on T017, T019
- [ ] T032 [US3] Implement `failure_reason` narrative generation and the failed-status task artifact writer in backend/src/Grimoire.IngestAgent/TaskArtifact/ (FR-007) — depends on T023, T031
- [ ] T033 [US3] Extend the log.md appender to record failed-outcome entries in backend/src/Grimoire.IngestAgent/IngestLog/ (FR-015) — depends on T027, T032
- [ ] T034 [US3] Implement Hub startup reconciliation logic (query SQLite for `status=running`, mark the corresponding task artifact `failed` with an interruption reason, emit an `ingest.task.reconciled` log event, remove the row) in backend/src/Grimoire.Hub/OperationalState/ (FR-013, ADR-003) — depends on T009, T028

**Checkpoint**: quickstart.md Scenarios 3 & 4 pass; all three user stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Observability instrumentation (constitution Principle IV DoD gate) and end-to-end validation across all stories.

- [ ] T035 [P] Observability test asserting business metrics (`wiki.ingest.operations_total`, `wiki.ingest.pages_touched_total`, `wiki.ingest.duration_seconds`, `wiki.ingest.tasks_reconciled_total`) via an in-memory OTel exporter in backend/tests/Grimoire.IntegrationTests/ObservabilityMetricsTests.cs
- [ ] T036 [P] Observability test asserting structured log events (`ingest.task.created`, `ingest.task.status_changed`, `ingest.page.written`, `ingest.failed`, `ingest.task.reconciled`) with their mandatory fields in backend/tests/Grimoire.IntegrationTests/ObservabilityLogTests.cs
- [ ] T037 [P] Observability test asserting the trace span hierarchy (`hub.ingest.submit` → `hub.ingest.spawn_agent` → `ingest_agent.process_source` → `ingest_agent.decide_page_target`/`write_wiki_page`/`update_index`/`append_log`) in backend/tests/Grimoire.IntegrationTests/ObservabilityTraceTests.cs
- [ ] T038 Instrument production code with the metrics, log events, and trace spans asserted in T035-T037 across backend/src/Grimoire.Hub/ and backend/src/Grimoire.IngestAgent/ (constitution Principle IV) — depends on T035, T036, T037, T020, T028, T034
- [ ] T039 [P] Security check verifying `ANTHROPIC_API_KEY` never appears in the Hub's own process environment, logs, or trace attributes (ADR-004 compliance) in backend/tests/Grimoire.IntegrationTests/CredentialScopingTests.cs
- [ ] T040 Run full quickstart.md validation end-to-end (Scenarios 1-4 plus the Observability check) — depends on T038, T039

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural Boundary)**: No dependencies — must complete first
- **Phase 1 (Setup)**: Depends on Phase 0
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1, P1)**: Depends on Phase 2 — no dependency on other stories
- **Phase 4 (US2, P2)**: Depends on Phase 2; T024 depends on T019 (US1's wiki writer) since `pages_touched` references pages written by US1
- **Phase 5 (US3, P3)**: Depends on Phase 2; T031/T032 depend on US1/US2 components (source reader, wiki writer, task artifact writer) they extend for failure handling
- **Phase 6 (Polish)**: Depends on Phases 3-5 being complete

### User Story Dependencies

- **US1 (P1)**: Independently testable after Phase 2 — the vertical slice's core path
- **US2 (P2)**: Builds on US1's wiki writer (T019) for `pages_touched`; otherwise independent
- **US3 (P3)**: Builds on US1's source reader/wiki writer and US2's task artifact/log writers to add failure handling; otherwise independent

### Within Each User Story

- Tests written before implementation (T014/T015 before T016-T020; T021/T022 before T023-T028; T029/T030 before T031-T034)
- Domain logic before agent/Hub wiring
- Story complete before moving to the next priority

### Parallel Opportunities

- T004, T005, T006 (Setup) in parallel
- T009, T010, T011 (Foundational) in parallel
- T014, T015 (US1 tests) in parallel
- T021, T022 (US2 tests) in parallel; T026, T027 (US2 impl, different files) in parallel
- T029, T030 (US3 tests) in parallel
- T035, T036, T037, T039 (Polish tests) in parallel

---

## Parallel Example: User Story 1

```bash
# Launch US1 tests together:
Task: "Unit test for update-vs-create judgment in backend/tests/Grimoire.Domain.UnitTests/UpdateOrCreateDecisionTests.cs"
Task: "Integration test for git working-tree contract in backend/tests/Grimoire.IntegrationTests/WikiWriteContractTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0 → Phase 1 → Phase 2 (structural guard + setup + foundation)
2. Complete Phase 3 (US1)
3. **STOP and validate**: run quickstart.md Scenarios 1 & 2 independently
4. This is the deployable MVP — a source in, a wiki page out

### Incremental Delivery

1. MVP (US1) → validate → ship/demo
2. Add US2 (task artifact lifecycle/narrative) → validate independently → ship/demo
3. Add US3 (failure safety + restart reconciliation) → validate independently → ship/demo
4. Polish phase (observability instrumentation, security check, full quickstart validation) → Definition of Done met

# Tasks: Hub Foundation + Agent Lifecycle

**Input**: Design documents from `/specs/002-hub-agent-lifecycle/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/hub-agent-api.md

**Tests**: Integration tests via Testcontainers (real SQLite), unit tests for state machine logic. Tests are written FIRST and must FAIL before implementation.

**Organization**: Tasks are grouped by user story (US1-US4) and foundational layer, enabling independent implementation and testing.

---

## Phase 0: Architecture Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Automated architecture tests must exist and FAIL before any feature code is written.

⚠️ **NON-NEGOTIABLE**: No feature implementation can begin until this test is RED.

- [x] T000 Implement NetArchTest.Rules architecture test in `src/Grimoire.Api.Tests/Architecture/DomainIsolationTests.cs` enforcing:
  - Core.Domain namespace has no imports from Infrastructure, Api, or Framework layers
  - Infrastructure.Persistence cannot import from Api layer
  - IAgentWorker is defined in Core.Domain, implemented in Infrastructure layer
  - Test must FAIL before domain code is written

**Checkpoint**: Architecture test EXISTS and FAILS. Feature code may now begin.

---

## Phase 1: Setup & Project Structure

**Purpose**: Project initialization and build configuration

- [x] T001 Create project structure per plan.md in `src/Grimoire.Api/`:
  - Core/Domain/, Core/Exceptions/, Infrastructure/Persistence/, Infrastructure/Observability/, Api/Endpoints/, Api/Handlers/
  - Tests/Unit/, Tests/Integration/, Tests/Architecture/

- [x] T002 [P] Add .NET 9 dependencies to `src/Grimoire.Api/Grimoire.Api.csproj`:
  - System.Data.SQLite for SQLite persistence
  - xUnit, Testcontainers for testing
  - OpenTelemetry packages for instrumentation
  - NetArchTest.Rules for architecture testing

- [x] T003 [P] Configure appsettings.json and appsettings.Development.json in `src/Grimoire.Api/`:
  - SQLite database file path (default: `./grimoire.db`)
  - Logging configuration (structured JSON)
  - OpenTelemetry endpoint configuration

- [x] T004 [P] Configure build targets in `.github/workflows/` for CI/CD:
  - Architecture test gate (T000) must pass before feature tests
  - Integration test stage with real SQLite
  - No merge without passing all tests

**Checkpoint**: Project structure ready, dependencies configured, CI/CD gates in place.

---

## Phase 2: Foundational Infrastructure

**Purpose**: Core domain isolation, persistence layer, and observability setup

⚠️ **CRITICAL**: No user story work can begin until this phase completes.

### Domain Core Layer (Dependency-Free)

- [x] T005 [P] Create AgentStatus enum in `src/Grimoire.Api/Core/Domain/AgentStatus.cs`:
  - Values: Unregistered, Starting, Running, Stopping, Stopped, Faulted
  - Serializable for SQLite persistence

- [x] T006 [P] Create AgentDescriptor value object in `src/Grimoire.Api/Core/Domain/AgentDescriptor.cs`:
  - Fields: AgentId, Name, Status, Capabilities[], RegisteredAt, LastHealthCheckAt
  - Immutable (record type)
  - Validation: non-empty AgentId (alphanumeric + hyphens), non-empty Name, unique per Hub instance

- [x] T007 [P] Create AgentJob aggregate root in `src/Grimoire.Api/Core/Domain/AgentJob.cs`:
  - Fields: JobId, AgentId, Payload (JSON), Status, CreatedAt, StartedAt, CompletedAt, FailedAt, ErrorMessage
  - Methods: Start(), Complete(), Fail(reason)
  - Enforce state machine: Pending → Running → (Completed | Failed)

- [x] T008 [P] Create JobStatus enum in `src/Grimoire.Api/Core/Domain/JobStatus.cs`:
  - Values: Pending, Running, Completed, Failed

- [x] T009 [P] Create AgentHealthStatus value object in `src/Grimoire.Api/Core/Domain/AgentHealthStatus.cs`:
  - Fields: AgentId, IsHealthy, CheckedAt, Message
  - Immutable (record type)

- [x] T010 Create DomainException base class in `src/Grimoire.Api/Core/Exceptions/DomainException.cs`:
  - Base for all domain-level exceptions
  - AgentAlreadyRegisteredException (409 Conflict)
  - AgentNotFoundException (404 Not Found)
  - InvalidStateTransition (400 Bad Request)

- [x] T011 Create HubAgentRegistry domain service in `src/Grimoire.Api/Core/Domain/HubAgentRegistry.cs`:
  - No external dependencies (pure logic)
  - Methods:
    - `RegisterAgent(AgentDescriptor)` → throws DomainException if duplicate
    - `StartAgent(agentId)` → validates Unregistered → Starting → Running, emits events
    - `StopAgent(agentId)` → validates Running → Stopping → Stopped, emits events
    - `MarkAgentFaulted(agentId, reason)` → transitions Running → Faulted, emits event
    - `ValidateTransition(from, to)` → bool
    - `GetRegistrySnapshot()` → dict of all agent descriptors
  - Domain events: AgentRegisteredEvent, AgentStartingEvent, AgentRunningEvent, AgentStoppingEvent, AgentStoppedEvent, AgentFaultedEvent

### Persistence Layer

- [x] T012 Create SQLite schema in `src/Grimoire.Api/Infrastructure/Persistence/InitialSchema.sql`:
  - AgentDescriptors table: AgentId (PK), Name, Status, Capabilities (JSON), RegisteredAt, LastHealthCheckAt
  - AgentJobs table: JobId (PK), AgentId (FK), Payload (JSON), Status, CreatedAt, StartedAt, CompletedAt, FailedAt, ErrorMessage
  - Indexes: idx_AgentJobs_AgentId, idx_AgentJobs_Status, idx_AgentJobs_CreatedAt

- [x] T013 Create AgentDbContext in `src/Grimoire.Api/Infrastructure/Persistence/AgentDbContext.cs`:
  - DbSet for AgentDescriptors and AgentJobs (if using EF Core alternative; otherwise direct SQLite)
  - Auto-create schema on first startup if database file missing
  - Connection string from configuration

- [x] T014 Create AgentRepository in `src/Grimoire.Api/Infrastructure/Persistence/AgentRepository.cs`:
  - Methods:
    - `SaveAgentDescriptor(AgentDescriptor)` → create/update
    - `GetAgentDescriptor(agentId)` → read single
    - `GetAllAgentDescriptors()` → read all
    - `SaveAgentJob(AgentJob)` → create/update
    - `GetAgentJobsByStatus(JobStatus)` → query
    - `RecoverState()` → load all descriptors/jobs on startup
  - Each operation wrapped in implicit transaction

### Observability Layer

- [x] T015 [P] Create OpenTelemetry Metrics in `src/Grimoire.Api/Infrastructure/Observability/Metrics.cs`:
  - Counter: `grimoire.hub.agent.registered_total` (labels: agent_id, agent_name)
  - Gauge: `grimoire.hub.agent.active_total`
  - Counter: `grimoire.hub.agent.failed_total` (labels: agent_id, reason)
  - Counter: `grimoire.hub.job.queued_total` (labels: agent_id)
  - Counter: `grimoire.hub.job.completed_total` (labels: agent_id, status)
  - Histogram: `grimoire.hub.health_check_duration_ms`

- [ ] T016 [P] Create OpenTelemetry Tracing in `src/Grimoire.Api/Infrastructure/Observability/Tracing.cs`:
  - Span names: hub.register_agent, hub.start_agent, hub.stop_agent, hub.health_check, hub.dispatch_job, hub.recover_state
  - Attributes: agent_id, agent_name, agent_count, job_id, agents_count, jobs_count

- [ ] T017 Configure structured logging in `src/Grimoire.Api/Program.cs`:
  - Log events: agent_registered, agent_lifecycle_transition, agent_health_check, agent_faulted, job_dispatched, job_completed, job_failed, sqlite_recovery
  - JSON structured format (key=value pairs)

### IAgentWorker Interface

- [x] T018 Create IAgentWorker interface in `src/Grimoire.Api/Core/Domain/IAgentWorker.cs`:
  - Properties: AgentId, Descriptor
  - Methods: GetHealthAsync(), StartAsync(), StopAsync()
  - Documented with XML comments per contract

- [x] T019 Create NoOpAgent test stub in `src/Grimoire.Api.Tests/Stubs/NoOpAgent.cs`:
  - Implements IAgentWorker
  - Returns healthy status, completes start/stop immediately
  - Used for testing Hub without real agent implementations

**Checkpoint**: Domain layer complete (dependency-free), persistence configured, observability instrumented, architecture test passes.

---

## Phase 3: User Story 1 - Agent Registration & Discovery (Priority: P1)

**Goal**: Hub can register IAgentWorker implementations and query the registry.

**Independent Test**: Register 3+ agents, verify all discoverable by ID, verify duplicate ID rejected with 409 error.

### Unit Tests for US1

- [ ] T020 [P] [US1] Create unit test for AgentDescriptor validation in `src/Grimoire.Api.Tests/Unit/Domain/AgentDescriptorTests.cs`:
  - AgentId validation (non-empty, alphanumeric + hyphens)
  - Name validation (non-empty)
  - Capabilities validation (if present, non-empty strings)

- [ ] T021 [P] [US1] Create unit test for HubAgentRegistry.RegisterAgent in `src/Grimoire.Api.Tests/Unit/Domain/HubAgentRegistryTests.cs`:
  - Register new agent succeeds, emits AgentRegisteredEvent
  - Register duplicate AgentId throws AgentAlreadyRegisteredException
  - GetRegistrySnapshot returns all registered agents

### Integration Tests for US1

- [ ] T022 [P] [US1] Create integration test for POST /api/agents in `src/Grimoire.Api.Tests/Integration/AgentRegistrationTests.cs`:
  - Register single agent returns 201 with AgentDescriptor
  - Register multiple agents, all discoverable
  - Attempt duplicate registration returns 409 Conflict
  - Verify agents persisted to SQLite

- [ ] T023 [P] [US1] Create integration test for GET /api/agents in `src/Grimoire.Api.Tests/Integration/AgentRegistrationTests.cs`:
  - List all agents returns 200 with array of agents
  - Response includes agentId, name, status, capabilities, registeredAt, lastHealthCheckAt
  - Empty list when no agents registered

- [ ] T024 [P] [US1] Create integration test for GET /api/agents/{agentId} in `src/Grimoire.Api.Tests/Integration/AgentRegistrationTests.cs`:
  - Get single agent returns 200 with AgentDescriptor
  - Get non-existent agent returns 404 AgentNotFoundException

### Implementation for US1

- [ ] T025 Create RegisterAgentEndpoint in `src/Grimoire.Api/Api/Endpoints/RegisterAgentEndpoint.cs`:
  - POST /api/agents
  - Accept JSON: { agentId, name, capabilities[] }
  - Call HubAgentRegistry.RegisterAgent (enforces uniqueness)
  - Save to AgentRepository
  - Emit grimoire.hub.agent.registered_total metric
  - Emit agent_registered structured log event
  - Return 201 with AgentDescriptor or 409 on duplicate

- [ ] T026 Create ListAgentsEndpoint in `src/Grimoire.Api/Api/Endpoints/ListAgentsEndpoint.cs`:
  - GET /api/agents
  - Query all from AgentRepository
  - Return 200 with { agents[], total }

- [ ] T027 Create GetAgentEndpoint in `src/Grimoire.Api/Api/Endpoints/GetAgentEndpoint.cs`:
  - GET /api/agents/{agentId}
  - Query from AgentRepository
  - Return 200 with AgentDescriptor or 404 if not found

- [ ] T028 Create HubOrchestrationHandler in `src/Grimoire.Api/Api/Handlers/HubOrchestrationHandler.cs`:
  - Coordinates HubAgentRegistry, AgentRepository, observability
  - Methods align with endpoints
  - Handles domain exceptions, logs errors

- [ ] T029 Wire endpoints in `src/Grimoire.Api/Program.cs`:
  - Register endpoint methods in Minimal API
  - Configure dependency injection for HubAgentRegistry, AgentRepository
  - Initialize SQLite schema on startup

**Checkpoint**: US1 fully functional — agents register, duplicate IDs rejected, registry queryable, SQLite persists.

---

## Phase 4: User Story 2 - Agent Lifecycle Management (Priority: P1)

**Goal**: Hub can start, stop, and health-check registered agents through valid state transitions.

**Independent Test**: Register agents, start them (verify Running), health-check (verify 200), stop them (verify Stopped), attempt invalid transition (verify 400).

### Unit Tests for US2

- [ ] T030 [P] [US2] Create unit test for agent state machine in `src/Grimoire.Api.Tests/Unit/Domain/AgentLifecycleStateTests.cs`:
  - Valid transitions: Unregistered → Starting → Running → Stopping → Stopped
  - Valid fault transition: Running → Faulted
  - Invalid transitions throw InvalidStateTransition
  - Terminal states: Stopped, Faulted (no further transitions without re-registration)

### Integration Tests for US2

- [ ] T031 [P] [US2] Create integration test for POST /api/agents/{agentId}/start in `src/Grimoire.Api.Tests/Integration/AgentLifecycleTests.cs`:
  - Start Unregistered agent transitions to Running (through Starting)
  - Lifecycle events logged as structured entries
  - Invalid start (already Running) returns 400 InvalidStateTransition
  - Unknown agent returns 404

- [ ] T032 [P] [US2] Create integration test for POST /api/agents/{agentId}/stop in `src/Grimoire.Api.Tests/Integration/AgentLifecycleTests.cs`:
  - Stop Running agent transitions to Stopped (through Stopping)
  - Invalid stop (already Stopped) returns 400
  - Unknown agent returns 404

- [ ] T033 [P] [US2] Create integration test for fault detection in `src/Grimoire.Api.Tests/Integration/AgentLifecycleTests.cs`:
  - Running agent with failed health check transitions to Faulted
  - Faulted agent appears in registry with Faulted status
  - Log events capture fault with reason

### Implementation for US2

- [ ] T034 Create StartAgentEndpoint in `src/Grimoire.Api/Api/Endpoints/StartAgentEndpoint.cs`:
  - POST /api/agents/{agentId}/start
  - Call HubAgentRegistry.StartAgent (validates state machine)
  - Call IAgentWorker.StartAsync()
  - Persist updated descriptor to AgentRepository
  - Emit agent_lifecycle_transition structured log event
  - Emit grimoire_hub_agent_active_total gauge update
  - Return 200 with updated status or 400/404 on error
  - Broadcast AgentStatusChanged via SignalR

- [ ] T035 Create StopAgentEndpoint in `src/Grimoire.Api/Api/Endpoints/StopAgentEndpoint.cs`:
  - POST /api/agents/{agentId}/stop
  - Call HubAgentRegistry.StopAgent (validates state machine)
  - Call IAgentWorker.StopAsync()
  - Persist updated descriptor to AgentRepository
  - Emit agent_lifecycle_transition structured log event
  - Emit grimoire_hub_agent_active_total gauge update
  - Return 200 with updated status or 400/404 on error
  - Broadcast AgentStatusChanged via SignalR

- [ ] T036 Create HealthCheckService in `src/Grimoire.Api/Api/Handlers/HealthCheckService.cs`:
  - Periodic or on-demand health checks for all agents
  - Call IAgentWorker.GetHealthAsync() for each agent
  - Detect faults (IsHealthy=false), call HubAgentRegistry.MarkAgentFaulted()
  - Persist updated descriptor
  - Emit agent_health_check structured log event
  - Update grimoire_hub_agent_failed_total counter on fault

- [ ] T037 Integrate SignalR for real-time updates in `src/Grimoire.Api/Program.cs`:
  - Configure SignalR hub for agent status broadcasts
  - Broadcast AgentStatusChanged after state transitions
  - Include agentId, previousStatus, currentStatus, transitionedAt, reason

**Checkpoint**: US2 fully functional — agents transition through valid states, lifecycle logged, health-checked, faults detected and broadcast.

---

## Phase 5: User Story 3 - Health Endpoint (Priority: P2)

**Goal**: Operator can query `/health` endpoint to verify Hub and all agents are operational.

**Independent Test**: Call GET /health with all agents Running (expect HTTP 200), with one agent Faulted (expect HTTP 503), verify response includes per-agent status.

### Integration Tests for US3

- [ ] T038 [P] [US3] Create integration test for GET /health (all healthy) in `src/Grimoire.Api.Tests/Integration/HealthEndpointTests.cs`:
  - Register 2+ agents, start all
  - GET /health returns 200 OK
  - Response includes: overall="Healthy", timestamp, agents[] with status, lastHealthCheckAt
  - Response time < 50ms (performance requirement)

- [ ] T039 [P] [US3] Create integration test for GET /health (degraded) in `src/Grimoire.Api.Tests/Integration/HealthEndpointTests.cs`:
  - Register 2 agents, start both, fault one
  - GET /health returns 503 Service Unavailable
  - Response includes: overall="Degraded", agents with mixed statuses
  - Include faultReason for Faulted agent

- [ ] T040 [P] [US3] Create integration test for GET /health (empty) in `src/Grimoire.Api.Tests/Integration/HealthEndpointTests.cs`:
  - No agents registered
  - GET /health returns 200 (Hub itself healthy)
  - agents[] is empty

### Implementation for US3

- [ ] T041 Create HealthEndpoint in `src/Grimoire.Api/Api/Endpoints/HealthEndpoint.cs`:
  - GET /health
  - Collect all agent descriptors from AgentRepository
  - Determine overall status: Healthy (all Running), Degraded (any Faulted), Unknown (none Running)
  - Emit grimoire_hub_health_check_duration_ms histogram
  - Emit hub.health_check distributed trace span
  - Return 200 if all Running, 503 if any Faulted
  - Response: { overall, timestamp, agents[] }

- [ ] T042 Emit health_check structured logs in `src/Grimoire.Api/Api/Handlers/HealthCheckService.cs`:
  - Log agent_health_check event for each agent with status, timestamp
  - Include optional message (e.g., "timeout", "connection refused")

**Checkpoint**: US3 fully functional — /health endpoint returns correct status codes and agent details, performance SLA met.

---

## Phase 6: User Story 4 - State Persistence (Priority: P2)

**Goal**: Hub operational state (agents, jobs) survives restarts without polluting Git-managed domain content.

**Independent Test**: Register agents, queue jobs, stop Hub, restart Hub, verify agents and jobs recovered from SQLite, verify git status unchanged.

### Unit Tests for US4

- [ ] T043 [P] [US4] Create unit test for AgentJob state machine in `src/Grimoire.Api.Tests/Unit/Domain/AgentJobTests.cs`:
  - Job creation sets Pending status, CreatedAt timestamp
  - Transition to Running sets StartedAt
  - Transition to Completed sets CompletedAt, validates CreatedAt ≤ StartedAt ≤ CompletedAt
  - Transition to Failed sets FailedAt, ErrorMessage

### Integration Tests for US4

- [ ] T044 [P] [US4] Create integration test for SQLite persistence in `src/Grimoire.Api.Tests/Integration/SqlitePersistenceTests.cs`:
  - Register agents, verify saved to AgentDescriptors table
  - Query SQLite directly, verify schema matches contract
  - No null violations, proper types (TEXT, INTEGER)

- [ ] T045 [P] [US4] Create integration test for state recovery in `src/Grimoire.Api.Tests/Integration/SqlitePersistenceTests.cs`:
  - Register 3 agents, start 2
  - Stop Hub (simulate by closing DbContext)
  - Restart Hub, verify agents recovered from SQLite with correct statuses
  - Verify RecoverState() timing < 500ms
  - Verify startup logs include sqlite_recovery event

- [ ] T046 [P] [US4] Create integration test for job persistence in `src/Grimoire.Api.Tests/Integration/SqlitePersistenceTests.cs`:
  - Dispatch 10 jobs to agents, verify saved to AgentJobs table
  - Query by status (Pending, Completed, Failed)
  - Verify no data loss across restarts

- [ ] T047 [P] [US4] Create integration test for Git isolation in `src/Grimoire.Api.Tests/Integration/SqlitePersistenceTests.cs`:
  - Perform all operations (register, start, dispatch jobs)
  - Run `git status` subprocess, verify no changes to wiki/, audits/, raw/ directories
  - Verify only SQLite (./grimoire.db) is modified in working directory

### Implementation for US4

- [ ] T048 Create AgentDbInitializer in `src/Grimoire.Api/Infrastructure/Persistence/AgentDbInitializer.cs`:
  - On Hub startup, check if database file exists
  - If not, create file and execute InitialSchema.sql
  - If exists, verify schema matches contract (or migrate if needed)
  - Log initialization event: sqlite_recovery with agents_recovered, jobs_recovered, duration_ms

- [ ] T049 Implement AgentRepository.RecoverState() in `src/Grimoire.Api/Infrastructure/Persistence/AgentRepository.cs`:
  - Load all AgentDescriptors from database
  - Load all AgentJobs from database (filter by status for efficiency)
  - Rebuild HubAgentRegistry in-memory state
  - Emit structured log: sqlite_recovery

- [ ] T050 Create SqliteConfigurationProvider in `src/Grimoire.Api/Infrastructure/Persistence/SqliteConfigurationProvider.cs`:
  - Read database file path from appsettings.json or environment variable (GRIMOIRE_DB_PATH)
  - Support both relative and absolute paths
  - Create parent directories if needed

- [ ] T051 Wire startup sequence in `src/Grimoire.Api/Program.cs`:
  - Initialize SQLite on startup (T048)
  - Recover state on startup (T049)
  - Register agents that were registered before shutdown

**Checkpoint**: US4 fully functional — state persists to SQLite, recovers on restart, Git content never polluted, performance SLA met.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements affecting multiple user stories, final validation, documentation.

- [ ] T052 [P] Add comprehensive error handling in `src/Grimoire.Api/Api/Middleware/ExceptionHandlingMiddleware.cs`:
  - Catch all domain exceptions (DomainException, InvalidStateTransition, etc.)
  - Map to appropriate HTTP status codes (400, 404, 409)
  - Return JSON error response per contract (error, message, statusCode)
  - Log error events with stack traces for 500 errors

- [ ] T053 [P] Add validation middleware in `src/Grimoire.Api/Api/Middleware/ValidationMiddleware.cs`:
  - Validate request payloads (agentId format, required fields)
  - Return 400 Bad Request with validation errors

- [ ] T054 [P] Implement additional observability in `src/Grimoire.Api/Infrastructure/Observability/`:
  - Add request/response tracing via OpenTelemetry middleware
  - Correlate traces with W3C Trace Context headers
  - Export metrics to Prometheus (if configured)
  - Export traces to Jaeger (if configured)

- [ ] T055 [P] Add integration test for all scenarios in `src/Grimoire.Api.Tests/Integration/EndToEndTests.cs`:
  - Run quickstart.md scenarios 1-5 programmatically
  - Register 3+ agents, lifecycle transitions, health checks, persistence, observability
  - Verify all acceptance scenarios from spec.md pass

- [ ] T056 Verify architecture test still passes in `src/Grimoire.Api.Tests/Architecture/DomainIsolationTests.cs`:
  - Re-run T000
  - Core.Domain remains dependency-free
  - No circular dependencies introduced

- [ ] T057 [P] Add README documentation in `docs/HUB_FOUNDATION.md`:
  - Overview of Hub Foundation feature
  - Architecture diagram (Hub-Spoke)
  - API quick reference
  - Observability metrics and logs
  - Troubleshooting guide

- [ ] T058 Run full quickstart.md validation in `docs/`:
  - Execute quickstart.md scenarios 1-5 manually
  - Verify all test checklist items pass
  - Capture screenshots or logs of successful runs
  - Verify performance requirements met (lifecycle < 100ms, health < 50ms, recovery < 500ms)

- [ ] T059 [P] Performance benchmarking in `src/Grimoire.Api.Tests/Performance/BenchmarkTests.cs`:
  - Measure lifecycle transition times (target: < 100ms)
  - Measure health check endpoint response (target: < 50ms)
  - Measure SQLite recovery time (target: < 500ms)
  - Assert targets in CI/CD pipeline

- [ ] T060 Final code review and cleanup:
  - Remove test stubs (NoOpAgent) if moved to separate assemblies
  - Verify all code follows C# style guidelines
  - Ensure no dead code or unused imports
  - Add XML comments to public API (endpoints, domain services)

**Checkpoint**: All user stories complete, tested, documented, performance validated, production-ready.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Architecture)**: No dependencies — run immediately. Must FAIL before proceeding.
- **Phase 1 (Setup)**: No dependencies — run after Phase 0 RED.
- **Phase 2 (Foundational)**: Depends on Phase 1 completion — BLOCKS all user stories.
- **Phase 3-6 (User Stories)**: All depend on Phase 2 completion. Can run in parallel.
- **Phase 7 (Polish)**: Depends on Phases 3-6 completion.

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependencies on other stories.
- **US2 (P1)**: Can start after Phase 2 — leverages US1 registry but independently testable.
- **US3 (P2)**: Can start after Phase 2 — depends on US1/US2 for agents to query.
- **US4 (P2)**: Can start after Phase 2 — leverages US1/US2 operations but independently testable (state recovery).

### Within Each User Story

- Tests written FIRST, must FAIL before implementation (TDD)
- Models/entities before services before endpoints
- Parallel opportunities where marked [P]

### Parallel Execution Examples

```
Phase 1 Setup (parallel T002, T003, T004):
  - Add dependencies
  - Configure settings
  - Configure CI/CD

Phase 2 Foundational (parallel groups):
  Group 1: T005, T006, T007, T008, T009 (domain entities)
  Group 2: T012, T013, T014 (persistence layer)
  Group 3: T015, T016 (observability)
  Then: T010, T011, T018, T019 (sequential domain logic)

Phase 3-6 User Stories (parallel by story):
  Developer A: US1 (T020-T029)
  Developer B: US2 (T030-T037)
  Developer C: US3 (T038-T042)
  Developer D: US4 (T043-T051)

Phase 7 Polish (parallel T052, T053, T054, T055, T057, T059):
  Cross-cutting concerns can run in parallel
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0: Architecture test (RED)
2. Complete Phase 1: Setup
3. Complete Phase 2: Foundational (CRITICAL)
4. Complete Phase 3: User Story 1 (Agent Registration)
5. **STOP and VALIDATE**: Test US1 independently via quickstart scenario 1
6. Deploy/demo if ready

### Incremental Delivery

1. Phases 0-2 complete → Foundation ready
2. Add US1 → Test independently → Deploy/Demo (MVP)
3. Add US2 → Test independently → Deploy/Demo (Lifecycle control)
4. Add US3 → Test independently → Deploy/Demo (Observability)
5. Add US4 → Test independently → Deploy/Demo (Resilience)
6. Each story adds value without breaking previous stories

### Parallel Team Strategy (4 developers)

1. Everyone: Phases 0-2 together
2. Once Foundational done:
   - Developer A: US1 (T020-T029) → 4 hours
   - Developer B: US2 (T030-T037) → 4 hours
   - Developer C: US3 (T038-T042) → 3 hours
   - Developer D: US4 (T043-T051) → 4 hours
3. All stories complete in parallel → Integration/polish

---

## Testing Notes

- **Unit Tests**: AgentLifecycleStateTests, HubAgentRegistryTests, AgentJobTests only
- **Integration Tests**: All endpoint contracts, persistence, state recovery via real SQLite
- **Architecture Tests**: Phase 0 mandatory, re-run in Phase 7 to verify no violations
- **Performance Tests**: Phase 7 benchmarking validates SLAs
- **End-to-End**: quickstart.md scenarios 1-5 executed programmatically in T055
- **Git Isolation**: T047 verifies SQLite ops never commit to Git

---

## Notes

- [P] = Parallelizable (different files, no inter-task dependencies)
- [US#] = Maps task to specific user story for traceability
- Each task includes exact file paths for navigation
- Tests FIRST (Red), then implementation (Green)
- Commit after each task or logical group
- Architecture test MUST fail initially (Red → Green only after enforcement in code)
- Verify git status clean before Phase 2 (no accidental domain state commits)

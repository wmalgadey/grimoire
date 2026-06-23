# Feature Specification: Hub Foundation + Agent Lifecycle

**Feature Branch**: `002-hub-agent-lifecycle`

**Created**: 2026-06-23

**Status**: Draft

**Input**: Hub-Spoke orchestration pattern with agent registration, lifecycle management, and operational state persistence

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Agent Registration and Discovery (Priority: P1)

As the Hub, I want to register IAgentWorker implementations at startup so that the Hub knows which agents are available and can manage their lifecycle.

**Why this priority**: This is the foundational capability required by all other Hub operations. No other agent lifecycle feature can work without registered agents.

**Independent Test**: Can be fully tested by starting the Hub, registering agents, and querying the registry. Delivers value by establishing the agent inventory.

**Acceptance Scenarios**:

1. **Given** an unregistered agent, **When** the agent is registered with a unique ID and descriptor, **Then** the agent appears in the Hub's registry
2. **Given** an already-registered agent ID, **When** attempting to register the same agent ID again, **Then** a domain exception is thrown
3. **Given** multiple registered agents, **When** querying the Hub, **Then** all registered agents are discoverable by ID

---

### User Story 2 - Agent Lifecycle Management (Priority: P1)

As the Hub, I want to start, stop, and health-check registered agents so that I can ensure agents are available before dispatching work.

**Why this priority**: Critical for operational stability and ensuring agents transition through valid states. Without lifecycle control, agents cannot be reliably managed.

**Independent Test**: Can be fully tested by registering agents, starting them, verifying Running state, health-checking, then stopping them. Delivers value by guaranteeing agent operational readiness.

**Acceptance Scenarios**:

1. **Given** an unregistered agent, **When** the Hub calls start, **Then** the agent transitions from Unregistered → Starting → Running and all transitions are logged
2. **Given** a running agent, **When** the Hub calls stop, **Then** the agent transitions from Running → Stopping → Stopped and all transitions are logged
3. **Given** a running agent, **When** the agent encounters a fatal error, **Then** the Hub detects the fault and transitions the agent to Faulted status
4. **Given** any agent, **When** a lifecycle transition occurs, **Then** the event is captured as a structured log entry with agentId, fromStatus, and toStatus

---

### User Story 3 - Health Endpoint (Priority: P2)

As an operator, I want a `/health` endpoint so that I can verify the Hub and all registered agents are operational.

**Why this priority**: Enables observability and external monitoring. High priority for operations but depends on agent registration and lifecycle being complete.

**Independent Test**: Can be fully tested by calling GET /health and verifying the response structure and HTTP status code. Delivers value by providing operational visibility.

**Acceptance Scenarios**:

1. **Given** all agents are Running, **When** GET /health is called, **Then** HTTP 200 is returned with a JSON response containing overall Hub status and each agent's status (id, name, AgentStatus)
2. **Given** at least one agent is Faulted, **When** GET /health is called, **Then** HTTP 503 is returned with agent statuses in the response body
3. **Given** the Hub is running, **When** GET /health is called, **Then** the response includes agentId, agent name, and current AgentStatus for each registered agent

---

### User Story 4 - Hub Operational State Persistence (Priority: P2)

As the Hub, I want to persist agent job queue and agent status in SQLite so that operational state survives Hub restarts without polluting the Git-managed Wiki state.

**Why this priority**: Required for production resilience and prevents data loss on unexpected restarts. Depends on lifecycle management being complete.

**Independent Test**: Can be fully tested by starting the Hub, registering agents, performing operations, stopping the Hub, restarting it, and verifying state is restored. Delivers value by ensuring operational continuity.

**Acceptance Scenarios**:

1. **Given** registered agents and queued jobs, **When** the Hub is restarted, **Then** agent descriptors and job history are restored from SQLite
2. **Given** a new Hub startup, **When** the database file doesn't exist, **Then** the schema is created automatically with AgentDescriptors and AgentJobs tables
3. **Given** agent operations, **When** AgentJobs are dispatched, **Then** each job is persisted in SQLite with JobStatus and timestamps (created, started, completed/failed)
4. **Given** any SQLite persistence operation, **When** the operation completes, **Then** no Wiki domain content (Git state) is modified — only operational metadata persists

---

### Edge Cases

- What happens when Hub receives a stop request for an already-stopped agent? (No-op, return current state)
- How does the system handle agent registration with duplicate IDs? (Throws domain exception, logs error)
- What happens when a queued job's agent is detected as Faulted before job dispatch? (Job transitions to Failed, logged as routing failure)
- How does the system recover if SQLite becomes corrupted? (Logged as fatal, operator intervention required — out of scope for this feature)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Hub MUST support registration of IAgentWorker implementations with unique agent ID and AgentDescriptor
- **FR-002**: Hub MUST enforce uniqueness constraint on agent IDs (duplicate registration throws DomainException)
- **FR-003**: Hub MUST transition agents through valid lifecycle states: Unregistered → Starting → Running → Stopping → Stopped, with optional Faulted state
- **FR-004**: Hub MUST detect agent faults and transition the agent to Faulted status
- **FR-005**: Hub MUST persist all agent lifecycle transitions as structured log events with agentId, fromStatus, toStatus, and timestamp
- **FR-006**: Hub MUST provide a GET /health endpoint returning HTTP 200 if all agents are operational, HTTP 503 if any agent is Faulted
- **FR-007**: GET /health MUST return JSON response with Hub status and per-agent status (id, name, AgentStatus)
- **FR-008**: Hub MUST persist AgentDescriptors and AgentJobs in SQLite (operational state only)
- **FR-009**: Hub MUST create SQLite schema automatically on first startup if database file doesn't exist
- **FR-010**: Hub MUST NOT modify Wiki content (Git-managed state) through persistence operations — only operational metadata (jobs, agent descriptors) persists to SQLite
- **FR-011**: SQLite file path MUST be configurable via application configuration

### Key Entities

- **AgentDescriptor**: Metadata describing a registered agent (id, name, status, capabilities — capabilities are context-aware per bounded context)
- **AgentStatus**: Enum with values Unregistered, Starting, Running, Stopping, Stopped, Faulted
- **AgentJob**: A unit of work dispatched to an agent (id, agentId, payload, JobStatus, createdAt, startedAt, completedAt/failedAt)
- **JobStatus**: Enum with values Pending, Running, Completed, Failed

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hub successfully registers and discovers 3+ agents without registration failures
- **SC-002**: Agent lifecycle transitions (start/stop) complete within 100ms and are logged as structured events
- **SC-003**: GET /health endpoint responds with correct agent statuses within 50ms
- **SC-004**: SQLite persists 100+ AgentJobs without data loss across Hub restarts
- **SC-005**: Hub operational state recovery from SQLite completes within 500ms on startup
- **SC-006**: No Git-managed content is modified by Hub persistence operations (verified via git status post-operation)

## Assumptions

- IAgentWorker interface remains an abstract contract during this feature; concrete implementations will be provided in future features (NoOpAgent stub is sufficient for testing)
- IChannel interface remains an abstract contract during this feature; channel implementation will follow in future features
- AgentJob dispatch logic is out of scope — this feature only persists job metadata, does not execute jobs
- Wiki domain logic is out of scope — this feature operates only on operational metadata
- Hub runs as a single .NET Minimal API application (scaling/distribution is future work per ADR-006)
- SQLite is suitable for single-Hub operational state; multi-Hub scenarios are out of scope
- Application configuration (appsettings.json or environment variables) provides SQLite database file path; no runtime UI for path configuration

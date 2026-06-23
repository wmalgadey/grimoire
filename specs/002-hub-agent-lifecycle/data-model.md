# Data Model: Hub Foundation + Agent Lifecycle

**Phase**: Phase 1 (Design)
**Date**: 2026-06-23
**Spec**: [spec.md](spec.md)
**Research**: [research.md](research.md)

## Core Entities

### AgentStatus (Enum)

State machine representation for agent lifecycle.

```
Unregistered
    ↓ (register + start)
Starting
    ↓ (start completes)
Running
    ├─→ Stopping (on stop request or fault)
    └─→ Faulted (on health check failure)

Stopping
    ↓ (stop completes)
Stopped

Faulted
    ↓ (manual restart, not automatic)
Starting
```

**Valid Transitions** (enforced by Hub state machine):
- Unregistered → Starting (register + start)
- Starting → Running (startup successful)
- Running → Stopping (stop request)
- Stopping → Stopped (shutdown successful)
- Running → Faulted (health check detects fault)
- Stopped → (Terminal, no further transitions without re-registration)
- Faulted → (Terminal until operator intervention)

**Representation**:
```csharp
public enum AgentStatus
{
    Unregistered = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5
}
```

---

### AgentDescriptor (Value Object)

Immutable metadata describing a registered agent.

**Fields**:
- `AgentId` (string, unique) — Machine-readable identifier (e.g., "ingest-v1", "query-001")
- `Name` (string) — Human-readable name (e.g., "Wiki Ingest Agent")
- `Status` (AgentStatus) — Current state
- `Capabilities` (string[], nullable) — Context-aware capabilities (e.g., ["parse-pdf", "extract-markdown"])
- `RegisteredAt` (DateTime, UTC) — When agent was registered
- `LastHealthCheckAt` (DateTime?, UTC) — Last successful health check time

**Invariants**:
- AgentId is non-empty and unique across Hub instance
- Name is non-empty
- Capabilities list is optional but if present, all strings are non-empty
- RegisteredAt is always set at registration time
- LastHealthCheckAt updated only after successful health checks

**Usage**:
```csharp
public record AgentDescriptor(
    string AgentId,
    string Name,
    AgentStatus Status,
    string[]? Capabilities,
    DateTime RegisteredAt,
    DateTime? LastHealthCheckAt);
```

---

### AgentJob (Aggregate Root)

A unit of work dispatched to an agent.

**Fields**:
- `JobId` (string, unique) — Machine-readable job identifier (e.g., "job-2026-06-23-001")
- `AgentId` (string, FK to AgentDescriptors) — Target agent
- `Payload` (JSON string) — Work specification (format defined by agent's capabilities)
- `Status` (JobStatus) — Current job state
- `CreatedAt` (DateTime, UTC) — When job was queued
- `StartedAt` (DateTime?, UTC) — When agent began processing
- `CompletedAt` (DateTime?, UTC) — When job finished successfully
- `FailedAt` (DateTime?, UTC) — When job failed
- `ErrorMessage` (string?, nullable) — Failure reason if failed

**Invariants**:
- JobId is unique
- AgentId references a registered agent (FK constraint)
- Payload is valid JSON
- Status transitions are ordered: Pending → Running → (Completed | Failed)
- StartedAt ≥ CreatedAt
- CompletedAt or FailedAt ≥ StartedAt
- Only one of CompletedAt or FailedAt is non-null
- ErrorMessage non-null only if status is Failed

**Usage**:
```csharp
public class AgentJob : AggregateRoot
{
    public string JobId { get; private set; }
    public string AgentId { get; private set; }
    public string Payload { get; private set; }
    public JobStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    
    // State transition methods
    public void Start() { /* validate, update Status → Running, set StartedAt */ }
    public void Complete() { /* validate, update Status → Completed, set CompletedAt */ }
    public void Fail(string reason) { /* validate, update Status → Failed, set FailedAt, ErrorMessage */ }
}
```

---

### JobStatus (Enum)

State machine for job lifecycle.

```
Pending
    ↓ (agent starts processing)
Running
    ├─→ Completed (work successful)
    └─→ Failed (work unsuccessful)
```

**Representation**:
```csharp
public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
```

---

### HubAgentRegistry (Domain Service)

Stateless service implementing agent registration and lifecycle state machine.

**Responsibilities**:
- Register new agents (enforce unique AgentId)
- Validate state transitions (Unregistered → Starting → Running, etc.)
- Detect agent faults (health check failures)
- Emit domain events for state transitions

**Key Methods**:
- `RegisterAgent(AgentDescriptor)` → throws DomainException if AgentId already registered
- `StartAgent(string agentId)` → transitions Unregistered → Starting → Running, emits event
- `StopAgent(string agentId)` → transitions Running → Stopping → Stopped, emits event
- `MarkAgentFaulted(string agentId, string reason)` → transitions Running → Faulted, emits event
- `ValidateTransition(AgentStatus from, AgentStatus to)` → true/false
- `GetRegistrySnapshot()` → current state of all agents

**Implementation Style** (per Constitution):
- No external dependencies (no database access, no HTTP calls)
- Pure logic focused on domain rules
- Events emitted but not published (caller decides)

---

### AgentHealthStatus (Value Object)

Result of a health check operation.

**Fields**:
- `AgentId` (string) — Which agent was checked
- `IsHealthy` (bool) — Health status
- `CheckedAt` (DateTime, UTC) — When check was performed
- `Message` (string?, nullable) — Optional status message

**Usage**:
```csharp
public record AgentHealthStatus(
    string AgentId,
    bool IsHealthy,
    DateTime CheckedAt,
    string? Message);
```

---

## Domain Events

Events emitted by HubAgentRegistry for observability and audit trail.

1. **AgentRegisteredEvent**
   - AgentId, Name, RegisteredAt

2. **AgentStartingEvent**
   - AgentId, StartedAt

3. **AgentRunningEvent**
   - AgentId, TransitionedAt

4. **AgentStoppingEvent**
   - AgentId, StoppedAt, Reason (e.g., "stop requested", "health check failure")

5. **AgentStoppedEvent**
   - AgentId, StoppedAt

6. **AgentFaultedEvent**
   - AgentId, FaultedAt, Reason (e.g., "health check failed", "unhandled exception")

7. **JobDispatchedEvent**
   - JobId, AgentId, DispatchedAt

8. **JobCompletedEvent**
   - JobId, AgentId, CompletedAt, Duration

9. **JobFailedEvent**
   - JobId, AgentId, FailedAt, ErrorMessage

---

## Validation Rules

### Agent Registration
- ✓ AgentId is non-empty, alphanumeric + hyphens (regex: `^[a-z0-9-]+$`)
- ✓ Name is non-empty (1-256 chars)
- ✓ No duplicate AgentId in registry
- ✓ Capabilities (if present) are non-empty strings

### Agent Lifecycle
- ✓ Start is only valid from Unregistered state
- ✓ Stop is only valid from Running state
- ✓ Fault detection only applies to Running agents
- ✓ Health checks return within SLA (50ms per spec)

### Jobs
- ✓ Payload is valid JSON
- ✓ Job cannot be dispatched to Faulted agent (job fails immediately)
- ✓ Job cannot be dispatched to non-Running agent (queued as Pending)
- ✓ JobId follows format: `job-{timestamp}-{sequence}` (e.g., `job-2026-06-23-0001`)

---

## Relationships

```
┌─────────────────────────────┐
│      AgentDescriptor        │
│  (registered agents)        │
├─────────────────────────────┤
│ PK: AgentId                 │
│ Name, Status, Capabilities  │
└────────────┬────────────────┘
             │ (1 to many)
             │
             ↓
┌─────────────────────────────┐
│        AgentJob             │
│  (queued/running work)      │
├─────────────────────────────┤
│ PK: JobId                   │
│ FK: AgentId                 │
│ Payload, Status, Timestamps │
└─────────────────────────────┘
```

---

## Database Persistence

**Primary Storage**: SQLite (AgentDescriptors, AgentJobs tables)

**Schema Auto-Created** on Hub startup if database file missing.

**Domain Logic**: All validation happens in HubAgentRegistry (Core.Domain layer).

**Repository Pattern**: AgentRepository (Infrastructure layer) handles:
- `SaveAgentDescriptor(AgentDescriptor)` — create/update
- `SaveAgentJob(AgentJob)` — create/update
- `GetAgentDescriptor(string agentId)` — read
- `GetAllAgentDescriptors()` — read all
- `GetAgentJobsByStatus(JobStatus)` — query
- `RecoverState()` — on startup, load all descriptors/jobs from SQLite

**Transactions**: Each save operation wraps in implicit transaction (SQLite autocommit).

---

## Phase 1 Summary

✅ Entities: AgentStatus, AgentDescriptor, AgentJob, JobStatus, HubAgentRegistry

✅ Invariants: All validation rules documented

✅ State Machines: Lifecycle transitions clearly modeled

✅ Domain Events: Observability hooks identified

✅ Persistence: SQLite schema defined, repository pattern outlined

**Ready for Phase 2 (Contracts & Quickstart)**: Proceed to contracts/ and quickstart.md.

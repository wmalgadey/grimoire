# Implementation Plan: Hub Foundation + Agent Lifecycle

**Branch**: `002-hub-agent-lifecycle` | **Date**: 2026-06-23 | **Spec**: [specs/002-hub-agent-lifecycle/spec.md](spec.md)

**Input**: Feature specification from `/specs/002-hub-agent-lifecycle/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

The Hub Foundation establishes the central orchestrator for the Grimoire system. This feature introduces:
- **Agent Registration & Discovery**: The Hub maintains a registry of IAgentWorker implementations with unique IDs and descriptors
- **Lifecycle Management**: Agents transition through states (Unregistered → Starting → Running → Stopping → Stopped) with structured event logging
- **Health Monitoring**: GET /health endpoint returns Hub + per-agent status (HTTP 200 if all healthy, HTTP 503 if any faulted)
- **Operational State Persistence**: SQLite database for agent descriptors, job queues, and execution history — separate from Git-managed domain state

The Hub operates as the single point of orchestration (Hub-Spoke pattern per ADR-006), routing requests to agents and broadcasting results via channels.

## Technical Context

**Language/Version**: C# / .NET 9

**Primary Dependencies**: 
- ASP.NET Core Minimal API (ADR-001)
- SignalR for real-time communication (ADR-001)
- SQLite (System.Data.SQLite)
- xUnit + Testcontainers (per Constitution: Pragmatic Testing Strategy)

**Storage**: 
- **Operational**: SQLite (agent descriptors, job queue, execution history) — per ADR-008
- **Domain**: Git + Markdown files (wiki/, audits/, raw/) — per ADR-007 (out of scope for this feature)

**Testing**: xUnit + Testcontainers (real SQLite instance per integration tests)

**Target Platform**: Linux server (single .NET Minimal API process per ADR-006)

**Project Type**: web-service (Hub backend orchestrator)

**Performance Goals**: 
- Agent lifecycle transitions: < 100ms
- Health endpoint response: < 50ms
- SQLite state recovery on startup: < 500ms

**Constraints**: 
- State persistence must not pollute Git-managed content (ADR-007 + ADR-008)
- SQLite schema auto-creates on first startup
- SQLite file path configurable via appsettings.json or environment variable

**Scale/Scope**: 
- Support 3+ registered agents in initial release
- Single Hub instance (clustering out of scope per ADR-006)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

✅ **I. Domain Architecture & Strategic DDD**: 
- Bounded Context identified: Hub Orchestration (agent lifecycle, job dispatch, channel routing)
- Ubiquitous Language: agent, descriptor, status, job, channel, lifecycle event
- Core Domain module: agent registry and state machine (dependency-free per constitution)

✅ **II. Pragmatic Testing Strategy**: 
- Integration tests via real SQLite (not in-memory, not mocked)
- Unit tests only for state machine logic (agent lifecycle transitions)

✅ **III. ADR-First & Test-Driven Architecture**: 
- ADRs 001, 002, 006, 008 constrain this feature (see section below)
- Architecture test required: enforce dependency-free Core Domain
- ADR-008 drafted for operational state persistence boundary

✅ **IV. Behavioral & Observable Engineering**: 
- Observability section completed below
- Business metrics, structured logs, and distributed traces enumerated

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend Framework — .NET 9 Minimal API + SignalR | Hub is built as .NET Minimal API; agent lifecycle status broadcast via SignalR |
| ADR-002 | Agent Runtime Strategy — Worker Services + IAgentWorker | Hub registers and manages IAgentWorker implementations; lifecycle state machine operates on this interface |
| ADR-006 | Hub-Spoke Orchestration | Hub is the central orchestrator; agents are passive (no agent-to-agent communication) |
| ADR-008 | Operational State Persistence — SQLite for Hub Ephemera | Agent descriptors and job queue persisted in SQLite; domain state (Git) remains separate |

**New ADR required?**: ADR-008 drafted and accepted (Operational State Persistence — SQLite for Hub Ephemera)

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `grimoire.hub.agent.registered_total` | Counter | Total agents registered since Hub startup | `agent_id`, `agent_name` |
| `grimoire.hub.agent.active_total` | Gauge | Current count of agents in Running state | None |
| `grimoire.hub.agent.failed_total` | Counter | Total agents that transitioned to Faulted state | `agent_id`, `reason` |
| `grimoire.hub.job.queued_total` | Counter | Total jobs queued for dispatch | `agent_id` |
| `grimoire.hub.job.completed_total` | Counter | Total jobs completed (success or failure) | `agent_id`, `status` |
| `grimoire.hub.health_check_duration_ms` | Histogram | Time taken for health check endpoint | None |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `agent_registered` | INFO | Agent successfully registered with Hub | `agent_id`, `agent_name`, `timestamp` |
| `agent_lifecycle_transition` | INFO | Agent state change (Starting → Running, Running → Stopping, etc.) | `agent_id`, `from_status`, `to_status`, `timestamp` |
| `agent_health_check` | INFO | Health check performed on agent | `agent_id`, `status`, `timestamp` |
| `agent_faulted` | WARN | Agent detected as faulted or crashed | `agent_id`, `reason`, `timestamp` |
| `job_dispatched` | INFO | Job sent to agent | `job_id`, `agent_id`, `timestamp` |
| `job_completed` | INFO | Job completed (success) | `job_id`, `agent_id`, `status`, `duration_ms`, `timestamp` |
| `job_failed` | ERROR | Job failed | `job_id`, `agent_id`, `error_message`, `timestamp` |
| `sqlite_recovery` | INFO | Hub recovered state from SQLite on startup | `agents_recovered`, `jobs_recovered`, `duration_ms`, `timestamp` |

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `hub.register_agent` | root | `agent_id`, `agent_name` |
| `hub.start_agent` | root | `agent_id` |
| `hub.stop_agent` | root | `agent_id` |
| `hub.health_check` | root | `agent_count` |
| `hub.dispatch_job` | root | `job_id`, `agent_id` |
| `hub.recover_state` | root | `agents_count`, `jobs_count` |

## Project Structure

### Documentation (this feature)

```text
specs/002-hub-agent-lifecycle/
├── spec.md              # Feature specification
├── plan.md              # This file (planning output)
├── research.md          # Phase 0 research (TBD)
├── data-model.md        # Phase 1 data model (TBD)
├── quickstart.md        # Phase 1 validation guide (TBD)
├── contracts/           # Phase 1 API contracts (TBD)
└── tasks.md             # Phase 2 tasks (generated by /speckit-tasks, NOT by /speckit-plan)
```

### Source Code (repository root)

```text
src/Grimoire.Api/
├── Program.cs           # Minimal API setup, Hub registration, SQLite initialization
├── Core/
│   ├── Domain/
│   │   ├── AgentStatus.cs      # Enum: Unregistered, Starting, Running, Stopping, Stopped, Faulted
│   │   ├── AgentDescriptor.cs  # Immutable value object
│   │   ├── AgentJob.cs         # Job entity with state
│   │   ├── JobStatus.cs        # Enum: Pending, Running, Completed, Failed
│   │   └── HubAgentRegistry.cs  # Domain service: state machine + registration logic
│   └── Exceptions/
│       └── DomainException.cs  # Unified domain exception (duplicate agent ID, etc.)
├── Infrastructure/
│   ├── Persistence/
│   │   ├── AgentDbContext.cs           # SQLite DbContext (auto-schema)
│   │   ├── Migrations/
│   │   │   └── InitialSchema.sql       # SQLite schema definition
│   │   └── AgentRepository.cs          # Repository for agent descriptors and jobs
│   └── Observability/
│       ├── Metrics.cs                  # OpenTelemetry metrics registration
│       └── Tracing.cs                  # OpenTelemetry trace configuration
├── Api/
│   ├── Endpoints/
│   │   ├── RegisterAgentEndpoint.cs
│   │   ├── StartAgentEndpoint.cs
│   │   ├── StopAgentEndpoint.cs
│   │   └── HealthEndpoint.cs
│   └── Handlers/
│       └── HubOrchestrationHandler.cs  # Coordinates registry, repository, logging
└── Tests/
    ├── Unit/
    │   └── Domain.AgentLifecycleStateTests.cs   # State machine unit tests
    └── Integration/
        ├── AgentRegistrationTests.cs
        ├── AgentLifecycleTests.cs
        ├── HealthEndpointTests.cs
        └── SqlitePersistenceTests.cs
```

**Structure Decision**: Flat monorepo structure per ADR-005. Hub backend (orchestrator) is the primary service in `src/Grimoire.Api/`. Core Domain logic isolated in dedicated namespace to enforce ADR-008 + Constitution dependency constraint. Infrastructure and API layers clearly separated.

## Complexity Tracking

No violations of Constitution. ADR constraints are all manageable within the stated scope.



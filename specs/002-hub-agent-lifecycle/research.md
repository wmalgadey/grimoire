# Research: Hub Foundation + Agent Lifecycle

**Phase**: Phase 0 (Research & Technical Foundations)
**Date**: 2026-06-23
**Spec**: [spec.md](spec.md)

## Executive Summary

All technical decisions are grounded in existing ADRs and project tech stack. No external research or vendor evaluations required. The Hub Foundation builds directly on established architectural choices:
- **.NET 9 Minimal API** (ADR-001) for the orchestrator
- **IAgentWorker interface + Worker Services** (ADR-002) for agent abstraction
- **Hub-Spoke pattern** (ADR-006) for orchestration topology
- **SQLite for operational state** (ADR-008) — newly drafted

## Key Technical Decisions

### 1. Backend Framework: .NET 9 Minimal API + SignalR

**Decision**: Use ASP.NET Core Minimal API for Hub implementation.

**Rationale**:
- ADR-001 mandates this choice for developer productivity and type safety
- Minimal API reduces boilerplate, making SDD (Spec-Driven Development) more effective
- SignalR enables real-time agent status broadcasts to connected clients (future use case: Web UI)

**Implementation**:
- Endpoints defined as extension methods for clarity
- Dependency Injection configured in Program.cs
- OpenTelemetry instrumentation integrated at startup

**Alternatives Considered**:
- Flask/FastAPI (Python) — weaker type safety, less developer familiarity
- Express/Node.js — weaker type system
- *Rejected*: All alternatives introduce development friction for solo developer

---

### 2. Agent Abstraction: IAgentWorker Interface (Worker Services)

**Decision**: Agents register as IAgentWorker implementations (not containers).

**Rationale**:
- ADR-002 establishes this pattern for early project stage
- No container orchestration complexity; agents live in same process as Hub
- Interface enables future migration to HTTP/gRPC without Hub changes
- SDD specs can focus on behavior, not infrastructure

**Interface Definition**:
```csharp
public interface IAgentWorker
{
    string AgentId { get; }
    AgentDescriptor Descriptor { get; }
    Task<AgentHealthStatus> GetHealthAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
```

**Consequences**:
- All agents run in the same process (worker service)
- Hub can directly invoke agent methods (no network hop)
- Test agents (NoOpAgent) register inline for testing

---

### 3. Hub Architecture: Central Orchestrator (Hub-Spoke)

**Decision**: Hub is the single point of orchestration; agents are passive.

**Rationale**:
- ADR-006 mandates this topology
- No agent-to-agent communication (prevents circular dependencies)
- All state transitions go through Hub (single source of truth)
- Request tracing naturally flows through Hub

**Data Flow**:
```
User Input (Web UI/Telegram/CLI)
  ↓
Hub (routes to appropriate agent)
  ↓
Agent (performs work, returns result)
  ↓
Hub (stores result, broadcasts via channels)
  ↓
User Output
```

**Consequences**:
- Hub is centralized but not bottlenecked (agents run in same process)
- All agent state is managed by Hub (no agent self-management)
- Future: If scaling needed, Hub can delegate to message broker without changing agent interface

---

### 4. Operational State Storage: SQLite (Dual Persistence)

**Decision**: Use SQLite for Hub ephemera (job queue, agent status), Git+Markdown for domain content.

**Rationale**:
- ADR-008 (newly drafted) separates operational state from domain state
- SQLite is embedded (no external server) — matches project stage
- State survives unplanned shutdown; supports operational resilience
- Agent write operations go to Git (domain content), not SQLite
- ADR-007 mandates Git+Markdown for user-facing knowledge

**Schema**:
```
AgentDescriptors
  ├── AgentId (PK)
  ├── Name
  ├── Status
  └── RegisteredAt

AgentJobs
  ├── JobId (PK)
  ├── AgentId (FK)
  ├── Payload (JSON)
  ├── Status
  └── CreatedAt, StartedAt, CompletedAt, FailedAt

AgentHealthEvents
  ├── EventId (PK)
  ├── AgentId (FK)
  ├── Status
  └── CheckedAt
```

**Consequences**:
- No schema migrations (auto-created on startup)
- Database file path configurable (appsettings.json or env var)
- Domain content never pollutes SQLite; operational data never commits to Git

---

### 5. Testing Strategy: Integration Tests + Real SQLite

**Decision**: Primary verification via integration tests with real SQLite (not mocked).

**Rationale**:
- Constitution mandates integration tests for API boundaries and persistence
- Real SQLite instance (via Testcontainers) ensures no mock-to-prod drift
- Unit tests reserved for state machine logic (lifecycle transitions)
- Simple data mappers, pass-through adapters not tested (covered by integration tests)

**Test Structure**:
```
Unit Tests:
  └── AgentLifecycleStateTests.cs
      (state transitions: Unregistered → Starting → Running → Stopping, etc.)

Integration Tests:
  ├── AgentRegistrationTests.cs (Hub.RegisterAgent, duplicate ID check)
  ├── AgentLifecycleTests.cs (start, stop, health check, faulted detection)
  ├── HealthEndpointTests.cs (GET /health responses)
  └── SqlitePersistenceTests.cs (Hub restart, state recovery, no data loss)
```

**Tooling**:
- xUnit for test framework
- Testcontainers (if needed for real SQLite) or System.Data.SQLite directly
- Moq only for external dependencies (SignalR hubs, ILogger)

---

### 6. Observability: OpenTelemetry Metrics, Logs, Traces

**Decision**: Emit business metrics, structured logs, and distributed traces per Constitution.

**Rationale**:
- Constitution mandates observable engineering; violations fail DoD
- Metrics: Track agent count, job throughput, health status
- Logs: Capture lifecycle transitions, health checks, faults
- Traces: Enable request tracing through Hub → Agent → Result

**Implementation**:
- OpenTelemetry SDK configured in Program.cs
- Metrics exported to Prometheus (default)
- Logs structured as JSON (key=value pairs)
- Traces correlate with W3C Trace Context (automatic in SignalR)

**Consequences**:
- Observable signals are mandatory (cannot be removed without breaking DoD)
- Must be present in code before PR submission

---

## Architectural Enforcement

### Architecture Test (Required by Constitution)

**First task in tasks.md**: Implement NetArchTest.Rules enforcing:
1. Core.Domain namespace is dependency-free (no imports from Infrastructure or API)
2. Infrastructure cannot import from API layer
3. IAgentWorker is defined in Core.Domain, implemented in Infrastructure/Agents/

**Verification**:
```bash
dotnet test --filter="ArchitectureTests" --collect:"XPlat Code Coverage"
```

---

## Phase 0 Summary

✅ All research complete. No NEEDS CLARIFICATION items remain.

✅ ADRs 001, 002, 006, 008 fully define the technical approach.

✅ .NET 9 Minimal API + SignalR is the settled backend framework.

✅ IAgentWorker abstraction + Worker Services is the settled agent model.

✅ SQLite operational state + Git domain state is the settled dual-persistence model.

✅ Integration testing strategy established; tools (xUnit, Testcontainers) selected.

**Ready for Phase 1 Design**: Proceed to data-model.md, contracts/, quickstart.md.

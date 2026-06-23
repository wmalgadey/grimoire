# Implementation Plan: Domain-Driven Code Reorganization (Screaming Architecture)

**Branch**: `003-domain-driven-refactor` | **Date**: 2026-06-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-domain-driven-refactor/spec.md`

## Summary

Refactor the `.NET Grimoire.Api` codebase from layer-based (Clean Architecture) organization to domain-driven (Screaming Architecture) organization. Reorganize code by business domains (Agents, Hubs, Channels, Shared) instead of technical layers (Api, Core, Infrastructure). This improves developer onboarding, code locality, and compliance with project constitution principle I (Domain Architecture & Strategic DDD). All existing tests must pass without modification to test logic; this is purely an architectural reorganization with zero breaking changes to API contracts.

## Technical Context

**Language/Version**: C# with .NET 9, nullable reference types enabled

**Primary Dependencies**: ASP.NET Core 9, SignalR (Grimoire.Api.csproj), xUnit (tests), NetArchTest.Rules (architecture enforcement)

**Storage**: SQLite for ephemeral operational state (ADR-008); Git + Markdown for persistent content (ADR-007)

**Testing**: xUnit for unit and integration tests; Testcontainers for infrastructure testing (per Constitution Principle II); architecture tests via NetArchTest.Rules

**Target Platform**: Linux server (.NET 9 compatible)

**Project Type**: Web service with real-time communication (SignalR hub) and worker service agents

**Performance Goals**: No change to existing performance expectations; refactor is purely structural

**Constraints**: 
- No breaking changes to API contracts (external clients unaffected)
- All existing tests must pass without modification to test logic
- No circular dependencies between domains (enforced by architecture test)
- Namespace organization must follow domain structure: `Grimoire.Api.{Domain}.*`

**Scale/Scope**: 
- Current structure: `/Api`, `/Core`, `/Infrastructure` layers
- Target structure: `/Agents`, `/Hubs`, `/Channels`, `/Shared` domains
- Estimated file count to move: ~40-60 C# source files + corresponding test files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Principle I: Domain Architecture & Strategic DDD** ✅ **PASS**
- This refactor **directly implements** the constitution requirement: "Strategic Domain-Driven Design MUST be applied from the first commit. Ubiquitous Language and Bounded Context definitions are established before any code is written and reflected in all naming within the codebase."
- Current layer-based structure **violates** this principle; reorganization **fulfills** it.
- Bounded contexts: Agents (agent lifecycle), Hubs (orchestration & real-time), Channels (multi-channel abstraction), Shared (cross-cutting infrastructure)

**Principle II: Pragmatic Testing Strategy** ✅ **PASS**
- All existing integration tests will continue to work unchanged
- No test logic modifications required
- Architecture tests (new) will use NetArchTest.Rules to enforce domain boundaries

**Principle III: ADR-First & Test-Driven Architecture** ✅ **PASS**
- ADR-009 drafted and will be referenced in this plan
- First task in `tasks.md` will be the architecture test (NetArchTest.Rules) to enforce domain boundaries and namespace compliance
- This refactoring is triggered by ADR-009; all implementation follows ADR-first principle

**Principle IV: Behavioral & Observable Engineering** ⚠️ **CONDITIONAL PASS**
- This is a refactoring, not a new feature; observability requirements are deferred to subsequent feature work
- Existing observability instrumentation is preserved as-is
- New observability requirements will be specified in future features

**Overall Constitution Assessment**: ✅ **PASS** — This refactoring aligns with and fulfills Constitution principles I and III; Principle II (testing) is preserved; Principle IV is N/A for this refactor.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

This refactoring reorganizes the code structure but does not introduce new architectural boundaries. It is driven by and enforces the following existing ADRs plus one new ADR:

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend Framework — .NET 9 Minimal API with SignalR | Refactoring must preserve ASP.NET Core endpoint registration and SignalR hub definitions; no technology changes |
| ADR-002 | Agent Runtime Strategy — Worker Services | IAgentWorker interface contracts must remain unchanged; Agents domain will house agent-related request handlers |
| ADR-004 | Channel Abstraction — Unified Channel Interface | IChannel interface contracts must remain unchanged; Channels domain will house channel-related implementations |
| ADR-005 | Monorepo Structure and Spec-Kit Workflow Integration | This refactoring is within the `/src/backend` subproject; does not affect monorepo-level structure |
| ADR-006 | Hub-Spoke Orchestration Architecture | Refactoring makes the Hub-Spoke pattern visible at filesystem level: `/Hubs` domain acts as orchestrator, `/Agents` and `/Channels` as spokes |
| ADR-009 | Domain-Driven Code Organization — Screaming Architecture | **NEW** — This is the foundational ADR for this refactoring. Establishes domain-driven structure, namespace organization, and no-circular-dependency rule |

**New ADR Status**: ADR-009 is drafted at `/docs/adr/adr-009.md` and **must be accepted** before implementation begins.

**Architecture Test Requirement**: First task in `tasks.md` will implement NetArchTest.Rules enforcement of:
1. No circular dependencies between domains (Agents ↔ Hubs ↔ Channels)
2. Namespace compliance: All types in `Grimoire.Api.Agents.*`, `Grimoire.Api.Hubs.*`, `Grimoire.Api.Channels.*`, or `Grimoire.Api.Shared.*`
3. Interface-based cross-domain communication (domains communicate via IAgentWorker, IChannel, not direct class references)

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

**Note**: This is a refactoring, not a feature that introduces new runtime behavior. Observability requirements are focused on the refactoring process itself and architecture compliance verification.

### Business Metrics (None — this is a refactoring)

N/A — refactoring does not introduce new user-facing metrics

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `architecture_test_executed` | INFO | Architecture tests run during CI/CD | `test_count`, `failures`, `domain_violations` |

### Distributed Trace Spans (None — this is a refactoring)

N/A — no new runtime endpoints or services introduced

### Architectural Observability (Automated Enforcement)

The following must be enforced by CI/CD gates and fail the build if violated:

1. **NetArchTest.Rules Architecture Test**: Verifies no circular dependencies, namespace compliance, and interface-based communication
2. **Test Pass Rate**: All existing unit and integration tests must pass without modification
3. **Namespace Coverage**: Minimum 80% of types follow `Grimoire.Api.{Domain}.*` pattern; exceptions documented

## Project Structure

### Documentation (this feature)

```text
specs/003-domain-driven-refactor/
├── spec.md              # Feature specification
├── plan.md              # This file (implementation plan)
├── research.md          # Phase 0 output: current codebase structure analysis
├── data-model.md        # Phase 1 output: domain boundaries and code organization mapping
├── quickstart.md        # Phase 1 output: validation steps to verify refactoring success
├── contracts/           # Phase 1 output: cross-domain communication interfaces
├── checklists/
│   └── requirements.md  # Quality validation checklist
└── tasks.md             # Phase 2 output: actionable refactoring tasks
```

### Source Code — Current Structure (Before Refactoring)

```text
src/backend/Grimoire.Api/
├── Api/
│   ├── Endpoints/       (HTTP endpoint handlers)
│   ├── Handlers/        (Request handlers)
│   ├── Hubs/            (SignalR hub definitions)
│   └── Middleware/
├── Core/
│   ├── Domain/          (Business logic)
│   └── Exceptions/
└── Infrastructure/
    ├── Observability/
    └── Persistence/
```

### Source Code — Target Structure (After Refactoring)

```text
src/backend/Grimoire.Api/
├── Agents/
│   ├── Endpoints/       (Agent-related HTTP endpoints)
│   ├── Handlers/        (Agent request handlers)
│   ├── Services/        (Agent business services)
│   ├── Models/          (Agent domain models)
│   └── Tests/           (Agent-specific tests)
│
├── Hubs/
│   ├── Endpoints/       (SignalR hub endpoint registration)
│   ├── Handlers/        (Hub connection/message handlers)
│   ├── Services/        (Hub orchestration services)
│   ├── Models/          (Hub-specific models)
│   └── Tests/           (Hub-specific tests)
│
├── Channels/
│   ├── Endpoints/       (Channel-related HTTP endpoints)
│   ├── Handlers/        (Channel request handlers)
│   ├── Services/        (Channel abstraction services — IChannel implementations)
│   ├── Models/          (Channel models)
│   └── Tests/           (Channel-specific tests)
│
└── Shared/
    ├── Middleware/      (HTTP middleware — authentication, CORS, etc.)
    ├── Observability/   (OpenTelemetry instrumentation utilities)
    ├── Persistence/     (Data access utilities, SQLite interactions)
    ├── Exceptions/      (Application exception types)
    └── Models/          (Shared DTOs and common models)

src/backend/Grimoire.Api.Tests/
├── Unit/
│   ├── Agents/          (Agent domain logic unit tests)
│   ├── Hubs/            (Hub services unit tests)
│   └── Channels/        (Channel services unit tests)
│
├── Integration/
│   ├── Agents/          (Agent API integration tests)
│   ├── Hubs/            (Hub real-time integration tests)
│   └── Channels/        (Channel API integration tests)
│
├── Architecture/
│   └── ArchitectureTests.cs  (NetArchTest.Rules enforcement)

src/backend/Grimoire.Core/
├── Agents/              (Core agent abstractions — IAgentWorker, etc.)
└── Channels/            (Core channel abstraction — IChannel, etc.)
```

**Structure Decision**: The refactoring moves from layer-based (technical concerns) to domain-based (business concerns) organization. Each domain folder contains:
- **Endpoints**: HTTP route handlers and endpoint definitions
- **Handlers**: Request processing logic for that domain
- **Services**: Business logic and domain-specific orchestration
- **Models**: Domain-specific DTOs and value objects
- **Tests**: Isolated unit and integration tests for the domain

Shared infrastructure (middleware, observability, persistence, exceptions) is centralized to prevent duplication while remaining accessible to all domains.

**Namespace Organization**:
- `Grimoire.Api.Agents.*` — Agent domain
- `Grimoire.Api.Hubs.*` — Hub domain
- `Grimoire.Api.Channels.*` — Channel domain
- `Grimoire.Api.Shared.*` — Shared infrastructure
- `Grimoire.Api.Tests.Unit.{Domain}.*` — Unit tests per domain
- `Grimoire.Api.Tests.Integration.{Domain}.*` — Integration tests per domain
- `Grimoire.Api.Tests.Architecture.*` — Architecture enforcement tests

## Complexity Tracking

> No Constitution Check violations. Refactoring aligns with and fulfills Constitutional principles I and III.

| Item | Status | Notes |
|------|--------|-------|
| Principle I Compliance | ✅ Resolves | Reorganizing code by domain directly implements "Domain Architecture & Strategic DDD" |
| Principle III Compliance | ✅ Resolves | ADR-009 drafted before implementation; Architecture tests as first task |
| Test Preservation | ✅ Guaranteed | Zero modifications to test logic; all tests pass as-is |
| Breaking Changes | ✅ None | API contracts unchanged; external clients unaffected |


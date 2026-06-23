# Implementation Plan: Grimoire Project Skeleton Setup

**Branch**: `001-grimoire-scaffold` | **Date**: 2026-06-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-grimoire-scaffold/spec.md`

## Summary

Establish the foundational monorepo structure for Grimoire: a .NET 9 Minimal API backend with SignalR scaffolding, `IChannel` and `IAgentWorker` core interfaces in `Grimoire.Core`, a Svelte 5 + Vite TypeScript frontend, NetArchTest.Rules architecture tests enforcing all ADRs 001–007, and GitHub Actions CI with path-based triggers per subproject. No business logic is implemented — every artifact compiles cleanly and all architecture tests pass green from the first commit.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript / Node.js 20+ (frontend)

**Primary Dependencies**:
- Backend: `Microsoft.AspNetCore` (.NET 9 Minimal API), `Microsoft.AspNetCore.SignalR`, `xunit`, `NetArchTest.Rules` 3.x
- Frontend: Svelte 5, Vite 6, TypeScript 5, ESLint 9

**Storage**: N/A for skeleton (ADR-007: Git + Markdown storage configured outside this feature)

**Testing**: xUnit + NetArchTest.Rules (backend architecture enforcement); TypeScript compiler + ESLint (frontend)

**Target Platform**: Linux (CI/CD), cross-platform local development

**Project Type**: Monorepo scaffold — .NET backend web service + Svelte SPA

**Performance Goals**:
- Architecture tests execute in < 30 seconds (SC-004)
- CI backend workflow completes < 3 minutes (SC-005)
- CI frontend workflow completes < 2 minutes (SC-005)

**Constraints**:
- Zero errors and zero warnings on `dotnet build` (SC-001)
- Zero errors and zero warnings on `npm run build` (SC-002)
- `IChannel` and `IAgentWorker` max 3 method signatures each in skeleton (SC-007)
- C# 13 with nullable reference types enabled and `TreatWarningsAsErrors=true` throughout

**Scale/Scope**: Skeleton only — empty scaffolds, no feature implementation or business logic

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Domain Architecture & Strategic DDD | ✅ PASS | `IChannel` and `IAgentWorker` defined in `Grimoire.Core` (domain boundary). No tactical DDD in skeleton — Core contains interfaces only, zero Infrastructure references. |
| II. Pragmatic Testing Strategy | ✅ PASS | NetArchTest.Rules is the correct mechanism for a skeleton (no API boundaries yet, no Testcontainers needed). Architecture tests cover structural rules implicitly. |
| III. ADR-First & Test-Driven Architecture | ✅ PASS | All ADRs 001–007 read before filling this plan. `## Architectural Constraints & ADRs` section present. First task in `tasks.md` will be architecture enforcement tests (Red). No new structural boundary introduced — no new ADR required. |
| IV. Behavioral & Observable Engineering | ✅ PASS | `## Observability` section present. Skeleton emits host lifecycle log events. All rules have CI gates (GitHub Actions). |

**Post-design re-check**: ✅ No violations found after Phase 1 design.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend Framework — .NET 9 Minimal API with SignalR | Backend must target `net9.0`. Architecture test must assert the `Grimoire.Api` assembly references `Microsoft.AspNetCore` and targets .NET 9. SignalR scaffolded (empty hub class, not wired to any channel implementation). |
| ADR-002 | Agent Runtime — Worker Services + IAgentWorker | `IAgentWorker` interface MUST reside in `Grimoire.Core`. Architecture test must assert any class in the `Grimoire.Agents.*` namespace implements `IAgentWorker`. No agent implementations in skeleton — placeholder directory only. |
| ADR-003 | Frontend — Svelte 5 + Vite | Frontend must be a Svelte 5 + Vite project with TypeScript strict mode. No other UI framework may be introduced. Verified by presence of `svelte.config.js` and `"svelte": "^5"` in `package.json`. |
| ADR-004 | Channel Abstraction — IChannel | `IChannel` interface MUST reside in `Grimoire.Core.Channels`. Architecture test must assert any class in `Grimoire.Infrastructure.Channels.*` implements `IChannel`. No channel implementations in skeleton. |
| ADR-005 | Monorepo Structure | Directory layout must match exactly: `src/backend/`, `src/frontend/`, `src/agents/`, `docs/adr/`, `specs/`. Architecture test must assert `Grimoire.Core` has no references to `Grimoire.Api` or any Infrastructure assembly. |
| ADR-006 | Hub-Spoke Orchestration | `Grimoire.Api` is the Hub assembly. Architecture test must assert that agent and channel assemblies do not reference each other directly (all routing goes through the Hub/Orchestrator layer). |
| ADR-007 | Storage Strategy — Git + Markdown | `Grimoire.Core` MUST NOT reference any binary database package (EF Core, SQLite, LiteDB). Architecture test must assert Core assembly has no dependency on `Microsoft.EntityFrameworkCore` or similar. |

**New ADR required?**: No — the skeleton introduces no new structural boundary, integration pattern, or cross-cutting concern beyond ADRs 001–007.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

This feature is a project skeleton with no runtime business behavior. Observability is scoped to host lifecycle events. OpenTelemetry SDK is scaffolded in `Grimoire.Api` for future instrumentation.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| N/A | — | No domain operations in skeleton; metrics added with first feature | — |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `grimoire.host.started` | INFO | ASP.NET host starts and is ready to serve | `environment`, `version` |
| `grimoire.host.stopped` | INFO | ASP.NET host shuts down gracefully | `environment`, `uptime_seconds` |

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| N/A | — | No request-handling paths in skeleton; spans added with first channel feature | — |

> OpenTelemetry SDK (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`) is added to `Grimoire.Api` in scaffold state. The two host lifecycle log events above MUST be emitted by `Program.cs` via `ILogger<Program>`.

## Project Structure

### Documentation (this feature)

```text
specs/001-grimoire-scaffold/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── IChannel.cs      # IChannel interface contract
│   └── IAgentWorker.cs  # IAgentWorker interface contract
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── backend/
│   ├── Grimoire.sln
│   ├── Directory.Build.props           # Nullable=enable, LangVersion=13, TreatWarningsAsErrors=true
│   ├── Grimoire.Api/
│   │   ├── Program.cs                  # Minimal API bootstrap + structured startup logging
│   │   └── Grimoire.Api.csproj
│   ├── Grimoire.Core/
│   │   ├── Channels/
│   │   │   └── IChannel.cs             # Channel abstraction (ADR-004)
│   │   ├── Agents/
│   │   │   └── IAgentWorker.cs         # Agent worker abstraction (ADR-002)
│   │   └── Grimoire.Core.csproj        # No external dependencies
│   └── Grimoire.ArchTests/
│       ├── ArchitectureTests.cs        # NetArchTest.Rules: ADRs 001–007 enforcement
│       └── Grimoire.ArchTests.csproj
├── frontend/
│   ├── src/
│   │   ├── App.svelte
│   │   └── main.ts
│   ├── package.json                    # scripts: build, dev, lint
│   ├── vite.config.ts
│   ├── tsconfig.json
│   └── svelte.config.js
└── agents/
    └── .gitkeep                        # Placeholder for future agent implementations

.github/
└── workflows/
    ├── backend.yml                     # paths: src/backend/** → dotnet build + arch tests
    └── frontend.yml                    # paths: src/frontend/** → npm build + lint

README.md                               # Monorepo structure, build instructions, ADR links
```

**Structure Decision**: Flat monorepo matching ADR-005 exactly. Three .NET projects: `Grimoire.Api` (host), `Grimoire.Core` (domain, zero external deps), `Grimoire.ArchTests` (structural enforcement). Frontend is a self-contained Svelte 5 project under `src/frontend/`. GitHub Actions workflows are path-filtered per subproject.

## Complexity Tracking

> No constitution violations requiring justification.

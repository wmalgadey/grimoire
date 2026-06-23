# Research: Current Codebase Structure & Refactoring Analysis

**Phase**: 0 (Research)  
**Date**: 2026-06-23

## Objective

Analyze the current `.NET Grimoire.Api` layer-based structure and plan the migration path to domain-driven organization. Identify all files that need reorganization, namespace updates, and cross-domain dependencies.

## Current Structure Analysis

### Existing Directory Layout

```
src/backend/Grimoire.Api/
├── Api/
│   ├── Endpoints/        (HTTP route definitions)
│   ├── Handlers/         (Request processing)
│   ├── Hubs/             (SignalR hub definitions)
│   └── Middleware/       (HTTP middleware)
│
├── Core/
│   ├── Domain/           (Business logic, aggregates, services)
│   └── Exceptions/       (Application exceptions)
│
└── Infrastructure/
    ├── Observability/    (OpenTelemetry, logging)
    └── Persistence/      (Data access, SQLite interactions)
```

### Files to Reorganize (By Domain)

#### Agent-related files → `/Agents` folder

**Current locations** (layer-based):
- `Api/Endpoints/*Agent*.cs` → `Agents/Endpoints/`
- `Api/Handlers/*Agent*.cs` → `Agents/Handlers/`
- `Core/Domain/*Agent*.cs` → `Agents/Services/` or `Agents/Models/`
- Related test files in `Grimoire.Api.Tests/`

**Action**: Move and update namespaces to `Grimoire.Api.Agents.*`

#### Hub-related files → `/Hubs` folder

**Current locations** (layer-based):
- `Api/Hubs/*.cs` → `Hubs/Endpoints/`
- `Api/Handlers/*Hub*.cs` → `Hubs/Handlers/`
- `Core/Domain/*Hub*.cs` → `Hubs/Services/` or `Hubs/Models/`

**Action**: Move and update namespaces to `Grimoire.Api.Hubs.*`

#### Channel-related files → `/Channels` folder

**Current locations** (layer-based):
- `Api/Endpoints/*Channel*.cs` → `Channels/Endpoints/`
- `Api/Handlers/*Channel*.cs` → `Channels/Handlers/`
- `Core/Domain/*Channel*.cs` → `Channels/Services/` or `Channels/Models/`
- Implementations of `IChannel` interface

**Action**: Move and update namespaces to `Grimoire.Api.Channels.*`

#### Shared infrastructure → `/Shared` folder

**Current locations** (layer-based):
- `Api/Middleware/*` → `Shared/Middleware/`
- `Infrastructure/Observability/*` → `Shared/Observability/`
- `Infrastructure/Persistence/*` → `Shared/Persistence/`
- `Core/Exceptions/*` → `Shared/Exceptions/`
- Common DTOs and models → `Shared/Models/`

**Action**: Move and update namespaces to `Grimoire.Api.Shared.*`

## Test Structure Migration

### Current Test Organization

```
Grimoire.Api.Tests/
├── Unit/
├── Integration/
└── Stubs/
```

### Target Test Organization

```
Grimoire.Api.Tests/
├── Unit/
│   ├── Agents/
│   ├── Hubs/
│   └── Channels/
├── Integration/
│   ├── Agents/
│   ├── Hubs/
│   └── Channels/
└── Architecture/
    └── ArchitectureTests.cs
```

**Action**: Reorganize test files to mirror domain structure; update namespaces accordingly.

## Namespace Update Strategy

### Pattern: `Grimoire.Api.{Domain}.{Component}`

**Examples**:
- `Grimoire.Api.Agents.Endpoints.AgentCreateEndpoint`
- `Grimoire.Api.Agents.Handlers.AgentLifecycleHandler`
- `Grimoire.Api.Agents.Services.AgentOrchestrationService`
- `Grimoire.Api.Hubs.Endpoints.GrimoireHub`
- `Grimoire.Api.Channels.Services.ChannelDispatcher`
- `Grimoire.Api.Shared.Middleware.AuthenticationMiddleware`
- `Grimoire.Api.Shared.Observability.MetricsCollector`

**Action**: Use global find-and-replace with verification to update all namespace declarations.

## Cross-Domain Dependencies

### Interface-Based Communication (Preserved)

- **IAgentWorker** (from `Grimoire.Core.Agents`): Agents domain implements; Hubs domain references
- **IChannel** (from `Grimoire.Core.Channels`): Channels domain implements; Hubs and Agents domains reference

**Action**: These interface contracts remain unchanged; only consuming code is reorganized.

### Identified Coupling Points

1. **Hubs ↔ Agents**: Hub dispatches work to agents via `IAgentWorker` interface
2. **Hubs ↔ Channels**: Hub broadcasts results to channels via `IChannel` interface
3. **All domains → Shared**: All domains consume middleware, observability, persistence utilities

**Action**: Verify no direct class references; all cross-domain communication must use interfaces.

## Architecture Test Requirements

### NetArchTest.Rules Validations

1. **No Circular Dependencies**: `Agents` ↔ `Hubs` ↔ `Channels` must not have circular references
2. **Namespace Compliance**: All types must be in `Grimoire.Api.{Domain}.*` or `Grimoire.Api.Shared.*`
3. **Interface-Based Communication**: Cross-domain references must be to interfaces only, not concrete classes
4. **Shared Layer Dependency**: Shared infrastructure can be referenced by all domains; no domain references other domains except via interfaces

## Validation Checklist

- [ ] All namespace declarations updated
- [ ] All `using` statements updated to reflect new namespaces
- [ ] All project file references (`.csproj`) updated if necessary
- [ ] All test files reorganized and namespace-updated
- [ ] Architecture test created and passing
- [ ] All unit tests passing (no test logic changes required)
- [ ] All integration tests passing
- [ ] No circular dependencies detected
- [ ] API endpoints and contracts unchanged (zero breaking changes)

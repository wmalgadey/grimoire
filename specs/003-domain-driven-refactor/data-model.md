# Domain Model: Code Organization & Architectural Boundaries

**Phase**: 1 (Design)  
**Date**: 2026-06-23

## Bounded Contexts (Business Domains)

### 1. Agents Domain

**Ubiquitous Language**: Agent lifecycle, agent capabilities, work assignment, agent execution

**Responsibility**: Manage agent worker lifecycle, handle agent-specific requests, coordinate with agent runtime

**Contains**:
- Agent endpoint definitions (HTTP routes for agent operations)
- Agent request handlers (request-to-domain translation)
- Agent services (orchestration, lifecycle management)
- Agent models (DTOs, domain entities specific to agents)
- Agent-specific tests

**External Dependencies**:
- `IAgentWorker` (from `Grimoire.Core.Agents`) — implemented by agent runtime
- `IChannel` (from `Grimoire.Core.Channels`) — to broadcast agent results

**Interfaces Exposed**:
- None — consumes existing core interfaces only

**Namespace Structure**:
```
Grimoire.Api.Agents
├── Endpoints
├── Handlers
├── Services
├── Models
└── Tests (mirrors above)
```

---

### 2. Hubs Domain

**Ubiquitous Language**: Real-time orchestration, message routing, connection management, broadcast

**Responsibility**: Central orchestration hub; routes incoming requests to appropriate agents; manages SignalR real-time communication

**Contains**:
- SignalR hub definitions (connection, message handlers)
- Hub endpoint definitions (HTTP routes for hub-specific operations)
- Hub request handlers
- Hub orchestration services (routing logic, state management)
- Hub models (connection state, message types)
- Hub-specific tests

**External Dependencies**:
- `IAgentWorker` (from `Grimoire.Core.Agents`) — to dispatch work
- `IChannel` (from `Grimoire.Core.Channels`) — to broadcast results
- `Shared` infrastructure (middleware, observability)

**Interfaces Exposed**:
- None — implements hub-spoke pattern as orchestrator

**Namespace Structure**:
```
Grimoire.Api.Hubs
├── Endpoints
├── Handlers
├── Services
├── Models
└── Tests (mirrors above)
```

---

### 3. Channels Domain

**Ubiquitous Language**: Channel abstraction, multi-channel dispatch, input/output channels

**Responsibility**: Implement `IChannel` interface; provide multi-channel support (Web UI, Telegram, future channels)

**Contains**:
- Channel endpoint definitions
- Channel request handlers
- Channel implementation services (`IChannel` implementations)
- Channel models (channel state, message types)
- Channel-specific tests

**External Dependencies**:
- `IChannel` (from `Grimoire.Core.Channels`) — interface to implement

**Interfaces Exposed**:
- Implements `IChannel` interface (contract from `Grimoire.Core`)

**Namespace Structure**:
```
Grimoire.Api.Channels
├── Endpoints
├── Handlers
├── Services
├── Models
└── Tests (mirrors above)
```

---

### 4. Shared Infrastructure

**Ubiquitous Language**: Cross-cutting concerns, infrastructure utilities

**Responsibility**: Provide common infrastructure to all domains; prevent code duplication

**Contains**:
- Middleware (authentication, CORS, error handling)
- Observability utilities (OpenTelemetry, logging)
- Persistence utilities (SQLite data access, repository patterns)
- Exception definitions (application exception types)
- Common models (shared DTOs)

**External Dependencies**:
- ASP.NET Core framework, OpenTelemetry, SQLite driver

**Interfaces Exposed**:
- Shared utilities (not interfaces; plain classes/helpers)

**Namespace Structure**:
```
Grimoire.Api.Shared
├── Middleware
├── Observability
├── Persistence
├── Exceptions
├── Models
└── (no Tests — tested implicitly by domain integration tests)
```

---

## Cross-Domain Communication

### Interface-Based Contracts (Preserved)

```
Hubs Domain (Orchestrator)
  ↓
  ├─ Calls IAgentWorker (from Grimoire.Core.Agents)
  │   → Agents Domain consumes
  │
  └─ Calls IChannel (from Grimoire.Core.Channels)
      → Channels Domain implements
```

**Key Rule**: Domains communicate **only** via well-defined interfaces, never direct class references.

### No Circular Dependencies

- **Agents** → (no internal dependencies on other domains)
- **Hubs** → Agents (via `IAgentWorker`), Channels (via `IChannel`)
- **Channels** → (no internal dependencies on other domains)
- **All domains** → Shared (unidirectional; Shared depends on nothing)

---

## File Organization by Domain

### Agents Domain Directory Structure

```
src/backend/Grimoire.Api/Agents/
├── Endpoints/
│   ├── AgentStatusEndpoint.cs
│   ├── AgentCommandEndpoint.cs
│   └── ...
├── Handlers/
│   ├── AgentLifecycleHandler.cs
│   ├── AgentCommandHandler.cs
│   └── ...
├── Services/
│   ├── AgentOrchestrationService.cs
│   ├── AgentLifecycleManager.cs
│   └── ...
├── Models/
│   ├── AgentStatusDto.cs
│   ├── AgentCommandRequest.cs
│   └── ...
└── Tests/
    ├── Unit/
    │   ├── AgentLifecycleManagerTests.cs
    │   └── ...
    └── Integration/
        ├── AgentEndpointTests.cs
        └── ...
```

### Hubs Domain Directory Structure

```
src/backend/Grimoire.Api/Hubs/
├── Endpoints/
│   ├── GrimoireHubEndpoint.cs
│   └── ...
├── Handlers/
│   ├── HubConnectionHandler.cs
│   ├── HubMessageHandler.cs
│   └── ...
├── Services/
│   ├── HubOrchestrationService.cs
│   ├── RequestRoutingService.cs
│   └── ...
├── Models/
│   ├── HubConnectionState.cs
│   ├── HubMessage.cs
│   └── ...
└── Tests/
    ├── Unit/
    │   └── ...
    └── Integration/
        ├── HubConnectionTests.cs
        └── ...
```

### Channels Domain Directory Structure

```
src/backend/Grimoire.Api/Channels/
├── Endpoints/
│   ├── ChannelStatusEndpoint.cs
│   └── ...
├── Handlers/
│   ├── ChannelRequestHandler.cs
│   └── ...
├── Services/
│   ├── WebUiChannelImpl.cs     (IChannel implementation)
│   ├── TelegramChannelImpl.cs  (IChannel implementation)
│   └── ...
├── Models/
│   ├── ChannelMessage.cs
│   └── ...
└── Tests/
    ├── Unit/
    │   └── ...
    └── Integration/
        ├── WebUiChannelTests.cs
        └── ...
```

### Shared Infrastructure Directory Structure

```
src/backend/Grimoire.Api/Shared/
├── Middleware/
│   ├── AuthenticationMiddleware.cs
│   ├── ErrorHandlingMiddleware.cs
│   ├── CorsMiddleware.cs
│   └── ...
├── Observability/
│   ├── MetricsCollector.cs
│   ├── LoggingExtensions.cs
│   └── ...
├── Persistence/
│   ├── SqliteRepository.cs
│   ├── UnitOfWork.cs
│   └── ...
├── Exceptions/
│   ├── DomainException.cs
│   ├── NotFoundException.cs
│   └── ...
└── Models/
    ├── CommonDto.cs
    ├── PaginationModel.cs
    └── ...
```

---

## Namespace Convention

All C# namespaces must follow this pattern:

```
Grimoire.Api.{Domain}.{Component}
```

**Examples**:
- `Grimoire.Api.Agents.Endpoints`
- `Grimoire.Api.Agents.Handlers`
- `Grimoire.Api.Agents.Services`
- `Grimoire.Api.Agents.Models`
- `Grimoire.Api.Hubs.Endpoints`
- `Grimoire.Api.Hubs.Services`
- `Grimoire.Api.Channels.Endpoints`
- `Grimoire.Api.Shared.Middleware`
- `Grimoire.Api.Shared.Observability`
- `Grimoire.Api.Shared.Persistence`

---

## Design Validation

### Bounded Context Integrity

- ✅ Each domain has clear, non-overlapping responsibility
- ✅ Cross-domain communication via explicit interfaces only
- ✅ No circular dependencies
- ✅ Shared infrastructure isolated and unidirectional

### Architecture Patterns Aligned

- ✅ **ADR-006 (Hub-Spoke)**: Hubs domain acts as orchestrator; Agents and Channels are spokes
- ✅ **ADR-002 (Worker Services)**: Agents domain interfaces with `IAgentWorker`
- ✅ **ADR-004 (Channel Abstraction)**: Channels domain implements `IChannel`
- ✅ **Constitution I (Domain Architecture)**: Code organization reflects business domains

---

## Implementation Order

1. **Define architecture test** (validates boundaries)
2. **Create folder structure** (empty subdirectories)
3. **Move files by domain** (batch reorganization)
4. **Update namespaces** (systematic find-and-replace)
5. **Update test structure** (mirror domain organization)
6. **Run tests** (verify zero breaking changes)
7. **Validate architecture test** (ensure no violations)

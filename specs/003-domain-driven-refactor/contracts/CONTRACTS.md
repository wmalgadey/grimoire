# Cross-Domain Communication Contracts

This directory documents the interfaces and contracts used for communication between domains.

## Overview

All cross-domain communication uses well-defined interfaces to maintain loose coupling and enable independent testing.

## Core Interfaces (from Grimoire.Core)

### IAgentWorker

**Defined in**: `Grimoire.Core.Agents`

**Used by**: Hubs domain (to dispatch work)

**Implemented by**: Agents domain

```csharp
namespace Grimoire.Core.Agents
{
    public interface IAgentWorker
    {
        Task<AgentResult> ExecuteAsync(AgentRequest request);
        Task<AgentStatus> GetStatusAsync();
    }
}
```

**Contract**: Hubs domain calls `IAgentWorker.ExecuteAsync()` to dispatch work to agents. Agents domain provides implementation.

---

### IChannel

**Defined in**: `Grimoire.Core.Channels`

**Used by**: Hubs domain (to broadcast results), Agents domain (to report results)

**Implemented by**: Channels domain

```csharp
namespace Grimoire.Core.Channels
{
    public interface IChannel
    {
        Task SendAsync(ChannelMessage message);
        Task<ChannelStatus> GetStatusAsync();
    }
}
```

**Contract**: Hubs and Agents domains call `IChannel.SendAsync()` to broadcast results to channels. Channels domain provides implementation(s) for Web UI, Telegram, etc.

---

## Domain-Specific Interfaces

### Agents Domain

**Internal interfaces** (not exposed outside domain; not documented here):
- `IAgentLifecycleManager` — manages agent startup/shutdown
- `IAgentRegistry` — tracks registered agents

**External interface**:
- Implements `IAgentWorker` (from `Grimoire.Core`)

---

### Hubs Domain

**Internal interfaces** (not exposed outside domain):
- `IHubOrchestrator` — routes requests to agents
- `IRequestRouter` — determines which agent should handle a request

**External interfaces**:
- Implements SignalR hub base class (standard ASP.NET Core interface)
- Consumes `IAgentWorker` (from `Grimoire.Core`)
- Consumes `IChannel` (from `Grimoire.Core`)

---

### Channels Domain

**Internal interfaces** (not exposed outside domain):
- `IChannelAdapter` — adapts different channel protocols

**External interface**:
- Implements `IChannel` (from `Grimoire.Core`)

---

## Communication Patterns

### Pattern 1: Hubs → Agents

```csharp
// In Hubs/Services/HubOrchestrationService.cs
public class HubOrchestrationService
{
    private readonly IAgentWorker _agentWorker; // Dependency injected
    
    public async Task RouteRequestAsync(HubRequest request)
    {
        var agentRequest = MapToAgentRequest(request);
        var result = await _agentWorker.ExecuteAsync(agentRequest); // Interface call
        // ...
    }
}
```

**Key**: Hubs domain references `IAgentWorker` interface, never concrete Agents classes.

---

### Pattern 2: Hubs/Agents → Channels

```csharp
// In Hubs/Services/HubOrchestrationService.cs
public class HubOrchestrationService
{
    private readonly IChannel _channel; // Dependency injected
    
    public async Task BroadcastResultAsync(AgentResult result)
    {
        var message = MapToChannelMessage(result);
        await _channel.SendAsync(message); // Interface call
    }
}
```

**Key**: Hubs and Agents domains reference `IChannel` interface, never concrete Channels classes.

---

## Design Validation

- ✅ No circular dependencies: Hubs depends on Agents and Channels, but Agents and Channels don't depend on Hubs
- ✅ Interface-based: All cross-domain communication through well-defined interfaces
- ✅ Testability: Each domain can be tested in isolation by mocking its dependencies
- ✅ Extensibility: New channel types can be added (implement `IChannel`) without modifying Hubs or Agents

---

## Changes During Refactoring

**No changes** to these interface contracts. The refactoring is purely organizational; interface definitions and implementations remain functionally identical.

**After refactoring**:
- Interface definitions stay in `Grimoire.Core.*`
- Interface implementations move to domain folders:
  - `IAgentWorker` implementations: `Grimoire.Api.Agents.Services.*`
  - `IChannel` implementations: `Grimoire.Api.Channels.Services.*`

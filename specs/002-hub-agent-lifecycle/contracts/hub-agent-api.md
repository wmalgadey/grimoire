# API Contracts: Hub Agent Registry

**Phase**: Phase 1 (Design)
**Date**: 2026-06-23
**Spec**: [../spec.md](../spec.md)
**Data Model**: [../data-model.md](../data-model.md)

## Contract: Register Agent

**Endpoint**: `POST /api/agents`

**Request** (JSON):
```json
{
  "agentId": "ingest-v1",
  "name": "Wiki Ingest Agent",
  "capabilities": ["parse-pdf", "extract-markdown"]
}
```

**Response 201 Created** (JSON):
```json
{
  "agentId": "ingest-v1",
  "name": "Wiki Ingest Agent",
  "status": "Unregistered",
  "capabilities": ["parse-pdf", "extract-markdown"],
  "registeredAt": "2026-06-23T14:30:00Z",
  "lastHealthCheckAt": null
}
```

**Response 409 Conflict** (duplicate AgentId):
```json
{
  "error": "AgentAlreadyRegisteredException",
  "message": "Agent with ID 'ingest-v1' is already registered",
  "agentId": "ingest-v1"
}
```

---

## Contract: Start Agent

**Endpoint**: `POST /api/agents/{agentId}/start`

**Request**: (empty body)

**Response 200 OK** (JSON):
```json
{
  "agentId": "ingest-v1",
  "status": "Running",
  "transitionedAt": "2026-06-23T14:31:00Z"
}
```

**Response 400 Bad Request** (invalid state transition):
```json
{
  "error": "InvalidStateTransition",
  "message": "Cannot start agent 'ingest-v1' from status 'Running'",
  "currentStatus": "Running"
}
```

**Response 404 Not Found** (agent not registered):
```json
{
  "error": "AgentNotFoundException",
  "message": "Agent with ID 'unknown-agent' not found",
  "agentId": "unknown-agent"
}
```

---

## Contract: Stop Agent

**Endpoint**: `POST /api/agents/{agentId}/stop`

**Request**: (empty body)

**Response 200 OK** (JSON):
```json
{
  "agentId": "ingest-v1",
  "status": "Stopped",
  "transitionedAt": "2026-06-23T14:35:00Z"
}
```

**Response 400 Bad Request** (invalid state transition):
```json
{
  "error": "InvalidStateTransition",
  "message": "Cannot stop agent 'ingest-v1' from status 'Stopped'",
  "currentStatus": "Stopped"
}
```

**Response 404 Not Found**:
```json
{
  "error": "AgentNotFoundException",
  "message": "Agent with ID 'unknown-agent' not found",
  "agentId": "unknown-agent"
}
```

---

## Contract: Health Check

**Endpoint**: `GET /health`

**Request**: (no body)

**Response 200 OK** (all agents healthy):
```json
{
  "overall": "Healthy",
  "timestamp": "2026-06-23T14:40:00Z",
  "agents": [
    {
      "agentId": "ingest-v1",
      "name": "Wiki Ingest Agent",
      "status": "Running",
      "lastHealthCheckAt": "2026-06-23T14:39:55Z"
    },
    {
      "agentId": "query-v1",
      "name": "Query Agent",
      "status": "Running",
      "lastHealthCheckAt": "2026-06-23T14:39:50Z"
    }
  ]
}
```

**Response 503 Service Unavailable** (at least one agent faulted):
```json
{
  "overall": "Degraded",
  "timestamp": "2026-06-23T14:40:00Z",
  "agents": [
    {
      "agentId": "ingest-v1",
      "name": "Wiki Ingest Agent",
      "status": "Running",
      "lastHealthCheckAt": "2026-06-23T14:39:55Z"
    },
    {
      "agentId": "lint-v1",
      "name": "Lint Agent",
      "status": "Faulted",
      "lastHealthCheckAt": "2026-06-23T14:37:00Z",
      "faultReason": "Health check timeout"
    }
  ]
}
```

---

## Contract: Get Agent Status

**Endpoint**: `GET /api/agents/{agentId}`

**Request**: (no body)

**Response 200 OK** (JSON):
```json
{
  "agentId": "ingest-v1",
  "name": "Wiki Ingest Agent",
  "status": "Running",
  "capabilities": ["parse-pdf", "extract-markdown"],
  "registeredAt": "2026-06-23T14:30:00Z",
  "lastHealthCheckAt": "2026-06-23T14:39:55Z"
}
```

**Response 404 Not Found**:
```json
{
  "error": "AgentNotFoundException",
  "message": "Agent with ID 'unknown-agent' not found",
  "agentId": "unknown-agent"
}
```

---

## Contract: List All Agents

**Endpoint**: `GET /api/agents`

**Request**: (no body)

**Response 200 OK** (JSON):
```json
{
  "agents": [
    {
      "agentId": "ingest-v1",
      "name": "Wiki Ingest Agent",
      "status": "Running",
      "capabilities": ["parse-pdf", "extract-markdown"],
      "registeredAt": "2026-06-23T14:30:00Z",
      "lastHealthCheckAt": "2026-06-23T14:39:55Z"
    },
    {
      "agentId": "query-v1",
      "name": "Query Agent",
      "status": "Running",
      "capabilities": ["semantic-search"],
      "registeredAt": "2026-06-23T14:31:00Z",
      "lastHealthCheckAt": "2026-06-23T14:39:50Z"
    }
  ],
  "total": 2
}
```

---

## Contract: Domain Events (SignalR Broadcast)

Hub broadcasts agent lifecycle events to connected Web UI clients via SignalR.

**Signal**: `AgentStatusChanged`

**Payload** (JSON):
```json
{
  "agentId": "ingest-v1",
  "previousStatus": "Starting",
  "currentStatus": "Running",
  "transitionedAt": "2026-06-23T14:31:00Z",
  "reason": "Agent startup completed"
}
```

**Broadcast Trigger**:
- After successful state transition (start, stop, fault detection)
- All connected Web UI clients receive immediately (real-time status updates)

---

## Error Contracts

### DomainException (400 Bad Request)

Base exception for domain rule violations.

```json
{
  "error": "DomainException",
  "message": "[domain-specific error message]",
  "statusCode": 400
}
```

### AgentNotFoundException (404 Not Found)

Agent is not registered.

```json
{
  "error": "AgentNotFoundException",
  "message": "Agent with ID '{agentId}' not found",
  "agentId": "{agentId}",
  "statusCode": 404
}
```

### AgentAlreadyRegisteredException (409 Conflict)

Duplicate agent ID during registration.

```json
{
  "error": "AgentAlreadyRegisteredException",
  "message": "Agent with ID '{agentId}' is already registered",
  "agentId": "{agentId}",
  "statusCode": 409
}
```

### InvalidStateTransition (400 Bad Request)

Lifecycle transition is invalid.

```json
{
  "error": "InvalidStateTransition",
  "message": "Cannot {action} agent '{agentId}' from status '{currentStatus}'",
  "currentStatus": "{currentStatus}",
  "requestedTransition": "{action}",
  "statusCode": 400
}
```

---

## IAgentWorker Interface Contract

C# interface that all agent implementations must implement.

```csharp
/// <summary>
/// Abstraction for an agent worker managed by the Hub.
/// </summary>
public interface IAgentWorker
{
    /// <summary>
    /// Unique identifier for this agent (e.g., "ingest-v1").
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Metadata describing this agent (name, capabilities, etc.).
    /// </summary>
    AgentDescriptor Descriptor { get; }

    /// <summary>
    /// Check agent health. Returning IsHealthy=false triggers fault transition.
    /// </summary>
    Task<AgentHealthStatus> GetHealthAsync(CancellationToken ct);

    /// <summary>
    /// Start the agent. Transitions from Unregistered → Starting → Running.
    /// Throws if already started.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stop the agent. Transitions from Running → Stopping → Stopped.
    /// Throws if already stopped.
    /// </summary>
    Task StopAsync(CancellationToken ct);
}
```

---

## Database Schema Contract

SQLite tables auto-created by Hub on startup.

```sql
CREATE TABLE AgentDescriptors (
    AgentId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Status INTEGER NOT NULL,
    Capabilities TEXT, -- JSON array or null
    RegisteredAt TEXT NOT NULL, -- ISO 8601 UTC
    LastHealthCheckAt TEXT -- ISO 8601 UTC or null
);

CREATE TABLE AgentJobs (
    JobId TEXT PRIMARY KEY,
    AgentId TEXT NOT NULL,
    Payload TEXT NOT NULL, -- JSON
    Status INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL, -- ISO 8601 UTC
    StartedAt TEXT, -- ISO 8601 UTC or null
    CompletedAt TEXT, -- ISO 8601 UTC or null
    FailedAt TEXT, -- ISO 8601 UTC or null
    ErrorMessage TEXT, -- or null

    FOREIGN KEY (AgentId) REFERENCES AgentDescriptors(AgentId)
);

CREATE INDEX idx_AgentJobs_AgentId ON AgentJobs(AgentId);
CREATE INDEX idx_AgentJobs_Status ON AgentJobs(Status);
CREATE INDEX idx_AgentJobs_CreatedAt ON AgentJobs(CreatedAt DESC);
```

---

## Contract: Backward Compatibility

The Hub API commits to:
- ✅ **Additive changes only** (new fields in responses, new optional endpoints)
- ✅ **No breaking changes** to existing endpoint paths or response structure
- ✅ **New error types** introduced as additional error codes (existing codes unchanged)

Future versions will use versioning (e.g., `/api/v2/agents`) if structural changes are required.

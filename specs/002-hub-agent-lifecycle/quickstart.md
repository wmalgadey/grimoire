# Quickstart: Hub Foundation + Agent Lifecycle Validation

**Phase**: Phase 1 (Design)
**Date**: 2026-06-23
**Spec**: [../spec.md](../spec.md)
**Data Model**: [../data-model.md](../data-model.md)
**API Contracts**: [contracts/hub-agent-api.md](contracts/hub-agent-api.md)

## Overview

This guide demonstrates end-to-end validation of the Hub Foundation feature. It covers agent registration, lifecycle transitions, health monitoring, and state persistence.

## Prerequisites

- .NET 9 SDK installed
- SQLite CLI (optional, for inspecting database)
- cURL or Postman (for HTTP testing)
- The Hub backend running locally

## Setup

### 1. Start the Hub

```bash
cd src/Grimoire.Api
dotnet run --configuration Debug
```

**Expected output**:
```
info: Grimoire.Api.Program[0]
      Hub starting up...
info: Grimoire.Api.Infrastructure.Persistence.SqliteInitializer[0]
      SQLite database initialized: ./grimoire.db
info: Grimoire.Api[0]
      Hub ready on http://localhost:5000
```

### 2. Verify Hub is Healthy

```bash
curl -X GET http://localhost:5000/health
```

**Expected response** (HTTP 200):
```json
{
  "overall": "Healthy",
  "timestamp": "2026-06-23T14:30:00Z",
  "agents": []
}
```

---

## Scenario 1: Agent Registration & Discovery

**Goal**: Register agents and verify they appear in the registry.

### Step 1.1: Register Ingest Agent

```bash
curl -X POST http://localhost:5000/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "agentId": "ingest-v1",
    "name": "Wiki Ingest Agent",
    "capabilities": ["parse-pdf", "extract-markdown"]
  }'
```

**Expected response** (HTTP 201):
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

### Step 1.2: Register Query Agent

```bash
curl -X POST http://localhost:5000/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "agentId": "query-v1",
    "name": "Query Agent",
    "capabilities": ["semantic-search"]
  }'
```

### Step 1.3: List All Agents

```bash
curl -X GET http://localhost:5000/api/agents
```

**Expected response** (HTTP 200):
```json
{
  "agents": [
    {
      "agentId": "ingest-v1",
      "name": "Wiki Ingest Agent",
      "status": "Unregistered",
      "capabilities": ["parse-pdf", "extract-markdown"],
      "registeredAt": "2026-06-23T14:30:00Z",
      "lastHealthCheckAt": null
    },
    {
      "agentId": "query-v1",
      "name": "Query Agent",
      "status": "Unregistered",
      "capabilities": ["semantic-search"],
      "registeredAt": "2026-06-23T14:30:01Z",
      "lastHealthCheckAt": null
    }
  ],
  "total": 2
}
```

### Step 1.4: Attempt Duplicate Registration (Error Path)

```bash
curl -X POST http://localhost:5000/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "agentId": "ingest-v1",
    "name": "Duplicate",
    "capabilities": []
  }'
```

**Expected response** (HTTP 409):
```json
{
  "error": "AgentAlreadyRegisteredException",
  "message": "Agent with ID 'ingest-v1' is already registered",
  "agentId": "ingest-v1"
}
```

**Verification**:
- ✓ Agents registered successfully
- ✓ All agents discoverable by ID
- ✓ Duplicate ID rejected with appropriate error

---

## Scenario 2: Agent Lifecycle Management

**Goal**: Verify agent state transitions (Unregistered → Starting → Running → Stopping → Stopped).

### Step 2.1: Start Ingest Agent

```bash
curl -X POST http://localhost:5000/api/agents/ingest-v1/start \
  -H "Content-Type: application/json"
```

**Expected response** (HTTP 200):
```json
{
  "agentId": "ingest-v1",
  "status": "Running",
  "transitionedAt": "2026-06-23T14:31:00Z"
}
```

**Verify in logs**: Look for structured log entry:
```
[INFO] agent_lifecycle_transition: agent_id="ingest-v1" from_status="Unregistered" to_status="Starting" timestamp="2026-06-23T14:31:00Z"
[INFO] agent_lifecycle_transition: agent_id="ingest-v1" from_status="Starting" to_status="Running" timestamp="2026-06-23T14:31:00Z"
```

### Step 2.2: Verify Agent Status

```bash
curl -X GET http://localhost:5000/api/agents/ingest-v1
```

**Expected response** (HTTP 200):
```json
{
  "agentId": "ingest-v1",
  "name": "Wiki Ingest Agent",
  "status": "Running",
  "capabilities": ["parse-pdf", "extract-markdown"],
  "registeredAt": "2026-06-23T14:30:00Z",
  "lastHealthCheckAt": "2026-06-23T14:31:00Z"
}
```

### Step 2.3: Start Query Agent

```bash
curl -X POST http://localhost:5000/api/agents/query-v1/start \
  -H "Content-Type: application/json"
```

### Step 2.4: Stop Ingest Agent

```bash
curl -X POST http://localhost:5000/api/agents/ingest-v1/stop \
  -H "Content-Type: application/json"
```

**Expected response** (HTTP 200):
```json
{
  "agentId": "ingest-v1",
  "status": "Stopped",
  "transitionedAt": "2026-06-23T14:31:30Z"
}
```

### Step 2.5: Attempt Invalid Transition (Error Path)

```bash
curl -X POST http://localhost:5000/api/agents/ingest-v1/start \
  -H "Content-Type: application/json"
```

**Expected response** (HTTP 400):
```json
{
  "error": "InvalidStateTransition",
  "message": "Cannot start agent 'ingest-v1' from status 'Stopped'",
  "currentStatus": "Stopped"
}
```

**Verification**:
- ✓ Agents transition through valid states
- ✓ All transitions logged as structured events
- ✓ Invalid transitions rejected with appropriate error

---

## Scenario 3: Health Monitoring

**Goal**: Verify GET /health endpoint returns correct status.

### Step 3.1: Check Health (All Running)

Start both agents:
```bash
curl -X POST http://localhost:5000/api/agents/ingest-v1/start
curl -X POST http://localhost:5000/api/agents/query-v1/start
```

Then check health:
```bash
curl -X GET http://localhost:5000/health
```

**Expected response** (HTTP 200):
```json
{
  "overall": "Healthy",
  "timestamp": "2026-06-23T14:35:00Z",
  "agents": [
    {
      "agentId": "ingest-v1",
      "name": "Wiki Ingest Agent",
      "status": "Running",
      "lastHealthCheckAt": "2026-06-23T14:35:00Z"
    },
    {
      "agentId": "query-v1",
      "name": "Query Agent",
      "status": "Running",
      "lastHealthCheckAt": "2026-06-23T14:35:00Z"
    }
  ]
}
```

### Step 3.2: Check Health (One Agent Faulted)

Simulate agent fault (depends on test implementation). Then check health:
```bash
curl -X GET http://localhost:5000/health
```

**Expected response** (HTTP 503):
```json
{
  "overall": "Degraded",
  "timestamp": "2026-06-23T14:36:00Z",
  "agents": [
    {
      "agentId": "ingest-v1",
      "name": "Wiki Ingest Agent",
      "status": "Running",
      "lastHealthCheckAt": "2026-06-23T14:35:00Z"
    },
    {
      "agentId": "query-v1",
      "name": "Query Agent",
      "status": "Faulted",
      "lastHealthCheckAt": "2026-06-23T14:36:00Z",
      "faultReason": "Health check timeout"
    }
  ]
}
```

**Verification**:
- ✓ HTTP 200 when all agents healthy
- ✓ HTTP 503 when any agent faulted
- ✓ Per-agent status correct

---

## Scenario 4: State Persistence

**Goal**: Verify Hub restarts without data loss.

### Step 4.1: Register and Start Agents

```bash
curl -X POST http://localhost:5000/api/agents \
  -H "Content-Type: application/json" \
  -d '{"agentId": "batch-v1", "name": "Batch Agent", "capabilities": ["synthesis"]}'

curl -X POST http://localhost:5000/api/agents/batch-v1/start
```

### Step 4.2: Verify State in SQLite

```bash
sqlite3 grimoire.db "SELECT AgentId, Name, Status FROM AgentDescriptors;"
```

**Expected output**:
```
batch-v1|Batch Agent|2
```
(Status 2 = Running)

### Step 4.3: Stop Hub

Press Ctrl+C in the terminal running `dotnet run`.

### Step 4.4: Start Hub Again

```bash
cd src/Grimoire.Api
dotnet run --configuration Debug
```

### Step 4.5: Verify State Recovered

```bash
curl -X GET http://localhost:5000/api/agents
```

**Expected response** (HTTP 200):
```json
{
  "agents": [
    {
      "agentId": "batch-v1",
      "name": "Batch Agent",
      "status": "Running",
      "capabilities": ["synthesis"],
      "registeredAt": "2026-06-23T14:40:00Z",
      "lastHealthCheckAt": "2026-06-23T14:40:05Z"
    },
    ...
  ],
  "total": 3
}
```

**Verify logs**: Look for:
```
[INFO] sqlite_recovery: agents_recovered=3 jobs_recovered=0 duration_ms=45
```

**Verification**:
- ✓ Agent descriptors persisted to SQLite
- ✓ State recovered on Hub restart
- ✓ No data loss across restarts

---

## Scenario 5: Observability

**Goal**: Verify observability signals (metrics, logs, traces) are emitted.

### Step 5.1: Check Structured Logs

Hub logs should include structured entries for all lifecycle transitions:

```bash
# Find log file or observe log output
grep "agent_lifecycle_transition" /var/log/grimoire/hub.log
# Expected output:
# {"level":"INFO","event":"agent_lifecycle_transition","agent_id":"ingest-v1","from_status":"Unregistered","to_status":"Starting","timestamp":"2026-06-23T14:30:00Z"}
```

### Step 5.2: Verify OpenTelemetry Metrics

If Prometheus is configured, check metrics:

```bash
curl -X GET http://localhost:9090/metrics | grep grimoire_hub_
```

**Expected metrics**:
```
grimoire_hub_agent_registered_total{agent_id="ingest-v1",agent_name="Wiki Ingest Agent"} 1
grimoire_hub_agent_active_total 2
grimoire_hub_job_queued_total{agent_id="ingest-v1"} 0
```

### Step 5.3: Verify Distributed Traces

If OpenTelemetry is exported to Jaeger, verify traces include:
- `hub.register_agent` span for agent registration
- `hub.start_agent` span for lifecycle transitions
- Trace correlation IDs propagated through all operations

**Verification**:
- ✓ Structured logs emitted for all lifecycle events
- ✓ Business metrics tracked and exported
- ✓ Distributed traces capture request flow

---

## Test Checklist

- [ ] **Scenario 1 (Registration)**: All agents registered, discovery works, duplicate ID rejected
- [ ] **Scenario 2 (Lifecycle)**: Agents transition through valid states, invalid transitions rejected
- [ ] **Scenario 3 (Health)**: Health endpoint returns 200 (healthy) or 503 (degraded) correctly
- [ ] **Scenario 4 (Persistence)**: State recovered from SQLite after Hub restart, no data loss
- [ ] **Scenario 5 (Observability)**: Structured logs, metrics, and traces emitted correctly
- [ ] **Git State Unchanged**: `git status` shows no modifications to wiki/, audits/, raw/ directories
- [ ] **Performance**: Lifecycle transitions < 100ms, health checks < 50ms, startup recovery < 500ms

---

## Troubleshooting

| Issue | Diagnosis | Resolution |
|-------|-----------|-----------|
| Hub fails to start | Check SQLite permissions | Ensure `./grimoire.db` is writable |
| Agent registration fails (500 error) | Check Domain exceptions | Review logs for AgentId validation errors |
| Duplicate agent registration not caught | Domain logic bug | Verify HubAgentRegistry.RegisterAgent enforces uniqueness |
| State not persisted across restarts | SQLite not saving | Verify AgentRepository saves to database |
| Health endpoint returns 500 | Agent health check exception | Implement IAgentWorker.GetHealthAsync correctly |
| Observability signals missing | Instrumentation not registered | Verify OpenTelemetry configured in Program.cs |

---

## Next Steps (Post-Validation)

Once all scenarios pass:
1. Move to `/speckit-tasks` to generate implementation tasks
2. Implement unit tests for state machine logic
3. Implement integration tests with real SQLite
4. Add OpenTelemetry instrumentation
5. Configure CI/CD pipeline to enforce architecture tests

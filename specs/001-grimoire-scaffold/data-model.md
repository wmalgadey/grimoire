# Data Model: Grimoire Project Skeleton Setup

**Feature**: `001-grimoire-scaffold` | **Phase**: 1 | **Date**: 2026-06-23

## Overview

The skeleton introduces **no persistent entities**. The spec explicitly states: *"No persistent entities are introduced in the skeleton."* (ADR-007: storage is Git + Markdown, configured outside this feature.)

This document captures the **interface contracts** that form the domain core boundary. These are the structural primitives that all future features depend on.

---

## Core Interfaces (Grimoire.Core)

### IChannel — Channel Abstraction (ADR-004)

**Namespace**: `Grimoire.Core.Channels`  
**Assembly**: `Grimoire.Core`  
**Purpose**: Unified abstraction for all output channels (Web UI, Telegram, future channels). The Orchestrator routes agent results to active channels through this interface without knowledge of the underlying protocol.

| Member | Kind | Description |
|--------|------|-------------|
| `ChannelId` | Property (get) | Unique identifier for this channel instance |
| `SendAsync` | Method | Deliver a message payload to the channel's endpoint |
| `DisconnectAsync` | Method | Gracefully close the channel connection |

**State transitions**: Channels are either connected (can receive `SendAsync`) or disconnected (terminal). `DisconnectAsync` transitions a channel to disconnected state. Re-connection creates a new channel instance.

**Validation rules**:
- `ChannelId` MUST be non-empty and unique per channel registry entry
- `SendAsync` MUST NOT throw for empty message strings (no-op send is valid)
- `DisconnectAsync` MUST be idempotent (safe to call multiple times)

**Contract reference**: See [contracts/IChannel.cs](contracts/IChannel.cs)

---

### IAgentWorker — Agent Worker Abstraction (ADR-002)

**Namespace**: `Grimoire.Core.Agents`  
**Assembly**: `Grimoire.Core`  
**Purpose**: Unified abstraction for all agent worker implementations (Ingest, Query, Lint, Batch). The Orchestrator dispatches work via this interface — agents are passive and react to assignments.

| Member | Kind | Description |
|--------|------|-------------|
| `AgentId` | Property (get) | Unique identifier for this agent type (e.g., `"ingest"`, `"query"`) |
| `ExecuteAsync` | Method | Process an input payload and return a result payload |
| `StopAsync` | Method | Signal the agent to stop any in-progress work gracefully |

**State transitions**: Agents are either idle (ready for `ExecuteAsync`) or executing (processing a request). `StopAsync` interrupts execution and returns to idle. Multiple concurrent calls to `ExecuteAsync` are not expected in skeleton (hub-spoke serializes via request queue per ADR-006).

**Validation rules**:
- `AgentId` MUST be a lowercase, hyphen-separated string (e.g., `"ingest"`, `"batch-2026"`)
- `ExecuteAsync` MAY return an empty string for agents with no output in a given run
- `StopAsync` MUST be idempotent

**Contract reference**: See [contracts/IAgentWorker.cs](contracts/IAgentWorker.cs)

---

## Architecture Boundary Map

```
Grimoire.Core (no external dependencies)
├── Channels/
│   └── IChannel              ← consumed by Grimoire.Api (Orchestrator)
│                             ← implemented by Grimoire.Infrastructure.Channels.*
└── Agents/
    └── IAgentWorker          ← consumed by Grimoire.Api (Orchestrator)
                              ← implemented by Grimoire.Infrastructure.Agents.*

Grimoire.Api (depends on Grimoire.Core only)
└── OrchestratorService       ← routes via IChannel[] and IAgentWorker[]

Grimoire.ArchTests (depends on Grimoire.Core + Grimoire.Api)
└── ArchitectureTests         ← enforces the above boundary via NetArchTest.Rules
```

**Key boundary rule (ADR-005 + ADR-007)**: `Grimoire.Core` MUST NOT reference any project or NuGet package outside of `netstandard2.1` / `net9.0` base class libraries. No EF Core, no SignalR, no Telegram SDK — ever.

---

## Future Entities (Out of Scope for Skeleton)

These entities will be introduced in subsequent feature specs:

| Entity | Owning feature | Stored as |
|--------|----------------|-----------|
| `WikiPage` | Ingest feature | `wiki/{slug}.md` (ADR-007) |
| `AuditFinding` | Lint feature | `audits/open/{id}.md` (ADR-007) |
| `BatchSynthesis` | Batch feature | `wiki/batch/{date}.md` (ADR-007) |
| `ChannelSession` | Web UI channel | In-memory (SignalR connection state) |

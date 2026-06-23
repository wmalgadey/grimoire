# Architecture Decision Records

**Format:** MADR 3.0 (Markdown Any Decision Records)

**Status:** Live (2026-06)

**Language:** English

---

## Index

| ADR | Title | Status | Decision |
|-----|-------|--------|----------|
| [ADR-001](adr-001.md) | Backend Framework — .NET 9 Minimal API with SignalR | Accepted | Type safety + SignalR for real-time instead of Python/Node |
| [ADR-002](adr-002.md) | Agent Runtime Strategy — Worker Services Instead of Container-per-Agent | Accepted | In-process Worker Services with interface abstraction for later container escape |
| [ADR-003](adr-003.md) | Frontend — Svelte 5 + Vite Instead of HTMX | Accepted | Reactive components + streaming instead of server-rendered HTML |
| [ADR-004](adr-004.md) | Channel Abstraction — Unified Channel Interface | Accepted | `IChannel` interface + registry for protocol agnosticism |
| [ADR-005](adr-005.md) | Monorepo Structure and Spec-Kit Workflow Integration | Accepted | Flat monorepo with `constitution.md` for Spec-Kit context |
| [ADR-006](adr-006.md) | Hub-Spoke Orchestration Architecture | Accepted | Centralized backend orchestrator routing to specialized agent workers |
| [ADR-007](adr-007.md) | Storage Strategy — Git + Plain Markdown Files | Accepted | Git-native, Obsidian-compatible knowledge base inspired by Karpathy's llm.txt |
| [ADR-008](adr-008.md) | Operational State Persistence — SQLite for Hub Ephemera | Accepted | SQLite for ephemeral runtime state; Git + Markdown remains canonical storage |
| [ADR-009](adr-009.md) | Domain-Driven Code Organization — Screaming Architecture | Proposed | Reorganize by business domain (Agents, Hubs, Channels, Shared) instead of technical layers |

---

## Key Architectural Principles

- **Spec-Kit-First**: All technical decisions support agentic code generation via Spec-Kit
- **Solo Developer**: No infrastructure complexity before its time
- **Extensible Contracts**: Interfaces (`IChannel`, `IAgentWorker`) enable future escape-hatches without rework
- **Framework Ownership**: Deep framework expertise outweighs marginal LLM performance differences
- **Monorepo for Context**: Project-wide `constitution.md` gives agents clear constraints

---

## Related Documents

- `.specify/memory/constitution.md` — Tech stack and coding conventions
- `.specify/templates/` — Spec-Kit agent prompts
- `specs/` — Feature-specific specifications

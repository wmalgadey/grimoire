---
status: proposed
date: 2026-07-01
deciders: Wolfgang Malgadey
context-section: "docs/decision-context-overview.md §4"
---

# ADR-004: Repository and Code Structure

## Context and Problem Statement

Grimoire consists of at least three independently evolvable subprojects: a .NET backend,
a SvelteKit frontend, and one or more agent implementations. Spec-Kit artifacts (specs,
plans, tasks) must map clearly to their corresponding subproject. At the in-project level,
the codebase must make the domain architecture visible on first inspection — a developer
opening the project must see Agents, Hubs, and Channels, not Controllers, Services, and
Repositories. This directly implements Constitution Principle I (Strategic DDD visible
from the first commit).

Full problem statement: `docs/decision-context-overview.md §4`

## Decision Drivers

- **Screaming Architecture**: folder names must reflect domain intent, not technical role
- **Strategic DDD visibility**: Bounded Contexts (Ingest, Query, Lint) are top-level names
- **Spec-Kit traceability**: `specs/NNN-feature/` maps to its implementation subproject
- **Independent evolvability**: backend, frontend, agents can be built and tested in isolation
- **Solo-developer manageability**: monorepo over polyrepo — one clone, one CI pipeline
- **Constitution enforcement**: `constitution.md` applies project-wide from a single root

## Considered Options

### Repository Topology

| Option | Solo-dev overhead | Spec-Kit mapping | CI complexity |
|--------|------------------|-----------------|---------------|
| Monorepo (single repo, top-level dirs) | ✅ low | ✅ direct | low |
| Polyrepo (one repo per subproject) | ❌ high | ❌ cross-repo refs | high |
| Monorepo with Nx/Turborepo orchestration | ⚠️ medium | ✅ | medium |

### Backend Internal Structure

| Option | DDD visibility | Screaming Architecture | Agent reviewability |
|--------|---------------|----------------------|-------------------|
| Domain-first folders (Ingest/, Query/, Lint/, Hub/, Channels/) | ✅ | ✅ | ✅ |
| Technical layers (Controllers/, Services/, Repositories/) | ❌ | ❌ | ❌ |
| Feature-slice folders (per endpoint) | ⚠️ partial | ⚠️ | ✅ |

## Decision Outcome

### Repository topology: Monorepo

A single Git repository with top-level directories per subproject:

```
grimoire/                         ← repo root
├── backend/                      ← ASP.NET Core solution
├── frontend/                     ← SvelteKit app
├── agents/
│   └── ingest/                   ← standalone Ingest agent (own runtime/deps)
├── docs/
│   ├── adr/                      ← Architecture Decision Records (this file)
│   └── decision-context-overview.md
├── specs/                        ← Spec-Kit feature artifacts
│   ├── index.md
│   └── NNN-feature-name/
│       ├── spec.md
│       ├── plan.md
│       └── tasks.md
├── .specify/                     ← Spec-Kit toolchain config
├── CLAUDE.md                     ← agent context (project-wide)
└── constitution.md               ← (symlink or reference to .specify/memory/constitution.md)
```

No build orchestration tool (Nx, Turborepo) is introduced until a concrete need arises
(Simplicity Budget ADR-009 governs this gate).

### Backend internal structure: Domain-first

The backend project is organized by Bounded Context, not by technical role:

```
backend/
└── src/
    └── Grimoire.Backend/
        ├── Ingest/               ← Ingest Bounded Context
        │   ├── Domain/           ← pure domain types, no infrastructure imports
        │   ├── Application/      ← use case handlers
        │   └── Infrastructure/   ← file I/O, LLM client adapters
        ├── Query/                ← Query Bounded Context (same structure)
        ├── Lint/                 ← Lint Bounded Context (same structure)
        ├── Hub/                  ← Orchestrator: routes requests, relays task state
        │   ├── Domain/
        │   ├── Application/
        │   └── Infrastructure/
        ├── Channels/             ← External-facing adapters
        │   ├── WebUi/            ← SignalR hubs, HTTP endpoints
        │   └── Telegram/         ← Telegram Bot API adapter
        └── Shared/               ← cross-context types (Task artifact schema, etc.)
```

Tactical DDD patterns (Aggregates, Repositories, Domain Events) are only permitted
inside `*/Domain/` subfolders, per Constitution Principle I.

### Frontend structure

```
frontend/
└── src/
    ├── routes/
    │   ├── ingest/               ← Ingest surface (source submission + task progress)
    │   ├── query/                ← Query surface (chat interface)
    │   └── lint/                 ← Lint surface (trigger + findings)
    ├── lib/
    │   ├── components/           ← shared, reusable UI components
    │   └── hub/                  ← SignalR connection client
    └── styles/
        └── tokens.css            ← design tokens (CSS custom properties)
```

### Agents structure

Each agent is an independent subproject with its own runtime and dependency chain:

```
agents/
└── ingest/                       ← standalone Ingest agent
    ├── src/
    ├── tests/
    └── Dockerfile                ← independent containerizability
```

### Spec-Kit artifact mapping

| Spec folder | Corresponds to |
|-------------|---------------|
| `specs/001-ingest-minimal/` | `agents/ingest/` + `backend/src/.../Ingest/` |
| `specs/010-architecture-baseline/` | repo root, `backend/`, `frontend/` scaffolding |
| `specs/NNN-query-*/` | `backend/src/.../Query/` + `frontend/src/routes/query/` |

### Consequences

**Positive:**
- Opening the backend immediately reveals the domain: Ingest, Query, Lint, Hub, Channels
- Spec-Kit feature folders map directly to implementation folders — traceability is structural
- Bounded Context isolation is visible at the filesystem level, making architecture tests straightforward to write
- Agents directory allows the Ingest agent to carry its own runtime without polluting the backend

**Negative:**
- Domain-first structure requires discipline: technical utilities (logging config, DI setup)
  must be placed in `Shared/` or a dedicated `Infrastructure/` root — not scattered across
  Bounded Contexts
- A new Bounded Context requires creating the three-folder skeleton (Domain/, Application/,
  Infrastructure/) even before any code is written; SDD tasks must include this scaffolding

## Architectural Enforcement

The following rules MUST be enforced by structural boundary tests (Phase 0 in tasks.md):

1. **Domain layer isolation**: No file under `*/Domain/` may import from `*.Infrastructure`
   or `*.Channels` namespaces. Enforced via NetArchTest.Rules.

2. **Channel isolation**: No file under `Hub/` or Bounded Context `Application/` or
   `Domain/` may reference `Microsoft.AspNetCore.SignalR` or Telegram-specific types.
   Only `Channels/WebUi/` and `Channels/Telegram/` may import channel-specific SDKs.

3. **Cross-context imports**: No Bounded Context may import from another Bounded Context's
   `Domain/` or `Application/` layer directly. Cross-context communication goes through
   `Shared/` types or domain events only.

## Related ADRs

- ADR-001 (Backend/Frontend Frameworks) — defines the technology within this structure
- ADR-002 (Agent Orchestration) — defines how Hub coordinates the Bounded Contexts
- ADR-009 (Simplicity Budget) — gates introduction of build orchestration tools

# Grimoire

AI Agent Orchestrator — a .NET 9 Minimal API backend with SignalR and a Svelte 5 frontend.

## Directory Structure

```text
src/
├── backend/           # .NET 9 solution (Grimoire.Api, Grimoire.Core, Grimoire.ArchTests)
├── frontend/          # Svelte 5 + Vite TypeScript SPA
└── agents/            # Placeholder for future agent implementations

docs/
└── adr/               # Architecture Decision Records (ADR-001 through ADR-007)

specs/                 # Spec-Kit feature specs and implementation plans
.github/workflows/     # CI pipelines (path-filtered per subproject)
```

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 9.0+ |
| Node.js | 20+ |
| npm | 10+ |

## Build Commands

**Backend**

```sh
cd src/backend
dotnet build          # compile all three projects (0 errors, 0 warnings)
dotnet test Grimoire.ArchTests   # run all 6 architecture enforcement tests
```

**Frontend**

```sh
cd src/frontend
npm install
npm run build         # production build → dist/
npm run lint          # ESLint 9 flat config
```

## Architecture Decision Records

All structural choices are documented under [`docs/adr/`](docs/adr/):

| ADR | Title |
|-----|-------|
| [ADR-001](docs/adr/ADR-001-backend-framework.md) | Backend Framework — .NET 9 Minimal API with SignalR |
| [ADR-002](docs/adr/ADR-002-agent-runtime.md) | Agent Runtime — Worker Services + IAgentWorker |
| [ADR-003](docs/adr/ADR-003-frontend.md) | Frontend — Svelte 5 + Vite |
| [ADR-004](docs/adr/ADR-004-channel-abstraction.md) | Channel Abstraction — IChannel |
| [ADR-005](docs/adr/ADR-005-monorepo-structure.md) | Monorepo Structure |
| [ADR-006](docs/adr/ADR-006-hub-spoke-orchestration.md) | Hub-Spoke Orchestration |
| [ADR-007](docs/adr/ADR-007-storage-strategy.md) | Storage Strategy — Git + Markdown |

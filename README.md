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

See [`docs/adr/`](docs/adr/) for all structural decisions.

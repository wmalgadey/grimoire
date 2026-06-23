# LLM-Wiki AI-Harness: Architecture & Development Guidelines

## Language Policy

**Primary Language: English**

All code, comments, documentation, and architectural artifacts must be written in English. This ensures:
- Consistency across codebase and documentation
- Accessibility for international teams and contributors
- Compatibility with LLM code generation (models trained primarily on English codebases)

Exception: Project-internal notes or personal development logs may use other languages if clearly marked, but all shared documentation, code comments, and specifications must be in English.

## Tech Stack

Reference: `docs/adr/` for detailed rationale on each decision.

- **Backend**: .NET 9 Minimal API + SignalR (ADR-001)
- **Frontend**: Svelte 5 + Vite (ADR-003)
- **Agent Runtime**: .NET Worker Services (ADR-002)
- **Channel Abstraction**: `IChannel` interface (ADR-004)
- **Project Structure**: Flat monorepo with Spec-Kit integration (ADR-005)
- **Orchestration Pattern**: Hub-spoke — all routing via `Grimoire.Api` (ADR-006)
- **Storage Strategy**: Git + Markdown; no binary database (ADR-007)

## Spec-Kit Workflow

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at `specs/003-domain-driven-refactor/plan.md`.
<!-- SPECKIT END -->

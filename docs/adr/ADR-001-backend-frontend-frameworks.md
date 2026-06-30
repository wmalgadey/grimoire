---
status: proposed
date: 2026-07-01
deciders: Wolfgang Malgadey
context-section: "docs/decision-context-overview.md §1"
---

# ADR-001: Backend and Frontend Frameworks

## Context and Problem Statement

Grimoire requires a backend that unifies agent orchestration, multi-channel management
(Web UI, Telegram), and real-time task-state streaming. The frontend must expose three
distinct agent-shaped surfaces (Ingest, Query, Lint) with live progress updates. Both
are developed agentic-first via Spec-Kit, so framework choices must enable predictable
code generation with low boilerplate. The solo developer has deep .NET and TypeScript
expertise; framework selection must leverage that rather than work against it.

Full problem statement: `docs/decision-context-overview.md §1`

## Decision Drivers

- **Streaming-first**: all three agent surfaces stream in-flight task state to the UI
- **Solo-developer readability**: coupling between components must stay low and auditable
- **SDD-agent predictability**: frameworks must generate code that is reviewable and low-noise
- **Existing expertise**: .NET backend, TypeScript frontend — no ramp-up cost
- **UI predictability**: layout and style governed by shared definitions, not per-component ad-hoc CSS
- **Support horizon**: frameworks with active LTS release tracks so architecture tests stay valid

## Considered Options

### Backend

| Option | Streaming | .NET native | Boilerplate | SDD-agent predictability |
|--------|-----------|-------------|-------------|--------------------------|
| ASP.NET Core + SignalR | ✅ native hubs | ✅ | low | high |
| ASP.NET Core + SSE only | ⚠️ server-push only | ✅ | low | high |
| Node.js (Express/Fastify) | ✅ | ❌ different stack | low | high |
| Go (net/http) | ✅ | ❌ new language | medium | medium |

### Frontend

| Option | TypeScript | Reactive/streaming | Component model | Bundle size |
|--------|------------|-------------------|-----------------|-------------|
| SvelteKit | ✅ | ✅ reactive by default | simple, file-based | minimal |
| Next.js (React) | ✅ | ⚠️ requires manual wiring | verbose, hooks-heavy | large |
| Nuxt (Vue) | ✅ | ✅ | moderate | medium |
| Vanilla TS + Web Components | ✅ | ✅ | low-level | minimal |

### CSS / Design System

| Option | Predictability | Token-based | Auditability |
|--------|---------------|-------------|--------------|
| CSS custom properties (design tokens) + utility classes | ✅ | ✅ | ✅ |
| Tailwind CSS | ✅ | ⚠️ config-based | ✅ |
| CSS-in-JS (Emotion, styled-components) | ❌ scattered | ❌ | ❌ |
| Per-component `<style>` (no shared tokens) | ❌ | ❌ | ❌ |

## Decision Outcome

**Backend: ASP.NET Core (.NET 10) + SignalR**

ASP.NET Core Minimal API as the HTTP layer; SignalR hubs for persistent, bidirectional
connections to the Web UI. SignalR provides native fan-out to all connected clients when
agent task state changes — no polling, no SSE workarounds. .NET 10 is the current LTS
track and the developer's primary runtime.

**Frontend: SvelteKit**

SvelteKit as the full-stack frontend framework. Svelte's reactive model maps cleanly onto
streaming task state without manual subscription wiring. The file-based routing matches the
three-surface structure (Ingest, Query, Lint) directly. TypeScript-first. Minimal bundle.

**CSS: CSS custom properties as design tokens + scoped component styles**

A single shared token file (`frontend/src/styles/tokens.css`) defines all color, spacing,
and typography values. Components consume tokens via `var(--token-name)` and may use
scoped `<style>` blocks only for layout. No per-component color or size literals.
Tailwind is explicitly rejected: its utility-class volume increases review noise for an
SDD-generated codebase.

### Consequences

**Positive:**
- SignalR fan-out covers all three streaming surfaces with one pattern
- Both framework choices are in the developer's existing expertise
- Svelte's compiler-based reactivity produces minimal, readable output for agent review
- Design token layer makes visual consistency an auditable constraint

**Negative:**
- SignalR adds a WebSocket connection model — the Telegram channel must use a separate
  adapter, not a shared SignalR client (Telegram is a pull-based bot API)
- SvelteKit's SSR layer adds a Node.js server process for the frontend; this must be
  considered in the deployment topology (see ADR-004)
- .NET 10 Minimal API syntax diverges from classic MVC conventions; SDD agents must be
  given explicit code patterns, not relied on to infer them

## Architectural Enforcement

The following rule MUST be enforced by a structural boundary test (Phase 0 in tasks.md):

> No backend source file outside `Channels/WebUi/` may reference `Microsoft.AspNetCore.SignalR` directly.
> Hub definitions are the exclusive responsibility of the WebUi channel adapter.

This ensures SignalR does not leak into orchestration or domain logic.

## Related ADRs

- ADR-004 (Repository Structure) — defines where backend and frontend source live
- ADR-002 (Agent Orchestration) — defines how agents connect to SignalR hubs
- ADR-003 (External Interface Contracts) — defines the SignalR hub contract surface

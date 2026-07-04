---
status: accepted
---

# ADR-001: Backend and Frontend Technology Stack

## Context and Problem Statement

Grimoire needs a backend that unifies agent orchestration, channel management (e.g. a Web
UI, Telegram, and future channels), and real-time task-state updates, plus a frontend Web
UI that consumes that state as a stateful, live connection rather than plain
request/response. Grimoire is built and maintained by a single developer working
agentic-first: a coding agent generates implementation from specifications, and the
developer reviews that output. No backend or frontend technology has previously been
ratified for the project, so this decision fixes the stack once, rather than letting it
be decided implicitly, feature by feature, as agent processes and API contracts are built.

## Decision Drivers

- Must play to the developer's strongest existing expertise (deep, hands-on experience
  with .NET/C# and TypeScript) so that agent-generated code can be reviewed with genuine
  confidence by the one human responsible for it, rather than in an unfamiliar stack.
- Must support real-time/streaming task-state updates natively (current and future
  interactive ingest/lint/query surfaces), without bolting on a second transport later.
- Must keep the codebase easy to read with low coupling between components, so a solo
  developer can hold the whole system in their head.
- Must have a clear, currently-valid support horizon, so the choice does not need to be
  revisited again shortly after being made.

## Considered Options

1. C# / .NET 10 backend + TypeScript / SvelteKit frontend
2. Python / FastAPI backend + htmx frontend (no separate JS framework)
3. C# / .NET backend + TypeScript / React frontend

## Decision Outcome

Chosen option: **Option 1 — C# / .NET 10 (ASP.NET Core Minimal APIs + SignalR) backend,
TypeScript / SvelteKit frontend.**

- **.NET 10** is chosen over .NET 9: .NET 9 is a Short-Term-Support release (~18 months
  of support from its November 2024 release) and is already out of support by the
  project's current timeline, whereas .NET 10 is the current Long-Term-Support release
  with a multi-year support window. Starting a new project on an already-unsupported
  runtime would be indefensible.
- **SignalR** is selected as the real-time transport because it is the idiomatic
  ASP.NET Core mechanism for persistent, bidirectional connections and requires no
  additional infrastructure beyond the Hub process itself.
- **SvelteKit** is selected as the frontend framework: it matches the developer's
  TypeScript expertise, and its component-plus-scoped-style model naturally supports
  building a small set of central, reusable UI components governed by shared CSS, rather
  than many one-off, inconsistently styled views.
- The frontend is fixed by this decision but is not necessarily built immediately by
  every feature that touches the backend — some features may only need the Hub side.
  Any backend API/task-artifact contract should still be designed compatibly with this
  frontend choice from the start, so it does not need to be reshaped later.

### Consequences

- Good, because both halves of the stack match the developer's strongest expertise,
  directly supporting confident review of agent-generated code.
- Good, because .NET 10's multi-year LTS window avoids a near-term forced re-platforming.
- Bad, because SvelteKit is a smaller ecosystem than React, which may mean fewer
  ready-made component libraries; mitigated by the fact that the UI is deliberately built
  from a small set of central, reusable components rather than a large surface of
  off-the-shelf widgets.
- Neutral, because this ADR commits the project to a two-language codebase (C# on the
  backend, TypeScript on the frontend) rather than a single-language stack; this is
  accepted as the direct consequence of choosing a native real-time backend transport
  paired with a strongly-typed frontend framework.

## More Information

This ADR fixes the project's foundational technology stack. Later ADRs may refine
specific subsystems (execution model, persistence, credentials, observability) but should
not need to re-litigate the backend/frontend language or framework choice itself.

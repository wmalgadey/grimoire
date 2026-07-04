# Research: Ingest Minimal

**Input**: Technical Context unknowns from `plan.md`, guided by
`docs/decision-context-overview.md` (§1 Tech Stack, §2 Agent Execution, §5 Persistence,
§6 Credentials, §8 Observability) and `dev-experience.md` (prior stack attempt + open
question "why .NET 9 and not .NET 10?").

This is the **first planned feature** in the project. Because no ADRs exist yet
(`docs/adr/` is empty after the 2026-06-30 reset), this research resolves the foundational,
project-wide technology stack decision in addition to the decisions strictly local to
Ingest — per user instruction, this is deliberate: later features (Query, Lint, Web UI)
build on the same stack rather than re-litigating it.

## 1. Backend language/runtime & frontend framework

- **Decision**: Backend = C# on **.NET 10** (LTS), ASP.NET Core Minimal APIs, hosting a
  **Hub** process. Frontend = **TypeScript** with **SvelteKit** (deferred to the Web UI
  feature; the decision is locked now so the API contract in this feature doesn't need to
  be redesigned later).
- **Rationale**: `docs/decision-context-overview.md` §1 explicitly frames the choice as
  needing to "play to the solo developer's existing expertise rather than against it" —
  the developer has deep .NET and TypeScript experience. `dev-experience.md` records a
  prior attempt already converged on .NET + Svelte before being reset for scope reasons,
  not stack reasons; there is no new information that would overturn that choice.
  `dev-experience.md` also left an explicit open question — "Warum .NET 9 und nicht .NET
  10?" — which this research resolves: as of 2026-07-03, .NET 9 (STS, ~18 months support
  from Nov 2024) is out of support; **.NET 10 is the current LTS** (3-year support
  window), making it the only defensible choice for a project starting now.
- **Alternatives considered**:
  - Python + FastAPI + htmx (the earlier `docs/project-conversation.md` sketch) — rejected:
    contradicts the developer's stated expertise, and decision-context-overview.md
    frames the backend/frontend choice as coupled to real-time transport (§1), where
    ASP.NET Core + SignalR is a native fit for a .NET developer.
  - React/Next.js frontend — rejected only for consistency with the prior direction
    (Svelte); no functional requirement in this feature depends on the frontend choice,
    since the Web UI is out of scope for 001-ingest-minimal (see spec Assumptions).
- **New ADR**: `docs/adr/ADR-001-backend-frontend-tech-stack.md`

## 2. Real-time/streaming transport

- **Decision**: **SignalR** (ASP.NET Core) as the transport for task-state updates to
  connected channels, for when interactive streaming is implemented.
- **Rationale**: decision-context-overview.md §1 requires the backend to support
  streaming/in-flight task state "natively," and couples this to the frontend's
  connection model. SignalR is the idiomatic ASP.NET Core mechanism for persistent,
  bidirectional connections and integrates directly with the Hub.
- **Scope note for this feature**: 001-ingest-minimal is explicitly a **batch,
  fire-and-forget** slice (per decision-context-overview.md §0, "MVP version: batch
  processing... Future version: interactive streaming"). SignalR wiring is decided now
  (ADR-001) but is **not required to be implemented** until a channel needs live
  progress; this feature only needs the task artifact file to reach a terminal state on
  disk.
- **Alternatives considered**: raw WebSockets (more boilerplate, no built-in reconnection
  semantics), Server-Sent Events (one-directional only, insufficient for future
  interactive annotation/interrupt use cases per decision-context-overview.md §0).

## 3. Ingest agent execution model

- **Decision**: The Ingest agent is a **standalone .NET console application**, invoked by
  the Hub as a **child process** (subprocess), not an in-process library call. The Hub
  passes the source reference and repo paths via CLI arguments/environment variables and
  waits for process exit; the agent itself performs all file writes (wiki page, task
  artifact, index.md, log.md) directly against the git working tree.
- **Rationale**: decision-context-overview.md §2 mandates that Ingest specifically
  "requires standalone CLI operation, independent containerizability, and a lifecycle
  decoupled from the backend" because of its LLM pipeline dependency chain. Subprocess
  invocation is the simplest mechanism that satisfies this today while leaving a clean
  migration path to a containerized/queued execution later (same CLI contract, different
  process substrate) — consistent with §9's simplicity budget (no message broker needed
  for a solo-developer MVP with one concurrent ingest at a time).
- **Alternatives considered**: gRPC/HTTP call to a separately-running agent daemon —
  rejected as premature operational overhead for the MVP (a second long-running service
  to deploy/monitor) with no current concurrency requirement; in-process library call —
  rejected outright, contradicts the explicit standalone-lifecycle requirement in §2.
- **New ADR**: `docs/adr/ADR-002-ingest-agent-execution-model.md`

## 4. Persistence: domain state vs. operational state

- **Decision**: Domain state (wiki pages, task artifacts, `index.md`, `log.md`) is
  **plain markdown files committed to the project's git repository**. Operational state
  (which task is currently "running", needed to reconcile stuck tasks after a restart per
  spec FR-013) is a small embedded **SQLite** database file owned by the Hub, outside git.
- **Rationale**: decision-context-overview.md §5 requires domain state to be portable,
  diffable, and externally editable (Obsidian-compatible), which only plain files satisfy,
  while operational state needs restart durability without polluting the git history with
  internal bookkeeping. SQLite is a single embedded file, requires no separate server
  process (§9 simplicity budget), and has first-class .NET support (`Microsoft.Data.Sqlite`
  / EF Core).
- **Alternatives considered**: in-memory-only operational state — rejected, cannot satisfy
  FR-013 (reconciling a "running" task to "failed" requires the state to survive the
  crash that necessitated the restart); Postgres/Redis — rejected as disproportionate
  infrastructure for single-process, single-user operational bookkeeping at MVP scale.
- **New ADR**: `docs/adr/ADR-003-domain-operational-state-persistence.md`

## 5. Credential handling for the LLM API key

- **Decision**: The Hub reads the LLM API key (Anthropic Claude) from a local,
  git-ignored secrets file (e.g. `.env`) and injects it **only into the Ingest agent's
  child-process environment** at spawn time. No other process or channel receives it.
- **Rationale**: decision-context-overview.md §6 asks whether an equivalent to NanoClaw's
  Credential Gateway is warranted. For a solo-developer MVP with exactly one credential
  and one credential-consuming agent, a dedicated gateway process is disproportionate
  overhead (§9). Scoping the secret to the one child process that needs it (rather than
  the whole Hub environment) satisfies the underlying goal — "no agent holds credentials
  beyond its own operational scope" — without new infrastructure.
- **Alternatives considered**: Process-wide environment variables shared by the whole Hub
  — rejected, violates least-privilege since future agents (Query, Lint) would then also
  have ambient access to the LLM key even when not calling it; a full credential-gateway
  proxy — deferred until a second credential or a second agent creates an actual
  cross-agent leakage risk to defend against.
- **New ADR**: `docs/adr/ADR-004-credential-scoping.md`

## 6. Observability backend (local + CI)

- **Decision**: OpenTelemetry .NET SDK in the Hub and the Ingest agent, exporting via
  OTLP. Local dev target: the **.NET Aspire Dashboard** (single container, native OTLP
  receiver, no cloud dependency). CI verifies emitted spans/metrics/log events via an
  in-memory OTel exporter in tests rather than a running backend.
- **Rationale**: The constitution (Principle IV) mandates OTel instrumentation as a
  build-breaking Definition-of-Done gate, and decision-context-overview.md §8 requires
  this to be locally verifiable without deploying cloud infrastructure. The Aspire
  Dashboard is a single local container purpose-built for exactly this, and in-memory
  exporter assertions in CI avoid standing up any collector in the pipeline.
- **Alternatives considered**: Grafana/Tempo/Loki docker-compose stack — rejected for MVP
  as disproportionate to a single-feature slice (§9); no local backend at all — rejected,
  fails decision-context-overview.md §8's explicit local-verifiability requirement.
- **New ADR**: `docs/adr/ADR-005-observability-backend.md`

## Resolved Technical Context

| Field | Resolution |
|---|---|
| Language/Version | C# 14 / .NET 10 (Hub + Ingest agent) |
| Primary Dependencies | ASP.NET Core (Minimal APIs, SignalR), Anthropic Claude API client, `Microsoft.Data.Sqlite` |
| Storage | Git-tracked markdown files (wiki, tasks, index.md, log.md) + SQLite file for operational task state |
| Testing | xUnit + Testcontainers (integration, per constitution Principle II); no unit tests for DTOs/mappers |
| Target Platform | Self-hosted Linux (container) for the Hub; cross-platform .NET console app for the Ingest agent |
| Project Type | Web application skeleton (backend now; frontend deferred) + standalone agent process |
| Performance Goals | Single-ingest MVP: no concurrency target; one ingest operation completes without blocking Hub responsiveness to other requests |
| Constraints | No partial/orphaned wiki writes on failure (FR-008); task artifact reconciliation on restart (FR-013) |
| Scale/Scope | Single trusted user, one source per ingest operation, one primary wiki page per source (per spec Assumptions) |

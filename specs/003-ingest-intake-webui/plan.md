# Implementation Plan: Ingest Intake Web UI

**Branch**: `003-ingest-intake-webui` | **Date**: 2026-07-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-ingest-intake-webui/spec.md`

## Summary

Build a Web UI ingest-submission surface where users submit one source (URL, Markdown, PDF, Office),
receive immediate acceptance feedback, and track complete lifecycle progress on a Kanban
board (`received -> converting -> queued -> running -> completed|failed`). The Hub owns
URL fetch + conversion with MarkItDown, persists both original and normalized artifacts,
creates/updates the existing Task Artifact, and auto-triggers Ingest once `queued`.
Frontend is implemented with reusable Svelte components and a modern CSS framework
selection justified in [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10 backend, TypeScript + SvelteKit frontend (ADR-001)

**Primary Dependencies**:
- Backend: ASP.NET Core Minimal APIs, SignalR, MarkItDown execution adapter,
  OpenTelemetry .NET SDK
- Frontend: SvelteKit, reusable component primitives, modern CSS framework
  (chosen in research)

**Storage**:
- Domain state in git-tracked markdown/plain files under content root (`wiki/` by
  default), per ADR-003
- Operational state in SQLite (`backend/data/operational-state.db`)
- Intake artifacts in raw storage: normalized markdown (`raw/sources/*.md`) plus
  original source payload (`raw/originals/*`) with metadata reference

**Testing**:
- Backend integration tests (xUnit + Testcontainers where boundary coverage requires it)
- Frontend component/integration tests (Vitest + Testing Library)
- Architecture tests for boundary constraints
- End-to-end smoke scenario for submit -> board lifecycle updates

**Target Platform**: Linux dev container and developer workstation; browser-based UI;
Hub + Ingest agent on same repository root

**Project Type**: Web application (backend service + realtime frontend)

**Performance Goals**:
- Submission acknowledgment visible to user within 2 seconds (p95)
- Board lifecycle state propagation to connected clients within 2 seconds after state
  transition commit (p95)
- Conversion start for accepted source within 5 seconds (single-node baseline)

**Constraints**:
- Existing single-concurrent-ingest-run constraint remains authoritative (FR-013)
- URL content is fetched by Hub during intake and ingested from persisted raw file
  (clarification session)
- No new infrastructure beyond approved stack/ADRs
- Reusable UI components are mandatory for repeated interface patterns

**Scale/Scope**:
- Single trusted user scope (current project assumption)
- One source per submission
- Multiple concurrent submissions accepted; ingest execution serialized by existing
  agent-run constraint

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance in this plan | Status |
| --- | --- | --- |
| I. Domain Architecture & Strategic DDD | Reuses existing domain language (`Task Artifact`, `Ingest Submission`, `Ingest Run`, `Raw Source File`). No tactical DDD outside domain core is introduced in planning artifacts. | PASS |
| II. Pragmatic Testing Strategy | Success criteria map to deterministic integration/contract tests for harness behavior. No requirement is reframed as deterministic agent-judgment logic in backend. | PASS |
| III. ADR-Driven & Test-Enforced Architecture | All ADRs in `docs/adr/` were read and are listed below with constraints. Structural boundary tests are planned as first tasks in `/speckit-tasks`. | PASS |
| IV. Behavioral & Observable Engineering | Observability section defines required metrics/logs/spans for intake + realtime propagation. Trace/log contract derivation retained for tasks phase. | PASS |
| V. Agentic Core & Deterministic Harness | UI/hub feature remains harness-only for source-submission orchestration; no wiki-content judgment moved to deterministic backend. Agent is triggered only on persisted source artifacts. | PASS |

No violations. `## Complexity Tracking` remains empty.

**Post-Phase-1 re-check**: Research, data model, contracts, and quickstart preserve the
same constraints; no new architectural boundary requiring a new ADR was introduced.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-001 | Backend and Frontend Technology Stack | Backend remains .NET 10 + ASP.NET Core/SignalR; frontend remains SvelteKit. CSS framework choice must fit SvelteKit and reusable component strategy. |
| ADR-002 | Ingest Agent Execution Model | Hub continues spawning the Ingest agent child process. This feature can trigger runs but must not redesign invocation model. |
| ADR-003 | Domain vs. Operational State Persistence | Task artifacts and wiki-facing state remain git-tracked files; operational lifecycle bookkeeping remains SQLite-backed. |
| ADR-004 | Credential Scoping for the LLM API Key | Any automatic trigger path must preserve current least-privilege credential injection at child-process spawn. |
| ADR-005 | Observability Backend (Local and CI) | Ingest-submission and ingest-lifecycle signals use OTel + Aspire locally and in-memory/export assertions in CI tests. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | Ingest feature may queue and trigger agent runs, but must not bypass guarded tool boundary or encode content judgment in hub/frontend logic. |

**New ADR required?**: No. This feature uses existing architectural boundaries and
introduces no new infrastructure or cross-cutting runtime model.

ADR policy note for this feature: UI component composition and CSS framework choice are
treated as implementation-level decisions under ADR-001 (fixed frontend stack:
TypeScript + SvelteKit). A new ADR becomes mandatory only if we introduce a new
cross-cutting architectural boundary (for example, a project-wide design-system package
spanning multiple bounded contexts, a second frontend runtime, or infrastructure-level
styling/tooling requirements that affect the whole platform).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
| --- | --- | --- |
| Ingest submission validation, conversion orchestration, queueing | Harness | `backend/src/Grimoire.Hub/Submission/` + new ingest-submission API layer |
| URL fetch + artifact persistence (original + normalized) | Harness | `backend/src/Grimoire.Hub/Submission/` |
| Realtime board status projection | Harness | Hub API + SignalR stream + frontend board adapters |
| Auto-trigger ingest on `queued` | Harness | `backend/src/Grimoire.Hub/AgentDispatch/` + submission workflow |
| Wiki-content judgment and edits | Agentic core | Existing Ingest agent instructions/tool loop (`agents/ingest/*`, `Grimoire.IngestAgent`) |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| SC-001: accepted submissions appear on board within seconds | Deterministic guarantee | Backend+frontend integration test | Test hub host + SignalR test client + fake clock | URL submission fixture, markdown upload fixture | Asserts ack response and board card visibility timing window |
| SC-002: successful conversions produce markdown in raw location | Deterministic guarantee | Integration test | Test filesystem sandbox + conversion adapter stub/real MarkItDown in CI profile | PDF/Office/url fetch fixtures | Verifies persisted normalized markdown path is task-linked |
| SC-003: failed conversions leave no partial file + show reason | Deterministic guarantee | Integration test | Fault-injecting conversion adapter + failing fetch stub | Corrupt file, timeout URL fixture | Ensures terminal failed status + no partial normalized artifact |
| SC-004: stage/outcome visible without filesystem/log inspection | Deterministic guarantee | UI integration test | Hub test host + websocket client | Multi-task lifecycle stream fixture | Board is single source for lifecycle visibility |
| SC-005: successful vs failed distinguishable quickly | Deterministic guarantee | Frontend component integration test | Deterministic task stream simulator | Completed/failed card states | Validates semantic styling + reason rendering for failed cards |
| SC-006: `queued` tasks auto-trigger ingest | Deterministic guarantee | Hub integration test | Fake dispatcher process + queue gating harness | Two queued tasks with one running lock | Verifies immediate trigger or wait-then-trigger semantics |
| SC-007: full journey observable end-to-end from board | Deterministic guarantee | End-to-end smoke test | Hub + frontend + fake ingest completion hooks | URL and file submissions | Asserts observed sequence `received->converting->queued->running->terminal` |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `hub.ingest_submissions_total` | Counter | Accepted/rejected ingest-submission requests | `kind=url|markdown|pdf|office`, `outcome=accepted|rejected` |
| `hub.ingest_submission_conversions_total` | Counter | Ingest-submission conversion outcomes | `kind`, `outcome=completed|failed` |
| `hub.ingest_submission_url_fetch_total` | Counter | URL fetch attempts in ingest submission | `outcome=completed|failed`, `failure_type` |
| `hub.ingest_submission_artifacts_persisted_total` | Counter | Stored artifacts by type | `artifact=original|normalized_markdown` |
| `hub.ingest_submission_queue_wait_seconds` | Gauge | Waiting time in queued before ingest run starts | `task_id` |
| `hub.ingest_lifecycle_updates_total` | Counter | Realtime ingest lifecycle events published | `stage` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `ingest.submission.accepted` | INFO | Ingest submission validated and task created | `task_id`, `source_kind`, `submitted_at` |
| `ingest.submission.url_fetch.failed` | WARN | URL fetch fails during ingest submission | `task_id`, `url`, `failure_reason`, `http_status` |
| `ingest.submission.conversion.completed` | INFO | MarkItDown conversion succeeds | `task_id`, `source_kind`, `normalized_path`, `duration_ms` |
| `ingest.submission.conversion.failed` | ERROR | Conversion fails | `task_id`, `source_kind`, `failure_reason` |
| `ingest.submission.original.persisted` | INFO | Original source artifact saved | `task_id`, `original_path`, `size_bytes`, `content_type` |
| `ingest.run.triggered` | INFO | Task transitions `queued->running` by dispatch | `task_id`, `queued_duration_ms` |
| `ingest.lifecycle.published` | INFO | Realtime stage update emitted | `task_id`, `from_stage`, `to_stage` |

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory fields.
3. CI task(s) ensuring those logging tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `hub.ingest_submission.submit` | Root | `task_id`, `source_kind` |
| `hub.ingest_submission.fetch_url` | `hub.ingest_submission.submit` | `task_id`, `url_host`, `http_status` |
| `hub.ingest_submission.store_original` | `hub.ingest_submission.submit` | `task_id`, `original_path`, `size_bytes` |
| `hub.ingest_submission.convert_to_markdown` | `hub.ingest_submission.submit` | `task_id`, `source_kind`, `converter=markitdown` |
| `hub.ingest_submission.store_normalized` | `hub.ingest_submission.submit` | `task_id`, `normalized_path` |
| `hub.ingest_lifecycle.publish_update` | `hub.ingest_submission.submit` | `task_id`, `stage` |
| `hub.ingest_run.trigger` | `hub.ingest_submission.submit` | `task_id`, `dispatcher=child_process` |

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) that create the span with declared parent/child linkage and required attributes.
2. Deterministic integration test task(s) validating span name, parent/child relationship, and correlation attributes.
3. CI task(s) ensuring those trace tests run in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/003-ingest-intake-webui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── ingest-submission-api.md
│   ├── ingest-lifecycle-events.md
│   └── source-artifact-reference.md
└── tasks.md
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.Hub/
│   │   ├── Program.cs
│   │   ├── Submission/
│   │   ├── AgentDispatch/
│   │   └── OperationalState/
│   └── Grimoire.IngestAgent/
└── tests/
    ├── Grimoire.IntegrationTests/
    └── Grimoire.ArchTests/

frontend/
├── src/
│   ├── lib/components/      # reusable UI components (form controls, cards, kanban columns)
│   ├── lib/services/        # API + realtime board client adapters
│   └── routes/
└── tests/
```

**Structure Decision**: Keep the existing backend projects; add ingest-submission API/realtime
adapters in `Grimoire.Hub`; introduce a concrete SvelteKit application layout under
`frontend/src` emphasizing reusable components and shared styling tokens.

Naming note: use mixed terminology from clarification: `ingest` is the umbrella
capability, `ingest submission` names the pre-agent user-facing phase, and `ingest run`
names the agent-owned phase from `queued` onward.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitutional violations requiring justification.

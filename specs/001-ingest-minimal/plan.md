# Implementation Plan: Ingest Minimal

**Branch**: `001-ingest-minimal` | **Date**: 2026-07-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-ingest-minimal/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

A user submits exactly one raw source; a standalone Ingest agent (invoked by the Hub as a
child process) synthesizes it into one primary wiki markdown page (creating or updating
it via its own semantic judgment), while creating and maintaining a task artifact across
the operation's lifecycle, plus an `index.md` entry and a `log.md` line. Failures leave
the wiki untouched and the task artifact clearly marked failed. This is also the
project's first planned feature, so the plan resolves the foundational technology stack
(backend, agent execution model, persistence split, credential scoping, observability
backend) via new ADRs, guided by `docs/decision-context-overview.md`.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (LTS) for both the Hub and the Ingest agent

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR (Hub), Anthropic Claude API
client (Ingest agent's LLM pipeline), `Microsoft.Data.Sqlite` (Hub operational state),
OpenTelemetry .NET SDK (both processes)

**Storage**: Git-tracked markdown files for domain state (wiki pages, task artifacts,
`index.md`, `log.md`); embedded SQLite file for Hub operational task state (outside git)

**Testing**: xUnit + Testcontainers for integration tests (constitution Principle II);
unit tests reserved for the Ingest agent's update-vs-create decision logic (non-trivial
domain judgment); no dedicated unit tests for DTOs/mappers

**Target Platform**: Self-hosted Linux container for the Hub; cross-platform .NET console
app for the Ingest agent (spawned as a child process)

**Project Type**: Web application skeleton (backend Hub now; SvelteKit frontend stack
decided via ADR-001 but not built in this feature — spec leaves the trigger channel open)
plus a standalone agent process

**Performance Goals**: No concurrency target for this MVP; one ingest operation must not
block the Hub from responding to other requests (e.g., task-status reads) while running

**Constraints**: No partial/orphaned wiki writes on failure (FR-008); a Hub restart mid-
ingest must reconcile the task artifact to "failed" with an interruption reason (FR-013);
raw source must remain unmodified (FR-009)

**Scale/Scope**: Single trusted user/developer context (no auth in this slice, per spec
Assumptions); one source per ingest operation; one primary wiki page per source (fan-out
deferred)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Domain Architecture & Strategic DDD | Ubiquitous language (Source, Wiki Page, Task Artifact, Wiki Index, Ingest Log) is fixed in spec.md `## Key Entities` before any code; Domain Core (ingest decision logic: update-vs-create judgment) is isolated from the Hub's HTTP/SignalR adapters and from the agent's process/IO shell | PASS |
| II. Pragmatic Testing Strategy | Integration tests (xUnit + Testcontainers) will cover: Hub↔SQLite operational-state contract, Hub→child-process spawn contract, git working-tree read/write contract. Unit tests reserved for the update-vs-create semantic-judgment logic only | PASS |
| III. ADR-Driven & Test-Enforced Architecture | 5 new ADRs drafted (below) since `docs/adr/` was empty; each introduces a structural boundary not previously covered. Phase 0 structural tests will be the first `tasks.md` entries (Red/Green probes) | PASS (pending ADR acceptance before `/speckit-tasks`) |
| IV. Behavioral & Observable Engineering | `## Observability` below enumerates metrics/logs/spans; no infrastructure introduced beyond what ADR-003 and ADR-005 justify | PASS |

No unjustified violations. No entry required in `## Complexity Tracking`.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

`docs/adr/` was empty prior to this plan. Five new ADRs were drafted to cover the
structural boundaries this feature introduces (tech stack, agent execution, persistence
split, credential scoping, observability backend) — see `research.md` for the rationale
behind each.

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend and Frontend Technology Stack | Hub MUST be C#/.NET 10 (ASP.NET Core + SignalR); confirms no frontend is built in this feature |
| ADR-002 | Ingest Agent Execution Model | Ingest MUST be a standalone .NET console app, invoked by the Hub as a child process with a file-based result contract (task artifact + wiki files written directly by the agent) |
| ADR-003 | Domain vs. Operational State Persistence | Wiki pages/task artifacts/index.md/log.md MUST be git-tracked markdown; in-flight task status MUST live in a Hub-owned SQLite file used for FR-013 restart reconciliation |
| ADR-004 | Credential Scoping for the LLM API Key | The Claude API key MUST be read from a git-ignored local secrets file and injected only into the Ingest child process's environment, never the whole Hub process |
| ADR-005 | Observability Backend (Local and CI) | Both processes MUST emit OpenTelemetry via OTLP; local verification target is the .NET Aspire Dashboard; CI verification uses in-memory exporter assertions |

**New ADR required?**: Yes — all five ADRs above must reach **Accepted** status (author
sign-off) before `/speckit-tasks` is invoked, per constitution Principle III.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `wiki.ingest.operations_total` | Counter | Number of ingest operations attempted | `outcome=completed\|failed` |
| `wiki.ingest.pages_touched_total` | Counter | Number of wiki pages created or updated across ingest operations | `action=created\|updated` |
| `wiki.ingest.duration_seconds` | Histogram | Wall-clock duration of an ingest operation from task creation to terminal status | `outcome=completed\|failed` |
| `wiki.ingest.tasks_reconciled_total` | Counter | Number of "running" tasks reconciled to "failed" on Hub restart (FR-013) | — |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| `ingest.task.created` | INFO | Task artifact created immediately on submission (FR-002) | `task_id`, `source_ref` |
| `ingest.task.status_changed` | INFO | Task artifact transitions status (queued→running→completed/failed) | `task_id`, `from_status`, `to_status` |
| `ingest.page.written` | INFO | A wiki page is created or updated | `task_id`, `page_path`, `action` (`created`/`updated`) |
| `ingest.failed` | ERROR | Ingest operation ends in failure | `task_id`, `reason` |
| `ingest.task.reconciled` | WARN | Hub startup reconciles a stuck "running" task to "failed" (FR-013) | `task_id`, `interruption_reason` |

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|------------|
| `hub.ingest.submit` | root | `task_id`, `source_ref` |
| `hub.ingest.spawn_agent` | `hub.ingest.submit` | `task_id`, `agent=ingest` |
| `ingest_agent.process_source` | `hub.ingest.spawn_agent` (cross-process link via task_id) | `task_id` |
| `ingest_agent.decide_page_target` | `ingest_agent.process_source` | `task_id`, `decision=create\|update` |
| `ingest_agent.write_wiki_page` | `ingest_agent.process_source` | `task_id`, `page_path` |
| `ingest_agent.update_index` | `ingest_agent.process_source` | `task_id`, `category` |
| `ingest_agent.append_log` | `ingest_agent.process_source` | `task_id`, `outcome` |

## Project Structure

### Documentation (this feature)

```text
specs/001-ingest-minimal/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
# Option 2: Web application, per ADR-001 (backend Hub now, frontend stack fixed but not
# built in this feature) + a standalone agent process, per ADR-002

backend/
├── src/
│   ├── Grimoire.Hub/                # ASP.NET Core host: HTTP submission endpoint,
│   │   ├── Submission/              #   SignalR hub (wired later), operational state
│   │   ├── OperationalState/        #   (SQLite per ADR-003), agent process spawning
│   │   └── AgentDispatch/           #   (ADR-002), OTel wiring (ADR-005)
│   ├── Grimoire.Domain/             # Dependency-free Core Domain (constitution Principle I):
│   │   └── Ingest/                  #   Task/WikiPage entities, update-vs-create judgment
│   └── Grimoire.IngestAgent/        # Standalone console app (ADR-002): LLM pipeline,
│                                     #   writes wiki pages/task artifacts/index.md/log.md
│                                     #   directly to the git working tree
└── tests/
    ├── Grimoire.ArchTests/          # Phase 0 structural boundary tests (constitution III)
    ├── Grimoire.IntegrationTests/   # Testcontainers-based: Hub↔SQLite, Hub→agent spawn,
    │                                #   git working-tree read/write contracts
    └── Grimoire.Domain.UnitTests/   # Update-vs-create judgment logic only

frontend/                            # Stack fixed by ADR-001 (SvelteKit); scaffolding and
                                      # screens are out of scope for 001-ingest-minimal

wiki/                                 # Content root (git-tracked), name configurable via
                                      #   Grimoire.Hub's ContentRootDirName setting / --content-root
├── index.md                         #   Wiki index, category-grouped
├── log.md                           #   Append-only ingest log
├── pages/                           #   Wiki pages (one primary page per ingest)
└── tasks/                           #   One task artifact per operation
```

**Structure Decision**: Web-application layout (Option 2) split into a `backend/`
solution with three projects — `Grimoire.Hub` (orchestrator, adapters), `Grimoire.Domain`
(dependency-free Core Domain per constitution Principle I), and `Grimoire.IngestAgent`
(standalone process per ADR-002) — plus a `frontend/` placeholder whose stack is fixed
(ADR-001) but not implemented here. Domain content (`index.md`, `log.md`, `pages/`,
`tasks/`) lives together under a single content-root directory at the repository root,
git-tracked, separate from the `backend/`/`frontend/` code trees, per ADR-003's
domain/operational state split. The content-root directory name defaults to `wiki` but is
configurable (Hub `ContentRootDirName` appsettings.json key, overridable per-invocation via
`--content-root`); the Hub resolves it to absolute paths and passes them to the Ingest
agent, which remains unaware of the setting.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — this section is intentionally empty.

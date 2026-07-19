# Implementation Plan: Hexagonal Architecture Alignment & Task Detail Markdown View

**Branch**: `006-hexagonal-arch-tasks-ui` | **Date**: 2026-07-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-hexagonal-arch-tasks-ui/spec.md`

## Summary

Two coupled increments. First, align the codebase with the constitution v1.4.0 hexagonal
rules: keep the two existing ports (`IModelClient`, `IAgentProcessLauncher`), add two new
ones (`IMarkdownConverter`, `IUrlContentFetcher`), move all four production adapters into
context-nested `<Consumer>.Adapters.<System>` namespaces (co-located with the port they
implement), fix the direct `SubmissionService →
AgentProcessHost` reference, and enforce everything with new NetArchTest containment
rules (ADR-010), each proven by a Red/Green probe — with zero behavioral change. Second,
replace the task card's raw-JSON "Details" target with a SvelteKit route `/tasks/[taskId]`
that renders the per-task markdown record (parsed frontmatter as a metadata header,
`marked`+`dompurify`-rendered body) and live-updates via a new `taskRecordChanged`
SignalR event published by a debounced Hub-side `TasksDir` watcher.

## Technical Context

**Language/Version**: C# / .NET 10 (Hub, IngestAgent, tests — per ADR-001, `net10.0` in `backend/Directory.Build.props`); TypeScript 6 + Svelte 5 / SvelteKit 2 (frontend)

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR, Anthropic .NET SDK, OpenTelemetry .NET, NetArchTest.Rules, xUnit; frontend: @microsoft/signalr, Tailwind 4, Vitest (browser, Playwright); new: `marked`, `dompurify`

**Storage**: Plain-file domain state (`<data>/wiki/tasks/*.md` task records); SQLite operational store (ADR-003/ADR-009) — unchanged by this feature

**Testing**: `dotnet test` (arch, domain unit, hermetic integration with in-memory OTel exporter), `npm test` (Vitest browser component tests)

**Target Platform**: Local dev on macOS, Linux server/container deployment (ADR-009 path configuration)

**Project Type**: Web application (backend Hub + agent worker + SvelteKit frontend)

**Performance Goals**: Record change visible in an open detail view ≤ 5 s end-to-end (SC-005); watcher debounce 300 ms per task

**Constraints**: Zero behavioral change to existing contracts (SC-003); hermetic tests without LLM keys/network (SC-002); no new infrastructure (Principle IV); torn-read-free record serving (FR-011, existing atomic-rename discipline)

**Scale/Scope**: Solo-operator instance; single-digit concurrent board viewers; four external-system boundaries; ~5 new arch rules, 1 endpoint, 1 SignalR event, 1 route

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
| --- | --- | --- |
| I — Strategic DDD & hexagonal boundaries | Feature exists to close the Principle I gap: ports for all four replaceable external systems, port ownership on consumer side, adapter containment, persistence exemption respected, no new assemblies. | PASS (this is the remediation) |
| I — New boundaries via ADR | No new external system introduced; the namespace scheme itself is fixed by new ADR-010. | PASS |
| II — Pragmatic testing | Harness contracts (record API, watcher/event, arch rules) tested hermetically; no unit tests for pass-through adapters; no mocking of persistence (file-based fixtures in real temp dirs). No agent-behavior change ⇒ no evaluation tests needed. | PASS |
| III — ADR-driven & test-enforced | All ADRs read (001–009); ADR-010 drafted this phase and MUST be Accepted before `/speckit-tasks`; every new rule ships with a Red/Green probe; first task in tasks.md will be the structural tests. | PASS (pending ADR-010 acceptance) |
| IV — Behavioral & observable | Observability section below enumerates metrics/logs/spans with the mandatory tasks.md derivation rules; arch rules run in the standard PR pipeline; no new infrastructure (reuses SignalR hub, filesystem, existing OTel wiring). | PASS |
| V — Agentic core & deterministic harness | Harness-only feature: serving/rendering/observing the record involves zero wiki-content judgment; agent instructions untouched; guarded tool boundary untouched. | PASS |

**Post-design re-check (after Phase 1)**: PASS — design artifacts introduce no tactical
DDD outside the Domain core, no new infrastructure, no content judgment in backend code,
and no port for persistence stores (exemption honored).

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.* (All of
ADR-001 … ADR-009 read; ADR-010 drafted as part of this plan.)

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-001 | Backend/Frontend Tech Stack | .NET + SvelteKit + SignalR fixed; the live-update channel MUST reuse SignalR (no second transport); frontend additions stay in the existing SvelteKit app. |
| ADR-002 | Ingest Agent Execution Model | Hub↔agent child-process contract (CLI args, file artifacts, exit codes) must not change; each process keeps owning its artifact I/O — the watcher only observes files, never writes. |
| ADR-003 | Domain vs. Operational State | Task records stay plain-file, git-diffable domain state; no record content moves into SQLite; watcher/read model add no new store. |
| ADR-004 | Credential Scoping | Untouched; moving `AgentProcessHost`/`LocalSecretsLoader` must preserve per-agent env scoping exactly. |
| ADR-005 | Observability Backend | New signals exported via existing OTel wiring; CI verification via in-memory exporter assertions. |
| ADR-006 | Agent Tool Loop & Guarded Boundary | `IModelClient` port shape is fixed by this ADR; moving `AnthropicModelClient` must not alter the loop contract or guardrail seams. |
| ADR-007 | Agent Instruction Surface | Untouched; no instruction-file changes (harness-only feature). |
| ADR-008 | Event Channel & Run Supervision | `taskRecordChanged` follows the established event-shape conventions on the existing lifecycle hub; NDJSON stdout channel unchanged. |
| ADR-009 | Runtime Path Configuration | `TasksDir` comes from `ResolvedGrimoirePaths` only; the watcher and record endpoint MUST NOT re-derive paths. |
| ADR-010 | Hexagonal Ports & Adapter Namespaces (drafted, **proposed**) | Defines the port inventory (P1–P4), adapter namespaces, containment rules (C1–C5), and composition-root/persistence exemptions this feature implements. |

**New ADR required?**: Yes — ADR-010 drafted at
[docs/adr/ADR-010-hexagonal-ports-adapter-namespaces.md](../../docs/adr/ADR-010-hexagonal-ports-adapter-namespaces.md).
It MUST reach **Accepted** status before `/speckit-tasks` is invoked.

## Agentic Boundary (Constitution Principle V)

No agentic surface — harness-only feature. All capabilities below are harness-side:

| Capability | Side | Where it lives |
| --- | --- | --- |
| Ports/adapters restructuring & arch rules | Harness | `backend/src/**`, `backend/tests/Grimoire.ArchTests` |
| Task record read model & API | Harness | `Grimoire.Hub.IngestSubmission` (endpoint + parsing) |
| Record change watching & `taskRecordChanged` publish | Harness | `Grimoire.Hub.Realtime` |
| Markdown rendering & detail route | Harness (UI) | `frontend/src/routes/tasks/[taskId]`, `TaskRecordView` component |

The record's *content* remains agent judgment written under instruction files; this
feature only displays it and MUST NOT template, rewrite, or classify it in backend code.

## Test Strategy

*Every success criterion maps to its primary verification method.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| SC-001 arch rules cover Principle I, Red/Green-proven | Deterministic guarantee | NetArchTest structural tests (Phase 0) | none | temporary violation probes (deleted after Red) | One test per rule C1–C5 + port-presence; probe protocol in quickstart §1 |
| SC-002 hermetic suite: zero LLM/network | Deterministic guarantee | Hermetic integration tests | `FakeModelClient`, `FakeAgentProcess`, `FakeMarkdownConverter`, `FakeUrlContentFetcher` | scripted fake transcripts (existing) | New fakes implement the new ports; offline CI run proves it |
| SC-003 zero regression | Deterministic guarantee | Full existing suite re-run | as today | as today | Move-only refactor; existing contract tests unchanged |
| SC-004 record rendered / placeholder | Deterministic guarantee | Hermetic integration test (API) + Vitest component test (view) | in-proc Hub host; fetch stub in Vitest | valid v2 record file, missing file, torn/malformed frontmatter file | API: 200-shape & 404; UI: rendered DOM vs placeholder |
| SC-005 change visible ≤ 5 s | Deterministic guarantee | Hermetic integration test (watcher → SignalR event) + component test (event → refetch) | SignalR test client; temp `TasksDir` | atomic-rename write sequence fixture | Asserts event within budget after file rename; UI refetch on event & on reconnect |
| SC-006 full history readable in view | Deterministic guarantee | Vitest component test | fetch stub | representative full-lifecycle record fixture | Asserts metadata header + all body sections rendered, no raw frontmatter |

## Observability

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `hub.task_record_reads_total` | Counter | Task-record API reads | `outcome=ok\|missing\|unparseable` |
| `hub.task_record_change_events_total` | Counter | `taskRecordChanged` events published | — |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `task_record.served` | INFO | Task-record API request completed | `task_id`, `outcome`, `content_length` |
| `task_record.change_published` | INFO | Debounced record change published to hub | `task_id`, `event_id`, `changed_at` |
| `task_record.watch_failed` | WARN | Watcher IO/handle failure (before self-restart) | `path`, `reason` |

**Derivation rule (MANDATORY)**: Every row above maps to tasks.md work in all three
categories: implementation (stable event name + mandatory fields), deterministic
integration tests (name, level, fields per trigger), and CI enforcement (tests run in the
standard PR pipeline).

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `hub.task_record.serve` | ASP.NET Core request span | `task_id`, `outcome` |
| `hub.task_record.publish_change` | root (watcher-initiated) | `task_id`, `event_id` |

**Derivation rule (MANDATORY)**: Every row above maps to tasks.md work in all three
categories: span implementation with declared parentage/attributes, deterministic
integration tests (span name, parent/child linkage, correlation attributes incl.
`task_id`), and CI enforcement in the standard PR pipeline. Logs/metrics above are
emitted within these span contexts and correlate via `task_id` (Principle IV).

## Project Structure

### Documentation (this feature)

```text
specs/006-hexagonal-arch-tasks-ui/
├── plan.md              # This file
├── research.md          # Phase 0 (7 decisions)
├── data-model.md        # Phase 1 (TaskRecord, event, ports inventory)
├── quickstart.md        # Phase 1 (validation guide)
├── contracts/
│   ├── task-record-api.md
│   ├── task-record-changed-event.md
│   └── ports-and-adapters.md
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.Hub/
│   │   ├── AgentDispatch/           # IAgentProcessLauncher port (+ RunToExitAsync), coordinator
│   │   │   └── Adapters/
│   │   │       └── AgentProcess/    # AgentProcessHost, LocalSecretsLoader (moved)
│   │   ├── IngestSubmission/        # IMarkdownConverter & IUrlContentFetcher ports (new),
│   │   │   │                        #   pipeline on ports, task-record endpoint + read model
│   │   │   └── Adapters/
│   │   │       ├── MarkItDown/      # MarkItDownConverter, MarkItDownOptions (moved)
│   │   │       └── HttpFetch/       # UrlContentFetcher (moved)
│   │   ├── Realtime/                # TaskRecordWatcher (new), taskRecordChanged publish
│   │   └── Conversion/              # remaining non-adapter conversion types (registry, stores)
│   └── Grimoire.IngestAgent/
│       └── AgentCore/               # IModelClient port (unchanged), loop
│           └── Adapters/
│               └── Anthropic/       # AnthropicModelClient (moved)
└── tests/
    ├── Grimoire.ArchTests/          # + C1–C5 containment & port rules (first tasks)
    └── Grimoire.IntegrationTests/   # + Fakes/FakeMarkdownConverter, Fakes/FakeUrlContentFetcher,
                                     #   task-record API/watcher/observability tests

frontend/
└── src/
    ├── lib/
    │   ├── components/TaskRecordView.svelte        # metadata header + rendered markdown
    │   ├── services/ingestSubmissionsApi.ts        # + getTaskRecord()
    │   ├── services/ingestLifecycleClient.ts       # + onTaskRecordChanged()
    │   └── types.ts                                # + TaskRecord, TaskRecordChangedEvent
    └── routes/tasks/[taskId]/+page.svelte          # detail route (replaces raw-JSON link)
```

**Structure Decision**: Web application layout (existing `backend/` + `frontend/`).
Backend changes are namespace moves plus one new hosted service and one endpoint group;
frontend adds one route, one component, and service extensions. No new projects or
assemblies (ADR-010).

## Complexity Tracking

No constitution violations to justify — table intentionally empty.

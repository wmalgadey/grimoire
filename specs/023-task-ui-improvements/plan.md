# Implementation Plan: Task Visibility & Recovery Improvements

**Branch**: `023-task-ui-improvements` | **Date**: 2026-08-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-task-ui-improvements/spec.md`

## Summary

Six improvements to ingest-task visibility and recovery: (1) a clickable source link in the task detail (URL sources link directly; local-file sources are served read-only by the Hub from the persisted raw copy), (2) a human-readable task label extracted deterministically from the normalized source content with a filename/URL fallback, (3) a persisted, ordered status history displayed as a "path" in the task detail, (4) bounded automatic reactivation with increasing backoff when a run's liveness window is exceeded (replacing immediate permanent failure), (5) removal of the redundant status badge from board cards, and (6) manual restart of a finally-failed task from the UI with concurrency protection. Items 3, 4, and 6 change the run-supervision lifecycle decided in ADR-008 and therefore require a new ADR (ADR-025, amending ADR-008) to reach Accepted status before `/speckit-tasks`.

## Technical Context

**Language/Version**: C# / .NET 10 (backend Hub), TypeScript / SvelteKit (frontend)

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR (ADR-001), Spectre.Console.Cli (ADR-020), Microsoft.Data.Sqlite (ADR-003), OpenTelemetry .NET SDK (ADR-005); frontend: SvelteKit, vitest browser mode with Playwright chromium

**Storage**: Markdown task artifacts at `<MemoryDir>/tasks/{taskId}.md` (ADR-024); raw originals + manifest sidecars under `<DataDir>/raw/` (`RawStoragePaths`); Hub-owned SQLite operational-state DB (`OperationalStateRepository`) — this feature adds an append-only `ingest_status_history` table and extends the source-artifact manifest with title metadata

**Testing**: xUnit — `Grimoire.IntegrationTests` (Integration tier), `Grimoire.ArchTests` + `Grimoire.Domain.UnitTests` (Fast tier) per ADR-021; frontend: vitest browser mode, colocated `*.svelte.test.ts` / `page.test.ts`

**Target Platform**: Local-first operator machine / devcontainer (ADR-019); Hub process serves HTTP + SignalR; browser UI

**Project Type**: Web application (backend `backend/src/Grimoire.Hub` + frontend `frontend/`)

**Performance Goals**: Board and detail views update via existing SignalR push (no polling); source-content endpoint streams the raw file; no new numeric targets — single-operator scale

**Constraints**: Deterministic tests MUST NOT use `Task.Delay`/`Thread.Sleep` outside `PollAsync`/`TimingDependent` (ADR-021) — backoff scheduling MUST be testable via an injectable `TimeProvider`; all filesystem paths resolve through the single composition point (ADR-022/ADR-024, rules R2/M2); classicist state-based tests only, hand-rolled fakes on existing ports (Principle II); frozen platform OTel identities MUST NOT change (ADR-013)

**Scale/Scope**: Single operator, dozens of tasks; 2 new HTTP endpoints, 1 new SQLite table, 1 manifest extension, ~4 frontend components touched

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
| --- | --- | --- |
| I — Domain/Hexagonal | No new external system → no new port. Source serving, history persistence, and title metadata are persistence/local-filesystem concerns (port-exempt per Principle I, containment-bound per ADR-10 C1–C5). Reactivation re-uses the existing `IAgentProcessLauncher` port. No infrastructure package enters domain/orchestration namespaces. | PASS |
| II — Pragmatic Testing | All success criteria are deterministic harness guarantees (no agent-judgment thresholds — see Test Strategy). Verification via integration tests against real infrastructure: real temp filesystems, real SQLite file, real HTTP hosting (TestServer), `FakeAgentProcessLauncher` (existing hand-rolled port fake), `FakeTimeProvider` (concrete fake for the .NET `TimeProvider` abstraction — not a mocking framework). No mocking frameworks. | PASS |
| III — ADR-driven | All 24 ADRs read (see next section). New ADR-025 drafted as part of this plan (amends ADR-008; bidirectional headers + index updated in the same change). ADR-025 MUST reach Accepted before `/speckit-tasks`. ADR-025 introduces **no new Boundary Rule** — its rules are Feature-Scoped Invariants covered by classicist behavioral tests; Phase 0 will state this explicitly. | PASS (gate: ADR-025 acceptance pending) |
| IV — Observability | Full signal enumeration below; every log/span row derives the three mandatory task categories in tasks.md. Contract tests use production telemetry wiring (`AddHubTelemetry` + in-memory exporters on TestServer, the `HubRequestTracingTests` pattern) — not test-only providers. | PASS |
| V — Agentic boundary | Harness-only feature. Title extraction is deterministic display metadata (filename/heading), not wiki-content judgment; reactivation/restart are lifecycle orchestration; no instruction-file content is asserted by any test. | PASS |

## Architectural Constraints & ADRs

*GATE: All ADRs in `docs/adr/` read before completing this section (24 ADRs + index, all currently Accepted; supersession chains respected — path rules read from ADR-022 as amended by ADR-024, not ADR-009).*

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-001 | Backend/Frontend Tech Stack | New endpoints are Minimal APIs on the existing Hub; live updates flow over the existing `/hubs/ingest-lifecycle` SignalR channel, no polling. |
| ADR-002 | Ingest Agent Execution Model | Agent runs as spawned child process per task; its explicit deferral of retry/backoff ("acceptable while one operator…") is revoked by this feature — ADR-025 cites it. Task artifact remains agent-writable; Hub MUST NOT contend on it for new metadata (title and history live in Hub-owned stores instead). |
| ADR-003 | Domain vs. Operational State Persistence | Status history is Hub operational bookkeeping → append-only table in the existing Hub-owned SQLite operational DB, not markdown, and not agent-reachable. |
| ADR-005 | Observability Backend | New signals use OTel SDK + in-memory exporter assertions in CI; naming follows existing `wiki.ingest.*` / `hub.*` / `ingest.*` conventions. |
| ADR-008 | Event Channel, Run Supervision, Persistent Run Queue | **Directly changed.** ADR-008 makes liveness silence the sole, immediately-terminal failure authority and treats terminal states as one-way. Reactivation (bounded retries) and manual restart (failed → queued re-entry under the same task id) amend these decisions → ADR-025. Queue-advance timing and supervision re-arming change accordingly. |
| ADR-010 | Hexagonal Ports & Adapter Namespaces | Source serving/history/title are local-filesystem+persistence (port-exempt); containment rules C1–C5 still bind — no `Microsoft.Data.Sqlite` outside persistence namespaces, no path literals. Reactivation re-launches via the existing `IAgentProcessLauncher` port. |
| ADR-013 | Unified Agent Platform Packaging & Naming | Frozen OTel identities: new Hub-side signals only; no changes to agent-emitted telemetry identities. |
| ADR-018 | Human-Authorized Remediation Execution | Precedent for restart concurrency: CAS on a persisted row under the coordinator slot lock resolves duplicate-restart races deterministically. |
| ADR-020 | Hub CLI Command Surface (as amended by 022, 023) | Restart is implemented as a coordinator method; the HTTP endpoint is a thin wrapper, preserving the shared-coordinator parity pattern. No new CLI command is added in this feature (the spec requires UI access; `ingest-retrigger` keeps its existing queue-resume meaning and is NOT conflated with restart). SQLite WAL/busy-tolerance conventions apply to the new table. |
| ADR-021 | Test Tier Taxonomy & Deterministic-Wait Enforcement | Backoff and liveness tests are deterministic via injectable `TimeProvider`; no `Task.Delay` outside `PollAsync`; new tests carry correct tier placement (Integration project). |
| ADR-022 (amended by ADR-024) | Minimal Directory Configuration Surface / Memory Directory Root | All paths (raw originals, normalized markdown, task artifacts, SQLite DB) resolve through `GrimoirePathResolver`/`ResolvedGrimoirePaths`; no ambient discovery, no path literals (R2/M2). Source serving reads ONLY the persisted `RawDir` copy — never arbitrary operator-disk paths. |

**New ADR required?**: **Yes** — `docs/adr/ADR-025-ingest-task-lifecycle-reentry.md` (drafted as part of this plan, status `proposed`), amending ADR-008. It decides: (a) liveness interruption becomes a recorded, non-terminal event with bounded automatic reactivation under increasing backoff (revoking ADR-002's retry deferral); (b) manual restart re-enters a finally-failed task into the queue under the same task id, arbitrated by CAS under the slot lock (ADR-018 idiom); (c) status history is an append-only Hub-owned SQLite record of every transition, including interruption/reactivation/restart entries. All rules tagged **Feature-Scoped Invariant** (no new dependency-direction Boundary Rule). ADR-008's status header and `docs/adr/index.md` are updated bidirectionally in the same change. **ADR-025 MUST be Accepted before `/speckit-tasks`.**

## Agentic Boundary (Constitution Principle V)

Harness-only feature — no agentic surface. For the avoidance of doubt:

| Capability | Side | Where it lives |
| --- | --- | --- |
| Title extraction (first markdown H1 / filename / URL fallback) | Harness | Hub ingest pipeline (deterministic display metadata, not wiki-content judgment) |
| Status-history recording & display | Harness | Hub lifecycle publisher + SQLite + frontend detail view |
| Liveness reactivation with backoff | Harness | `IngestRunCoordinator` (re-launch via existing `IAgentProcessLauncher` port) |
| Manual restart endpoint + UI action | Harness | Hub coordinator method + Minimal API + frontend |
| Source-content serving | Harness | Hub read-only endpoint over persisted `RawDir` copy |

No instruction files are added or changed; no test asserts instruction-file content.

## Test Strategy

*Every spec success criterion maps to its primary verification method. All criteria are deterministic harness guarantees — this feature has no agent-judgment thresholds, so no evaluation tests are required (the completeness audit in tasks.md must still confirm this).*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| SC-001 source link works (URL + file) | Deterministic | Hermetic integration (TestServer) + frontend component test | Real temp `DataDir` with raw original + manifest; no doubles | URL submission fixture; file submission with known original | Detail response carries link info; source endpoint streams the raw copy with manifest content-type |
| SC-002 unresolvable source shows "unavailable" | Deterministic | Hermetic integration + frontend component test | Real temp dirs (original deleted after submission) | Fixture with removed raw file | Endpoint 404s; detail marks source unavailable; UI renders indicator, no anchor |
| SC-003 human-readable label everywhere | Deterministic | Hermetic integration (board + detail endpoints) + frontend tests | Real conversion output in temp dirs | Markdown with H1; markdown without H1 (filename fallback); URL source | Title extracted at normalization; fallback chain asserted state-based |
| SC-004 full ordered status history in detail | Deterministic | Hermetic integration (full lifecycle) | `FakeAgentProcessLauncher` (existing port fake), real SQLite file | Lifecycle fixture driving received→…→completed and →failed | History rows appended at the lifecycle-publisher choke point; detail endpoint returns ordered list |
| SC-005 liveness interruption recorded distinctly | Deterministic | Hermetic integration (supervision) | `FakeAgentProcessLauncher` gone-silent mode; `FakeTimeProvider` | Silent-run fixture | Interruption entry + reactivation entries + final failure after exhaustion all asserted from history |
| SC-006 no status badge on cards; column conveys status | Deterministic | Frontend component tests (vitest browser) | none | Board fixture with tasks in each stage | `TaskCard` renders no `StatusBadge`; `KanbanColumn` header still names the stage |
| SC-007 one-action restart of failed task | Deterministic | Hermetic integration + frontend test | `FakeAgentProcessLauncher`, real SQLite | Finally-failed task fixture | POST restart → task re-queued under same id; history preserved + appended |
| SC-008 restart rejected unless failed; single winner on races | Deterministic | Hermetic integration (concurrency) | Real SQLite (CAS under slot lock) | Concurrent duplicate-restart harness; non-failed-status fixtures | Exactly one restart takes effect; others get conflict; state-based assertions on history + queue |

Doubles policy: only existing hand-rolled port fakes (`FakeAgentProcessLauncher`) plus a concrete `FakeTimeProvider` for the .NET `TimeProvider` abstraction. No mocking frameworks. All backend tests live in `Grimoire.IntegrationTests` (Integration tier); frontend tests colocated per component/route.

## Observability

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `wiki.ingest.reactivations_total` | Counter | Automatic reactivation attempts after liveness interruption | `outcome` ∈ {`attempted`, `exhausted`} |
| `wiki.ingest.restarts_total` | Counter | Manual restart requests for finally-failed tasks | `outcome` ∈ {`accepted`, `rejected`} |
| `hub.source_content_reads_total` | Counter | Source-content endpoint serves of the persisted raw copy | `result` ∈ {`served`, `not_found`} |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `ingest.run.liveness_interrupted` | WARN | Liveness window exceeded, reactivation still available | `task_id`, `attempt`, `next_delay_seconds` |
| `ingest.run.reactivated` | INFO | Agent process re-launched for an interrupted task | `task_id`, `attempt` |
| `ingest.run.reactivation_exhausted` | ERROR | Bounded attempts used up → final failure | `task_id`, `attempts` |
| `ingest.task.restarted` | INFO | Manual restart accepted; task re-queued | `task_id` |
| `ingest.task.restart_rejected` | WARN | Restart refused (not failed / concurrent duplicate) | `task_id`, `current_status` |
| `ingest.source.served` | INFO | Source-content endpoint streamed the raw copy | `task_id`, `content_type` |

The existing `ingest.run.liveness_failed` event remains and is emitted only at final failure (after exhaustion), preserving its current meaning.

**Derivation rule (MANDATORY)**: Every row above MUST map to tasks.md work in all three categories: (1) implementation with stable event name + mandatory fields, (2) deterministic integration tests validating name/level/fields, (3) CI tasks ensuring these tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `ingest_hub.reactivation` | root (links `task_id` correlation; emitted when scheduling/executing a reactivation) | `task_id`, `attempt`, `delay_seconds` |
| `hub.ingest_task.restart` | ASP.NET Core request span | `task_id`, `outcome` |
| `hub.ingest_source.serve` | ASP.NET Core request span | `task_id`, `result` |

All spans come from the existing `Grimoire.Hub` ActivitySource; logs/metrics are emitted within active span context and correlate via `task_id`. Contract tests MUST obtain signals through the production composition root (`AddHubTelemetry` + in-memory exporter on TestServer — the `HubRequestTracingTests` pattern), never a test-only provider; the request-span parentage rows above exist precisely because of the sampler failure documented in Principle IV.

**Derivation rule (MANDATORY)**: Every span row MUST map to tasks.md work in all three categories (implementation with declared parentage/attributes, deterministic integration tests, CI enforcement).

## Project Structure

### Documentation (this feature)

```text
specs/023-task-ui-improvements/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── http-api.md      # Endpoint deltas + new endpoints
│   └── signalr-events.md# Lifecycle event additions
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/Grimoire.Hub/
├── IngestSubmission/
│   ├── IngestSubmissionEndpoints.cs      # detail/board responses gain title, history, sourceLink; new restart + source endpoints
│   ├── IngestSubmissionPipeline.cs       # title extraction at normalization; original filename persisted to manifest
│   └── (KanbanBoardProjectionStore.cs)   # board title from manifest title (fallback chain)
├── IngestDispatch/
│   └── IngestRunCoordinator.cs           # reactivation with backoff (TimeProvider), RestartAsync coordinator method
├── OperationalState/
│   └── OperationalStateRepository.cs     # ingest_status_history append-only table (+ attempt tracking)
├── Realtime/
│   └── IngestLifecyclePublisher.cs       # history recording at the transition choke point
└── Observability (HubMetrics.cs, IngestSubmissionLogEvents.cs)  # new signals per Observability section

backend/tests/Grimoire.IntegrationTests/  # all new backend tests (Integration tier)
backend/tests/Grimoire.ArchTests/         # unchanged — Phase 0 declares no new Boundary Rule

frontend/src/
├── lib/components/TaskCard.svelte        # remove StatusBadge; render title
├── lib/components/TaskRecordView.svelte  # source link / unavailable indicator
├── lib/components/StatusHistoryPath.svelte  # NEW: ordered status "path"
├── lib/types.ts                          # BoardTask/TaskDetail additions (title, history, sourceLink)
├── lib/services/ingestSubmissionsApi.ts  # restart call, source URL helper
└── routes/tasks/[taskId]/+page.svelte    # title as heading (uid secondary), history, restart button

docs/adr/
├── ADR-025-ingest-task-lifecycle-reentry.md  # NEW (proposed → must be Accepted pre-tasks)
├── ADR-008-agent-event-channel-run-supervision.md  # status header: Amended by ADR-025
└── index.md                              # ADR-025 row + chain update
```

**Structure Decision**: Web application layout already in place (`backend/` + `frontend/`); the feature only extends existing namespaces listed above — no new projects, assemblies, or adapter namespaces.

## Complexity Tracking

No constitution violations to justify — table intentionally empty.

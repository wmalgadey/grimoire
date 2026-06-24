# Implementation Plan: Ingest Agent & Web-UI Channel

**Branch**: `004-ingest-agent-webui` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-ingest-agent-webui/spec.md`

---

## Summary

Build the Ingest Agent as a standalone .NET 9 process in `src/agents/ingest/` that watches `raw/sources/`, processes files through an LLM-driven pipeline (Claude SDK: chunk → analyze → index), and auto-commits results to Git. Extend the Hub backend with an `Ingest/` domain (upload, trigger, relay endpoints + SignalR hub) and add a Svelte 5 Web-UI page with upload form, real-time status feed, feedback dialog, post-ingest conversation panel, and batch summary.

---

## Technical Context

**Language/Version**: .NET 9 (agent process + backend extensions), TypeScript 6 + Svelte 5 (frontend)

**Primary Dependencies**:
- Agent: `Anthropic.SDK` (Claude LLM), `LibGit2Sharp` (git commits), `System.Data.SQLite` (processing cache), .NET Generic Host + Minimal API (agent HTTP surface)
- Backend: existing stack + `@microsoft/signalr` (already present via Hub), new `IngestHub` SignalR hub
- Frontend: `@microsoft/signalr` (client), Svelte 5 Runes syntax throughout

**Storage**:
- Agent: SQLite DB (`ingest-cache.db`) for `IngestRecord` and `IngestConversation` persistence
- Hub: SQLite DB (existing `grimoire.db`) extended with `IngestRuns` and `ConversationTurns` tables
- Domain output: Markdown files written to `wiki/` with YAML frontmatter, committed via LibGit2Sharp

**Testing**: xUnit + Testcontainers (integration), NetArchTest.Rules (architecture), unit tests for pipeline domain logic only

**Target Platform**: Linux (containerizable via Docker), local dev (dotnet run)

**Project Type**: Standalone process (`src/agents/ingest/`) + Hub domain extension (`src/backend/Grimoire.Api/Ingest/`) + SPA route (`src/frontend/src/`)

**Performance Goals**:
- File detection to processing start: < 30 s (SC-001)
- Feedback dialog visible to user: < 3 s from agent request (SC-007)
- Conversation panel open after processing: < 5 s (SC-010)
- Batch summary visible: < 2 s after run completion (SC-008)

**Constraints**:
- Single active ingest run at any time (enforced in agent and Hub)
- Graceful LLM failure: `IngestRecord.Status = failed`, run continues
- `ANTHROPIC_API_KEY` MUST NOT appear as literal in any source file
- Agent process MUST NOT import from `src/backend/` namespaces

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|---------|
| I. Domain Architecture & Strategic DDD | ✅ Pass | Bounded contexts in spec; Ubiquitous Language defined; `Ingest/` domain folder already scaffolded in backend per ADR-009 |
| II. Pragmatic Testing | ✅ Pass | Integration tests via Testcontainers for all Hub HTTP endpoints; unit tests scoped to pipeline domain logic (Chunker, LlmAnalyzer); no mocked databases |
| III. ADR-First & Test-Driven Architecture | ✅ Pass | ADR-010 accepted before this plan; all ADRs read; first task in tasks.md will be the architecture test enforcing `src/agents/ingest/` isolation |
| IV. Behavioral & Observable Engineering | ✅ Pass | Observability section below enumerates all metrics, log events, and trace spans; CI/CD gates required |

**Post-design re-check**: No new structural boundary introduced beyond ADR-010; no unapproved infrastructure added.

---

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | .NET 9 Minimal API + SignalR | Hub extensions use Minimal API endpoint pattern; dedicated `IngestHub` for real-time ingest streaming |
| ADR-002 | Agent Runtime — Worker Services + Escape-Hatch | `IAgentWorker` for in-process agents; RemoteAgent concept replaced — see ADR-010 |
| ADR-010 | Ingest Agent Standalone + Claude SDK | Standalone process in `src/agents/ingest/`; Hub uses `IngestAgentClient` (typed HttpClient) — no proxy abstraction; amends ADR-002 |
| ADR-003 | Frontend — Svelte 5 + Vite | Ingest UI is Svelte 5 with Runes syntax (`$state`, `$derived`, `$effect`); no class-based components |
| ADR-004 | Channel Abstraction — IChannel | `IngestChannel` implements `IChannel` in `Grimoire.Api/Channels/Ingest/`; routes ingest events (progress, feedback, conversation) to SignalR clients |
| ADR-005 | Monorepo Structure | Agent in `src/agents/ingest/`; Ingest Channel in `src/backend/Grimoire.Api/Channels/Ingest/`; frontend components in `src/frontend/src/components/ingest/`; no mixing of subproject code |
| ADR-006 | Hub-Spoke Orchestration | Hub relays all ingest triggers and results; no direct frontend ↔ agent communication; agent reports progress to Hub via HTTP callback |
| ADR-007 | Storage — Git + Markdown | Processed output written to `wiki/` as Markdown with YAML frontmatter; auto git-commit per run; no SQL writes to wiki/ |
| ADR-008 | SQLite for Hub Ephemera | Hub extends `grimoire.db` with `IngestRuns` and `ConversationTurns` tables; agent maintains its own `ingest-cache.db` |
| ADR-009 | Screaming Architecture | Hub code organized by domain: `Grimoire.Api/Agents/`, `Grimoire.Api/Channels/Ingest/`, `Grimoire.Api/Hubs/`, `Grimoire.Api/Shared/` |
| ADR-010 | Ingest Agent Standalone + Claude SDK | Agent in `src/agents/ingest/`; uses `Anthropic.SDK`; all config via env vars; no `src/backend/` namespace imports |

**New ADR required?**: No — ADR-010 covers the new structural boundary introduced by this feature.

---

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `grimoire.ingest.files_total` | Counter | Files evaluated per run | `status=processed\|failed\|skipped` |
| `grimoire.ingest.chunks_total` | Counter | Chunks produced across all processed files | — |
| `grimoire.ingest.run_duration_ms` | Histogram | End-to-end duration of an ingest run | `status=completed\|failed` |
| `grimoire.ingest.active_run` | Gauge | 1 if a run is in progress, 0 otherwise | — |
| `grimoire.ingest.llm_calls_total` | Counter | Claude SDK calls made | `status=success\|failed\|rate_limited` |
| `grimoire.ingest.conversation_turns_total` | Counter | Total conversation turns across all sessions | — |
| `grimoire.ingest.feedback_requests_total` | Counter | Feedback requests raised | `reason=unknown_format\|oversized\|missing_metadata` |
| `grimoire.ingest.git_commits_total` | Counter | Successful git commits after runs | — |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `ingest.run_started` | INFO | Run begins | `run_id`, `source_dir`, `file_count` |
| `ingest.file_detected` | INFO | File picked up by watcher or trigger | `file_path`, `sha256` |
| `ingest.file_skipped` | INFO | SHA256 cache hit | `file_path`, `reason=cache_hit` |
| `ingest.file_processed` | INFO | File completed successfully | `file_path`, `chunk_count`, `duration_ms` |
| `ingest.file_failed` | ERROR | File processing error | `file_path`, `error`, `stage` |
| `ingest.run_completed` | INFO | Run ends | `run_id`, `processed`, `failed`, `skipped`, `duration_ms` |
| `ingest.llm_call` | INFO | Claude SDK invocation | `file_path`, `chunk_index`, `model`, `input_tokens`, `output_tokens` |
| `ingest.llm_error` | ERROR | Claude SDK failure | `file_path`, `chunk_index`, `error`, `retry_attempt` |
| `ingest.feedback_requested` | INFO | Agent needs user decision | `file_path`, `reason` |
| `ingest.feedback_received` | INFO | User response delivered | `file_path`, `action` |
| `ingest.conversation_turn` | INFO | LLM responds in conversation | `conversation_id`, `turn_index`, `role` |
| `ingest.git_commit` | INFO | Git commit created | `commit_sha`, `file_count`, `chunk_count` |
| `ingest.hub_registered` | INFO | Agent registered with Hub | `hub_url`, `agent_id` |
| `ingest.hub_unavailable` | WARN | Hub unreachable at startup or heartbeat | `hub_url`, `error` |

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `ingest.run` | root | `run_id`, `file_count` |
| `ingest.file.process` | `ingest.run` | `file_path`, `sha256` |
| `ingest.pipeline.chunk` | `ingest.file.process` | `file_path`, `chunk_count` |
| `ingest.pipeline.llm_analyze` | `ingest.file.process` | `chunk_index`, `model`, `input_tokens` |
| `ingest.pipeline.index` | `ingest.file.process` | `file_path`, `output_path` |
| `ingest.git.commit` | `ingest.run` | `file_count`, `commit_sha` |
| `ingest.conversation.turn` | root | `conversation_id`, `turn_index` |
| `ingest.llm.respond` | `ingest.conversation.turn` | `model`, `context_tokens` |
| `hub.ingest.upload` | root | `file_count`, `total_bytes` |
| `hub.ingest.trigger` | root | `run_id` |
| `hub.ingest.relay_feedback` | root | `run_id`, `file_path` |

---

## Project Structure

### Documentation (this feature)

```text
specs/004-ingest-agent-webui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── agent-http-api.md       # Ingest Agent HTTP surface
│   ├── hub-ingest-api.md       # Hub Ingest domain endpoints
│   └── signalr-events.md       # IngestHub real-time event contracts
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code

```text
src/
├── agents/
│   └── ingest/                              # NEW — standalone agent process
│       ├── Grimoire.Ingest.csproj
│       ├── Program.cs                       # Generic Host + embedded Minimal API
│       ├── Pipeline/
│       │   ├── IngestPipeline.cs            # Orchestrates chunk→analyze→index per file
│       │   ├── Chunker.cs                   # Content-type-aware text splitting
│       │   ├── LlmAnalyzer.cs              # Anthropic.SDK: structured metadata extraction
│       │   └── Indexer.cs                   # Writes wiki/ Markdown output
│       ├── Watcher/
│       │   └── SourceWatcher.cs             # FileSystemWatcher + debounce
│       ├── Cache/
│       │   ├── IngestCache.cs               # SHA256 lookup + status read/write
│       │   └── IngestCacheRepository.cs     # SQLite CRUD (ingest-cache.db)
│       ├── Git/
│       │   └── IngestGitService.cs          # LibGit2Sharp: stage + commit
│       ├── Conversation/
│       │   └── ConversationService.cs       # Multi-turn Claude SDK dialogue
│       ├── Hub/
│       │   ├── HubClient.cs                 # HTTP client: register, heartbeat, report
│       │   └── HubReporter.cs               # Posts progress/feedback/conversation to Hub
│       ├── Models/
│       │   ├── IngestRecord.cs
│       │   ├── IngestRun.cs
│       │   ├── FeedbackRequest.cs
│       │   ├── FeedbackResponse.cs
│       │   └── IngestConversation.cs
│       └── Api/
│           ├── TriggerRunEndpoint.cs        # POST /ingest/runs
│           ├── FeedbackEndpoint.cs          # POST /ingest/runs/{id}/feedback
│           ├── ConversationEndpoint.cs      # POST /ingest/conversations/{id}/turns
│           └── HealthEndpoint.cs            # GET /health
│
├── backend/
│   └── Grimoire.Api/
│       ├── Agents/
│       ├── Hubs/
│       ├── Channels/                        # EXTEND — per ADR-004 (IChannel)
│       │   └── Ingest/                      # Ingest channel: Web-UI entry point for file ingestion
│       │       ├── Endpoints/
│       │       │   ├── UploadSourceEndpoint.cs  # POST /api/ingest/upload
│       │       │   ├── TriggerIngestEndpoint.cs # POST /api/ingest/trigger
│       │       │   ├── GetIngestRunEndpoint.cs  # GET /api/ingest/runs/{runId}
│       │       │   └── ConversationEndpoint.cs  # POST /api/ingest/conversations/{id}/messages
│       │       ├── Services/
│       │       │   ├── IngestChannel.cs     # IChannel implementation (ADR-004)
│       │       │   ├── IngestHub.cs         # SignalR hub for real-time ingest events
│       │       │   └── IngestAgentClient.cs # Typed HttpClient — direct HTTP to agent (ADR-010)
│       │       ├── Persistence/
│       │       │   └── IngestRepository.cs  # SQLite CRUD for IngestRuns + Turns
│       │       └── Models/
│       │           ├── IngestRunRecord.cs
│       │           └── ConversationTurnRecord.cs
│       └── Shared/
│
└── frontend/
    └── src/
        ├── components/
        │   └── ingest/
        │       ├── UploadForm.svelte
        │       ├── TriggerButton.svelte
        │       ├── StatusFeed.svelte
        │       ├── FeedbackDialog.svelte
        │       ├── ConversationPanel.svelte
        │       └── BatchSummary.svelte
        ├── pages/
        │   └── IngestPage.svelte            # Composes all ingest components
        └── services/
            └── ingestHub.ts                 # @microsoft/signalr client for IngestHub
```

**Structure Decision**: Three-subproject layout per ADR-005. Agent is a new .NET project in `src/agents/ingest/`. Backend gets Ingest Channel implementation in `Grimoire.Api/Channels/Ingest/` (per ADR-004 and ADR-009 screaming architecture — all Hub-side additions are domain-organized). Frontend gets a new page and component subtree under `src/frontend/src/`.

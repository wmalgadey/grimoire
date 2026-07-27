# Implementation Plan: Interactive Wiki Query Process

**Branch**: `008-query-agent` | **Date**: 2026-07-23 | **Spec**: `specs/008-query-agent/spec.md`

**Input**: Feature specification from `/specs/008-query-agent/spec.md`

## Summary

Add a `/query` capability: the Web UI lets the user submit a free-text Query Prompt,
the Hub dispatches a Query agent run with a dedicated, versioned system prompt, the
answer streams progressively into the UI as it is produced, the user can interrupt an
in-progress answer, and follow-up questions carry the conversation's prior turns as
context. The Query agent is strictly read-only and wiki-scoped (no write capability at
all, no sources beyond wiki content), and runs fully decoupled from the Ingest
single-agent slot under its own configurable concurrency limit (default 3).

Technical approach: extract a shared `Grimoire.AgentRuntime` library from the existing
Ingest agent's loop/model-client/guarded-tool-executor/event-emitter code (per the
project owner's direction that Ingest and Query share one agent loop, differing by
system prompt, with tools/policy free to vary); build a new standalone
`Grimoire.QueryAgent` process on it with a read-only tool registry; extend the existing
ADR-008 NDJSON event channel with a streamed `answer_chunk` event; add a bounded-
concurrency `QueryRunCoordinator` (reject-over-limit, no queue) alongside the existing
single-slot `IngestRunCoordinator`; add a sibling `QueryLifecycleHub`/`QueryLifecyclePublisher`
SignalR channel; write Query Run Artifacts entirely from the Hub (the agent never
writes) under `<base>/data/query-runs/`. Full architectural rationale: ADR-011.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / Svelte 5 (frontend) — matches
the existing `backend/`/`frontend/` split, no new language.

**Primary Dependencies**: ASP.NET Core + SignalR (existing); Anthropic Messages API,
now including its streaming variant (existing dependency, new usage mode); new shared
library `Grimoire.AgentRuntime`; `@microsoft/signalr` (existing frontend dependency).

**Storage**: Markdown files under `<base>/data/query-runs/<conversationId>/<turnId>.md`
for Query Run Artifacts (ADR-009 pattern; no new database, no SQLite table — Query runs
are not queued operational state the way Ingest runs are, R7/ADR-011).

**Testing**: xUnit (`Grimoire.IntegrationTests`, `Grimoire.Domain.UnitTests`) with
`FakeAgentProcess`/`FakeModelClient` doubles; `Grimoire.ArchTests` (NetArchTest +
Mono.Cecil IL scan) for structural rules; `Grimoire.AgentEvals` (feature 007 harness) for
agent-judgment evaluation thresholds; Vitest + Testing Library for frontend components.

**Target Platform**: Same as existing Hub/Ingest agent — cross-platform .NET
console/web processes, local dev and CI; SvelteKit frontend, browser.

**Project Type**: Web application (existing `backend/` + `frontend/` split).

**Performance Goals**: SC-003 — first answer content visible in the UI within 2s (p95)
of the agent producing it, subsequent content within 2s (p95) of production.

**Constraints**: SC-004 — interruption halts answer delivery within 2s; FR-017 —
configurable global query-concurrency limit, default 3, submissions beyond it rejected
immediately (never queued).

**Scale/Scope**: Single-user context (no auth/multi-user separation); one active Query
Conversation per browser window; up to `QueryConcurrencyLimit` concurrent Query Turns
Hub-wide, fully decoupled from Ingest's single-agent slot.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Domain Architecture, Strategic DDD & Hexagonal Boundaries)**: PASS.
  New external-system dependency (spawned Query agent process, streaming model calls)
  is consumed through the existing `IAgentProcessLauncher` port and the `IModelClient`
  port (relocated to the new shared consuming namespace, ADR-011). No tactical DDD
  patterns introduced outside `Grimoire.Domain`. Adapter containment is extended by
  ADR-011 (C6, C7) with Red/Green probes required in Phase 0/tasks.
- **Principle II (Pragmatic Testing Strategy)**: PASS. Harness contracts (dispatch,
  concurrency limiting, guardrail enforcement, artifact writing, event channel) are
  hermetically tested with `FakeAgentProcess`/`FakeModelClient` — no live LLM calls.
  Agent-judgment outcomes (SC-007..SC-010) are evaluation tests with explicit
  thresholds via `Grimoire.AgentEvals`, not reimplemented as deterministic code. See
  Test Strategy below for the full split.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: PASS, conditional on
  ADR-011 (drafted this plan, status `accepted`) and its Red/Green probes being the
  first tasks in `tasks.md` per the constitution's ordering rule.
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new infrastructure
  (reuses OTel/Aspire Dashboard backend from ADR-005, SignalR, markdown files). Full
  Observability section below with metrics/log events/trace spans and their
  logging/trace-contract task-category mapping deferred to `tasks.md`.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS. Answer content,
  grounding, citation style, gap-honesty, and read-only self-explanation are entirely
  governed by `agents/query/system-prompt.md` (agentic core); the Hub owns only
  dispatch, concurrency limiting, guardrail enforcement at the tool boundary, artifact
  persistence, and realtime delivery (harness). See Agentic Boundary table below.

No unjustified violations. Complexity Tracking is not needed.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | Query agent and Hub extensions stay on the existing .NET/SvelteKit stack; no new language/runtime. |
| ADR-002 | Ingest agent execution model | Query agent reuses the standalone-child-process pattern (spawned per Query Turn, CLI args in, event channel + Hub-written artifact out); no in-process business logic in the Hub. |
| ADR-003 | Domain vs. operational state persistence | Query Run Artifacts are operational/harness bookkeeping (not domain content edited in Obsidian), so they live under `<base>/data/`, not `wiki/`, per the same split this ADR establishes. |
| ADR-004 | Credential scoping for the LLM API key | The Query agent child process receives the Claude API key the same way Ingest does — injected only into its own environment at spawn, never the Hub's process environment. |
| ADR-006 | Agent tool-use loop and guarded tool boundary | Query reuses the guarded-tool-executor/deny-by-default-policy pattern with its own tool registry (read-only) and its own `agents/query/policy.json`; the physical guardrail chokepoint is unchanged. |
| ADR-007 | Agent instruction surface | Query gets its own `agents/query/system-prompt.md`, loaded verbatim and fail-closed, SHA-256 recorded per run; no default-user-prompt document is needed (the user's Query Prompt is always supplied per turn, R1 in research.md). |
| ADR-008 | Agent event channel, run supervision, and persistent run queue | Query reuses the NDJSON stdout event channel and liveness-silence supervision, extended with one new `answer_chunk` event type; Query's dispatch is bounded-concurrency (no FIFO queue), a deliberate divergence documented in ADR-011. |
| ADR-009 | Explicit runtime path configuration | Query Run Artifacts and `agents/query/` instruction files are new runtime locations added to the single path-composition point (`GrimoirePathOptions`/`GrimoirePathResolver`), beneath `<base>/data` and `<base>` respectively, following the established pattern — no ambient discovery introduced. |
| ADR-010 | Hexagonal ports and adapter namespaces | `IModelClient`'s port ownership and adapter namespace move to the new shared `Grimoire.AgentRuntime.Core` consumer as amended by ADR-011; `IAgentProcessLauncher` ownership is unchanged. |
| ADR-011 | Query agent shared runtime and concurrency model (new, this plan) | Fixes the shared-loop extraction, streaming mechanism, bounded-concurrency dispatch, interruption mechanism, Query Run Artifact ownership/location, and conversation-context transport for this feature. |

**New ADR required?**: Yes — `docs/adr/ADR-011-query-agent-shared-runtime-and-concurrency-model.md`,
drafted as part of this plan and marked **Accepted** (author sign-off, consistent with
ADR-002 through ADR-010's acceptance convention in this project).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|-----------------|
| Grounding style, citation conventions, tone, honest-gap handling (US1) | Agentic core | `agents/query/system-prompt.md` |
| Declining write-requesting prompts with an explanation (US4, SC-010) | Agentic core | `agents/query/system-prompt.md` |
| Resolving follow-up references against prior turns (US3) | Agentic core | `agents/query/system-prompt.md` (interprets the harness-supplied conversation history) |
| Query Prompt validation (empty/whitespace/max-length) | Harness | Frontend `QueryPromptForm.svelte` + Hub-side re-validation in the turn-submission endpoint |
| Query agent dispatch, concurrency limiting, rejection over limit | Harness | `Grimoire.Hub.QueryDispatch.QueryRunCoordinator` |
| Read-only tool registry (no `write_file` at all) | Harness | `Grimoire.QueryAgent`'s tool registry (structural, not policy-only) |
| Deny-by-default policy enforcement at tool-call time | Harness | `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor` + `agents/query/policy.json` |
| Streaming answer delivery, interruption, terminal-state transitions | Harness | `QueryRunCoordinator`, `RunEventEmitter.EmitAnswerChunk`, `QueryLifecyclePublisher` |
| Query Run Artifact persistence (100% Hub-written) | Harness | New `QueryRunArtifactWriter` (`Grimoire.Hub.QueryRunArtifact`) |
| Conversation context transport (client-supplied prior turns) | Harness | Turn-submission endpoint's request scaffold (harness-owned, non-agent-editable, ADR-007 pattern) |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (system prompt loaded verbatim, hash recorded, fail-closed on missing/empty) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess`, in-memory instruction files | Missing/empty/unreadable `system-prompt.md` fixtures | Mirrors existing `SystemPromptLoader` fail-closed tests for Ingest |
| SC-002 (zero wiki writes, zero out-of-scope reads, all denials recorded) | Deterministic guarantee | Hermetic integration test + structural (ArchTests) | `FakeModelClient` scripted to request `write_file`/out-of-scope `read_file` | Query policy fixture, scripted tool-use sequences | ArchTests C7 Red/Green probe (FR-014) proves no write API is reachable at all, not just policy-denied |
| SC-003 (progressive delivery, first/subsequent content within 2s p95) | Deterministic guarantee | Hermetic integration test against `FakeModelClient` streaming deltas, timed | `FakeModelClient` with scripted delta timing, `FakeAgentProcess` | Scripted delta sequences with injected delays | Asserts `answer_chunk` events reach the SignalR publisher within the budget, not wall-clock LLM latency |
| SC-004 (interruption halts within 2s, preserves partial answer, terminal-state finality) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` (controllable termination), fake clock | Long-running scripted turn + interrupt-mid-stream scenario | Mirrors `IngestRunCoordinator` liveness-failure test idiom, applied to user-triggered `Terminate()` |
| SC-005 (Query Run Artifact fields present, dead runs marked failed within liveness window) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` silent-run scenario, fake clock | N/A | Reuses ADR-008 liveness-window test pattern |
| SC-006 (query submissions never wait on ingest and vice versa, within concurrency limit) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` for both `IngestRunCoordinator` and `QueryRunCoordinator` running concurrently | N/A | Proves the two coordinators share no lock/slot |
| SC-007 (≥90% grounded answers with page references, covered questions) | Agent-judgment threshold | Evaluation run via `Grimoire.AgentEvals` | Real or recorded LLM output (NIM-endpoint judge, feature 007 pattern) | Sampled wiki fixture + question set with known-correct grounding | Threshold 90%, judge rubric scores grounding + citation |
| SC-008 (≥90% honest-gap answers for uncovered questions) | Agent-judgment threshold | Evaluation run via `Grimoire.AgentEvals` | Same as SC-007 | Sampled question set with no wiki coverage | Threshold 90%, judge rubric scores absence-of-fabrication |
| SC-009 (≥90% correctly resolved follow-up dependencies) | Agent-judgment threshold | Evaluation run via `Grimoire.AgentEvals` | Same as SC-007 | Sampled two-turn conversation fixtures (pronoun/reference dependency) | Threshold 90% |
| SC-010 (≥90% of write-requesting prompts declined with explanation) | Agent-judgment threshold | Evaluation run via `Grimoire.AgentEvals` | Same as SC-007 | Sampled write-provoking prompt set | Threshold 90%; SC-002 independently guarantees the write never happens regardless of the answer text |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `query.turns_total` | Counter | Query Turns reaching a terminal state | `outcome=completed\|interrupted\|failed` |
| `query.concurrent_runs` | Gauge | Currently running Query Turns | none |
| `query.answer_chunks_total` | Counter | `answer_chunk` events emitted | none |
| `query.tool_calls_total` | Counter | Guarded tool calls dispatched by the Query agent | `tool`, `decision=allowed\|denied` |
| `query.turn_duration_seconds` | Histogram | Wall-clock duration of a Query Turn | `outcome` |
| `query.submissions_rejected_total` | Counter | Submissions rejected for being over the concurrency limit | none |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| `query.turn.created` | INFO | Turn accepted and dispatched | `conversation_id`, `turn_id` |
| `query.instructions.loaded` | INFO | System prompt + policy loaded | `turn_id`, `system_prompt_sha256`, `policy_version`, `policy_sha256` |
| `query.instructions.load_failed` | ERROR | System prompt missing/unreadable/empty (fail-closed) | `turn_id`, `reason` |
| `query.tool.denied` | WARN | A guarded tool call is denied | `turn_id`, `tool`, `target`, `reason` |
| `query.turn.completed` | INFO | Turn reaches `completed` | `turn_id`, `duration_ms` |
| `query.turn.interrupted` | INFO | Turn reaches `interrupted` | `turn_id` |
| `query.turn.failed` | ERROR | Turn reaches `failed` | `turn_id`, `reason` |
| `query.submission.rejected` | INFO | Submission rejected over concurrency limit | `conversation_id` |
| `query.lifecycle.published` | INFO | A `queryTurnChanged` realtime event is broadcast | `turn_id`, `from_state`, `to_state` |

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md` work
covering implementation, deterministic integration test, and CI-pipeline enforcement
(final phase), per the constitution's logging-contract requirement.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|------------|
| `hub.query.submit` | root (HTTP request) | `turn_id`, `conversation_id` |
| `hub.query.spawn_agent` | `hub.query.submit` | `turn_id`, `agent=query` |
| `hub.query.run_supervision` | root (dispatcher background task) | `turn_id` |
| `hub.query.handle_run_event` | `hub.query.run_supervision` | `turn_id`, `event_type` |
| `hub.query_lifecycle.publish_update` | `hub.query.handle_run_event` or `hub.query.submit` | `turn_id`, `stage` |
| `query_agent.run` | root (agent process) | `turn_id` |
| `query_agent.load_instructions` | `query_agent.run` | `turn_id`, `system_prompt_sha256` |
| `query_agent.model_turn` | `query_agent.run` | `turn_id`, `turn`, `stop_reason` |
| `query_agent.tool_call` | `query_agent.model_turn` | `turn_id`, `tool`, `decision` |
| `query_agent.finalize_artifact` | `query_agent.run` | `turn_id`, `outcome` |

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md` work
covering implementation (span creation with declared parent/child + attributes),
deterministic integration test (validates name/linkage/correlation), and CI enforcement,
per the constitution's trace-contract requirement.

## Project Structure

### Documentation (this feature)

```text
specs/008-query-agent/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/
├── Grimoire.Domain/                          # unchanged (SafetyPolicy stays shared/dependency-free)
├── Grimoire.AgentRuntime/                    # NEW shared library (ADR-011)
│   ├── Core/                                 # AgentLoop, IModelClient, conversation types (moved from Grimoire.IngestAgent.AgentCore)
│   │   └── Adapters/Anthropic/               # AnthropicModelClient (moved)
│   ├── Guardrails/                           # GuardedToolExecutor, WriteJournal, DeniedActionRecord (moved, generalized)
│   ├── RunEvents/                            # RunEventEmitter incl. new answer_chunk (moved, extended)
│   └── Instructions/                         # SystemPromptLoader, PolicyLoader (moved)
├── Grimoire.IngestAgent/                     # unchanged behavior; now references Grimoire.AgentRuntime
├── Grimoire.QueryAgent/                      # NEW standalone process (ADR-002 pattern)
│   ├── Program.cs                            # CLI entry, references Grimoire.AgentRuntime
│   ├── QueryToolRegistry.cs                  # list_files, read_file only — no write_file
│   └── QueryCliOptions.cs
└── Grimoire.Hub/
    ├── QueryDispatch/                         # NEW: QueryRunCoordinator, QueryAgentRequest (Grimoire.Hub.QueryDispatch)
    ├── QueryRunArtifact/                      # NEW: QueryRunArtifactWriter/Store (Grimoire.Hub.QueryRunArtifact)
    ├── QuerySubmission/                       # NEW: turn-submission endpoint + validation
    └── Realtime/                              # + QueryLifecycleHub, QueryLifecyclePublisher (sibling additions)

backend/tests/
├── Grimoire.ArchTests/                        # + rules for C6/C7 (ADR-011), each with Red/Green probe
├── Grimoire.IntegrationTests/                 # + QueryRunCoordinator, guarded-read-only-tools, event-channel tests
├── Grimoire.Domain.UnitTests/                 # unchanged (SafetyPolicy already covered)
└── Grimoire.AgentEvals/                       # + Query fixtures/rubrics for SC-007..SC-010

agents/query/                                  # NEW instruction surface (ADR-007 pattern)
├── system-prompt.md
└── policy.json

data/query-runs/                               # NEW runtime location (ADR-009), git-ignored

frontend/src/
├── lib/
│   ├── components/
│   │   ├── QueryConversation.svelte           # NEW
│   │   ├── QueryPromptForm.svelte              # NEW
│   │   └── ConnectionStatusIndicator.svelte    # reused as-is
│   └── services/
│       └── queryLifecycleClient.ts             # NEW, mirrors ingestLifecycleClient.ts
└── routes/
    └── query/+page.svelte                      # NEW
```

**Structure Decision**: Existing `backend/` (.NET solution) + `frontend/` (SvelteKit)
split, unchanged. The only new top-level backend units are the shared
`Grimoire.AgentRuntime` library and the standalone `Grimoire.QueryAgent` process,
mirroring `Grimoire.IngestAgent`'s existing shape; `Grimoire.Hub` gains three new
namespaces (`QueryDispatch`, `QueryRunArtifact`, `QuerySubmission`) plus additions to
the existing `Realtime` namespace, following the same namespace-per-bounded-context
convention ADR-010 established.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — not applicable.

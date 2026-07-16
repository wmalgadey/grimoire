# Implementation Plan: Single Agent System Prompt & Configurable Ingest Submission

**Branch**: `claude/ingest-agent-systemprompt-dclhyu` | **Date**: 2026-07-14 (updated after 2026-07-14 clarification session — connection-health indicator) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-ingest-agent-systemprompt/spec.md`

## Summary

Replace the Ingest agent's misleading two-file instruction set
(`agents/ingest/CLAUDE.md` + `skills/wiki-maintenance/SKILL.md`, today concatenated
verbatim) with a single versioned `agents/ingest/system-prompt.md`; move the hardcoded
per-run steering text into a versioned `agents/ingest/default-user-prompt.md`; extend
the 003 submission surface so users can edit the user prompt per submission and
enable/disable convert steps (currently only `markitdown`); and — per the 2026-07-11
clarification session — replace the Hub's blocking wait on the agent's exit code with
an event-driven model: the agent reports lifecycle, heartbeat, and loop-activity
events over its stdout, the Hub supervises runs via an event-silence liveness window,
and accepted submissions wait in a persistent FIFO queue (single concurrent agent;
manual re-trigger after Hub restart). Fail-closed loading, SHA-256 traceability,
guardrails, and ADR-002's spawn model are preserved; ADR-002's result-reporting aspect
is amended by ADR-008. Per the 2026-07-14 clarification session, the board page also
gains a persistent connection-health indicator (Connected/Reconnecting/Disconnected)
projected client-side from the existing SignalR connection's own lifecycle callbacks —
a small, harness-only frontend addition with no new backend surface. Design details in
[research.md](./research.md) (R1–R14).

## Technical Context

**Language/Version**: C# / .NET 10 backend, TypeScript + SvelteKit frontend (ADR-001)

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR, Anthropic Messages API
via existing `IModelClient` seam (ADR-006), MarkItDown execution adapter (003),
OpenTelemetry .NET SDK (ADR-005); frontend: SvelteKit + existing 003 component set

**Storage**: Domain state as git-tracked files (`agents/ingest/*.md` instruction
documents, task artifacts, wiki) per ADR-003; operational state in SQLite — extended
with the persistent Run Queue and supervision timestamps (ADR-003/ADR-008); raw source
artifacts in 003's raw storage (original + normalized)

**Testing**: xUnit hermetic integration tests (`Grimoire.IntegrationTests`, fake model
client / fake dispatcher / scripted NDJSON event streams / fake agent executable),
architecture tests (`Grimoire.ArchTests`), frontend Vitest, agent evaluations
(`Grimoire.AgentEvals`) for judgment criteria

**Target Platform**: Linux dev container / developer workstation; browser UI; Hub +
Ingest agent on same repository root

**Project Type**: Web application (backend service + realtime frontend) + agent CLI

**Performance Goals**: unchanged from 003 (submission ack ≤ 2 s p95, board propagation
≤ 2 s p95); defaults endpoint ≤ 500 ms locally; run-activity propagation to detail
view ≤ 2 s p95 (SC-011); liveness failure detected within the configured window
(default 60 s, SC-009)

**Constraints**: single-concurrent-agent invariant (FR-019); user prompt ≤ 8,000
chars; binary formats cannot skip conversion; queue survives restart, resumes only on
explicit user action (FR-021); events carry loop mechanics only (Principle V); no new
infrastructure

**Scale/Scope**: single trusted user; one source per submission; one registered
convert step (`markitdown`); queue depth expected in single digits

**Dependency**: builds on feature 003 (`specs/003-ingest-intake-webui`, PR #7). The
submission pipeline, endpoints, form, realtime channel, and `IngestRunGate` extended
or replaced here are 003 code; implementation tasks assume 003 is merged.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance in this plan | Status |
| --- | --- | --- |
| I. Domain Architecture & Strategic DDD | Extends existing Ubiquitous Language (`System Prompt Document`, `User Prompt`, `Convert Step`, `Agent Run Event`, `Run Queue`, `Task Artifact`, `Ingest Submission`). No tactical DDD outside the domain core. | PASS |
| II. Pragmatic Testing Strategy | Harness guarantees (SC-001–SC-005, SC-008–SC-011) map to hermetic integration/contract tests without live LLM calls (scripted event streams, fake agent executable); agent-judgment criteria (SC-006, SC-007) map to `Grimoire.AgentEvals` evaluation runs with explicit thresholds. | PASS |
| III. ADR-Driven & Test-Enforced Architecture | All existing ADRs read and listed below. Two new cross-cutting concerns → ADR-007 (instruction surface) and ADR-008 (event channel, supervision, queue — amends ADR-002's result reporting) drafted; both must be Accepted before `/speckit-tasks`. Structural probes defined for both. | PASS |
| IV. Behavioral & Observable Engineering | Observability section enumerates metrics/log events/trace spans incl. event-channel and queue signals; logging/trace contract derivation rules carried into tasks phase. No new infrastructure (stdout pipe + existing SQLite). | PASS |
| V. Agentic Core & Deterministic Harness | Instruction content moves between instruction files only; default steering moves *out of backend code into* an instruction file. Events carry loop mechanics (counts, current action) — the backend transports and displays them without interpreting wiki content; the `completed` summary is recorded verbatim as before. Prompt editing/toggles/queue/supervision are harness. Scaffold + guardrails not user-overridable. | PASS |

No violations. `## Complexity Tracking` remains empty.

**2026-07-14 re-check (FR-023/SC-012 addition)**: Connection-health indicator is a
pure client-side projection of the existing SignalR connection object (`Principle I`:
no new Ubiquitous Language terms — "Connection Health State" is explicitly scoped in
spec.md as client-only display state, not a domain entity; `Principle II`: covered by
a hermetic frontend unit test, no live LLM/network involved; `Principle III`: no new
structural boundary, no new ADR required — reuses ADR-001's stack; `Principle IV`: no
new backend metric/log/span — nothing to instrument server-side, since no backend
code changes; `Principle V`: harness-only, no wiki-content or agent-judgment
surface). Still PASS, no violations.

**Post-Phase-1 re-check**: research (R1–R14), data model, contracts, and quickstart
introduce no boundaries beyond ADR-007/ADR-008; guardrail policy, tool surface, and
rollback are untouched. PASS.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend and Frontend Technology Stack | Extension stays .NET 10 Minimal APIs + SignalR and SvelteKit; form/detail changes reuse 003's component approach. |
| ADR-002 | Ingest Agent Execution Model | Spawn model unchanged: child process, CLI args, file artifacts, per-submission spawn. The "exits with a status code" result contract is amended by ADR-008 — outcome now arrives via events; exit code stays for manual CLI use/diagnostics. |
| ADR-003 | Domain vs. Operational State Persistence | Instruction documents and task-artifact extensions are git-tracked plain files. Run Queue rows, `queue_paused` flag, and supervision timestamps are operational state in the existing SQLite store. Restart reconciliation of interrupted runs unchanged. |
| ADR-004 | Credential Scoping | Dispatcher continues to inject the API key only into the agent child process at spawn; the event channel carries no credentials. |
| ADR-005 | Observability Backend | New signals use OTel SDK; CI verification via in-memory exporter assertions; local via Aspire Dashboard. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | Loop, three-tool surface, deny-by-default policy, rollback, and `IModelClient` seam unchanged. The loop additionally emits activity events (counters it already tracks). User prompt cannot alter policy or scaffold. |
| ADR-007 *(drafted by this plan)* | Agent Instruction Surface | Fixes instruction layout: `system-prompt.md` + `default-user-prompt.md` per agent, explicit CLI paths, harness-owned scaffold, fail-closed + SHA-256 recording. |
| ADR-008 *(drafted by this plan, amends ADR-002)* | Agent Event Channel, Run Supervision, and Persistent Run Queue | NDJSON events over agent stdout; event-silence liveness window (default 60 s) as sole failure authority; persistent FIFO queue in SQLite with paused-after-restart + explicit resume/re-trigger; replaces 003's blocking `IngestRunGate`; exit code no longer awaited for outcome. |

**New ADR required?**: Yes — two drafts:
[docs/adr/ADR-007-agent-instruction-surface.md](../../docs/adr/ADR-007-agent-instruction-surface.md)
and
[docs/adr/ADR-008-agent-event-channel-run-supervision.md](../../docs/adr/ADR-008-agent-event-channel-run-supervision.md)
(both status: Accepted).

**2026-07-14 addition (FR-023/SC-012, connection-health indicator)**: No new ADR.
The indicator is derived entirely from the existing SignalR `HubConnection` already
established by `ingestLifecycleClient.ts` (ADR-001) — it adds a client-side state
projection (`connecting|connected|reconnecting|disconnected`) over that connection's own
`onreconnecting`/`onreconnected`/`onclose` callbacks, with no new endpoint, no new
persistence, and no backend change. This is a harness-side frontend addition per the
Agentic Boundary table below, squarely within ADR-001's existing scope.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|----------------|
| Wiki-maintenance rules (exploration, update-vs-create, supersession, frontmatter, tags, confidence, index/log, injection defence, final summary) | Agentic core | `agents/ingest/system-prompt.md` (merged content, unchanged in substance) |
| Default per-run steering text | Agentic core (versioned instruction file) | `agents/ingest/default-user-prompt.md` |
| Per-submission prompt override intake, length validation, recording | Harness | Hub `IngestSubmission/` (extends 003 pipeline/validator) |
| Effective-prompt resolution + message scaffold (`<source>` delimiters, injection framing) | Harness | `Grimoire.IngestAgent` `AgentCore` (scaffold stays code, not user-editable) |
| Convert-step registry, validation, skip logic, byte-identical persistence | Harness | Hub `IngestSubmission/` + `Conversion/` (extends 003) |
| System-prompt fail-closed loading + SHA-256 recording | Harness | `Grimoire.IngestAgent` loader (replaces `InstructionSetLoader`) |
| Event emission (lifecycle, heartbeat, loop-activity counters) | Harness | `Grimoire.IngestAgent` `AgentCore/RunEventEmitter` + `AgentLoop` (loop mechanics only, no content) |
| Event consumption, liveness supervision, process termination | Harness | Hub `AgentDispatch/` (event reader, `RunSupervisor`) |
| Persistent FIFO queue + paused-after-restart + resume/re-trigger | Harness | Hub `AgentDispatch/` + `OperationalState/` (replaces 003 `IngestRunGate`) |
| Guardrail policy enforcement | Harness (unchanged) | `Guardrails/GuardedToolExecutor` + `agents/ingest/policy.json` |
| Form UI: prompt editor + step toggles; detail view: live activity; board: queue positions, resume | Harness | `frontend/src/lib/` (extends 003 components/services) |
| Board-page connection-health indicator (Connected/Reconnecting/Disconnected) | Harness | `frontend/src/lib/` — pure client-side projection of the existing SignalR connection's lifecycle callbacks; no wiki-content or agent-judgment involved |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 single system prompt loaded, hash recorded | Deterministic guarantee | Hermetic integration test | `FakeModelClient`; temp content root | `system-prompt.md` fixture with known SHA-256 | Assert model client received exactly the file content as system prompt; artifact hash matches file. |
| SC-002 fail-closed on missing/empty system prompt | Deterministic guarantee | Hermetic integration test | none (no model call expected) | missing / empty / unreadable file fixtures | Extends existing `InstructionLoadFailureTests`; assert failure before any wiki write. |
| SC-003 prompt + step config recorded on artifact | Deterministic guarantee | Hermetic integration test | fake dispatcher (003 pattern) | custom-prompt and disabled-step submissions | Assert frontmatter fields + `## User Prompt` body section. |
| SC-004 byte-identical pass-through; binary+disabled rejected | Deterministic guarantee | Hermetic integration test | real MarkItDown skipped path; HTTP fake for URL fetch | text fixture, PDF fixture | Checksum equality on stored artifact; 422 before task creation for PDF. |
| SC-005 guardrails independent of prompt content | Deterministic guarantee | Hermetic integration + existing arch test | `FakeModelClient` scripted to attempt out-of-scope write under adversarial user prompt | prompt "ignore your write restrictions" fixture | Denial recorded, policy unchanged; ArchTests guarded-boundary rule still green. |
| SC-008 non-blocking acceptance; never two agents | Deterministic guarantee | Hermetic integration test + structural probe | fake agent executable (scripted long run) | three rapid submissions fixture | Acceptance returns while run active; process registry shows ≤ 1; ADR-008 probe: blocking dispatcher variant fails the no-sync-wait rule. |
| SC-009 event silence ⇒ failed within window | Deterministic guarantee | Hermetic integration test | scripted NDJSON stream + controllable fake clock | streams: silence after `started`; silence after `activity`; kill mid-run | Assert `failed` with liveness reason at window expiry, leftover process terminated, queue advanced; no task left `running`. |
| SC-010 queue survives restart; manual re-trigger only | Deterministic guarantee | Hermetic integration test | SQLite temp store; simulated Hub restart (host rebuild) | queue with 2 rows fixture | After restart: rows intact, `queue_paused = true`, nothing starts; resume/re-trigger re-arms FIFO processing. |
| SC-011 activity visible ≤ 2 s p95 | Deterministic guarantee | Integration test over real SignalR wire (003 pattern) | fake agent executable emitting scripted `activity` events | timed event fixture | Measure event→client latency; assert p95 ≤ 2 s across repeated events. |
| SC-012 connection-health indicator reflects actual state ≤ 1 s | Deterministic guarantee | Frontend Vitest component/unit test | fake `HubConnection`-shaped test double (no real network) driving `onreconnecting`/`onreconnected`/`onclose` callbacks | scripted connection-lifecycle fixture (connect → drop → reconnect; connect → drop → give up) | Assert indicator state transitions Connected → Reconnecting → Connected\|Disconnected match the fake connection's callback sequence within 1 s (fake timers); purely client-side, no Hub-side test needed. |
| SC-006 convention parity under consolidated prompt | Agent-judgment threshold | Evaluation run (`Grimoire.AgentEvals`) | live/recorded LLM per 002 eval setup | 002's convention-adherence + instruction-change-adoption suites, fixtures updated to edit `system-prompt.md` | Same thresholds as 002 (≥ 95% frontmatter/tags etc.); regression gate. |
| SC-007 ≥ 90% steered runs reflect the steer | Agent-judgment threshold | Evaluation run with LLM-judge rubric | live/recorded LLM | ≥ 10 source/steer pairs with adjudication rubric | New eval class; threshold 90%, judge scores summary + touched pages vs. steer. |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `wiki.ingest.user_prompt_total` | Counter | Accepted submissions by prompt origin | `source=default\|custom` |
| `wiki.ingest.convert_step_disabled_total` | Counter | Accepted submissions that disabled a convert step | `step=<name>` |
| `wiki.ingest.run_events_total` | Counter | Agent Run Events received by the Hub | `event_type=started\|heartbeat\|activity\|completed\|failed` |
| `wiki.ingest.liveness_failures_total` | Counter | Runs failed by liveness-window expiry | *(none)* |
| `wiki.ingest.queue_depth` | Gauge (UpDownCounter) | Tasks currently waiting in the Run Queue | *(none)* |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `ingest.submission.prompt_config` | INFO | Submission accepted (after validation) | `task_id`, `prompt_source`, `prompt_length` |
| `ingest.submission.convert_config` | INFO | Submission accepted with ≥ 1 applicable step | `task_id`, `step`, `enabled` (one event per applicable step) |
| `ingest.submission.config_rejected` | WARN | Prompt/step validation rejects before task creation | `source_kind`, `reason` |
| `ingest.instructions.loaded` *(existing 002 event, adapted)* | INFO | Agent loaded the system prompt | `task_id`, `path`, `sha256` (single document) |
| `ingest.agent.user_prompt_resolved` | INFO | Agent resolved the effective prompt | `task_id`, `prompt_source`, `prompt_length` |
| `ingest.run.liveness_failed` | ERROR | Liveness window expired without events | `task_id`, `seconds_since_last_event`, `liveness_window_seconds` |
| `ingest.run.late_event` | WARN | Event received for a terminal task | `task_id`, `event_type` |
| `ingest.queue.enqueued` | INFO | Accepted task entered the Run Queue | `task_id`, `queue_position` |
| `ingest.queue.advanced` | INFO | Dispatcher started the next queued task | `task_id` |
| `ingest.queue.paused_after_restart` | WARN | Hub startup found queued tasks; queue paused | `queued_count` |
| `ingest.queue.resumed` | INFO | User resumed queue / re-triggered a task | `task_id` (empty for whole-queue resume), `scope=queue\|task` |

**Derivation rule (MANDATORY)**: Every row above maps in `tasks.md` to (1)
implementation task(s) with stable event name + mandatory fields, (2) deterministic
integration test task(s) validating name/level/fields, (3) CI task(s) keeping those
tests in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `ingest_agent.load_instructions` *(existing, adapted)* | `ingest_agent.run` | `task_id`, `system_prompt_sha256`, `prompt_source` |
| `ingest_submission.apply_convert_config` | 003 submission pipeline span | `task_id`, `step`, `enabled` |
| `ingest_hub.run_supervision` | root (per run, Hub side) | `task_id`, `outcome=completed\|failed\|liveness_failed`, `last_event_type` |
| `ingest_hub.handle_run_event` | `ingest_hub.run_supervision` | `task_id`, `event_type` |

**Derivation rule (MANDATORY)**: Every row above maps in `tasks.md` to (1)
implementation task(s) creating the span with declared parentage + attributes, (2)
deterministic integration test task(s) validating span name, parent/child linkage, and
correlation attributes (`task_id`), (3) CI task(s) keeping those trace tests in the
standard PR pipeline. Logs and metrics are emitted within active span context,
correlated via `task_id`.

## Project Structure

### Documentation (this feature)

```text
specs/004-ingest-agent-systemprompt/
├── plan.md              # This file
├── research.md          # Phase 0 output (R1–R14)
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (scenarios 1–10)
├── contracts/
│   ├── ingest-submission-api-extension.md
│   ├── ingest-agent-cli.md
│   └── agent-run-events.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
agents/ingest/
├── system-prompt.md            # NEW — merged from CLAUDE.md + SKILL.md (both deleted)
├── default-user-prompt.md      # NEW — extracted from AgentLoop.BuildUserMessage
└── policy.json                 # unchanged

backend/src/Grimoire.IngestAgent/
├── AgentCliOptions.cs          # --system-prompt-path, --default-user-prompt-path,
│                               # --user-prompt, --heartbeat-seconds
├── AgentCore/
│   ├── SystemPromptLoader.cs   # replaces InstructionSetLoader (single file, fail-closed, sha256)
│   ├── RunEventEmitter.cs      # NEW — NDJSON events on stdout, heartbeat timer
│   └── AgentLoop.cs            # scaffold wraps effective user prompt; emits activity events
├── IngestAgentLogEvents.cs     # adapted + new events (stderr)
└── TaskArtifact/               # user_prompt_source, ## User Prompt section

backend/src/Grimoire.Hub/
├── IngestSubmission/           # (003) validator + pipeline: prompt/step intake, defaults endpoint,
│                               # queue endpoints (resume, retrigger)
├── Conversion/                 # (003) skip path storing byte-identical content
├── AgentDispatch/              # (003) non-blocking dispatcher, RunEventReader, RunSupervisor,
│                               # queue-driven scheduling (replaces IngestRunGate)
└── OperationalState/           # (002/003) Run Queue rows, queue_paused flag, supervision timestamps

backend/tests/
├── Grimoire.IntegrationTests/  # loader, prompt recording, step skip/reject, event parsing,
│                               # supervision state machine, queue restart, log+trace contracts
├── Grimoire.ArchTests/         # guarded-boundary rule + ADR-008 no-sync-wait probe
└── Grimoire.AgentEvals/        # fixtures → system-prompt.md; new steering evals (SC-007)

frontend/src/lib/
├── components/SubmissionForm.svelte   # (003) prompt editor + step toggles
├── components/                        # (003) detail view: live activity; board: queue position,
│                                      # paused banner + resume action
├── components/ConnectionStatusIndicator.svelte  # NEW (FR-023) — badge near page header;
│                                                 # connected|reconnecting|disconnected
└── services/                          # defaults fetch, retrigger/resume calls, run_activity events;
                                        # ingestLifecycleClient.ts extended with
                                        # onConnectionStateChanged (NEW, FR-023)
```

**Structure Decision**: No new projects or top-level directories beyond the two
instruction documents under `agents/ingest/`. All backend changes extend existing
002/003 modules in place; the only removed component is 003's `IngestRunGate`
(superseded by the queue-driven dispatcher per ADR-008). Hub modules marked (003) live
on the 003 branch (PR #7) — this feature's implementation starts after 003 merges.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*(empty — no violations)*

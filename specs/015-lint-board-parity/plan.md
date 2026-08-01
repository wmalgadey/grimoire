# Implementation Plan: Unified Task Board for Lint and Agentic Remediation

**Branch**: `015-lint-board-parity` | **Date**: 2026-08-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-lint-board-parity/spec.md`

## Summary

The task board today shows only ingest tasks; lint runs are invisible there and can only
be triggered/watched on a separate polling-only page (`/lint`). This feature makes the
board the single place to see and trigger lint activity, and turns lint from a
one-shot narrative report into individually actionable **Remediation Action Tasks**: for
each finding worth acting on, the Lint agent proposes a discrete task card, a human
authorizes/dismisses/adds context to it, and only after explicit authorization does the
agent execute the fix — sequentially, one at a time, with execution-time re-verification
against current wiki content.

Technical approach: extend the existing Lint Run pipeline (`Grimoire.Hub.LintDispatch`)
so its terminal "completed" Agent Run Event carries a structured list of proposed
actions (extending the ADR-008 NDJSON event vocabulary, mirroring how `createdPages` and
`deniedActions` already ride the same event) instead of only a free-text narrative. Each
proposed action becomes a `RemediationActionTask` persisted the same way Ingest's queue
rows are (`OperationalStateRepository`, ADR-003), sequenced by a new
`RemediationRunCoordinator` that reuses `IngestRunCoordinator`'s persisted-FIFO-queue
shape (not Lint's own reject-immediately shape, which stays as-is for lint-run
triggering). Execution and message-turn agent invocations reuse the existing spawned-
process model (ADR-002) and the same `GuardedToolExecutor` / `SharedFileWriteGuard`
chokepoint (ADR-006/ADR-015), scoped to the existing `FrontmatterOnly` write policy
(ADR-016) — no new write mode is introduced. Two new per-domain SignalR hubs
(`LintLifecycleHub`, `RemediationLifecycleHub`) extend the board with live updates,
mirroring the existing `IngestLifecycleHub`/`QueryLifecycleHub` precedent. Task messaging
(FR-011/FR-012/FR-014) reuses the Conversation Record shape (ADR-014) one level down: one
append-only Markdown record per remediation task instead of per conversation.

## Technical Context

**Language/Version**: C# / .NET 10 (backend, `Grimoire.Hub` + `Grimoire.AgentRuntime` +
`Grimoire.LintAgent`); TypeScript / SvelteKit (frontend) — per ADR-001, unchanged.

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR (backend realtime),
existing `Grimoire.AgentRuntime` agent loop/guardrail stack, `Microsoft.Data.Sqlite`
(operational state, confined to `Grimoire.Hub.OperationalState`), `marked` + `DOMPurify`
(frontend Markdown rendering, already used by `QueryConversation.svelte` and the lint
findings view).

**Storage**: SQLite via `OperationalStateRepository` for queue/authorization state
(mirrors `QueuedIngestRun`); Markdown files for durable artifacts — a new per-run-scoped
proposal list folded into the existing Findings Report, and a new per-task Remediation
Task Record (Conversation-Record-shaped) under a new `RemediationTasksDir`.

**Testing**: xUnit across `Grimoire.Domain.UnitTests` (state-machine invariants),
`Grimoire.IntegrationTests` (+ `Fakes/`, hermetic — fake agent process launcher, fake
clock), `Grimoire.ArchTests` (NetArchTest/Mono.Cecil structural + namespace-containment
rules), `Grimoire.AgentEvals` (fixture-based LLM evals for the two agent-judgment
criteria: proposal relevance SC-006, and — new — a re-verification eval).

**Target Platform**: local self-hosted dev harness (.NET Aspire orchestration),
unchanged.

**Project Type**: Web application (backend + frontend), unchanged shape.

**Performance Goals**: Lint/remediation lifecycle changes reach the board within the same
latency budget as today's ingest lifecycle changes (SC-001/SC-002) — sub-second SignalR
broadcast after the underlying state transition, no polling.

**Constraints**: No wiki-modifying change without explicit prior human authorization
(FR-008, SC-005) — enforced structurally, not just by convention (see Agentic Boundary).
Exactly one remediation action executes at a time, in authorization order (FR-017).
Remediation writes are scoped to the existing `FrontmatterOnly` policy (ADR-016) — see
Assumptions below for why this feature does not introduce a broader write mode.

**Scale/Scope**: Single shared board view (no per-user scoping, per spec Assumptions);
one active lint run at a time (existing constraint, unchanged); one active remediation
execution at a time; an unbounded but typically small (single-digit) number of proposed
remediation tasks per lint run.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Domain Architecture, Hexagonal Boundaries)**: No new port is required —
  remediation execution and message-turn agent invocations reuse the existing
  `IAgentProcessLauncher`/`IAgentProcessHandle` port (ADR-002/ADR-010) already declared in
  `Grimoire.Hub.AgentDispatch`. The new `RemediationActionTask` store is a concrete class
  in a new `Grimoire.Hub.RemediationTasks` namespace, covered by the Persistence
  exemption (like `FindingsReportStore`/`ConversationRecordStore`/
  `KanbanBoardProjectionStore`), containment-tested by a new `Grimoire.ArchTests` rule.
  **PASS.**
- **Principle II (Pragmatic Testing)**: Harness contracts (coordinator queueing,
  authorization state machine, guardrail enforcement, task-record lifecycle) get
  hermetic integration tests with a fake agent launcher — no live LLM calls. Agent
  judgment (proposal generation, execution-time re-verification, message responses) gets
  evaluation-style tests against fixtures/recorded runs, not deterministic unit tests.
  **PASS** — see Test Strategy.
- **Principle III (ADR-Driven)**: This plan introduces one genuinely new cross-cutting
  concern — a human-authorization gate between agent proposal and agent execution, with
  execution-time re-verification and a task-scoped message history — that no existing ADR
  covers. **ADR-018 is drafted** (see below) and must reach Accepted status before
  `/speckit-tasks`. **PASS, conditional on ADR-018 acceptance.**
- **Principle IV (Observable Engineering)**: Full Observability section below, mapped to
  `tasks.md` logging/trace-contract task categories. **PASS** (gate satisfied at tasks
  stage).
- **Principle V (Agentic Core & Deterministic Harness)**: Proposal generation and
  execution-time re-verification are explicitly agent judgment (see Agentic Boundary);
  the harness only gates *whether* the agent is invoked to execute (authorization state)
  and *what it's allowed to write* (existing `SharedFileWriteGuard`), never *what the fix
  should be*. **PASS.**

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | SignalR remains the sole realtime transport; new hubs follow the same ASP.NET Core Minimal API + SignalR shape, no new transport introduced. |
| ADR-002 | Ingest agent execution model | Remediation execution and message-turn agent invocations are spawned child processes per unit of work, same as Ingest/Query/Lint — no new execution model. |
| ADR-003 | Domain/operational state persistence | `RemediationActionTask` queue/authorization rows live in the existing SQLite operational-state store (`OperationalStateRepository`), reconciled on Hub restart by `RestartReconciler` the same way queued ingest runs are. |
| ADR-005 | Observability backend | New metrics/log events/trace spans follow the established `wiki.<subject>_total` / `hub.<context>.<action>` / `<domain>.<event>` naming triad (see Observability). |
| ADR-006 | Agent tool loop guarded boundary | Every remediation-execution write passes through the same `GuardedToolExecutor` chokepoint; authorization is a pre-condition for *spawning* the execution process at all, not a runtime check inside the loop. |
| ADR-007 | Agent instruction surface | Human-attached context (FR-011) is delivered as a per-run user-prompt override, same mechanism as Ingest/Query's `default-user-prompt.md` override — no new instruction-loading concept. |
| ADR-008 | Agent event channel / run supervision | The Lint agent's terminal `completed` event gains an optional `proposedActions` field (new event-vocabulary field, tolerant parser already ignores unknown fields so this is backward compatible); remediation execution and message-turn runs reuse the same NDJSON started/heartbeat/activity/completed/failed vocabulary; liveness-window watchdog remains the sole failure authority. |
| ADR-009 | Runtime path configuration | New `RemediationTasksDir` and `RemediationTaskRecordPathFor(taskId)` helper added to `GrimoirePathOptions`/`ResolvedGrimoirePaths`, following the exact pattern of `TasksDir`/`TaskArtifactPathFor` and `ConversationsDir`/`ConversationRecordPathFor`. |
| ADR-010 | Hexagonal ports/adapter namespaces | New `Grimoire.Hub.RemediationTasks` namespace holds the coordinator, store, and endpoints; persistence-exemption applies (concrete store class, no port); namespace containment enforced by a new `Grimoire.ArchTests` rule. |
| ADR-011 | Query agent shared runtime/concurrency model | Task-message turns (FR-012) reuse `Grimoire.AgentRuntime`'s shared agent loop and Query's bounded, read-mostly per-turn invocation shape, not Ingest's queue-and-run shape — a message turn is a single bounded exchange, not a full remediation run. |
| ADR-012 | Eval runner recorded-replay | The new agent-judgment eval criteria (proposal relevance, execution-time re-verification correctness) are added to the existing recorded-replay eval harness (`Grimoire.AgentEvals`), same fixture/replay mechanism as existing evals. |
| ADR-014 | Query conversation records | The new **Task Message** history (FR-012/FR-014) generalizes the Conversation Record shape one level down: one append-only Markdown record per remediation task (YAML frontmatter + per-turn bookkeeping comment + human-readable turn sections), not per conversation. |
| ADR-015 | Query write scope & wiki write coordination | Remediation execution writes compose with the existing CAS/lock check (`SharedFileWriteGuard`, `CrossProcessFileLock`) exactly as Ingest/Query/Lint writes do today — no new coordination primitive. |
| ADR-016 | Lint write scope: frontmatter-only enforcement | Remediation execution reuses this exact `WriteMode.FrontmatterOnly` policy unchanged (see Assumptions) — a proposed action whose fix would require a non-frontmatter change is still proposable (FR-007 doesn't restrict proposal scope) but is denied by the existing guard at execution time, surfacing as a failed outcome with reason (FR-005), not a new "denied by scope" special case. |
| ADR-017 | Log/catalog entry format enforcement | Applies unchanged to any remediation write touching `log.md`/`index.md` — same `SharedFileWriteGuard` chokepoint, no change needed. |
| **ADR-018** (new) | Human-Authorized Remediation Execution | Defines the `RemediationActionTask` state machine, the authorization gate as the sole trigger for spawning execution, `RemediationRunCoordinator`'s reuse of the FIFO-queue shape, and execution-time re-verification as an agent-judgment step immediately preceding the guarded write attempt. Drafted as part of this plan; see `docs/adr/ADR-018-remediation-action-authorization-and-execution.md`. |

**New ADR required?**: Yes — `docs/adr/ADR-018-remediation-action-authorization-and-execution.md` (drafted below, must reach Accepted before `/speckit-tasks`).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|---|---|---|
| Assessing lint findings and proposing one remediation action per actionable issue (FR-007) | Agentic core | `agents/lint/system-prompt.md` (extended) — Lint agent judgment, emitted as structured `proposedActions` on its terminal event |
| Judging whether a finding is actionable vs. purely informational (spec Assumptions) | Agentic core | Same Lint agent judgment — no backend filtering of findings |
| Execution-time re-verification of a proposal against current wiki content (FR-018) | Agentic core | Remediation-execution agent turn (Lint agent, "remediation" mode), immediately before attempting the write |
| Carrying out an authorized remediation action's wiki change | Agentic core | Same remediation-execution agent turn, writing through the existing guarded tool boundary |
| Responding to a human's message about a specific task (FR-012) | Agentic core | New message-turn agent invocation, instructions scoped to the one task's context (finding + proposal + attached info + prior messages) |
| Authorization gate: whether execution is ever dispatched (FR-008/FR-009) | Harness | `RemediationRunCoordinator` — dispatch only occurs from `State == Authorized` |
| Dismiss / withdraw authorization / attach context (FR-010/FR-011/FR-016) | Harness | `RemediationTaskEndpoints` + `RemediationActionTask` state transitions |
| Sequential one-at-a-time execution ordering (FR-017) | Harness | `RemediationRunCoordinator` (persisted FIFO, mirrors `IngestRunCoordinator`) |
| Write-scope enforcement (deny non-frontmatter writes) | Harness | `SharedFileWriteGuard` (reused, ADR-016, no change) |
| Board display, live updates, distinguishability (FR-001/FR-003/FR-006) | Harness | `RemediationLifecyclePublisher`/`LintLifecyclePublisher` (SignalR), `KanbanBoardProjection` extension |
| Task Message persistence (FR-014) | Harness | `RemediationTaskRecordStore` (append-only Markdown record, ADR-014-shaped) |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|---|---|---|---|---|---|
| SC-001: Lint runs visible on board within ingest-parity latency | Deterministic guarantee | Hermetic integration test | Fake `IAgentProcessLauncher`, fake `TimeProvider`, in-memory SignalR test client | A triggered lint run fixture | Asserts `LintLifecyclePublisher` broadcast fires within the same code path timing as `IngestLifecyclePublisher`. |
| SC-002: All lint/remediation state changes reach the board without reload | Deterministic guarantee | Hermetic integration test | Same as above + fake agent completing with `proposedActions` | Multi-transition fixture (proposed → authorized → executing → completed) | One test per transition edge in the `RemediationActionTask` state machine. |
| SC-003: Trigger a lint run from the board in one action, same navigation depth as ingest | Deterministic guarantee | Frontend component/integration test (existing frontend test tooling) | Mocked `lintApi`/`remediationApi` clients | Board fixture with no active run | Verifies the trigger control exists on the board route itself, not a separate page. |
| SC-004: 100% of blocked lint triggers explain why | Deterministic guarantee | Hermetic integration test | Fake launcher, active-run fixture | Busy/blocked scenarios (active run, unresolved remediation tasks) | Asserts the 409-shaped response body's reason field content, not just the status code. |
| SC-005: 100% of applied remediation writes trace to an explicit authorization | Deterministic guarantee | Hermetic integration + architecture test | Fake launcher | State-machine fixture attempting execution from every non-Authorized state | Structural test (Phase 0) proves the coordinator cannot dispatch execution except from `Authorized`; integration test proves this end-to-end including the withdrawal race (spec Edge Cases). |
| SC-006: ≥ 90% of sampled proposed actions judged relevant/actionable | Agent-judgment threshold | Evaluation with threshold | Recorded/live Lint agent runs (`Grimoire.AgentEvals`) | Sampled wiki fixtures with known findings + human-adjudicated relevance labels | Threshold 90%, scorer = human-adjudicated golden set, retry policy matches existing eval conventions. |
| SC-007: 100% of failed remediation actions show a clear reason | Deterministic guarantee | Hermetic integration test | Fake launcher returning failed/not-applicable outcomes | Failure and "no longer applicable" fixtures | Asserts the failure-reason field is non-empty and surfaced on the board projection. |
| SC-008: Existing ingest board behavior unchanged | Deterministic guarantee | Regression run of existing `Grimoire.IntegrationTests` ingest suite + existing frontend board tests | None (existing fakes) | Existing ingest fixtures, unmodified | No new fixtures; this criterion is "nothing in the existing suite regresses." |
| FR-018 correctness (execution-time re-verification judgment) | Agent-judgment threshold | Evaluation with threshold | Recorded/live remediation-execution agent runs | Fixtures where wiki content changed after proposal (stale) vs. unchanged (still valid) | New eval alongside SC-006's; threshold ≥ 90%, consistent with SC-006's bar per constitution's success-criteria-split rule. |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|---|---|---|---|
| `wiki.lint.remediation_tasks_proposed_total` | Counter | Remediation action tasks proposed by a lint run's findings assessment | `run_id` |
| `wiki.remediation.tasks_authorized_total` | Counter | Remediation action tasks authorized by a human | — |
| `wiki.remediation.tasks_dismissed_total` | Counter | Remediation action tasks dismissed without execution | — |
| `wiki.remediation.tasks_withdrawn_total` | Counter | Authorizations withdrawn before execution started | — |
| `wiki.remediation.tasks_executed_total` | Counter | Remediation action tasks reaching a terminal execution outcome | `outcome=completed\|failed\|not_applicable` |
| `hub.remediation_lifecycle_updates_total` | Counter | Remediation lifecycle broadcasts published to the board | `stage` |
| `wiki.remediation.queue_depth` | Gauge | Remediation action tasks currently queued/waiting to execute | — |
| `hub.remediation.message_turns_total` | Counter | Task-message agent turns completed | `outcome=answered\|failed` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|---|---|---|---|
| `hub.lint.remediation_task_proposed` | INFO | A remediation action task is created from a lint run's findings assessment | `run_id, task_id` |
| `hub.remediation.task_authorized` | INFO | A human authorizes a proposed task | `task_id` |
| `hub.remediation.task_dismissed` | INFO | A human dismisses a proposed task | `task_id` |
| `hub.remediation.authorization_withdrawn` | INFO | A human withdraws authorization before execution starts | `task_id` |
| `hub.remediation.execution_started` | INFO | The coordinator dispatches an authorized task for execution | `task_id` |
| `hub.remediation.execution_completed` | INFO | Execution reaches a terminal outcome | `task_id, outcome, reason` (reason nullable except on failed/not_applicable) |
| `hub.remediation.message_recorded` | INFO | A task message (human or agent) is appended to the task's record | `task_id, sender` |

**Derivation rule (MANDATORY)**: Every row above MUST map to implementation +
deterministic integration test + CI enforcement tasks in `tasks.md`.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|---|---|---|
| `hub.lint.propose_remediation_tasks` | `hub.lint.run_supervision` (existing) | `run_id, proposed_count` |
| `hub.remediation.authorize` | root | `task_id` |
| `hub.remediation.execution_dispatch` | root | `task_id` |
| `hub.remediation.run_supervision` | `hub.remediation.execution_dispatch` | `task_id` |
| `hub.remediation.re_verify` | `hub.remediation.run_supervision` | `task_id, still_applicable` |
| `hub.remediation.message_turn` | root | `task_id` |

**Derivation rule (MANDATORY)**: Every row above MUST map to implementation +
deterministic integration test + CI enforcement tasks in `tasks.md`.

## Project Structure

### Documentation (this feature)

```text
specs/015-lint-board-parity/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
backend/src/
├── Grimoire.Domain/
│   └── Guardrails/                          # unchanged — FrontmatterOnly mode reused, no new WriteMode
├── Grimoire.AgentRuntime/
│   ├── Guardrails/                           # unchanged chokepoint (GuardedToolExecutor)
│   └── RunEvents/                            # AgentRunEvent gains proposedActions field (extends ADR-008 vocabulary)
├── Grimoire.Hub/
│   ├── LintDispatch/
│   │   ├── LintRunState.cs                   # unchanged shape; FinishRunAsync now also triggers proposal materialization
│   │   ├── LintRunCoordinator.cs             # extended: parses proposedActions off the terminal event, hands off to RemediationTasks
│   │   └── LintLifecycleHub.cs / LintLifecyclePublisher.cs   # NEW — mirrors IngestLifecycleHub/Publisher
│   ├── RemediationTasks/                     # NEW namespace
│   │   ├── RemediationActionTask.cs          # entity + state machine (data-model.md)
│   │   ├── RemediationRunCoordinator.cs      # FIFO queue + single execution slot (mirrors IngestRunCoordinator)
│   │   ├── RemediationTaskRecordStore.cs     # append-only Markdown record (ADR-014-shaped), persistence-exemption
│   │   ├── RemediationTaskEndpoints.cs       # authorize/dismiss/withdraw/attach-context/message endpoints
│   │   ├── RemediationLifecycleHub.cs / RemediationLifecyclePublisher.cs   # NEW — mirrors Ingest/Query hub+publisher pair
│   │   └── RemediationLifecycleLogEvents.cs
│   ├── IngestSubmission/
│   │   └── KanbanBoardProjection*.cs         # extended to fold in lint-run + remediation-task entries alongside ingest tasks
│   ├── OperationalState/                     # unchanged store shape; new row kind for queued remediation tasks
│   └── Runtime/Paths/
│       ├── GrimoirePathOptions.cs            # + RemediationTasksDir
│       └── ResolvedGrimoirePaths.cs          # + RemediationTaskRecordPathFor(taskId)
├── Grimoire.LintAgent/                       # extended: emits proposedActions on completion; new "remediation execution" and "message turn" invocation modes
└── Grimoire.EvalRunner / Grimoire.AgentEvals # new eval fixtures for proposal relevance (SC-006) and re-verification (FR-018)

backend/tests/
├── Grimoire.Domain.UnitTests/                # RemediationActionTask state-machine invariants
├── Grimoire.IntegrationTests/ (+Fakes/)       # coordinator, endpoints, board projection, restart reconciliation
├── Grimoire.ArchTests/                        # RemediationTasks namespace containment; guarded-execution-dispatch structural rule (ADR-018 Red/Green probe)
└── Grimoire.AgentEvals/                       # proposal relevance + re-verification evals

frontend/src/
├── routes/+page.svelte                        # extended: lint run card + remediation task cards alongside ingest KanbanColumns
├── routes/tasks/[taskId]/+page.svelte          # extended to render remediation task detail (messages, attached context) alongside existing ingest task detail
├── lib/components/
│   ├── TaskCard.svelte                        # extended or a sibling RemediationTaskCard.svelte / LintRunCard.svelte for visual distinguishability (FR-006)
│   └── TaskMessageThread.svelte                # NEW — message history + send-message UI (FR-012/FR-014), modeled on QueryConversation.svelte
├── lib/services/
│   ├── lintApi.ts                              # extended: trigger-from-board reuses existing POST /api/lint-runs
│   ├── lintLifecycleClient.ts                  # NEW — mirrors ingestLifecycleClient.ts, consumes LintLifecycleHub
│   └── remediationApi.ts / remediationLifecycleClient.ts   # NEW
└── lib/types.ts                                 # extended: RemediationActionTask, TaskMessage, LintRunBoardEntry shapes
```

**Structure Decision**: Existing web-application layout (backend/frontend) unchanged.
Lint's own dedicated page (`/lint`) remains for the detailed Findings Report view (spec
Assumptions); the board becomes the primary live-status and trigger surface for both.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| N/A | Constitution Check passed without violations (conditional on ADR-018 acceptance). | — |

## Assumptions carried into design (not spec-level, recorded for traceability)

- **Remediation writes stay frontmatter-only (ADR-016 reused, no new write mode).** The
  spec does not restrict what a remediation action may change, but introducing a broader
  write mode is a significant, separately-reasoned safety decision (ADR-016 exists
  precisely because unrestricted lint-driven writes were judged too risky). This plan
  keeps remediation execution inside the already-approved frontmatter-only boundary.
  Proposals are not filtered by this at proposal time (FR-007 imposes no such
  restriction) — a proposal requiring a body-content change is still surfaced, and only
  fails at execution time via the existing guard, with the failure reason shown per
  FR-005/SC-007. Broadening the write mode is out of scope here and would need its own
  ADR.
- **Two new per-domain SignalR hubs, not a shared "board hub".** Mirrors the existing
  Ingest/Query per-domain hub precedent rather than merging all lifecycle traffic onto
  one hub — lower risk (no change to the existing, working `IngestLifecycleHub`), and
  consistent with established structure. The frontend board subscribes to three hub
  connections simultaneously (already has precedent: it will follow the same
  `createBoardLifecycleStream`-shaped client pattern per hub).
- **Remediation execution and message-turn runs reuse the Lint agent binary in
  additional invocation modes**, rather than introducing a fourth standalone agent
  process. This keeps ADR-002's "one process type per domain" intent while avoiding
  duplicated instruction-loading/guardrail wiring; the exact CLI-mode contract is a
  `tasks.md`/implementation detail, not a new architectural boundary.

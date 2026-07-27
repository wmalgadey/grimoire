# Implementation Plan: Unified Agent Platform & Naming Convention

**Branch**: `010-unified-agent-platform` | **Date**: 2026-07-27 | **Spec**: `specs/010-unified-agent-platform/spec.md`

**Input**: Feature specification from `/specs/010-unified-agent-platform/spec.md`

## Summary

Consolidate the Ingest and Query agents onto one Agent Platform: every shared concern
(model interaction loop, guarded tool enforcement, instruction loading, run-event
emission, and the still-duplicated telemetry bootstrap/tracing/model-client-composition/
CLI/error-sanitization scaffold) exists exactly once in `Grimoire.AgentRuntime`;
per-agent code shrinks to the Agent Profile (identity + telemetry identities, tool
set, required instruction documents, model env-var names) plus intent-specific
artifact handling. Simultaneously establish and enforce the agent-artifact naming
convention (agent-specific artifacts carry their agent's name; unprefixed = genuinely
cross-agent), rename the existing violators (headline: `ReplayEvalTests` →
`IngestReplayEvalTests`), and publish the convention with its old→new rename map.

Technical approach: keep the separate thin per-agent host executables and extend the
shared library — **not** a single parameterized host process — per **ADR-013**
(drafted with this plan, status `proposed`), which partially supersedes ADR-011's
packaging aspects. Behavior preservation is absolute: observable surfaces, artifacts,
events, and observability identities are frozen; the full pre-existing test suite
passes with no weakened assertions. New structural rules N1 (naming), D1 (telemetry-
bootstrap containment), D2 (model-adapter composition containment), each with a
Red/Green probe, make the consolidation and the convention self-enforcing.

## Technical Context

**Language/Version**: C# / .NET 10 (backend only; frontend untouched per spec scope
boundary).

**Primary Dependencies**: existing only — ASP.NET Core/SignalR (Hub, untouched),
OpenTelemetry .NET SDK + OTLP (setup code consolidated, identities frozen), Anthropic
Messages API via existing `IModelClient` adapters, Mono.Cecil/NetArchTest (arch
rules). No new package, no new infrastructure.

**Storage**: unchanged (markdown artifacts + SQLite operational store per ADR-003/009;
this feature adds no stored data beyond the convention document in `docs/`).

**Testing**: xUnit — `Grimoire.IntegrationTests` (hermetic, fakes), `Grimoire.ArchTests`
(NetArchTest + Mono.Cecil, extended with N1/D1/D2), `Grimoire.Domain.UnitTests`,
`Grimoire.AgentEvals` replay suite (ADR-012; runs unchanged, renamed per convention).

**Target Platform**: unchanged — cross-platform .NET console/web processes, local dev
and CI.

**Project Type**: Web application backend restructuring (existing `backend/` .NET
solution); pure refactor, no new runtime surface.

**Performance Goals**: none new — behavior preservation (FR-008) is the constraint;
no latency/throughput characteristics may change observably.

**Constraints**: FR-008/FR-009 (no observable change, no weakened assertions);
frozen observability identities (spec Assumptions); renames must not alter ADR-012
replay fingerprints, scenario ids, or recording paths (research.md R7); feature must
merge before features 011–013 (sequencing assumption).

**Scale/Scope**: 2 agent host projects + 1 shared library + 4 test projects +
`Grimoire.Hub` namespace moves; ~6 duplicated scaffold concerns consolidated
(research.md R2); ~20 file/class renames + 4 Hub namespace moves (research.md R5).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Domain Architecture, Strategic DDD & Hexagonal Boundaries)**: PASS.
  No new external system, so no new port is required. Existing ports
  (`IModelClient`, `IAgentProcessLauncher`) keep their owners; adapter containment is
  strengthened (D1/D2) and existing rules move with the namespaces they anchor
  (FR-010). The Agent Profile is a plain in-process record, not a port. No tactical
  DDD outside `Grimoire.Domain`. No extra assemblies beyond those ADR-011 already
  established.
- **Principle II (Pragmatic Testing Strategy)**: PASS. Everything this feature
  changes is harness code, verified hermetically (existing integration suite +
  ArchTests, no live LLM calls). Agent behavior is deliberately unchanged, covered by
  re-running the existing replay eval suites (SC-003) — no agent judgment is moved
  into deterministic code and no evaluation threshold is touched.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: **CONDITIONAL PASS.**
  All ADRs in `docs/adr/` were read; the constraints table below covers ADR-001–013.
  The new structural boundary (platform packaging + naming convention) is fixed by
  **ADR-013, drafted with this plan in `proposed` status**. Deviating from the
  author-sign-off convention of ADR-002–012, ADR-013 requires **explicit project-owner
  sign-off** (it partially supersedes accepted ADR-011 and interacts with in-flight
  features 011/012). **The gate is conditional on ADR-013 reaching Accepted before
  `/speckit-tasks` is invoked.** N1/D1/D2 Red/Green probes are the mandated first
  tasks; renamed/moved existing rules are re-probed (FR-010).
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new
  infrastructure. The Observability section below freezes the existing contracts and
  introduces **no new runtime signals** (justified there); enforcement of the new
  rules is build-time (ArchTests in the standard PR pipeline), which is the CI gate
  this principle demands for the naming/duplication conventions.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS. Pure harness
  restructuring: no wiki-content judgment enters backend code; instruction files are
  not modified (their loading code is already shared and stays byte-compatible);
  the guarded tool boundary and its structural tests remain in force, including the
  Query no-write rule per FR-004.

No unjustified violations → Complexity Tracking not needed. **Post-Phase-1 re-check**:
design artifacts (data-model.md, quickstart.md) introduce no new violation; the only
open gate item remains ADR-013 acceptance.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.* (All
of ADR-001 through ADR-012 were read; ADR-013 is drafted by this plan.)

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | Consolidation stays on .NET 10/xUnit; no new language, framework, or frontend change. |
| ADR-002 | Ingest agent execution model | The standalone-child-process spawn contract (CLI args in, artifacts/events out) is untouchable; the platform library must sit beneath it, and hosts remain independently containerizable executables. |
| ADR-003 | Domain vs. operational state persistence | Unchanged; the feature adds no stored state. The convention document is project documentation in `docs/`, not domain or operational state. |
| ADR-004 | Credential scoping | Per-spawn credential injection and per-agent model env-var names (`GRIMOIRE_QUERY_MODEL`/`GRIMOIRE_QUERY_BASE_URL` vs Ingest defaults) are preserved as **profile inputs** to the shared `ModelClientFactory` — consolidation must not merge the two agents' credential/config scopes. |
| ADR-005 | Observability backend | OTel SDK + OTLP + in-memory-exporter CI assertions stay; the consolidated `AgentTelemetryBootstrap` must reproduce today's provider wiring exactly (same resources, sources, meters), verified by the untouched existing observability tests. |
| ADR-006 | Agent tool-use loop and guarded tool boundary | `GuardedToolExecutor`/policy evaluation is already platform code and must remain the single physical chokepoint; per-agent tool registries stay profile artifacts; the guarded-write structural rule survives the renames with a re-probe (FR-010). |
| ADR-007 | Agent instruction surface | One system-prompt document (+ default-user-prompt for Ingest), fail-closed loading, SHA-256 recording — unchanged; the platform host template must sequence loading identically. Instruction folders `data/agents/<agent>/` already satisfy the naming convention. |
| ADR-008 | Agent event channel, run supervision, run queue | NDJSON stdout events, heartbeat cadence, supervision semantics, and Ingest's single-slot FIFO queue are frozen; the platform host template must reproduce the instructions→`started`→heartbeat sequencing byte-compatibly. |
| ADR-009 | Runtime path configuration | No ambient discovery may creep in during consolidation; worker-binary and instruction paths keep flowing Hub→CLI; no new runtime locations are introduced. |
| ADR-010 | Hexagonal ports and adapter namespaces | Port ownership and `.Adapters.` containment (C1–C5) remain in force; Hub namespace renames must keep `IAgentProcessLauncher` + `Adapters.AgentProcess` in cross-agent `Grimoire.Hub.AgentDispatch` so C4/C5 and the port table stay valid; moved rules are re-probed. |
| ADR-011 | Query agent shared runtime and concurrency model | Baseline being extended: streaming, bounded concurrency, interruption, artifact ownership, and ports table stay intact; its packaging/runtime-sharing aspects and the "no write tool compiled in" framing are superseded by ADR-013 (FR-004's profile-scoped formulation). |
| ADR-012 | Standalone eval runner and recorded replay | Replay suite is part of the frozen verification baseline; renames must not change scenario ids, recording paths, or staleness fingerprints; the shared `ModelClientFactory` implements ADR-012's selection semantics once, still invoked per host composition root (D2). |
| ADR-013 | Unified agent platform packaging and naming (new, this plan, **proposed**) | Fixes: shared-library-under-thin-hosts packaging (rejects single parameterized host for now), Agent Profile shape, `Telemetry`/`Composition`/`Host` platform namespaces, the naming convention + `docs/conventions/agent-artifact-naming.md` + rename map, and structural rules N1/D1/D2. |

**New ADR required?**: Yes —
`docs/adr/ADR-013-unified-agent-platform-packaging-and-naming.md`, drafted as part of
this plan in **`proposed`** status. It MUST be moved to **Accepted** by explicit
project-owner sign-off before `/speckit-tasks` runs (see Constitution Check).
Conversation/turn-persistence questions are deliberately left to the sibling feature
011 plan (ADR-014) and are not decided by ADR-013.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface is added or changed — this is a harness-only restructuring.
Instruction files (`data/agents/ingest/*`, `data/agents/query/*`) are not modified;
agent judgment, prompts, policies, and tool sets are byte-identical before and after.
For completeness, the capabilities this feature *moves* (not changes) are all
harness-side:

| Capability | Side | Where it lives (after) |
|------------|------|------------------------|
| Telemetry bootstrap / tracing scaffold for agent processes | Harness | `Grimoire.AgentRuntime.Telemetry` (was: duplicated per host) |
| Model-adapter selection (replay/capture/live, ADR-012) | Harness | `Grimoire.AgentRuntime.Composition.ModelClientFactory`, invoked from each host composition root |
| Startup sequencing (fail-closed instruction load → started → heartbeat) | Harness | `Grimoire.AgentRuntime.Host.AgentHost` template |
| Agent Profile declaration (identity, tool set, instruction requirements, env-var names) | Harness | Each host's composition root (`Grimoire.IngestAgent`, `Grimoire.QueryAgent`) |
| Intent-specific artifact handling (task artifact/rollback vs conversation scaffold) | Harness | Host assemblies (unchanged code, unchanged behavior) |
| Guarded tool enforcement, deny-by-default policy | Harness | `Grimoire.AgentRuntime.Guardrails` (already shared — unchanged) |
| Wiki-content judgment (all of it) | Agentic core | `data/agents/<agent>/system-prompt.md` etc. — **untouched by this feature** |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

All criteria are deterministic guarantees (the spec declares no agent-judgment
criteria for this feature); no live LLM calls or credentials are needed anywhere.

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (each platform concern exists exactly once, probe-verified) | Deterministic guarantee | Structural arch tests **D1** (OTel bootstrap containment) + **D2** (model-adapter composition containment), each Red/Green probed; plus deletion of the duplicated scaffolds verified by compilation | None (Mono.Cecil IL scan) | Deliberate-violation probe classes (introduced, observed failing, removed) | D1/D2 pin the two concerns that demonstrably drifted; the remaining concerns are already single-implementation in `Grimoire.AgentRuntime` and stay covered by existing arch rules |
| SC-002 (100% naming-convention compliance, automated check in standard pipeline, probe-verified) | Deterministic guarantee | Structural arch test **N1** (reference-based ownership + Hub namespace-ownership map + documented exemptions), Red/Green probed; convention-document existence/content asserted by the same test fixture | None | Deliberately misnamed probe type referencing exactly one agent | Runs in the standard PR pipeline (`Grimoire.ArchTests` already gates CI, Principle IV) |
| SC-003 (full pre-existing suite passes, no weakened assertions; artifacts/events structurally identical) | Deterministic guarantee | The entire existing suite re-run unchanged: hermetic integration tests (fakes: `FakeModelClient`, `FakeAgentProcess`), arch tests, domain unit tests, and the ADR-012 replay eval suites | Existing fakes + versioned recordings under `data/evals/recordings/` (no provider) | Existing fixtures; existing recordings (fingerprints must remain green — research.md R7) | Test-code diffs are limited to FR-006 renames + mechanical namespace updates; PR review enforces the no-weakening rule against the diff; green replay = artifact/event/prompt byte-compatibility proof |
| SC-004 (capabilities = declared profile; Query no-write guarantee stays probe-enforced) | Deterministic guarantee | Existing `QueryReadOnlyGuardrailTests` (hermetic) + `QueryAgentGuardedWriteBoundaryRuleTests` (structural, re-probed after moves) + a new profile-fidelity assertion (each host's registered tools == its profile declaration) | `FakeModelClient` scripted to request out-of-profile tools | Existing query policy fixture + scripted tool-use sequences | FR-004: rule stays in force verbatim while the Query profile declares no write tool; feature 012 renegotiates it under its own ADR |
| SC-005 (feature 013 adds zero duplicated platform code) | Deterministic guarantee (deferred measurement) | Measured when feature 013 lands: its diff is checked against D1/D2 (which make scaffold duplication a build failure) and reviewed against FR-003 | — | — | Not verifiable inside this feature; this plan's deliverable is that D1/D2 + the `AgentHost`/`AgentProfile` seam exist so 013's plan can cite them; recorded as an explicit gate for feature 013's plan |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

**This feature freezes existing observability identities and introduces no new
runtime signals.** Rationale: the feature's only observable-behavior requirement is
that nothing observable changes (FR-008; spec edge case "established per-agent
observability identities … remain unchanged"). Its own new guarantees (naming,
non-duplication) are enforced at build time by ArchTests in the standard PR pipeline —
they have no runtime execution path and therefore, by design, no runtime signal.
Adding metrics/logs/spans here would itself violate FR-008's frozen-identity
constraint.

### Preserved observability contract (must survive consolidation byte-identically)

| Contract | Source of truth | Preserved identities (non-exhaustive anchors) | Verified by |
|----------|-----------------|-----------------------------------------------|-------------|
| Ingest agent + Hub ingest pipeline metrics/log events/spans | `specs/002-agentic-ingest-core/plan.md`, `specs/004-ingest-agent-systemprompt/plan.md` ## Observability | OTel service/source/meter `Grimoire.IngestAgent`; spans `ingest_agent.run` / `.load_instructions` / `.model_turn` / `.tool_call` / `.finalize_artifact` / `.rollback` with `task_id`; all `IngestAgentMetrics`/`IngestAgentLogEvents` names + mandatory fields | Existing in-memory-exporter integration tests (`ObservabilityLogTests`/`ObservabilityMetricsTests`/`ObservabilityTraceTests` → renamed `IngestObservability*Tests`, assertions untouched) |
| Query agent + Hub query pipeline metrics/log events/spans | `specs/008-query-agent/plan.md` ## Observability | OTel service/source/meter `Grimoire.QueryAgent`; spans `query_agent.*` and `hub.query.*` with `turn_id`; `query.*` metric and `query.*` log-event names + mandatory fields | Existing `QueryLifecycle{LogEvent,Metrics,Trace}Tests` and agent-side tests, assertions untouched |
| Hub request tracing, eval-runner telemetry | `specs/006-hexagonal-arch-tasks-ui/plan.md`, `specs/009-agent-eval-replay/plan.md` | `HubTracing`/`HubMetrics`, `EvalRunnerTelemetry` identities | Existing `HubRequestTracingTests`, `EvalRunnerObservabilityTests`, `EvalObservabilityTests`, assertions untouched |

The consolidated `AgentTelemetryBootstrap`/`AgentTracing` take these identities as
frozen profile inputs (ADR-013); any identity drift fails the existing tests.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| *(none new)* | — | No new runtime signal; all existing metrics preserved per the table above | — |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| *(none new)* | — | No new runtime signal; all existing log events preserved per the table above | — |

**Derivation rule (MANDATORY)**: zero new rows ⇒ no new logging-contract
implementation/test/CI tasks are generated. Instead, `tasks.md` MUST include (a)
regression tasks asserting the existing logging/trace tests still run in the standard
PR pipeline after the renames, and (b) the final-phase completeness-audit task
cross-referencing every preserved-contract row above against its (existing, renamed)
passing test.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| *(none new)* | — | No new runtime signal; all existing spans (incl. parentage `ingest_agent.model_turn` → `ingest_agent.run`, `query_agent.tool_call` → `query_agent.model_turn`, and `task_id`/`turn_id` correlation) preserved per the table above |

**Derivation rule (MANDATORY)**: zero new rows ⇒ same treatment as log events: no new
trace-contract tasks; existing trace tests must remain in the standard PR pipeline
and be confirmed by the final-phase completeness audit.

## Project Structure

### Documentation (this feature)

```text
specs/010-unified-agent-platform/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (conceptual entities of the restructuring)
├── quickstart.md        # Phase 1 output (validation guide)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

`contracts/` is deliberately omitted: this feature exposes **no new external
interface** — every existing contract (ingest/query HTTP + SignalR surfaces, NDJSON
run events, task/query-run artifact schemas, eval CLI, recording format) is frozen
and already documented under `specs/002|004|008|009/*/contracts/`. The Agent
Profile/`AgentHost` seam is internal to the backend and is specified in
`data-model.md` instead.

### Source Code (repository root)

```text
backend/src/
├── Grimoire.Domain/                          # unchanged
├── Grimoire.AgentRuntime/                    # the Agent Platform (extended, ADR-013)
│   ├── Core/                                 # AgentLoop, IModelClient (+ Adapters/Anthropic, Adapters/Replay) — unchanged
│   ├── Guardrails/                           # GuardedToolExecutor, ToolRegistry, WriteJournal — unchanged
│   ├── RunEvents/                            # RunEventEmitter — unchanged
│   ├── Instructions/                         # SystemPromptLoader, PolicyLoader — unchanged
│   ├── Telemetry/                            # NEW: AgentTelemetryBootstrap, AgentTracing (frozen identities as inputs)
│   ├── Composition/                          # NEW: ModelClientFactory, ErrorSanitizer, AgentArgumentReader
│   └── Host/                                 # NEW: AgentProfile, AgentHost (startup/shutdown template + intent hooks)
├── Grimoire.IngestAgent/                     # thin host: profile + intent handling only
│   ├── Program.cs                            # composition root: IngestProfile + hooks + AgentHost
│   ├── IngestCliOptions.cs                   # renamed from AgentCliOptions.cs
│   ├── IngestToolRegistry.cs                 # NEW: explicit profile tool set (list/read/write — unchanged tools)
│   ├── IngestAgentInstrumentation.cs         # AgentCore/ namespace dissolved
│   ├── IngestAgent{Metrics,LogEvents,Tracing}.cs  # frozen identities (Tracing now delegates to platform)
│   ├── TaskArtifact/ · IngestLog/ · Source/  # intent-specific artifact handling — unchanged
│   └── (TelemetryBootstrap.cs DELETED → platform)
├── Grimoire.QueryAgent/                      # thin host: profile + intent handling only
│   ├── Program.cs · QueryCliOptions.cs · QueryToolRegistry.cs
│   ├── QueryAgent{Instrumentation,Metrics,LogEvents,Tracing}.cs   # frozen identities
│   └── (QueryAgentTelemetryBootstrap.cs DELETED → platform)
├── Grimoire.EvalRunner/                      # ScenarioDefinitions → IngestScenarioDefinitions (ids/paths unchanged)
└── Grimoire.Hub/
    ├── AgentDispatch/                        # cross-agent only: IAgentProcessLauncher, AgentRunEvent, Adapters/AgentProcess
    ├── IngestDispatch/                       # NEW namespace: IngestRunCoordinator, IngestAgentRequest (moved)
    ├── QueryDispatch/                        # + QueryAgentRequest (moved in)
    ├── IngestSubmission/                     # absorbs Grimoire.Hub.Submission
    ├── IngestTaskArtifact/                   # renamed from TaskArtifact/
    └── (QueryRunArtifact/, QuerySubmission/, Realtime/, Runtime/, OperationalState/, ContentRoot/, Conversion/ unchanged)

backend/tests/
├── Grimoire.ArchTests/                       # + N1 (naming), D1, D2 (each Red/Green probed);
│   │                                         #   GuardedWriteBoundaryRuleTests → IngestAgentGuardedWriteBoundaryRuleTests (re-probed)
├── Grimoire.IntegrationTests/                # FR-006 renames (Ingest* prefixes per research.md R5); assertions untouched
├── Grimoire.AgentEvals/                      # ReplayEvalTests.cs → IngestReplayEvalTests.cs; infra tests stay unprefixed
└── Grimoire.Domain.UnitTests/                # unchanged

docs/
├── adr/ADR-013-unified-agent-platform-packaging-and-naming.md   # this plan's ADR (proposed)
└── conventions/agent-artifact-naming.md      # NEW: convention + old→new rename map (FR-005)

data/agents/{ingest,query}/                   # instruction surfaces — untouched, already convention-conformant
data/evals/recordings/                        # untouched (scenario ids/paths frozen, research.md R7)
```

**Structure Decision**: Existing `backend/` solution shape retained — no project
added or removed (ADR-013 packaging decision). Changes are: three new namespaces
inside `Grimoire.AgentRuntime`, deletion of duplicated host scaffolds, the
research.md R5 rename/move set, two new docs (`ADR-013`, the convention document),
and three new arch rules. Frontend, Hub endpoints/routes, instruction files, and all
persisted formats are untouched.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — not applicable. (The single conditional item — ADR-013 in `proposed`
status pending owner sign-off — is a workflow gate, not a principle violation.)

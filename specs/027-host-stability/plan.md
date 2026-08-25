# Implementation Plan: Host Stability Guarantee for Agent Runs

**Branch**: `027-host-stability` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/027-host-stability/spec.md`

## Summary

Constitution v1.12.0's Host stability guarantee, as corrected on 2026-08-25, requires
the harness to ensure an agent process cannot corrupt the host — by writing outside its
guarded roots, or by causing an unsanctioned process to run — regardless of task or
instruction-file content. A pre-drafting research pass found both mechanisms already
correct for the plain cases: `GuardedToolExecutor`'s canonicalize-then-match path
resolution (ADR-006) and the two existing process-spawn sites (`AgentProcessHost`,
`MarkItDownConverter`, ADR-002), which already use fixed executables and argument-list
invocation. This feature is therefore a hardening/structural-enforcement feature, not a
new subsystem: it closes residual adversarial-input gaps in path resolution (chained
symlinks, a symlink swapped in after validation, null-byte truncation) with a classicist
behavioral test suite, and turns the spawn sites' already-safe-by-construction property
into a structurally enforced, Red/Green-probed invariant so it cannot silently regress.

## Technical Context

**Language/Version**: C# / .NET (per `backend/global.json`) — unchanged, no new stack.

**Primary Dependencies**: Mono.Cecil (already a `Grimoire.ArchTests` dependency, used
for the new structural spawn-site test); no new package.

**Storage**: N/A — this feature persists no new state; it hardens in-memory decision
logic (`GuardedToolExecutor`) and a structural code invariant.

**Testing**: xUnit, classicist/state-based (Constitution Principle II) — extends
`Grimoire.IntegrationTests` (real filesystem, real symlinks) and `Grimoire.ArchTests`
(Mono.Cecil IL scan, matching the existing `NonBlockingDispatchRuleTests` idiom).

**Target Platform**: Linux server (unchanged, ADR-001).

**Project Type**: Backend harness feature — no frontend surface, no agentic surface.

**Performance Goals**: N/A — no new hot path; path resolution's added recursion is
capped at 40 hops (research.md D2), bounded and negligible relative to existing I/O.

**Constraints**: Verification MUST be hermetic (no live LLM calls, no API key —
Principle II); the guarantee MUST hold even when an agent is actively misbehaving
(spec.md FR-007) — never proven by agent-behavior evaluation.

**Scale/Scope**: Two production types (`GuardedToolExecutor`, `AgentProcessHost` +
`MarkItDownConverter`), one existing validator (`IngestSubmissionValidator`); no new
type, port, or adapter namespace.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Domain Architecture, Hexagonal Boundaries)**: PASS. No new external
  system, port, or adapter is introduced. `GuardedToolExecutor` stays in
  `Grimoire.AgentRuntime.Guardrails`; `AgentProcessHost`/`MarkItDownConverter` stay in
  their existing adapter namespaces (ADR-010 containment). No persistence exemption
  question arises — no new store.
- **Principle II (Pragmatic Testing Strategy, classicist TDD)**: PASS. All new tests are
  hermetic, state-based integration tests against a real filesystem/real symlinks and
  the real compiled assembly (Mono.Cecil scan of `Grimoire.Hub.dll`) — no mocking
  framework, no interaction verification. All success criteria are deterministic 100%
  harness guarantees (spec.md Success Criteria); no agent-judgment criterion exists, so
  no high-stakes/lower-stakes classification and no eval suite is needed.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: New ADR required —
  ADR-034 (drafted alongside this plan, Accepted). Phase 0 covers ADR-034's two
  Boundary Rules (R1/R2) with a Red/Green-probed structural test; its two
  Feature-Scoped Invariants (R3/R4) are covered by classicist behavioral tests in their
  normal implementation phase, per Principle III's explicit prohibition on giving an
  FSI a reflection/IL-based test.
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new business metric
  or trace span is introduced; this feature adds label values (new denial reasons) to
  the existing `wiki.<agent>.actions_denied_total` counter and `RecordDenied`/tool-call
  span machinery already emitted by every guarded tool call — see Observability below.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS — "No agentic surface,
  harness-only feature" (below). This feature makes no wiki-content judgment; it only
  strengthens a harness-owned guardrail boundary and a harness-owned process-spawn
  invariant.

No violations requiring justification — Complexity Tracking is not filled.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-002 | Ingest Agent Execution Model | Establishes the child-process spawn model (fixed executable, CLI args, credential scoping) this feature's spawn-site registry (R1/R2) structurally pins as a closed, argument-list-only set. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | Establishes `GuardedToolExecutor`'s canonicalize-then-match design (ADR-006, amended by ADR-009/ADR-030/ADR-031) that this feature hardens (R3) — not a rewrite, a closing of residual adversarial-input gaps in the same mechanism. |
| ADR-010 | Hexagonal Ports and Adapter Namespaces for External Systems | Confirms `AgentProcessHost`/`MarkItDownConverter` already sit in their designated adapter namespaces; this feature adds no new namespace and must not move either type. |
| ADR-013 | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | `ArchScan.cs`'s IL-scan helpers (reused by this feature's new structural test) were built for ADR-013's D1/D2 rules; this feature is the second consumer of that shared scanning idiom. |
| ADR-032 | Behavioral Enforcement for Feature-Scoped Path-Surface Invariants | Establishes the Boundary-Rule-vs-Feature-Scoped-Invariant classification and the rule that an FSI must never get a reflection/IL test — directly governs how ADR-034 classifies and enforces R1–R4 in this feature. |
| ADR-034 | Path and Subprocess Containment Hardening (new, this feature) | The feature's own ADR — see `docs/adr/ADR-034-path-and-subprocess-containment-hardening.md`. Names R1/R2 (Boundary Rules, structural test) and R3/R4 (Feature-Scoped Invariants, classicist tests). |

**New ADR required?**: Yes. R1/R2 (spawn-site registry, argument-list-only invocation)
are Boundary Rules that no existing ADR names: `ADR-002` documents the agent spawn
model (child process, CLI args, credential scoping) but never names injection safety —
which type may construct a process — as a concern; `ADR-006` documents the guarded-tool
boundary's path-canonicalization design but never addresses subprocess safety at all.
Per Principle III, "if `plan.md` introduces a new structural boundary... not covered by
existing ADRs, the agent MUST draft a new ADR," and separately, "a Boundary Rule named
in an Accepted ADR, without a corresponding automated structural enforcement test, MUST
NOT be referenced as an active architectural constraint" — meaning `tasks.md`'s Phase 0
task cannot cite R1/R2 as something it tests until an ADR names them. Drafted as
`docs/adr/ADR-034-path-and-subprocess-containment-hardening.md`, status `accepted`
(this project's established solo-operator sign-off convention, matching
ADR-032/ADR-033). `docs/adr/index.md` updated in the same change. (R3/R4 are
Feature-Scoped Invariants, not Boundary Rules, and do not independently require an
ADR — they are folded into ADR-034 because the feature's containment topic covers both
halves together.)

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. This feature makes no wiki-content judgment
and adds no new agent capability; it strengthens the harness-owned guarded-tool boundary
(`GuardedToolExecutor`, already the sole physical chokepoint every agent write/read/
delete passes through, per ADR-006) and a harness-owned structural invariant over which
backend types may spawn a process. No instruction file changes; no agent instruction
surface is touched.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 — 100% of adversarial path variants denied, zero out-of-root actions | Deterministic guarantee | Hermetic classicist integration test extending `PathTraversalTests` (`Grimoire.IntegrationTests`) | Real temp-directory filesystem, real `File.CreateSymbolicLink` (no doubles) | Per-variant fixtures: `../` traversal (existing), absolute override (existing), single symlink (existing), chained/nested symlink (new), percent-encoded string (new), Unicode-confusable string (new), embedded-NUL string (new), post-validation symlink swap (new — swap the link between the policy check and the mutating call) | Asserts denial AND that the out-of-root file's content is byte-identical before/after (proves containment, not just an error code) |
| SC-002 — 100% of spawn call sites covered by the enumerated set; unlisted site fails 100% of Red/Green probes | Deterministic guarantee | Hermetic architecture test (`Grimoire.ArchTests`), Mono.Cecil IL scan of `Grimoire.Hub.dll` | None — scans the real compiled assembly | Red/Green probe: a deliberately added violating `Process.Start` call site, verified to fail the test, then removed | New test, modeled on `NonBlockingDispatchRuleTests`; Phase 0 task per Boundary Rule R1 |
| SC-003 — 100% of spawned-process invocations use `ArgumentList`; 0 use a shell-interpreted string | Deterministic guarantee | Same architecture test as SC-002 (sibling assertion) | None | Same Red/Green probe, second violating call site (one using `.Arguments =`) | Phase 0 task per Boundary Rule R2 |
| SC-004 — 100% of filename-/content-derived values validated against a fixed allowlist before use | Deterministic guarantee | Hermetic classicist integration test against `IngestSubmissionValidator` (`Grimoire.IntegrationTests`) | Real validator, no doubles | An unlisted extension (`.exe`, `.sh`) submitted through the existing submission validation entry point | Feature-Scoped Invariant (R4); covered in its normal implementation phase, not Phase 0 |

No agent-judgment success criterion exists in this feature — every row above is a
deterministic guarantee per Constitution Principle II's success-criteria split; no eval
suite is in scope.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

This feature adds no new metric, log event, or span — it adds new **label/reason
values** to signals every guarded tool call already emits (`IToolCallInstrumentation.
RecordDenied`, wired through each agent's own `*AgentInstrumentation` implementation,
e.g. `IngestAgentInstrumentation`). Enumerated here because the Observability section is
mandatory regardless, and so the completeness-audit task (Principle III) has a concrete
row to check off.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `wiki.<agent>.actions_denied_total` (existing, e.g. `wiki.ingest.actions_denied_total`) | Counter | Already emitted on every guardrail denial | `reason` gains three new values this feature introduces: `malformed_path`, `symlink_loop`, `revalidation_failed` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| Existing denial log event (per `IToolCallInstrumentation.RecordDenied`'s implementation per agent) | WARN | A guarded tool call denied for any reason, including the three new ones above | `task_id`/`turn_id`/`run_id` (per agent), `tool`, `requested_target`, `canonical_target`, `reason`, `turn` — unchanged shape, `reason` gains the three new values |

**Derivation rule check**: no new event name is introduced, so no new implementation/
test/CI-enforcement task triple is required by the mandatory derivation rule — the
existing logging-contract tests already cover this event's name, level, and mandatory
fields; this feature's tasks only need to prove the three new `reason` values are
reachable (covered by the SC-001 test rows above, which trigger each new denial path).

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|------------|
| Existing `*_agent.tool_call` span (per ADR-006/ADR-030 tool-call instrumentation) | Existing agent run span | Unchanged shape — `reason` tag gains the same three new values on a denied call |

No new span is introduced; the derivation rule likewise requires no new task triple.

## Project Structure

### Documentation (this feature)

```text
specs/027-host-stability/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

No `contracts/` directory: this feature exposes no external API, CLI surface, or
agent-facing tool contract — `data-model.md`'s Spawn-site registry table serves the same
"reviewed, enumerated contract" role that a `contracts/` file would for an external
interface, in-repo where the structural test consumes it.

### Source Code (repository root)

**Structure Decision**: Single existing backend solution (`backend/Grimoire.sln`,
Option 1 shape already established by prior features) — no new project. Production
changes land in two existing files:

```text
backend/
├── src/
│   └── Grimoire.AgentRuntime/
│       └── Guardrails/
│           └── GuardedToolExecutor.cs      # R3: chained-symlink recursion, revalidation,
│                                            # controlled null-byte denial (research.md D1-D3)
└── tests/
    ├── Grimoire.IntegrationTests/
    │   ├── PathTraversalTests.cs           # R3: extended with the new adversarial variants
    │   └── IngestSubmissionValidatorAllowlistTests.cs  # R4: new, dedicated allowlist test (new file)
    └── Grimoire.ArchTests/
        └── SpawnSiteRegistryRuleTests.cs   # R1/R2: new structural test (new file, mirrors
                                             # NonBlockingDispatchRuleTests)
```

`AgentProcessHost.cs` and `MarkItDownConverter.cs` themselves require no production
change — R1/R2 pin their already-correct behavior; only the new structural test file is
added.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — not applicable.

# Implementation Plan: Fast Developer Feedback Tier for the Backend Test Suite

**Branch**: `019-fast-test-tier` | **Date**: 2026-08-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-fast-test-tier/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

The backend test suite (four projects: `Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`,
`Grimoire.IntegrationTests`, `Grimoire.AgentEvals`) currently offers no fast inner loop —
every documented path runs everything, ~300s locally. A direct audit (clarification
session 2026-08-03, corrected against issue #44's assumptions) found the real cost drivers
are not edge-case bloat or 146 fixed waits, but two mechanical facts: (1)
`Grimoire.IntegrationTests`' `xunit.runner.json` sets `parallelizeTestCollections: false`,
serializing all 120 test classes even though only one needs that isolation; and (2)
`Grimoire.AgentEvals`' `EvalRunnerProcessTests` collection disables parallelization across
all nine of its process-touching classes, while the true replay-eval scenarios internally
spawn their ~235 agent-process samples one at a time. The plan: (a) introduce a
`Tier` xUnit trait so the ~45 hermetic harness-mechanics tests inside
`Grimoire.AgentEvals` can join a documented, single-command Fast tier alongside
`Grimoire.Domain.UnitTests`/`Grimoire.ArchTests` without moving any file or namespace
(FR-014); (b) flip `Grimoire.IntegrationTests`' collection-parallelization default and
formalize its existing ad hoc poll-loop pattern into one shared helper, triaging the ~4
genuinely fixed waits found by the audit; (c) split `Grimoire.AgentEvals`' serialized
collection so hermetic tests parallelize freely and only the five genuine replay-eval
classes keep a (now un-disabled) collection, while each scenario's internal sample loop
spawns agent processes concurrently instead of sequentially; (d) add one new
`Grimoire.ArchTests` IL-scan rule banning fixed unconditional waits in deterministic-tier
test code outside an allow-listed poll helper or an explicit `TimingDependent` marker; and
(e) replace CONTRIBUTING.md's stale `GRIMOIRE_EVAL=1` claim and the dead
`EvalFactAttribute`/`EvalGate` code it describes, and drop the unused `Testcontainers`
package reference from `Grimoire.IntegrationTests.csproj`. No test is deleted or weakened
(FR-008); the four suites still gate every merge unchanged.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), xUnit test projects under
`backend/tests/`.

**Primary Dependencies**: No new package dependencies. Uses xUnit's existing trait
(`[Trait]`) and collection (`[CollectionDefinition]`/`[Collection]`) mechanisms, and
`Mono.Cecil` (already a `Grimoire.ArchTests` dependency, used by
`RuntimePathsBoundaryRuleTests` and others) for the new IL-scan rule. Removes the unused
`Testcontainers` package reference from `Grimoire.IntegrationTests.csproj` /
`backend/Directory.Packages.props` (FR-015).

**Storage**: N/A — no persisted or runtime application state is touched. This feature is
entirely test-project organization, CI-pipeline-adjacent tooling, and documentation.

**Testing**: The feature's own subject is the test suite — xUnit across
`Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, `Grimoire.IntegrationTests`,
`Grimoire.AgentEvals`. Verification of this feature's own success criteria is via a new
`Grimoire.ArchTests` structural rule (fixed-wait ban), a timed before/after comparison
against the recorded baselines (SC-001/SC-003/SC-008), and count/parity assertions
(SC-002/SC-006/SC-007) — see Test Strategy below.

**Target Platform**: Same as the existing backend — developer workstation (local `dotnet
test`) and the GitHub Actions `ubuntu-latest` CI runner (`.github/workflows/ci.yml`).

**Project Type**: Existing .NET solution (`backend/Grimoire.slnx`) — no new project is
added; one new test file per targeted assembly plus one new `Grimoire.ArchTests` rule
file.

**Performance Goals**: SC-001 (fast-tier test execution ≤ 5s, excluding build); SC-003
(`Grimoire.IntegrationTests` wall clock ≥ 30% below the recorded 61s baseline, executed
test count ≥ 583); SC-008 (`Grimoire.AgentEvals` wall clock ≥ 50% below the recorded
~190s baseline, zero skipped tests, identical sample count/scorers/thresholds to a
sequential run). All targets are anchored to the same reference environment as the issue
#44 baseline measurements (spec Assumptions).

**Constraints**: FR-008 — no test may be deleted or weakened, and every suite that gates
merges today (`ci.yml`'s four `dotnet test` steps) must continue to gate merges. FR-002 —
the fast tier must run with zero evaluation-suite prerequisites (no recordings, no
provider credential). FR-012 — replay-eval concurrency must not change sample counts,
scorers, scores, or thresholds; every sample keeps full isolation.

**Scale/Scope**: `Grimoire.IntegrationTests` — 120 test classes, 1 existing serialized
collection, ~4 true fixed waits among 53 `Task.Delay`/`Thread.Sleep` occurrences (audit
finding). `Grimoire.AgentEvals` — 66 facts across ~13 classes, 9 of which currently share
one serialized collection; ~45 facts are hermetic harness-mechanics tests, ~26 are genuine
replay-eval scenarios across 5 classes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal/DDD)**: N/A — no new external system, port, or production-code
  namespace is introduced; this feature is scoped to test-project organization and one
  new test-scanning `Grimoire.ArchTests` rule. **Pass.**
- **Principle II (Pragmatic Testing Strategy)**: Directly operationalizes this principle —
  formalizes the harness-contracts-vs-agent-behavior split already required (FR-014 moves
  hermetic mechanics tests out of the slow eval tier; the replay-eval tier keeps its
  evaluation-style verification, ADR-012, untouched). No hermetic test gains a live LLM
  dependency; no agent-judgment outcome is downgraded to a deterministic assertion, or
  vice versa. **Pass.**
- **Principle III (ADR-Driven & Test-Enforced)**: All 19 existing ADRs read (Phase 0,
  below). This feature introduces a cross-cutting testing convention (tier-membership
  trait, deterministic-wait convention, its structural enforcement rule) no existing ADR
  covers, so **ADR-021 is drafted** (`docs/adr/ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md`,
  status `accepted`, sign-off 2026-08-03) as required. Its structural rule
  (`DeterministicTierNoFixedWaitRuleTests`) ships with a Red/Green probe as the first
  `tasks.md` task, per the constitution's mandatory ordering. **Pass.**
- **Principle IV (Behavioral & Observable Engineering)**: See Observability section below
  — N/A with justification: this feature adds no production business logic, no request
  path, and no new runtime signal; it is test-infrastructure and CI/documentation work.
  **Pass.**
- **Principle V (Agentic Core & Deterministic Harness)**: No agentic surface — see
  Agentic Boundary section below. **Pass.**

No violations requiring Complexity Tracking justification.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-002 | Ingest Agent Execution Model | Confirms the replay-eval tier's process-per-sample spawn shape (child process per unit of work) is the established pattern; concurrent sample execution (FR-012) parallelizes *how many* of these spawns run at once, not the spawn contract itself. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | The hermetic tests being re-tiered (FR-014) include guardrail/policy-adjacent harness-mechanics tests; re-tiering changes only which `dotnet test` invocation selects them, never the guarded-tool-boundary behavior itself. |
| ADR-009 | Runtime Path Configuration | Supplies the exact IL-scan idiom (`RuntimePathsBoundaryRuleTests` — Mono.Cecil, allow-listed call sites, Red/Green probe) this feature's new `DeterministicTierNoFixedWaitRuleTests` rule reuses verbatim. |
| ADR-011 | Shared Agent Runtime, Streaming, and Query Concurrency Model | Establishes that Query/Ingest agent processes are independently spawnable and safe to run concurrently under bounded limits — the same reasoning this feature applies to concurrent replay-eval sample spawning (FR-012), which reuses `EvalWorkspace`'s existing per-sample isolation rather than inventing a new one. |
| ADR-012 | Standalone Eval Runner and Recorded-Replay at the Model Port | Fixes the replay-eval tier's semantics (sample counts, scorers, fingerprint-gated staleness) that this feature's concurrency change (FR-012/FR-013) MUST leave byte-identical; also fixes `EvalWorkspace`'s per-sample isolation contract, which this feature's copy-parallelization (R7) and concurrent spawning (R6) both build on without altering. |
| ADR-013 | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | Its N1 naming/namespace-ownership rule is the reason this feature chose a trait-based tier-membership mechanism (ADR-021) instead of physically relocating the ~45 hermetic `Grimoire.AgentEvals` tests — a physical move would need new namespace-ownership entries this feature has no reason to introduce. |
| ADR-021 (new, this feature) | Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers | Defines the `Tier`/`TimingDependent` trait vocabulary, the fast-tier command composition, the `Grimoire.IntegrationTests` collection-parallelization decision, and the `Grimoire.AgentEvals` collection split — the structural backbone this entire feature implements. |

All other ADRs (001, 003, 004, 005, 007, 008, 010, 014–019) read and confirmed not to
apply: none govern test-project organization, test-suite tiering, or CI test-selection
tooling, and this feature adds no external-system dependency, no persistence, no agent
instruction-surface change, and no production namespace.

**New ADR required?**: Yes — ADR-021 was drafted as part of this planning run and
reached **Accepted** status via project-owner sign-off on 2026-08-03, per Constitution
Principle III and the Spec-Kit Workflow's mandatory step 4. `/speckit-tasks` may proceed.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. This feature reorganizes and speeds up how
tests are *selected and executed*; it changes no wiki content, no agent instruction file,
no guardrail/policy behavior, and no agent-judgment outcome. The replay-eval tier's
scorers, thresholds, and recorded samples (agentic-behavior verification, per Principle
II) are explicitly required to stay byte-identical under concurrency (FR-012) — this
feature touches only the *execution strategy* around that verification, never its
judgment content.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001: fast-tier execution ≤ 5s (excl. build) | Deterministic guarantee | Timed run of `scripts/test-fast.sh` on the reference environment | None — the point is zero external prerequisites | N/A | Manual/quickstart-validated timing comparison, not an ongoing CI performance gate (spec Assumptions) |
| SC-002: 100% of fast-tier runs execute zero eval tests; runs with no eval prerequisites available | Deterministic guarantee | Hermetic test: assert `Tier=Fast`-filtered `Grimoire.AgentEvals` run contains no replay-eval class name; run the fast tier in an environment with `data/evals/recordings/` absent and no provider credential set | None | Environment with recordings/credentials deliberately removed | Directly exercises FR-002 |
| SC-003: `Grimoire.IntegrationTests` wall clock ≥ 30% below 61s baseline, test count ≥ 583 | Deterministic guarantee | Timed full-project run compared against the recorded baseline; `dotnet test`'s own summary line for executed count | Real infrastructure per existing tests (Kestrel, SignalR, fake/real agent processes) — unchanged | Existing fixtures, unchanged | Verifies FR-011's levers (parallelization + wait triage) collectively hit the target |
| SC-004: zero fixed unconditional waits outside `TimingDependent`-marked tests | Deterministic guarantee | New `Grimoire.ArchTests` rule `DeterministicTierNoFixedWaitRuleTests` (Mono.Cecil IL scan), Red/Green probed | None | A scratch violating test method for the probe (removed after) | This IS the Phase 0 structural boundary test for ADR-021 |
| SC-005: single command per tier; default workflow excludes eval suite | Deterministic guarantee | Quickstart walkthrough (`quickstart.md`) executing each documented command | None | N/A | Human-verifiable per Acceptance Scenario, same treatment as prior features' UX-shaped criteria |
| SC-006: full suite executes ≥ 796 tests total; every previously-gating suite still gates | Deterministic guarantee | Sum of `dotnet test` executed-count summaries across all four projects; `ci.yml` diff review | None | N/A | Confirms FR-008 (no coverage loss) |
| SC-007: 100% of attempts to reintroduce a forbidden fixed wait are rejected | Deterministic guarantee | `DeterministicTierNoFixedWaitRuleTests`, exercised in the standard `Grimoire.ArchTests` CI step | None | Same scratch violation as SC-004's probe | CI enforcement, not just a local check — `ci.yml`'s existing "Run architecture tests" step needs no new pipeline step |
| SC-008: `Grimoire.AgentEvals` wall clock ≥ 50% below ~190s baseline; identical sample count/scorers/thresholds/zero skips | Mixed: deterministic guarantee (speed, isolation, zero skips) + agent-judgment preservation (scores unchanged) | Timed full-project run vs. baseline; diff of scorer/threshold output between a concurrent run and a forced-sequential run (`xunit.parallelizeTestCollections=false` override) | Real spawned agent processes replaying committed recordings (ADR-012) — unchanged | Existing `data/evals/recordings/` — unchanged | The spec explicitly frames this feature's outcomes as deterministic harness/tooling guarantees (see spec.md "Success Criteria" preamble): concurrency must not alter agent-judgment results, so this is verified by equality comparison, not a new evaluation threshold |

No new agent-judgment success criterion is introduced by this feature (per the spec's own
framing) — every row above is a deterministic tooling/harness guarantee, including SC-008,
whose only judgment-adjacent aspect (replay scores) is verified by exact-match comparison
against the existing, unchanged sequential baseline rather than a new sampled threshold.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

**N/A, with rationale**: this feature introduces no production business logic, no HTTP
request path, no agent run, and no new runtime component — it is entirely test-project
organization (xUnit traits/collections), one new test-scanning `Grimoire.ArchTests` rule,
a CI-adjacent shell script (`scripts/test-fast.sh`), and documentation (CONTRIBUTING.md).
None of these execute inside a deployed Grimoire process or emit telemetry today, and
this feature gives them no new reason to: there is no business event, no span-worthy
operation, and no metric a running Grimoire instance would report as a result of this
work. The existing observability instrumentation already covering
`Grimoire.IntegrationTests`' own subjects under test (Hub spans/metrics/logs, exercised
via `OpenTelemetry.Exporter.InMemory` per ADR-005) is unchanged by re-timing or
re-parallelizing when those tests run — this feature touches test *scheduling*, never the
production code paths those tests assert against.

### Business Metrics (OpenTelemetry Counters / Gauges)

None — N/A per justification above.

### Structured Log Events

None — N/A per justification above.

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory fields.
3. CI task(s) ensuring those logging tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

None — N/A per justification above.

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) that create the span with declared parent/child linkage and required attributes.
2. Deterministic integration test task(s) validating span name, parent/child relationship, and correlation attributes.
3. CI task(s) ensuring those trace tests run in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/019-fast-test-tier/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/
│   ├── test-tier-commands.md       # Phase 1 output — CLI/tier contract
│   └── deterministic-wait-rule.md  # Phase 1 output — structural-rule contract
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

**Structure Decision**: No new project. Changes are confined to existing test projects
plus one new shell script and documentation updates.

```text
backend/tests/Grimoire.IntegrationTests/
├── xunit.runner.json                    # parallelizeTestCollections: false → true
├── IngestAgentObservabilityCollection.cs # unchanged — remains the sole DisableParallelization exception
├── TestSupport/PollAsync.cs              # new: shared condition-based-wait helper
└── (existing test files)                 # ~4 fixed waits triaged; ad hoc poll loops consolidated onto PollAsync

backend/tests/Grimoire.AgentEvals/
├── SyntheticRecordings.cs                # EvalRunnerProcessTests collection split into two
├── (hermetic classes: ReplayContractTests.cs, CaptureHygieneTests.cs,
│    LintDeterministicScorersTests.cs, RemediationReVerificationScorerTests.cs,
│    LocalEnvFileTests.cs, TimeoutEnforcingModelClientTests.cs)
│                                          # drop [Collection], gain [Trait("Tier","Fast")]
├── (StalenessTests.cs, EvalProviderResolverTests.cs)
│                                          # new small serialized collection (env-mutating)
├── (IngestReplayEvalTests.cs, LintReplayEvalTests.cs, QueryReplayEvalTests.cs,
│    LintRemediationProposalRelevanceEvalTests.cs, RemediationReVerificationEvalTests.cs)
│                                          # new EvalRunnerReplayScenarios collection (parallel default)
└── EvalFactAttribute.cs                  # deleted — dead code, zero [EvalFact] usages found

backend/src/Grimoire.EvalRunner/
├── Replay/ReplayPipeline.cs              # per-sample spawn loop: sequential → bounded-concurrent
└── Workspace/EvalWorkspace.cs            # CopyDirectory: sequential → parallelized file copy

backend/tests/Grimoire.ArchTests/
└── DeterministicTierNoFixedWaitRuleTests.cs   # new: Phase 0 structural boundary test (ADR-021)

backend/tests/Grimoire.IntegrationTests/Grimoire.IntegrationTests.csproj  # remove unused Testcontainers ref
backend/Directory.Packages.props                                          # remove Testcontainers PackageVersion if now unreferenced

scripts/test-fast.sh                      # new: single fast-tier command (FR-001)

CONTRIBUTING.md                           # new "Test Tiers" section; removes stale GRIMOIRE_EVAL=1 claim (FR-007);
                                           # new TDD/tier-placement/edge-case-traceability guidance (FR-009)

docs/adr/ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md  # new (this plan)
```

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table not applicable.

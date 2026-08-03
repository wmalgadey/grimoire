---
status: proposed
---

# ADR-021: Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers

## Context and Problem Statement

Feature 019 (`specs/019-fast-test-tier/spec.md`) requires a documented, single-command
"fast tier" (domain unit tests + architecture tests, excluding the agent-evaluation
suite) that completes in low single-digit seconds (FR-001/FR-002, SC-001/SC-002); a
faster `Grimoire.IntegrationTests` suite via any deterministic lever, not only fixed-wait
replacement (FR-011, SC-003); an automated, CI-enforced ban on reintroducing fixed
unconditional real-time waits into deterministic-tier tests (FR-010, SC-007); a faster,
concurrency-safe `Grimoire.AgentEvals` replay tier with unchanged replay semantics
(FR-012/FR-013, SC-008); and — the one requirement with no existing architectural
precedent — tier membership that "reflects what a test verifies, not which project it
lives in" (FR-014), because a suite audit (clarification session 2026-08-03) found ~45
of `Grimoire.AgentEvals`' ~71 facts are hermetic harness-mechanics tests (scorers, replay
contracts, staleness, config parsing), not agent-judgment evals, while only ~26 are
genuine replay-eval scenarios.

No existing ADR governs test-suite organization, a cross-project tier-membership scheme,
or a structural rule targeting test *code* (every existing `Grimoire.ArchTests` rule
targets *production* assemblies — ADR-009's `RuntimePathsBoundaryRuleTests`, ADR-010/011's
containment rules, ADR-013's N1 naming rule). This is new structural surface: how a test's
tier is declared and machine-checked, and how the fixed-wait ban is enforced, are
cross-cutting conventions every future backend test inherits (per the spec's own User
Story 5), so per Constitution Principle III ("if `plan.md` introduces … a cross-cutting
concern not covered by existing ADRs, the agent MUST draft a new ADR") this is fixed here.

This ADR introduces no new external system, port, or production-code namespace — it is
scoped entirely to test-project organization and one new `Grimoire.ArchTests` rule that
scans test assemblies instead of production ones. Constitution Principle I's hexagonal
gate (ports/adapters for external systems) does not apply.

## Decision Drivers

- FR-014: tier membership must be assignable independent of project boundaries, without
  reopening ADR-013's namespace-ownership/naming-convention rules or requiring
  `Grimoire.EvalRunner`-internal types to be referenced from unrelated test projects.
- FR-001/FR-002/SC-001/SC-002: the fast tier must run with a single documented command
  and with zero evaluation-suite prerequisites (no recordings, no provider credentials).
- FR-003/FR-004/FR-005/FR-010/SC-004/SC-007: deterministic condition-based waiting must
  be the norm, timing-dependent tests must be explicitly, machine-checkably distinguished,
  and reintroducing a fixed wait must be rejected by the standard pipeline, not by
  reviewer vigilance (Constitution Principle IV: "conventions not enforced by CI/CD do
  not exist").
- FR-011/SC-003: any deterministic runtime lever counts toward the integration-suite
  target, including test parallelization — audit found the suite is currently
  fully serialized (`parallelizeTestCollections: false`) despite only one of its 120 test
  classes needing that isolation.
- FR-012/FR-013/SC-008: replay-eval concurrency must not alter sample counts, scorers,
  scores, or thresholds; per-sample isolation (own workspace, fixture copy, recordings)
  must be preserved.
- Constitution Principle III: every rule needs an automated structural test with a
  Red/Green probe; an ADR without one must not be cited as an active constraint.
- Minimal surface / no BDUF: reuse the existing IL-scan (Mono.Cecil) structural-rule idiom
  rather than introducing new tooling (a Roslyn analyzer package, a second test framework).

## Considered Options

### Tier-membership mechanism (FR-014)
1. **A plain xUnit `[Trait("Tier", "Fast"|"Integration"|"SlowEval")]` per test
   class/method**, filtered via `dotnet test --filter`, with no file/project relocation.
2. Physically move the ~45 hermetic `Grimoire.AgentEvals` tests into
   `Grimoire.Domain.UnitTests` or `Grimoire.IntegrationTests`.
3. An environment-variable gate (`GRIMOIRE_FAST_TESTS=1`), mirroring the pattern this
   feature is simultaneously retiring (`GRIMOIRE_EVAL=1`).
4. A custom `[FastFact]`/`[SlowEvalFact]` attribute with runtime skip logic.

### Fixed-wait enforcement (FR-010)
A. **A new `Grimoire.ArchTests` rule, IL-scanning deterministic-tier test assemblies for
   `Task.Delay`/`Thread.Sleep` calls**, allow-listing one shared polling helper and any
   method/class carrying a `[Trait("TimingDependent", "true")]` marker — same Mono.Cecil
   idiom as `RuntimePathsBoundaryRuleTests`.
B. A Roslyn analyzer package enforcing the same rule at compile time.
C. Convention-only (code review), no automated check.

### `Grimoire.IntegrationTests` speed (FR-011/SC-003)
I. **Enable `parallelizeTestCollections` in `xunit.runner.json`**, keeping the one
   existing `DisableParallelization` collection as the sole exception, plus formalize the
   existing ad hoc polling pattern into a shared helper and triage the ~4 true fixed waits.
II. Split the project into multiple physical test projects for coarser parallel
    `dotnet test` invocations.
III. Rely solely on fixed-wait replacement (no parallelization change).

### `Grimoire.AgentEvals` concurrency (FR-012/FR-013/SC-008)
i. **Split the single serialized `EvalRunnerProcessTests` collection** into a default-
   parallel group (hermetic mechanics, no process spawns) and a dedicated
   `EvalRunnerReplayScenarios` collection (replay-eval classes, parallelization left at
   its default) plus make each scenario's internal per-sample loop spawn concurrently
   (bounded degree); parallelize `EvalWorkspace`'s per-sample directory copy.
ii. Parallelize only at the `[Fact]` level, leave each scenario's internal sample loop
    sequential.

## Decision Outcome

Chosen: **Option 1** (tier membership), **Option A** (wait enforcement), **Option I**
(integration-suite speed), **Option i** (eval concurrency).

### Tier taxonomy and membership

Three tiers, declared by trait, not by project:

| Tier | Command | Contains |
|---|---|---|
| **Fast** | `scripts/test-fast.sh` (`Grimoire.Domain.UnitTests` + `Grimoire.ArchTests` + `Grimoire.AgentEvals --filter "Tier=Fast"`) | Domain unit tests, architecture tests, and every hermetic harness-mechanics test currently housed in `Grimoire.AgentEvals` (scorers, replay contract, staleness, config parsing) tagged `[Trait("Tier","Fast")]` |
| **Integration** | `dotnet test backend/tests/Grimoire.IntegrationTests` | Everything in `Grimoire.IntegrationTests` (untagged; the project *is* the tier) |
| **SlowEval (opt-in)** | `dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"` (or the unfiltered project run, which is this tier plus the fast-tagged tests) | Only the genuine replay-eval scenario classes (`IngestReplayEvalTests`, `LintReplayEvalTests`, `QueryReplayEvalTests`, `LintRemediationProposalRelevanceEvalTests`, `RemediationReVerificationEvalTests`) |

No test file moves projects or namespaces. `Grimoire.Domain.UnitTests` and
`Grimoire.ArchTests` need no trait — their entire content is fast/architectural by
construction. `Grimoire.IntegrationTests` needs no trait either — the whole project is
the Integration tier per the spec's Assumptions (folding parts of it into Fast is
"desirable... not a hard requirement"). Only `Grimoire.AgentEvals` is genuinely mixed and
needs per-class tagging.

### Fixed-wait convention and enforcement

A shared helper, `Grimoire.IntegrationTests.TestSupport.PollAsync(condition, timeout,
onTimeoutMessage)`, is the one sanctioned way to wait on an async condition in a
deterministic-tier test (bounded timeout, clear diagnostic on expiry — FR-004). A test
whose verified behavior is itself time-based carries `[Trait("TimingDependent", "true")]`
with a rationale comment (FR-005). A new `Grimoire.ArchTests` rule,
`DeterministicTierNoFixedWaitRuleTests`, IL-scans `Grimoire.Domain.UnitTests`,
`Grimoire.ArchTests`, `Grimoire.IntegrationTests`, and `Grimoire.AgentEvals` for
`Task.Delay`/`Thread.Sleep` calls outside `PollAsync` itself and outside
`TimingDependent`-tagged methods/classes (Mono.Cecil custom-attribute inspection, same
idiom as `RuntimePathsBoundaryRuleTests`'s allow-listed-namespace scan). Ships with a
Red/Green probe (a scratch unexempted `Task.Delay` call must turn the rule red, naming
the call site, before removal) and runs in the existing `Grimoire.ArchTests` CI step —
no new pipeline step required.

### `Grimoire.IntegrationTests`: collection parallelization

`xunit.runner.json`'s `parallelizeTestCollections` flips from `false` to `true`. The
existing `IngestAgentObservabilityListeners` collection
(`[CollectionDefinition(..., DisableParallelization = true)]`) is unchanged and remains
the sole serialization boundary (process-wide `MeterListener`/`ActivityListener` state).
This is combined with R4-style triage of the audit's ~4 true fixed unconditional waits
(converted to `PollAsync` where an observable completion signal exists, tagged
`TimingDependent` where the wait's subject is genuinely time-based) and consolidation of
the ~49 existing ad hoc poll loops onto the shared helper.

### `Grimoire.AgentEvals`: concurrency and setup cost

`SyntheticRecordings.cs`'s single `EvalRunnerProcessTests` collection is split: hermetic,
non-process-spawning classes drop the collection attribute (rejoining xUnit's default
parallel execution) and gain `[Trait("Tier","Fast")]`; two provider-env-mutating classes
(`StalenessTests`, `EvalProviderResolverTests`) keep a small serialized collection of
their own; the five genuine replay-eval classes move to a new
`EvalRunnerReplayScenarios` collection with parallelization left at its default. Each
scenario's internal per-sample loop (`ReplayPipeline.RunScenarioAsync`) is changed from
sequential to bounded-concurrent process spawning (capped at `Environment.ProcessorCount`)
— this is where the bulk of the ~235 sequential spawns live, not in fact-level
scheduling. `EvalWorkspace.Create`'s per-sample directory copy is parallelized
internally. Every sample retains its own workspace, write-locks directory, and recordings
(FR-012's isolation guarantee is unchanged — it was already structural).

### Consequences

- Good, because FR-014 is satisfied with the smallest possible surface — one trait,
  filterable by the tool already in use (`dotnet test --filter`) — with no namespace
  churn and no collision with ADR-013's naming/ownership rules.
- Good, because the fixed-wait ban reuses a proven, already-accepted enforcement idiom
  (Mono.Cecil IL scan + allow-list + Red/Green probe) instead of introducing new tooling.
- Good, because the integration-suite speedup targets the audit's actual dominant cost
  (sequential execution of infrastructure-heavy tests) rather than the audit-corrected,
  much smaller fixed-wait budget (~0.5s of a 61s baseline).
- Good, because eval concurrency targets the audit's actual dominant cost (sequential
  per-sample process spawns inside each scenario, not inter-scenario fact scheduling).
- Bad, because two test assemblies (`Grimoire.IntegrationTests`, `Grimoire.AgentEvals`)
  now run under looser parallelization defaults than before, which requires the one-time
  audit this feature performs (confirming exactly one collection needs isolation in each)
  to have been thorough; a missed shared-state case would surface as CI flakiness rather
  than a silent bug, and is treated as a genuine finding to fix (per the spec's own edge
  case), not a reason to revert to full serialization.
- Neutral, because this ADR governs only test-project organization and one test-scoped
  `Grimoire.ArchTests` rule; it changes no production namespace, port, or adapter, so no
  other existing ADR is superseded or amended.

## More Information

Detailed rationale: `specs/019-fast-test-tier/research.md` (R1–R10). Per Constitution
Principle III this ADR MUST reach **Accepted** (project-owner sign-off) before
`/speckit-tasks` runs for feature 019; it is deliberately left `proposed` by this
planning run.

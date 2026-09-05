---
status: accepted
supersedes: ADR-021
---

# ADR-051: Backend Test Tier Taxonomy, Deterministic-Wait Convention, and Suite Concurrency Levers

## Context and Problem Statement

Grimoire's backend test suite must give contributors a fast, single-command tier for everyday
iteration (domain unit tests + architecture tests, excluding the agent-evaluation suite), a
concurrency-safe `Grimoire.IntegrationTests` suite, a concurrency-safe `Grimoire.AgentEvals` replay
tier, and a CI-enforced ban on reintroducing fixed unconditional real-time waits into
deterministic-tier tests. Tier membership must reflect what a test verifies, not which project it
happens to live in, because a suite audit found roughly two-thirds of `Grimoire.AgentEvals`' facts
are hermetic harness-mechanics tests (scorers, replay contracts, staleness, config parsing), not
genuine agent-judgment evals.

ADR-021 fixed this taxonomy. ADR-033 subsequently narrowed ADR-021's enumerated SlowEval
replay-eval class set from five classes to four, after Constitution v1.12.0 reclassified
agent-judgment success criteria into high-stakes/lower-stakes tiers and the project removed its
lower-stakes eval scenarios. That is a real reversal of ADR-021's own decided, structurally-enforced
enumeration — not an addition alongside it — so per Constitution v2.0.0's whole-ADR supersession
rule, ADR-021 transitions to Superseded and its entire still-live decision (the taxonomy, the
fixed-wait convention, the concurrency levers, and the current class enumeration) is restated here
as one current-truth ADR, rather than left split across an Accepted parent and an amending child.

## Decision Drivers

- The fast tier must run with a single documented command and zero evaluation-suite prerequisites
  (no recordings, no provider credentials).
- Tier membership must be assignable independent of project boundaries, without reopening
  ADR-013's namespace-ownership/naming-convention rules.
- Deterministic condition-based waiting must be the norm; timing-dependent tests must be explicit
  and machine-checkably distinguished; reintroducing a fixed wait must be rejected by the standard
  pipeline (Constitution Principle IV: "conventions not enforced by CI/CD do not exist").
- Any deterministic runtime lever counts toward the integration-suite speed target, including test
  parallelization.
- Replay-eval concurrency must not alter sample counts, scorers, scores, or thresholds; per-sample
  isolation (own workspace, fixture copy, recordings) must be preserved.
- Constitution Principle III: every rule needs an automated structural test with a Red/Green probe;
  an ADR without one must not be cited as an active constraint.
- Constitution Principle II: the SlowEval tier holds only genuine agent-judgment replay-eval
  scenarios; a scenario removed under the lower-stakes eval-tiering rule leaves the tier's
  enumeration, not its taxonomy.

## Considered Options

Restated from ADR-021 (unchanged by this ADR):

### Tier-membership mechanism
1. **A plain xUnit `[Trait("Tier", "Fast"|"Integration"|"SlowEval")]` per test class/method**,
   filtered via `dotnet test --filter`, with no file/project relocation.
2. Physically move hermetic `Grimoire.AgentEvals` tests into other projects.
3. An environment-variable gate, mirroring the pattern this taxonomy retires.
4. A custom `[FastFact]`/`[SlowEvalFact]` attribute with runtime skip logic.

### Fixed-wait enforcement
A. **A `Grimoire.ArchTests` rule, IL-scanning deterministic-tier test assemblies** for
   `Task.Delay`/`Thread.Sleep` calls, allow-listing one shared polling helper and any
   `[Trait("TimingDependent", "true")]`-marked method/class.
B. A Roslyn analyzer package enforcing the same rule at compile time.
C. Convention-only (code review), no automated check.

## Decision Outcome

Chosen: **Option 1** (tier membership), **Option A** (wait enforcement) — both restated verbatim
from ADR-021, with the SlowEval enumeration corrected to its current, post-ADR-033 membership.

### Tier taxonomy and membership

Three tiers, declared by trait, not by project:

| Tier | Command | Contains |
|---|---|---|
| **Fast** | `scripts/test-fast.sh` (`Grimoire.Domain.UnitTests` + `Grimoire.ArchTests` + `Grimoire.AgentEvals --filter "Tier=Fast"`) | Domain unit tests, architecture tests, and every hermetic harness-mechanics test currently housed in `Grimoire.AgentEvals` (scorers, replay contract, staleness, config parsing) tagged `[Trait("Tier","Fast")]` |
| **Integration** | `dotnet test backend/tests/Grimoire.IntegrationTests` | Everything in `Grimoire.IntegrationTests` (untagged; the project *is* the tier) |
| **SlowEval (opt-in)** | `dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"` (or the unfiltered project run, which is this tier plus the fast-tagged tests) | Only the genuine replay-eval scenario classes: `IngestReplayEvalTests`, `LintReplayEvalTests`, `QueryReplayEvalTests`, `RemediationReVerificationEvalTests` |

No test file moves projects or namespaces. `Grimoire.Domain.UnitTests` and `Grimoire.ArchTests` need
no trait — their entire content is fast/architectural by construction. `Grimoire.IntegrationTests`
needs no trait either — the whole project is the Integration tier. Only `Grimoire.AgentEvals` is
genuinely mixed and needs per-class tagging.

**Boundary Rule — SlowEval membership is an exact enumeration.** `AgentEvalsTierMembershipRuleTests`
asserts that the SlowEval tier contains exactly the four classes named above, declared by
`[Trait("Tier","SlowEval")]`, and no other class carries that trait. The enumeration itself is
Accepted decision content: adding or removing a genuine replay-eval scenario class changes what
this ADR decided and is recorded by a new superseding ADR (as ADR-033 did to this ADR's
predecessor, ADR-021), never by editing the table above in place.

### Fixed-wait convention and enforcement

A shared helper, `Grimoire.IntegrationTests.TestSupport.PollAsync(condition, timeout,
onTimeoutMessage)`, is the one sanctioned way to wait on an async condition in a deterministic-tier
test. A test whose verified behavior is itself time-based carries `[Trait("TimingDependent",
"true")]` with a rationale comment. `Grimoire.ArchTests/DeterministicTierNoFixedWaitRuleTests`
IL-scans `Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, `Grimoire.IntegrationTests`, and
`Grimoire.AgentEvals` for `Task.Delay`/`Thread.Sleep` calls outside `PollAsync` itself and outside
`TimingDependent`-tagged methods/classes (Mono.Cecil custom-attribute inspection). Ships with a
Red/Green probe and runs in the existing `Grimoire.ArchTests` CI step.

### `Grimoire.IntegrationTests`: collection parallelization

`xunit.runner.json`'s `parallelizeTestCollections` is `true`. The `IngestAgentObservabilityListeners`
collection (`[CollectionDefinition(..., DisableParallelization = true)]`) remains the sole
serialization boundary (process-wide `MeterListener`/`ActivityListener` state). All fixed
unconditional waits are either converted to `PollAsync` (where an observable completion signal
exists) or tagged `TimingDependent` (where the wait's subject is genuinely time-based).

### `Grimoire.AgentEvals`: concurrency and setup cost

Hermetic, non-process-spawning classes carry no collection attribute (default parallel execution)
and `[Trait("Tier","Fast")]`; provider-env-mutating classes keep a small serialized collection of
their own; the genuine replay-eval classes run in a dedicated `EvalRunnerReplayScenarios`
collection with parallelization left at its default. Each scenario's internal per-sample loop
(`ReplayPipeline.RunScenarioAsync`) spawns bounded-concurrent processes (capped at
`Environment.ProcessorCount`) rather than sequentially. `EvalWorkspace.Create`'s per-sample
directory copy is parallelized internally. Every sample retains its own workspace, write-locks
directory, and recordings — isolation is unchanged.

### Consequences

- Good, because tier membership is satisfied with the smallest possible surface — one trait,
  filterable by the tool already in use — with no namespace churn and no collision with ADR-013's
  naming/ownership rules.
- Good, because the fixed-wait ban reuses a proven, already-accepted enforcement idiom (Mono.Cecil
  IL scan + allow-list + Red/Green probe).
- Good, because the SlowEval enumeration is a Boundary Rule with an exact, currently-correct class
  set, kept honest by requiring a new ADR (as ADR-033 did once already) rather than silent
  divergence between the accepted table and the enforced test whenever the set changes.
- Bad, because `Grimoire.IntegrationTests` and `Grimoire.AgentEvals` run under looser
  parallelization defaults than a fully-serialized suite, which depends on the one-time audit
  behind this decision having correctly identified every shared-state case; a missed one surfaces
  as CI flakiness, treated as a genuine finding to fix, not a reason to revert to full serialization.
- Neutral, because this ADR governs only test-project organization and test-scoped
  `Grimoire.ArchTests` rules; it changes no production namespace, port, or adapter.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new hermetic test tagged into the Fast tier; a
  new integration test added to `Grimoire.IntegrationTests`; a new `TimingDependent`-tagged test
  with its own rationale; a new serialized collection added for a genuine shared-state case.
- **Invalidations (would require full supersession):** changing the SlowEval class enumeration
  (this ADR's own genesis — ADR-033 narrowed ADR-021's enumeration — is the precedent: the
  enumeration is Accepted decision content, so a future change is recorded by a new
  superseding ADR, never an in-place edit of the table above); replacing trait-based tier
  membership with project-based membership; removing the fixed-wait structural test or its
  Red/Green-probed enforcement; reintroducing an environment-variable gate as the
  tier-selection mechanism.

## More Information

Supersedes [ADR-021](ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md), folding in
the SlowEval class-set reduction [ADR-033](ADR-033-sloweval-replay-class-set-reduction.md) recorded
against it. Detailed rationale for the original taxonomy: `specs/019-fast-test-tier/research.md`
(R1–R10).

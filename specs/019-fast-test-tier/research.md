# Research: Fast Developer Feedback Tier for the Backend Test Suite

**Feature**: `019-fast-test-tier` | **Date**: 2026-08-03

This research is grounded in a direct audit of the current worktree (not the issue #44
assumptions, which the spec's Clarifications already corrected). Every finding below cites
the file(s) inspected.

## R1: Tier-membership mechanism (FR-014)

**Decision**: Tier membership is declared with a plain xUnit trait,
`[Trait("Tier", "Fast" | "Integration" | "SlowEval")]`, applied per test class (or per
method where a class is mixed). No test file, namespace, or project is physically moved.

**Rationale**: The audit confirms the ~45 hermetic harness-mechanics tests living in
`Grimoire.AgentEvals` (`ReplayContractTests`, `CaptureHygieneTests`, `StalenessTests`,
`EvalProviderResolverTests`, `EvalCredentialRedactionTests`, `LintDeterministicScorersTests`,
`RemediationReVerificationScorerTests`, `LocalEnvFileTests`, `TimeoutEnforcingModelClientTests`)
reference internal types of `Grimoire.EvalRunner` (`RecordingStore`, `ReplayModelClient`,
`ScenarioDefinition`, …) that only `Grimoire.AgentEvals.csproj` currently references.
Physically relocating them to `Grimoire.Domain.UnitTests` or `Grimoire.IntegrationTests`
would require adding those project references there too (spreading `Grimoire.EvalRunner`'s
surface into unrelated test projects) and would collide with ADR-013's N1 naming-convention
rule (agent-artifact ownership is asserted by *namespace*, not by which trait a test
carries). A trait is the minimal-surface way to satisfy FR-014's "tier membership reflects
what a test verifies, not which project it lives in" without reopening ADR-013.

**Alternatives considered**:
- Physical file/project move — rejected: churn, breaks `git blame` for no structural gain,
  and risks N1 rule false positives (a hermetic test moved into `Grimoire.IntegrationTests`
  would need to also carry that project's ownership-map entry).
- A custom `[FastFact]`/`[SlowEvalFact]` attribute (mirroring `EvalFactAttribute`) —
  rejected as unnecessary: a plain `[Trait]` is sufficient for `dotnet test --filter` and
  needs no runtime skip logic (there is nothing to gate on an environment variable; tier
  membership is static).
- An environment-variable gate (`GRIMOIRE_FAST_TESTS=1`, mirroring the now-dead
  `GRIMOIRE_EVAL=1`) — rejected: SC-002 requires the fast tier to run to completion "on a
  machine with no evaluation prerequisites available" with a single discoverable command;
  a trait filter is inspectable (`dotnet test --filter "Tier=Fast"`) without needing a
  README lookup for the right env var name, and avoids repeating the exact staleness
  problem (CONTRIBUTING.md's stale `GRIMOIRE_EVAL=1` claim, see R8) with a new variable.

## R2: Single documented fast-tier command (FR-001, FR-002, SC-001, SC-002)

**Decision**: One wrapper script, `scripts/test-fast.sh`, runs:
```
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=Fast"
```
CONTRIBUTING.md documents this single script as "the fast tier" (FR-001) and states its
expected duration class (SC-001: low single-digit seconds of test execution, excluding
build). The `Tier=Fast` filter only ever selects the hermetic harness-mechanics classes in
`Grimoire.AgentEvals` (R1); it never selects a replay-eval scenario, so no evaluation
recording or provider credential is required to run it (FR-002, SC-002) — confirmed by
inspecting those classes: none reference `data/evals/recordings/` or a live model client.

**Rationale**: The spec requires "a single documented fast-tier command" (FR-001). Three
separate `dotnet test` invocations chained in one script satisfy "a single command" from
the developer's perspective (`./scripts/test-fast.sh`) while keeping each `dotnet test`
invocation scoped to one project — `dotnet test` cannot target three separate `.csproj`
files with three different `--filter` expressions in a single invocation.

**Alternatives considered**: A single `dotnet test backend/Grimoire.slnx --filter
"Tier=Fast"` — rejected: this would build and load every project in the solution
(including `Grimoire.IntegrationTests`, `Grimoire.LintAgent`, etc.) for no test-selection
benefit over the scoped three-project version, and `Grimoire.Domain.UnitTests`/
`Grimoire.ArchTests` carry no `Tier` trait today (their entire content is fast/architectural
by construction — see R1), so a solution-wide filter would need every one of their ~?
existing facts retrofitted with the trait for no semantic gain.

## R3: `Grimoire.IntegrationTests` collection parallelization (FR-011, SC-003)

**Decision**: Flip `backend/tests/Grimoire.IntegrationTests/xunit.runner.json`'s
`parallelizeTestCollections` from `false` to `true`. The suite's sole existing
`[CollectionDefinition(..., DisableParallelization = true)]`
(`IngestAgentObservabilityListeners`, `IngestAgentObservabilityCollection.cs`) is left in
place unchanged and remains the isolation boundary for the one identified case of genuinely
process-wide, order-sensitive state (a shared `MeterListener`/`ActivityListener` on the
`Grimoire.IngestAgent` meter/activity source).

**Rationale**: The audit found `parallelizeTestCollections: false` currently serializes
**all 120 test classes** in the project, even though only **one** of them (`[Collection(
"IngestAgentObservabilityListeners")]`) is explicitly grouped into a shared collection —
every other class is, by xUnit's default rule, its own implicit collection and is safe to
run in parallel with any other implicit collection. This single boolean is by far the
largest available deterministic lever for SC-003 (a 30% wall-clock reduction target): the
suite's dominant cost, per the spec's own framing, is "sequential execution of tests that
each start real infrastructure" (a Kestrel host + SignalR, or a spawned fake/real agent
process, per test) — turning that into bounded parallel execution scales with available
CPU cores, whereas fixed-wait replacement (R4) only recovers a fixed, already-small
~0.5s budget.

**Alternatives considered**: Leave collection parallelization off and rely solely on
fixed-wait replacement and poll-interval tuning — rejected: the audit (clarification
2026-08-03) already found only ~4 true fixed waits totaling ~0.5s, nowhere near a 30%
cut of a 61s baseline; collection parallelization is required to plausibly hit SC-003.
Splitting the project into multiple physical test projects for coarser-grained parallel
`dotnet test` invocations — rejected: adds project-management overhead the constitution's
"Big Design Up Front is rejected" guidance argues against when a one-line config change
achieves the same wall-clock effect via xUnit's own parallel runner.

## R4: Deterministic condition-based waiting convention (FR-003, FR-004, FR-005)

**Decision**: Formalize the polling pattern already used ~49 times across the suite (a
`deadline`-bounded `while` loop polling a condition every ~20-25ms, failing with
`Assert.Fail($"... did not reach ... in time ...")` on timeout) into one shared helper,
`Grimoire.IntegrationTests.TestSupport.PollAsync(condition, timeout, onTimeoutMessage)`.
Existing ad hoc poll loops are consolidated onto it (no behavior change — they already
satisfy FR-003/FR-004; this only removes duplication and gives R5's structural rule a
single allow-listed call site). The audit's ~4 true fixed unconditional waits (e.g.
`IngestTaskRecordWatcherTests.cs`'s `Task.Delay(TimeSpan.FromSeconds(1))` used only to let
a 300ms debounce window elapse before asserting no extra event arrived, and
`QueryInterruptionTests.cs`'s `Task.Delay(500)`) are triaged individually:
- Waits that exist only to out-wait an async operation with an observable completion
  signal are converted to `PollAsync` on that signal (FR-003).
- Waits whose subject is genuinely time-based — e.g. asserting that a 300ms debounce
  window coalesces rapid writes, or that a documented 1s self-restart delay actually
  elapses before recovery — are marked `[Trait("TimingDependent", "true")]` with a
  one-line rationale comment, satisfying FR-005's "explicitly identified as
  timing-dependent" requirement, and are the only tests R5's structural rule exempts.

**Rationale**: This is the smallest change that (a) makes FR-003/FR-004 uniformly true
suite-wide, (b) gives FR-005 a machine-checkable exemption marker instead of a comment
convention alone, and (c) gives R5 exactly one allow-listed non-test call site to scan for.

## R5: Automated fixed-wait-ban structural rule (FR-010, SC-007)

**Decision**: A new `Grimoire.ArchTests` rule,
`DeterministicTierNoFixedWaitRuleTests`, IL-scans the deterministic-tier test assemblies
(`Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, `Grimoire.IntegrationTests`, and the
`Tier=Fast`-selected classes' containing assembly `Grimoire.AgentEvals`) for calls to
`System.Threading.Tasks.Task::Delay` / `System.Threading.Thread::Sleep`. A call is a
violation unless it originates inside `Grimoire.IntegrationTests.TestSupport.PollAsync`
itself (R4's one allow-listed poll-tick call site) or inside a method/class carrying the
`[Trait("TimingDependent", "true")]` marker (detected via Mono.Cecil custom-attribute
inspection on the containing method/class, same IL-level idiom as
`RuntimePathsBoundaryRuleTests`).

**Rationale**: Mirrors the existing, constitution-compliant idiom exactly
(`RuntimePathsBoundaryRuleTests` — allow-listed namespace/call-site + Mono.Cecil IL scan +
Red/Green probe) rather than inventing a new enforcement mechanism. Per Constitution
Principle III this ships with a Red/Green probe (a scratch `Task.Delay(1000)` added to an
un-exempted test method must turn the rule red, naming the call site, before removal) and
runs in the standard PR pipeline (`ci.yml`'s existing `Run architecture tests` step needs
no new step — it already runs the whole `Grimoire.ArchTests` project).

**Alternatives considered**: A Roslyn analyzer shipped as a NuGet analyzer package —
rejected: heavier tooling (a new analyzer project + packaging) for a rule the existing
IL-scan idiom already expresses in ~100 lines matching a pattern the codebase already has
three instances of (`RuntimePathsBoundaryRuleTests`, `IngestAgentGuardedWriteBoundaryRuleTests`,
`QueryAgentGuardedWriteBoundaryRuleTests`).

## R6: AgentEvals concurrency (FR-012, SC-008)

**Decision**: Split the single `[CollectionDefinition("EvalRunnerProcessTests",
DisableParallelization = true)]` collection (`SyntheticRecordings.cs`) into two:
1. The hermetic mechanics classes that do not spawn a real agent process or mutate
   process-wide provider env vars (`ReplayContractTests`, `CaptureHygieneTests`,
   `LintDeterministicScorersTests`, `RemediationReVerificationScorerTests`,
   `LocalEnvFileTests`, `TimeoutEnforcingModelClientTests`) drop the `[Collection(...)]`
   attribute entirely, rejoining xUnit's default parallel execution and gaining
   `[Trait("Tier", "Fast")]` (R1). `StalenessTests` and `EvalProviderResolverTests`
   (which mutate `GRIMOIRE_EVAL_PROVIDER_*`/related env vars, process-wide state) keep a
   small serialized collection of their own — they were never part of the wall-clock
   problem (they don't spawn ~235 processes) but do need mutual exclusion from each other.
2. The five genuinely process-spawning replay-eval classes (`IngestReplayEvalTests`,
   `LintReplayEvalTests`, `QueryReplayEvalTests`, `LintRemediationProposalRelevanceEvalTests`,
   `RemediationReVerificationEvalTests`) move to a new collection,
   `EvalRunnerReplayScenarios`, with `DisableParallelization` left at its default
   (`false`) — each already runs its own scenario against an isolated `EvalWorkspace`
   (R7), so nothing about them requires serialization; xUnit's own bounded thread pool
   (`maxParallelThreads`, left at its runner default) provides the "concurrent where
   sample isolation permits" behavior FR-012 asks for, and each scenario's own sample loop
   (`ReplayPipeline.RunScenarioAsync`) is changed to issue its per-sample agent-process
   spawns concurrently (bounded degree, e.g. `Parallel.ForEachAsync` capped at
   `Environment.ProcessorCount`) instead of one sample at a time — this is where the bulk
   of the ~235 sequential spawns currently live (each `[Fact]` above is one scenario,
   internally looping over that scenario's sample count sequentially).

**Rationale**: The ~235-spawn cost is concentrated inside each scenario's sample loop, not
primarily in xUnit's fact-level scheduling (there are only ~26 replay-eval facts total
across the five classes) — so the highest-value concurrency lever is inside
`ReplayPipeline`, not the collection attribute. Splitting the collection additionally
unblocks the ~45 hermetic tests to run at full parallelism (contributing to R1/R2's fast
tier being genuinely fast) without weakening the two still-order-sensitive env-mutating
classes.

**Isolation guarantee preserved (FR-012)**: `EvalWorkspace.Create` (R7) already gives every
sample its own temp root, its own `write-locks` directory, and its own agent-instructions
copy — concurrent samples were already structurally isolated at the filesystem level; the
change only removes an artificial sequential-execution constraint, it adds no new sharing.

**Alternatives considered**: Parallelizing only at the `[Fact]` level (removing the
collection attribute but leaving `ReplayPipeline`'s internal sample loop sequential) —
rejected: modeled cost shows the internal per-scenario sample loop, not inter-scenario
fact scheduling, dominates the ~190s baseline (a scenario with 10 samples pays 10 sequential
spawns regardless of how many other scenarios run alongside it), so this alone would miss
SC-008's 50% target.

## R7: Per-sample setup cost (FR-013)

**Decision**: `EvalWorkspace.Create`'s two `CopyDirectory` calls (wiki fixture, agent
instructions) are parallelized internally (`Parallel.ForEachAsync` over the file list
instead of a sequential `foreach`) rather than restructured into a shared-template/hardlink
scheme.

**Rationale**: A shared-template-plus-hardlink approach was considered but rejected as
disproportionate risk for this feature's scope: hardlinks share a single inode, so any
sample that (correctly, per its isolation contract) writes into its own copy would
corrupt a shared template unless every consumer is first audited to prove read-only access
to the linked files before the first agent-process write — a correctness risk not worth
taking for a setup cost that is already small relative to a real agent-process spawn
(seconds) and is fully parallelizable with no isolation risk (each sample's copy target is
already unique, per-sample; only the *source* — a small, fixed fixture directory — is
shared, and it is only ever read, never written, by `CopyDirectory`).

**Alternatives considered**: Caching a pre-built template per scenario and using
`File.Copy` from the cached template instead of from the repo fixture directly —
rejected: no measurable benefit over parallelizing the existing copy (the repo fixture
directory is already on local disk, not network storage; the cost is per-file syscall
overhead, which parallelization addresses directly) and adds a second cache-invalidation
concern (a stale template if the fixture changes mid-run) that the constitution's
"Big Design Up Front is rejected" principle argues against introducing without evidence
it is needed.

## R8: Stale documentation and dead code (FR-007, cleanup)

**Decision**: CONTRIBUTING.md's existing paragraph — "Agent-behavior (evaluation) tests
that call a real LLM provider are gated behind `GRIMOIRE_EVAL=1` and are not part of the
default hermetic test run" — is replaced by the new `## Test Tiers` section (R2). The
audit additionally found this claim is not just stale but describes genuinely **dead
code**: `backend/tests/Grimoire.AgentEvals/EvalFactAttribute.cs` defines `[EvalFact]` and
`EvalGate`, but zero `[EvalFact]` usages and zero `EvalGate.*` call sites remain anywhere
in the test suite (superseded by ADR-012's recorded-replay tier, which gates on recording
staleness, not a live-call environment variable). `EvalFactAttribute.cs` is deleted as
part of this feature's cleanup — leaving it in place while also documenting the new tier
scheme would contradict FR-007's "no contradictory description of how to run tests may
remain," now applied to code as well as prose.

## R9: Dead `Testcontainers` package reference (FR-015)

**Decision**: Remove the `<PackageReference Include="Testcontainers" />` line from
`backend/tests/Grimoire.IntegrationTests/Grimoire.IntegrationTests.csproj` and its
corresponding `<PackageVersion>` entry in `backend/Directory.Packages.props` (if left
otherwise unreferenced after this removal — confirmed no other test project references
`Testcontainers`).

**Rationale**: `grep -rl "Testcontainers" backend/tests/Grimoire.IntegrationTests
--include="*.cs"` returns zero files — the package is referenced but never imported or
used anywhere in the project. This is exactly the dead reference the clarification
session (2026-08-03) named explicitly.

## R10: Location of the new testing-guidance content (FR-009, User Story 5)

**Decision**: All new guidance (tier taxonomy, tier-placement rule, deterministic-waiting
rule, TDD/edge-case-traceability rule) is added to **CONTRIBUTING.md**, extending its
existing "Building and testing" section — no new top-level document is created.

**Rationale**: CLAUDE.md's Document Map requires "a declared reader" for any new document
and explicitly names `dev-experience.md` as the fallback destination when none exists —
but `dev-experience.md` is a personal, German-language log explicitly out of scope for
binding contributor guidance. CONTRIBUTING.md is not itself one of the Document Map's
governed artifacts (that table covers SDD-pipeline documents — constitution, ADRs,
decision-context, specs, the remediation-prompt library, absorbed source material, the dev
log); it is the project's existing, standard contributor-facing how-to-build/test document,
already the explicit target FR-007 names for the stale-claim fix. Extending it in place
avoids minting a new document that would need its own Document Map justification for no
added clarity.

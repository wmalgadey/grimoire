# Quickstart: Validating the Fast Developer Feedback Tier

**Feature**: `019-fast-test-tier`

This walks through validating each user story end-to-end after implementation. It does
not duplicate the command contract (`contracts/test-tier-commands.md`) or the rule
contract (`contracts/deterministic-wait-rule.md`) — it links to them and records expected
outcomes.

## Prerequisites

- A clean checkout on `019-fast-test-tier` (or its merge target).
- .NET SDK matching `backend/Directory.Build.props`.
- `dotnet build backend/Grimoire.slnx --configuration Release` completed once (build
  time is explicitly out of scope for every runtime target in this feature).

## US1 — Fast inner-loop feedback (P1)

```bash
time ./scripts/test-fast.sh
```

**Expected**:
- Exit code 0.
- Console output shows `Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, and a
  `Tier=Fast`-filtered `Grimoire.AgentEvals` run — no replay-eval scenario name appears
  (SC-002).
- `time`'s reported test-execution wall clock (excluding the prior `dotnet build`) is a
  low single-digit number of seconds (SC-001).
- Repeat on a machine/container with no `data/evals/recordings/` directory and no
  `ANTHROPIC_AUTH_TOKEN` set: the script still completes successfully (SC-002).

Break a domain rule deliberately (e.g. violate an invariant in
`backend/src/Grimoire.Domain`) and rerun — confirm the fast tier reports the failure
without needing to run `Grimoire.IntegrationTests` or `Grimoire.AgentEvals` first
(Acceptance Scenario 3).

## US2 — Integration suite without fixed waits (P2)

```bash
time dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
```

**Expected**:
- All tests pass; executed test count ≥ 583 (SC-003, SC-006 contribution).
- Wall clock is ≥ 30% lower than the recorded 61s baseline on the same/comparable
  environment (SC-003).
- Grep the project for remaining `Task.Delay`/`Thread.Sleep` calls not routed through
  `TestSupport.PollAsync` and not tagged `[Trait("TimingDependent", "true")]` — expect
  zero (SC-004); cross-check against `contracts/deterministic-wait-rule.md`'s allow-list.
- Temporarily make a `PollAsync`-awaited condition never become true (e.g. point it at a
  condition that can't be satisfied) — confirm the test fails with a bounded-timeout
  diagnostic message rather than hanging (Acceptance Scenario 4).

## US3 — Agent-evaluation suite as an opt-in slow tier (P3)

1. Read CONTRIBUTING.md's "Test Tiers" section — confirm it names all three tiers, each
   tier's purpose, duration class, and command, and marks the eval tier "slow, opt-in"
   (Acceptance Scenario 1).
2. Run the documented default local workflow (`./scripts/test-fast.sh`) — confirm no
   agent-evaluation test executes (Acceptance Scenario 2, same check as US1/SC-002).
3. Run `dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"` —
   confirm it executes independently (no other tier's tests run in the same invocation)
   and exercises exactly the five replay-eval classes named in
   `contracts/test-tier-commands.md` (Acceptance Scenario 3).

## US4 — Faster agent-evaluation suite, unchanged verification (P3)

```bash
time dotnet test backend/tests/Grimoire.AgentEvals --configuration Release
```

**Expected**:
- Wall clock is ≥ 50% lower than the recorded ~190s baseline (SC-008).
- Zero skipped tests (`ci.yml`'s existing `Skipped:\s+0,` check still passes).
- Diff the executed sample count, scorer results, and pass/fail thresholds against a
  sequential baseline run (e.g. `dotnet test ... -- xunit.parallelizeAssembly=false
  xunit.parallelizeTestCollections=false`, or a pre-change git stash) — confirm they are
  identical (Acceptance Scenario 2/3; FR-012).
- Confirm each replay sample still used its own isolated `EvalWorkspace` (inspect
  `Path.GetTempPath()/grimoire-eval-runner/` during a run — one directory per sample,
  never shared).

## US5 — Guidance for future tests (P4)

1. Read CONTRIBUTING.md's testing-guidance addition — confirm it states: which tier a
   new test belongs in, the TDD-against-expected-behavior rule, and the edge-case
   traceability rule (edge cases only when traceable to a spec/FR/user story) —
   Acceptance Scenario 1.
2. Add a scratch test with a deliberate un-exempted `Task.Delay` call to a
   deterministic-tier project; run `dotnet test backend/tests/Grimoire.ArchTests`.
   Confirm `DeterministicTierNoFixedWaitRuleTests` fails, naming the exact call site
   (Acceptance Scenario 2; this doubles as the feature's own Red half of the Phase 0
   Red/Green probe — see `contracts/deterministic-wait-rule.md`). Remove the scratch test.

## Full-suite / merge-gate parity check

```bash
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release
```

Sum the executed test counts across all four invocations — confirm the total is ≥ 796
(SC-006) and that `ci.yml` still runs all four as merge-gating steps (FR-008).

# Contract: Test Tier Commands

**Feature**: `019-fast-test-tier`

This is the developer-facing CLI contract this feature introduces. It is the single
source of truth CONTRIBUTING.md's "Test Tiers" section documents (FR-007) and what
`quickstart.md` validates end-to-end.

## Fast tier

```bash
./scripts/test-fast.sh
```

- **Runs**: `Grimoire.Domain.UnitTests` (all), `Grimoire.ArchTests` (all),
  `Grimoire.AgentEvals --filter "Tier=Fast"` (hermetic harness-mechanics classes only).
- **Excludes**: every replay-eval scenario test (FR-001) — zero agent-evaluation tests
  execute (SC-002).
- **Prerequisites**: none beyond a built solution. No `data/evals/recordings/` content,
  no `ANTHROPIC_AUTH_TOKEN`/provider credential, no network access (FR-002, SC-002).
- **Exit code**: non-zero if any invocation in the chain fails (`set -eo pipefail`
  semantics); the script stops at the first failing suite so the developer sees which
  tier failed.
- **Expected duration**: low single-digit seconds of test execution on the reference
  environment, excluding build time (SC-001).

## Integration tier

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
```

- **Runs**: the entire `Grimoire.IntegrationTests` project — no filter, no change to
  which tests execute (FR-008: no test deleted or weakened).
- **Prerequisites**: a built solution; a container runtime is only needed by tests that
  already required one before this feature (this feature adds no new Testcontainers
  usage — R9 in `research.md` removes the project's *unused* reference).
- **Expected duration**: reduced by at least 30% versus the recorded 61s baseline on the
  reference environment (SC-003), with the executed test count not lower than 583.

## Slow, opt-in agent-evaluation tier

```bash
dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"
```

- **Runs**: exactly the five genuine replay-eval scenario classes
  (`IngestReplayEvalTests`, `LintReplayEvalTests`, `QueryReplayEvalTests`,
  `LintRemediationProposalRelevanceEvalTests`, `RemediationReVerificationEvalTests`).
- **Never executes as part of**: the fast tier or the documented default local workflow
  (FR-006, SC-002).
- **Prerequisites**: the versioned recordings under `data/evals/recordings/` (already
  committed) — no live provider credential is required for replay (ADR-012 unchanged).
- **Semantics preserved exactly** (FR-012): sample count, scorers, scores, and pass
  thresholds are identical to a sequential run; concurrency is an execution-strategy
  change only.
- **Expected duration**: reduced by at least 50% versus the recorded ~190s baseline on
  the reference environment (SC-008), with zero skipped tests (unchanged from today's
  CI gate, `ci.yml`'s `grep -Eq "Skipped:\s+0,"` check).

## Full suite (unchanged shape, still gates merges)

```bash
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release
```

- This is `ci.yml`'s existing four-step sequence (unfiltered `Grimoire.AgentEvals` run
  covers both the Fast-tagged hermetic tests and the SlowEval replay scenarios in one
  invocation, exactly as it does today) — unchanged by this feature (FR-008: the merge
  gate still runs every suite; spec Assumptions: re-sequencing the merge gate itself is
  welcome but not required).
- **Expected total test count**: at least 796 across the four suites (SC-006), matching
  or exceeding the issue #44 baseline.

## `Tier` trait vocabulary

| Value | Meaning |
|---|---|
| `Tier=Fast` | Hermetic, no external prerequisite, safe to run on every edit (only used inside `Grimoire.AgentEvals`; other fast-tier projects need no tag) |
| `Tier=SlowEval` | Genuine agent-judgment replay scenario; the default/unfiltered `dotnet test` run on `Grimoire.AgentEvals` includes both `Fast` and untagged/`SlowEval` classes |

An untagged `Grimoire.AgentEvals` test class is implicitly part of whichever tier its
content matches (see `research.md` R1 for the current class-by-class assignment) — the
trait is only added where a class must join the Fast tier from its home project's
default.

## `TimingDependent` trait vocabulary

| Value | Meaning |
|---|---|
| `TimingDependent=true` | Exempted from the fixed-wait ban (ADR-021); the test's own subject is elapsed real time, not merely out-waiting an async operation (FR-005) |

# Data Model: Fast Developer Feedback Tier for the Backend Test Suite

**Feature**: `019-fast-test-tier` | **Date**: 2026-08-03

This feature has no domain entities and touches no persisted or runtime state — it is
test-project/tooling organization. The spec's Key Entities section describes test-metadata
concepts, not domain objects; they are captured here as the configuration/attribute shapes
that realize them (see ADR-020 and `research.md` for full rationale).

## Test Tier

A named grouping of tests with a defined purpose, duration class, run command, and
inclusion rule (FR-014: membership follows what a test verifies, not project boundaries).

| Field | Type | Notes |
|---|---|---|
| Name | `"Fast" \| "Integration" \| "SlowEval"` | Fixed enumeration; no fourth tier is introduced by this feature |
| Command | shell command | `scripts/test-fast.sh`; `dotnet test backend/tests/Grimoire.IntegrationTests`; `dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"` |
| Duration class | `"fast" \| "moderate" \| "slow"` | Documented in CONTRIBUTING.md per FR-007 |
| Inclusion rule | project membership, or `[Trait("Tier", ...)]` | `Grimoire.Domain.UnitTests`/`Grimoire.ArchTests` (whole project = Fast); `Grimoire.IntegrationTests` (whole project = Integration); `Grimoire.AgentEvals` (per-class trait: untagged/`Tier=SlowEval` classes are the five genuine replay-eval classes, `Tier=Fast`-tagged classes are the hermetic harness-mechanics classes) |

**Representation**: an xUnit `[Trait("Tier", "Fast")]` attribute on the ~9 hermetic
`Grimoire.AgentEvals` test classes that must join the Fast tier (see research.md R1).
No new type is introduced — this is metadata attached to existing test classes, filtered
at `dotnet test` invocation time.

## Timing-Dependent Test

A test explicitly marked as verifying real elapsed-time behavior, exempt from the
fixed-delay ban (FR-005).

| Field | Type | Notes |
|---|---|---|
| Marker | `[Trait("TimingDependent", "true")]` | Applied to the test method or its containing class |
| Rationale | code comment | One-line justification adjacent to the marker (e.g. "asserts the 300ms debounce window itself elapses") |

**Representation**: an xUnit trait, the same mechanism as Test Tier, so both the fast-wait
audit (SC-004) and the structural rule (ADR-020, `DeterministicTierNoFixedWaitRuleTests`)
can query it via Mono.Cecil custom-attribute inspection without a second metadata scheme.

## Baseline Measurement

The recorded issue #44 runtimes (per suite, on the reference environment) against which
improvement is judged (spec Key Entities; Assumptions).

| Suite | Baseline runtime | Baseline test count | Source |
|---|---|---|---|
| `Grimoire.IntegrationTests` | 61s | 583 | spec SC-003 |
| `Grimoire.AgentEvals` | ~190s | (unspecified in spec; preserved exactly, not reduced — SC-008) | spec SC-008 |
| All four suites combined | — | 796 | spec SC-006 |

**Representation**: this is a fixed, historical reference value cited in `research.md` and
`CONTRIBUTING.md`'s tier documentation — not a runtime object, config record, or persisted
value. No code models it; SC-003/SC-008's "at least 30%/50% reduction" is verified by a
one-time timed comparison run during implementation validation (`quickstart.md`), not by
an ongoing automated performance-regression gate (out of scope per the spec's Assumptions:
runtime targets are anchored to the reference environment, not continuously enforced in
CI beyond the existing suites continuing to pass).

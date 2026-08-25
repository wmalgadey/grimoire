# Quickstart: Validating Lint at Scale

**Feature**: 027-lint-at-scale | **Date**: 2026-08-24

How to prove this feature works, end to end. Deterministic checks first; the agent-judgment
checks need recordings and are gated separately.

## Prerequisites

- .NET 10 SDK
- A wiki content root sized for the scale checks. The `lint-at-scale-survey` eval scenario's
  fixture (generated at build time, git-ignored) covers the deterministic and evaluation
  checks below; a real ~633-page snapshot (e.g., a copy of the self-hosted deployment's
  content root, or an accumulated corpus at 2x that) is needed only for the manual SC-001/
  SC-003 spot check.
- No API key is needed for anything in the "Harness contracts" section. If one is required to
  run those, that is a defect (Principle II).

## Harness contracts (hermetic)

```bash
./scripts/test-fast.sh                                            # domain + arch + fast tier
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
```

Expected: green. Because this feature introduces no new Boundary Rule (see plan.md's
Architectural Constraints section), there is no new Phase 0 structural test — the
`ConsideredPaths`/`WikiCoverage` behavior is covered by classicist, state-based integration
tests against the real `GuardedToolExecutor` and a real temp-directory content root.

### Spot checks worth doing by hand

| Check | How | Expected |
|---|---|---|
| Full-wiki completion (SC-001) | Point Lint at a ~633-page (or larger) content root and start a run | Run reaches a terminal success state and a `FindingsReport` is written — no `AgentLoopCapException` |
| Coverage on a complete pass | Same run, small enough that the agent reasonably reads/considers every page | `WikiCoverage.Status == Complete`, `PagesConsidered == PagesTotal` in the persisted report |
| Coverage on a forced-partial pass | Point Lint at a wiki with a `ContextBudgetTokens`-equivalent cap tight enough to force early stop | `WikiCoverage.Status == Partial`, `PagesConsidered < PagesTotal`, and this is visibly distinct from `FindingsReport.Partial` (crash) being `false` |
| `list_files` alone doesn't count as coverage | Trigger a run where the agent lists but never opens a page | That page is absent from `ConsideredPaths` / not counted toward `PagesConsidered` |
| No regression on a small wiki | Run against a wiki small enough for the old whole-wiki read (e.g., the `lint-seeded-defects` base fixture) | Run time and thoroughness are unchanged from before this feature (FR-007) |

## Observability

Run the Hub against the real telemetry registration — **not** a test-only `ActivitySource` or
an always-on sampler.

```bash
docker compose -f compose.yaml up -d       # Aspire dashboard for local inspection
```

Trigger a Lint run to completion, then confirm:

- The `lint_agent.run` root span carries `coverage.pages_total`, `coverage.pages_considered`,
  `coverage.status` attributes.
- A `lint.run.coverage_computed` structured log event appears with `run_id`, `pages_total`,
  `pages_considered`, `coverage_status`, and its `run_id` matches the span's own run
  correlation attribute.
- `wiki.lint.coverage_ratio` records one observation for the run.
- `wiki.lint.runs_total{status=complete|partial}` increments with the correct label.

## Agent behavior (evaluation tier)

```bash
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=SlowEval&FullyQualifiedName~LintAtScale"
```

Expected:

- `lint-at-scale-survey` (extended per research.md R3) replays at its existing pass threshold,
  now also asserting the persisted `WikiCoverage` shape.
- The cross-page-findings variant (SC-004) surfaces the planted contradiction/duplicate at
  ≥ 90% across sampled recordings.
- The inbound-link variant (SC-005) refreshes the stale count correctly at ≥ 90% across
  sampled recordings — holding steady against, not necessarily beating, the pre-feature
  baseline.

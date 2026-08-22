# Quickstart: Validating the Guarded Tool and Policy Surface

**Feature**: 026-guarded-tool-surface | **Date**: 2026-08-22

How to prove this feature works, end to end. Deterministic checks first; the agent-judgment
checks need recordings and are gated separately.

## Prerequisites

- .NET 10 SDK
- A wiki content root with at least a few hundred pages for the scale checks. The eval fixture
  under `Grimoire.AgentEvals` provides one; a smaller root is fine for the harness checks.
- No API key is needed for anything in the "Harness contracts" section. If one is required to
  run those, that is a defect (Principle II).

## Harness contracts (hermetic)

```bash
./scripts/test-fast.sh                                            # domain + arch + fast tier
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
```

Expected: green, with the Phase 0 boundary tests present and passing. Each of the three
Boundary Rules must have had its Red/Green probe demonstrated when it was written — a
structural test that has never been seen to fail proves nothing (Principle III).

### Spot checks worth doing by hand

| Check | How | Expected |
|---|---|---|
| Search cannot widen the read scope | Put the search term inside a path the read policy excludes | The match is absent from results, and no denial names the path |
| A partial read cannot license an overwrite | `read_file` with `offset`/`limit`, then `write_file` on the same path | Write refused; page must be read in full first |
| Batch is read-only | Submit a batch containing one `write_file` | Whole batch rejected; no member executed |
| Deletion is not inherited | Attempt `delete_file` as Ingest | Denied — Ingest's policy declares no `delete` scope |
| Rollback covers deletion | Force a failure after a delete in the same run | The page is back on disk with its content |

## Observability

Run the Hub against the real telemetry registration — **not** a test-only `ActivitySource` or
an always-on sampler. A contract test that passes under a hand-built provider while production
exports nothing is the exact failure feature 003 shipped.

```bash
docker compose -f compose.yaml up -d       # Aspire dashboard for local inspection
```

Trigger a Lint run that searches, ranged-reads, batches and deletes, then confirm:

- `guardrails.search_scan` appears as a child of `lint_agent.tool_call`, carrying
  `files_scanned`, `matches`, `truncated`, `outcome`
- `guardrails.batch` under `lint_agent.model_turn` with `call_count` and `denied_count`
- `guardrails.delete_file` under `lint_agent.tool_call` with `journaled`
- `wiki.search.invocations_total` increments with the right `outcome` label
- `wiki.read.invocations_total` shows `shape=range`/`frontmatter` displacing `shape=full`
- Every log event above carries `task_id`, and it matches the span's `task_id`

## Agent behavior (evaluation tier)

```bash
dotnet run --project backend/src/Grimoire.EvalRunner -- replay --scenario lint-at-scale
```

| Criterion | Threshold |
|---|---|
| SC-011 survey completes under the context guard | ≥ 90% of sampled runs |
| SC-012 searches are scoped, not unbounded sweeps | ≥ 90% of sampled searches |
| SC-013 authorized body edits address the proposal | ≥ 90% of sampled remediations |
| SC-014 median content tokens read | ≥ 50% below the pre-feature baseline |

**Capture the SC-014 baseline before implementation starts** (research.md D9). Measured
afterwards it is a reconstruction, not a baseline.

SC-013 carries extra weight: it is what replaces ADR-016's deleted structural guarantee. If it
cannot be made to hold, the right response is to revisit ADR-031, not to lower the threshold.

## What "done" looks like

- Three Boundary Rules structurally enforced, each Red/Green probed
- SC-001..SC-010 plus SC-005a/SC-005b green as hermetic integration tests
- Every Observability row emitted and asserted through the production composition root
- SC-011..SC-014 at threshold
- `docs/adr/index.md` and the five amended ADR headers consistent with ADR-030/031

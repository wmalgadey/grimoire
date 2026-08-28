# Quickstart: Validating Lint at Scale

**Feature**: 028-lint-at-scale | **Date**: 2026-08-24

How to prove this feature works, end to end. Deterministic checks first; the agent-judgment
checks need recordings and are gated separately.

## Prerequisites

- .NET 10 SDK
- No large or production-scale content root is needed anywhere in this quickstart. SC-001/
  SC-002 use a small ad hoc temp-directory root the hermetic tests build themselves; SC-003/
  SC-004/SC-005/SC-006 reuse the existing `lint-at-scale-survey` eval fixture (generated at
  build time, git-ignored, ~69 pages) unchanged in size; SC-007/SC-008's `log.md` fixture is
  built inline by the write-side tests (seeded to ~128KB to reproduce the production size).
- No API key is needed for anything in the "Harness contracts" section. If one is required to
  run those, that is a defect (Principle II).

## Harness contracts (hermetic)

```bash
./scripts/test-fast.sh                                            # domain + arch + fast tier
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
```

Expected: green. Neither side of this feature introduces a Boundary Rule: the read-side
(`ConsideredPaths`/`WikiCoverage`) and the write-side's three Feature-Scoped Invariants
(FSI-1/FSI-2/FSI-3, plan.md — schema stays `additionalProperties: false`-compatible; no
`OnReadFile` call is reachable from the prepend dispatch path; format/ordering deviations
are recorded, never denied, on either write mode) are all covered entirely by classicist
integration tests, no `Grimoire.ArchTests` addition — none decides a new system boundary
or technology choice (Constitution Principle III).

### Spot checks worth doing by hand — read side

| Check | How | Expected |
|---|---|---|
| Cap-enforcement mechanics (SC-001) | Run the hermetic `LintAtScaleCompletionTests` with the fake `IModelClient` scripted past the simulated budget | Run reaches a terminal success state and a `FindingsReport` is written — no `AgentLoopCapException` |
| Coverage on a complete pass | Same harness, scripted to read every page in the small ad hoc content root | `WikiCoverage.Status == Complete`, `PagesConsidered == PagesTotal` in the persisted report |
| Coverage on a forced-partial pass | Same harness with the simulated budget tight enough to force early stop | `WikiCoverage.Status == Partial`, `PagesConsidered < PagesTotal`, and this is visibly distinct from `FindingsReport.Partial` (crash) being `false` |
| `list_files` alone doesn't count as coverage | Trigger a run where the agent lists but never opens a page | That page is absent from `ConsideredPaths` / not counted toward `PagesConsidered` |
| No regression on a small wiki | Run against a wiki small enough for the old whole-wiki read (e.g., the `lint-seeded-defects` base fixture) | Run time and thoroughness are unchanged from before this feature (FR-007) |

### Spot checks worth doing by hand — write side (FSI-1/FSI-2/FSI-3, plan.md)

| Check | How | Expected |
|---|---|---|
| Reproduces, then fixes, issue #201 (SC-007) | Seed a temp-dir `log.md` to ~128KB; call `write_file` with `mode: "prepend"` and a small entry | Write succeeds; on-disk file is `entry + originalContent`, byte-for-byte |
| Cost is proportional to entry, not file (SC-007) | Same seeded file; compare the *call's own* `content` length to the file's total size | Call `content` length is the entry's length only — never includes the seeded 128KB |
| Malformed entry still commits (SC-009) | Prepend-mode call with no heading, or a heading with no following paragraph | Write succeeds unchanged (no denial); `wiki.log.format_deviation` fires with `reason=log_entry_malformed_heading` (or `log_entry_missing_paragraph`), and `wiki.log.format_deviation_total` increments with the matching label |
| Wrong prepend order still commits (SC-009) | `mode: "replace"` call whose proposed content does not end with the current content | Write succeeds unchanged (no `log_entry_not_prepended` denial); the same deviation signal fires with `reason=log_entry_not_prepended` |
| Conforming entry emits no signal (SC-009) | A well-formed prepend-mode entry | No `wiki.log.format_deviation` event, no metric increment — the signal only fires on deviation |
| Concurrent prepends both land (SC-008) | Two tasks/threads submit different entries to the same `log.md` near-simultaneously | Both entries present afterward, newest-first, in lock-acquisition order — no `write_conflict_stale_read` denial, no lost entry |
| `index.md` unaffected | Prepend-mode call targeting `index.md` | Rejected/handled by the existing, unchanged catalog-entry check — no prepend-specific behavior applies |

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
- `wiki.lint.coverage_runs_total{coverage_status=complete|partial}` increments with the
  correct label (renamed from the originally planned `wiki.lint.runs_total` during Layer 2
  implementation — that name already exists as an unrelated Hub-side metric; see plan.md ##
  Observability).

Trigger a `log.md` write with a deliberately malformed entry (via any of Ingest/Query/Lint),
then confirm:

- A `wiki.log.format_deviation` structured log event (WARN) appears with `agent`, `mode`,
  `path`, and `reason` fields matching the deviation.
- `wiki.log.format_deviation_total{agent=...,mode=...,reason=...}` increments with matching
  labels.
- The write itself still succeeds — inspect `log.md` on disk and confirm the entry is
  present exactly as submitted, not rejected.

## Agent behavior (evaluation tier)

```bash
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=SlowEval&FullyQualifiedName~LintAtScale"
```

Expected:

- `lint-at-scale-survey`'s new, tighter-budget variant (T004) replays cleanly, confirming the
  real agent's reading stays proportionately bounded at the tighter ratio (SC-003's
  agent-judgment half).
- If the optional SC-004/SC-005 checks (T025/T026) were kept, they surface the planted
  contradiction/duplicate and refresh the stale inbound-link count respectively; if they were
  skipped, that is a deliberate, visible choice (T031), not a gap — SC-004/SC-005 are
  lower-stakes per Constitution v1.12.0 and are satisfied primarily by an operator reading the
  Findings Report and adjusting `agents/lint/system-prompt.md` if something looks wrong (the
  correction loop; see plan.md § Observability).

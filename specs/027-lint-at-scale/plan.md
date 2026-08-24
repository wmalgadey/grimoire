# Implementation Plan: Lint at Scale

**Branch**: `027-lint-at-scale` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/027-lint-at-scale/spec.md`

## Summary

Lint's wiki health check must complete over a wiki that no longer fits in one context window
(633 pages / ~400k tokens today, per issue #108), and an operator must be able to tell a
complete pass from a partial one without reading the run's raw tool-call log.

This feature does **not** rebuild Lint's reading strategy. ADR-030's guarded retrieval tools
(`search_files`, ranged `read_file`, read-only `batch`) and the frontmatter-first/search-first
"Choosing how to read" instruction-file rewrite (PR #179) already landed as part of spec 026,
and already demonstrated an 86% reduction in content tokens read on the `lint-at-scale-survey`
eval scenario — that is Direction A from the issue, already substantially delivered as a side
effect of a different feature. What #108 still needs, and what this feature delivers:

1. A harness-computed **wiki-coverage signal** (`WikiCoverage`: pages considered vs. total,
   complete vs. partial) threaded through the same pipeline that already carries
   `DeniedActions` and `InboundLinksRefreshed` onto the persisted Findings Report — new,
   because no equivalent of `GuardedToolExecutor`'s write-tracking accumulators exists for
   reads today.
2. Evaluation coverage scoped to *this* issue's own acceptance criteria — completion at
   current scale, headroom at 2x scale, and no regression on cross-page findings or
   inbound-link accuracy (#42) — extending the existing `lint-at-scale-survey` scenario and
   its fixture rather than building new eval infrastructure.

Direction B (harness-side sharding into windowed sub-runs with partial-report merging) is
explicitly not adopted here — see research.md R1 for the rationale and its revisit trigger.
Because this reuses an existing pipeline shape rather than introducing a new one, this
feature needs no new ADR (see Architectural Constraints below).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`) for the backend; no frontend surface.

**Primary Dependencies**: No new external dependency. Reuses `Grimoire.AgentRuntime`
(`GuardedToolExecutor`, `AgentLoop`), `Grimoire.LintAgent` (`LintToolRegistry`,
`RunEventEmitter`, `LintAgentInstrumentation`), `Grimoire.Hub.LintDispatch`
(`LintRunCoordinator`), `Grimoire.Hub.LintFindings` (`FindingsReportFormat`),
`Grimoire.EvalRunner`/`Grimoire.AgentEvals` (recorded-replay evaluation, ADR-012).

**Storage**: Markdown files on the real filesystem under the content root (unchanged). The
persisted Findings Report file (`grimoire-findings/1`) gains one additive YAML field; no new
file, table, or format family.

**Testing**: xUnit. `Grimoire.IntegrationTests` (classicist state-based tests against the real
`GuardedToolExecutor`, a real temp-directory content root, and the real `LintRunCoordinator`
pipeline); `Grimoire.AgentEvals` recorded-replay (ADR-012) for agent-judgment success
criteria, extending the existing `lint-at-scale-survey` scenario and `LintAtScaleFixture`.

**Target Platform**: Linux container (Docker) in production; Windows/macOS for development —
unchanged from spec 026.

**Performance Goals**: A Lint run over the current production wiki size (633 pages / ~400k
tokens of page content) completes within the existing `AgentLoop` caps
(`DefaultContextTokenCap = 200_000` per turn, `DefaultSpendTokenCap = 1_000_000` cumulative,
`DefaultTurnCap = 50`) without hitting an `AgentLoopCapException`.

**Constraints**: No new external system, no new port, no new tool capability (this feature
adds observability around the *existing* six Lint tools; it does not add a seventh). Hermetic
harness tests require no live LLM calls or API keys — only the evaluation tier does, and it
runs against recordings in CI (ADR-012).

**Scale/Scope**: Validated at 1x (633 pages / ~400k tokens, today's production size) and 2x
(≥1200 pages, via an extended synthetic fixture or an accumulated snapshot) per SC-003.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Verdict |
|---|---|---|
| I — Domain architecture & hexagonal boundaries | No new external system, no new port. `ConsideredPaths` is an in-process accumulator on the existing `GuardedToolExecutor`, the same shape as its existing `TouchedPaths`/`CreatedPaths` fields — falls under the same persistence/filesystem-adjacent reasoning already accepted for those. No infrastructure package relocation. | PASS |
| II — Pragmatic testing | Integration tests against the real filesystem in temp dirs and the real `LintRunCoordinator`; no mocking framework. Doubles limited to whatever port fakes already exist upstream (none new here). Success criteria split: SC-001/002/003/006 deterministic; SC-004/005 evaluation thresholds. SC-002 verified at the `FindingsReport` record level, not via a new parser (contracts/coverage-signal.md "Verification approach") — avoids adding parsing infrastructure the feature does not otherwise need (Ownership Test). | PASS |
| III — ADR-driven & test-enforced | All ADRs in `docs/adr/` read. ADR-030 and ADR-031 already Accepted and directly constrain the reused tool surface and write scope; neither needs amendment (see Architectural Constraints below). This feature introduces **no new structural boundary** — it extends an existing data-flow pipeline (`GuardedToolExecutor` → `RunCompletionMetadata` → NDJSON → `LintRunCoordinator` → `FindingsReportFormat`) with a new field, the same pipeline `DeniedActions`/`InboundLinksRefreshed` already travel. No new ADR is drafted. Phase 0 states explicitly: no Boundary Rule introduced by this feature. | PASS |
| IV — Behavioral & observable | Observability section below enumerates metrics, log events, and spans; each derives implementation + deterministic test + CI tasks. Contract tests exercise the existing `LintAgentTracing`/`LintAgentMetrics` production wiring, not a test-only provider. | PASS |
| V — Agentic core & deterministic harness | The coverage signal is deliberately behavior-agnostic: it records *whether* a read-shaped tool call touched a page, never *whether the agent's judgment about that page was right*. Computing it is squarely harness bookkeeping (like `TouchedPaths` already is for writes), not a decision about wiki content. What to read, when to stop, and what counts as a finding remain the agent's, under the already-updated instruction files. | PASS |

**Post-design re-check**: unchanged after Phase 1 — data-model.md and contracts/
introduce no new boundary, no new dependency, and no relocation of judgment into backend
code.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| [ADR-030](../../docs/adr/ADR-030-guarded-retrieval-tool-surface.md) | Guarded Retrieval Tools — Search, Ranged Read, and Read-Only Batch | Names the #108 problem directly in its own Context. Delivers `search_files`, ranged `read_file` (`frontmatter_only`), and read-only `batch`, which this feature's coverage tracking observes but does not modify. R3 (a ranged read must never set the write-guard CAS baseline) is unaffected — `ConsideredPaths` only records that a path was read, never re-implements or touches `OnReadFile`. |
| [ADR-031](../../docs/adr/ADR-031-lint-full-wiki-write-scope.md) | Lint Holds Full Authority Over Wiki Content, in Both Modes | Governs write scope, not reads; unaffected by this feature. `WikiContentWrites` (its accumulator) and the new `ConsideredPaths` are sibling, independent lists on `GuardedToolExecutor` — this feature adds one, does not touch the other. |
| [ADR-006](../../docs/adr/ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Every tool dispatch already passes through `GuardedToolExecutor`; `ConsideredPaths` is populated on the existing success path of that dispatch, not a new one. No new tool is added — R3/R11 (unknown-tool rejection) is unaffected. |
| [ADR-012](../../docs/adr/ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Governs how the SC-004/SC-005 evaluation tests and the extended `lint-at-scale-survey` scenario run — recorded-replay against the real `Grimoire.LintAgent` executable, `[Trait("Tier","SlowEval")]` in the standard PR pipeline. No new eval mechanism. |

**New ADR required?**: **No.** This feature extends an existing, already-Accepted data-flow
pipeline with an additive field; it introduces no new external system, no new port, no new
tool, and no new structural boundary. Per Constitution Principle III, Phase 0 of `tasks.md`
MUST state explicitly: "no Boundary Rule introduced by this feature."

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|---|---|---|
| Deciding which pages need a full read vs. frontmatter-only vs. skip | Agentic core | `agents/lint/system-prompt.md` ("Choosing how to read" — already updated by PR #179; this feature does not change it further unless SC-004/SC-005 evaluation results show a gap, in which case any change is a prompt change, not backend logic) |
| Judging what counts as a finding (contradiction, duplicate, stale link, etc.) | Agentic core | `agents/lint/system-prompt.md`, unchanged by this feature |
| Recording which pages a read-shaped tool call actually touched (`ConsideredPaths`) | Harness | `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor` |
| Computing `WikiCoverage` (pages considered vs. total, complete vs. partial) from harness-observed facts | Harness | `Grimoire.LintAgent` (`LintIntentHandler`/`RunEventEmitter`), `Grimoire.Hub.LintDispatch.LintRunCoordinator` |
| Persisting `WikiCoverage` onto the Findings Report | Harness | `Grimoire.Hub.LintFindings.FindingsReportFormat` |
| Emitting coverage metrics/log events/span attributes | Harness | `Grimoire.LintAgent.LintAgentInstrumentation`, `LintAgentMetrics`, `LintAgentTracing` |

No wiki-content judgment moves into backend code by this feature. The harness gains only the
ability to *observe and report* what the agent already does, never to decide it.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|---|---|---|---|---|---|
| SC-001 (633-page run completes) | Deterministic guarantee | Integration test against a real temp-dir content root sized to reproduce the token volume (via the extended `LintAtScaleFixture`, scaled `FillerPageCount`), asserting no `AgentLoopCapException` | None — real filesystem, real `AgentLoop`, real `GuardedToolExecutor` | Extended `LintAtScaleFixture` at production-scale `FillerPageCount` | Confirms Direction A's existing narrowing holds at the exact scale #108 names, not just the smaller pre-existing eval size |
| SC-002 (100% of runs carry a coverage report) | Deterministic guarantee | Integration test asserting the `FindingsReport.WikiCoverage` value passed to `FindingsReportFormat.Build`, across both a complete-pass and a forced-partial-pass run | Real filesystem, real coordinator; no mocking framework | A small fixture (complete pass) and a tightly budget-capped run (forced partial pass) | Record-level assertion, no new parser (contracts/coverage-signal.md) |
| SC-003 (2x scale headroom) | Deterministic guarantee | Integration/eval test at ≥1200-page synthetic scale, asserting completion and comparing reading-volume growth against the 1x baseline | Real filesystem; extended fixture | `LintAtScaleFixture` at `FillerPageCount` ≈ 1200+ | Also the trigger check for research.md R1's Direction-B revisit condition (super-linear growth) |
| SC-004 (cross-page findings survive narrowing) | Agent-judgment threshold (≥90%) | Recorded-replay evaluation, extending `lint-at-scale-survey` | Recorded model responses (ADR-012) | Fixture gains a planted contradiction pair and a duplicate-content pair | New scenario variant or additional planted defects on the existing scenario |
| SC-005 (inbound-link accuracy holds ≥90%) | Agent-judgment threshold (≥90%) | Recorded-replay evaluation | Recorded model responses (ADR-012) | Fixture gains a stale inbound-link-count page | Threshold chosen to hold steady against the pre-existing baseline, not to improve on it (FR-006) |
| SC-006 (token-efficiency gain not regressed) | Deterministic measurement of an agent-driven outcome | Comparison against the recorded `lint-at-scale-survey` baseline (`specs/026-guarded-tool-surface/baseline.md`) | Recorded model responses (ADR-012) | Existing `lint-at-scale-survey` recordings | Observational check, not a new agent-judgment threshold — a regression here is a harness-measurable fact about the same recordings |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `wiki.lint.coverage_ratio` | Histogram | `pages_considered / pages_total` for one completed Lint run (0.0–1.0) | `agent=lint` |
| `wiki.lint.runs_total` | Counter | Completed Lint runs, by coverage status | `agent=lint`, `coverage_status=complete\|partial` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `lint.run.coverage_computed` | INFO | Once per completed Lint run, when `WikiCoverage` is computed (Hub side, alongside `PersistFindingsReportAsync`) | `run_id`, `pages_total`, `pages_considered`, `coverage_status` |

**Derivation rule (MANDATORY)**: `tasks.md` MUST include, for this row: (1) an
implementation task emitting `lint.run.coverage_computed` with a stable event name and these
mandatory fields; (2) a deterministic integration test validating event name, level, and every
mandatory field for this trigger; (3) a CI task ensuring this test runs in the standard PR
pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `lint_agent.run` (existing span — attributes added, no new span) | root | adds `coverage.pages_total`, `coverage.pages_considered`, `coverage.status` alongside its existing attributes |

**Derivation rule (MANDATORY)**: `tasks.md` MUST include: (1) an implementation task adding
these attributes to the existing `lint_agent.run` span at run completion; (2) a deterministic
integration test validating the span carries these attributes (against the real
`LintAgentTracing` composition root, per Principle IV — not a test-only `ActivitySource`); (3)
a CI task ensuring this test runs in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/027-lint-at-scale/
├── plan.md              # This file
├── research.md           # Phase 0 output
├── data-model.md          # Phase 1 output
├── quickstart.md          # Phase 1 output
├── contracts/
│   └── coverage-signal.md # Phase 1 output
└── tasks.md               # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.AgentRuntime/
│   │   └── Guardrails/
│   │       └── GuardedToolExecutor.cs        # + ConsideredPaths accumulator
│   ├── Grimoire.LintAgent/
│   │   ├── Program.cs                        # LintIntentHandler computes WikiCoverage at completion
│   │   ├── RunEvents/
│   │   │   └── RunEventEmitter.cs            # RunCompletionMetadata + terminal-event payload gain WikiCoverage
│   │   ├── LintAgentMetrics.cs               # + wiki.lint.coverage_ratio, wiki.lint.runs_total
│   │   ├── LintAgentTracing.cs               # + coverage.* attributes on lint_agent.run
│   │   └── LintAgentInstrumentation.cs       # wires the above
│   └── Grimoire.Hub/
│       ├── LintDispatch/
│       │   └── LintRunCoordinator.cs         # threads WikiCoverage into PersistFindingsReportAsync; emits lint.run.coverage_computed
│       └── LintFindings/
│           └── FindingsReportFormat.cs       # FindingsReport gains WikiCoverage field + bookkeeping-block serialization
└── tests/
    ├── Grimoire.IntegrationTests/            # classicist tests for ConsideredPaths, WikiCoverage computation, log event, span attributes
    ├── Grimoire.EvalRunner/
    │   └── Scenarios/
    │       └── LintScenarioDefinitions.cs    # lint-at-scale-survey scale variants; new SC-004/SC-005 fixture defects
    └── Grimoire.AgentEvals/
        └── LintReplayEvalTests.cs            # extended assertions for coverage + new SC-004/SC-005 scenarios
```

**Structure Decision**: No new project. This feature touches exactly the existing
`Grimoire.AgentRuntime` (guarded execution), `Grimoire.LintAgent` (agent process harness),
and `Grimoire.Hub` (persistence/coordination) projects already governed by ADR-006/030/031,
plus their existing test projects. No frontend change — the Findings Report remains a
file humans read directly; a UI surface for coverage, if wanted later, is out of scope here
(not named in spec.md's user stories).

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — table intentionally omitted.

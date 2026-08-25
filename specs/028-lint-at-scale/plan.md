# Implementation Plan: Lint at Scale

**Branch**: `028-lint-at-scale` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-lint-at-scale/spec.md`

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
2. Deterministic validation of this issue's scale claims (completion, headroom, token
   efficiency) via the existing small `lint-at-scale-survey` fixture with its reading budget
   tuned — not a new large corpus. Cross-page findings and inbound-link accuracy (#42) are
   classified lower-stakes agent judgment per Constitution v1.12.0 and are covered primarily
   by the user-reported correction loop against the persisted Findings Report, with at most
   one small optional recorded-replay check for extra confidence — not a mandatory eval
   matrix. Both choices keep this feature's own eval footprint proportionate to what #108
   needs verified, rather than building new eval infrastructure disproportionate to the risk.

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

**Scale/Scope**: Validated as a budget-to-content-size *relation* (SC-003), not by literal
page count — a small fixture with its reading budget tuned to reproduce, and then exceed,
today's 633-page/200k-context ratio, holding at a tighter ratio too. See research.md R3/R5
for why this replaced the earlier plan to grow the fixture toward 633/1200+ pages.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Verdict |
|---|---|---|
| I — Domain architecture & hexagonal boundaries | No new external system, no new port. `ConsideredPaths` is an in-process accumulator on the existing `GuardedToolExecutor`, the same shape as its existing `TouchedPaths`/`CreatedPaths` fields — falls under the same persistence/filesystem-adjacent reasoning already accepted for those. No infrastructure package relocation. | PASS |
| II — Pragmatic testing | Integration tests against the real filesystem in temp dirs and the real `LintRunCoordinator`; no mocking framework. Doubles limited to whatever port fakes already exist upstream (none new here). Success criteria split per Constitution v1.12.0's tiering: SC-001/002/003/006 deterministic harness guarantees; SC-004/005 are agent-judgment criteria classified **lower-stakes** (a missed cross-page finding or a stale link count is correctable on a later pass, not destructive) — expressed narratively, satisfied primarily by the user-reported correction loop, with one small optional recorded-replay check for extra confidence rather than a mandatory eval suite gating the DoD. SC-002 verified at the `FindingsReport` record level, not via a new parser (contracts/coverage-signal.md "Verification approach") — avoids adding parsing infrastructure the feature does not otherwise need (Ownership Test). | PASS |
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
| [ADR-012](../../docs/adr/ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Governs the one optional recorded-replay check this feature adds — recorded-replay against the real `Grimoire.LintAgent` executable, `[Trait("Tier","SlowEval")]` in the standard PR pipeline. No new eval mechanism. |
| [ADR-033](../../docs/adr/ADR-033-sloweval-replay-class-set-reduction.md) | SlowEval Replay Class Set Reduced by the Lower-Stakes Eval Removal | This feature adds its one optional check as a new scenario inside the existing `LintReplayEvalTests` class (extending `lint-at-scale-survey`); it does **not** introduce a new SlowEval replay-eval class, so ADR-033's four-class enumeration is unaffected and needs no further amendment. |

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
| SC-001 (633-page-equivalent run completes) | Deterministic guarantee | Integration test: a real temp-directory content root (a handful of small pages, purpose-built for the test — not the shared eval fixture) plus a hand-rolled fake `IModelClient` (existing port, Principle II) scripted to issue enough tool calls to exceed a small simulated token/turn budget; asserts the run completes with no `AgentLoopCapException` | Hand-rolled fake `IModelClient` (existing port); real filesystem, real `AgentLoop`, real `GuardedToolExecutor` | A small ad hoc content root (a handful of pages); no eval fixture, no live/recorded LLM calls | Fully hermetic — proves the cap-enforcement mechanism is correct regardless of agent behavior. Does **not** by itself prove the *real* agent stays within that envelope on a real 633-page wiki — that is Direction A's own already-established evidence (spec 026's `lint-at-scale-survey`/SC-011, unchanged, still running in CI), which this feature relies on rather than re-proving |
| SC-002 (100% of runs carry a coverage report) | Deterministic guarantee | Integration test asserting the `FindingsReport.WikiCoverage` value passed to `FindingsReportFormat.Build`, across both a complete-pass and a forced-partial-pass run | Real filesystem, real coordinator; hand-rolled fake `IModelClient`; no mocking framework | Same small ad hoc content root as SC-001 | Record-level assertion, no new parser (contracts/coverage-signal.md) |
| SC-003 (scale headroom as a relation) | Deterministic measurement of a real agent-driven outcome | One additional scenario variant on the existing `lint-at-scale-survey` (same fixture, a tighter `ContextBudgetTokens`), recorded-replay, comparing reading volume against the SC-006 baseline ratio for super-linear growth | Recorded model responses (ADR-012) | Existing `LintAtScaleFixture`, one new budget value — no new pages | The one genuinely new eval addition this feature needs for the scale claim: confirms the *real* agent's real behavior holds at a tighter ratio, which SC-001/002's hermetic harness test cannot show (that test proves the cap mechanics are correct, not that the real agent stays inside them) — also the trigger check for research.md R1's Direction-B revisit condition (super-linear growth) |
| SC-004 (cross-page findings survive narrowing) | Lower-stakes agent-judgment (Constitution v1.12.0) | Primarily the user-reported correction loop (Observability below); one optional recorded-replay case, extending `lint-at-scale-survey`, MAY additionally exist | Recorded model responses (ADR-012), only if the optional check is added | At most one small addition to the existing fixture (one contradiction OR duplicate-content pair) — not a matrix | Absence of the optional check does not fail the DoD; see Test what we own / Constitution v1.12.0 |
| SC-005 (inbound-link accuracy holds) | Lower-stakes agent-judgment (Constitution v1.12.0) | Same treatment as SC-004: correction loop primary, one optional recorded-replay case MAY exist | Recorded model responses (ADR-012), only if the optional check is added | At most one small addition to the existing fixture (one stale inbound-link-count page) | Threshold, if the optional check is kept, is framed as "holds steady," not "improves" (FR-006) |
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

### Correction-loop observability surface (Constitution v1.12.0, Principle V)

SC-004 and SC-005 are lower-stakes and rely primarily on the user-reported correction loop.
Per the constitution's "Human in the loop" bullet, the surface the user observes this on
MUST be named here: the **persisted Findings Report file itself** (`grimoire-findings/1`,
`specs/013-lint-agent/contracts/findings-report-format.md`) — an operator reads a run's
narrative body directly to judge whether cross-page findings and inbound-link counts look
right, and, per `WikiCoverage` (this feature's own addition), now also sees whether that
judgment was made over the whole wiki or a partial pass. No new user-facing surface is
introduced for this — the same file operators already read for every other Finding Category
carries this feedback loop. If the operator notices a miss, the fix is an edit to
`agents/lint/system-prompt.md`, verified by the operator on the next run — not a code change.

## Project Structure

### Documentation (this feature)

```text
specs/028-lint-at-scale/
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
    │       └── LintScenarioDefinitions.cs    # lint-at-scale-survey gains a parameterized budget, not a bigger fixture
    └── Grimoire.AgentEvals/
        └── LintReplayEvalTests.cs            # + SC-003's budget variant; up to two optional SC-004/SC-005 scenarios if kept
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

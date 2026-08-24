# Phase 0 Research: Lint at Scale

**Feature**: [spec.md](./spec.md) | **Branch**: `027-lint-at-scale`

## R1 — Direction A vs. Direction B (issue #108's central open question)

**Decision**: Retain and extend **Direction A** (instruction-file narrowing over
`index.md`/frontmatter/search, pulling full bodies only when justified). **Direction B**
(harness-side sharding of the run into windowed sub-runs with partial-report merging) is
explicitly **not** adopted by this feature.

**Rationale**:

- ADR-030 ("Guarded Retrieval Tools", Accepted) already states the #108 problem verbatim in
  its own Context and Problem Statement and delivers the retrieval primitives
  (`search_files`, ranged `read_file` with `frontmatter_only`, read-only `batch`) that
  Direction A needs. Those primitives are live in `LintToolRegistry`
  (`backend/src/Grimoire.LintAgent/LintToolRegistry.cs:34-42`) today.
- PR #179 (spec 026's Phase N, already merged) already rewrote
  `system-prompt.md`'s "Choosing how to read" section toward frontmatter-first/search-first
  reading and measured an 86% reduction in median content tokens read on the
  `lint-at-scale-survey` eval scenario as an incidental byproduct of proving spec 026's own
  SC-011. That is direct evidence the mechanism works, not a projection.
- `AgentLoop`'s two caps (`DefaultContextTokenCap = 200_000` per turn,
  `DefaultSpendTokenCap = 1_000_000` cumulative billed —
  `backend/src/Grimoire.AgentRuntime/Core/AgentLoop.cs`) leave wide headroom once reading is
  narrowed: a run that reads a fraction of 400k tokens of content, spread across turns, does
  not approach either cap the way an unconditional whole-wiki read does.
- Direction B is a materially larger undertaking — a new run/sub-run relationship, a
  partial-report merge step, and (per Constitution Principle III) an Accepted ADR before
  `/speckit-tasks` can proceed — for a problem Direction A already has strong evidence of
  solving. Constitution Principle I ("Big Design Up Front is explicitly rejected. Structural
  boundaries are earned via ADRs, not assumed upfront") argues against building it
  speculatively.
- The issue itself frames this the same way: "A alone may be enough for a while... worth
  deciding explicitly rather than by default." This research decides it explicitly: not
  by default, but because the evidence available today doesn't justify the larger boundary.

**What this feature actually still owes**, given Direction A's mechanism already landed as a
side effect of spec 026:

1. Validate SC-001 (completion over the current wiki size) and SC-003 (2x scale headroom)
   with evaluation runs scoped to *this* issue's acceptance criteria, not borrowed
   incidentally from a different feature's eval scenario.
2. Build the harness-observable coverage signal (FR-003/FR-004) — this does not exist today
   in any form (see R2).
3. Guard against regression on cross-page findings and inbound-link accuracy (FR-005/FR-006).

**Alternative rejected**: Direction B now. Revisit only if a future SC-003 re-validation at
larger scale shows reading volume growing *super-linearly* with wiki size (evidence Direction
A's narrowing degrades as pages accumulate) — that would be the trigger for a dedicated ADR,
not a default assumed here.

## R2 — What "coverage" means and where it's computed

**Decision**: A page is **considered** by a run if any read-shaped tool call
(`read_file` in any mode — full, ranged, or `frontmatter_only` — or a page-body result from
`batch`) returns content naming that page during the run, or the page appears as a match in a
`search_files` result. A page merely enumerated by `list_files` (filename only, no content
disclosed) does **not** count — that's navigation, not something the agent has actually
looked at. **Coverage** = distinct pages considered ÷ total pages that existed in the wiki
when the run started. **Coverage status** is `complete` when every existing page was
considered in some form, `partial` otherwise.

**Rationale**: This is deliberately agent-behavior-agnostic and harness-computable: it asks
only "did a read-shaped tool call touch this page," never "was the agent's judgment about
this page correct." That keeps it a deterministic harness signal (FR-004, SC-002) rather than
smuggling agent judgment into a backend rule (Principle V) — the harness does not decide
*whether* a page needed attention, only records *whether it was looked at*. It also matches
Direction A's own premise: a page an agent judged safe to skip after reading only its
frontmatter should count as considered, not as uncovered — coverage measures reading scope,
not depth of scrutiny.

**Where it's computed — reusing an existing pipeline, not inventing one**: `GuardedToolExecutor`
already accumulates in-memory, per-run facts about write-shaped calls — `TouchedPaths`,
`CreatedPaths`, `DeletedPaths`, `WikiContentWrites`
(`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs:148-183`) — but has no
equivalent for reads (confirmed: only aggregate tool-name counters exist on `AgentLoop`, no
per-path list for reads anywhere). This feature adds a parallel accumulator,
`ConsideredPaths`, populated on the same success path as the existing write accumulators, for
the read-shaped tool names. Total page count is a filesystem enumeration the harness already
knows how to do (the same traversal `list_files` performs), taken once at run start.

The resulting `(PagesTotal, PagesConsidered, CoverageStatus)` triple flows through the exact
pipeline that already carries `DeniedActions` and `InboundLinksRefreshed` from the agent
process to the persisted record:
`GuardedToolExecutor` → `RunCompletionMetadata`
(`backend/src/Grimoire.LintAgent/RunEvents/RunEventEmitter.cs:13-38`) → NDJSON terminal event
→ `LintRunCoordinator.PersistFindingsReportAsync`
(`backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs:427-469`) →
`FindingsReportFormat.Build` (`backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs`).
Reusing this existing pipeline is why this plan introduces no new structural boundary or
integration pattern (see plan.md's Architectural Constraints section) — it's the same
data path with a new field, not a new path.

**Alternative rejected**: reusing the existing `FindingsReport.Partial` field
(`FindingsReportFormat.cs`) for this. Confirmed by research: `Partial` already means "the run
failed/crashed mid-analysis" (`LintRunCoordinator.cs`: `var partial = status ==
LintRunStatus.Failed;`), a run-outcome axis wholly distinct from wiki-coverage. Overloading it
would silently conflate "this run crashed" with "this run finished but skipped some pages by
design," which is exactly the false-confidence failure mode User Story 2 exists to prevent.
The new signal gets its own, distinctly named field.

**Alternative rejected**: deriving coverage from OpenTelemetry span attributes after the fact
(the `target` tag already on `lint_agent.tool_call` spans) instead of an in-process
accumulator. Rejected because it would make the persisted Findings Report's coverage claim
dependent on telemetry export succeeding and a separate query step, rather than a value the
Hub computes synchronously while building the record it already builds — weaker as a
"deterministic harness guarantee" (SC-002) and inconsistent with how every other bookkeeping
field on `FindingsReport` is produced today.

## R3 — Evaluation strategy for SC-001, SC-003, SC-004, SC-005, SC-006

**Decision**: Extend the existing `lint-at-scale-survey` scenario
(`backend/tests/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs:140-145`,
fixture `LintAtScaleFixture` — confirmed ~69 pages / ~50,895 content tokens, generated at
build time from `FillerPageCount = 60` + the `lint-seeded-defects` fixture, git-ignored,
deterministic via a hand-rolled LCG) rather than building new eval infrastructure. This
feature:

- Adds scenario variants that raise `FillerPageCount` and/or lower
  `ContextBudgetTokens` to validate SC-001 (current-scale completion) and SC-003 (2x
  headroom) — the scenario is already designed as a *relation* between corpus size and
  budget, not a fixed corpus, so this is parameter tuning, not new plumbing.
- Adds planted cross-page defects (a contradiction pair, a duplicate-content pair) to the
  fixture generator for SC-004, and a stale inbound-link-count page for SC-005 — both
  slot into the existing seeded-defects pattern `lint-seeded-defects` already uses.
- Adds a scorer assertion for the new coverage signal (SC-002 is deterministic and checked
  directly against the persisted `FindingsReport`; SC-006 compares content-tokens-read
  against the existing 86% baseline recorded in `specs/026-guarded-tool-surface/baseline.md`).

**Rationale**: `Grimoire.EvalRunner`'s capture/replay machinery
(`backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs`, ADR-012 recorded-replay,
`[Trait("Tier","SlowEval")]`) already runs the real `Grimoire.LintAgent` executable against
recorded model responses in the standard PR pipeline. Reusing it means this feature's
evaluation tests are gated the same way spec 026's already are, with no new harness needed to
run them.

**Correction carried from spec.md**: the earlier draft's "655 pages" figure for this fixture
was wrong; the actual fixture is ~69 pages and the 86% reduction was achieved by lowering the
context budget relative to that smaller corpus, not by running against a wiki-scale corpus.
spec.md's SC-003/SC-006 language was corrected to reflect this before this research was
written up.

## R4 — Observability signal shape

**Decision**: Add coverage attributes to the existing `lint_agent.run` root span (no new
span — the run's outcome, including coverage, is exactly the kind of fact that span already
carries), plus one new structured log event and two new metrics. See plan.md's Observability
section for the full contract.

**Rationale**: Constitution Principle IV requires contract tests to exercise the production
telemetry composition root, not a test-only provider — this reuses the same
`LintAgentTracing`/`LintAgentMetrics`/`LintAgentInstrumentation` wiring
(`backend/src/Grimoire.LintAgent/LintAgentInstrumentation.cs`) already proven to reach a real
exporter, rather than introducing a second telemetry surface for one feature.

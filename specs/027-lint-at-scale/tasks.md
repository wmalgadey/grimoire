# Tasks: Lint at Scale

**Input**: Design documents from `/specs/027-lint-at-scale/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/coverage-signal.md, quickstart.md

**Tests**: Required — Constitution Principle II mandates classicist TDD for every task
category below. SC-001/002/003/006 are hermetic deterministic tests; SC-004/005 are
recorded-replay evaluations (ADR-012).

**Logging Contract (MANDATORY)**: the 1 row in `plan.md ## Observability > Structured Log
Events` (`lint.run.coverage_computed`) has an implementation task, a deterministic test task,
and CI enforcement (T027).

**Trace Contract (MANDATORY)**: the 1 row in `plan.md ## Observability > Distributed Trace
Spans` (attributes added to the existing `lint_agent.run` span) has an implementation task, a
deterministic test task, and CI enforcement (T028).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1/US2/US3 from spec.md
- Every task cites at least one `FR-###` / `SC-###`, or names the phase goal explicitly

## Path Conventions

Web app layout: `backend/src/`, `backend/tests/`. `frontend/` is untouched by this feature.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

No Boundary Rule introduced by this feature (see plan.md § Architectural Constraints &
ADRs). ADR-030/ADR-031/ADR-006 already govern the reused tool surface, write scope, and
guarded dispatch respectively, unchanged by this feature. `ConsideredPaths` (below) is a
sibling accumulator to the existing `TouchedPaths` family on `GuardedToolExecutor`, added on
the same already-guarded dispatch path — no new dependency direction is introduced. Proceed
directly to Phase 1.

---

## Phase 1: Setup

**Purpose**: fixture/scenario infrastructure the user-story tests below need. No production
code in this phase.

- [ ] T001 [P] Add a scale parameter to `LintAtScaleFixture`'s filler-page generation
  (`backend/tests/Grimoire.EvalRunner/Workspace/LintAtScaleFixture.cs`) so `FillerPageCount`
  can be raised to production scale (~633 pages) and 2x scale (~1200+ pages) while staying
  deterministic (LCG-seeded, git-ignored) (SC-001, SC-003)
- [ ] T002 [P] Add a planted contradiction-pair generator and a planted duplicate-content-pair
  generator to the `lint-seeded-defects` fixture (`backend/tests/Grimoire.EvalRunner/...`)
  (SC-004, data-model.md "Eval fixture additions")
- [ ] T003 [P] Add a planted stale-inbound-link-count page generator to the same fixture set
  (SC-005, data-model.md "Eval fixture additions")
- [ ] T004 [US1] Add `lint-at-scale-survey-production` and `lint-at-scale-survey-2x` scenario
  variants in `backend/src/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs`, reusing
  the existing `lint-at-scale-survey` shape with the scaled fixture from T001 (SC-001, SC-003)

**Checkpoint**: fixtures and scenarios exist; no user story is blocked on anything further
shared — see Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

No prerequisites are shared across all three user stories: US1 validates existing
behavior against the scaled fixtures from Phase 1; US2 is this feature's only new production
code and is self-contained within its own phase; US3 is evaluation-only against the fixture
additions from Phase 1. Proceed directly to the user story phases — each declares its own
task-level dependencies.

---

## Phase 3: User Story 1 — A health-check run over the full wiki completes (Priority: P1) 🎯 MVP

**Goal**: Confirm SC-001 (completion at current production scale) and contribute to SC-003
(2x headroom) against the already-landed Direction A narrowing (ADR-030 tools + PR #179's
"Choosing how to read" prompt section) — extend it only if the tests below expose a real gap.

**Independent Test**: point Lint at the T004 production-scale fixture and start a run; it
reaches a terminal success state and produces a Findings Report, with zero
`AgentLoopCapException`s.

- [ ] T005 [P] [US1] Test in `backend/tests/Grimoire.IntegrationTests/LintAtScaleCompletionTests.cs`:
  a Lint run against the `lint-at-scale-survey-production` fixture (T004) completes and
  produces a `FindingsReport`, asserting no `AgentLoopCapException` is thrown (SC-001)
- [ ] T006 [P] [US1] Test in the same file: a Lint run against the `lint-at-scale-survey-2x`
  fixture (T004) also completes, and its total content tokens read do not grow
  super-linearly relative to the 1x run from T005 (SC-003; a super-linear result is the
  documented trigger to revisit Direction B per research.md R1 — file that as a new issue
  rather than silently accepting it here)
- [ ] T007 [US1] If T005 or T006 fails, narrow `agents/lint/system-prompt.md`'s "Choosing how
  to read" section only as much as needed to pass (FR-001, FR-002) — an agentic-core change,
  not backend code (plan.md § Agentic Boundary); skip this task entirely if both tests pass
  against the already-landed prompt
- [ ] T008 [P] [US1] Test in `backend/tests/Grimoire.IntegrationTests/LintAtScaleCompletionTests.cs`:
  a run against the small `lint-seeded-defects` base fixture (pre-existing, no filler pages)
  shows no regression in run time or thoroughness versus its pre-feature baseline (FR-007)

**Checkpoint**: Lint completes at 1x and 2x scale; small-wiki behavior is unregressed.

---

## Phase 4: User Story 2 — An operator can tell a complete pass from a partial one (Priority: P1)

**Goal**: Deliver the harness-computed `WikiCoverage` signal end-to-end: `GuardedToolExecutor`
→ `RunCompletionMetadata` → NDJSON terminal event → `LintRunCoordinator` →
`FindingsReport`/`FindingsReportFormat`, plus its observability.

**Independent Test**: inspect the persisted `FindingsReport` of a completed run and determine,
without reading raw tool-call logs, whether that run's wiki coverage was complete or partial.

### Core signal

- [ ] T009 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/GuardedToolExecutorConsideredPathsTests.cs`:
  `ConsideredPaths` accumulates the target path of every allowed `read_file` (full, ranged,
  `frontmatter_only`), every page-body result inside an allowed `batch`, and every match
  surfaced by `search_files` — and does **not** accumulate a path from `list_files`, nor from
  a denied read (data-model.md "ConsideredPaths", FR-004)
- [ ] T010 [US2] Implement the `ConsideredPaths` accumulator on
  `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, populated on the
  existing success path of the read-shaped tool dispatches, sibling to `TouchedPaths` (FR-004,
  data-model.md)
- [ ] T011 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/WikiCoverageComputationTests.cs`:
  against a real temp-directory content root, `WikiCoverage.Status` is `Complete` iff
  `PagesConsidered == PagesTotal`, else `Partial`, with `PagesTotal` matching an independent
  filesystem count (FR-003, SC-002)
- [ ] T012 [US2] Implement `WikiCoverage` computation in `LintIntentHandler.ExecuteAsync`
  (`backend/src/Grimoire.LintAgent/Program.cs`), from `ConsideredPaths` plus a page-count
  snapshot taken at run start — never from the agent's own narrative (FR-003, FR-004,
  data-model.md)
- [ ] T013 [US2] Add a `WikiCoverage` field to `RunCompletionMetadata` and serialize it onto
  the NDJSON `completed` event in
  `backend/src/Grimoire.LintAgent/RunEvents/RunEventEmitter.cs` (contracts/coverage-signal.md
  "NDJSON terminal event")
- [ ] T014 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/RunEventEmitterCoverageTests.cs`:
  the NDJSON terminal event's `wiki_coverage` object carries `pages_total`, `pages_considered`,
  `status` matching the computed `WikiCoverage` (contracts/coverage-signal.md)
- [ ] T015 [US2] Add a `WikiCoverage` field to the `FindingsReport` record and its bookkeeping
  block serialization in `backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs`,
  distinctly named from the existing `Partial` field (data-model.md, contracts/coverage-signal.md)
- [ ] T016 [US2] Thread the terminal event's `wiki_coverage` value into
  `LintRunCoordinator.PersistFindingsReportAsync`
  (`backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs`) onto the new
  `FindingsReport.WikiCoverage` field (data-model.md "Transport path")
- [ ] T017 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/FindingsReportCoverageTests.cs`:
  the `FindingsReport` record passed to `FindingsReportFormat.Build` carries the correct
  `WikiCoverage` for a run that considers every page (`Complete`) and for a run whose reading
  is deliberately capped short of every page (`Partial`) — asserted at the record level, no
  new parser (SC-002, contracts/coverage-signal.md "Verification approach")
- [ ] T018 [P] [US2] Test in the same file: `WikiCoverage.Status` and `FindingsReport.Partial`
  vary independently — a successful run (`Partial: false`) can carry `WikiCoverage.Status:
  Partial`, and this is asserted as the expected common case, not an error (data-model.md
  "Explicitly not `FindingsReport.Partial`")
- [ ] T019 [P] [US2] Test in the same file: a page touched only via `list_files` (never
  `read_file`, `batch`, or a `search_files` match) is excluded from `PagesConsidered`
  (edge case from spec.md, FR-003/FR-004)

### Observability

- [ ] T020 [US2] Implement the `lint.run.coverage_computed` structured log event with
  `run_id`, `pages_total`, `pages_considered`, `coverage_status` fields, emitted alongside
  `PersistFindingsReportAsync` (plan.md § Observability)
- [ ] T021 [P] [US2] Deterministic test in
  `backend/tests/Grimoire.IntegrationTests/LintCoverageObservabilityTests.cs`: the
  `lint.run.coverage_computed` event's name, level, and every mandatory field, read from the
  production logging composition root (Principle IV)
- [ ] T022 [US2] Add `coverage.pages_total`, `coverage.pages_considered`, `coverage.status`
  attributes to the existing `lint_agent.run` root span, in
  `backend/src/Grimoire.LintAgent/LintAgentTracing.cs` (plan.md § Observability)
- [ ] T023 [P] [US2] Deterministic test in the same observability test file: the
  `lint_agent.run` span carries the three coverage attributes, read from the production
  tracing composition root — not a test-only `ActivitySource` or always-on sampler
  (Principle IV)
- [ ] T024 [US2] Emit `wiki.lint.coverage_ratio` (histogram) and `wiki.lint.runs_total`
  (counter, `coverage_status` label) from `backend/src/Grimoire.LintAgent/LintAgentMetrics.cs`
  (plan.md § Observability)
- [ ] T025 [P] [US2] Deterministic test in the same observability test file: both metrics are
  recorded with the correct value/label for a completed run, read from the production metrics
  composition root (Principle IV)

**Checkpoint**: every completed Lint run persists a harness-derived, operator-legible
coverage report, fully instrumented.

---

## Phase 5: User Story 3 — Findings that span multiple pages are not silently lost (Priority: P2)

**Goal**: Confirm SC-004 (cross-page findings) and SC-005 (inbound-link accuracy) hold at or
above their thresholds now that reading is narrowed — extend the prompt only if evaluation
exposes a real gap.

**Independent Test**: run Lint against the T002/T003 planted-defect fixtures; the resulting
Findings Report surfaces the planted contradiction/duplicate and refreshes the stale
inbound-link count, at the sampled thresholds below.

- [ ] T026 [US3] Recorded-replay evaluation extending `lint-at-scale-survey` (ADR-012) in
  `backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs`: ≥ 90% of sampled runs against
  the T002 contradiction/duplicate fixture surface the planted finding in the Findings Report
  (SC-004)
- [ ] T027 [US3] Recorded-replay evaluation in the same file: ≥ 90% of sampled runs against
  the T003 stale-inbound-link fixture refresh the count to match the actual inbound-link
  graph (SC-005)
- [ ] T028 [US3] If T026 or T027 falls below threshold, narrow-tune
  `agents/lint/system-prompt.md`'s cross-page-comparison or inbound-link-refresh guidance
  only as much as needed to clear it (FR-005, FR-006) — an agentic-core change, not backend
  code; skip entirely if both evaluations already clear threshold

**Checkpoint**: cross-page finding detection and inbound-link accuracy hold at or above their
pre-feature baselines under the narrowed reading strategy.

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T029 SC-006 check: compare `lint-at-scale-survey`'s post-feature content-tokens-read
  against the recorded baseline in `specs/026-guarded-tool-surface/baseline.md`; confirm no
  regression from the 86% reduction already measured (SC-006)
- [ ] T030 Observability completeness audit (MANDATORY — Constitution Principle III/IV):
  cross-reference `plan.md ## Observability`'s one log-event row (T020/T021) and one
  trace-span row (T022/T023) plus both metrics (T024/T025) against their implementing task
  and passing test; file any gap found as a new task before declaring the DoD met
- [ ] T031 Logging contract CI enforcement: ensure T021's deterministic test runs in the
  standard PR pipeline (MANDATORY — Constitution Principle IV)
- [ ] T032 Trace contract CI enforcement: ensure T023's deterministic test runs in the
  standard PR pipeline (MANDATORY — Constitution Principle IV)
- [ ] T033 Agent-behavior evaluation completeness audit (MANDATORY — Constitution Principles
  II & V): confirm SC-004 (T026) and SC-005 (T027) both have a passing evaluation test at
  their ≥ 90% threshold via sampled recorded runs; file any gap found as a new task before
  declaring the DoD met
- [ ] T034 Run `quickstart.md` validation end to end, including the manual spot checks table
  and the Aspire Dashboard observability walkthrough

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0**: No Boundary Rule — nothing to gate on; proceed immediately
- **Phase 1 (Setup)**: No dependencies — can start immediately; T004 depends on T001
- **Phase 2 (Foundational)**: Empty — no cross-story blocking prerequisites (see phase note)
- **User Story phases (3-5)**: US1 depends on Phase 1 (T001, T004). US2 has no fixture
  dependency and can start in parallel with US1. US3 depends on Phase 1 (T002, T003). US1,
  US2, and US3 do not depend on each other.
- **Polish (Phase N)**: Depends on US1 (T029 needs a stable post-feature baseline run), US2
  (T030-T032 audit its tasks), and US3 (T033 audits its tasks)

### Within Each User Story

- Tests (T005/T006/T008 for US1; T009/T011/T014/T017-T019/T021/T023/T025 for US2;
  T026/T027 for US3) are written and run FAIL-before-pass, except the conditional
  prompt-narrowing tasks (T007, T028), which exist only to make an already-written test pass
- Within US2: T009 before T010; T011 before T012; T012 before T013; T013 before T014; T013
  before T015/T016; T015/T016 before T017/T018/T019; T016 before T020; T020 before T021;
  T022/T024 can proceed in parallel with T020/T021 once T012 exists

### Parallel Opportunities

- T001, T002, T003 (Setup, different fixture generators) run in parallel
- T005, T006, T008 (US1 tests, independent scenarios) run in parallel
- T009, T011 (US2, independent test files) run in parallel; T014, T017, T018, T019, T021,
  T023, T025 each run in parallel with their phase's other test tasks once their respective
  implementation task lands
- T026, T027 (US3, independent eval scenarios) run in parallel
- US1 and US3 can be staffed in parallel with US2 — none share an implementation file

---

## Parallel Example: User Story 2 (core signal)

```bash
# Once T012 (WikiCoverage computation) exists, these test tasks run together:
Task: "Test: NDJSON terminal event carries wiki_coverage (T014)"
Task: "Test: FindingsReport.WikiCoverage at record level, complete and partial (T017)"
Task: "Test: WikiCoverage.Status independent of FindingsReport.Partial (T018)"
Task: "Test: list_files-only page excluded from PagesConsidered (T019)"
```

---

## Implementation Strategy

### Delivery shape: Single PR (not a stack)

Per `CLAUDE.md`'s delivery-shape rule, this decision is made explicitly here, before
`/speckit-implement`: **this feature ships as one PR**, not a stacked series, even though
`tasks.md` has more than two phase groups beyond Phase 0 (the stack default's trigger
condition).

**Why a stack would be ceremony here**: unlike spec 026 (three new tools, a write-scope
change, and two ADRs — genuinely separable layers), this feature's phases are tightly coupled
around one signal:

- US1 is almost entirely test-only (validating already-landed behavior at scale); it produces
  little to no reviewable production code on its own and would make a near-empty first layer.
- US2 is the feature's entire production-code payload (one signal threaded through one
  existing pipeline) — it is not separable into smaller independently-valuable slices without
  fragmenting a single data flow across multiple PRs.
- US3 is evaluation-only, sharing its fixture groundwork with Setup and structurally unable to
  be reviewed without the fixture work Setup already does for US1.

A single PR keeps the one coherent change (fixtures → signal → observability → eval guards)
reviewable as what it actually is: one addition, not several.

### MVP First (User Story 1 + 2)

US1 and US2 are both P1 and together are the MVP — a run that completes *and* reports its own
coverage. US3 (P2) hardens against a named regression risk and should land in the same PR
per the delivery-shape decision above, but if time-boxed, US1+US2 alone already satisfy the
issue's core "completes, and coverage is observable" acceptance direction.

### Incremental Validation Inside the One PR

1. Phase 0 → Phase 1 (fixtures exist)
2. US1 tests pass against existing Direction A behavior (or T007 closes a real gap)
3. US2 implemented and tested — the coverage signal exists end to end
4. US3 evaluations pass against the new fixture defects (or T028 closes a real gap)
5. Phase N completeness audits confirm the DoD before requesting review

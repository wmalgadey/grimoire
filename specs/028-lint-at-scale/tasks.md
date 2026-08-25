# Tasks: Lint at Scale

**Input**: Design documents from `/specs/028-lint-at-scale/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/coverage-signal.md, quickstart.md

**Tests**: Required — Constitution Principle II mandates classicist TDD for every task
category below. SC-001/002 are fully hermetic (hand-rolled fake `IModelClient`, no model
calls); SC-003 is one recorded-replay evaluation variant (ADR-012); SC-004/005 are lower-stakes
per Constitution v1.12.0 — satisfied primarily by the user-reported correction loop, with at
most one small optional recorded-replay check each.

**Logging Contract (MANDATORY)**: the 1 row in `plan.md ## Observability > Structured Log
Events` (`lint.run.coverage_computed`) has an implementation task, a deterministic test task,
and CI enforcement (T029).

**Trace Contract (MANDATORY)**: the 1 row in `plan.md ## Observability > Distributed Trace
Spans` (attributes added to the existing `lint_agent.run` span) has an implementation task, a
deterministic test task, and CI enforcement (T030).

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

No shared setup needed. US1's hermetic tests build their own small ad hoc content root
inline and need one shared test double (T001, scoped to US1 since only US1 needs it); US2 has
no fixture dependency; US3's optional checks (if kept) extend the existing shared eval fixture
without any generator or scenario-infrastructure work of their own. Proceed directly to
Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

No prerequisites are shared across all three user stories: US1 is fully self-contained
(its own test double, its own ad hoc fixture); US2 is this feature's only new production code
and is self-contained within its own phase; US3 is, at most, one or two tiny additions to an
already-existing fixture. Proceed directly to the user story phases — each declares its own
task-level dependencies.

---

## Phase 3: User Story 1 — A health-check run over the full wiki completes (Priority: P1) 🎯 MVP

**Goal**: Prove SC-001 (completion) and SC-002's sibling SC-003 (scale headroom) hold, split
by what each half actually verifies: harness cap-enforcement mechanics (hermetic, fake model)
vs. the real agent's real reading behavior (recorded-replay, reusing the already-landed
Direction A narrowing from ADR-030/PR #179 — extended only if a real gap is found).

**Independent Test**: with the fake `IModelClient` scripted to read past a small simulated
budget over a real temp-directory content root, a Lint run completes and produces a
`FindingsReport`, with zero `AgentLoopCapException`s; separately, the extended
`lint-at-scale-survey` recorded-replay scenario confirms the real agent's reading stays
proportionately bounded at a tighter budget-to-content ratio than the pre-existing baseline.

- [ ] T001 [P] [US1] Hand-rolled fake `IModelClient` test double (implements the existing
  port, Principle II) in `backend/tests/Grimoire.IntegrationTests/Fakes/` — scripted to issue
  a configurable sequence of read/search/batch tool calls against a given content root, so
  SC-001/SC-002/SC-003's harness tests need no live or recorded LLM call (SC-001, SC-002,
  SC-003)
- [ ] T002 [P] [US1] Test in `backend/tests/Grimoire.IntegrationTests/LintAtScaleCompletionTests.cs`:
  with T001's fake model scripted to read enough content to exceed a small simulated
  token/turn budget over a real temp-directory content root (a handful of pages, purpose-built
  for this test — not the shared eval fixture), a Lint run completes with no
  `AgentLoopCapException` (SC-001)
- [ ] T003 [US1] Test in the same file: the same harness, run at a tighter budget-to-content
  ratio than T002, still completes and shows no super-linear growth in reading volume versus
  T002 — this is the deterministic-mechanics half of SC-003; it proves the cap-enforcement
  code is correct, not that a real agent would choose to stay inside it (see T004)
- [ ] T004 [US1] Recorded-replay evaluation (ADR-012): add exactly one new
  `lint-at-scale-survey` scenario variant (a lower `ContextBudgetTokens` against the *same*
  existing `LintAtScaleFixture` — no new pages) in
  `backend/src/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs`, replayed in
  `backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs`; confirms the real agent's real
  reading behavior stays proportionately bounded at the tighter ratio — the agent-judgment
  half of SC-003 that T003's fake model cannot demonstrate
- [ ] T005 [P] [US1] Test in `LintAtScaleCompletionTests.cs`: a run against the small
  `lint-seeded-defects` base fixture (pre-existing, no filler pages) shows no regression in
  run time or thoroughness versus its pre-feature baseline (FR-007)
- [ ] T006 [US1] If T004 shows reading volume growing super-linearly relative to the looser
  ratio (SC-003's own stated pass condition, tasks.md T003/plan.md's Test Strategy row for
  SC-003 — not a comparison against SC-011's original, looser-ratio baseline number), narrow
  `agents/lint/system-prompt.md`'s "Choosing how to read" section only as much as needed to
  restore proportional growth (FR-001, FR-002) — an agentic-core change, not backend code
  (plan.md § Agentic Boundary); skip entirely if T004 already holds against the already-landed
  prompt

**Checkpoint**: Lint's cap-enforcement mechanics are proven hermetically; the real agent's
scale headroom is confirmed by one minimal, targeted eval addition; small-wiki behavior is
unregressed.

---

## Phase 4: User Story 2 — An operator can tell a complete pass from a partial one (Priority: P1)

**Goal**: Deliver the harness-computed `WikiCoverage` signal end-to-end: `GuardedToolExecutor`
→ `RunCompletionMetadata` → NDJSON terminal event → `LintRunCoordinator` →
`FindingsReport`/`FindingsReportFormat`, plus its observability.

**Independent Test**: inspect the persisted `FindingsReport` of a completed run and determine,
without reading raw tool-call logs, whether that run's wiki coverage was complete or partial.

### Core signal

- [ ] T007 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/GuardedToolExecutorConsideredPathsTests.cs`:
  `ConsideredPaths` accumulates the target path of every allowed `read_file` (full, ranged,
  `frontmatter_only`), every page-body result inside an allowed `batch`, and every match
  surfaced by `search_files` — and does **not** accumulate a path from `list_files`, nor from
  a denied read (data-model.md "ConsideredPaths", FR-004)
- [ ] T008 [US2] Implement the `ConsideredPaths` accumulator on
  `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, populated on the
  existing success path of the read-shaped tool dispatches, sibling to `TouchedPaths` (FR-004,
  data-model.md)
- [ ] T009 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/WikiCoverageComputationTests.cs`:
  against a real temp-directory content root, `WikiCoverage.Status` is `Complete` iff
  `PagesConsidered == PagesTotal`, else `Partial`, with `PagesTotal` matching an independent
  filesystem count (FR-003, SC-002)
- [ ] T010 [US2] Implement `WikiCoverage` computation in `LintIntentHandler.ExecuteAsync`
  (`backend/src/Grimoire.LintAgent/Program.cs`), from `ConsideredPaths` plus a page-count
  snapshot taken at run start — never from the agent's own narrative (FR-003, FR-004,
  data-model.md)
- [ ] T011 [US2] Add a `WikiCoverage` field to `RunCompletionMetadata` and serialize it onto
  the NDJSON `completed` event in
  `backend/src/Grimoire.LintAgent/RunEvents/RunEventEmitter.cs` (contracts/coverage-signal.md
  "NDJSON terminal event")
- [ ] T012 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/RunEventEmitterCoverageTests.cs`:
  the NDJSON terminal event's `wiki_coverage` object carries `pages_total`, `pages_considered`,
  `status` matching the computed `WikiCoverage` (contracts/coverage-signal.md)
- [ ] T013 [US2] Add a `WikiCoverage` field to the `FindingsReport` record and its bookkeeping
  block serialization in `backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs`,
  distinctly named from the existing `Partial` field (data-model.md, contracts/coverage-signal.md)
- [ ] T014 [US2] Thread the terminal event's `wiki_coverage` value into
  `LintRunCoordinator.PersistFindingsReportAsync`
  (`backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs`) onto the new
  `FindingsReport.WikiCoverage` field (data-model.md "Transport path")
- [ ] T015 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/FindingsReportCoverageTests.cs`:
  the `FindingsReport` record passed to `FindingsReportFormat.Build` carries the correct
  `WikiCoverage` for a run that considers every page (`Complete`) and for a run whose reading
  is deliberately capped short of every page (`Partial`) — asserted at the record level, no
  new parser (SC-002, contracts/coverage-signal.md "Verification approach")
- [ ] T016 [P] [US2] Test in the same file: `WikiCoverage.Status` and `FindingsReport.Partial`
  vary independently — a successful run (`Partial: false`) can carry `WikiCoverage.Status:
  Partial`, and this is asserted as the expected common case, not an error (data-model.md
  "Explicitly not `FindingsReport.Partial`")
- [ ] T017 [P] [US2] Test in the same file: a page touched only via `list_files` (never
  `read_file`, `batch`, or a `search_files` match) is excluded from `PagesConsidered`
  (edge case from spec.md, FR-003/FR-004)

### Observability

- [ ] T018 [US2] Implement the `lint.run.coverage_computed` structured log event with
  `run_id`, `pages_total`, `pages_considered`, `coverage_status` fields, emitted alongside
  `PersistFindingsReportAsync` (plan.md § Observability)
- [ ] T019 [P] [US2] Deterministic test in
  `backend/tests/Grimoire.IntegrationTests/LintCoverageObservabilityTests.cs`: the
  `lint.run.coverage_computed` event's name, level, and every mandatory field, read from the
  production logging composition root (Principle IV)
- [ ] T020 [US2] Add `coverage.pages_total`, `coverage.pages_considered`, `coverage.status`
  attributes to the existing `lint_agent.run` root span, in
  `backend/src/Grimoire.LintAgent/LintAgentTracing.cs` (plan.md § Observability)
- [ ] T021 [P] [US2] Deterministic test in the same observability test file: the
  `lint_agent.run` span carries the three coverage attributes, read from the production
  tracing composition root — not a test-only `ActivitySource` or always-on sampler
  (Principle IV)
- [ ] T022 [US2] Emit `wiki.lint.coverage_ratio` (histogram) and `wiki.lint.runs_total`
  (counter, `coverage_status` label) from `backend/src/Grimoire.LintAgent/LintAgentMetrics.cs`
  (plan.md § Observability)
- [ ] T023 [P] [US2] Deterministic test in the same observability test file: both metrics are
  recorded with the correct value/label for a completed run, read from the production metrics
  composition root (Principle IV)

**Checkpoint**: every completed Lint run persists a harness-derived, operator-legible
coverage report, fully instrumented.

---

## Phase 5: User Story 3 — Findings that span multiple pages are not silently lost (Priority: P2)

**Goal**: Confirm SC-004 (cross-page findings) and SC-005 (inbound-link accuracy) hold under
Direction A's narrowed reading — classified **lower-stakes** per Constitution v1.12.0
Principle II, satisfied primarily by the user-reported correction loop against the persisted
Findings Report (plan.md § Observability's new "Correction-loop observability surface"
subsection), not by a mandatory eval suite.

**Independent Test**: an operator reading a completed run's Findings Report can judge, from
its narrative body and its (this feature's) `WikiCoverage` field, whether cross-page findings
and inbound-link counts look right — and, if not, adjusts `agents/lint/system-prompt.md` and
verifies the fix on the next run.

- [ ] T024 [US3] Confirm `agents/lint/system-prompt.md` already carries adequate guidance for
  cross-page comparison and inbound-link-count refreshing under the narrowed reading strategy
  (it should, per PR #179's existing "Choosing how to read" rewrite and the pre-existing
  Finding Category guidance) — this is a read-and-confirm task, not a mandatory rewrite;
  only add narrative guidance if a real gap is found (FR-005, FR-006). No numeric threshold
  gates this task — the correction loop, not a formal eval, is what makes it "done"
- [ ] T025 [P] [US3] **Optional, for extra confidence — its absence does not fail the DoD.**
  One small recorded-replay check (ADR-012) extending the existing `lint-seeded-defects`
  fixture with a single planted contradiction-or-duplicate-content pair, added to
  `backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs`'s existing `LintReplayEvalTests`
  class (no new SlowEval replay-eval class, per ADR-033) (SC-004)
- [ ] T026 [P] [US3] **Optional, for extra confidence — its absence does not fail the DoD.**
  One small recorded-replay check extending the same fixture with a single planted stale
  inbound-link-count page, in the same test class (SC-005)

**Checkpoint**: the correction loop is in place and its observability surface is documented
(plan.md); T025/T026 either pass or were deliberately, visibly skipped — never silently
dropped.

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T027 SC-006 check: compare `lint-at-scale-survey`'s post-feature content-tokens-read
  against the recorded baseline in `specs/026-guarded-tool-surface/baseline.md`; confirm no
  regression from the 86% reduction already measured (SC-006)
- [ ] T028 Observability completeness audit (MANDATORY — Constitution Principle III/IV):
  cross-reference `plan.md ## Observability`'s one log-event row (T018/T019) and one
  trace-span row (T020/T021) plus both metrics (T022/T023) against their implementing task
  and passing test; file any gap found as a new task before declaring the DoD met
- [ ] T029 Logging contract CI enforcement: ensure T019's deterministic test runs in the
  standard PR pipeline (MANDATORY — Constitution Principle IV)
- [ ] T030 Trace contract CI enforcement: ensure T021's deterministic test runs in the
  standard PR pipeline (MANDATORY — Constitution Principle IV)
- [ ] T031 Lower-stakes correction-loop completeness check (MANDATORY — Constitution v1.12.0
  Principle II/III, replaces a mandatory eval-gate audit for SC-004/SC-005): confirm
  plan.md § Observability names the Findings Report file as the surface the correction loop
  runs on, T024 was performed, and T025/T026 either pass or were deliberately skipped with
  that choice visible in this task's own resolution — not silently absent
- [ ] T032 Run `quickstart.md` validation end to end, including the manual spot checks table
  and the Aspire Dashboard observability walkthrough

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0**: No Boundary Rule — nothing to gate on; proceed immediately
- **Phase 1 (Setup)**: Empty — no shared setup; proceed immediately
- **Phase 2 (Foundational)**: Empty — no cross-story blocking prerequisites (see phase note)
- **User Story phases (3-5)**: US1, US2, and US3 do not depend on each other or on any shared
  fixture/setup phase. US1 depends only on its own T001. US3's optional checks depend only on
  the pre-existing shared eval fixture, untouched by US1/US2.
- **Polish (Phase N)**: Depends on US1 (T027 needs a stable post-feature baseline run), US2
  (T028-T030 audit its tasks), and US3 (T031 audits its tasks)

### Within Each User Story

- Tests (T002/T003/T005 for US1; T007/T009/T012/T015-T017/T019/T021/T023 for US2; T025/T026
  for US3, if kept) are written and run FAIL-before-pass, except the conditional
  prompt-narrowing/confirmation tasks (T006, T024), which exist only to make an
  already-written test pass or to confirm no gap exists
- Within US1: T001 before T002/T003; T002/T003 before T004 is not required (independent
  mechanisms), but T004 informs whether T006 is needed
- Within US2: T007 before T008; T009 before T010; T010 before T011; T011 before T012; T011
  before T013/T014; T013/T014 before T015/T016/T017; T014 before T018; T018 before T019;
  T020/T022 can proceed in parallel with T018/T019 once T010 exists

### Parallel Opportunities

- T002, T003, T005 (US1 tests, independent scenarios, once T001 exists) run in parallel
- T007, T009 (US2, independent test files) run in parallel; T012, T015, T016, T017, T019,
  T021, T023 each run in parallel with their phase's other test tasks once their respective
  implementation task lands
- T025, T026 (US3, independent optional eval scenarios) run in parallel
- US1 and US3 can be staffed in parallel with US2 — none share an implementation file

---

## Parallel Example: User Story 2 (core signal)

```bash
# Once T010 (WikiCoverage computation) exists, these test tasks run together:
Task: "Test: NDJSON terminal event carries wiki_coverage (T012)"
Task: "Test: FindingsReport.WikiCoverage at record level, complete and partial (T015)"
Task: "Test: WikiCoverage.Status independent of FindingsReport.Partial (T016)"
Task: "Test: list_files-only page excluded from PagesConsidered (T017)"
```

---

## Implementation Strategy

### Delivery shape: Single PR (not a stack)

Per `CLAUDE.md`'s delivery-shape rule, this decision is made explicitly here, before
`/speckit-implement`: **this feature ships as one PR**, not a stacked series. The feature's
eval footprint shrank considerably in the 2026-08-25 revision (research.md R5) — no new large
fixture, no new eval infrastructure beyond one scenario variant and up to two optional
planted-defect additions — which makes a stack even less warranted than it already was:

- US1 is almost entirely test-only (hermetic mechanics plus one eval scenario variant); it
  produces little reviewable production code on its own and would make a near-empty first
  layer.
- US2 is the feature's entire production-code payload (one signal threaded through one
  existing pipeline) — it is not separable into smaller independently-valuable slices without
  fragmenting a single data flow across multiple PRs.
- US3 is, at most, a documentation-confirmation task plus two small optional eval additions —
  far too small to be its own layer.

A single PR keeps the one coherent change (hermetic mechanics → signal → observability →
correction-loop documentation) reviewable as what it actually is: one small addition, not
several.

### MVP First (User Story 1 + 2)

US1 and US2 are both P1 and together are the MVP — a run that completes *and* reports its own
coverage. US3 (P2) documents and optionally hardens against a named regression risk and should
land in the same PR per the delivery-shape decision above, but if time-boxed, US1+US2 alone
already satisfy the issue's core "completes, and coverage is observable" acceptance direction.

### Incremental Validation Inside the One PR

1. Phase 0 → Phase 1 → Phase 2 (all pass-through; no gating work)
2. US1: T001's fake model double lands, T002/T003 pass hermetically, T004's eval variant
   confirms the real agent (or T006 closes a real gap)
3. US2 implemented and tested — the coverage signal exists end to end
4. US3: T024 confirms the prompt already covers cross-page/inbound-link guidance (or adds
   the minimal gap-closing text); T025/T026 pass if kept, or are visibly, deliberately skipped
5. Phase N completeness audits (including the new T031 correction-loop check) confirm the DoD
   before requesting review

---

description: "Task list template for feature implementation"
---

# Tasks: Lint at Scale

**Input**: Design documents from `/specs/028-lint-at-scale/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Included — Constitution Principle II mandates classicist TDD, and plan.md's Test Strategy maps every success criterion to a required test type.

**Logging Contract (MANDATORY)**: Covered for both `plan.md ## Observability > Structured Log Events` rows (`lint.run.coverage_computed` in US2, `wiki.log.format_deviation` in US3) — each gets an implementation task, a deterministic integration test task, and a CI-enforcement task (Final Phase).

**Trace Contract (MANDATORY)**: Covered for the one `plan.md ## Observability > Distributed Trace Spans` row (`lint_agent.run` attribute additions, US2) — implementation task, deterministic integration test task, and CI-enforcement task (Final Phase).

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2/US3 = P1, US4 = P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3/US4)
- Every task cites at least one literal `FR-###`/`SC-###` identifier from spec.md

## Path Conventions

Backend-only feature. All paths are under `backend/` (`backend/src/...`, `backend/tests/...`), matching plan.md's Project Structure.

---

## Phase 0: Structural Boundary Enforcement (Constitution Principle III)

No Boundary Rule introduced by this feature (see plan.md § Architectural Constraints & ADRs). No new ADR was accepted — the write-side's schema addition and dispatch-safety rules (FSI-1, FSI-2, FSI-3) are Feature-Scoped Invariants covered by classicist integration tests in their own user-story phase (US3), never a Phase 0 structural/reflection test, per plan.md's explicit classification. Proceed directly to Phase 1.

---

## Phase 1: Setup (Shared Infrastructure)

No setup tasks required. This feature introduces no new project, external dependency, or configuration surface (plan.md § Technical Context: "No new external dependency... no new port"). It extends four existing projects (`Grimoire.AgentRuntime`, `Grimoire.LintAgent`, `Grimoire.Hub`, and their existing test projects) in place.

---

## Phase 2: Foundational (Blocking Prerequisites)

No foundational tasks required. US1/US2 (read-side, `Grimoire.LintAgent`/`Grimoire.Hub`) and US3 (write-side, `Grimoire.AgentRuntime.Guardrails`/`WikiLog`) touch independent subsystems with no shared blocking infrastructure — and the read-narrowing mechanism US1 needs (ADR-030's retrieval tools, PR #179's prompt rewrite) is already shipped by a prior feature. Each user story below can start immediately after this phase.

**Checkpoint**: Proceed directly to user story phases; all four may be worked in parallel if staffed.

---

## Phase 3: User Story 1 - A health-check run over the full wiki completes (Priority: P1) 🎯 MVP

**Goal**: Prove a Lint run over the current production-scale wiki (633 pages / ~400k tokens) — and beyond, as the wiki grows — reaches a terminal success state and produces a Findings Report, using the already-shipped narrowing mechanism (ADR-030 retrieval tools + PR #179's prompt rewrite, spec 026). This feature adds **no new production code** for this story — it is verification-only.

**Independent Test**: Start a Lint run against a wiki of at least 600 pages / ~400k tokens of content; the run reaches a terminal success state and produces a Findings Report, without aborting on a context or token-budget failure.

### Tests for User Story 1

> **NOTE**: Write these first; the hermetic test must fail against a naive "no cap enforcement" stub before passing against the real `AgentLoop`, confirming it actually exercises cap behavior.

- [ ] T001 [P] [US1] Hermetic completion test in `backend/tests/Grimoire.IntegrationTests/LintAgent/LintAtScaleCompletionTests.cs`: a small ad hoc temp-dir content root plus a hand-rolled fake `IModelClient` scripted past a simulated `AgentLoop` budget; asserts the run reaches a terminal success state and a `FindingsReport` is written, with no `AgentLoopCapException` (FR-001, SC-001)
- [ ] T002 [P] [US1] Recorded-replay eval scenario variant in `backend/tests/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs`: add a tighter-`ContextBudgetTokens` variant of `lint-at-scale-survey` against the existing `LintAtScaleFixture` (no new pages), for comparing reading-volume growth across two budget points (FR-008, SC-003)
- [ ] T003 [US1] Recorded-replay assertion in `backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs`: the T002 variant's reading volume does not grow super-linearly as the budget fraction shrinks, and total content tokens read stays at or below the ~86% reduction already observed against `specs/026-guarded-tool-surface/baseline.md` (FR-007, SC-006)

### Implementation for User Story 1

No new production code for this story — Direction A's mechanism (ADR-030's `search_files`/ranged `read_file`/`batch`, PR #179's frontmatter-first prompt rewrite) already ships. If T001–T003 fail, treat the failure as a defect in already-shipped behavior, not a gap this feature's own tasks should patch.

**Checkpoint**: User Story 1 is independently verified — Lint completes at production scale and beyond, on the existing mechanism, with no new coupling to US2/US3.

---

## Phase 4: User Story 2 - An operator can tell a complete pass from a partial one (Priority: P1)

**Goal**: Every completed Lint run carries a harness-computed coverage report (pages considered vs. total) on its Findings Report, so "found nothing wrong" is never confused with "looked at everything and found nothing wrong."

**Independent Test**: Inspect the Findings Report (or accompanying run metadata) of any Lint run and determine, without reading instruction files or the raw tool-call log, how much of the wiki that run covered.

### Tests for User Story 2

> **NOTE**: Write these first; they must fail (no `ConsideredPaths`/`WikiCoverage` exist yet) before the implementation tasks below make them pass.

- [ ] T004 [P] [US2] Integration test in `backend/tests/Grimoire.IntegrationTests/LintAgent/WikiCoverageTests.cs`: a complete-pass run (agent reads every page) produces `FindingsReport.WikiCoverage.Status == Complete` with `PagesConsidered == PagesTotal`; a forced-partial-pass run (budget tight enough to stop early) produces `Status == Partial` with `PagesConsidered < PagesTotal` — both against a real temp-directory content root and the same fake `IModelClient` as T001 (FR-003, FR-004, SC-002)
- [ ] T005 [P] [US2] Integration test in the same file: a page that appears only in a `list_files` result (never `read_file`/`batch`/`search_files`) is absent from `ConsideredPaths` and not counted toward `PagesConsidered` (FR-004; spec.md Edge Cases)

### Implementation for User Story 2

- [ ] T006 [US2] Add a `ConsideredPaths` accumulator to `GuardedToolExecutor` (`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, alongside the existing `TouchedPaths`/`CreatedPaths`/`DeletedPaths`/`WikiContentWrites` accumulators, ~lines 148-183): populate from every successful `read_file` result (any mode), a `batch` member's read result, or a `search_files` match — never from a denied path or a bare `list_files` result (data-model.md `ConsideredPaths`) (FR-004)
- [ ] T007 [US2] Compute `WikiCoverage` (`PagesTotal`, `PagesConsidered`, `Status`) at run completion in `LintIntentHandler` (`backend/src/Grimoire.LintAgent/Program.cs`), from `ConsideredPaths` plus a filesystem page-count snapshot taken at run start (the same traversal `list_files` already performs) — never self-reported by the agent's narrative (FR-003, FR-004)
- [ ] T008 [US2] Add a `WikiCoverage` field to `RunCompletionMetadata` and serialize it onto the NDJSON `completed` terminal event in `backend/src/Grimoire.LintAgent/RunEvents/RunEventEmitter.cs` (~lines 13-38, 139-174), per `contracts/coverage-signal.md`'s `wiki_coverage` shape (FR-003)
- [ ] T009 [US2] Thread `WikiCoverage` from the parsed terminal event into `PersistFindingsReportAsync` in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` (~lines 291-296, 427-469) (FR-003)
- [ ] T010 [US2] Add a `WikiCoverage` field to the `FindingsReport` record and write it into the persisted Findings Report's bookkeeping block in `backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs` (~lines 51-96), additive per `contracts/coverage-signal.md` (FR-003)

### Observability for User Story 2

- [ ] T011 [P] [US2] Implement `wiki.lint.coverage_ratio` (Histogram) and `wiki.lint.runs_total` (Counter, labeled `coverage_status`) in `backend/src/Grimoire.LintAgent/LintAgentMetrics.cs` (plan.md § Observability)
- [ ] T012 [P] [US2] Add `coverage.pages_total`, `coverage.pages_considered`, `coverage.status` attributes to the existing `lint_agent.run` root span in `backend/src/Grimoire.LintAgent/LintAgentTracing.cs` (plan.md § Observability — Distributed Trace Spans)
- [ ] T013 [US2] Wire T011/T012 into `backend/src/Grimoire.LintAgent/LintAgentInstrumentation.cs` (plan.md § Observability)
- [ ] T014 [US2] Implement the `lint.run.coverage_computed` structured log event (INFO; fields `run_id`, `pages_total`, `pages_considered`, `coverage_status`) emitted once per completed run, alongside `PersistFindingsReportAsync` in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` (plan.md § Observability — Structured Log Events)
- [ ] T015 [P] [US2] Deterministic integration test validating `lint.run.coverage_computed`'s event name, level, and all four mandatory fields for its trigger (Logging Contract; FR-003)
- [ ] T016 [P] [US2] Deterministic integration test validating the `lint_agent.run` span carries T012's three attributes, against the real `LintAgentTracing` composition root — not a test-only `ActivitySource` (Trace Contract; Constitution Principle IV; FR-003)

**Checkpoint**: User Stories 1 AND 2 both work independently — a Lint run completes and its Findings Report states its own coverage.

---

## Phase 5: User Story 3 - A log entry can be written without re-emitting the whole file (Priority: P1)

**Goal**: `write_file` gains a `mode: "prepend"` call shape so Ingest/Query/Lint can add one `log.md` entry at O(entry size) cost instead of O(file size); and, independently, `log.md`'s format/ordering checks move from a hard deny to a monitored signal on *both* write modes (Clarifications 2026-08-27).

**Independent Test**: With `log.md` already at or beyond its current production size (~128KB), submit one well-formed new entry through the guarded tool surface — the write succeeds, the entry appears newest-first, and the agent did not need to reproduce the file's existing content.

### Tests for User Story 3

> **NOTE**: Write these first; each must fail against today's schema/dispatch/denial behavior before the implementation tasks below make it pass.

- [ ] T017 [P] [US3] Integration test in `backend/tests/Grimoire.IntegrationTests/Guardrails/SharedFileWriteGuardPrependTests.cs`: `ToolRegistry.WriteFileDefinition`'s schema still rejects an unlisted field; a call omitting `mode` is byte-identical to today's behavior; a call with `mode: "prepend"` is accepted identically by `LintToolRegistry`, `IngestToolRegistry`, and `QueryToolRegistry` (FSI-1, FR-010)
- [ ] T018 [P] [US3] Integration test in the same file: a `mode: "prepend"` write to `log.md` succeeds with no preceding `read_file` call in the same run, and commits `entry + currentContent` byte-for-byte (FSI-2, FR-010)
- [ ] T019 [P] [US3] Integration test in the same file: two concurrent writers (any mix of `mode: "replace"`/`mode: "prepend"`) both land, in lock-acquisition order, with neither entry lost or silently overwritten (FSI-2, FR-012, SC-008)
- [ ] T020 [P] [US3] Integration test in the same file: a write with a malformed heading, a missing body paragraph, or the wrong prepend order — on `mode: "replace"` and `mode: "prepend"` alike — commits exactly as submitted and is never denied (FSI-3, FR-011)
- [ ] T021 [P] [US3] Integration test in the same file: each T020 deviation emits `wiki.log.format_deviation` (event) and increments `wiki.log.format_deviation_total` (metric) with the correct reason code(s) (`log_entry_not_prepended`/`log_entry_malformed_heading`/`log_entry_missing_paragraph`); a conforming write emits neither (FSI-3, FR-016, SC-009)
- [ ] T022 [P] [US3] Integration test in the same file: a `write_file` call with `mode: "prepend"` against a `log.md` fixture seeded to ~128KB succeeds, and the call's own `content` length is the entry's length only — never the seeded size (SC-007)

### Implementation for User Story 3

- [ ] T023 [US3] Add an optional `mode` string property (`enum: ["replace", "prepend"]`, default `"replace"`) to `ToolRegistry.WriteFileDefinition`'s JSON schema in `backend/src/Grimoire.AgentRuntime/Guardrails/ToolRegistry.cs` (~line 250), keeping `additionalProperties: false` (FSI-1, FR-010)
- [ ] T024 [US3] Forward `mode` from `GuardedToolExecutor.ExecuteWriteFileAsync` (~line 450) into `SharedFileWriteGuard.EvaluateWriteAsync` in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-010)
- [ ] T025 [US3] Implement `mode: "prepend"` dispatch in `SharedFileWriteGuard` (`backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs`): acquire the existing per-target `CrossProcessFileLock`, read current content fresh under the lock (no `OnReadFile` baseline, no compare-and-swap), assemble `entry + currentContent`, commit via the existing atomic temp-file + `File.Move` path, then re-baseline via `OnWriteCommitted` (FSI-2, FR-010, FR-012)
- [ ] T026 [US3] Add an entry-direct `ValidateLogEntryFormat` overload/retargeting for the prepend path in `SharedFileWriteGuard.cs`, per `contracts/log-prepend-write.md` (FR-011)
- [ ] T027 [US3] Remove the log-format branch's denial short-circuit from `EvaluateExistingTargetChecksAsync` (`SharedFileWriteGuard.cs:230-244`): the branch's result (conforming or not, and which reason) no longer contributes to the returned denial tuple, for `mode: "replace"` and `mode: "prepend"` alike — the compare-and-swap check and the `FrontmatterOnly` checks are unaffected (FSI-3, FR-011)
- [ ] T028 [P] [US3] Add `WikiLogMetrics.RecordFormatDeviation` (`wiki.log.format_deviation_total`, labels `agent`/`mode`/`reason`) in `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogMetrics.cs`, mirroring the existing `RecordUnloggedChange` pattern (FR-016)
- [ ] T029 [P] [US3] Add `WikiLogEvents.LogFormatDeviation` (`wiki.log.format_deviation`, WARN, fields `agent`/`mode`/`path`/`reason`) in `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogEvents.cs`, mirroring the existing `LogChangeNotLogged` pattern (FR-016)
- [ ] T030 [US3] Call T028/T029 from `SharedFileWriteGuard` when the (non-denying) format check in T027 finds a deviation, and retarget the existing `guardrails.format_validate` span's `outcome` tag from `allowed`/`denied` to `conforming`/`deviated` (FR-016, SC-009)
- [ ] T031 [P] [US3] Update `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md`'s "Ingest Log (log.md) Upkeep" section (~line 325) to call `mode: "prepend"` instead of "read the whole file, then write your entry followed by exactly what you read" (FR-015)
- [ ] T032 [P] [US3] Update `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md`'s `log.md` section (~lines 192-213) to call `mode: "prepend"` (FR-015)
- [ ] T033 [P] [US3] Update `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md`'s "Reconciling `index.md` and `log.md`" step to call `mode: "prepend"` (FR-015)

**Checkpoint**: User Stories 1, 2, AND 3 are all independently functional.

---

## Phase 6: User Story 4 - Findings that span multiple pages are not silently lost (Priority: P2)

**Goal**: Cross-page findings (contradictions, duplicates) and inbound-link-count accuracy keep working at least as reliably as before Direction A's narrowing. Both are lower-stakes agent-judgment criteria (Constitution v1.12.0), satisfied primarily by the user-reported correction loop — no mandatory eval suite.

**Independent Test**: An operator reading a completed run's Findings Report can judge whether cross-page findings and inbound-link counts look right, at least as reliably as before narrowing, via the correction loop and/or an optional recorded-replay check.

### Tasks for User Story 4

- [ ] T034 [US4] Confirm in `plan.md`/`spec.md` that SC-004 and SC-005 are recorded as lower-stakes agent-judgment (Constitution v1.12.0), satisfied by the correction loop reading the persisted Findings Report narrative (`grimoire-findings/1`) — no code change; this is the completeness check the Final Phase audit (T041) will re-verify (FR-005, FR-006)
- [ ] T035 [P] [US4] OPTIONAL: add a contradiction-or-duplicate-content pair to `LintAtScaleFixture`/`lint-seeded-defects` (data-model.md § Eval fixture additions) and one recorded-replay case in `backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs` confirming the Findings Report still surfaces it at least as reliably as before narrowing — its absence does not fail the DoD (SC-004)
- [ ] T036 [P] [US4] OPTIONAL: add a stale inbound-link-count page to the same fixture and one recorded-replay case confirming the refreshed count matches the actual graph at least as reliably as the pre-existing baseline — its absence does not fail the DoD (SC-005)

**Checkpoint**: All four user stories are independently functional — US4's coverage may legitimately be correction-loop-only, by design.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [ ] T037 Observability completeness audit (MANDATORY — Constitution Principle III/IV): cross-reference every row of `plan.md ## Observability` (`wiki.lint.coverage_ratio`, `wiki.lint.runs_total`, `wiki.log.format_deviation_total`, `lint.run.coverage_computed`, `wiki.log.format_deviation`, the `lint_agent.run` span's `coverage.*` attributes) against its implementing task (T006-T016, T028-T030) and passing test, and file any gap found as a new task before declaring the DoD met
- [ ] T038 Logging contract CI enforcement (MANDATORY — Constitution Principle IV): ensure T015's and the equivalent `wiki.log.format_deviation` deterministic test (T021) run in the standard PR pipeline
- [ ] T039 Trace contract CI enforcement (MANDATORY — Constitution Principle IV): ensure T016's `lint_agent.run` attribute test runs in the standard PR pipeline
- [ ] T040 [US3] CI enforcement for the write-side deviation tests: ensure T020/T021 (`SharedFileWriteGuardPrependTests.cs`) run in the standard PR pipeline (FR-016, SC-009)
- [ ] T041 Agent-behavior evaluation completeness audit (MANDATORY — Constitution Principles II, III & V): confirm this feature has **no high-stakes** agent-judgment success criteria — SC-004, SC-005, and SC-009 are all classified lower-stakes (Constitution v1.12.0) and each is backed by a hermetic test of its surrounding harness plumbing (T004/T005 for coverage computation; T020/T021 for the deviation signal) plus documented correction-loop coverage (plan.md § Correction-loop observability surface); file any gap found as a new task before declaring the DoD met
- [ ] T042 [P] Documentation: confirm no `docs/` operator guide references the old "log.md writes are denied on malformed shape" behavior or omits the new coverage-report field; update any that do
- [ ] T043 Run `quickstart.md` validation end-to-end: Harness contracts, both sets of hand-verified spot checks (read side, write side), Observability, and the Agent behavior (evaluation tier) sections

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** and **Foundational (Phase 2)**: no tasks — proceed directly to user stories.
- **User Stories (Phase 3-6)**: each depends only on Phase 0-2 completing (trivially, since both are empty) — not on each other. US1, US2, US3 (all P1) and US4 (P2) may be worked in parallel by different people.
- **Polish (Final Phase)**: depends on all four user stories being complete (T037/T041 audit across all of them).

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on US2/US3/US4 — verification-only against already-shipped mechanism.
- **User Story 2 (P1)**: No dependency on US1's tasks (independent subsystem edges: `ConsideredPaths` on `GuardedToolExecutor`, threaded through `Grimoire.LintAgent`/`Grimoire.Hub`); shares no file with US1's test-only tasks.
- **User Story 3 (P1)**: Fully independent of US1/US2 — different subsystem (`Grimoire.AgentRuntime.Guardrails`/`WikiLog`, plus three instruction files).
- **User Story 4 (P2)**: Independent of this feature's own US1-3 tasks; depends only on Direction A's narrowing already being in place (pre-existing, from spec 026/PR #179).

### Within Each User Story

- Tests (T001-T003, T004-T005, T017-T022) MUST be written and FAIL before their story's implementation tasks.
- Within US2: T006 (accumulator) before T007 (computation) before T008 (event payload) before T009 (coordinator threading) before T010 (persisted format) — each depends on the previous link in the transport path (data-model.md).
- Within US3: T023 (schema) and T024 (dispatch forwarding) before T025 (prepend assembly); T026/T027 (format-check retargeting) can proceed in parallel with T025 but must land before T030 (signal wiring, which needs T027's deviation outcome and T028/T029's signal methods).

### Parallel Opportunities

- All of US1's tests (T001, T002) — different files.
- US2's tests (T004, T005 — same file, but independent scenarios) and its Observability tasks (T011, T012) — different files/concerns.
- All of US3's tests (T017-T022 — same file, independent scenarios) and its metric/event additions (T028, T029 — different files) and its three instruction-file updates (T031, T032, T033 — different files).
- Once Phase 2 completes, US1/US2/US3/US4 can all start in parallel if staffed.

---

## Parallel Example: User Story 3

```bash
# Launch all of US3's tests together (same file, independent scenarios — safe as separate edits, sequential commit):
Task: "Schema acceptance/rejection test in SharedFileWriteGuardPrependTests.cs (T017)"
Task: "No-baseline prepend dispatch test in SharedFileWriteGuardPrependTests.cs (T018)"
Task: "Concurrent-writer safety test in SharedFileWriteGuardPrependTests.cs (T019)"
Task: "Non-denial-on-deviation test in SharedFileWriteGuardPrependTests.cs (T020)"
Task: "Deviation-signal test in SharedFileWriteGuardPrependTests.cs (T021)"
Task: "Cost-proportionality test in SharedFileWriteGuardPrependTests.cs (T022)"

# Launch the three independent instruction-file updates together:
Task: "Update Ingest's log.md Upkeep section (T031)"
Task: "Update Query's log.md section (T032)"
Task: "Update Lint's Reconciling step (T033)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 0-2: no tasks, confirm and move on.
2. Phase 3 (US1): T001-T003. This alone proves the already-shipped read-narrowing mechanism holds at production scale — a cheap, mostly-verification MVP slice.
3. **STOP and VALIDATE**: run T001-T003; confirm green.

### Incremental Delivery

1. Phase 0-2 (no-op) → Phase 3 (US1) → validate → this is the MVP.
2. Add US2 (T004-T016) → validate independently → operators can now see coverage.
3. Add US3 (T017-T033) → validate independently → the production `log.md` write failure (issue #201) is fixed.
4. Add US4 (T034-T036) → validate via correction loop (and optional evals if kept).
5. Final Phase (T037-T043) → DoD complete.

### Delivery shape (per `CLAUDE.md`'s stacked-PR convention)

This `tasks.md` has four user-story phase groups beyond Phase 0-2 (US1, US2, US3, US4) — more than two — so the stacked-PR default applies. Per `CLAUDE.md`: *"The delivery-shape decision happens between `/speckit-tasks` and `/speckit-implement`, and it is made out loud."* Recommended cut, given the dependency analysis above (all four stories are mutually independent, sharing no file):

1. **Layer 1** — US1 (T001-T003): verification-only, smallest possible first PR.
2. **Layer 2** — US2 (T004-T016): coverage reporting, on top of Layer 1's branch.
3. **Layer 3** — US3 (T017-T033): the write-side fix, independent subsystem — could stack after Layer 2 or be sequenced first if issue #201's production urgency warrants going first.
4. **Layer 4** — US4 + Final Phase (T034-T043): optional eval additions and the completeness audit, last since it audits all three prior layers.

This is a recorded intent, not yet an invoked `stacked-pr` skill run — that invocation, and any reordering (e.g., US3 first given issue #201's live production failure), happens at the start of `/speckit-implement`, out loud, per `CLAUDE.md`.

---

## Notes

- [P] tasks touch different files, or independent scenarios within a file that don't share setup state.
- [Story] labels map every implementation/test task to its user story for traceability; Final Phase tasks have none (cross-cutting) except T040, which is US3-specific CI enforcement.
- Every task cites at least one `FR-###`/`SC-###` identifier, per this template's traceability convention.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- No task in this list touches ADR files or the constitution — that work is already complete (research.md R11-R15).

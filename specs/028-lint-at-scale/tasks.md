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

- [x] T001 [P] [US1] Hermetic completion test in `backend/tests/Grimoire.IntegrationTests/LintAtScaleCompletionTests.cs` (flat test-project layout, not the `LintAgent/` subdirectory this line originally suggested): a small ad hoc temp-dir content root plus a hand-rolled fake `IModelClient` scripted past a simulated `AgentLoop` budget; asserts the run reaches a terminal success state and a `FindingsReport` is written, with no `AgentLoopCapException` (FR-001, SC-001)
- [x] T002 [P] [US1] Recorded-replay eval scenario variant in `backend/tests/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs`: added `AtScaleSurveyTightBudget` (`ContextBudgetTokens: 10_000` vs. the original's `20_000`) against the existing `LintAtScaleFixture` (no new pages) (FR-008, SC-003)
- [ ] T003 [US1] **Deferred — not completed.** Recorded-replay assertion in `backend/tests/Grimoire.AgentEvals/LintReplayEvalTests.cs` requires a captured recording (`dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario lint-at-scale-survey-tight-budget`), which needs `GRIMOIRE_EVAL_PROVIDER_API_KEY` — unavailable in the sessions that implemented this feature. A human with credentials must run the capture command and add the assertion before this task can close (FR-007, SC-006). T041's audit records this as the one open gap.

### Implementation for User Story 1

No new production code for this story — Direction A's mechanism (ADR-030's `search_files`/ranged `read_file`/`batch`, PR #179's frontmatter-first prompt rewrite) already ships. If T001–T003 fail, treat the failure as a defect in already-shipped behavior, not a gap this feature's own tasks should patch.

**Checkpoint**: User Story 1 is independently verified — Lint completes at production scale and beyond, on the existing mechanism, with no new coupling to US2/US3.

---

## Phase 4: User Story 2 - An operator can tell a complete pass from a partial one (Priority: P1)

**Goal**: Every completed Lint run carries a harness-computed coverage report (pages considered vs. total) on its Findings Report, so "found nothing wrong" is never confused with "looked at everything and found nothing wrong."

**Independent Test**: Inspect the Findings Report (or accompanying run metadata) of any Lint run and determine, without reading instruction files or the raw tool-call log, how much of the wiki that run covered.

### Tests for User Story 2

> **NOTE**: Write these first; they must fail (no `ConsideredPaths`/`WikiCoverage` exist yet) before the implementation tasks below make them pass.

- [x] T004 [P] [US2] Integration test in `backend/tests/Grimoire.IntegrationTests/LintWikiCoverageTests.cs` (renamed from `WikiCoverageTests.cs` — CI's `AgentArtifactNamingRuleTests` requires a shared-assembly type referencing only Lint-owned namespaces to carry the "Lint" token, ADR-013): a complete-pass run (agent reads every page) produces `Complete` status with `PagesConsidered == PagesTotal`; a forced-partial-pass run (budget tight enough to stop early) produces `Partial` status with `PagesConsidered < PagesTotal` — both against a real temp-directory content root and the same fake `IModelClient` idiom as T001, exercised at the `GuardedToolExecutor`/`WikiCoverage.Compute` level (`LintIntentHandler` is `internal` and not visible across the assembly boundary from the test project) (FR-003, FR-004, SC-002)
- [x] T005 [P] [US2] Integration test in the same file: a page that appears only in a `list_files` result (never `read_file`/`batch`/`search_files`) is absent from `ConsideredPaths` and not counted toward `PagesConsidered` (FR-004; spec.md Edge Cases)

### Implementation for User Story 2

- [x] T006 [US2] Add a `ConsideredPaths` accumulator to `GuardedToolExecutor` (`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`): populated from every successful `read_file` result (any mode) and every `search_files` match (`batch` members recurse through the same `read_file`/`search_files` dispatch, so no separate handling is needed there) — never from a denied path or a bare `list_files` result (data-model.md `ConsideredPaths`) (FR-004)
- [x] T007 [US2] Compute `WikiCoverage` (`PagesTotal`, `PagesConsidered`, `Status`) at run completion in `LintIntentHandler` (`backend/src/Grimoire.LintAgent/Program.cs`), from `ConsideredPaths` plus a filesystem page-count snapshot (`LintPaths.CountMarkdownPages`) taken at run start, before `AgentLoop.RunAsync` — never self-reported by the agent's narrative (FR-003, FR-004)
- [x] T008 [US2] Add a `WikiCoverage` field to `RunCompletionMetadata` and serialize it (as `wikiCoverage`, camelCase — matching every sibling NDJSON field's convention rather than the contract doc's snake_case sketch) onto the NDJSON `completed` terminal event in `backend/src/Grimoire.AgentRuntime/RunEvents/RunEventEmitter.cs` (FR-003)
- [x] T009 [US2] Thread `WikiCoverage` from the parsed terminal event (`AgentRunEventWikiCoverage`, new Hub-side wire DTO) into `PersistFindingsReportAsync` in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` (FR-003)
- [x] T010 [US2] Add a `WikiCoverage` field (as `FindingsWikiCoverage`, nullable — null for a run whose terminal event never carried one, e.g. a liveness/spawn failure) to the `FindingsReport` record and write it (`wiki_coverage`, snake_case YAML, `null` when absent) into the persisted Findings Report's bookkeeping block in `backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs`, additive per `contracts/coverage-signal.md` (FR-003)

### Observability for User Story 2

- [x] T011 [P] [US2] Implement `wiki.lint.coverage_ratio` (Histogram) and `wiki.lint.coverage_runs_total` (Counter, labeled `coverage_status`) in `backend/src/Grimoire.LintAgent/LintAgentMetrics.cs` (plan.md § Observability). **Renamed** from the originally planned `wiki.lint.runs_total`: that name already exists as an unrelated Hub-side metric (`HubMetrics.cs`, labeled `outcome`) — reusing it for a second, differently-labeled agent-side counter would double-count runs under one metric name. `plan.md`/`quickstart.md` updated to match.
- [x] T012 [P] [US2] Add `coverage.pages_total`, `coverage.pages_considered`, `coverage.status` attributes to the existing `lint_agent.run` root span, via a new `LintAgentTracing.RecordCoverageOnCurrentRun` helper (`backend/src/Grimoire.LintAgent/LintAgentTracing.cs`) called from `LintIntentHandler.ExecuteAsync` (plan.md § Observability — Distributed Trace Spans)
- [x] T013 [US2] **Not wired through `LintAgentInstrumentation.cs`** as originally scoped: that file is the `IToolCallInstrumentation`/`IAgentLoopInstrumentation` per-tool-call/per-turn seam, and coverage is a once-per-run value with no natural per-call-back hook. T011/T012 are instead called directly from `LintIntentHandler.ExecuteAsync` in `Program.cs` — the same composition-root pattern `LintAgentLogEvents.LogInstructionsLoaded` (also a once-per-run value) already uses without going through this seam.
- [x] T014 [US2] Implement the `lint.run.coverage_computed` structured log event (INFO; fields `run_id`, `pages_total`, `pages_considered`, `coverage_status`) in `LintLifecycleLogEvents.cs`, emitted once per completed run alongside `PersistFindingsReportAsync` in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` (plan.md § Observability — Structured Log Events)
- [x] T015 [P] [US2] Deterministic integration test (`LintLogEventTests.RunCoverageComputedEvent_EmitsExpectedNameLevelAndFields`) validating `lint.run.coverage_computed`'s event name, level, and all four mandatory fields for its trigger (Logging Contract; FR-003)
- [x] T016 [P] [US2] Deterministic integration test (`LintCoverageObservabilityTests.RunSpan_CarriesCoverageAttributes_SetByRecordCoverageOnCurrentRun`) validating the `lint_agent.run` span carries T012's three attributes, against the real `LintAgentTracing` composition root — not a test-only `ActivitySource` (Trace Contract; Constitution Principle IV; FR-003)

**Checkpoint**: User Stories 1 AND 2 both work independently — a Lint run completes and its Findings Report states its own coverage.

---

## Phase 5: User Story 3 - A log entry can be written without re-emitting the whole file (Priority: P1)

**Goal**: `write_file` gains a `mode: "prepend"` call shape so Ingest/Query/Lint can add one `log.md` entry at O(entry size) cost instead of O(file size); and, independently, `log.md`'s format/ordering checks move from a hard deny to a monitored signal on *both* write modes (Clarifications 2026-08-27).

**Independent Test**: With `log.md` already at or beyond its current production size (~128KB), submit one well-formed new entry through the guarded tool surface — the write succeeds, the entry appears newest-first, and the agent did not need to reproduce the file's existing content.

### Tests for User Story 3

> **NOTE**: Write these first; each must fail against today's schema/dispatch/denial behavior before the implementation tasks below make it pass.

- [x] T017 [P] [US3] Integration test in `backend/tests/Grimoire.IntegrationTests/SharedFileWriteGuardPrependTests.cs` (flat test-project layout, not the `Guardrails/` subdirectory this line originally suggested): the schema still declares `additionalProperties: false` and the `mode` enum; a call omitting `mode` is byte-identical to today's behavior; `WriteFileDefinition` is the same shared instance across `LintToolRegistry`/`IngestToolRegistry`/`QueryToolRegistry` (FSI-1, FR-010)
- [x] T018 [P] [US3] Integration test in the same file: a `mode: "prepend"` write to `log.md` succeeds with no preceding `read_file` call in the same run, and commits `entry + currentContent` byte-for-byte (FSI-2, FR-010)
- [x] T019 [P] [US3] Integration test in the same file: two concurrent writers both land, in lock-acquisition order, with neither entry lost or silently overwritten (FSI-2, FR-012, SC-008)
- [x] T020 [P] [US3] Integration test in the same file (and in `LogEntryFormatEnforcementTests.cs`): a write with a malformed heading, a missing body paragraph, or the wrong prepend order — on `mode: "replace"` and `mode: "prepend"` alike — commits exactly as submitted and is never denied (FSI-3, FR-011)
- [x] T021 [P] [US3] Integration tests in `QueryWriteConflictRejectionAdr017MetricsTests.cs`: each deviation emits `wiki.log.format_deviation` (event, name/level/fields asserted) and increments `wiki.log.format_deviation_total` (metric) with the correct reason code(s); a conforming write emits neither (FSI-3, FR-016, SC-009)
- [x] T022 [P] [US3] Integration test in `SharedFileWriteGuardPrependTests.cs`: a `write_file` call with `mode: "prepend"` against a `log.md` fixture seeded to ~130KB succeeds, and the call's own JSON payload never includes the seeded size (SC-007)

### Implementation for User Story 3

- [x] T023 [US3] Add an optional `mode` string property (`enum: ["replace", "prepend"]`, default `"replace"`) to `ToolRegistry.WriteFileDefinition`'s JSON schema in `backend/src/Grimoire.AgentRuntime/Guardrails/ToolRegistry.cs`, keeping `additionalProperties: false` (FSI-1, FR-010)
- [x] T024 [US3] Forward `mode` from `GuardedToolExecutor.ExecuteWriteFileAsync` through `AcquireWriteLockAsync` into `SharedFileWriteGuard.EvaluateWriteAsync`'s new `isPrepend` parameter (FR-010)
- [x] T025 [US3] Implement `mode: "prepend"` dispatch in `SharedFileWriteGuard` (`backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs`): acquire the existing per-target `CrossProcessFileLock`, read current content fresh under the lock (no `OnReadFile` baseline, no compare-and-swap), assemble `entry + currentContent` (returned via the new `WriteGuardDecision.ResolvedContent`, since the caller — not the guard — performs the atomic write), commit via the existing atomic temp-file + `File.Move` path, then re-baseline via `OnWriteCommitted` (FSI-2, FR-010, FR-012). Deliberately **not reachable from index.md's catalog check** — a prepend-mode write to `index.md` concatenates unconditionally, per contract "What does not change".
- [x] T026 [US3] Log-format deviations are computed by a new `ComputeLogFormatDeviations`/`ValidateLogHeadShape` pair (replacing the old single-reason, early-returning `ValidateLogEntryFormat`) that returns *every* applicable reason code (not just the first), since a non-conforming write commits regardless and every deviation is worth reporting — the prepend path calls it with the assembled `entry + currentContent`, which trivially satisfies the ordering check by construction, so it reduces to validating the entry's own heading/paragraph shape (FR-011)
- [x] T027 [US3] Removed the log-format branch's denial short-circuit from `EvaluateExistingTargetChecksAsync`: it now returns a 3-tuple `(Reason, Detail, FormatDeviations)` — the log-format result rides in `FormatDeviations` and never contributes to `Reason`, for `mode: "replace"` and `mode: "prepend"` alike. The compare-and-swap check, the `FrontmatterOnly` checks, and index.md's catalog check are unaffected and still deny. Dead cases removed from `GuardedToolExecutor.IsWriteConflictReason`/`BuildDenialMessage` (the three `log_entry_*` reasons can no longer reach either) (FSI-3, FR-011)
- [x] T028 [P] [US3] Add `WikiLogMetrics.RecordFormatDeviation` (`wiki.log.format_deviation_total`, labels `agent`/`mode`/`reason`) in `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogMetrics.cs`, mirroring the existing `RecordUnloggedChange` pattern (FR-016)
- [x] T029 [P] [US3] Add `WikiLogEvents.LogFormatDeviation` (`wiki.log.format_deviation`, WARN, fields `agent`/`mode`/`path`/`reason`) in `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogEvents.cs`, mirroring the existing `LogChangeNotLogged` pattern (FR-016)
- [x] T030 [US3] Call T028/T029 via a new `IToolCallInstrumentation.RecordFormatDeviation` method (default no-op), overridden in `IngestToolCallInstrumentation`/`QueryToolCallInstrumentation`/`LintToolCallInstrumentation` — mirrors `RecordWriteConflictRejected`'s placement: `GuardedToolExecutor.AcquireWriteLockAsync` calls it once it has an ALLOWED decision whose `FormatDeviationReasons` is non-empty. Retargeted the existing `guardrails.format_validate` span's `outcome` tag from `allowed`/`denied` to `conforming`/`deviated` (FR-016, SC-009)
- [x] T031 [P] [US3] Updated `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md`'s "Ingest Log (log.md) Upkeep" section to call `mode: "prepend"` instead of "read the whole file, then write your entry followed by exactly what you read" (FR-015)
- [x] T032 [P] [US3] Updated `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md`'s `log.md` section and its "Recovering from a write error" bullet list to call `mode: "prepend"` and drop the now-impossible `log_entry_not_prepended` tool error (FR-015)
- [x] T033 [P] [US3] Updated `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md`'s "Reconciling `index.md` and `log.md`" step to call `mode: "prepend"` (FR-015)

**Checkpoint**: User Stories 1, 2, AND 3 are all independently functional.

---

## Phase 6: User Story 4 - Findings that span multiple pages are not silently lost (Priority: P2)

**Goal**: Cross-page findings (contradictions, duplicates) and inbound-link-count accuracy keep working at least as reliably as before Direction A's narrowing. Both are lower-stakes agent-judgment criteria (Constitution v1.12.0), satisfied primarily by the user-reported correction loop — no mandatory eval suite.

**Independent Test**: An operator reading a completed run's Findings Report can judge whether cross-page findings and inbound-link counts look right, at least as reliably as before narrowing, via the correction loop and/or an optional recorded-replay check.

### Tasks for User Story 4

- [x] T034 [US4] Confirmed in `spec.md` (Clarifications 2026-08-27 and the SC-004/SC-005 definitions themselves) that both are recorded as lower-stakes agent-judgment (Constitution v1.12.0), satisfied by the correction loop reading the persisted Findings Report narrative (`grimoire-findings/1`) — no code change (FR-005, FR-006)
- [ ] T035 [P] [US4] **Skipped, deliberately.** OPTIONAL: add a contradiction-or-duplicate-content pair to `LintAtScaleFixture`/`lint-seeded-defects` and one recorded-replay case confirming the Findings Report still surfaces it — requires the same eval-capture credentials T003 is blocked on. A human with `GRIMOIRE_EVAL_PROVIDER_API_KEY` can add this later without it counting as a DoD gap (SC-004).
- [ ] T036 [P] [US4] **Skipped, deliberately**, same reason as T035: a stale inbound-link-count fixture page plus a recorded-replay case (SC-005).

**Checkpoint**: All four user stories are independently functional — US4's coverage may legitimately be correction-loop-only, by design.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [x] T037 Observability completeness audit (MANDATORY — Constitution Principle III/IV): cross-referenced every row of `plan.md ## Observability` against its implementing task and passing test —
  - `wiki.lint.coverage_ratio` / `wiki.lint.coverage_runs_total` (renamed, see T011): T011 implements, `LintMetricsTests.LintAgentMetrics_RecordCoverage_RecordsRatio_AndIncrementsCoverageRunsTotal_WithCoverageStatusTag` tests
  - `lint_agent.run` span `coverage.*` attributes: T012 implements, `LintCoverageObservabilityTests.RunSpan_CarriesCoverageAttributes_SetByRecordCoverageOnCurrentRun` tests (real composition root)
  - `lint.run.coverage_computed` log event: T014 implements, `LintLogEventTests.RunCoverageComputedEvent_EmitsExpectedNameLevelAndFields` tests
  - `wiki.log.format_deviation_total`: T028 implements, `QueryWriteConflictRejectionAdr017MetricsTests.GuardedWrite_WithFormatDeviatingLogEntry_...`/`..._WithConformingLogEntry_EmitsNoFormatDeviationSignal` test (real `QueryToolCallInstrumentation`)
  - `wiki.log.format_deviation` log event: T029 implements, the same test class asserts event name/level/fields
  - No gap found.
- [x] T038 Logging contract CI enforcement (MANDATORY — Constitution Principle IV): `Grimoire.IntegrationTests` runs unconditionally in `.github/workflows/ci.yml`'s standard PR job — T015's and T021's tests need no separate wiring
- [x] T039 Trace contract CI enforcement (MANDATORY — Constitution Principle IV): same `ci.yml` job covers T016's test — no separate wiring needed
- [x] T040 [US3] CI enforcement for the write-side deviation tests: same `ci.yml` job covers `SharedFileWriteGuardPrependTests.cs` and every rewritten pre-existing file (FR-016, SC-009)
- [x] T041 Agent-behavior evaluation completeness audit (MANDATORY — Constitution Principles II, III & V): confirmed this feature has **no high-stakes** agent-judgment success criteria of its own — SC-004, SC-005, and SC-009 are all classified lower-stakes (Constitution v1.12.0). SC-002/coverage-computation is pure harness mechanics (T004/T005), not agent judgment at all. SC-009's deviation signal is likewise pure harness mechanics (T020/T021). SC-004/SC-005 remain correction-loop-only, per T034 — T035/T036 (their optional recorded-replay checks) are **not** a DoD gap: spec.md says explicitly their absence does not fail the Definition of Done, and the constitution's correction-loop carve-out (Principle II) applies to them by name. That much is fully compliant, not merely deferred.

  Two genuinely different items remain unresolved, neither covered by that same carve-out, both blocked on the same missing `GRIMOIRE_EVAL_PROVIDER_API_KEY` credential no session implementing this feature has had — recorded here as an accepted, explicit DoD gap rather than folded into the exempt item above:

  - **T003 (SC-003, SC-006)**: unlike SC-004/SC-005/SC-009, spec.md classifies SC-003 and SC-006 as *deterministic* criteria ("Deterministic harness guarantee, scale envelope" / "Deterministic measurement of an agent-driven outcome... not asserted as an agent-judgment threshold") — not agent-judgment, and so not eligible for Principle II's lower-stakes correction-loop exemption at all. T003's recorded-replay assertion (`lint-at-scale-survey-tight-budget`) is the only thing that would actually verify these two criteria hold; it is incomplete, purely for lack of a capture credential — no code gap. This is an unmet deterministic guarantee accepted knowingly pending that credential, not a criterion the constitution excuses.
  - **Newly discovered while stacking Layer 3/US3 onto CI**: FR-015's `write_file` `mode: "prepend"` change (T023-T026) rewrote the `log.md`-writing instructions in all three agents' `system-prompt.md` files (Ingest, Query, Lint). `Grimoire.AgentEvals`' staleness fingerprint includes each recording's `system_prompt` hash, so this correctly invalidated the trust of **every pre-existing recorded-replay scenario for all three agents**, not only the one Lint scenario anticipated above: `remediation-reverify-still-applicable`, `remediation-reverify-no-longer-applicable`, `remediation-body-edit-applied`, `lint-at-scale-survey` (Lint); `query-synthesis-decline-edit-request`, `query-read-only-decline` (Query); `instruction-change-adoption`, `adversarial-source` (Ingest) — 8 scenarios, 9 failing assertions in CI. These scenarios verify success criteria belonging to **other, already-shipped features** (Remediation Re-Verification, Query Synthesis, Ingest instruction handling), not this feature's own SC-004/SC-005/SC-009 — this audit has no visibility into whether any of them are classified high-stakes for their owning feature, and does not assert either way. This is the correct, intended behavior of the staleness mechanism itself (the old recordings depict agent behavior — reading `log.md` and reproducing its content — that the new prompt text no longer asks for), not a bug in the mechanism or in this feature's code — but it is a real, current gap in CI's automated verification of other features' criteria, caused by this feature's change, not something this feature's own lower-stakes classification can excuse. Refreshing all 8 requires the same credentialed capture step, once per scenario, run by a human with `GRIMOIRE_EVAL_PROVIDER_API_KEY`:
    ```
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario remediation-reverify-still-applicable
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario remediation-reverify-no-longer-applicable
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario remediation-body-edit-applied
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario lint-at-scale-survey
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario query-synthesis-decline-edit-request
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario query-read-only-decline
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario instruction-change-adoption
    dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario adversarial-source
    ```
    Filed as the true scope of the open gap in place of the narrower one recorded above.

  **Net effect on the Definition of Done**: the DoD's "CI/CD pipeline passes" bullet is not currently met — `Grimoire.AgentEvals` is red for the two reasons above. This is a known, explained, credential-blocked gap, not an unexplained failure, but it is still an open DoD item, not a closed one; declaring this feature's DoD "met" while it holds is a merge-readiness call for whoever owns that decision, not something this audit resolves on its own (`/speckit-analyze`, 2026-09-03).
- [x] T042 [P] Documentation: searched `docs/` for references to the old "log.md writes are denied on malformed shape" behavior — none found outside ADR-017/ADR-028 (already Deprecated/Superseded historical record, immutable per Constitution) and non-binding point-in-time analyses (`docs/llm-wiki-pattern-conformance.md`, explicitly marked `binding: none`). No operator guide references the coverage-report field's absence either (none existed before this feature). No changes needed beyond the system-prompt.md/spec-artifact updates already made in Layers 2/3.
- [x] T043 `quickstart.md` validation: **not run end-to-end** — no `dotnet` SDK available in the sessions that implemented this feature (documented in every layer's PR description). Reviewed the document's Harness contracts, spot-check tables, and Observability sections by hand against the actual implementation and confirmed they describe the shipped behavior accurately (both metric-name corrections applied); a human with build tooling should still run it once before merge as the actual gate.

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

---

## Phase 7: Convergence

- [ ] T044 [P] Update `plan.md`'s Observability § Structured Log Events row for `wiki.log.format_deviation` to list `task_id` and `turn` among its mandatory fields, and note in T037's audit that the row now matches shipped code — both fields were added to `WikiLogEvents.LogFormatDeviation` and asserted by `QueryWriteConflictRejectionAdr017MetricsTests` during PR review (commit `38e5362`), after T037's audit and plan.md's field list were both written, per plan.md: Observability § Structured Log Events (partial)

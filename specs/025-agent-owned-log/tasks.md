# Tasks: Agent-Owned, Newest-First Wiki Activity Log

**Input**: Design documents from `/specs/025-agent-owned-log/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/activity-log-write-contract.md](contracts/activity-log-write-contract.md),
[quickstart.md](quickstart.md)

**ADR gate**: [ADR-028](../../docs/adr/ADR-028-agent-owned-activity-log-prepend-ordering.md)
is **Accepted** (author sign-off, 2026-08-17), with the reciprocal `Amended by ADR-028` link
on ADR-017 and a row in `docs/adr/index.md`. Constitution Principle III's precondition for
this command is therefore met.

**Tests**: Required, not optional. Deterministic harness contracts (SC-001–SC-004, SC-008,
SC-009) are covered by hermetic integration and architecture tests; agent-judgment criteria
(SC-005–SC-007) are covered by evaluation-tier scenarios at their ≥ 0.90 thresholds. No
deterministic test may assert the wording or substance of any `system-prompt.md`
(Constitution Principle V).

**Logging Contract (MANDATORY)**: `wiki.log.change_not_logged` → implementation (T025),
deterministic integration test (T029), CI enforcement (T035).

**Trace Contract (MANDATORY)**: `wiki_log.coverage_check` and its `wiki.log.change_not_logged`
child → implementation (T024, T025), deterministic integration test (T029), CI enforcement
(T036). `guardrails.format_validate`'s existing trace test is updated in place (T013).

**Organization**: Tasks are grouped by user story. Because this feature ships as a single PR
(see Implementation Strategy), the phases are executed in order rather than staffed in
parallel; the story grouping still governs what each phase must prove.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Include exact file paths in descriptions

**Traceability**: every task cites at least one `FR-###` or `SC-###` from `spec.md`
literally. Setup and cross-cutting tasks that serve no single requirement cite the phase goal
and say so explicitly.

## Path Conventions

Backend service plus spawned CLI agent processes. Sources under `backend/src/`, tests under
`backend/tests/`. No new project, assembly, namespace, port, or adapter is introduced.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Make Boundary Rule **BR-1** live and probed before any feature code is written.

**Rule classification** (from ADR-028 § "Rule classification", already tagged by design —
no task re-derives it): **BR-1** is the only Boundary Rule. **FSI-1** (prepend-only guard
behaviour) and **FSI-2** (no harness component writes the log path) are Feature-Scoped
Invariants and are therefore covered by classicist behavioural tests in their own story
phases (T011, T016), never by a reflection/IL test here.

**BR-1**: filesystem-write APIs reachable from `Grimoire.IngestAgent`,
`Grimoire.QueryAgent`, `Grimoire.LintAgent`, and the shared `Grimoire.AgentRuntime` may be
called only from `Grimoire.AgentRuntime.Guardrails*` and
`Grimoire.AgentRuntime.Core.Adapters.Replay`. `Grimoire.AgentRuntime.WikiLog` loses its
exemption.

**⚠️ NON-NEGOTIABLE**: No feature implementation begins until Phase 0 is complete.

> **Why the deletion lives here and not in US2**: the existing `WikiLogAppender` *is* the
> violation the tightened rule detects. Tightening the allow-list is what makes the rule
> real; deleting the component is what makes it green. Research R3 records these as a single
> self-reinforcing change. US2 then owns the parts BR-1 structurally cannot reach — the Hub
> writer and the behavioural evidence.

- [ ] T001 Remove the `"Grimoire.AgentRuntime.WikiLog"` allow-list entry (and its now-stale explanatory comment) from `_allowedNamespacePrefixes` in `backend/tests/Grimoire.ArchTests/IngestAgentGuardedWriteBoundaryRuleTests.cs`, `backend/tests/Grimoire.ArchTests/QueryAgentGuardedWriteBoundaryRuleTests.cs`, and `backend/tests/Grimoire.ArchTests/LintAgentGuardedWriteBoundaryRuleTests.cs`, including the namespace list in the Ingest test's failure message (line ~113); run `dotnet test backend/tests/Grimoire.ArchTests --filter "FullyQualifiedName~GuardedWriteBoundaryRule"` and record the **RED** output naming `WikiLogAppender`'s `File.AppendAllTextAsync` call site — this is the tightened rule detecting the real violation (SC-002, ADR-028 BR-1)
- [ ] T002 Delete `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogAppender.cs` and every call site — the `WikiLogAppender` construction, field, constructor parameter, and both `EnsureLogEntryAsync` calls in `backend/src/Grimoire.IngestAgent/Program.cs` (lines ~42, ~124, ~141, ~320, ~415) and in `backend/src/Grimoire.QueryAgent/Program.cs` (lines ~48, ~51, ~104, ~115, ~200, ~236) — then re-run the arch tests and record **GREEN** (FR-001, FR-002, SC-002)
- [ ] T003 Retire the backstop's signals with the component: delete `LogBackstopAppended`/`BackstopAppendedEvent` from `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogEvents.cs`, `RecordBackstopAppended` and the `wiki.log.backstop_appended_total` counter from `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogMetrics.cs`, the `wiki_log.backstop_append` span, and their tests `backend/tests/Grimoire.IntegrationTests/WikiLogAppenderTests.cs` and `WikiLogAppenderMetricsTests.cs` (deleted whole); remove the backstop span cases from `backend/tests/Grimoire.IntegrationTests/IngestObservabilityTraceTests.cs` and refresh the now-wrong pointer comments in `backend/src/Grimoire.IngestAgent/IngestAgentLogEvents.cs` (lines ~14-15) and `backend/tests/Grimoire.IntegrationTests/IngestObservabilityLogTests.cs` (lines ~76-77, ~197) so no comment claims a deleted test asserts them (FR-002, SC-002)
- [ ] T004 Complete the Constitution Principle III **Red/Green probe** for BR-1 against the tightened rule: add a temporary class in `Grimoire.AgentRuntime.WikiLog` calling `File.AppendAllTextAsync`, run the three `*GuardedWriteBoundaryRuleTests` and confirm they FAIL naming that type, delete the probe class, re-run and confirm they PASS; record the probe outcome in the commit message (SC-002, ADR-028 BR-1)

**Definition of Done**:

- [ ] Every ADR-028 rule is classified (done in the ADR: BR-1 Boundary Rule; FSI-1, FSI-2 Feature-Scoped Invariants)
- [ ] The tightened rule is committed in all three arch tests
- [ ] Red/Green probe completed and recorded in the commit message (T004)
- [ ] Arch tests pass in CI with no violations and no probe file left behind

**Checkpoint**: No code in any agent assembly can write the activity log outside the guarded
layer. Feature code may now begin.

---

## Phase 1: Setup

**Purpose**: Establish an attributable baseline. Serves the phase goal, not a single
requirement — this is a brownfield change to existing components, so there is no project
scaffolding to create.

- [ ] T005 Record the pre-change baseline for this feature (phase goal, no single FR): run `dotnet build backend/Grimoire.slnx --configuration Release`, then `dotnet test` for `backend/tests/Grimoire.ArchTests`, `backend/tests/Grimoire.Domain.UnitTests`, and `backend/tests/Grimoire.IntegrationTests`, and note the result so any later red is attributable to this feature rather than to pre-existing state

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The two versioned instruction files (FR-013). These are foundational rather than
story-scoped because a single passage in each file carries all three behaviours the
agent-judgment stories sample — newest-first placement (US1/SC-005), the changes-only
criterion (US2/SC-006), and one complete entry per action (US3/SC-007). Editing that passage
once, here, avoids rewriting the same section in three phases.

**⚠️ CRITICAL**: No user-story work begins until this phase is complete.

> **Consequence, recorded here and actioned in T033**: the eval recording fingerprint set
> hashes the agent's `system-prompt.md`
> (`backend/src/Grimoire.EvalRunner/Recording/Fingerprints.cs`), so editing these two files
> marks **every existing Ingest and Query scenario recording stale**, and `ci.yml`'s replay
> job fails on stale or skipped recordings. This is the FR-016 instruction-change merge gate
> from feature 009 working as designed. Plan the re-capture (T033) as part of this feature,
> not as a surprise at merge time.

- [ ] T006 [P] Rewrite the activity-log passages of `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` — the write-scope lines (~44-45), the tree comment naming "the append-only activity log" (~141), and the whole **Ingest Log (log.md) Upkeep** section (~315-347) — to state newest-first placement, one complete entry per action with its own date heading regardless of existing same-date entries, and the changes-only criterion; delete the "so the harness backstop can tell your entry already covers this run" clause and the closing "for a failed run the harness appends its own minimal fallback entry" paragraph, which describe a component this feature deletes (FR-013, FR-003, FR-006, FR-007)
- [ ] T007 [P] Rewrite the corresponding passages of `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md` — write-scope item 3 (~100), the append-only shape paragraph (~191-203), and the `write_conflict_stale_read` recovery bullet (~222) whose "merged into the current content" advice must describe re-reading and re-composing a *prepend* — and name `log_entry_not_prepended` in the `Recovering from a write error` section, since that reason is in `GuardedToolExecutor`'s recoverable set (FR-013, FR-003, FR-006, FR-007, FR-011)
- [ ] T008 Verify FR-013's explicit scoping holds: `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md` is byte-unchanged by this feature, and no deterministic test anywhere asserts the wording or substance of any instruction file (Constitution Principle V) — grep the test tree for string matches against instruction content and confirm existing coverage asserts only byte-exact loading and recorded hashes (FR-013)

**Checkpoint**: The agents are instructed to prepend, to write one entry per action, and to
log only real changes. The guard that still denies prepends is inverted next (T009).

---

## Phase 3: User Story 1 — Read the newest wiki change first (Priority: P1) 🎯 MVP

**Goal**: A new entry lands at the top of the activity log and every existing entry is
preserved byte-for-byte below it, enforced at the guarded write boundary.

**Independent Test**: Two consecutive wiki-changing runs — the second run's entry appears
above the first, and the first is present and unmodified below it (quickstart.md Scenario 1).

**Feature-Scoped Invariant FSI-1** is covered here by T011's classicist, state-based tests
against the real guard — never by reflecting over the guard's shape.

- [ ] T009 [US1] Invert `SharedFileWriteGuard.ValidateLogEntryFormat` in `backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs` (~line 263): replace `proposedContent.StartsWith(currentContent, Ordinal)` with `EndsWith`, return `"log_entry_not_prepended"` on failure, and scan the **head** (`proposedContent[..^currentContent.Length]`) instead of the tail — leaving the heading-pattern and following-paragraph scans, the check's position after the compare-and-swap, and its `guardrails.format_validate` span untouched — the entry shape itself must not change, only the direction the check reads (FR-003, FR-004, FR-008, FR-010, SC-001, SC-004)
- [ ] T010 [US1] Propagate the renamed reason to every call site found in research.md R1: the recoverable-denial set in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (~line 295), the doc comment in `backend/src/Grimoire.AgentRuntime/Guardrails/DeniedActionRecord.cs` (~line 21), and the ADR-017 summary comment in `SharedFileWriteGuard.cs` (~line 26), which must now cite ADR-028 for the `log.md` half (FR-004, FR-005)
- [ ] T011 [US1] Rewrite `backend/tests/Grimoire.IntegrationTests/LogEntryFormatEnforcementTests.cs` for the prepend rule against the real guard and real files in a per-test temp dir, asserting state (returned denial reason and on-disk bytes), never interactions: a conforming prepend is **allowed** and the committed file has the new entry first with the prior bytes as an exact suffix; the old append shape is **denied** `log_entry_not_prepended` with the file unchanged; an edit, re-sort, or removal of an existing entry is denied the same way; a missing file and a zero-byte file are both valid bases whose first write is allowed; a whitespace-only head is denied `log_entry_malformed_heading`; a heading-with-no-paragraph head is denied `log_entry_missing_paragraph`; and two successive allowed prepends with byte-identical headings leave both entries present and both matching the unchanged `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` pattern (FR-003, FR-004, FR-005, FR-008, FR-009, FR-010, SC-001, SC-003, SC-004)
- [ ] T012 [P] [US1] Update the renamed reason in `backend/tests/Grimoire.IntegrationTests/QueryWriteConflictRejectionAdr017MetricsTests.cs` (lines ~20, ~51) in place rather than duplicating the case, and confirm the concurrent-prepend path still yields `write_conflict_stale_read` ahead of the ordering check (FR-004, FR-011, SC-001)
- [ ] T013 [US1] Update the existing `guardrails.format_validate` trace assertions for the new `reason` attribute value `log_entry_not_prepended`, keeping the span name, `path`/`target=log`/`outcome` attributes, and parentage unchanged (plan.md ## Observability, Distributed Trace Spans row 3) (FR-004, SC-001)
- [ ] T014 [US1] Add the `log-newest-first-placement` Ingest evaluation scenario (threshold 0.90) to `backend/src/Grimoire.EvalRunner/Scenarios/IngestScenarioDefinitions.cs` over the `empty-topic` fixture with a pre-seeded `log.md`, plus its deterministic scorer in `backend/src/Grimoire.EvalRunner/Scoring/DeterministicScorers.cs` asserting exactly one new `## [` heading was added, the pre-existing content is an unchanged suffix, and the new entry carries a non-blank paragraph; leave the existing `log-paragraph-specificity` scenario untouched — it already covers SC-005's "accurately describes" half (SC-005)

**Checkpoint**: The operator's primary complaint is fixed and structurally guaranteed.

---

## Phase 4: User Story 2 — Every entry is an agent-written record of a real change (Priority: P1)

**Goal**: Zero harness-authored content in the activity log, on every path — success, failure,
no-write, and crash reconciliation.

**Independent Test**: A no-write question-answering turn and a run that fails before changing
anything both leave the log unchanged, with no harness-generated text anywhere in the file
(quickstart.md Scenario 4).

Phase 0 already removed the agent-side writer and made BR-1 enforce its absence. This phase
covers what BR-1 structurally cannot: the Hub writer, and the behavioural evidence for
**FSI-2**.

- [ ] T015 [US2] Delete `RestartReconciler.AppendReconciliationLogAsync` and its call in `backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs` (method ~line 126, call ~line 77), along with the now-unused log-path resolution it required; reconciliation continues to record the failure in the task artifact and the operational status history and writes nothing to the wiki (FR-001, SC-002)
- [ ] T016 [US2] Add `backend/tests/Grimoire.IntegrationTests/RestartReconcilerActivityLogTests.cs` covering **FSI-2** as a classicist state-based test: run the real `RestartReconciler` against a temp content root whose `log.md` holds known bytes, with a task left in `running` state, and assert the file is byte-for-byte unchanged **while** the task artifact is marked failed and the status transition is recorded (FR-001, SC-002, SC-008)
- [ ] T017 [P] [US2] Add an integration test that a failed Ingest run leaves `log.md` byte-for-byte unchanged and writes no fallback entry, asserting additionally that no test-produced `log.md` contains the string `harness backstop` (FR-001, FR-002, FR-007, SC-002)
- [ ] T018 [P] [US2] Add an integration test that a Query turn which answers without writing any wiki content leaves `log.md` byte-for-byte unchanged (FR-007, SC-002)
- [ ] T019 [US2] Add the `log-changes-only` Query evaluation scenario (threshold 0.90) to `backend/src/Grimoire.EvalRunner/Scenarios/QueryScenarioDefinitions.cs` over `empty-topic` with a pre-seeded `log.md`, with a deterministic scorer asserting a routine lookup turn that writes no page leaves `log.md` byte-for-byte unchanged (SC-006)

**Checkpoint**: `log.md` is unambiguously agent-owned wiki content on every run path.

---

## Phase 5: User Story 3 — One complete entry per logged action (Priority: P2)

**Goal**: An action logged on a date that already has an entry produces its own complete
entry with its own date heading, never a merge into the existing day's section.

**Independent Test**: Two wiki-changing runs on the same calendar day produce two separate
complete entries, neither merged (quickstart.md Scenario 6, `log-no-day-grouping`).

- [ ] T020 [US3] Add the integration case proving the harness has no concept of a "day section": a second prepend dated the same day as an existing entry is a normal allowed prepend, both entries remain independently locatable by `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`, and no rule requires or rewards merging (FR-006, FR-009, SC-003)
- [ ] T021 [US3] Create the `backend/tests/Grimoire.AgentEvals/Fixtures/log-same-day-entry/` fixture with a pre-seeded `log.md` whose entry date is hard-coded to the scenario's capture-run date, plus a `README.md` recording the re-seed-on-re-record caveat from research.md R5 — a fixture cannot compute "today", so re-recording this scenario without re-seeding the date silently degrades it into the generic case (SC-007)
- [ ] T022 [US3] Add the `log-no-day-grouping` Ingest evaluation scenario (threshold 0.90) over that fixture, with a deterministic scorer asserting the `## [` heading count grew by exactly one and the pre-existing dated entry and its section are byte-unchanged — not extended with a bullet or a second paragraph (FR-006, SC-007)

**Checkpoint**: Day-grouping ambiguity is removed from agent behaviour and sampled.

---

## Phase 6: User Story 4 — Run bookkeeping stays visible in operational signals (Priority: P2)

**Goal**: Deleting the harness fallback costs no diagnostic capability — existing coverage is
confirmed by test (FR-012), and the one diagnostic nothing else carries is replaced by a
write-free harness signal (FR-012a).

**Independent Test**: A failed run and a no-write run are fully accounted for without opening
the wiki; a run that changes the wiki without logging emits the new signal and writes nothing
(quickstart.md Scenarios 4 and 5).

- [ ] T023 [US4] Expose the two mechanical, journal-derived properties on `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`: the run's allowed wiki-content writes (`TouchedPaths` minus the canonical activity-log path already computed at ~line 101) and whether the activity log is among `TouchedPaths` — deriving from `TouchedPaths`, never `CreatedPaths`, which is create-only and would miss an index-only or page-update run, and never by reading file content (FR-012a)
- [ ] T024 [US4] Add the write-free `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogCoverageObserver.cs`, taking the caller's frozen `ActivitySource`/`Meter` per the existing cross-agent pattern (ADR-005/ADR-013), evaluating the coverage outcome once at run end and opening the `wiki_log.coverage_check` span with attributes `type`, `task_id_or_run_id`, `wiki_content_writes`, and `outcome` ∈ {`logged`, `not_logged`, `no_change`}; it performs no I/O of any kind, which is what keeps BR-1 green (FR-012a, SC-009)
- [ ] T025 [US4] Implement the `wiki.log.change_not_logged` structured log event in `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogEvents.cs` at **Warning** level with the stable event name and all three mandatory fields `type`, `task_id_or_run_id`, `wiki_content_writes`, started as a child span of `wiki_log.coverage_check` using the existing `StartLogEventSpan` idiom, emitted only when wiki-content writes are non-empty and the log was not written (FR-012a, SC-009)
- [ ] T026 [US4] Implement the `wiki.log.unlogged_change_total` counter in `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogMetrics.cs` with its single `type` label (`ingest` | `query`), incremented on the same condition as T025 (FR-012a, SC-009)
- [ ] T027 [P] [US4] Invoke the observer once at run end in `backend/src/Grimoire.IngestAgent/Program.cs`, under the run's `ingest_agent.finalize_artifact` activity so `wiki_log.coverage_check` is its child, passing the task id as `task_id_or_run_id` (FR-012a, SC-009)
- [ ] T028 [P] [US4] Invoke the observer once at run end in `backend/src/Grimoire.QueryAgent/Program.cs` — root-parented, since Query has no run-level span at completion — passing the turn id as `task_id_or_run_id` (FR-012a, SC-009)
- [ ] T029 [US4] Add `backend/tests/Grimoire.IntegrationTests/WikiLogCoverageObservabilityTests.cs` collecting signals **through the production composition root** — the real telemetry registration, sampler, and exporter pipeline, never a hand-attached listener on the observer (the Feature-003 false-negative this rule exists to prevent): assert the event name, `Warning` level, and every mandatory field; the metric increment and its `type` label; the `wiki_log.coverage_check` and `wiki.log.change_not_logged` span names, their parent/child linkage, and the shared `task_id_or_run_id` correlation attribute; **and that `log.md` is absent or byte-unchanged**. Include the negative control: a run that writes both a page and the log emits no `wiki.log.change_not_logged` and reports `outcome=logged` (FR-012a, SC-009)
- [ ] T030 [P] [US4] Add the FR-012 confirmation test for failed runs: a failed Ingest run's outcome, stage, and correlation reference are all discoverable from the task artifact and the operational state repository **without reading `log.md`** — confirming existing coverage rather than introducing a new signal (FR-012, SC-008)
- [ ] T031 [P] [US4] Add the FR-012 confirmation test for no-write runs: a completed Query turn that wrote nothing is fully recorded in its conversation record, with completion and the absence of wiki changes discoverable without reading `log.md` (FR-012, SC-008)

**Checkpoint**: All four stories are functional; no operational visibility was lost.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Close the recording gate the instruction-file edits open, then run the mandatory
completeness audits that gate the Definition of Done.

- [ ] T032 Confirm no migration was performed: activity-log files written under the previous oldest-first rules are not rewritten, re-sorted, or migrated anywhere in the change, and an existing file legitimately holds a newest-first section above an older oldest-first section (FR-014)
- [ ] T033 Re-capture the eval recordings invalidated by T006/T007. The fingerprint set hashes each agent's `system-prompt.md`, so **every** existing Ingest scenario (`update-over-duplicate`, `overlapping-topic`, `convention-adherence`, `catalog-discoverability`, `instruction-change-adoption`, `adversarial-source`, `steering-adoption`, `log-paragraph-specificity`) and **every** existing Query scenario (`query-grounding-covered`, `query-grounding-uncovered`, `query-follow-up`, `query-read-only-decline`, `query-synthesis-created`, `query-synthesis-declined-routine`, `query-synthesis-decline-edit-request`) is stale, in addition to the three new scenarios from T014/T019/T022. Capture each with `dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario <id>` (requires live provider credentials — this is the only task in the feature that does) and commit the refreshed recordings. `ci.yml`'s replay job fails on any stale, missing, or skipped recording, so this task gates the merge (FR-013, SC-005, SC-006, SC-007)
- [ ] T034 **Observability completeness audit** (MANDATORY — Constitution Principle III/IV): cross-reference every row of `plan.md ## Observability` against its implementing task and passing test — `wiki.log.unlogged_change_total` (T026/T029), `wiki.log.change_not_logged` (T025/T029), `wiki_log.coverage_check` and its child span (T024, T025/T029), `guardrails.format_validate`'s updated `reason` (T010/T013) — and confirm the three retired signals `wiki.log.backstop_appended`, `wiki.log.backstop_appended_total`, and `wiki_log.backstop_append` are absent from source and tests (T003). File any gap found as a new task before declaring the DoD met (SC-001, SC-009)
- [ ] T035 Logging contract CI enforcement (MANDATORY — Constitution Principle IV): confirm the `wiki.log.change_not_logged` deterministic test from T029 runs in the standard PR pipeline — it lives in `backend/tests/Grimoire.IntegrationTests`, which `.github/workflows/ci.yml` already runs at the "Run hermetic integration tests" step — and that it is not tier-excluded or filtered out (SC-009)
- [ ] T036 Trace contract CI enforcement (MANDATORY — Constitution Principle IV): confirm the `wiki_log.coverage_check` parent/child and correlation assertions from T029, and the updated `guardrails.format_validate` assertions from T013, run in the same standard PR pipeline step and are not tier-excluded (SC-009, SC-001)
- [ ] T037 **Agent-behavior evaluation completeness audit** (MANDATORY — Constitution Principles II & V): confirm each agent-judgment criterion has a passing evaluation test at its defined threshold via sampled replayed runs — SC-005 (`log-newest-first-placement` plus the untouched `log-paragraph-specificity`), SC-006 (`log-changes-only`), SC-007 (`log-no-day-grouping`) — all at ≥ 0.90, and that no deterministic test was added that asserts instruction-file wording. File any gap found as a new task before declaring the DoD met (SC-005, SC-006, SC-007)
- [ ] T038 Run the quickstart.md validation end to end — Scenarios 1–5 hermetically, Scenario 6 on the eval tier, and Scenario 7's manual by-eye check that two same-day ingests produce two separate entries newest-first with no line reading like harness bookkeeping (SC-001, SC-002, SC-003, SC-004, SC-008, SC-009)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural)**: no dependencies — must complete first, including the Red/Green probe (T004)
- **Phase 1 (Setup)**: independent; may run alongside Phase 0
- **Phase 2 (Foundational)**: depends on Phase 0 — blocks all user stories
- **Phase 3 (US1)**: depends on Phase 2
- **Phase 4 (US2)**: depends on Phase 0 (which removed the agent-side writer); T019's scenario depends on Phase 2
- **Phase 5 (US3)**: depends on Phase 2 for the instruction change it samples, and on Phase 3 for the inverted guard its fixture writes against
- **Phase 6 (US4)**: depends on Phase 0 (T023's properties replace the deleted appender's role)
- **Phase 7 (Polish)**: depends on all of the above; T033 specifically requires T006, T007, T014, T019, and T022 to be final

### Story Dependencies

Unlike the template's default, these stories are **not** independently shippable — research.md
R7 records why, and the Implementation Strategy below acts on it. US1 and US2 are mutually
load-bearing: US1's tests assert denial reasons US2's removal touches, and US2's deletion
leaves the file in a state no writer can extend correctly while the guard still enforces
append-only. US3 samples behaviour that only exists once US1's inversion and Phase 2's
instructions are both in place. US4 replaces a diagnostic Phase 0 deleted.

### Within Each Story

- Tests are written against expected behaviour and observed failing before the implementation lands, except the evaluation scenarios, whose thresholds are verified after implementation by design
- Guard change before its tests are green (T009 → T011)
- Observer and signals before their contract test (T024–T028 → T029)
- Instruction files before any recording capture (T006/T007 → T033)

### Parallel Opportunities

- T006 and T007 (different instruction files)
- T012 alongside T011 (different test files)
- T017 and T018 (different run paths)
- T027 and T028 (different agent programs)
- T030 and T031 (different confirmation paths)

---

## Parallel Example: Phase 2

```bash
# Both instruction files, different agents, no shared lines:
Task: "Rewrite the activity-log passages of backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md"
Task: "Rewrite the corresponding passages of backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md"
```

---

## Implementation Strategy

### Delivery shape: **single pull request** — not a stack

CLAUDE.md's default points at a stacked PR once `tasks.md` has more than two phase groups
beyond Phase 0, and this feature has five. This is the stated exception, decided out loud
before implementation begins rather than recorded as an intent nobody acts on.

**Why**: the phases cannot be reviewed independently. The prepend inversion (US1) and the
backstop removal (US2, begun in Phase 0) are load-bearing for each other in both directions —
the guard's tests assert denial reasons the removal changes, and removing the backstop while
the guard still enforces append-only leaves the file in a state no writer can extend
correctly. Shipping US1 alone would leave BR-1's tightened allow-list red. The whole change is
one method inversion, one component deletion, one new write-free observability signal, two
instruction files, and three eval scenarios — small enough that a stack would be ceremony
rather than shorter review.

Do **not** invoke the `stacked-pr` skill for this feature.

### Execution order

1. Phase 0 — tighten BR-1, delete the appender, probe Red/Green (**non-negotiable first**)
2. Phase 1 — baseline
3. Phase 2 — instruction files (blocks all stories; opens the T033 recording gate)
4. Phase 3 — US1, the inversion and its FSI-1 tests
5. Phase 4 — US2, the Hub writer and FSI-2 evidence
6. Phase 5 — US3, same-day entry behaviour
7. Phase 6 — US4, the replacement operational signal
8. Phase 7 — re-capture recordings, then the two mandatory completeness audits and quickstart validation

### Merge gate

```bash
dotnet build backend/Grimoire.slnx --configuration Release
dotnet format backend/Grimoire.slnx --verify-no-changes
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release   # zero skips required
```

---

## Notes

- `[P]` tasks touch different files and have no ordering dependency between them
- Every task cites the `FR-###`/`SC-###` identifiers it implements or verifies; T005 cites the phase goal explicitly, as the template requires for setup work
- No mocking framework may be referenced by any test project (Constitution Principle II); every test double in this feature would be a port fake, and none is needed
- No deterministic test may assert the wording or substance of an instruction file (Constitution Principle V) — instruction-file correctness is sampled by T014/T019/T022 only
- T033 is the only task requiring live provider credentials; every other test tier is hermetic

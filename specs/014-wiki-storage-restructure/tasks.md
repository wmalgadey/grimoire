# Tasks: Wiki Storage Layout & Shared Log/Catalog Format

**Input**: Design documents from `/specs/014-wiki-storage-restructure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/log-and-catalog-entry-format.md, quickstart.md, ADR-017 (**accepted**,
extends ADR-006/ADR-015/ADR-016)

**Tests**: Required — the constitution mandates hermetic harness tests for
deterministic guarantees (SC-001–SC-004, SC-006) and evaluation-with-threshold tests
for the agent-judgment success criteria (SC-005, SC-007). No test in this feature
requires a live LLM call except the one-time `Grimoire.EvalRunner` re-recording step.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P2) to enable
independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- File paths are exact, relative to repository root

## Path Conventions

Existing web-app split: `backend/src/`, `backend/tests/`. This feature adds no new
project or assembly. It relocates and generalizes one existing type
(`Grimoire.IngestAgent.IngestLog.IngestLogAppender` → `Grimoire.AgentRuntime.WikiLog.WikiLogAppender`)
and extends one existing containment-tested type
(`Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard`, ADR-017). No
frontend change.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove the ADR-009 single-composition-point discipline extends to
forbidding the retired `pages/` wrapper concept, *before* the rename sweep begins.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [X] T001 Add `backend/tests/Grimoire.ArchTests/PagesWrapperRetirementBoundaryRuleTests.cs`:
  a source/IL scan asserting no production `.cs` file outside
  `backend/src/Grimoire.Hub/Runtime/Paths/{GrimoirePathResolver,GrimoirePathOptions,ResolvedGrimoirePaths}.cs`
  contains the literal path segment `"pages"` (as a string literal fragment, case
  sensitive) or references a `PagesDir` symbol. Run it now: it MUST fail, naming every
  one of the ~30 currently-violating files identified in `research.md` R1/R8 — this
  real, non-synthetic RED state is the Red half of the Red/Green probe (no scratch
  file needed; the violations already exist). Record the exact violation count in the
  commit message as the baseline Phase 3 (US1) must clear to zero.

**Definition of Done**:

- [X] Rule (T001) written and committed, RED baseline recorded (9 violations)
- [X] Confirmed GREEN at the end of Phase 3 (T019) — 46/46 `Grimoire.ArchTests` pass

**Checkpoint**: The retirement rule is live and proven to detect every real violation
before the rename sweep starts.

---

## Phase 1: Setup

- [X] T002 [P] Update `.gitignore`: change `data/conversations/` to `conversations/`
  (anchor moves from `data/` to base — R2); add a new `tasks/` entry (previously
  implicitly inside `wiki/`, now a base-level sibling that must not be committed to
  the outer repo).
- [X] T003 [P] Review `.env-example` and any documented `Grimoire:Paths:*` override
  examples for `PagesDir`/`ConversationsDir` naming; update to reflect
  `TasksDir`/`ContentRoot`-only naming (no `PagesDir` example remains).

**Checkpoint**: Repo bookkeeping ready for the new sibling directories.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single composition point (ADR-009) every user story depends on:
`TasksDir`/`ConversationsDir` become base-anchored, independently-configurable
locations, and `PolicyLoader` gains the `.` root-directory prefix (R2, R3).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs`: add
  `TasksDir` field + `DefaultTasksDirName = "tasks"` const, mirroring
  `ConversationsDir`'s existing shape; update `ConversationsDir`'s doc comment ("sibling
  of the content root, under the base directory," not "under the data directory").
- [X] T005 `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs`: remove the
  `pagesDir` local and its `CreateDirectoryIfMissing` call; add
  `var tasksDir = ResolveAgainst(options.TasksDir, baseDir, GrimoirePathOptions.DefaultTasksDirName);`
  (replacing the old `Path.Combine(contentRoot, "tasks")`); change `conversationsDir`'s
  anchor from `dataDir` to `baseDir`; add a `BuildLocation("tasks_dir", "TasksDir", ...)`
  entry (source-tracked, matching `ConversationsDir`/`WriteLocksDir`/`FindingsDir`) and
  a `CreateDirectoryIfMissing(logger, "tasks_dir", options.TasksDir, tasksDir)` call.
- [X] T006 `backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs`: remove
  `PagesDir`; promote `TasksDir` to a first-class resolved property alongside
  `ConversationsDir`/`WriteLocksDir`/`FindingsDir`.
- [X] T007 [P] Integration tests extending
  `backend/tests/Grimoire.IntegrationTests/PathConfiguration/DefaultLayoutTests.cs`:
  default `TasksDir` = `<base>/tasks`, default `ConversationsDir` = `<base>/conversations`;
  both auto-created; both independently overridable via config/env/CLI; both appear in
  the existing path-resolution log event's location list with no schema change needed.
- [X] T008 `backend/src/Grimoire.AgentRuntime/Instructions/PolicyLoader.cs`: extend
  `NormalizeRulePrefix` with one new case — the literal `"."` resolves to `_wikiRoot`
  itself, treated directory-style (matches the anchor and everything under it), the
  same way a trailing-slash prefix does today (R3).
- [X] T009 [P] Unit tests (new file, e.g.
  `backend/tests/Grimoire.Domain.UnitTests/PolicyLoaderRootPrefixTests.cs` or
  extending the existing `PolicyLoaderModeTests.cs`): `.` matches every path under
  `_wikiRoot`, including a nested category file (e.g. `concepts/foo.md`); an
  exact-match `index.md`/`log.md` rule placed *before* `.` in the rule list still wins
  (first-match-wins) even though `.` would also match it — pins the ordering
  dependency R3/ADR-017 relies on.

**Checkpoint**: Composition point ready. All four user stories can now proceed
(independently or in parallel).

---

## Phase 3: User Story 1 - Articles live directly under the wiki content root (Priority: P1) 🎯 MVP

**Goal**: Remove the `pages/` wrapper — articles land directly at
`<content-root>/<category>/<article>.md`.

**Independent Test**: Point an agent at a fresh content root, have it create an
article, confirm the resulting path has no wrapper segment.

### Implementation for User Story 1

- [X] T010 [US1] Repoint `ResolvedGrimoirePaths.PagesDir` consumers to `ContentRoot`:
  `backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs`,
  `backend/src/Grimoire.Hub/QueryDispatch/QueryRunCoordinator.cs`,
  `backend/src/Grimoire.Hub/IngestSubmission/SubmissionService.cs`,
  `backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs`,
  `backend/src/Grimoire.Hub/IngestDispatch/IngestAgentRequest.cs`,
  `backend/src/Grimoire.Hub/QueryDispatch/QueryAgentRequest.cs`.
- [X] T011 [US1] Rename the internal Hub↔agent-process CLI contract `--pages-dir` to
  `--content-root`: `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs`,
  `backend/src/Grimoire.IngestAgent/IngestCliOptions.cs`,
  `backend/src/Grimoire.QueryAgent/QueryCliOptions.cs`,
  `backend/src/Grimoire.LintAgent/LintCliOptions.cs`, and each agent's `Program.cs`
  argument parsing.
- [X] T012 [P] [US1] Update `data/agents/{ingest,query,lint}/policy.json`: replace
  every `{"pathPrefix": "pages/"}` entry with `{"pathPrefix": "."}` (carrying over each
  file's existing `mode`, if any); reorder each `read`/`write` array so the exact-match
  `index.md`/`log.md` entries precede `.` (R3 — required for correct first-match-wins
  behavior, pinned by T009).
- [X] T013 [P] [US1] Update `backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs`:
  remove `PagesDir`, mirror the flattened layout (articles directly under `WikiRoot`);
  update `QueryAgentProcessInvoker.cs`/`AgentProcessInvoker.cs` CLI-arg construction to
  `--content-root`.
- [X] T014 [P] [US1] Update `backend/src/Grimoire.EvalRunner/Scoring/{DeterministicScorers.cs,LintDeterministicScorers.cs}`:
  remove `pages/`-prefixed path assumptions.
- [X] T015 [P] [US1] Flatten `backend/tests/Grimoire.AgentEvals/Fixtures/lint-seeded-defects/wiki/`
  (remove the `pages/` nesting level).
- [X] T016 [P] [US1] Update `backend/tests/Grimoire.IntegrationTests/PathConfiguration/{ArtifactRelativePathsTests.cs,PathConfigurationTestHelpers.cs,QueryRuntimePathsTests.cs,IngestDispatchPathArgumentsTests.cs,StartupValidationTests.cs}`
  for the flattened layout and the `--content-root` CLI flag name.
- [X] T017 [P] [US1] Update
  `backend/tests/Grimoire.IntegrationTests/{QueryTurnSubmissionApiTests.cs,QueryWriteConflictObservabilityTests.cs,QuerySynthesisWriteObservabilityTests.cs,QueryWriteLockObservabilityTests.cs,ReplayAdapterTests.cs,PolicyLoaderModeTests.cs,PolicyLoaderFrontmatterOnlyModeTests.cs,IngestOperationalStateAndDispatchTests.cs,IngestGovernanceIdentityTests.cs,IngestFailureAndReconciliationTests.cs,LintRunLifecycleTests.cs,LintConcurrencyAndLivenessTests.cs,Fakes/IngestSubmissionPipelineFixture.cs}`:
  repoint `pages/`/`PagesDir` fixture construction to the flattened layout.
- [X] T018 [US1] Integration test proving SC-001 (new file, e.g.
  `backend/tests/Grimoire.IntegrationTests/FlatContentRootLayoutTests.cs`): fresh
  content root + scripted article-creation write lands at
  `<content-root>/<category>/<article>.md`, zero wrapper segments; top-level listing
  shows only `index.md`, `log.md`, and category folders.
- [X] T019 [US1] Rerun Phase 0's `PagesWrapperRetirementBoundaryRuleTests` — confirm
  GREEN (zero remaining `pages`/`PagesDir` references outside the three path files).
  This is the Green half of Phase 0's probe.
- [X] T020 [US1] Run `quickstart.md` Scenario 1; confirm the documented expected
  outcome.

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Tasks and conversations sit alongside the wiki (Priority: P1)

**Goal**: `tasks/` and `conversations/` become true siblings of the content root.

**Independent Test**: Trigger a task and a conversation; confirm both are written
under base-level sibling directories, neither nested inside the other or inside the
wiki content root or the internal data directory.

### Implementation for User Story 2

- [X] T021 [US2] Verify and, if needed, repoint every place that composes a task
  artifact's file path (e.g. `backend/src/Grimoire.IngestAgent/Program.cs`,
  `backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs`'s restart
  reconciliation path) to use `ResolvedGrimoirePaths.TasksDir` (from Phase 2) instead
  of any lingering `Path.Combine(contentRoot, "tasks")`.
- [X] T022 [P] [US2] Remove `{"pathPrefix": "tasks/"}` from
  `data/agents/ingest/policy.json`'s `read` and `write` arrays (R4 — task artifacts are
  harness-owned via `TaskArtifactStore`'s direct file I/O, never reached through the
  agent's own guarded `write_file`/`read_file` tool calls).
- [X] T023 [P] [US2] Update
  `backend/tests/Grimoire.IntegrationTests/IngestTaskRecordWatcherTests.cs`: `TasksDir`
  fixture at `<base>/tasks` (not `<base>/wiki/tasks`); confirm `ConversationsDir`
  fixture already matches `<base>/conversations` (it does today, per research.md — this
  test's assumption becomes production-consistent, not just accidentally similar).
- [X] T024 [P] [US2] Update
  `backend/tests/Grimoire.IntegrationTests/PathConfiguration/QueryRuntimePathsTests.cs`:
  `ConversationsDir` default assertion changes from `data/conversations` to
  `<base>/conversations`.
- [X] T025 [US2] Integration test proving SC-002 (new file, e.g.
  `backend/tests/Grimoire.IntegrationTests/SiblingDirectoryLayoutTests.cs`): trigger a
  task and a conversation against a fresh base dir; assert both under base-level
  siblings, neither nested inside `wiki/` or `data/`.
- [X] T026 [P] [US2] Regression test (FR-005): `data/`'s remaining contents (raw
  intake, operational state, secrets, agent instructions/policy, write-locks,
  findings) are structurally unaffected by this feature — a location-list snapshot
  assertion against the existing path-resolution report.
- [X] T027 [US2] Run `quickstart.md` Scenario 2; confirm the documented expected
  outcome.

**Checkpoint**: User Stories 1 and 2 both work independently — the full layout
restructuring (FR-001–FR-006) is complete.

---

## Phase 5: User Story 3 - Every agent writes log entries in the same readable format (Priority: P2)

**Goal**: One `[DATE] TYPE | SUMMARY` heading-plus-paragraph shape for every `log.md`
entry, agent-written or backstop, structurally enforced (ADR-017).

**Independent Test**: Two different agent types each append a `log.md` entry;
confirm both share the same heading-plus-paragraph structure, independently
locatable by pattern search.

### Tests for User Story 3

> **NOTE: Write these tests FIRST — they fail until T033/T036 land (Red half of ADR-017's probe)**

- [X] T028 [P] [US3] Deterministic tests
  `backend/tests/Grimoire.IntegrationTests/LogEntryFormatEnforcementTests.cs`
  (ADR-017): a well-formed append is allowed; a non-append write is denied
  (`log_entry_not_appended`); a malformed heading is denied
  (`log_entry_malformed_heading`); a heading with no following paragraph is denied
  (`log_entry_missing_paragraph`). Run now — MUST fail (RED, the check doesn't exist
  yet).
- [X] T029 [P] [US3] Unit tests for the generalized backstop (new file, e.g.
  `backend/tests/Grimoire.Domain.UnitTests/WikiLogAppenderTests.cs` or an
  `Grimoire.AgentRuntime`-adjacent test project): generates a conforming
  heading+paragraph for all three agent types (`ingest`/`query`/`lint`).

### Implementation for User Story 3

- [X] T030 [US3] Move `backend/src/Grimoire.IngestAgent/IngestLog/IngestLogAppender.cs`
  to `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogAppender.cs`, generalized: takes
  an agent `type` (`ingest`/`query`/`lint`) parameter; produces
  `## [{date:yyyy-MM-dd}] {type} | {summary}` followed by a blank line and a prose
  paragraph — replacing the old multi-field pipe-delimited heading
  (`## [{date}] ingest | {outcome} | source: ... | task: [[...]]`); source/task-link
  detail moves into the paragraph.
- [X] T031 [P] [US3] Add `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogEvents.cs`:
  `wiki.log.backstop_appended` (WARN, fields `type`, `task_id_or_run_id`, `outcome`) —
  replaces `IngestAgentLogEvents`'s `ingest.log.backstop_appended`.
- [X] T032 [P] [US3] Add `backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogMetrics.cs`:
  `wiki.log.backstop_appended_total` counter, labeled `type`.
- [X] T033 [US3] In `WikiLogAppender`, start the `wiki_log.backstop_append` trace span
  (child of the existing `{type}_agent.run` root span), replacing
  `ingest_agent.backstop_log`/`ingest_agent.append_log`; emit
  `WikiLogEvents.LogBackstopAppended` and increment the new counter.
- [X] T034 [US3] Update `backend/src/Grimoire.IngestAgent/Program.cs`: use
  `Grimoire.AgentRuntime.WikiLog.WikiLogAppender` with `type: "ingest"`; delete the old
  `Grimoire.IngestAgent.IngestLog` namespace and `IngestAgentLogEvents`'s retired
  backstop event.
- [X] T035 [P] [US3] Wire `WikiLogAppender` into `backend/src/Grimoire.QueryAgent/Program.cs`
  (`type: "query"`) — new backstop; Query has none today.
- [X] T036 [US3] Extend `backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs`
  (ADR-017): add a format-validation step gated on `canonicalTarget == LogPath`, run
  after the existing existence/CAS/`WriteMode` checks: deny `log_entry_not_appended`
  unless the proposed content extends the current content byte-for-byte (or the file
  doesn't exist yet); deny `log_entry_malformed_heading` unless the appended tail's
  first non-blank line matches `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`; deny
  `log_entry_missing_paragraph` unless a further non-blank line follows before the
  tail ends. Confirm T028 now passes (GREEN).
- [X] T037 [P] [US3] Add the `guardrails.format_validate` trace span (child of the
  existing `*_agent.tool_call` span) around the new check in `SharedFileWriteGuard`,
  attributes `path`, `target=log`, `outcome=allowed|denied`, `reason`.
- [X] T038 [P] [US3] `backend/src/Grimoire.AgentRuntime/Guardrails/DeniedActionRecord.cs`:
  document the three new reason strings (doc comment only, matching the ADR-015/016
  precedent).
- [X] T039 [US3] Update `data/agents/ingest/system-prompt.md`'s "Ingest Log (log.md)
  Upkeep" section: replace the `## YYYY-MM-DD` date-only heading + bulleted
  `* **Verb**:` convention with `## [YYYY-MM-DD] TYPE | SUMMARY` + prose paragraph;
  move source-reference/task-link detail into the paragraph.
- [X] T040 [P] [US3] Update `data/agents/query/system-prompt.md`'s log-upkeep
  delegation reference to match the new convention.
- [X] T041 [P] [US3] New `Grimoire.EvalRunner` scorer (SC-005, in
  `backend/src/Grimoire.EvalRunner/Scoring/`): checks a sampled log paragraph's
  specificity against the task's actual diff, not a generic restatement of the
  heading; wire into existing ingest/query eval scenarios, threshold ≥90%.
- [ ] T042 [US3] Re-record `Grimoire.EvalRunner` recordings affected by the new log
  format instruction (existing ingest/query scenarios) under `data/evals/recordings/`.
  **Superseded/consolidated by T062** (Phase 7 completeness audit) — do this as part of
  T062's single re-recording pass covering all 17 stale scenarios, not separately.
- [X] T043 [US3] Run `quickstart.md` Scenario 3; confirm the documented expected
  outcome.

**Checkpoint**: User Stories 1–3 are all independently functional.

---

## Phase 6: User Story 4 - The catalog entry format matches the reference wiki, like the log (Priority: P2)

**Goal**: One link-description-source-status shape for every newly added `index.md`
catalog entry, structurally enforced for shape (ADR-017), agent-judged for content
quality.

**Independent Test**: An agent adds a new catalog entry; confirm it follows the
link-description-status format, independent of any other change in this feature.

### Tests for User Story 4

> **NOTE: Write these tests FIRST — they fail until T046 lands**

- [X] T044 [P] [US4] Deterministic tests
  `backend/tests/Grimoire.IntegrationTests/CatalogEntryFormatEnforcementTests.cs`
  (ADR-017): a well-formed new catalog line is allowed; a malformed new catalog line
  is denied (`catalog_entry_malformed`); an edit to an unrelated existing line (e.g. a
  section heading, or an already-conforming line left untouched) is not denied. Run
  now — MUST fail (RED).

### Implementation for User Story 4

- [X] T045 [P] [US4] `DeniedActionRecord.cs`: document `catalog_entry_malformed`
  (doc comment only).
- [X] T046 [US4] Extend `SharedFileWriteGuard.EvaluateWriteAsync` (ADR-017): add a
  format-validation step gated on `canonicalTarget == IndexPath`: compute the set of
  `- [`-led lines present in the proposed content but absent, byte-for-byte, from the
  current content; deny `catalog_entry_malformed` if any such line fails
  `^- \[.+\]\(.+\) — .+ — .+$`. Confirm T044 now passes (GREEN).
- [X] T047 [US4] Extend T037's `guardrails.format_validate` span to also cover
  `target=index` (same span, same attributes, new target value).
- [X] T048 [US4] Update `data/agents/ingest/system-prompt.md`'s "Catalog (index.md)
  Upkeep" section: link + short description + trailing source-status marker
  (count or stub indicator), written in the wiki's configured content language.
- [X] T049 [P] [US4] Update `data/agents/query/system-prompt.md`'s catalog-upkeep
  delegation reference to match.
- [X] T050 [P] [US4] New `Grimoire.EvalRunner` scorer (SC-007, in
  `backend/src/Grimoire.EvalRunner/Scoring/`): checks a sampled catalog description's
  specificity against the article's actual content, threshold ≥90%.
- [ ] T051 [US4] Re-record `Grimoire.EvalRunner` recordings affected by the new
  catalog format instruction. **Superseded/consolidated by T062** (Phase 7
  completeness audit) — do this as part of T062's single re-recording pass covering
  all 17 stale scenarios, not separately.
- [X] T052 [US4] Run `quickstart.md` Scenario 4; confirm the documented expected
  outcome.

**Checkpoint**: All four user stories are independently functional. FR-001–FR-013 are
implemented and tested.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories, plus the mandatory
completeness audit (Constitution Principle III).

- [X] T053 Observability completeness audit (MANDATORY — Constitution Principle
  III/IV): cross-referenced every row of `plan.md ## Observability` against its
  implementing task and passing test. `wiki.log.backstop_appended` (log event):
  covered (`WikiLogAppenderTests.cs`, `IngestObservabilityLogTests.cs`). `wiki_log.backstop_append`/
  `guardrails.format_validate` (trace spans): covered (`WikiLogAppenderTests.cs`,
  `LogEntryFormatEnforcementTests.cs`, `CatalogEntryFormatEnforcementTests.cs`).
  **Gap found and filed as T060**: `wiki.log.backstop_appended_total` (business
  metric) has no dedicated test asserting its recorded value, unlike this codebase's
  established `MeterListener`-based idiom for every other counter (e.g.
  `IngestObservabilityMetricsTests.cs`) — not a constitution-mandated derivation rule
  (only Log Events/Trace Spans have that MANDATORY three-category rule), but a real
  coverage gap against this repo's own convention.
- [X] T054 Logging contract CI enforcement (MANDATORY — Constitution Principle IV):
  confirmed — `LogEntryFormatEnforcementTests`/`CatalogEntryFormatEnforcementTests`/
  `WikiLogAppenderTests` all live in `Grimoire.IntegrationTests`, which
  `.github/workflows/ci.yml`'s `Run hermetic integration tests` step already runs on
  every PR; no new CI wiring needed.
- [X] T055 Trace contract CI enforcement (MANDATORY — Constitution Principle IV):
  confirmed — same reasoning as T054, same test project, same existing CI step.
- [X] T056 Agent-behavior evaluation completeness audit (MANDATORY — Constitution
  Principles II & V): audit performed — **two gaps found and filed as T061/T062, both
  now closed** in a follow-up live-eval session (a real `ANTHROPIC_AUTH_TOKEN` became
  available). `LogParagraphSpecificityScorer`/`CatalogDescriptionSpecificityScorer`
  (SC-005/SC-007) are wired into the capture pipeline and both replay at 100% (T061).
  All 17 originally-stale recordings were refreshed (T062); 16 of them plus both new
  scenarios (18/19 total) now replay at/above threshold. **One residual, disclosed
  gap remains**: `lint-inbound-links-refreshed` (013-lint-agent, unrelated to 014's own
  changes) replays at 70% against its 95% threshold — a real Lint-agent
  link-counting reliability issue under `claude-haiku-4-5`, confirmed reproducible
  across two independent captures, kept as honest recorded evidence per explicit
  direction rather than discarded or re-rolled (see T062). This is the one remaining
  DoD blocker for this feature, and it is out of 014's scope to fix.
- [X] T057 [P] Documentation sweep: grep `docs/` for any remaining reference to the
  retired `pages/`/`wiki/tasks`/`data/conversations` layout and update. Fixed:
  `docs/operations/runtime-configuration.md` (the "two-home layout" section — now
  four homes — and the historical migration note); `docs/adr/ADR-011-*.md`,
  `docs/adr/ADR-014-*.md` (pointer notes on the Conversation Record's moved path,
  decision bodies left otherwise unedited per this repo's ADR convention);
  `docs/adr/ADR-015-*.md`, `docs/adr/ADR-016-*.md` (pointer notes on their illustrative
  `policy.json` snippets, now superseded in shape by ADR-017); `CLAUDE.md`'s
  `<!-- SPECKIT START -->` pointer (011 → 014). `docs/adr/ADR-009-*.md` needed no
  change — it describes the composition-point mechanism, not specific default values.
- [X] T058 [P] Full regression run: `Grimoire.ArchTests` (46/46), `Grimoire.Domain.UnitTests`
  (29/29), `Grimoire.IntegrationTests` (431/431) — all green, in both Debug and Release
  configuration, including `dotnet format --verify-no-changes`. Two flaky (not
  feature-related) failures were observed intermittently across repeated runs —
  `IngestFailureAndReconciliationTests.FailurePath_LeavesWikiUntouched_AndMarksTaskFailed`
  (once, not reproducible in isolation) and
  `QueryConversationRecordLifecycleTests.ThreeTurnConversation_ProducesExactlyOneRecord...`
  (turn-ordering race, pre-existing from feature 011, reproduces intermittently under
  full-suite parallel execution, passes reliably in isolation) — neither touches this
  feature's code paths; both are pre-existing test flakiness, not new regressions.
- [X] T059 Ran the full `quickstart.md` end-to-end: Scenario 1 (`FlatContentRootLayoutTests`),
  Scenario 2 (`SiblingDirectoryLayoutTests`), Scenario 3 (`LogEntryFormatEnforcementTests`/
  `WikiLogAppenderTests`), Scenario 4 (`CatalogEntryFormatEnforcementTests`), the
  structural guarantee (`Grimoire.ArchTests` 46/46), and the observability check (log
  events/spans covered per T053) — all pass. The one piece of quickstart.md not
  exercisable in this session is a live end-to-end agent run (needs an API key),
  consistent with T061/T062's deferral.

- [X] T060 [P] Add a `MeterListener`-based test asserting `wiki.log.backstop_appended_total`
  increments with the correct `type` label when the backstop fires, mirroring
  `IngestObservabilityMetricsTests.cs`'s idiom (gap found by T053; new file
  `WikiLogAppenderMetricsTests.cs`, cross-agent/unprefixed per naming convention);
  extend coverage to also assert `wiki.write_conflict.rejections_total` increments
  with each of ADR-017's four new `reason` labels (`log_entry_not_appended`,
  `log_entry_malformed_heading`, `log_entry_missing_paragraph`,
  `catalog_entry_malformed`) — a second gap against the same repo convention, found
  by `/speckit-analyze` (finding G2; new file
  `QueryWriteConflictRejectionAdr017MetricsTests.cs`, Query-prefixed since it
  exercises `QueryToolCallInstrumentation` exclusively — kept separate from the
  backstop test per ADR-013's N1 naming rule, which the combined file initially
  violated). **Writing this test surfaced a real production bug**:
  `GuardedToolExecutor.cs`'s `RecordWriteConflictRejected` gate only listed three of
  the four ADR-017 reasons — `catalog_entry_malformed` was missing, so the counter
  never incremented for catalog denials, contradicting plan.md's "four new denial
  reasons" claim. Fixed alongside this task.
- [X] T063 [P] SC-004 searchability test: extended
  `backend/tests/Grimoire.IntegrationTests/LogEntryFormatEnforcementTests.cs` with
  `MultiEntryLog_EveryEntryLocatableByHeadingPattern` — two guard-approved appends
  plus a `WikiLogAppender`-generated backstop entry — asserting the heading regex
  `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` (Multiline) matches exactly the number of
  entries written, i.e. every entry is locatable and none lacks a heading (gap
  found by `/speckit-analyze`, finding G1 — plan.md's Test Strategy named this
  assertion for SC-004 but no task had implemented it).
- [X] T061 Wired `LogParagraphSpecificityScorer`/`CatalogDescriptionSpecificityScorer`
  into `Grimoire.EvalRunner`'s scenario/capture pipeline (gap found by T056): two new
  `ScenarioDefinition`s in `IngestScenarioDefinitions.cs`
  (`log-paragraph-specificity`, `catalog-description-specificity`, both on the
  `empty-topic` fixture so the run's own log entry/catalog line is unambiguously the
  last one written); `CapturePipeline`'s previously steering-adoption-only judge call
  generalized into `InvokeJudgeAsync`, dispatching by `ScorerId` to
  `InvokeLogParagraphJudgeAsync`/`InvokeCatalogDescriptionJudgeAsync` (extracts the
  last heading/paragraph or catalog line via the scorers' own `FindHeadingLineIndices`/
  `FindCatalogLineIndices`/`ExtractEntry`, then judges it against the run's actual
  touched-page content); a shared `DeterministicScorers.JudgeVerdictGate` case
  (`completed && judge_verdict_pass`, same shape as `SteeringAdoption`) for both new
  `ScorerId`s; two new `[Fact]`s in `IngestReplayEvalTests.cs`. Added
  `EvalWorkspace.LogContent()` (mirrors the existing `IndexContent()`). Smoke-tested
  live with `--samples 1` before the full T062 capture run: both judges produced
  content-specific PASS rationales (e.g. correctly naming the created page and its
  actual sections), confirming the wiring reads genuine run output, not a
  rubber-stamp.
- [X] T062 Re-recorded all 19 target `Grimoire.AgentEvals` scenarios (the 17 originally
  reported stale plus T061's 2 new ones) via
  `dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario <name>`
  for each, using a live `ANTHROPIC_AUTH_TOKEN` (`data/.env`, model `claude-haiku-4-5`).
  18 of 19 now replay at/above their threshold (`SC005_LogParagraphSpecificity` and
  `SC007_CatalogDescriptionSpecificity` both pass at 100%, confirming T061's judge
  wiring reads genuine run output — see its own note). **One scenario does not close
  clean**: `lint-inbound-links-refreshed` (013-lint-agent, SC-008 — ≥95% of sampled
  pages have an accurate inbound-link count) measured 80% then, on an independent
  re-capture, 70% — both well under its 95% threshold. Inspecting the failing
  samples' actual written output showed the Lint agent under `claude-haiku-4-5`
  genuinely miscounting one page's (`spoke-a`) inbound-link total in ~20-30% of
  runs — a real agent-behavior gap in feature 013's Lint agent, not a regression
  from 014's path/policy changes (which only touched `pathPrefix` values, not
  Lint's counting logic or instructions; the other three checks in the same
  scenario — `hub-page`/`spoke-b` accuracy — passed consistently across both
  draws). Per explicit direction, the honestly-captured 70% recording is kept
  as-is rather than discarded or re-rolled further:
  `Grimoire.AgentEvals.LintReplayEvalTests.SC008_InboundLinksRefreshed_ReplaysAtThreshold`
  now fails on real evidence, and `.github/workflows/eval.yml`'s replay-eval gate
  will fail on this one scenario until a **separate, out-of-scope-for-014** fix to
  the Lint agent's link-counting reliability (or its system-prompt) lands. This is
  the one open item blocking a fully-green DoD for this feature; every other
  DoD condition is met.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural)**: No dependencies — start immediately.
- **Phase 1 (Setup)**: No dependencies — can run alongside Phase 0.
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories.
- **User Stories (Phase 3–6)**: All depend on Phase 2. US1/US2 (P1) have no
  dependency on each other or on US3/US4. US3/US4 (P2) do not depend on US1/US2's
  path-layout changes (they operate on `log.md`/`index.md`, whose own path is
  unchanged) but do share `SharedFileWriteGuard`'s format-validation hook — US4 (T046)
  extends the same dispatch point US3 (T036) introduces, so US4's implementation task
  follows US3's, though both remain independently testable per their own Independent
  Test criteria.
- **Phase 7 (Polish)**: Depends on all four user stories being complete.

### Parallel Opportunities

- T002/T003 (Setup) in parallel.
- T007/T009 (Foundational tests) in parallel once T004–T006/T008 land.
- US1's T012–T017 (policy files, eval runner, fixtures, test files) are almost all
  `[P]` — different files, no cross-dependencies — once T010/T011 land.
- US2's T022–T026 similarly parallel once T021 lands.
- US3 and US4 can be staffed in parallel by different contributors once Phase 2 is
  done, with US4's T046 waiting on US3's T036 landing first (same extension point).
- Once Phase 2 is done, US1 and US2 can also be staffed in parallel.

---

## Parallel Example: User Story 1

```bash
# Once T010/T011 land, launch the independent cleanup tasks together:
Task: "Update data/agents/{ingest,query,lint}/policy.json pages/ -> . (T012)"
Task: "Update EvalWorkspace.cs and eval scoring for the flattened layout (T013, T014)"
Task: "Flatten lint-seeded-defects fixture (T015)"
Task: "Update PathConfiguration test files (T016)"
Task: "Update the remaining pages/-referencing integration test files (T017)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0: Structural boundary rule (RED baseline recorded).
2. Complete Phase 1: Setup.
3. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
4. Complete Phase 3: User Story 1 — confirms Phase 0's rule goes GREEN.
5. **STOP and VALIDATE**: Run `quickstart.md` Scenario 1 independently.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate independently (MVP: flat content root).
3. US2 → validate independently (tasks/conversations as siblings) — layout
   restructuring (FR-001–FR-006) now complete.
4. US3 → validate independently (unified log format, ADR-017 enforcement live).
5. US4 → validate independently (unified catalog format, extends US3's enforcement
   hook).
6. Polish → completeness audits, full regression, end-to-end quickstart.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- ADR-017's `SharedFileWriteGuard` extension point is introduced by US3 (T036) and
  extended by US4 (T046) — the one deliberate cross-story sequencing dependency in
  this feature; every other pair of stories is independently orderable.
- Phase 0's structural rule intentionally starts RED against real, pre-existing
  violations (not a synthetic scratch file) — Phase 3's T019 is where it turns GREEN.
- Verify tests fail before implementing (T028/T044 must fail before T036/T046 land).
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- Deviation from plan.md as originally drafted: `WikiLogAppender` is wired into
  Ingest (T034) and Query (T035) only. Lint is not wired — its policy grants no
  `log.md` write rule, so no backstop can ever apply. `WikiLogAppender` itself
  supports `type: "lint"` (covered by T029) for when Lint's write scope grows.
  plan.md's Project Structure/Observability sections have been amended to match
  (`/speckit-analyze` finding I2).
- ADR-017's Red/Green probe (T028/T044 → T036/T046) lives in
  `Grimoire.IntegrationTests`, not `Grimoire.ArchTests` — plan.md's Project
  Structure/Test Strategy have been corrected to say so (`/speckit-analyze`
  finding I1). `Grimoire.ArchTests` only carries Phase 0's separate
  `PagesWrapperRetirementBoundaryRuleTests` (ADR-009).

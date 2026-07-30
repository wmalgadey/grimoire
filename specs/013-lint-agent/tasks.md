# Tasks: Lint Agent — Wiki Health Check

**Input**: Design documents from `/specs/013-lint-agent/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/findings-report-format.md, quickstart.md, ADR-016 (**accepted**, extends
ADR-015)

**Tests**: Required — the constitution mandates hermetic harness tests for
deterministic guarantees (SC-001–SC-004) and Red/Green-probed structural tests for
architectural boundaries (Phase 0), plus evaluation-with-threshold tests for the
agent-judgment success criteria (SC-005–SC-008). No test in this feature requires a
live LLM call except the one-time `Grimoire.EvalRunner` capture step.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P3) to
enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are exact, relative to repository root

## Path Conventions

Existing web-app split: `backend/src/`, `backend/tests/`, `frontend/src/`. This
feature adds one new agent executable (`Grimoire.LintAgent`), two new Hub namespaces
(`Grimoire.Hub.LintDispatch`, `Grimoire.Hub.LintFindings`), a new frontend route
(`/lint`), and the runtime location `data/findings/` (git-ignored). Extends (does not
replace) `Grimoire.Domain.Guardrails`/`Grimoire.AgentRuntime.Guardrails.Coordination`
from feature 012.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove ADR-016's write-boundary rule is live *before* any feature code
exists.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [X] T001 Add `backend/tests/Grimoire.ArchTests/LintAgentGuardedWriteBoundaryRuleTests.cs`:
  Mono.Cecil IL scan (same allow-listed-namespace idiom as
  `IngestAgentGuardedWriteBoundaryRuleTests`/`QueryAgentGuardedWriteBoundaryRuleTests`)
  asserting reachable filesystem-write API calls anywhere in the (not-yet-existing)
  `Grimoire.LintAgent` assembly are permitted only from
  `Grimoire.AgentRuntime.Guardrails` — every other type must show zero reachable write
  calls. Passes vacuously until T0xx creates the assembly.
  *Deviation: `Grimoire.LintAgent` exposes no public type (top-level-statement
  `Program.cs` generates only an internal `Program` class), so the assembly is loaded
  by simple name (`Assembly.Load("Grimoire.LintAgent")`) instead of
  `typeof(...).Assembly` — the ArchTests project's own `ProjectReference` to
  `Grimoire.LintAgent.csproj` puts it on the probing path regardless.*
- [X] T002 Red/Green probe for T001: once `Grimoire.LintAgent` exists (after T0xx
  below creates the project skeleton — this probe task runs after that skeleton
  exists but BEFORE any real Lint behavior is implemented), add a scratch class
  calling `File.WriteAllText` directly, run `dotnet test backend/tests/Grimoire.ArchTests`
  — the rule MUST fail naming the scratch type; delete it, run again — MUST pass.
  Commit message documents the probe result (exact violation name, pass count
  before/after).

**Definition of Done**:

- [X] Rule (T001) written and committed
- [X] Red/Green probe (T002) completed with commit message documenting the result
- [X] `Grimoire.ArchTests` passes in CI with no active violations (probe code removed)

**Checkpoint**: The write-boundary rule is live and proven to detect a real
violation before any feature behavior exists.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T003 Add `data/findings/` to `.gitignore` (ADR-003/ADR-009 pattern, next to
  `data/write-locks/`).
- [X] T004 Create the `Grimoire.LintAgent` project skeleton
  (`backend/src/Grimoire.LintAgent/Grimoire.LintAgent.csproj`, empty `Program.cs`
  entry point, added to `backend/Grimoire.slnx`) — enough for T002's probe to target
  a real assembly, before any real Lint behavior exists.

**Checkpoint**: Runtime location ignored; a real (empty) Lint assembly exists for
Phase 0's probe. Foundational work can begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `WriteMode` three-way mode (ADR-016) and the `FindingsDir` path
composition point that every user story depends on. Additive only — Ingest's and
Query's existing behavior, policy files, and tests are unaffected.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Extend `backend/src/Grimoire.Domain/Guardrails/PolicyDecision.cs` and
  `SafetyPolicy.cs`: introduce `WriteMode { ReadWrite, CreateOnly, FrontmatterOnly }`;
  `WriteRule` gains a `Mode` property (default `ReadWrite`) **alongside** its existing
  `CreateOnly` bool (keep `CreateOnly` as a computed convenience —
  `Mode == WriteMode.CreateOnly` — so every existing call site and test in
  `backend/tests/Grimoire.Domain.UnitTests/SafetyPolicyModeTests.cs`,
  `Grimoire.IntegrationTests/SharedFileWriteGuardTests.cs`,
  `QuerySynthesisWriteTests.cs`, `PolicyLoaderModeTests.cs` keeps compiling and
  passing unchanged); `PolicyDecision.Allow` gains a `WriteMode mode = WriteMode.ReadWrite`
  parameter, `IsCreateOnly` becomes the same kind of computed convenience. `Evaluate`
  returns the matched rule's `Mode`.
  *Deviation: `WriteRule`/`PolicyDecision` are no longer declared with a single
  primary-constructor parameter list — each gained a second, explicit `bool`-shaped
  constructor/overload (`WriteRule(string, bool)`, `PolicyDecision.Allow(bool)`)
  delegating to the new `WriteMode`-shaped one, so every named-argument call site using
  `CreateOnly:`/`isCreateOnly:` keeps binding to a real `bool` parameter and compiles
  byte-for-byte unchanged.*
- [X] T006 [P] Unit tests `backend/tests/Grimoire.Domain.UnitTests/WriteModeTests.cs`:
  a `frontmatter-only` rule surfaces `Mode == WriteMode.FrontmatterOnly` on allow and
  `IsCreateOnly == false`; existing `read-write`/`create-only` behavior (including the
  `IsCreateOnly` convenience) is unchanged — run the full existing
  `SafetyPolicyModeTests` suite unmodified to confirm zero regressions.
- [X] T007 Extend `backend/src/Grimoire.AgentRuntime/Instructions/PolicyLoader.cs`:
  recognize `"frontmatter-only"` as a third `mode` value; any other value remains a
  fail-closed load error, unchanged.
- [X] T008 [P] Integration tests
  `backend/tests/Grimoire.IntegrationTests/PolicyLoaderFrontmatterOnlyModeTests.cs`:
  loading a policy with `"mode": "frontmatter-only"` produces a policy whose
  write-scope `Evaluate` returns `Mode == FrontmatterOnly`; existing
  `PolicyLoaderModeTests` suite still passes unmodified.
- [X] T009 Extend `backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs`:
  `EvaluateWriteAsync` gains the proposed `content` (already available at the
  `GuardedToolExecutor` call site) and the resolved `WriteMode` (replacing its
  `isCreateOnly` bool param — internally still branches to the existing create-only
  logic for `WriteMode.CreateOnly`, unchanged). For `WriteMode.FrontmatterOnly`: deny
  `frontmatter_only_target_missing` if the canonical target does not exist; otherwise
  run the existing compare-and-swap check unchanged (`write_conflict_stale_read` on a
  stale read); then split both the current on-disk content and the proposed content
  at their closing `---` frontmatter delimiter (private helper, e.g.
  `TrySplitFrontmatter(string content, out string body)`, pure string operation, no
  I/O) — deny `frontmatter_only_malformed_document` if either isn't well-formed
  two-delimiter frontmatter, deny `frontmatter_only_body_changed` if the two bodies
  are not byte-identical, otherwise allow.
  *Deviation: `EvaluateWriteAsync` keeps its pre-ADR-016 `(canonicalPath, bool
  isCreateOnly, cancellationToken)` overload (delegating to the new
  `(canonicalPath, WriteMode, proposedContent, cancellationToken)` overload with an
  unused empty content) — same "keep the boolean shape as a compiling, passing
  convenience" approach as T005, so `SharedFileWriteGuardTests.cs` keeps calling
  `EvaluateWriteAsync(target, isCreateOnly: ..., ...)` unmodified. The current-content
  read for the compare-and-swap check reuses the exact pre-existing
  `File.ReadAllBytesAsync` + hash comparison, decoded to a string via `Encoding.UTF8`
  only for the frontmatter/body split — the CAS check's byte-level behavior is
  unchanged.*
- [X] T010 [P] Unit/integration tests
  `backend/tests/Grimoire.IntegrationTests/SharedFileWriteGuardFrontmatterOnlyTests.cs`:
  frontmatter-only write to a non-existent target denies
  `frontmatter_only_target_missing`; to an existing target with an identical body but
  changed frontmatter allows; to an existing target with a changed body denies
  `frontmatter_only_body_changed`; against a target (or proposed content) lacking a
  well-formed frontmatter block denies `frontmatter_only_malformed_document`; a stale
  read (content changed by another writer since last read) denies
  `write_conflict_stale_read` even when the body would otherwise have matched; the
  full existing `SharedFileWriteGuardTests` suite still passes unmodified.
- [X] T011 Update `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`:
  `ExecuteWriteFileAsync` passes the resolved `WriteMode` and the call's `content`
  into `EvaluateWriteAsync` (replacing the `isCreateOnly` bool argument); its
  post-write create-only bookkeeping is unchanged (still keyed off
  `Mode == WriteMode.CreateOnly` via the existing convenience). Extend
  `DeniedActionRecord`'s doc comment with the three new reason strings (no shape
  change).
  *Deviation: `wiki.write_conflict.rejected`/`wiki.write_conflict.rejections_total`'s
  reason-label extension (plan.md `## Observability` note: reuse the existing signal
  for the three new frontmatter-only reasons too) is deliberately left untouched here
  — `RecordDenial` already records every new reason correctly in `DeniedActionRecord`/
  `Denials` regardless (T012 confirms this end-to-end), so nothing is broken; the
  metric-label extension itself is observability wiring that belongs with Phase 3's
  user-story-scoped observability tasks (T027+), not this Phase 2 foundational task.*
- [X] T012 [P] Integration tests (extend
  `backend/tests/Grimoire.IntegrationTests/GuardedToolExecutorCoordinationTests.cs`):
  end-to-end through `GuardedToolExecutor` with a `frontmatter-only` policy rule —
  each of T010's cases reproduced through the full executor (not the guard in
  isolation), confirming `DeniedActionRecord`/`Denials`/`TouchedPaths` behave
  correctly for the new reasons.
- [ ] T013 Extend `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs`
  (add `FindingsDir` + `DefaultFindingsDirName = "findings"`), `GrimoirePathResolver.cs`
  (resolve beneath `DataDir`, report as `findings_dir`, auto-create as
  `PathLocationKind.WritableData`), `ResolvedGrimoirePaths.cs` (add
  `FindingsReportPathFor(runId) => Path.Combine(FindingsDir, $"{runId}.md")`) — ADR-009
  single composition point.
- [ ] T014 [P] Integration tests
  `backend/tests/Grimoire.IntegrationTests/PathConfiguration/FindingsPathTests.cs`:
  `findings_dir` resolves beneath `data/`, honors explicit override + env var with
  correct source reporting, auto-created — mirrors existing path-config test idiom.

**Checkpoint**: The frontmatter-only mode and findings path composition exist and are
hermetically verified in isolation. No agent's observed capability has changed —
Lint doesn't exist as a runnable agent yet.

---

## Phase 3: User Story 1 - Run lint, read the findings (Priority: P1) 🎯 MVP

**Goal**: The user can trigger a Lint Run from the Web UI; it reads the whole wiki
under its own fail-closed-loaded system prompt, and produces a persistent Findings
Report grouped by category, each finding naming affected pages, describing the
problem, and proposing a remediation (FR-001, FR-002, FR-005, FR-006, SC-001).

**Independent Test**: Seed a wiki fixture with known defects; trigger a lint run via
the API; verify a Findings Report is produced with the seeded defects appearing as
findings in their categories.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T015 [P] [US1] Integration test
  `backend/tests/Grimoire.IntegrationTests/LintRunLifecycleTests.cs` (SC-001):
  triggering a run with `FakeAgentProcess` scripted to produce a narrative → exactly
  one Findings Report file at `<base>/data/findings/<runId>.md`, frontmatter with
  `record_format: grimoire-findings/1`, `outcome_state: completed`, instruction
  identity + sha256 recorded; a missing/unreadable/empty system-prompt fixture fails
  the run before any agent output with a human-readable reason and instruction
  identity omitted, mirroring `IngestInstructionLoadTests`/`QueryInstructionLoadTests`.
- [ ] T016 [P] [US1] Integration test (in `LintRunLifecycleTests.cs`, T015's file):
  an empty/healthy wiki fixture produces a report whose three categories each state
  "No <category> findings." explicitly — never omitted, never fabricated (FR-006
  acceptance scenario 4).
- [ ] T017 [P] [US1] `Grimoire.EvalRunner` scenario
  `backend/src/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs`: add
  `lint-defects-found` (SC-005 threshold ≥ 85%, per-category) with a seeded wiki
  fixture containing one instance of each defect category (contradiction, orphan,
  missing cross-reference, missing tags, missing confidence, stale low-confidence
  page); deterministic sub-scorer in
  `backend/src/Grimoire.EvalRunner/Scoring/LintDeterministicScorers.cs` checking each
  seeded defect's affected page(s) appear in the report under the expected category
  (lightweight text/wikilink matching over the raw report body — no structured
  parser needed, per contracts/findings-report-format.md's Parsing section).
- [ ] T018 [P] [US1] `Grimoire.EvalRunner` scenario: add `lint-genuine-findings`
  (SC-006 threshold ≥ 90%) reusing T017's fixture; scorer cross-checks a sample of
  reported findings against the actual pages named, confirming the described problem
  genuinely exists (not fabricated).

### Implementation for User Story 1

- [ ] T019 [US1] Implement `Grimoire.LintAgent`'s composition root
  (`backend/src/Grimoire.LintAgent/Program.cs`, `LintCliOptions.cs`,
  `LintToolRegistry.cs`): follows the exact `Grimoire.QueryAgent/Program.cs` shape
  (`AgentProfile` with `RequiredInstructionDocuments = { SystemPrompt }`, no
  default-user-prompt; `--wiki-root`/`--system-prompt-path`/`--policy-path`/
  `--write-locks-dir`/`--heartbeat-seconds` CLI args; `GuardedToolExecutor` construction
  with `writeLocksDir`; `RunEventEmitter` over stdout). `LintToolRegistry` offers all
  three tools (`list_files`, `read_file`, `write_file`) unlike Query's two.
- [ ] T020 [US1] Create `data/agents/lint/policy.json` (data-model.md's exact
  frontmatter-only shape) and `data/agents/lint/system-prompt.md`: instructs the
  agent to read the whole wiki (`list_files` on `pages/` and its topic folders,
  `read_file` each page, plus `index.md`/`log.md`), judge health across all three
  Finding Categories, and produce one final narrative structured as the Findings
  Report body (frontmatter delimiter convention, categories, findings, proposed
  remediations) — reusing `agents/ingest/system-prompt.md`'s tag taxonomy and
  confidence-scoring conventions by reference, introducing `inbound_links`/
  `last_reviewed` as new optional frontmatter fields Lint alone maintains
  (research.md R6).
- [ ] T021 [US1] Implement `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs`:
  copies `QueryRunCoordinator`'s immediate-rejection `SemaphoreSlim(1,1)` shape
  (research.md R3) — `TryStartAsync` returns a busy result on
  `WaitAsync(0, cancellationToken)` failure, no queue; reuses the liveness-supervision
  loop (heartbeat, silence-window failure) unchanged from the existing coordinators.
- [ ] T022 [US1] Implement `backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs`
  (writer only, per contracts/findings-report-format.md: frontmatter + single
  `<!-- grimoire:findings ... -->` bookkeeping block with sentinel-neutralized string
  escaping mirroring `ConversationRecordFormat`'s existing escaping rule + the
  narrative body verbatim) and `FindingsReportStore.cs` (Hub-written, one file per
  run, `WriteAsync(runId, narrative, bookkeeping)` called from
  `LintRunCoordinator`'s terminal-event handling — mirrors
  `QueryRunCoordinator.FinishTurnAsync` → `ConversationRecordStore.AppendTurnAsync`).
- [ ] T023 [P] [US1] Integration tests
  `backend/tests/Grimoire.IntegrationTests/FindingsReportFormatTests.cs`: writer
  round-trip produces the documented layout; injection fixtures (narrative containing
  `-->`, `##` headings, quotes) cannot break or forge the bookkeeping block's
  structure; a `partial: true` run's report is clearly headed accordingly.
- [ ] T024 [US1] Implement `backend/src/Grimoire.Hub/LintDispatch/LintSubmissionEndpoints.cs`:
  `POST /api/lint-runs` (bare trigger, no body) → `Results.Accepted`/`Results.Conflict`
  (busy) via `LintRunCoordinator`; `GET /api/lint-runs/{runId}`/`GET /api/findings/{runId}`
  (or equivalent) to fetch a report for display — exact route shape decided here,
  mirroring `IngestSubmissionEndpoints`'s Minimal-API route-group pattern.
- [ ] T025 [US1] Frontend: `frontend/src/routes/lint/+page.svelte` (a "Run Lint"
  button, current-run status, Findings Report viewer rendering the report's markdown
  similar to `QueryConversation.svelte`'s `renderAnswer` pattern) and
  `frontend/src/lib/services/lintApi.ts` (typed fetch client, mirrors
  `ingestSubmissionsApi.ts`'s pattern).
- [ ] T026 [P] [US1] Frontend tests: `frontend/src/routes/lint/page.svelte.test.ts`
  and `frontend/src/lib/services/lintApi.test.ts` — trigger button posts, busy
  response shows a clear message, completed report renders formatted findings.

### Observability for User Story 1 (co-located, plan.md ## Observability)

- [ ] T027 [US1] Implement `lint.run.triggered`/`lint.run.rejected`/
  `lint.instructions.loaded`/`lint.instructions.load_failed`/`lint.run.completed`/
  `lint.run.failed`/`lint.findings_report.created` log events
  (`backend/src/Grimoire.Hub/LintDispatch/LintLogEvents.cs`, mirroring
  `QueryLifecycleLogEvents`' idiom) and the `wiki.lint.runs_total{outcome}` metric,
  emitted at their triggers in `LintRunCoordinator`/`FindingsReportStore`.
- [ ] T028 [P] [US1] Deterministic integration tests
  `backend/tests/Grimoire.IntegrationTests/LintLogEventTests.cs` and
  `LintMetricsTests.cs`: validate event name/level/mandatory fields and metric
  increments for all rows above.
- [ ] T029 [US1] Add trace spans `hub.lint.trigger` (root)/`hub.lint.run_supervision`
  (child)/`hub.lint.write_findings_report` (child) and agent-side
  `lint_agent.run`/`lint_agent.load_instructions`/`lint_agent.tool_call` — existing
  OTel bootstrap pattern (ADR-005), same shape as Query's/Ingest's agent-side spans.
- [ ] T030 [P] [US1] Deterministic integration tests
  `backend/tests/Grimoire.IntegrationTests/LintTraceTests.cs`: validate span
  names, parent/child linkage, and `run_id` correlation across events/metrics/spans
  of the same run.

**Checkpoint**: User Story 1 is fully functional and independently testable — Lint
runs, reads the wiki, and produces a readable Findings Report.

---

## Phase 4: User Story 2 - Metadata gets refreshed and proposed (Priority: P2)

**Goal**: After a run, every page's inbound-link count matches reality; the report
proposes taxonomy-conforming tags and convention-conforming confidence scores for
pages lacking them; stale low-confidence pages are flagged as review candidates
(FR-007–FR-009, SC-007, SC-008).

**Independent Test**: Seed pages with stale inbound-link counts, missing tags,
missing confidence, and an old low-confidence page; run lint; verify counts are
corrected in frontmatter (the only page modification), proposals conform to
convention, and the stale page is flagged.

### Tests for User Story 2

- [ ] T031 [P] [US2] Integration test
  `backend/tests/Grimoire.IntegrationTests/LintInboundLinkRefreshTests.cs`
  (SC-004/SC-008 groundwork): a scripted `frontmatter-only` `write_file` call
  updating `inbound_links` succeeds through the real `GuardedToolExecutor`/policy/
  guard stack against a temp wiki root loaded with `data/agents/lint/policy.json`
  (T020); the page's body is byte-identical before and after; these are the **only**
  page modifications the run performs — no other `write_file` call is scripted or
  attempted by the fixture.
- [ ] T032 [P] [US2] `Grimoire.EvalRunner` scenario: add `lint-metadata-proposals`
  (SC-007 threshold ≥ 90% tag-taxonomy conformance, ≥ 90% confidence-convention
  conformance) reusing T017's fixture's missing-tags/missing-confidence pages;
  deterministic sub-scorer in `LintDeterministicScorers.cs` parsing proposed
  tags/confidence text against `agents/ingest/system-prompt.md`'s taxonomy/formula.
- [ ] T033 [P] [US2] `Grimoire.EvalRunner` scenario: add `lint-inbound-links-refreshed`
  (SC-008 threshold ≥ 95%) with a wiki fixture with a known cross-link graph and
  deliberately stale recorded counts; deterministic scorer recomputes the true
  inbound-link graph and compares to post-run frontmatter.
- [ ] T034 [P] [US2] Integration test (in `LintRunLifecycleTests.cs`, T015's file):
  a low-confidence page whose `last_reviewed` is older than the Review Window
  (default 90 days, configurable) is listed as a review candidate in the
  Metadata Hygiene section; a low-confidence page within the window is not.

### Implementation for User Story 2

- [ ] T035 [US2] Close any gaps T031–T034 surface in `data/agents/lint/system-prompt.md`
  (proposal wording, review-window application) or `FindingsReportFormat.cs`
  (review-candidate rendering). Expected to be small — the mechanism (T009/T020) was
  built with these guarantees; this task exists so the story has an explicit
  implementation home if the tests find drift.
- [ ] T036 [US2] Make the Review Window configurable (default 90 days) via the same
  configuration surface as `QueryConcurrencyLimit` (e.g.
  `Grimoire:LintReviewWindowDays`), threaded into `data/agents/lint/system-prompt.md`'s
  effective instructions or a small harness-supplied parameter — exact mechanism
  decided here (instruction-file templating vs. a documented default the agent is
  simply told).

### Observability for User Story 2

- [ ] T037 [US2] Add `wiki.lint.findings_total{category}` and
  `wiki.lint.inbound_links_refreshed_total` metrics, emitted from
  `FindingsReportStore`/`LintRunCoordinator` at the terminal event (findings counted
  from the report's per-category sections; refreshed count from the run's
  frontmatter-only `TouchedPaths`).
- [ ] T038 [P] [US2] Deterministic integration tests extending `LintMetricsTests.cs`
  (T028's file): validate both new metrics' increments and category labels.

**Checkpoint**: User Stories 1 AND 2 hold independently — findings are accurate,
metadata proposals conform to convention, inbound-link counts are trustworthy again.

---

## Phase 5: User Story 3 - Lint can only do lint things (Priority: P3)

**Goal**: Lint's capabilities are exactly reading the wiki, refreshing link-count
frontmatter, and writing its report — nothing else; every out-of-scope attempt is
denied and recorded; injected wiki content cannot widen its capabilities; a hung or
dead run is detected and its partial report clearly marked (FR-012–FR-014, SC-002,
SC-003).

**Independent Test**: Drive a lint run toward out-of-scope writes (body edit, page
creation/deletion, write outside the wiki); verify denial at the tool boundary with
recorded reasons; verify the structural rule (Red/Green probe); verify concurrent
triggers are rejected and dead runs are detected.

### Tests for User Story 3

- [ ] T039 [P] [US3] Integration test
  `backend/tests/Grimoire.IntegrationTests/LintWriteScopeDenialTests.cs` (SC-002):
  scripted body-changing `write_file` on an existing page → denied
  `frontmatter_only_body_changed`, page unchanged; scripted `write_file` to a
  non-existent path under `pages/` → denied `frontmatter_only_target_missing`
  (no page creation); scripted write to `index.md`/`log.md`/outside the wiki → denied
  `out_of_scope`/`traversal` (no write rule exists for index/log at all — T020's
  policy). The run continues to completion and produces a report in every case.
- [ ] T040 [P] [US3] Prompt-injection resistance test (in
  `LintWriteScopeDenialTests.cs`, T039's file): a wiki page's content contains
  instruction-like text attempting to grant broader write access; reading it changes
  nothing about policy evaluation; an out-of-scope write attempted afterward is
  still denied identically to T039 (FR-013).
- [ ] T041 [P] [US3] Integration test
  `backend/tests/Grimoire.IntegrationTests/LintConcurrencyAndLivenessTests.cs`
  (SC-003): a trigger while a run is active is rejected immediately (409/429, no
  queue) with a clear message; a `FakeAgentProcess` scripted to go silent is marked
  failed once the liveness window elapses (fake `TimeProvider`), leftover process
  terminated, and any findings produced before the hang are persisted with
  `partial: true`.

### Implementation for User Story 3

- [ ] T042 [US3] Close any scope-enforcement/concurrency gaps T039–T041 surface.
  Expected to be small — the frontmatter-only check (T009/T011) and the
  immediate-rejection coordinator (T021) already structurally guarantee this story;
  this task exists so the story has an explicit implementation home if the tests
  find drift.

**Checkpoint**: All user stories are independently functional — Lint is accurate,
scoped, and safe under adversarial content and concurrent/failed-run conditions.

---

## Phase 6: Polish, Cross-Cutting Verification & Completeness Audit

**Purpose**: The mandatory completeness audit, CI-enforcement confirmation, eval
capture + threshold verification, and final validation.

- [ ] T043 **Completeness audit** (MANDATORY, named — Constitution Principle III/IV):
  cross-reference every row of `plan.md ## Observability` and every SC-001..SC-008
  against its implementing task and passing test. File any gap found as a new task
  before declaring the DoD met.
- [ ] T044 Logging-contract CI enforcement: confirm the new logging tests (T028,
  T038) run unfiltered in `.github/workflows/ci.yml`'s standard integration-tests
  step.
- [ ] T045 Trace-contract CI enforcement: same confirmation for the trace tests (T030)
  and the Phase 0 structural rule (T001) under "Run architecture tests".
- [ ] T046 Capture live eval recordings (one-time, non-hermetic, requires an API
  credential — available in this environment per feature 012's Phase 6 precedent):
  run `Grimoire.EvalRunner capture` for `lint-defects-found`, `lint-genuine-findings`,
  `lint-metadata-proposals`, `lint-inbound-links-refreshed` at the project's standard
  sample count (10, per existing recordings' convention); commit the recordings.
- [ ] T047 Verify thresholds: replay against the captured recordings, confirm SC-005
  (≥85% per category), SC-006 (≥90%), SC-007 (≥90%/≥90%), SC-008 (≥95%) are met. If a
  threshold is missed, fix `data/agents/lint/system-prompt.md` (Principle V — never a
  backend heuristic) and re-verify; re-capture only if the scenario itself changes.
- [ ] T048 Full-suite verification: `dotnet test` for `Grimoire.ArchTests`,
  `Grimoire.Domain.UnitTests`, `Grimoire.IntegrationTests`, `Grimoire.AgentEvals` (all
  green, zero skips) and `dotnet format --verify-no-changes` on `backend/`, plus
  frontend gates (`check`, `lint`, `test`, `build`).
- [ ] T049 Live quickstart: run `specs/013-lint-agent/quickstart.md`'s scenarios 1–4
  against a live local Hub with a real seeded-defect wiki fixture; record the
  outcome.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural)**: No dependencies — MUST be first; blocks everything.
- **Phase 1 (Setup)**: After Phase 0. T004 (Lint assembly skeleton) is itself a
  prerequisite for T002's probe — Phase 0 and Phase 1's T004 are interleaved in
  practice (write T001 → create T004's skeleton → run T002's probe against it).
- **Phase 2 (Foundational)**: After Phase 1 — BLOCKS all user stories. Within:
  T005 → T006; T005+T007 → T008; T005+T009 → T010; T009+T011 → T012; T013 → T014.
- **Phase 3 (US1)**: After Phase 2. Tests T015–T018 first (failing); T019 → T020 →
  T021 → T022 → T024 (composition root, then instructions, then dispatch, then
  report store, then endpoints — each assumes the prior is in place); T025 after T024
  (API shape fixed); observability T027–T030 after T021/T022 (trigger points exist).
- **Phase 4 (US2)**: After Phase 3 (report/dispatch path exists). T031–T034 first;
  T035 → T036; T037 → T038.
- **Phase 5 (US3)**: After Phase 3; independent of Phase 4. T039–T041 first; T042
  closes gaps.
- **Phase 6 (Polish)**: After Phases 3–5. T043 gates the DoD; T046 → T047;
  T044/T045/T048/T049 are final gates.

### User Story Dependencies

- **US1 (P1)**: Only on Foundational. Delivers the MVP: Lint runs, reads the wiki,
  produces a readable Findings Report.
- **US2 (P2)**: Builds on US1's write path and report format; independently testable
  via its own metadata-quality fixtures.
- **US3 (P3)**: Builds on US1's write path and dispatch; independent of US2 (can run
  in parallel with Phase 4).

### Parallel Opportunities

- Phase 2: T006 ∥ T008 ∥ T010 ∥ T012 ∥ T014 (after their respective implementation
  tasks).
- Phase 3: T015–T018 all parallel (different files/scenarios); T023/T026 parallel
  after their impl tasks; T028/T030 parallel after T027/T029.
- Phase 4 ∥ Phase 5: US2 and US3 touch disjoint test files and independent
  implementation seams (both depend only on Phase 3, not on each other).
- Phase 6: T044 ∥ T045.

---

## Parallel Example: User Story 1

```bash
# After Phase 2, launch all US1 test tasks together (they must fail first):
Task: "T015 LintRunLifecycleTests.cs — SC-001 one report, instruction identity, fail-closed load"
Task: "T016 LintRunLifecycleTests.cs — healthy-wiki honest empty-findings report"
Task: "T017 EvalRunner scenario lint-defects-found — SC-005"
Task: "T018 EvalRunner scenario lint-genuine-findings — SC-006"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 0 (rule + probe, interleaved with T004's assembly skeleton) → Phase 1 →
   Phase 2 (frontmatter-only mode + findings path proven in isolation).
2. Phase 3 completely — this is the feature: Lint runs, reads the wiki, produces a
   readable Findings Report.
3. **STOP and VALIDATE**: quickstart Scenario 1; SC-001/SC-005/SC-006 hold.

### Incremental Delivery

1. Add US2 → metadata accuracy and proposal quality verified (SC-007, SC-008) →
   validate quickstart Scenario 2.
2. Add US3 → scope/injection/concurrency/liveness guarantees verified explicitly
   (SC-002, SC-003) → validate quickstart Scenarios 3–4.
3. Phase 6 → completeness audit, CI-enforcement confirmation, eval capture +
   threshold verification, full gates → DoD.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- Verify story tests fail before implementing (Red → Green → Refactor).
- All tests hermetic except T046 (one-time eval capture) and T049 (manual
  quickstart run); no other test requires an API key or network.
- `data/agents/ingest/policy.json` and `data/agents/query/policy.json` need no edit —
  `frontmatter-only` is a purely additive third mode value (T005/T007 verified
  explicitly by T006/T008 against the existing suites, unmodified).
- The frontmatter-only check and the compare-and-swap check compose (both must pass);
  they are not alternative/exclusive checks.

# Tasks: The Guarded Tool and Policy Surface Lint Needs

**Input**: Design documents from `/specs/026-guarded-tool-surface/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required. Deterministic harness contracts (SC-001..SC-010, SC-005a/b) are hermetic
integration tests; SC-011 and SC-013 are recorded-replay evaluations.

**Logging Contract (MANDATORY)**: each of the 6 rows in `plan.md ## Observability > Structured
Log Events` has an implementation task, a deterministic test task, and CI enforcement (T063).

**Trace Contract (MANDATORY)**: each of the 3 rows in `plan.md ## Observability > Distributed
Trace Spans` has an implementation task, a deterministic test task, and CI enforcement (T064).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1..US4 from spec.md
- Every task cites at least one `FR-###` / `SC-###`, or names the phase goal explicitly

## Path Conventions

Web app layout: `backend/src/`, `backend/tests/`. `frontend/` is untouched by this feature.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Guard the three Dependency & Layering Boundary Rules named in
`plan.md § Architectural Constraints & ADRs` before any feature code exists. Every other rule
ADR-030/ADR-031 enumerates is tagged a **Feature-Scoped Invariant** and is covered by a
classicist behavioural test in its own phase — never here.

**⚠️ NON-NEGOTIABLE**: no feature implementation begins until Phase 0 is complete, each rule
Red/Green probed and the probe result recorded in the commit message.

- [X] T001 [P] Boundary Rule — no wiki deletion outside the guarded tool layer (ADR-031 R3, FR-022): NetArchTest rule in `backend/tests/Grimoire.ArchTests/` asserting `File.Delete`/`Directory.Delete` are reachable only from `Grimoire.AgentRuntime.Guardrails` and the harness-record namespaces. Red/Green probe: add a class calling `File.Delete` from `Grimoire.LintAgent`, confirm red, delete it, confirm green
- [X] T002 [P] Boundary Rule — search regex is always non-backtracking and bounded (ADR-030 R2, FR-007a): NetArchTest/IL rule in `backend/tests/Grimoire.ArchTests/` asserting every `Regex` construction reachable from the search implementation passes `RegexOptions.NonBacktracking` and a match timeout. Red/Green probe: construct a plain `Regex` in the search path, confirm red, revert, confirm green
- [X] T003 [P] Boundary Rule — guarded-only filesystem reach and no run-mode branch, in `backend/tests/Grimoire.ArchTests/` (ADR-030 R1/R4, ADR-031 R1, FR-014, FR-022): NetArchTest rule asserting search/batch/delete filesystem APIs are reachable only from `GuardedToolExecutor`, and that no type in `Grimoire.Hub.LintDispatch` or `Grimoire.Hub.RemediationTasks` selects a policy path by run mode. Red/Green probe: add a mode-conditional policy path in `RemediationRunCoordinator`, confirm red, revert, confirm green

**Checkpoint**: boundaries guarded. Feature code may begin.

---

## Phase 1: Setup

**Purpose**: measurements and fixtures that must exist before the code they measure.

- [X] T004 Capture the pre-feature baseline for the SC-014 measurement (research.md D9): record median content tokens read per Lint survey run on the eval fixture, into `specs/026-guarded-tool-surface/baseline.md`. **Must run before any Phase 2 task** — measured afterwards it is a reconstruction, not a baseline. **Done, and still a genuine baseline**: measured at commit `301242b` from the checked-in pre-feature recordings, *before* T009/T012/T065 landed. At that commit the Lint agent is behaviourally pre-feature (v1 policy, three tools, unranged `read_file` schema), so no ranged or frontmatter read was reachable — the all-`full` shape counts are a property of the build, not of sampling. Deriving from recordings rather than a fresh live run also makes both halves of the comparison like-for-like and reproducible without credentials. Headline: median **3012.5** content tokens read per survey run, 849/849 reads whole-page
- [X] T005 [P] Build the `lint-at-scale` fixture generator in `backend/tests/Grimoire.AgentEvals/Fixtures/` (SC-011): extends `lint-seeded-defects` with filler pages generated at build time. No corpus is authored or committed (plan.md § Eval scope). **Done**: `LintAtScaleFixture` (`Grimoire.EvalRunner/Workspace/`) materializes `lint-seeded-defects` verbatim plus 60 deterministic filler pages (~51k content tokens) on first resolution through `EvalPaths.FixtureWikiRoot`; output is git-ignored. Determinism is load-bearing (the tree feeds the staleness fingerprint) so the generator uses a hand-rolled LCG, fixed timestamps and LF endings — covered by `LintAtScaleFixtureTests`. Filler is templated prose, not word salad, and carries no defect category: 60 incoherent or untagged pages would crowd the seeded defects out of the report and fail SC-011 for an unrelated reason
- [X] T006 [P] Add the eval-config context-budget lever in `backend/src/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs` (SC-011): lets a small generated fixture exceed the guard, so fixture size stays irrelevant to the property under test. **Done**: `LintScenarioDefinition.ContextBudgetTokens` (nullable), appended to `StableSerialization()` only when set so no pre-existing scenario's fingerprint moved. `lint-at-scale-survey` declares 20 000 against a ~51k-token fixture

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the policy model, tool definitions and instrumentation seams every story needs.

**⚠️ CRITICAL**: blocks all of Phase 3–6.

- [X] T007 Add the `Delete` scope and `DeleteRule` record to `backend/src/Grimoire.Domain/Guardrails/SafetyPolicy.cs` (FR-021, ADR-031 R3, data-model.md): deny-by-default, canonicalized path matching, no mode variants
- [X] T008 Teach `PolicyLoader` (`backend/src/Grimoire.AgentRuntime/Instructions/PolicyLoader.cs`) the `delete` scope and keep `"frontmatter-only"` a recognized write mode (FR-020, ADR-031 R5): an unknown mode string still fails closed; a policy declaring `frontmatter-only` must still load
- [X] T009 Rewrite `backend/src/Grimoire.LintAgent/Instructions/policy.json` to version 2 (FR-014, FR-015, FR-016, FR-016a): one `read-write` rule on `.` with no `excludePrefixes`, plus a `delete` scope on `.`. **Done**, together with T012/T065 and the T068 recapture as the one inseparable unit tasks.md predicted.
- [X] T010 Assert Ingest and Query policies declare no `delete` scope, in `backend/tests/Grimoire.IntegrationTests/` (FR-021, research.md D6): the guard against deletion leaking to an agent that already holds `read-write` on the content root
- [X] T011 Add `search_files`, `batch` and `delete_file` definitions plus `read_file` range parameters to `backend/src/Grimoire.AgentRuntime/Guardrails/ToolRegistry.cs` (FR-001, FR-002, FR-008, FR-011, FR-024), each schema declaring `additionalProperties: false` per contracts/guarded-tool-surface.md
- [X] T012 Declare the new tools in `backend/src/Grimoire.LintAgent/LintToolRegistry.cs` only (FR-022, ADR-030 R6): Ingest and Query registries unchanged. **Done**: `list_files`, `read_file` (via `RangedReadFileDefinition`), `search_files`, `batch`, `write_file`, `delete_file`. **Offering `batch` to a live provider for the first time surfaced two schema defects** that no hermetic test could have caught, because they are the provider's validation rather than ours: `maxItems` on the `calls` array and a bare `{"type": "object"}` for a call's `input` are both rejected outright (HTTP 400, run fails before turn 1). Both fixed in `ToolRegistry` — the 20-call cap is prose plus the pre-existing `BatchMaxCalls` runtime check, and `input` is now the closed union of the three batchable tools' parameters.
- [X] T013 Extend `backend/src/Grimoire.AgentRuntime/Guardrails/IToolCallInstrumentation.cs` with the search, batch, read-shape and deletion signals (plan.md § Observability), each defaulted no-op so agents that do not use them need no adapter
- [X] T014 Register the new metrics, log events and spans in the **production** composition root, `backend/src/Grimoire.LintAgent/LintAgentTracing.cs` (Principle IV): contract tests must read them from the real telemetry registration, sampler and exporter — never a test-only `ActivitySource` or always-on sampler

**Checkpoint**: foundation ready; stories may proceed.

---

## Phase 3: User Story 1 — Find pages without reading the wiki (Priority: P1) 🎯 MVP

**Goal**: `search_files` returns capped, bounded, in-scope matches so Lint can survey a wiki
larger than its context guard.

**Independent Test**: run a survey against `lint-at-scale` with a term on three pages; the run
completes, findings reference those pages, and recorded tool calls show search rather than a
whole-wiki read.

- [X] T015 [US1] Implement `search_files` dispatch in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-001, FR-002): pattern + optional path prefix → `path:line:text`
- [X] T016 [US1] Evaluate the read policy per candidate path before opening it, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-003, SC-001): a denied path is **omitted silently**, never reported — reporting it would disclose the path
- [X] T017 [US1] Record a denial for an out-of-scope search root and continue the run, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-004, SC-002)
- [X] T018 [US1] Enforce the result cap with an explicit truncation marker, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-005, SC-003): default 200, `max_results` may lower it, hard ceiling 1000 (ADR-030 R5)
- [X] T019 [US1] Enforce the search time budget, returning partial results with an incomplete marker, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-006, SC-003): never "no matches", never a failed run
- [X] T020 [US1] Build the regex with `RegexOptions.NonBacktracking` and a match timeout, and enforce the 1000-character pattern bound, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-007, FR-007a, ADR-030 R2)
- [X] T021 [US1] Reject an unsupported or oversized pattern as a recorded denial naming the reason, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-007a, SC-002)
- [X] T022 [P] [US1] Test: a match inside a read-denied path is absent from results and no denial names it (SC-001) in `backend/tests/Grimoire.IntegrationTests/`
- [X] T023 [P] [US1] Test in `backend/tests/Grimoire.IntegrationTests/LintSearchToolTests.cs`: traversal and symlink search roots canonicalize before policy evaluation (FR-003, SC-001)
- [X] T024 [P] [US1] Test in `backend/tests/Grimoire.IntegrationTests/LintSearchToolTests.cs`: cap reached → truncation signalled; budget exhausted → incomplete signalled (SC-003)
- [X] T025 [P] [US1] Test in `backend/tests/Grimoire.IntegrationTests/LintSearchToolTests.cs`: lookaround pattern and >1000-char pattern each produce a recorded denial, run continues (FR-007a, SC-002)
- [X] T026 [P] [US1] Feature-Scoped Invariant test in `backend/tests/Grimoire.IntegrationTests/LintSearchToolTests.cs`: the four documented defaults are observable through behaviour, not reflection (ADR-030 R5, SC-003) — a 201st match truncates, a 1001 `max_results` clamps
- [X] T027 [US1] Emit `wiki.search.invocations_total`, `wiki.search.matches_returned`, `wiki.search.files_scanned` from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (plan.md § Observability)
- [X] T028 [US1] Emit `wiki.search.truncated`, `wiki.search.timed_out`, `wiki.search.pattern_rejected` log events with their mandatory fields, from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (plan.md § Observability)
- [X] T029 [US1] Create the `guardrails.search_scan` span as a child of `lint_agent.tool_call`, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, with its declared attributes (plan.md § Observability)
- [X] T030 [P] [US1] Deterministic test in `backend/tests/Grimoire.IntegrationTests/LintSearchObservabilityTests.cs`: the three search log events assert name, level and every mandatory field, read from the production composition root (Principle IV)
- [X] T031 [P] [US1] Deterministic test in `backend/tests/Grimoire.IntegrationTests/LintSearchObservabilityTests.cs`: `guardrails.search_scan` name, parent linkage and `task_id` correlation, read from the production composition root (Principle IV)

**Checkpoint**: Lint can find without reading. MVP deliverable.

---

## Phase 4: User Story 2 — The agent can change page content (Priority: P2)

**Goal**: one write scope for both modes, covering create, edit and delete across the content
root including the reserved files.

**Independent Test**: a body edit succeeds in a survey run and in an authorized remediation
execution on identical terms; a delete followed by a run failure restores the page.

- [X] T032 [US2] Verify all three coordinators — `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs`, `backend/src/Grimoire.Hub/RemediationTasks/RemediationRunCoordinator.cs`, `backend/src/Grimoire.Hub/RemediationTasks/RemediationMessageTurnCoordinator.cs` — keep passing `_paths.Lint.PolicyPath` unchanged and no mode branch is introduced (FR-014, FR-017, ADR-031 R1)
- [X] T033 [US2] Implement `delete_file` dispatch in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, evaluated against the **delete** scope (FR-015, ADR-031 R3)
- [X] T034 [US2] Journal a deletion with its content in `backend/src/Grimoire.AgentRuntime/Guardrails/WriteJournal.cs` and restore it in reverse-order rollback (FR-015a, ADR-031 R4)
- [X] T035 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintWriteScopeParityTests.cs`: the write decision is identical in a survey run and an execution run for the same path and content (SC-004) — the anti-regression test for ADR-031 R1
- [X] T036 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintWriteScopeParityTests.cs`: writes outside the wiki content root are denied and recorded in both modes (SC-005)
- [X] T037 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintDeletionRollbackTests.cs`: a page deleted by a run that then fails is restored by the journal (SC-005a)
- [X] T038 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintWriteScopeParityTests.cs`: an authorized remediation whose proposal targets page content runs under a scope that permits it (SC-006) — end-to-end through the real coordinator
- [X] T039 [P] [US2] Test: Lint writes to `index.md`/`log.md` are held to ADR-017 entry format and ADR-028 prepend ordering (SC-005b, FR-016b)
- [X] T040 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintPolicyIdentityTests.cs`: policy identity (version + hash) is recorded on every run (SC-009, FR-019)
- [X] T041 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintPolicyIdentityTests.cs`: a missing or unparseable policy fails the run before any wiki file changes (SC-010, FR-020)
- [X] T042 [P] [US2] Test in `backend/tests/Grimoire.IntegrationTests/LintPolicyIdentityTests.cs`: a policy declaring `frontmatter-only` still loads (ADR-031 R5) — the upgrade-safety invariant. Already covered verbatim by the pre-existing `PolicyLoaderFrontmatterOnlyModeTests` (013-lint-agent); no new test needed
- [X] T043 [US2] Emit `wiki.page.deletions_total` from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (plan.md § Observability)
- [X] T044 [US2] Emit `wiki.page.deleted` and `wiki.page.delete_rolled_back` log events with their mandatory fields, from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (plan.md § Observability). Also wires the actual rollback trigger into `Grimoire.LintAgent/Program.cs` (`LintIntentHandler`/`RemediationExecutionIntentHandler.DescribeUnhandledFailureAsync`) — previously unwired for Lint entirely, unlike Ingest's `RollbackAsync` — since `wiki.page.delete_rolled_back` needs a real production call site (Principle IV)
- [X] T045 [US2] Create the `guardrails.delete_file` span as a child of `lint_agent.tool_call`, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, with `journaled`/`outcome` attributes (plan.md § Observability). Resolved the same way T029 resolved `guardrails.search_scan` in layer 05: the achievable parent is `lint_agent.model_turn`, not `lint_agent.tool_call` — see `IToolCallInstrumentation.StartDeleteFileActivity`'s doc comment
- [X] T046 [P] [US2] Deterministic test in `backend/tests/Grimoire.IntegrationTests/LintDeletionObservabilityTests.cs`: both deletion log events assert name, level and mandatory fields from the production composition root (Principle IV). Already covered by the pre-existing `LintLogEventTests.GuardedRetrievalEvents_EmitExpectedNamesLevelsAndFields` (layer 04); no new test needed
- [X] T047 [P] [US2] Deterministic test in `backend/tests/Grimoire.IntegrationTests/LintDeletionObservabilityTests.cs`: `guardrails.delete_file` span name, parent linkage and `task_id` from the production composition root (Principle IV)

**Checkpoint**: the remediation loop completes; the agent maintains the wiki.

---

## Phase 5: User Story 3 — Read only the part that matters (Priority: P3)

**Goal**: bounded reads that do not compromise write coordination.

**Independent Test**: frontmatter-only and line-range reads return exactly their slice; a
partially read page cannot be overwritten.

- [X] T048 [US3] Implement `offset`/`limit`/`frontmatter_only` on `read_file` and the `ReadShape` discriminator, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-008, data-model.md) — schema stays on a new `ToolRegistry.RangedReadFileDefinition` constant, not the shared `ReadFileDefinition` every agent already declares (see that constant's doc comment); dispatch parses the fields regardless of which schema advertised the call
- [X] T049 [US3] Ensure a partial read never calls `SharedFileWriteGuard.OnReadFile`, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-010, ADR-030 R3): only a full read may set the compare-and-swap baseline
- [X] T050 [P] [US3] Test in `backend/tests/Grimoire.IntegrationTests/LintRangedReadWriteGuardTests.cs`: a write to a page read only in part is rejected and recorded (SC-008) — the ADR-015 protection test
- [X] T051 [P] [US3] Test in `backend/tests/Grimoire.IntegrationTests/LintRangedReadWriteGuardTests.cs`: no range parameters → byte-for-byte today's whole-file read, baseline still set (FR-009)
- [X] T052 [P] [US3] Test in `backend/tests/Grimoire.IntegrationTests/LintRangedReadWriteGuardTests.cs`: a range beyond end-of-file returns partial/empty with an explicit EOF signal, not a failed run (FR-008)
- [X] T053 [US3] Emit `wiki.read.invocations_total` labelled by read shape, from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (plan.md § Observability) — also the source for the SC-014 measurement
- [X] T054 [P] [US3] Deterministic test in `backend/tests/Grimoire.IntegrationTests/LintReadShapeObservabilityTests.cs`: the read-shape label is correct for full, range and frontmatter reads, from the production composition root (Principle IV)

**Checkpoint**: retrieval is cheap per page as well as per wiki.

---

## Phase 6: User Story 4 — Spend fewer turns on read-only work (Priority: P4)

**Goal**: several read-only calls in one turn, with writes unrepresentable.

**Independent Test**: a mixed allowed/denied batch returns all results with individual
denials; a batch containing a write performs nothing.

- [X] T055 [US4] Implement `batch` dispatch accepting only `list_files`, `read_file`, `search_files`, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-011, FR-012) — each member re-enters the same `ExecuteAsync` every top-level call goes through
- [X] T056 [US4] Reject a batch containing a write, a delete, a nested batch, or more than 20 calls — before any member executes — in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-012, SC-007, ADR-030 R4/R5)
- [X] T057 [US4] Evaluate and record each member individually against the policy, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-013, SC-002)
- [X] T058 [P] [US4] Test in `backend/tests/Grimoire.IntegrationTests/LintBatchToolTests.cs`: a batch with one write executes no member at all (SC-007)
- [X] T059 [P] [US4] Test in `backend/tests/Grimoire.IntegrationTests/LintBatchToolTests.cs`: a mixed batch returns allowed results plus an individual denial for the denied member (FR-013, SC-002)
- [X] T060 [US4] Emit `wiki.batch.invocations_total` and the `wiki.batch.rejected` log event with its mandatory fields, from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (plan.md § Observability)
- [X] T061 [US4] Create the `guardrails.batch` span as a child of `lint_agent.model_turn`, in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`, with `call_count`/`denied_count` (plan.md § Observability)
- [X] T062 [P] [US4] Deterministic test in `backend/tests/Grimoire.IntegrationTests/LintBatchObservabilityTests.cs`: `wiki.batch.rejected` fields and the `guardrails.batch` span parent linkage, from the production composition root (Principle IV) — the log-event half was already covered by `LintLogEventTests`

**Checkpoint**: full tool surface delivered.

---

## Phase N: Polish & Cross-Cutting Concerns

- [X] T063 Logging contract CI enforcement, in `.github/workflows/ci.yml` (MANDATORY — Principle IV): confirm the deterministic tests for all 6 Structured Log Events rows run in the `Deterministic Backend Gates` job on every PR — confirmed: the job's "Run hermetic integration tests" step runs `dotnet test backend/tests/Grimoire.IntegrationTests` unfiltered (ci.yml line 69), which is where `LintLogEventTests` (all 6 rows) lives; no tier exclusion or filter narrows it
- [X] T064 Trace contract CI enforcement, in `.github/workflows/ci.yml` (MANDATORY — Principle IV): confirm the deterministic tests for all 3 Distributed Trace Spans rows run in the `Deterministic Backend Gates` job on every PR — confirmed, same step and project as T063: `LintSearchObservabilityTests`, `LintBatchObservabilityTests`, `LintDeletionObservabilityTests` all live in `Grimoire.IntegrationTests`
- [X] T065 Update `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md` for the agent-side judgment the new capabilities need (FR-023, Principle V): when to search vs. read, when a body edit is warranted, when to delete rather than supersede, when to reconcile the index, and what to leave as a remediation task. **Instruction-file content is never asserted by a harness test** — only its load mechanism. **Done**, landed with T009/T012/T068 as one unit. Step 1 became *Survey the wiki* (narrow by depth, never by dropping pages) with a new **Choosing how to read** section covering all four read shapes and `batch`; new **What to fix yourself, and what to propose** (the judgment the widened authority makes load-bearing — contradictions the wiki cannot settle stay proposals), **Before any write**, **Deleting a page** (prefer supersession; own the cleanup), and **Reconciling `index.md` and `log.md`**. Remediation Execution Mode's binding limit is restated as the authorization rather than the guard, since the guard no longer stops an over-broad edit. Mode routing was rewritten because four sections now apply in every mode and the old "ignore everything above" instruction would have hidden them. **The recapture caught a real defect in the first draft of this rewrite.** Its "fix it yourself" rule listed "a required field is missing" as a mechanical correction, which swallowed tag and confidence gaps: the agent began *applying* metadata instead of *proposing* it, and `lint-metadata-proposals` fell from passing to **40%** (threshold 90%) because the proposals SC-007 scores had stopped being emitted. That is a criterion this feature never withdrew, and an instruction-file edit is not the place to silently redefine one. Narrowed the rule to values that are *derivable* rather than *chosen* (an `inbound_links` count, a dangling wikilink, an index entry for a deleted page) and added an explicit carve-out sending `tags`/`confidence` back to Step 2's proposal path — choosing a page's tags is deciding what it is about, not correcting it. Rescored **100%** on a 4-sample probe afterwards. Nothing but a live recapture could have surfaced this: the conflict was between two paragraphs of an instruction file, which no harness test may assert on (Principle V). **A second draft defect, same class, caught by the same means.** The rewrite's **Deleting a page** section said a merely-stale page's "fix is an edit or a supersession", which read as licence to raise a proposal for one — and `lint-remediation-proposals` fell to **70%** (threshold 90%) because three of ten runs proposed "review this stale page", the one target `RemediationGoldenSet` marks explicitly non-actionable. Step 3b already said informational findings produce no proposal; the new section had quietly undercut it from elsewhere in the same file. Fixed by restating the rule where the reader actually decides (**Informational findings produce neither a write nor a proposal**) and rewording the deletion guidance so a Review Window candidate lands in the report and nowhere else. **Both defects were contradictions between two paragraphs of one instruction file** — invisible to every harness test by construction (Principle V), and detectable only by running the agent. After the fix `lint-remediation-proposals` recaptured at **90.0% against its 90% threshold** — passing, but exactly on the line. The one remaining failure is unrelated to either defect above: a vague proposal ("Clarify index entries for test-fixture pages") carrying no `targetPath`, which `RemediationGoldenSet` does not recognise. **Deliberately not chased further.** The scenario meets its threshold, and continuing to tune an instruction file until a passing number climbs is fitting the agent to a frozen golden set rather than improving it — the caveat `RemediationGoldenSet`'s own doc comment raises about itself ("a placeholder, not a substitute for human review"). Recorded here so the margin is visible to the next person rather than discovered by a flake
- [X] T066 [P] Add the SC-011 eval scenario on `lint-at-scale`, in `backend/src/Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs`, at threshold ≥ 90% (SC-011). **Done**: `lint-at-scale-survey` + the `lint-at-scale-survey` scorer, gated in CI by `LintReplayEvalTests.SC011_AtScaleSurvey_ReplaysAtThreshold`. Scored on both halves — the survey must still find the seeded defects *and* stay under budget, since either alone is satisfiable in the wrong direction (read nothing, or read everything). **The budget is measured as content tokens read, not peak `input_tokens`**: the first probe run narrowed correctly (68 frontmatter-only reads, 18 full, 9 828 of the wiki's 50 895 content tokens) yet peaked at 33 296 input tokens, because that number is the whole twenty-turn conversation plus tool schemas — measuring it would have failed a passing run for a reason SC-011 does not name. `ReadShapeAccounting` reconstructs the read side from the recording, the same quantity baseline.md uses, so eval and measurement are denominated alike
- [X] T067 [P] Add the SC-013 eval scenario for authorized body-edit remediations, in `backend/src/Grimoire.EvalRunner/Scenarios/RemediationReVerificationScenarioDefinitions.cs`, at threshold ≥ 90% (SC-013) — the only assurance behind ADR-016's superseded structural guarantee; **not negotiable down**. **Done at 0.90, not negotiated**: `remediation-body-edit-applied` over the new committed `remediation-body-edit` fixture, gated by `RemediationReVerificationEvalTests.SC013_AuthorizedBodyEdit_ReplaysAtThreshold`. The fixture states the wrong value **once**, deliberately: a first draft repeated it in two paragraphs and the model corrected only one, which would have made the scenario measure exhaustive search rather than whether an authorized body edit lands surgically. The surgical-ness is where the superseded guarantee's weight now sits, so the scorer checks it directly — stale value gone, corrected value present, heading intact, unrelated frontmatter intact, and a named untouched paragraph preserved byte-for-byte
- [X] T068 Capture recordings for T066/T067 into `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/`, recording the one-time eval re-capture (ADR-012). **Done** — this was the task everything else in this list was downstream of, and it needed the live credentials this environment finally has. All nine Lint/Remediation scenarios captured on one `system-prompt.md` hash: `lint-defects-found` 90% (≥85%), `lint-genuine-findings` 100%, `lint-metadata-proposals` 100%, `lint-inbound-links-refreshed` 100% (≥95%), `lint-remediation-proposals` 90% (≥90%), **`lint-at-scale-survey` 100% (SC-011)**, `remediation-reverify-still-applicable` 100%, `remediation-reverify-no-longer-applicable` 100%, **`remediation-body-edit-applied` 100% (SC-013)**. `status` reports 27/27 trusted, and the hermetic replay suite is **81 passed, 0 skipped** (was 76). Took four capture rounds: two to fix instruction-file defects the recapture itself exposed (see T065), one lost to a scorer-wiring bug (`ContextBudgetTokens` was passed by the replay pipeline but not the capture pipeline, which scores too), and one clean. **Transient provider errors cost a whole round**: a lost sample discards the other nine (no partial stores) and the runner printed only "not every sample produced a recording" with no sample, no reason — so the final round retries per scenario and paces between them, and `Program.cs` now prints the per-sample detail it always had but never surfaced
- [X] T069 Record the SC-014 before/after numbers from `wiki.read.invocations_total{shape}` in `specs/026-guarded-tool-surface/baseline.md` and the implementation PR description (SC-014, research.md D9) — a measurement, not a gate. **Done, and the honest answer is two-sided.** On `lint-seeded-defects` content read went **up** 4.5% (3012.5 → 3149.0): nine pages totalling ~1 600 tokens have nothing to narrow, reading them all is correct, and the 16 ranged + 116 frontmatter reads that appear are the agent sampling metadata before deciding. On `lint-at-scale` — 69 pages, 50 895 content tokens, the size the feature exists for — the median run reads **7 209**, an **≈86% reduction** against the 50 895 a whole-wiki read costs, which was the pre-feature surface's only option (`read_file` took a bare `path`). Frontmatter reads outnumber full reads 285:186. Reporting either number alone would misrepresent the feature, so baseline.md reports both and says why they differ
- [X] T070 Observability completeness audit against `specs/026-guarded-tool-surface/plan.md` (MANDATORY — Principle III/IV): cross-reference every row of `plan.md ## Observability` — 6 metrics, 6 log events, 3 spans — against its implementing task and passing test, and file any gap as a new task in `specs/026-guarded-tool-surface/tasks.md` before declaring the DoD met. **Result: no gap.** All 6 metrics (`wiki.search.invocations_total`/`matches_returned`/`files_scanned` — #175; `wiki.read.invocations_total` — #177; `wiki.batch.invocations_total` — #177; `wiki.page.deletions_total` — #176), all 6 log events (`wiki.search.truncated`/`timed_out`/`pattern_rejected`, `wiki.batch.rejected`, `wiki.page.deleted`/`delete_rolled_back` — all in `LintLogEventTests`, foundational layer #174), and all 3 spans (`guardrails.search_scan` — #175, `guardrails.batch` — #177, `guardrails.delete_file` — #176) have a real call site and a passing test. One pre-existing, already-resolved discrepancy carried forward rather than hidden: plan.md's table still declares `guardrails.search_scan`/`guardrails.delete_file`'s parent as `lint_agent.tool_call`; the actual, tested, implemented parent is `lint_agent.model_turn` (that span is only live during dispatch — `lint_agent.tool_call` is created by `RecordAllowed`/`RecordDenied` *after* dispatch returns) — recorded in `IToolCallInstrumentation`'s doc comments and in #175/#176's PR bodies per this feature's established precedent of never hand-editing plan.md's prose after the fact
- [X] T071 Agent-behavior evaluation completeness audit against `specs/026-guarded-tool-surface/spec.md` (MANDATORY — Principles II & V): confirm SC-011 and SC-013 each have a passing evaluation at their threshold, and that **no agent-judgment criterion in spec.md lacks one** — SC-012/SC-014 are withdrawn as criteria, so the set is exactly two. **Result: re-run after T068, and the gap is now closed.** SC-011 is evaluated by `lint-at-scale-survey` at **100%** (threshold 90%), gated by `LintReplayEvalTests.SC011_AtScaleSurvey_ReplaysAtThreshold`; SC-013 by `remediation-body-edit-applied` at **100%** (threshold 90%, never negotiated down), gated by `RemediationReVerificationEvalTests.SC013_AuthorizedBodyEdit_ReplaysAtThreshold`. Both run in the standard PR pipeline's `Grimoire.AgentEvals` step with no filter and no skip. The set is exactly these two and both pass, so no agent-judgment criterion lacks an evaluation. **The earlier finding — that the DoD could not be met — was correct when recorded and is now superseded by this run, not retracted.** Also re-confirmed here: every *pre-existing* Lint and Remediation criterion still passes on the recaptured evidence (SC-005 90%/≥85%, SC-006 100%, SC-007 100%, SC-008 100%/≥95%, 015's SC-006 90%/≥90%, FR-018's pair 100%/100%), which matters because the recapture re-ran all of them against a rewritten `system-prompt.md` — a widened write scope could have quietly cost the agent behaviour those criteria protect, and twice during the recapture it did (see T065)
- [X] T072 ADR consistency audit against `docs/adr/index.md` (Principle III): confirm ADR-030/ADR-031 are Accepted, ADR-016 is `superseded`, the bidirectional links on ADR-006/011/016/017/018 are present, and `docs/adr/index.md` matches. **Result: no gap.** `docs/adr/index.md` and every ADR file's own frontmatter status header agree: ADR-030 `accepted` (amends ADR-006, ADR-011); ADR-031 `accepted` (supersedes ADR-016; amends ADR-017, ADR-018, ADR-006); ADR-016 `superseded` (superseded by ADR-031); ADR-017/ADR-018 both list "amended by ADR-031"; ADR-006/ADR-011 both list ADR-030 in their amendment chains. Every link is bidirectional on both sides

---

## Dependencies

```
Phase 0 (T001-T003)  ── blocks everything
        ↓
Phase 1 (T004-T006)  ── T004 MUST precede Phase 2 (baseline)
        ↓
Phase 2 (T007-T014)  ── blocks all stories
        ↓
   ┌────┴────┬─────────┬─────────┐
  US1       US2       US3       US4
(T015-31) (T032-47) (T048-54) (T055-62)
   └────┬────┴─────────┴─────────┘
        ↓
Phase N (T063-T072)
```

**Story independence**: US1, US2, US3 and US4 touch different dispatch branches and are
independent once Phase 2 lands. US3's T049 and US2's write path both concern
`SharedFileWriteGuard`, so run T049 and T035 in the same pass if both stories are in flight.

## Parallel Opportunities

- **Phase 0**: T001, T002, T003 — three separate rule files
- **Phase 1**: T005, T006 after T004
- **Phase 3**: T022–T026 together; T030–T031 together
- **Phase 4**: T035–T042 together; T046–T047 together
- **Phase 5**: T050–T052 together
- **Phase 6**: T058–T059 together
- **Phase N**: T066, T067 together

## Implementation Strategy

### Delivery shape — stated out loud, per CLAUDE.md

This feature has **six phase groups beyond Phase 0**, which is past the threshold where
CLAUDE.md's default applies. **Delivery shape: a stack**, continuing the one already open
(#165 spec → #168 plan → this tasks layer). The cut:

| Layer | Phases | Rationale |
|---|---|---|
| 04 | Phase 0 + Phase 1 + Phase 2 | Boundaries guarded and the policy model changed, with no behaviour yet |
| 05 | Phase 3 (US1) | The MVP — search alone makes Lint viable at scale |
| 06 | Phase 4 (US2) | The write scope; the largest blast-radius change, reviewed alone |
| 07 | Phase 5 + Phase 6 (US3 + US4) | Two small refinements, grouped rather than split |
| 08 | Phase N | Evals, audits, instruction files |

Five layers, inside the skill's 3–6 guidance. `tasks.md` checkboxes are ticked in **one layer
only** (the top) to avoid cascading rebase conflicts.

**Layer 08 shipped audit-only; Phase N was completed in a later session.** T009
(`policy.json` v2), T012 (`LintToolRegistry` switch-flip), T065 (`system-prompt.md` rewrite),
T066/T067 (new eval scenarios) and T068 (capturing recordings for them) are one inseparable
unit, exactly as layer 08 predicted: landing any of the first three alone stales every
checked-in Lint/Remediation recording, and T066/T067 cannot be captured without a live model.
Layer 08 had no `ANTHROPIC_AUTH_TOKEN` and correctly shipped only what did not need one —
T063/T064, T070, T072, and T071's finding that the DoD was not yet met.

**With credentials available, the whole unit landed together**, plus T004/T005/T006 which were
downstream of the same block. All 27 eval scenarios are trusted, the hermetic replay suite is
81 passed / 0 skipped, and the full backend suite is 1 200 passed / 0 skipped. The DoD line
item T071 could not satisfy in layer 08 — "agent-behavior evaluation tests pass for every
agent-judgment success criterion" — is now satisfied: SC-011 at 100% and SC-013 at 100%.

**What the recapture cost, and what it bought.** Four capture rounds: two spent fixing
instruction-file defects that only a live run could expose (a fix-it-yourself rule that
swallowed SC-007's metadata proposals, dropping that scenario to 40%; and deletion guidance
that licensed proposals against the one page the golden set marks non-actionable, dropping
SC-006 to 70%), one lost to a scorer-wiring bug, one clean. Both instruction defects were
contradictions between two paragraphs of the same file — the exact class of defect Principle V
forbids harness tests from catching, and that only an evaluation can. Three production defects
surfaced the same way: `batch`'s schema was rejected outright by the provider (`maxItems`, then
a bare `{"type":"object"}`), and Lint reached FR-016a's newly-in-scope `index.md`/`log.md`
without FR-016b's format checks wired at all.

### MVP scope

Phase 0 → 1 → 2 → 3 (US1). At that point Lint can survey a wiki it currently cannot, which is
the outcome #108 is waiting on. US2 completes the remediation loop; US3 and US4 are cost
refinements.

### Decision: `edit_file` declined for this feature

A partial write (`edit_file`, anchored `old_string`/`new_string`) was raised during planning
and not settled; tasks.md flagged it as needing a decision before layer 07 was cut, since
after that point adding it becomes a retrofit rather than a scoping choice.

**Declined, not deferred.** Reasons:

1. It is not required by any FR or SC in the accepted `spec.md`. Adding it now would mean
   amending an already-accepted spec late in an eight-layer stack, not implementing what was
   already scoped — exactly the kind of ad hoc, out-of-band feature growth the constitution's
   Spec-Kit workflow exists to prevent.
2. The token-cost problem it would solve is already substantially addressed by Phase 5's
   ranged reads (T048–T052): the remaining asymmetry (a ranged read still requires a full
   read before a write can follow) is a real but smaller residual cost, not the primary win.
3. This environment has no live-model credentials (`ANTHROPIC_AUTH_TOKEN` unset), so even if
   `edit_file` were added, its agent-judgment behavior (when a body edit is warranted vs. a
   full rewrite) could not be evaluated here before layer 08's recapture — it would sit
   undeclared in `LintToolRegistry.Default` exactly like `search_files`/`delete_file`/`batch`
   already do, adding surface without adding anything this stack can currently verify.

If a future feature wants `edit_file`, it goes through `/speckit-specify` on its own terms
(a new FR, an ADR-030 amendment naming the tool and its scope rule) rather than being folded
into this one after the fact.

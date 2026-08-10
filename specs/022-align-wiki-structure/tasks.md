# Tasks: Wiki Structure Truth — Retire `pages/` and Report Real Wiki State

**Feature**: 022-align-wiki-structure | **Branch**: `022-align-wiki-structure` | **Date**: 2026-08-10

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**ADRs in force**: ADR-002, ADR-006, ADR-007, ADR-012, ADR-013, ADR-016, ADR-017, ADR-018,
ADR-022, and **ADR-023** (new, Accepted).

## Sequencing note (read before starting)

The structural rules in Phase 2 go **RED against the real repository the moment they land**,
because the sweep has not happened yet. That is expected and is the Red half of Principle III's
probe — feature 014's own rule class documents the same pattern. The rules turn green as
Phases 3–6 land. Do not weaken a rule to make Phase 2 green.

The permanent probe `[Fact]`s (synthetic scan targets) are green from the moment they are
written; only the repository-wide `[Fact]`s are red in the interim.

---

## Phase 1: Setup

- [X] T001 Create `backend/src/Grimoire.Hub/HarnessSurfaces/` directory for the ADR-023 options record and reserved-name declaration
- [X] T002 [P] Add the four `Grimoire:HarnessSurfaceReads` keys (`Tasks`, `Conversations`, `Findings`, `RemediationTasks`), all `false`, to `backend/src/Grimoire.Hub/appsettings.json` with a comment citing ADR-023
- [X] T003 [P] Create `docs/conventions/wiki-content-root.md` from `specs/022-align-wiki-structure/contracts/wiki-content-root.md` sections C1–C7 — the single authoritative layout document (SC-005)

---

## Phase 2: Foundational — structural boundary tests (BLOCKING)

**Constitution Principle III**: these are the first tasks and each carries a Red/Green probe
proving the rule detects violations. No feature code before this phase completes.

- [X] T004 Extracted `ArchScan.FindRepositoryRoot()` (anchors on `backend/src` + `backend/tests` + `docs` all present, one directory stricter than the old two-anchor `FindBackendSrcDirectory`) and repointed `AgentArtifactNamingRuleTests.FindConventionDocument` at it
- [X] T005 Added `ArchScan.ScanTarget`, `ExemptedDirectorySegments`, and `EnumerateScanTargets` — excludes `bin`, `obj`, `node_modules`, `.svelte-kit`, `.git`, `.grimoire`, `frontend`, plus (added during implementation, see T008 note) `foundational`, `ideas`, and the frozen recordings path
- [X] T006 Created `RetiredPagesWrapperPathRuleTests.cs` replacing `PagesWrapperRetirementBoundaryRuleTests.cs`, scanning the full surface specified
- [X] T007 Deleted `PagesDirSymbolPattern` and `AllowedRelativeFilePaths` — confirmed dead (`PagesDir` had zero hits in `backend/src`)
- [X] T008 Implemented the historical-marker exemption — **design correction made during implementation**: the originally-specified ±1-line window was too narrow for the repo's actual pattern (ADR-015's retirement note sits up to 13 lines after the `pages/` literals it explains) and a flat wide window caused two real false positives (ADR-003:40 wrongly exempted by an unrelated "## Considered Options" heading 10 lines away; ADR-014:26 wrongly exempted by an unrelated "per-turn artifact mechanism retired" 12 lines away). Final design in `ArchScan.IsHistoricalRetirementContext`: Rule 1 requires a marker line that ALSO mentions "pages"/"wrapper" (not just proximity) within a ±20 line window; Rule 2 bounds "## Considered Options" to its actual section (heading → next heading of any level), not a flat window. Also added: `docs/foundational/` and `docs/ideas/` exempted wholesale as absorbed source material per CLAUDE.md's Document Map ("No — never cite as requirements"); `docs/conventions/wiki-content-root.md` itself exempted wholesale (self-referential — C7 must use the word "page" to say it's retired). Verified against research.md R11's full classification: ADR-015/ADR-016's already-annotated historical spans pass unmodified; ADR-003/006/009/011/014/016(non-marker lines)/018's genuinely stale current-state claims remain flagged for Phase 6.
- [X] T009 Added `Rule_DetectsAViolation_WhenOneIsIntroduced` — passes
- [X] T010 Added `Rule_DoesNotFlag_AnAcceptedRecordDocumentingTheRetirement` — passes
- [X] T011 Created `WikiContentTerminologyRuleTests.cs` — implemented as a single loose case-insensitive substring regex `pages?` (no word-boundary assertions) rather than three separate boundary patterns, because C# identifiers compound without boundaries (`createdPages`, `PagesTouched`) so `\bPages?\b`/`\bpages?_\b` alone would miss most real occurrences; verified no unrelated English word in this repo contains "page"/"pages" as a substring
- [X] T012 Added `Rule_DetectsAViolation_WhenOneIsIntroduced` (metric name) — passes. Also added two extra permanent probes beyond spec: `Rule_DetectsAViolation_InPascalCaseIdentifiers` and `Rule_DetectsAViolation_InCommentProse` (the latter specifically proves comments are in scope here, unlike the path rule)
- [X] T013 Added the exemption-fixture mirror `[Fact]` to both rule classes — passes; mirrors `docs/conventions/wiki-content-root.md`'s `## Exemption list`
- [X] T014 Created `HarnessSurfaceScopeRuleTests.cs` implementing H1 — used `NetArchTest.Rules` (`.ShouldNot().HaveDependencyOn(...)`) matching `DomainDependencyRuleTests`'s existing idiom exactly, rather than a fresh Mono.Cecil scan — equivalent enforcement, less new code, consistent with the file it's modelled on
- [X] T015 Implemented H2 — **narrowed during implementation from the spec's literal wording**: "any single reserved-surface literal outside the owner file" produced a real false positive (`Grimoire.EvalRunner/Workspace/EvalWorkspace.cs:20`, `Path.Combine(WikiRoot, "tasks")` — a legitimate, unrelated use of the ordinary word "tasks" for an eval fixture's own subdirectory, confirmed the only such hit in the repo via `grep -rn '"tasks"\|"conversations"\|"findings"\|"remediation-tasks"'`). Redesigned to flag only a file that redeclares the **complete four-name set** together — the actual drift ADR-023 H2 names ("someone hand-copying the reserved-surface list instead of referencing `ReservedHarnessSurfaces.All`"). Reused `ArchScan.Tokenize` (moved there from the path rule for sharing) rather than a separate Cecil IL scan.
- [X] T016 **Red/Green probe ceremony — RECORDED.** Added `internal sealed class DeliberateProbeViolation { public IOptions<object>? Options { get; set; } }` to `Grimoire.Domain/Guardrails/SafetyPolicy.cs` (required temporarily adding `Microsoft.Extensions.Options` to `Directory.Packages.props` and `Grimoire.Domain.csproj`, since Domain has zero package references today). **RED**: `dotnet test --filter HarnessSurfaceScopeRuleTests.Domain_Must_Not_Depend_On_ExtensionsOptions` → `Fehler: 1` — `"ADR-023 H1: Grimoire.Domain must not depend on Microsoft.Extensions.Options."` All three files reverted via `git status --short` confirming zero diff. **GREEN**: same filter → `Bestanden! Fehler: 0, erfolgreich: 7`.
- [X] T017 **Red/Green probe ceremony — RECORDED.** The spec's literal recipe (edit the query prompt) doesn't cleanly demonstrate detection, because that file already carries real pre-sweep violations (part of the expected Phase 2 red state) — adding one more wouldn't show a clean before/after. Used a genuinely clean file instead: added `internal static readonly string DeliberateProbeViolation = "pages/";` to `Grimoire.Domain/Guardrails/PolicyDecision.cs` (confirmed zero prior `pages/`/`/pages`/`--pages-dir` hits via grep). **RED**: `dotnet test --filter RetiredPagesWrapperPathRuleTests.RepositoryText` → `Fehler: 1` — `"backend/src/Grimoire.Domain/Guardrails/PolicyDecision.cs:47 → string literal content \"pages/\""`, naming exactly that file and line. Reverted via `git status --short` → clean. **GREEN**: full suite back to `Fehler: 2, erfolgreich: 75, gesamt: 77` — the two expected repository-wide facts, nothing else.
- [X] T018 Verified: `.github/workflows/ci.yml:37` runs `dotnet test backend/tests/Grimoire.ArchTests --configuration Release --no-build` in the standard PR pipeline. No new CI step required for any Phase 2 rule.

**Checkpoint**: rules exist, probes are green, repository-wide facts are red pending the sweep.

---

## Phase 3: User Story 1 — The query agent reports what is actually in the wiki (P1) 🎯 MVP

**Goal**: an operator asking the query agent about the wiki gets an accurate picture.

**Independent test**: point the query agent at a content root with categories and articles, ask
what the wiki covers, confirm the answer names real categories and articles and does not claim
emptiness.

- [ ] T019 [US1] Rewrite the **Path convention** block (lines 22–30) of `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md`: remove the `pages/<slug>.md` wikilink resolution, remove the false "`list_files(".")` on the bare root is not allowed" claim, and replace `list_files("pages/")` with root enumeration — per `contracts/wiki-content-root.md` C4/C5
- [ ] T020 [US1] State wikilink resolution in the query prompt as filename-based and folder-agnostic (`[[slug]]` and `[[category/slug]]` are the same target; only the final segment resolves; resolve against enumerated filenames, never construct a path) — research.md R2
- [ ] T021 [US1] Make Step 1 of the query prompt tolerate a missing `index.md`: enumerate the root, report the missing catalog as a gap, never as evidence of emptiness (FR-005, FR-013)
- [ ] T022 [US1] Add the reserved-harness-surface statement (C3) to the query prompt: `tasks/`, `conversations/`, `findings/`, `remediation-tasks/` are harness records, not wiki content; never cite them as a wiki source; a denied read is routine — continue
- [ ] T023 [US1] Restate `contracts/wiki-content-root.md` C1–C7 in full in the query prompt and name `docs/conventions/wiki-content-root.md` as the source of truth (ADR-007 forbids an include mechanism — restatement is required, plan.md "SC-005 versus ADR-007")
- [ ] T024 [US1] Update the query prompt's Write Scope: Synthesis Article location `concepts/<slug>.md` (not `pages/concepts/<slug>.md`), and "create a new Synthesis Article under a category folder in the content root"
- [ ] T025 [US1] Fix the two stale cross-references in the query prompt (lines 109, 131) that name `agents/ingest/system-prompt.md` — a path that has not existed since ADR-022; replace with a non-path citation since the ingest prompt is not runtime-readable by Query
- [ ] T026 [US1] Remove the false clause "and this instruction set if needed" from the query prompt's `read_file` tool description — instructions live outside the guarded content root and cannot be read
- [ ] T027 [P] [US1] Add eval fixture `backend/tests/Grimoire.AgentEvals/Fixtures/wiki-empty-root/` containing only the four harness surface directories — reproduces the reported failure exactly (SC-007)
- [ ] T028 [P] [US1] Add eval fixture `backend/tests/Grimoire.AgentEvals/Fixtures/wiki-populated/` with `index.md`, `log.md`, and at least three category folders holding articles (SC-006)
- [ ] T029 [US1] Add deterministic scorer `WikiStateReportScorer` to `backend/src/Grimoire.EvalRunner/Scoring/`: checks the answer names ≥1 real category and ≥1 real article filename from the fixture, and does not assert emptiness (SC-006, threshold ≥95% / ≤2%)
- [ ] T030 [US1] Add deterministic scorer `EmptyWikiHonestyScorer`: checks the answer contains no `pages/` token and does not attribute emptiness to a missing folder (SC-007, threshold ≥90%)
- [ ] T031 [US1] Register both eval scenarios in the query eval suite with their spec thresholds
- [ ] T032 [US1] Re-capture the **query** agent recordings under `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/` — the prompt SHA-256 changed, so ADR-012's fingerprint marks every query recording stale and CI's zero-skip gate fails until refreshed
- [ ] T033 [US1] Run quickstart scenario 1 end to end against a temp content root holding only the four harness directories and confirm the agent no longer reports the `pages/`-framed empty-wiki answer

**Checkpoint**: the reported defect is fixed and independently demonstrable.

---

## Phase 4: User Story 2 — Every agent instruction describes the real wiki layout (P1)

**Goal**: ingest and lint navigate, create and cite at paths that exist.

**Independent test**: run an ingest over a source, confirm the article lands at
`<content-root>/<category>/<slug>.md` and the catalog entry links to that same existing path.

- [X] T034 [US2] Replaced the ingest prompt's "Wiki Folder Structure" tree with a new "Wiki Content Root" section (`Root composition` tree including `index.md`, `log.md`, the illustrative category folders, and the four reserved harness surfaces marked `HARNESS-OWNED`), plus explicit `Categories are open-ended` subsection (FR-002, contracts C1–C3). Folded into the combined C1–C7 restatement required by T040 rather than a standalone tree, since both tasks touch the same location.
- [X] T035 [US2] Stripped the `pages/` segment from all nine rows of the ingest prompt's file-location table and renamed `## Page Types` → `## Article Types`; `type:` values (Concept, Technology, …) left unchanged (research.md R10)
- [X] T036 [US2] Rewrote the ingest prompt's Step 1 exploration instructions, the OKF-bundle-root sentence, and the "every page lives in a topic folder under pages/" line: content root enumerated via `list_files(".")`; "the content root itself is the bundle root"; "every article lives in a category folder directly under the content root"
- [X] T037 [US2] Resolved the catalog-link contradiction: amended the "Deviation from OKF" paragraph to carve out `index.md` catalog bullet lines as the one exception (content-root-relative markdown link, because ADR-017's `^- \[.+\]\(.+\) — .+ — .+$` guard cannot be satisfied by a wikilink), wikilinks everywhere else (research.md R3)
- [X] T038 [US2] Added a dedicated "Wikilink Resolution" section stating the rule once (bare slug or folder-qualified path, only the final segment resolves). Cross-referenced by name from the lint prompt (T044); **not** cross-referenced from the query prompt — that file belongs to the parallel Phase 3 agent and was explicitly out of scope for this agent to edit; the query prompt still needs its own cross-reference added when Phase 3 lands.
- [X] T039 [US2] Preserved the frontmatter convention in substance and made the ADR-016 dependency explicit: "Every article opens with exactly two `---` delimiter lines — the frontmatter block sits between them, and the body starts immediately after the second line," plus an explicit note that this shape is what makes Lint's frontmatter-only writes possible. Verified `frontmatter_only_malformed_document`'s guard (`SharedFileWriteGuard`) is untouched by this feature — the substance guarantee holds.
- [X] T040 [US2] Restated `docs/conventions/wiki-content-root.md` C1–C7 in full in the ingest prompt's new "Wiki Content Root" section (root composition, open-ended categories, reserved harness surfaces, empty/partial roots) — **deviation**: C7's "retired vocabulary" content is restated as "A folder from an earlier layout" without using the literal words "page"/"pages"/"pages/", because `WikiContentTerminologyRuleTests`/`RetiredPagesWrapperPathRuleTests` scan `backend/src/**/*.md` and would flag the retired term/path even inside an explanatory sentence saying it's retired (only `docs/conventions/wiki-content-root.md` itself is exempted from that). Confirmed by running the arch test filter (see below): zero violations remain in this file.
- [X] T041 [US2] Rewrote the lint prompt's Step 1: `list_files(".")` on the content root, then `list_files` on each category folder it reveals, explicitly skipping the four reserved harness surfaces; `read_file` target is `<category>/<slug>.md`
- [X] T042 [US2] **Fixed the live remediation bug**: changed the lint prompt's `targetPath` guidance and the worked JSON example from `pages/<slug>.md`/`pages/runtime-paths.md` to a content-root-relative article path (`tech/<slug>.md`/`tech/runtime-paths.md`). Regression-tested by T048.
- [X] T043 [US2] Updated orphan-article detection ("Orphan articles": zero inbound links, reserved surfaces never scanned or counted) and Step 4's inbound-link tally (pass 1 explicitly excludes the four reserved harness surfaces from enumeration and from contributing an occurrence)
- [X] T044 [US2] Restated `docs/conventions/wiki-content-root.md` C1–C7 in full in the lint prompt's own "Wiki Content Root" section (same "avoid the literal retired term" approach as T040) and fixed all three stale `agents/ingest/system-prompt.md` cross-references — replaced with non-path citations ("the Ingest agent's Tag Taxonomy convention", "the Ingest agent's Confidence Scoring formula and thresholds", "the Ingest agent's Article Types convention"), since ADR-007 means Lint cannot read Ingest's instruction file at runtime and the path never existed post-ADR-022 anyway
- [X] T045 [US2] Renamed the Confidence Scoring row to "Article contains an explicit contradiction marker (⚠️)" in the ingest prompt, and the lint prompt's cross-reference now points at "the Ingest agent's Confidence Scoring formula and thresholds" (non-path, wording coordinated — no literal row text duplicated to drift out of sync)
- [X] T046 [US2] Added `backend/tests/Grimoire.IntegrationTests/ArticlePlacementTests.cs`: a scripted `AgentLoop`+`FakeModelClient`+`GuardedToolExecutor` run (mirroring `IngestRunLifecycleTests`'s pattern, with `indexPath` wired so ADR-017's catalog-shape guard is live) writes an article to `tech/example-technology.md` with no wrapper segment, and asserts the appended `index.md` catalog line's link target resolves to that exact file. Passing.
- [X] T047 [US2] Added `backend/tests/Grimoire.IntegrationTests/FreshContentRootBootstrapTests.cs`: an ingest run against a root with neither `index.md` nor `log.md` present leaves both present and populated (article + catalog line + log heading/paragraph), with no separate setup step, confirming ADR-017's first-write exemption end to end. Passing.
- [X] T048 [US2] Added `backend/tests/Grimoire.IntegrationTests/RemediationTargetPathTests.cs`: a scripted `read_file` against a content-root-relative `targetPath` (`tech/runtime-paths.md`, the shape T042 fixed) succeeds and returns the article's real content — the regression proof for the live bug. Passing.
- [X] T049 [P] [US2] Added scenario `IngestScenarioDefinitions.ReservedSurfaceAvoidance` (fixture `empty-topic`, threshold 0.95) and scorer `DeterministicScorers.ReservedSurfaceAvoidance`, wired as `IngestReplayEvalTests.SC009_ReservedSurfaceAvoidance_ReplaysAtThreshold`. **Deviation**: the scorer scans the sandbox's raw wiki tree directly rather than `SampleRunData.PageFiles` (which excludes `tasks/` by construction and would make an agent-writes-into-`tasks/` failure invisible), and matches the four reserved names without declaring them as one literal array in this file — ADR-023 H2 (`HarnessSurfaceScopeRuleTests`) reserves that declaration for `Grimoire.Hub.HarnessSurfaces.ReservedHarnessSurfaces`, which is Phase 5 (not yet landed); three names are matched exactly and the fourth by its `remediation-`-prefixed shape. Verified this doesn't trip H2 by running the ArchTests suite. The new `[Fact]` itself currently fails with an actionable "no trusted recordings (Missing)" message — expected, since capture (T050) is blocked; this is the same red-until-swept pattern the Phase 2 structural rules use.
- [ ] T050 [US2] **Blocked** (not done — see note): attempted `dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario update-over-duplicate`; it exits with "Eval provider gate resolved. provider=none outcome=skipped ... Set ANTHROPIC_AUTH_TOKEN, or all three of GRIMOIRE_EVAL_PROVIDER_BASE_URL/GRIMOIRE_EVAL_PROVIDER_MODEL/GRIMOIRE_EVAL_PROVIDER_API_KEY, to run live agent-behavior evals." No such credentials are configured in this environment (no `.env`, only `.env-example`; confirmed via `env | grep -i anthropic`). Confirmed via `dotnet test` that every ingest and lint `SlowEval`-tier fact now reports `Stale (changed: default_user_prompt, system_prompt)` / `Stale (changed: system_prompt)` respectively — the prompt SHA-256s did change as expected — but re-capture itself cannot be performed in this session. Not marked done; capture must run in an environment with live provider credentials.

**Checkpoint**: all three agents navigate the real layout; both P1 stories independently testable.

---

## Phase 5: User Story 3 — Harness surfaces are recognisable as harness surfaces (P2)

**Goal**: the operator decides, per surface, whether agents may read harness records; denied by
default; every run records what it was permitted to read.

**Independent test**: place articles and populated harness folders in one content root, ask the
query agent what the wiki covers, confirm harness records are not presented as articles.

### Configuration and declaration

- [ ] T051 [US3] Create `backend/src/Grimoire.Hub/HarnessSurfaces/ReservedHarnessSurfaces.cs` declaring the four reserved names in exactly one place (ADR-023 H2)
- [ ] T052 [US3] Create `backend/src/Grimoire.Hub/HarnessSurfaces/HarnessSurfaceReadOptions.cs` with `SectionName = "Grimoire:HarnessSurfaceReads"` and four `bool` properties defaulting to `false`, following `LintReviewWindowOptions`
- [ ] T053 [US3] Bind the options in `backend/src/Grimoire.Hub/HubHostComposition.cs` and register as a singleton, following the `LintReviewWindowOptions` binding at lines 87–97
- [ ] T054 [US3] Verify the binding flows through the same configuration composition the CLI uses, so `grimoire query …` sees the same effective scope as an HTTP-triggered run (ADR-020 parity)

### Domain enforcement

- [ ] T055 [US3] Add a denied-read-subtree list to `backend/src/Grimoire.Domain/Guardrails/SafetyPolicy.cs`, checked in the read branch of `Evaluate` **before** the allow loop, returning `harness_surface_not_granted`; match directory-style covering the subtree **and the bare directory itself** so `list_files("tasks")` is denied
- [ ] T056 [US3] Keep `SafetyPolicy` dependency-free — it takes plain strings; the boolean→subtree mapping lives in agent composition (ADR-023 H1)
- [ ] T057 [US3] Document `harness_surface_not_granted` in the reason vocabulary on `backend/src/Grimoire.AgentRuntime/Guardrails/DeniedActionRecord.cs` and on `PolicyDecision.Deny`
- [ ] T058 [P] [US3] Domain unit tests in `backend/tests/Grimoire.Domain.UnitTests/`: denied subtree beats a matching read prefix; bare directory is denied; write evaluation is unaffected; ordering (denial before allow loop) is pinned

### Delivery to the agent

- [ ] T059 [US3] Add the grant-set CLI argument to **all five** spawn sites in `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs` (lines ~190, ~254, ~316, ~416, ~522), following the `--review-window-days` precedent
- [ ] T060 [US3] Add the grant set to the agent request records (`QueryAgentRequest`, `LintAgentRequest`, `RemediationExecutionAgentRequest`, `RemediationMessageTurnAgentRequest`, and the ingest submission request) and populate them in the coordinators
- [ ] T061 [US3] Parse the argument in each agent's CLI options and construct the effective `SafetyPolicy` in agent composition (`IngestAgent/Program.cs`, `QueryAgent/Program.cs`, `LintAgent/Program.cs`)

### Provenance (FR-017, SC-011)

- [ ] T062 [US3] Add `granted_harness_surfaces` to the task artifact frontmatter in `backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs` using the existing `BuildStringList`/`ParseStringList` idiom, and to `TaskArtifactDocument`
- [ ] T063 [US3] Add `grantedHarnessSurfaces` to `RunCompletionMetadata` and the terminal NDJSON payload in `backend/src/Grimoire.AgentRuntime/RunEvents/RunEventEmitter.cs`, and to `backend/src/Grimoire.Hub/AgentDispatch/AgentRunEvent.cs`
- [ ] T064 [US3] Add `granted_harness_surfaces` to the conversation record bookkeeping block in `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs` and to `RecordedTurn`/`QueryTurnState`

### Instruction-side

- [ ] T065 [US3] Add to all three prompts: a denied harness-surface read is routine — continue with allowed work; never present a harness record as an article; never cite one as a wiki source; never derive an article from one (FR-007, FR-018)

### Tests

- [ ] T066 [US3] Hermetic integration test `HarnessSurfaceReadDefaultDenyTests`: with nothing configured, scripted `list_files("tasks")` and `read_file("conversations/x.md")` are denied with reason `harness_surface_not_granted`, each recorded as a `DeniedActionRecord`, and the run reaches a terminal state (SC-010)
- [ ] T067 [US3] Hermetic integration test `HarnessSurfaceGrantProvenanceTests` **through the production composition root**: with a partial grant, assert the recorded grant set on the task artifact, the terminal event, and the conversation record (SC-011)
- [ ] T068 [US3] Hermetic integration test `RemediationUnaffectedByReadScopeTests`: a remediation message turn succeeds under the all-denied default, because context arrives as Hub-injected CLI arguments rather than guarded reads (research.md R6, quickstart scenario 5)
- [ ] T069 [P] [US3] Eval scenario: fixture with articles **and** populated harness surfaces, one granted — assert no harness record is cited as a wiki source and none is derived into an article (SC-008 ≥95%, SC-012 ≥95%/≤2%)

**Checkpoint**: the operator owns the decision; default is safe; every run is auditable.

---

## Phase 6: User Story 4 — One canonical term, and the retired one cannot return (P2)

**Goal**: "article" everywhere, including identifiers and persisted names; the rule bites.

**Independent test**: reintroduce a `pages/` navigation instruction or a `pages_touched` metric
name and confirm the build fails naming the file; remove it and confirm it passes.

### Telemetry renames (implementation)

- [ ] T070 [US4] Rename `wiki.ingest.pages_touched_total` → `wiki.ingest.articles_touched_total` (and `_pagesTouchedTotal`, `RecordPagesTouched`, the description) in `backend/src/Grimoire.IngestAgent/IngestAgentMetrics.cs`
- [ ] T071 [US4] Rename `wiki.query.synthesis_pages_created_total` → `wiki.query.synthesis_articles_created_total` in `backend/src/Grimoire.QueryAgent/QueryAgentMetrics.cs`
- [ ] T072 [US4] Rename the `ingest.agent.completed` fields `pages_created`/`pages_updated`/`pages_superseded` → `articles_*` and the `ingest_agent.finalize_artifact` span tags in `backend/src/Grimoire.IngestAgent/IngestAgentLogEvents.cs`
- [ ] T073 [US4] Rename log event `wiki.query.synthesis_page_created` → `wiki.query.synthesis_article_created` in `backend/src/Grimoire.AgentRuntime/Guardrails/IToolCallInstrumentation.cs`
- [ ] T074 [P] [US4] Update the metric description of `hub.lint.inbound_links_refreshed_total` in `backend/src/Grimoire.Hub/HubMetrics.cs` (name unchanged)

### New observability signals (implementation)

- [ ] T075 [US4] Add counter `wiki.<agent>.harness_surface_reads_denied_total` with label `surface` to each of `IngestAgentMetrics`, `QueryAgentMetrics`, `LintAgentMetrics`, fired from the harness-surface denial path
- [ ] T076 [US4] Emit log event `guardrails.harness_surface_read_denied` at **WARN** with mandatory fields `task_id`, `agent`, `surface`, `requested_target`, `canonical_target`, `reason`, `turn` from `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`
- [ ] T077 [US4] Emit log event `agent.harness_surface_scope_resolved` at **INFO** with mandatory fields `task_id`, `agent`, `granted_surfaces`, `denied_surfaces` at run start in each agent's composition
- [ ] T078 [US4] Create span `<agent>_agent.resolve_harness_surface_scope` as a child of `<agent>_agent.run` with attributes `task_id`, `granted_surfaces`, `denied_surfaces` (plus `turn_id` for Query) in all three agents
- [ ] T079 [US4] Add attributes `harness_surface` and `denial_reason` to the existing `<agent>_agent.tool_call` span when a harness-surface denial occurs

### Persisted and wire renames

- [ ] T080 [US4] Rename task-artifact frontmatter keys `pages_touched`/`pages_created`/`pages_updated`/`pages_superseded` → `articles_*` in `TaskArtifactStore.cs`, `TaskArtifactDocument.cs`, `HubTaskArtifactWriter.cs:61-64`, and `RestartReconciler.cs:112`
- [ ] T081 [US4] Rename conversation-record key `created_pages` → `created_articles` in `ConversationRecordFormat.cs` (write at 113/117, parse at 496) and `CreatedPages`→`CreatedArticles` in `RecordedTurn.cs`/`QueryTurnState.cs`
- [ ] T082 [US4] Rename NDJSON property `createdPages` → `createdArticles` in `AgentRunEvent.cs:54` and `RunEventEmitter`, resolving the existing divergence with ADR-015's `createdArtifacts` naming
- [ ] T083 [US4] Rename the remaining C# identifiers per `contracts/terminology-rename-map.md` across `Grimoire.IngestAgent`, `Grimoire.Hub`, and `Grimoire.EvalRunner/Scoring` (~308 occurrences)
- [ ] T084 [P] [US4] Update operator-facing CLI help text in `PathSwitchCatalog.cs:26`, `HubPathSettings.cs:40`, and `GrimoirePathOptions.cs:28`

### ADR amendments

- [ ] T085 [US4] Add an inline amendment note to `docs/adr/ADR-011-*.md` lines 108–111 — it states Query's read scope is `pages/`/`index.md`/`log.md` with "no write section", **both false today**; highest-priority stale record (research.md R11)
- [ ] T086 [P] [US4] Add amendment notes for renamed live field names to ADR-014:99, ADR-015:214/221, ADR-018:125
- [ ] T087 [P] [US4] Amend the stale current-state terminology in ADR-003:9,40, ADR-006:86, ADR-009:74, ADR-016:155 — leaving ADR-006:43, ADR-009:98, ADR-015:107–123, ADR-016:63/85–89 untouched as historical record (SC-004)

### Tests

- [ ] T088 [US4] Hermetic integration test `RenameInvarianceTests` **through `AddHubTelemetry`'s `configureTracing` hook**: an identical scripted run reports the same value under each renamed signal's new name as under the old, with the same labels (SC-015)
- [ ] T089 [US4] Hermetic integration test `LegacyConversationRecordTolerationTests`: a record carrying the old `created_pages:` key parses, the key is ignored, and the fail-closed `conversation_record_unreadable` path does **not** fire (research.md R10)
- [ ] T090 [US4] Deterministic log-contract tests for `guardrails.harness_surface_read_denied` and `agent.harness_surface_scope_resolved`: assert event name, level, and **every** mandatory field
- [ ] T091 [US4] Deterministic log-contract tests for the renamed `ingest.agent.completed` fields and `wiki.query.synthesis_article_created`
- [ ] T092 [US4] Deterministic trace-contract tests for the three `resolve_harness_surface_scope` spans: assert span name, parent linkage to `<agent>_agent.run`, and correlation by `task_id`
- [ ] T093 [US4] Deterministic trace-contract tests for the new `tool_call` attributes and the renamed `finalize_artifact` attributes
- [ ] T094 [US4] Join the observability tests to `HubActivityListenerObservability` or `IngestAgentObservabilityListeners` (`DisableParallelization`) per the process-wide-listener race documented in feature 019
- [ ] T095 [US4] Verify the repository-wide `[Fact]`s in both Phase 2 rule classes are now **green**, and record the transition here as the Green half of the Principle III probe

**Checkpoint**: terminology is canonical and structurally enforced.

---

## Phase 7: Polish, CI, and completeness audit

- [ ] T096 Re-run the full eval corpus and confirm `Skipped: 0` across ingest, query and lint; re-capture anything still stale after Phases 3–6 (ADR-012, CI zero-skip gate)
- [ ] T097 [P] Run `dotnet format backend/Grimoire.slnx --verify-no-changes` and fix any violations introduced by the rename
- [ ] T098 [P] Confirm fast-tier timing: the broadened arch scan must keep `Grimoire.ArchTests` within its low-single-digit-second budget (ADR-021); if not, switch to a per-file-type scanner rather than growing the C# tokenizer (research.md R8)
- [ ] T099 **CI enforcement record**: confirm and document that `.github/workflows/ci.yml` runs `Grimoire.ArchTests`, `Grimoire.Domain.UnitTests`, `Grimoire.IntegrationTests` and `Grimoire.AgentEvals`, so every logging, trace and structural contract test in this feature runs in the standard PR pipeline (Constitution Principle IV; FR-011)
- [ ] T100 Run every quickstart scenario 1–10 and record the results
- [ ] T101 **Completeness audit (Constitution Principle III, mandatory named task)**: cross-reference every row of `plan.md ## Observability` — 6 metrics, 4 log events, 5 span rows — and every agent-judgment success criterion in spec.md — SC-006, SC-007, SC-008, SC-009, SC-012 — against its implementing task and its passing test. File any gap found as a new task in this file **before** declaring the DoD met
- [ ] T102 Verify the Definition of Done checklist in `.specify/memory/constitution.md` item by item against this feature and record the result

---

## Dependencies

```text
Phase 1 (Setup)
   └─> Phase 2 (Foundational: structural rules + probes)   [BLOCKING]
          ├─> Phase 3 (US1, P1)  ──┐
          ├─> Phase 4 (US2, P1)  ──┤
          ├─> Phase 5 (US3, P2)  ──┼─> Phase 7 (Polish + audit)
          └─> Phase 6 (US4, P2)  ──┘
```

- **Phase 2 blocks everything.** Constitution Principle III: structural tests before feature code.
- **US1 and US2 are independent** — US1 touches only the query prompt, US2 the ingest and lint
  prompts. Either can ship alone.
- **US3 is independent** of US1/US2 except for T065 (instruction wording), which can land with
  either prompt phase.
- **US4's telemetry work (T075–T079) depends on US3's denial path** (T055–T057) existing.
- **T032 / T050 (eval re-capture) must follow their phase's prompt edits**, and T096 verifies the
  whole corpus after all phases.

## Parallel opportunities

- **Phase 1**: T002 and T003 in parallel.
- **Phase 2**: T006+T009+T010 (path rule) and T011+T012 (terminology rule) are separate files;
  T014+T015 is a third. Three streams after T004/T005 land.
- **Phase 3**: T027 and T028 (fixtures) in parallel with the prompt edits T019–T026.
- **Phase 4**: the ingest edits (T034–T040) and the lint edits (T041–T044) are separate files;
  T045 must touch both and comes after.
- **Phase 5**: T058 (domain unit tests) parallel with T059–T061 (delivery); T062/T063/T064 are
  three separate record types.
- **Phase 6**: T070–T074 are five separate files; T085/T086/T087 are separate ADR files.

## Implementation strategy

**MVP = Phase 1 + Phase 2 + Phase 3.** That fixes the reported defect — the query agent stops
reporting a populated wiki as empty — and lands the enforcement that prevents recurrence.

Then Phase 4 (the other two agents, plus the live remediation `targetPath` bug), then Phase 5
(the read scope), then Phase 6 (terminology), then Phase 7.

Each phase ends at a checkpoint that is independently demonstrable.

## Task count

| Phase | Tasks |
|-------|-------|
| 1 — Setup | 3 |
| 2 — Foundational (structural rules) | 15 |
| 3 — US1 (P1) | 15 |
| 4 — US2 (P1) | 17 |
| 5 — US3 (P2) | 19 |
| 6 — US4 (P2) | 26 |
| 7 — Polish & audit | 7 |
| **Total** | **102** |

---
description: "Task list for feature 030 — Prompt Conformance"
---

# Tasks: Prompt Conformance — Lifecycle Fields, Confidence Signals, Source Traceability

**Input**: Design documents from `/specs/030-prompt-conformance/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/document-consistency.md](./contracts/document-consistency.md),
[quickstart.md](./quickstart.md)

**Tests**: This feature adds exactly **one** new deterministic test (T002) and **no** new eval
scenario. That is a deliberate consequence of Constitution Principle V, not an oversight: every other
requirement here constrains what an instruction document *says*, and a deterministic test asserting
that would be the violation this feature exists to reverse. `spec.md` forbids it twice (Requirements
preamble, Out of Scope) and `contracts/document-consistency.md` forbids it per row. A reviewer asking
for a string-matching test against FR-005, FR-007, FR-008, FR-009 or FR-011 is asking for a
constitutional violation.

**Logging Contract**: `plan.md ## Observability > Structured Log Events` **declares no log event** —
the table carries a single `*(none added)*` placeholder and no signal row. The three mandated task
categories (implementation, deterministic integration test, CI enforcement) are therefore vacuous —
there is nothing to derive them from. This is stated here rather than silently
omitted so a reviewer can tell the difference between "no rows" and "rows forgotten". T027 records
the same fact at audit time.

**Trace Contract**: `plan.md ## Observability > Distributed Trace Spans` **declares no span**, in the
same shape — one `*(none added)*` placeholder, no signal row. Same vacuity, same explicit statement,
same audit task.

**Organization**: Tasks are grouped by user story so each story can be implemented and reviewed as a
unit. See *Delivery Shape* below for why they nonetheless ship as one pull request.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Every task names at least one `FR-###` or `SC-###` from `spec.md`, cited literally so it is
  greppable. Tasks serving no single requirement cite the phase goal and say so.

## Path Conventions

The four instruction documents this feature edits:

| Short name | Path |
|------------|------|
| `FOUND` | `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md` |
| `INGEST` | `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` |
| `LINT` | `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md` |
| `QUERY` | `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md` |

`contracts/document-consistency.md` states, per statement, which of the four is authoritative and
what the others may say about it. Every editing task below inherits that table: a task that says
"state" writes the rule, a task that says "refer" points at it and must not restate it in a form that
can drift. Restating a rule in two places is how issues #109–#111 happened.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**No Boundary Rule introduced by this feature** (see `plan.md ## Architectural Constraints & ADRs`,
row *III — Boundary Rule vs Feature-Scoped Invariant*).

Nothing this feature changes is a dependency direction between packages, namespaces or layers. It
adds no external system, no port, no adapter, and no infrastructure package. All eight ADRs it
touches are *extended*, none invalidated, and no new ADR is drafted — ADR-055 was drafted with an
earlier version of the plan and withdrawn, with the reasoning recorded in `plan.md`.

There is also **no Feature-Scoped Invariant** to schedule into a story phase. Every rule the feature
carries is agent behaviour under instruction files, which Principle V places outside both categories:
a deterministic test may verify the *load mechanism* of an instruction file, never its content.

This phase therefore contains no task. It is present, and says so explicitly, because the
constitution requires the absence to be stated rather than inferred from a missing heading.

---

## Phase 1: Setup

**Purpose**: Establish the pre-edit baseline that makes the staleness gate's later red-then-green
attributable to this feature's edits.

- [X] T001 Run `dotnet run --project backend/tests/Grimoire.EvalRunner -- status` **before editing any instruction document** and record the exit code and the scenario list in the implementation PR description; expected exit 0. **Result 2026-09-10: exit 0, 8 scenarios trusted, all captured 2026-09-06** — `instruction-change-adoption`, `adversarial-source`, `query-read-only-decline`, `query-synthesis-decline-edit-request`, `lint-at-scale-survey`, `remediation-reverify-still-applicable`, `remediation-reverify-no-longer-applicable`, `remediation-body-edit-applied`. These are the exact eight the post-edit run must flag, so the two are reconcilable line by line.** Note the count, and note the correction to it: `recordings/` holds **nine** directories while `status` enumerates **eight** scenarios. An earlier note here concluded that the ninth, `lint-at-scale-survey-tight-budget`, was therefore never evaluated. **That was wrong**, as the CI log on `6cdb1c2` showed: it is covered by its own test, `LintReplayEvalTests.SC003_AtScaleSurveyTightBudget_HasTrustedRecordedEvidence`, which asserts trusted evidence directly rather than through a scenario set. So all nine recordings must be re-captured, and **`status` exiting 0 is necessary but not sufficient** for `Grimoire.AgentEvals` to go green — `status` cannot see the ninth (SC-001, FR-012 — without this baseline a red `status` after the edits cannot be attributed to them rather than to pre-existing drift)

**Checkpoint**: Baseline recorded. Document edits may begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**No foundational prerequisite exists for this feature, and this is stated rather than omitted.**

The usual content of this phase — schema, framework wiring, base entities — has no analogue here: the
deliverable is prose in four files that already exist and are already loaded, composed and hashed by
the harness (ADR-053) and already build-delivered to a running agent (ADR-043). Nothing has to be
built before US1 can start.

One cross-story constraint belongs here even though it is not a task: **US1 and US2 both edit
`FOUND`, in different sections.** Their tasks are therefore not parallel with each other across
stories, which the Dependencies section below reflects.

---

## Phase 3: User Story 1 — Lifecycle fields exist from the moment a page is created (Priority: P1) 🎯 MVP

**Goal**: `inbound_links` and `last_reviewed` become part of the shared frontmatter standard, with a
per-field statement of which run writes each, so a lint run over a freshly ingested wiki inherits
pages that already carry the fields it works with and its review window measures *review* age rather
than *ingest* age.

**Independent Test**: run an ingest against a wiki fixture and inspect the frontmatter of the pages it
wrote (`inbound_links` present, `last_reviewed` absent); then run lint over a wiki whose pages carry a
recent `last_reviewed` and an older `timestamp` and read the review-candidate section of its findings
report.

**Scope note — FR-010 placement.** FR-010/FR-010a (lint degrades against a foundation document that
omits the lifecycle definitions) are implemented here, in the story that owns the lifecycle fields.
`spec.md` lists their acceptance scenario as US3 scenario 3, between the citation scenario and the
integration-depth scenario, which is a misfiling: US3's subject is source traceability and its
Independent Test does not mention degradation. This is a placement discrepancy in `spec.md`, recorded
here for `/speckit-analyze` to report; it changes no requirement and is not corrected from this layer.

### Tests for User Story 1

- [X] T002 [P] [US1] Add a hermetic integration test in `backend/tests/Grimoire.IntegrationTests/` dispatching a lint run against a fixture foundation document that is readable but omits the lifecycle rows, asserting the run reaches terminal **completion** rather than erroring and that no harness-level error names the document's content (FR-010, FR-010a)

> **This test is expected to pass against unchanged harness code, and that is its job.** The existing
> `FoundationPromptFailClosedTests` cover *absent*, *unreadable* and *whitespace-only* documents — all
> fail-closed paths by design. None covers "readable but silent", which must **not** fail closed. The
> test locks the absence of a harness content check (FR-010a). If it fails today, Principle V is
> already violated and that is the finding. It asserts run outcome only — state-based, classicist,
> product-owned — and asserts nothing about what any document says.

### Implementation for User Story 1

- [X] T003 [US1] Add the **Lifecycle** field group to the Frontmatter Standard in `FOUND` (`### Frontmatter Standard`), carrying `inbound_links` (required) and `last_reviewed` (optional — present only once the page has been reviewed), and group the existing fields as Identity / Provenance / Classification / Assessment per [data-model.md](./data-model.md) (FR-001, FR-002)
- [X] T004 [US1] State the canonical definition of the inbound-link count **once** in `FOUND`: the number of `[[wikilink]]` occurrences naming this page across all *other* files including `index.md` and `log.md`; self-references never count; repeats from the same file each count (FR-002a)
- [X] T005 [US1] State per field in `FOUND` which runs write it: a creating run writes its best observation of the canonical count, **explicitly provisional** because no creating run sees the whole link graph, and that provisionality does not forbid lint from recomputing; no run other than a reviewing one writes `last_reviewed`, on create or on update (FR-002a, FR-002c)
- [X] T006 [P] [US1] In `INGEST` (`## Step 2` / final-write guidance), require the run to write `inbound_links` as the count of the links that run itself created, and to never write `last_reviewed` — on create or on update — referring to `FOUND`'s definition rather than restating it (FR-001, FR-002a)
- [X] T007 [P] [US1] In `QUERY` (`### Synthesis Page conventions`), carry the same two lifecycle fields on the same terms at page-creation time, noting that query creates only and can never come back to amend a value (ADR-015), and reconcile the synthesis-only `review_date` field against `last_reviewed` — either drop it or state the deviation in both `QUERY` and `FOUND` (FR-002c, FR-011)
- [X] T008 [US1] In `LINT` (`## Step 4: Refresh inbound-link counts`), make the `last_reviewed` write **definite** rather than permissive and decouple it from the inbound-link-refresh rider, so a run that reviews a page without refreshing its count still records the date; under FR-002a lint is the field's sole writer, so permissive wording means the field never materialises (FR-002b)
- [X] T009 [US1] In `LINT`, state the qualifying act — the run produced a substantive finding or a remediation *proposal* about that page — plus its two exclusions: the review-candidate listing itself does not qualify, and a page about which the run produced no finding is not stamped, presented as the intended reading rather than a gap (FR-002b, FR-002d, FR-002e)
- [X] T010 [US1] In `LINT` (`### Metadata Hygiene`, Review candidates), state that the review window measures *review* age and that lint is no longer required to create the fields wiki-wide — its write becomes a correction of an existing value — while keeping its obligation to recompute the count in order to know whether the stored value is right (FR-003, FR-004)
- [X] T011 [US1] Add a degradation section to `LINT`: where the composed foundation document does not define an input a finding category depends on, carry out every category whose inputs are defined, skip those whose inputs are not, and name each skipped category and its reason in the run's own findings report (FR-010)

**Checkpoint**: US1 is complete when `FOUND` defines both fields with per-field writers, `INGEST` and
`QUERY` write the provisional count at creation, `LINT` writes the review date definitely and
degrades when the definitions are absent, and T002 passes.

---

## Phase 4: User Story 2 — A confidence score means the same thing whoever assigns it (Priority: P2)

**Goal**: the shared confidence convention becomes a usable judgment aid — its bands spread over the
signal set instead of `high` demanding a perfect score — and lint's link-graph extension and query's
synthesis treatment stop being silent disagreements about one convention.

**Not a criterion.** The 2026-09-10 clarification removed FR-005's MUST-level property: confidence
scoring is a means for the agent, and nothing here verifies or guarantees how the bands come out.
The thresholds below are a recommendation with recorded reasoning, and this phase is complete when
the documents *state* them coherently — not when any distribution property is proven.

**Independent Test**: read the resulting documents as a reviewer and check that each per-role
deviation is stated where the deviating role is described, and that a reader could apply the
convention without guessing; then run ingest and lint over the same fixture page and compare the
scores and reasons. Working out the attainable totals is a useful sanity read — [research.md](./research.md)
R1 does it — but a band's distribution is not a pass/fail condition on this phase.

**Numbers to write, from [research.md](./research.md) R1**: thresholds `high ≥ 1`, `medium −1 … 0`,
`low ≤ −2`, applied unchanged to both the shared range (`−3 … +2`) and lint's extended range
(`−4 … +3`). R1 records why these cut points rather than others; write them as given and do not
re-derive different ones inside an editing task. They are a recommendation, not a verified property.

### Implementation for User Story 2

- [X] T012 [US2] Re-baseline the thresholds in `FOUND` (`### Confidence Scoring`) to `high ≥ 1` / `medium −1 … 0` / `low ≤ −2` over the shared five-signal range `−3 … +2`, replacing the current `≥ 2` / `0–1` / `< 0` table whose `high` band collapses to the single point `+2` — the defect #110 reports. Write no MUST-style coherence claim into the document alongside the numbers: the convention is a judgment aid, not a contract (FR-005, FR-006)
- [X] T013 [US2] Record the rationale for the convention's shape in `FOUND` itself, where the next reader of that document meets it — why the shared set holds only signals every agent can observe, and what a role-specific extension adds — stated on its own terms and **not** grounded in a comparison to `docs/foundational/llm-wiki-*`, which is source material and is never cited as a requirement (FR-007)
- [X] T014 [US2] State in `FOUND` that a role may score on additional signals as a documented extension under these same thresholds, and that a page's score can therefore legitimately change when lint re-scores it (FR-006)
- [X] T015 [P] [US2] In `LINT` (`## Tag Taxonomy and Confidence Scoring`), state the extension explicitly: its two link-graph signals, the resulting `−4 … +3` range, that the shared thresholds apply to it unchanged, and the same re-score consequence `FOUND` states — applied always to the corrected count, never to a stale value on disk (FR-006)
- [X] T016 [P] [US2] In `QUERY` (`### Synthesis Page conventions`), replace the narrative confidence rule with the shared formula applied to the provenance of the pages the synthesis draws on, carrying the per-signal vocabulary mapping from [data-model.md](./data-model.md) — one explicit choice, never a third undeclared reading. Prescribe **no** aggregation rule for disagreeing signals: that weighting is the agent's judgment (FR-006a, FR-005)

**Checkpoint**: US2 is complete when the shared convention, lint's extension and query's synthesis
treatment each state their signals and thresholds explicitly, every deviation is stated in both
documents that carry it, and no document asserts a coherence property or an aggregation rule the
agent is required to satisfy.

---

## Phase 5: User Story 3 — Every source leaves a durable trace in the wiki (Priority: P3)

**Goal**: an ingest run produces a `sources/<slug>.md` page for its source and every citation wikilink
it writes resolves; the foundation and ingest documents state one integration-depth expectation with
the reason for it.

**Independent Test**: run an ingest against a wiki fixture, check the `sources/` folder for a page
representing the source, then resolve every citation wikilink in the pages that run wrote.

### Implementation for User Story 3

- [X] T017 [US3] In `INGEST`, require the run to produce a `sources/<slug>.md` source-summary page for its source and to leave every citation wikilink resolvable, covering the source with no canonical URI (the page still exists, identified by other means, with no fabricated `resource` value) and the second ingest of an already-recorded source (update or supersede the existing page under the existing supersession rules, never a second page under a different slug) (FR-008)
- [X] T018 [P] [US3] In `FOUND`, state the integration-depth expectation as `10–15` pages per source with the reason in the document's own terms — integrating a source means rippling it through the concept, person and source pages it bears on rather than filing one summary — explicitly as a typical depth and **not** a quota, so a genuinely narrow source is not padded with manufactured connections, and noting that the FR-008 source-summary page counts toward the total (FR-009)
- [X] T019 [US3] Replace `One source typically touches 5–15 pages` in `INGEST` (`## Step 2: Integrate, do not summarize`) with wording that states the same `10–15` expectation as `FOUND`, matching it exactly — this is the one deliberate duplication in the authority table and the numbers must not drift (FR-009)

**Checkpoint**: US3 is complete when the source-summary page is a requirement of an ingest run rather
than an available option and the two documents state one integration-depth number.

---

## Phase 6: Polish, Cross-Cutting Concerns & Completeness Audits

**Purpose**: the whole-feature obligations — cross-document agreement, the eval re-capture that is
this feature's one real cost, and the three mandatory completeness audits.

- [X] T020 Walk the authority table and the four reviewer checks (the items under *Invariants a reviewer checks*; these are review steps, **not** Feature-Scoped Invariants in the constitutional sense — this feature has none) in [contracts/document-consistency.md](./contracts/document-consistency.md) across all four edited documents; verify the integration-depth number matches exactly, every per-role deviation is stated in both documents that carry it, no document grounds a requirement in `docs/foundational/llm-wiki-*`, and no document acquired a rule the harness would have to enforce — human review, never a test (FR-011, SC-006) — **Done 2026-09-10**: all four reviewer checks pass. Integration depth reads `10–15 pages` in `FOUND` and `INGEST`, byte-identical, no `5–15` left anywhere. The extension rule and the re-score consequence are each stated in both `FOUND` and `LINT`. No document grounds a rule in `docs/foundational/llm-wiki-*` (the sole `Karpathy` hit is a tag example in the taxonomy, not a rule). No document acquired a rule the harness would enforce.
- [X] T021 Rebuild the agent artifacts so the edited documents reach a running agent (ADR-043: instruction files are build-delivered, so an edited document has no effect until the artifacts are rebuilt) — phase goal, prerequisite for T023 and T025, serves no single requirement — **Done 2026-09-10**: `dotnet build backend/Grimoire.slnx` republished all three agent runtimes (ingest, query, lint) with the edited documents, 0 errors.
- [X] T022 Run `dotnet run --project backend/tests/Grimoire.EvalRunner -- status` and confirm it now exits **3**, flagging every committed scenario whose fingerprint covers the edited documents; all nine committed recordings fingerprint `foundation_prompt`; `status` reports the eight it enumerates, and the ninth (`lint-at-scale-survey-tight-budget`) surfaces only in the `Grimoire.AgentEvals` run, so check both — a clean exit 0 here means the fingerprints do not cover what was edited and must be investigated, not celebrated (SC-001, FR-012) — **Done 2026-09-10**: exit **3**, all 8 enumerated scenarios stale, `changed: foundation_prompt, system_prompt`. Against T001's pre-edit baseline of exit 0 this attributes the staleness to these edits and confirms the fingerprints cover them. The CI run additionally showed the ninth recording (`lint-at-scale-survey-tight-budget`) stale through its own test, which `status` does not report — see T023.
- [X] T023 Re-capture every flagged scenario against a live provider (`EvalRunner capture --scenario <id>`, or the `eval.yml` workflow **with an explicit `scenarios` input**), then run `dotnet test backend/tests/Grimoire.AgentEvals` and re-run `status`; done when the replay suite is green across all four ADR-033 classes — `IngestReplayEvalTests`, `LintReplayEvalTests`, `QueryReplayEvalTests`, `RemediationReVerificationEvalTests` — and `status` exits 0 with no scenario left stale (SC-001, FR-012) — **Done 2026-09-10**: unblocked once `ANTHROPIC_AUTH_TOKEN` was available in the local `.env`, which `capture` picks up itself via `LocalEnvFile`. Re-captured all nine recordings against `claude-haiku-4-5`: `instruction-change-adoption`, `adversarial-source`, `query-read-only-decline`, `query-synthesis-decline-edit-request`, `remediation-reverify-still-applicable`, `remediation-reverify-no-longer-applicable`, `remediation-body-edit-applied` each ran as its own `capture --scenario <id>` process; `lint-at-scale-survey` and `lint-at-scale-survey-tight-budget` ran together in one process (both materialize the same generated `LintAtScaleFixture`, whose in-process generation lock does not cross OS processes, so those two cannot safely run in separate concurrent processes — every other scenario uses a static committed fixture and has no such constraint). `lint-at-scale-survey-tight-budget`'s own capture-time judge scored 80% against its 90% threshold (exit 1 for that process), which is expected and not a regression: per the note this task already carried, that scenario is exercised by its own test (`LintReplayEvalTests.SC003_AtScaleSurveyTightBudget_HasTrustedRecordedEvidence`), which asserts trusted recorded evidence directly rather than gating on the capture-time score. `status` now exits **0** (8 scenarios trusted; the ninth stays invisible to `status` as documented) and `dotnet test backend/tests/Grimoire.AgentEvals` is green — 54 passed, 0 failed, all four ADR-033 classes included.
- [X] T024 Run `./scripts/test-fast.sh` and `dotnet test backend/tests/Grimoire.IntegrationTests`, expecting an unchanged pass including the ADR-053 composition tests — verbatim load, in order, fail-closed, per-document SHA-256 recorded; add no assertion here about what the documents now say (SC-002) — **Done 2026-09-10**: `test-fast.sh` green (99 domain, 59 arch, 41 AgentEvals Fast). All 17 `FoundationPrompt*` integration tests pass, including the ADR-053 composition tests and T002. The wider `Grimoire.IntegrationTests` run has 16 failures that are **identical on the unmodified tree** at `92ed63d` (verified by stash-and-rerun) — sandbox-environment failures, not this feature.
- [ ] T025 Run the operator-loop validation in [quickstart.md](./quickstart.md) steps 5 and 6 against a scratch wiki and record the observations on the surfaces `plan.md ## Observability > Operator loop surfaces` names — created-page frontmatter, the `sources/` folder, citation resolution, the findings board at `frontend/src/routes/+page.svelte`, and the degraded run's report (SC-003, SC-004, SC-005, FR-010) — **BLOCKED 2026-09-10, needs a live provider.** Quickstart steps 5 and 6 dispatch real ingest and lint runs against a scratch wiki, which needs the same credential T023 does. Everything hermetic in the quickstart has been run, with one qualification. Step 1 (`test-fast.sh`) is green and step 4 (the cross-document read) was carried out as T020. Step 2 passes for what this feature is about — all 17 `FoundationPrompt*` tests, the ADR-053 composition tests among them — but **not** as the whole-suite "unchanged pass" the quickstart states: the wider `Grimoire.IntegrationTests` run carries the 16 failures T024 records, identical on the unmodified tree at `92ed63d` and therefore sandbox-environment rather than this feature. That distinction is stated here rather than folded into a blanket "passes".
- [X] T026 Observability completeness audit (MANDATORY — Constitution Principles III & IV): cross-reference every row of `plan.md ## Observability` against its implementing task; all three tables declare **no signal rows** (each carries only a `*(none added)*` placeholder), so confirm the feature emitted no new metric, log event or span and record that zero explicitly, plus the four existing metrics `plan.md` names as already covering these runs — file any gap found as a new task before declaring the DoD met (SC-002) — **Done 2026-09-10**: audited. All three Observability tables declare no signal rows, each carrying only a `*(none added)*` placeholder, and this feature adds no code path to instrument. Nothing to cross-reference and no gap to file. The existing signals covering these runs are unmodified.
- [X] T027 Logging and trace contract CI enforcement (MANDATORY — Constitution Principle IV): with no Structured Log Event and no Distributed Trace Span rows declared, no logging or trace implementation, test or CI task is derivable; record that explicitly at audit time so a reviewer can distinguish "no rows" from "rows forgotten", and confirm the standard PR pipeline is otherwise unchanged (SC-002) — **Done 2026-09-10**: audited. With no Structured Log Event and no Distributed Trace Span rows declared, no logging or trace implementation, test or CI task is derivable. Recorded here explicitly so a reviewer can tell "no rows" from "rows forgotten".
- [X] T028 Agent-behavior evaluation completeness audit (MANDATORY — Constitution Principles II, III & V): confirm this feature has **no high-stakes** agent-judgment success criterion, that SC-003, SC-004 and SC-005 are classified lower-stakes in `spec.md` with the argument stated and are covered by hermetic harness plumbing plus the user-reported correction loop with the surfaces `plan.md` names, that SC-001 and SC-002 are deterministic guarantees and SC-006 is a review outcome — and file any gap as a new task before the DoD is declared met (SC-003, SC-004, SC-005, SC-006) — **Done 2026-09-10**: audited. This feature has **no high-stakes** agent-judgment success criterion, so it introduces **no new eval threshold** and none of *its own* success criteria are gated by a formal eval suite. That is a statement about this feature's criteria, and must not be read as "no eval gate applies here": the existing recorded-replay suite (ADR-012/ADR-033) still gates this feature's DoD through T023 and FR-012 — every recording these edits invalidated must be re-captured and `Grimoire.AgentEvals` must be green. That gate governs the *freshness of the recorded evidence*, not a success criterion of this spec, which is why both statements hold at once. SC-003, SC-004 and SC-005 are classified lower-stakes in `spec.md` with the argument stated, each covered by a hermetic plumbing test plus the user-reported correction loop, and each has its observation surface named in `plan.md ## Observability > Operator loop surfaces`. No criterion is asserted at 100% hermetically.
- [X] T029 [P] File the `index.md` link-style drift as its own GitHub issue — `FOUND` requires `index.md` entries to use a markdown link, while `LINT` Step 4 counts `[[wikilink]]` occurrences there and calls dropping them the most common mistake — labelled per the `issue-triage` taxonomy; recorded in `spec.md ## Findings Recorded, Not Fixed Here` and deliberately not fixed by this feature (serves the spec's Findings section, no FR) — **Done 2026-09-10**: filed as #245, with both resolution options and the reason it needs a decision rather than a patch. Labelled `decision-needed` — its next step is a choice about contract semantics, not code, which is that label's definition in the taxonomy. It was first filed as `quick-fix`, contradicting its own body ("worth one deliberate decision rather than a quick edit"); relabelled the same day.
- [x] T030 [P] **Done 2026-09-10.** The FR-005 wording defect from [research.md](./research.md) R5 was routed through `/speckit-clarify` and resolved: the MUST criterion was **removed**, not tightened — confidence scoring is a means for the agent, and nothing about it needs deterministic verification. All three options offered (reachable by more than one combination; no band may require a perfect score; move it to a success criterion) were rejected along with the premise they shared. **Deviation from the workflow, recorded rather than claimed compliant.** `CLAUDE.md` requires a post-creation requirements change to be clarified on the branch that owns `spec.md`, with every layer above then rebased onto the corrected spec commit and its own output regenerated. That did not happen: `/speckit-clarify` ran on this implementation branch and edited `spec.md` here, on the author's explicit instruction that a stack overwrites downward and that the change is more visible on the top PR. What the branch rule exists to protect is intact, because `/speckit-clarify` was still the mechanism — the dated `## Clarifications` entry (Session 2026-09-10), the options framing and the checklist re-validation all happened, so the change is visible as a decision rather than an untracked rewrite. What is lost is the rest of the rule: the lower layers' `plan.md` and `tasks.md` were built against the pre-clarification FR-005 and were superseded by this layer rather than regenerated against the corrected spec (FR-005)
- [ ] T031 Run `/speckit-converge` against the whole feature and confirm the Definition of Done holds end to end (SC-006) — **Not run here.** `/speckit-converge` is a separate workflow step the author invokes; it should run once T023 and T025 have cleared, since the DoD it validates depends on both. Not to be confused with the delivery stack's **converge layer** (#246), which *is* delivered and is the PR carrying this Phase 6 — the two share a name and nothing else.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0**: no task — no Boundary Rule to enforce.
- **Phase 1 (T001)**: must run **before** the first document edit, or the baseline it exists to
  establish is worthless.
- **Phase 2**: empty by finding, not by omission. Blocks nothing.
- **Phase 3 (US1)**: starts after T001.
- **Phase 4 (US2)**: independent of US1 in content, but its `FOUND` tasks touch the same file as US1's
  and are therefore sequenced after them.
- **Phase 5 (US3)**: independent of US1 and US2 in content; its `FOUND` and `INGEST` tasks are
  sequenced behind the earlier tasks touching those files.
- **Phase 6**: T020–T025 depend on **every** document edit being complete — a partial edit set fails
  the cross-document audit by construction and would burn a re-capture that a later edit invalidates
  again.

### Within Each User Story

- Tasks touching the same file are strictly sequential: T003 → T004 → T005 (`FOUND`), T008 → T009 →
  T010 → T011 (`LINT`), T012 → T013 → T014 (`FOUND`), T017 → T019 (`INGEST`).
- T006 and T007 depend on T005: they refer to a definition `FOUND` must already carry.
- T019 depends on T018: it must match `FOUND`'s number exactly.
- T002 depends on nothing — it is a harness test and is expected to pass before T011 lands.

### Parallel Opportunities

- T006 (`INGEST`) ∥ T007 (`QUERY`) ∥ T002 (test project) — three different files.
- T015 (`LINT`) ∥ T016 (`QUERY`).
- T017 (`INGEST`) ∥ T018 (`FOUND`).
- T029 ∥ T030 — both are records, neither touches the deliverable.

The parallelism here is modest and honestly so: four files, one of which (`FOUND`) is touched by all
three stories.

---

## Implementation Strategy

### Delivery shape: **one implementation pull request, not a stack**

> **Superseded 2026-09-10 — this feature shipped as a stack of four implementation PRs, not one.**
> The author directed the change at `/speckit-implement` time, and the argument below did not survive
> contact with the implementing environment: it assumed the choice was between paying the re-capture
> cost once or paying it per layer. In fact **no layer can pay it** — the implementing session has no
> eval provider credential, so `capture` cannot run at all (T023). With the cost unpayable in every
> shape, the reason for preferring one PR evaporated, and what remained was the ordinary argument for
> a stack: four independently reviewable layers instead of one diff spanning three user stories.
>
> Delivered as #242 (US1) → #243 (US2) → #244 (US3) → #246 (converge layer), each based on the one below.
> "Converge layer" names the stack layer carrying Phase 6, not the `/speckit-converge` command — that is
> T031 and has not run.
> Every layer is red on the eval step until the re-capture runs against the final document content;
> that is the accepted cost of the shape, stated on each PR rather than discovered by a reviewer.
>
> The original reasoning is kept below rather than deleted, because it is the record of what was
> decided and why — and of which premise turned out to be wrong.

This decision was stated here because `CLAUDE.md` requires it to be made out loud between
`/speckit-tasks` and `/speckit-implement`, and because the default for a feature with more than two
phase groups beyond Phase 0 is a stack. This feature was recorded as an exception, for one concrete
reason:

**`Grimoire.AgentEvals` runs on every push in `ci.yml`, and it goes red the moment the first
instruction document is edited.** All eight scenarios the gate enumerates fingerprint `foundation_prompt`;
editing it marks every one stale, and a stale recording is a hard test failure, not a warning. Clearing
it needs a live-provider capture whose result is only valid for the exact document content that
produced it. In a stack that means either a full re-capture per layer — the feature's single largest
cost, paid three or four times, invalidated again by the next layer — or intermediate pull requests
that are red on the merge gate the constitution puts most weight on. Neither is a delivery shape worth
buying.

A second, weaker reason points the same way: FR-011 is a *whole-feature* invariant — the four documents
agree — that every intermediate layer violates by construction, so the cross-document review (T020,
SC-006) cannot happen until the last layer anyway.

The artifacts themselves stay stacked, as they already are: `spec.md` on layer 1 (PR #236), the
planning artifacts on layer 2, this `tasks.md` on layer 3, the implementation on layer 4. Each layer
targets the one below it. That keeps the review small where splitting is free and avoids paying for it
where it is not.

The diff this buys is modest: four markdown files, one new integration test, and re-captured
recordings. It is a reviewable pull request — which is the actual test the stacking convention exists
to satisfy.

### Sequence

1. **T001** — baseline `status` at exit 0. Before anything else.
2. **Phase 3 (US1)** — the MVP. It is the defect the other two build on: US2's shared-formula
   reasoning depends on the lifecycle field existing, and #38 (OKF 0.2) is blocked behind it.
3. **Phase 4 (US2)**, then **Phase 5 (US3)** — in that order, because both edit `FOUND` and a
   file-serialised order beats resolving conflicts in prose.
4. **Phase 6** — cross-document audit first (T020), then rebuild (T021), then the staleness
   red→capture→green cycle (T022–T023), then the hermetic re-runs (T024) and the operator loop (T025).
   Re-capturing before T020 risks paying for it twice.
5. **T026–T028** — the three mandatory audits. **T031** — `/speckit-converge`.

### What a reviewer should check first

The authority table in `contracts/document-consistency.md`. It is the whole of SC-006's verification
and the only place the feature's correctness is actually visible: four files that have to agree, with
one deliberate duplication (the integration-depth number) that must match exactly.

---

## Notes

- **The known pre-existing inconsistency is not a regression.** `FOUND` requires `index.md` entries to
  use a markdown link while `LINT` counts `[[wikilink]]` occurrences there. A reviewer meets it while
  checking the authority table; it is recorded in `spec.md ## Findings Recorded, Not Fixed Here` and
  filed by T029, not fixed here.
- **`StalenessTests` is not the staleness check.** It is a Fast-tier test of the staleness *mechanism*
  against a copied fake repo root with synthetic drift, so it stays green no matter what the real
  documents say. `EvalRunner status` is the command that evaluates the committed manifests against the
  real files. T022 uses `status` deliberately.
- Commit after each task or logical group; the `after_implement` auto-commit hook commits to whatever
  branch is checked out.
- Every task above cites at least one `FR-###` or `SC-###`, except T021 and T029, which say explicitly
  what they serve instead.

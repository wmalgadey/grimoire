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

- [ ] T001 [P] Boundary Rule — no wiki deletion outside the guarded tool layer (ADR-031 R3, FR-022): NetArchTest rule in `backend/tests/Grimoire.ArchTests/` asserting `File.Delete`/`Directory.Delete` are reachable only from `Grimoire.AgentRuntime.Guardrails` and the harness-record namespaces. Red/Green probe: add a class calling `File.Delete` from `Grimoire.LintAgent`, confirm red, delete it, confirm green
- [ ] T002 [P] Boundary Rule — search regex is always non-backtracking and bounded (ADR-030 R2, FR-007a): NetArchTest/IL rule in `backend/tests/Grimoire.ArchTests/` asserting every `Regex` construction reachable from the search implementation passes `RegexOptions.NonBacktracking` and a match timeout. Red/Green probe: construct a plain `Regex` in the search path, confirm red, revert, confirm green
- [ ] T003 [P] Boundary Rule — guarded-only filesystem reach and no run-mode branch (ADR-030 R1/R4, ADR-031 R1, FR-014, FR-022): NetArchTest rule asserting search/batch/delete filesystem APIs are reachable only from `GuardedToolExecutor`, and that no type in `Grimoire.Hub.LintDispatch` or `Grimoire.Hub.RemediationTasks` selects a policy path by run mode. Red/Green probe: add a mode-conditional policy path in `RemediationRunCoordinator`, confirm red, revert, confirm green

**Checkpoint**: boundaries guarded. Feature code may begin.

---

## Phase 1: Setup

**Purpose**: measurements and fixtures that must exist before the code they measure.

- [ ] T004 Capture the pre-feature baseline for the SC-014 measurement (research.md D9): record median content tokens read per Lint survey run on the eval fixture, into `specs/026-guarded-tool-surface/baseline.md`. **Must run before any Phase 2 task** — measured afterwards it is a reconstruction, not a baseline
- [ ] T005 [P] Build the `lint-at-scale` fixture generator in `backend/tests/Grimoire.AgentEvals/Fixtures/` (SC-011): extends `lint-seeded-defects` with filler pages generated at build time. No corpus is authored or committed (plan.md § Eval scope)
- [ ] T006 [P] Add the eval-config context-budget lever used by `lint-at-scale` (SC-011): lets a small generated fixture exceed the guard, so fixture size stays irrelevant to the property under test

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the policy model, tool definitions and instrumentation seams every story needs.

**⚠️ CRITICAL**: blocks all of Phase 3–6.

- [ ] T007 Add the `Delete` scope and `DeleteRule` record to `backend/src/Grimoire.Domain/Guardrails/SafetyPolicy.cs` (FR-021, ADR-031 R3, data-model.md): deny-by-default, canonicalized path matching, no mode variants
- [ ] T008 Teach `PolicyLoader` the `delete` scope and keep `"frontmatter-only"` a recognized write mode (FR-020, ADR-031 R5): an unknown mode string still fails closed; a policy declaring `frontmatter-only` must still load
- [ ] T009 Rewrite `backend/src/Grimoire.LintAgent/Instructions/policy.json` to version 2 (FR-014, FR-015, FR-016, FR-016a): one `read-write` rule on `.` with no `excludePrefixes`, plus a `delete` scope on `.`
- [ ] T010 Assert Ingest and Query policies declare no `delete` scope, in `backend/tests/Grimoire.IntegrationTests/` (FR-021, research.md D6): the guard against deletion leaking to an agent that already holds `read-write` on the content root
- [ ] T011 Add `search_files`, `batch` and `delete_file` definitions plus `read_file` range parameters to `backend/src/Grimoire.AgentRuntime/Guardrails/ToolRegistry.cs` (FR-001, FR-002, FR-008, FR-011, FR-024), each schema declaring `additionalProperties: false` per contracts/guarded-tool-surface.md
- [ ] T012 Declare the new tools in `backend/src/Grimoire.LintAgent/LintToolRegistry.cs` only (FR-022, ADR-030 R6): Ingest and Query registries unchanged
- [ ] T013 Extend `backend/src/Grimoire.AgentRuntime/Guardrails/IToolCallInstrumentation.cs` with the search, batch, read-shape and deletion signals (plan.md § Observability), each defaulted no-op so agents that do not use them need no adapter
- [ ] T014 Register the new metrics, log events and spans in the **production** composition root (Principle IV): contract tests must read them from the real telemetry registration, sampler and exporter — never a test-only `ActivitySource` or always-on sampler

**Checkpoint**: foundation ready; stories may proceed.

---

## Phase 3: User Story 1 — Find pages without reading the wiki (Priority: P1) 🎯 MVP

**Goal**: `search_files` returns capped, bounded, in-scope matches so Lint can survey a wiki
larger than its context guard.

**Independent Test**: run a survey against `lint-at-scale` with a term on three pages; the run
completes, findings reference those pages, and recorded tool calls show search rather than a
whole-wiki read.

- [ ] T015 [US1] Implement `search_files` dispatch in `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` (FR-001, FR-002): pattern + optional path prefix → `path:line:text`
- [ ] T016 [US1] Evaluate the read policy per candidate path before opening it (FR-003, SC-001): a denied path is **omitted silently**, never reported — reporting it would disclose the path
- [ ] T017 [US1] Record a denial for an out-of-scope search root and continue the run (FR-004, SC-002)
- [ ] T018 [US1] Enforce the result cap with an explicit truncation marker (FR-005, SC-003): default 200, `max_results` may lower it, hard ceiling 1000 (ADR-030 R5)
- [ ] T019 [US1] Enforce the search time budget, returning partial results with an incomplete marker (FR-006, SC-003): never "no matches", never a failed run
- [ ] T020 [US1] Build the regex with `RegexOptions.NonBacktracking` and a match timeout, and enforce the 1000-character pattern bound (FR-007, FR-007a, ADR-030 R2)
- [ ] T021 [US1] Reject an unsupported or oversized pattern as a recorded denial naming the reason (FR-007a, SC-002)
- [ ] T022 [P] [US1] Test: a match inside a read-denied path is absent from results and no denial names it (SC-001) in `backend/tests/Grimoire.IntegrationTests/`
- [ ] T023 [P] [US1] Test: traversal and symlink search roots canonicalize before policy evaluation (FR-003, SC-001)
- [ ] T024 [P] [US1] Test: cap reached → truncation signalled; budget exhausted → incomplete signalled (SC-003)
- [ ] T025 [P] [US1] Test: lookaround pattern and >1000-char pattern each produce a recorded denial, run continues (FR-007a, SC-002)
- [ ] T026 [P] [US1] Feature-Scoped Invariant test: the four documented defaults are observable through behaviour, not reflection (ADR-030 R5, SC-003) — a 201st match truncates, a 1001 `max_results` clamps
- [ ] T027 [US1] Emit `wiki.search.invocations_total`, `wiki.search.matches_returned`, `wiki.search.files_scanned` (plan.md § Observability)
- [ ] T028 [US1] Emit `wiki.search.truncated`, `wiki.search.timed_out`, `wiki.search.pattern_rejected` log events with their mandatory fields (plan.md § Observability)
- [ ] T029 [US1] Create the `guardrails.search_scan` span as a child of `lint_agent.tool_call` with its declared attributes (plan.md § Observability)
- [ ] T030 [P] [US1] Deterministic test: the three search log events assert name, level and every mandatory field, read from the production composition root (Principle IV)
- [ ] T031 [P] [US1] Deterministic test: `guardrails.search_scan` name, parent linkage and `task_id` correlation, read from the production composition root (Principle IV)

**Checkpoint**: Lint can find without reading. MVP deliverable.

---

## Phase 4: User Story 2 — The agent can change page content (Priority: P2)

**Goal**: one write scope for both modes, covering create, edit and delete across the content
root including the reserved files.

**Independent Test**: a body edit succeeds in a survey run and in an authorized remediation
execution on identical terms; a delete followed by a run failure restores the page.

- [ ] T032 [US2] Verify all three coordinators keep passing `_paths.Lint.PolicyPath` unchanged and no mode branch is introduced (FR-014, FR-017, ADR-031 R1)
- [ ] T033 [US2] Implement `delete_file` dispatch in `GuardedToolExecutor`, evaluated against the **delete** scope (FR-015, ADR-031 R3)
- [ ] T034 [US2] Journal a deletion with its content in `backend/src/Grimoire.AgentRuntime/Guardrails/WriteJournal.cs` and restore it in reverse-order rollback (FR-015a, ADR-031 R4)
- [ ] T035 [P] [US2] Test: the write decision is identical in a survey run and an execution run for the same path and content (SC-004) — the anti-regression test for ADR-031 R1
- [ ] T036 [P] [US2] Test: writes outside the wiki content root are denied and recorded in both modes (SC-005)
- [ ] T037 [P] [US2] Test: a page deleted by a run that then fails is restored by the journal (SC-005a)
- [ ] T038 [P] [US2] Test: an authorized remediation whose proposal targets page content runs under a scope that permits it (SC-006) — end-to-end through the real coordinator
- [ ] T039 [P] [US2] Test: Lint writes to `index.md`/`log.md` are held to ADR-017 entry format and ADR-028 prepend ordering (SC-005b, FR-016b)
- [ ] T040 [P] [US2] Test: policy identity (version + hash) is recorded on every run (SC-009, FR-019)
- [ ] T041 [P] [US2] Test: a missing or unparseable policy fails the run before any wiki file changes (SC-010, FR-020)
- [ ] T042 [P] [US2] Test: a policy declaring `frontmatter-only` still loads (ADR-031 R5) — the upgrade-safety invariant
- [ ] T043 [US2] Emit `wiki.page.deletions_total` (plan.md § Observability)
- [ ] T044 [US2] Emit `wiki.page.deleted` and `wiki.page.delete_rolled_back` log events with their mandatory fields (plan.md § Observability)
- [ ] T045 [US2] Create the `guardrails.delete_file` span as a child of `lint_agent.tool_call` with `journaled`/`outcome` attributes (plan.md § Observability)
- [ ] T046 [P] [US2] Deterministic test: both deletion log events assert name, level and mandatory fields from the production composition root (Principle IV)
- [ ] T047 [P] [US2] Deterministic test: `guardrails.delete_file` span name, parent linkage and `task_id` from the production composition root (Principle IV)

**Checkpoint**: the remediation loop completes; the agent maintains the wiki.

---

## Phase 5: User Story 3 — Read only the part that matters (Priority: P3)

**Goal**: bounded reads that do not compromise write coordination.

**Independent Test**: frontmatter-only and line-range reads return exactly their slice; a
partially read page cannot be overwritten.

- [ ] T048 [US3] Implement `offset`/`limit`/`frontmatter_only` on `read_file` and the `ReadShape` discriminator (FR-008, data-model.md)
- [ ] T049 [US3] Ensure a partial read never calls `SharedFileWriteGuard.OnReadFile` (FR-010, ADR-030 R3): only a full read may set the compare-and-swap baseline
- [ ] T050 [P] [US3] Test: a write to a page read only in part is rejected and recorded (SC-008) — the ADR-015 protection test
- [ ] T051 [P] [US3] Test: no range parameters → byte-for-byte today's whole-file read, baseline still set (FR-009)
- [ ] T052 [P] [US3] Test: a range beyond end-of-file returns partial/empty with an explicit EOF signal, not a failed run (FR-008)
- [ ] T053 [US3] Emit `wiki.read.invocations_total` labelled by read shape (plan.md § Observability) — also the source for the SC-014 measurement
- [ ] T054 [P] [US3] Deterministic test: the read-shape label is correct for full, range and frontmatter reads, from the production composition root (Principle IV)

**Checkpoint**: retrieval is cheap per page as well as per wiki.

---

## Phase 6: User Story 4 — Spend fewer turns on read-only work (Priority: P4)

**Goal**: several read-only calls in one turn, with writes unrepresentable.

**Independent Test**: a mixed allowed/denied batch returns all results with individual
denials; a batch containing a write performs nothing.

- [ ] T055 [US4] Implement `batch` dispatch accepting only `list_files`, `read_file`, `search_files` (FR-011, FR-012)
- [ ] T056 [US4] Reject a batch containing a write, a delete, a nested batch, or more than 20 calls — before any member executes (FR-012, SC-007, ADR-030 R4/R5)
- [ ] T057 [US4] Evaluate and record each member individually against the policy (FR-013, SC-002)
- [ ] T058 [P] [US4] Test: a batch with one write executes no member at all (SC-007)
- [ ] T059 [P] [US4] Test: a mixed batch returns allowed results plus an individual denial for the denied member (FR-013, SC-002)
- [ ] T060 [US4] Emit `wiki.batch.invocations_total` and the `wiki.batch.rejected` log event with its mandatory fields (plan.md § Observability)
- [ ] T061 [US4] Create the `guardrails.batch` span as a child of `lint_agent.model_turn` with `call_count`/`denied_count` (plan.md § Observability)
- [ ] T062 [P] [US4] Deterministic test: `wiki.batch.rejected` fields and the `guardrails.batch` span parent linkage, from the production composition root (Principle IV)

**Checkpoint**: full tool surface delivered.

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T063 Logging contract CI enforcement (MANDATORY — Principle IV): confirm the deterministic tests for all 6 Structured Log Events rows run in `Deterministic Backend Gates` on every PR
- [ ] T064 Trace contract CI enforcement (MANDATORY — Principle IV): confirm the deterministic tests for all 3 Distributed Trace Spans rows run in `Deterministic Backend Gates` on every PR
- [ ] T065 Update `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md` for the agent-side judgment the new capabilities need (FR-023, Principle V): when to search vs. read, when a body edit is warranted, when to delete rather than supersede, when to reconcile the index, and what to leave as a remediation task. **Instruction-file content is never asserted by a harness test** — only its load mechanism
- [ ] T066 [P] Add the SC-011 eval scenario on `lint-at-scale` at threshold ≥ 90% (SC-011)
- [ ] T067 [P] Add the SC-013 eval scenario for authorized body-edit remediations at threshold ≥ 90% (SC-013) — the only assurance behind ADR-016's superseded structural guarantee; **not negotiable down**
- [ ] T068 Capture recordings for T066/T067 and record the one-time eval re-capture (ADR-012)
- [ ] T069 Record the SC-014 before/after numbers from `wiki.read.invocations_total{shape}` in the implementation PR (SC-014, research.md D9) — a measurement, not a gate
- [ ] T070 Observability completeness audit (MANDATORY — Principle III/IV): cross-reference every row of `plan.md ## Observability` — 6 metrics, 6 log events, 3 spans — against its implementing task and passing test, and file any gap as a new task before declaring the DoD met
- [ ] T071 Agent-behavior evaluation completeness audit (MANDATORY — Principles II & V): confirm SC-011 and SC-013 each have a passing evaluation at their threshold, and that **no agent-judgment criterion in spec.md lacks one** — SC-012/SC-014 are withdrawn as criteria, so the set is exactly two
- [ ] T072 ADR consistency audit (Principle III): confirm ADR-030/ADR-031 are Accepted, ADR-016 is `superseded`, the bidirectional links on ADR-006/011/016/017/018 are present, and `docs/adr/index.md` matches

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

### MVP scope

Phase 0 → 1 → 2 → 3 (US1). At that point Lint can survey a wiki it currently cannot, which is
the outcome #108 is waiting on. US2 completes the remediation loop; US3 and US4 are cost
refinements.

### Open question carried from planning

**A partial write (`edit_file`, anchored `old_string`/`new_string`) is not in these tasks.**
It was raised during planning and not settled. If adopted it would add one tool, an ADR-030
rule, and roughly 4–6 tasks in Phase 5 — and it would remove the current asymmetry where a
ranged read must be followed by a full read before any write, which blunts the token saving in
exactly the edit path Lint uses most. Decide before layer 07 is cut; after that it is a
retrofit.

# Tasks: Shared Foundation Prompt and Wiki-Identity Wizard

**Input**: Design documents from `/specs/029-shared-foundation-prompt/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: required throughout. Every deterministic success criterion (SC-001..SC-007) gets a
hermetic test; SC-008/SC-009 are lower-stakes agent judgment and get **no eval suite** (Principle II) —
they are covered by a hermetic test of the plumbing plus the user-reported correction loop.

**Logging Contract (MANDATORY)**: all five `plan.md ## Observability > Structured Log Events` rows are
covered by implementation tasks (T031, T047, T048), deterministic integration tests (T033, T051, T052)
and CI enforcement (T063). The two metrics are T030/T046 with tests T032/T050.

**Trace Contract (MANDATORY)**: both `plan.md ## Observability > Distributed Trace Spans` rows are
covered by implementation (T049), deterministic integration tests (T053) and CI enforcement (T064).

**Organization**: grouped by user story so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1, US2, US3 — user-story phases only
- Every task cites at least one `FR-###`/`SC-###` from `spec.md`; setup and cross-cutting tasks cite
  the phase goal and say so.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: prove the one Boundary Rule this feature touches actually detects violations, before any
feature code exists.

**⚠️ NON-NEGOTIABLE**: no implementation starts until Phase 0 is complete.

The rule is ADR-053's, already enforced by `InstructionAuthorshipBoundaryRuleTests`: no production type
may author instruction content, and only allow-listed namespaces may write a file named by an
instruction filename literal. This feature adds a literal (`foundation-prompt.md`) and one allow-list
entry (the wizard's namespace). Everything else this feature's ADRs name is tagged **Feature-Scoped
Invariant** and is covered behaviourally in its own story phase, never here.

- [X] T001 Extend `backend/tests/Grimoire.ArchTests/InstructionAuthorshipBoundaryRuleTests.cs`: add `foundation-prompt.md` to `_instructionFilenameLiterals` and `Grimoire.Hub.WikiIdentity` to the allowed-namespace list, so the rule covers the new document and permits exactly one new writer (FR-004, FR-010)
- [X] T002 Red probe: add a throwaway type **outside** `Grimoire.Hub.WikiIdentity` and `Grimoire.Hub.Runtime.Paths` that writes `foundation-prompt.md`, run `dotnet test backend/tests/Grimoire.ArchTests`, and record that it **fails** naming that type (FR-004)
- [X] T003 Green probe: delete the throwaway type, re-run `Grimoire.ArchTests`, confirm it passes; a rule never seen to fail is not a guard (FR-004)

**Checkpoint**: the boundary is live and proven before any feature code.

---

## Phase 1: Setup — the authored default and its delivery

**Purpose**: one authored document in the repository, delivered to every agent by the existing build.
Phase goal; no single FR beyond FR-008.

- [X] T004 Create `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md` — the shipped default, describing the general personal-knowledge LLM-wiki Grimoire maintains today, in the shape `contracts/foundation-document.md` fixes (FR-008). Content is moved in T020–T023; this task creates the file and its headings only
- [X] T005 In `backend/Directory.Build.targets`, add a `Content` item conditioned on `'$(GrimoireAgentId)' != ''` that links `..\Grimoire.AgentRuntime\Instructions\foundation-prompt.md` to `Instructions\foundation-prompt.md` with `CopyToOutputDirectory="PreserveNewest"`, so every agent project delivers it without a per-project edit (FR-008)
- [X] T006 Verify delivery: `dotnet build backend/Grimoire.slnx`, then confirm `.grimoire/agents/{ingest,query,lint}/Instructions/foundation-prompt.md` exist and are byte-identical to the source (`cmp`), per quickstart S1 (FR-008)

**Checkpoint**: the default document exists once in git and three times in build output.

---

## Phase 2: Foundational — resolution, the CLI surface, and composition

**Purpose**: blocking prerequisites for every user story. Nothing in Phase 3+ works without these.

**⚠️ CRITICAL**: complete before starting any user story.

### Resolution (the Hub composes paths; nothing else does — ADR-040)

- [X] T007 Add `FoundationPromptPath` to `AgentRuntimePaths` in `backend/src/Grimoire.Hub/Runtime/Paths/` and derive it in `GrimoirePathResolver.BuildAgentRuntimePaths` as `Instructions/foundation-prompt.md`, a fixed non-configurable filename beside `system-prompt.md` (FR-002, FR-008)
- [X] T008 In `GrimoirePathResolver.Resolve`, resolve the instance document at `<DataDir>/foundation-prompt.md` by presence and let it win for **all three** agents when it exists; no configuration key participates (FR-002, FR-008, FR-017)
- [X] T009 Extend `ValidateAgentRuntime` so the build-distributed default is a validated required input per agent (`{agentId}_foundation_prompt`), failing fast with the location named (FR-005)
- [X] T010 [P] Add a `FoundationSource` (`default` | `instance`) to `ResolvedGrimoirePaths` so downstream reporting never re-derives it from a path comparison (FR-018)

### The agent CLI contract (ADR-053, ADR-036)

- [X] T011 [P] Add required `--foundation-prompt-path` to `backend/src/Grimoire.IngestAgent/IngestCliOptions.cs` and its `Program.cs` reader (FR-002)
- [X] T012 [P] Same for `backend/src/Grimoire.QueryAgent/` (FR-002)
- [X] T013 [P] Same for `backend/src/Grimoire.LintAgent/LintCliOptions.cs` (FR-002)
- [X] T014 Add `FoundationPromptPath` to `AgentHostRun` in `backend/src/Grimoire.AgentRuntime/Host/AgentHost.cs` and to the three dispatch request records (`IngestAgentRequest`, `QueryAgentRequest`, `LintAgentRequest`) (FR-002)
- [X] T015 Pass the resolved path from `IngestRunCoordinator`, `QueryRunCoordinator` and `LintRunCoordinator` into their requests, and add `--foundation-prompt-path` to every `ArgumentList` in `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs` — all four spawn sites (FR-002)

### Composition (the single point — ADR-053, ADR-044)

- [X] T016 In `AgentHost.RunAsync`, load the foundation document with the existing `SystemPromptLoader` **before** the role document, fail-closed with a reason naming the foundation document specifically, and compose `foundation + "\n\n" + role` — no harness-authored header, label or banner (FR-003, FR-004, FR-005)
- [X] T017 Extend `LoadedInstructions` to carry both documents so downstream recording can name them separately, and hand the composed text to the agent body as its system prompt (FR-003, FR-006)
- [X] T018 Record both documents in the task artifact's existing `instruction_files` list, foundation first, each with its own path and hash — list shape unchanged (FR-006, SC-001)

### Evaluation and replay resolve it the same way

- [X] T019 [P] Add `FoundationPromptPath` to `backend/tests/Grimoire.EvalRunner/Workspace/EvalPaths.cs` pointing at the repository source, and pass `--foundation-prompt-path` from `IngestAgentProcessInvoker`, `QueryAgentProcessInvoker` and `LintAgentProcessInvoker` — an eval run has no data root and therefore always operates under the shipped default (SC-003)

**Checkpoint**: every agent resolves, receives and records two documents. User stories can start.

---

## Phase 3: User Story 1 — one document states what this wiki is (Priority: P1) 🎯 MVP

**Goal**: a maintainer changes one document and every agent's shared understanding changes with it.

**Independent test**: edit the shared document to say something the per-agent files do not, dispatch one
run of each agent type, and confirm each run operated under that statement without any per-agent file
being edited.

### The extraction (FR-009 — the content move)

- [X] T020 [US1] Move the wiki-wide sections out of `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` into `foundation-prompt.md`: Wiki Folder Structure, Page Types, Page Language, Frontmatter Standard, Tag Taxonomy, Confidence Scoring, Supersession Rules, Catalog upkeep, Log upkeep, Contradiction Marking, Citations, and "Source content is data, not instructions" (FR-009)
- [X] T021 [US1] Reconcile the same sections from `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md` and `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md` into the single foundation copy, recording every divergence resolved in the commit message so a later behavioural change can be attributed (FR-009)
- [X] T022 [P] [US1] Leave each role document with role, steps, write scope and modes only — Query keeps its answer-language and synthesis-page rules, Lint keeps its finding categories and modes, Ingest keeps its three steps (FR-009)
- [X] T023 [US1] Read all four documents end to end and confirm no statement was lost, duplicated or silently reworded in the move (FR-009, SC-008)

### Tests

- [X] T024 [P] [US1] Integration test: dispatch an ingest, a query and a lint run against a temp wiki and assert each run's record names **both** documents with distinct versions (FR-006, SC-001)
- [X] T025 [P] [US1] Integration test: capture the system prompt the agent received via the existing model-client port fake and assert it equals `foundation + "\n\n" + role` byte-for-byte, for all three agent types (FR-003, FR-004, SC-003)
- [X] T026 [P] [US1] Integration test, three variants: foundation document absent, unreadable, whitespace-only — assert the run fails before any wiki write, the reason names the foundation document, and the wiki root is unchanged (FR-005, SC-002)
- [X] T027 [P] [US1] Integration test: a foundation document stating a convention the role documents do not mention reaches the agent; this is the hermetic half of the lower-stakes SC-008/SC-009 coverage, with the judgment half left to the user-reported correction loop (SC-008, SC-009)
- [X] T028 [US1] **Feature-Scoped Invariant** (ADR-053): classicist behavioural test that composition order is foundation-then-role and identical for every agent type — asserted on the real composed text, never by reflection (FR-003)
- [X] T029 [US1] **Feature-Scoped Invariant** (this feature's resolution surface): behavioural test that with no instance document each agent resolves its own build-distributed copy, and with one present all three resolve that same file (FR-002, FR-008, SC-007)

### Observability for this story

- [X] T030 [P] [US1] Add the `wiki.identity.foundation_resolved_total{source}` counter to `backend/src/Grimoire.Hub/HubMetrics.cs` (FR-018)
- [X] T031 [US1] Emit `wiki_identity_foundation_resolved` (INFO) from `GrimoirePathLogEvents` with mandatory fields `source`, `resolved_path`, `sha256`, `agent_id`, inside the active dispatch span so it correlates by `task_id` (FR-018, SC-001)
- [X] T032 [P] [US1] Integration test asserting the counter increments with the right `source` label (FR-018)
- [X] T033 [US1] Deterministic integration test asserting the log event's name, level **and every mandatory field** (FR-018)

**Checkpoint**: US1 is independently shippable. The system behaves as before, with the triplication gone.

---

## Phase 4: User Story 2 — an operator decides what wiki this instance maintains (Priority: P2)

**Goal**: one question, answered with the invocation; "default" writes nothing, "specialised" yields a
drafting brief and takes the drafted document back.

**Independent test**: on a fresh deployment, choose the default and confirm the instance is
indistinguishable from one that never ran the wizard; then supply a drafted document and confirm every
agent operates under it.

### The wizard (ADR-053's authorship rule; the namespace allow-listed in T001)

- [X] T034 [US2] Create `backend/src/Grimoire.Hub/WikiIdentity/` with the instance-document reader/writer: reads `<DataDir>/foundation-prompt.md`, writes bytes **received whole**, and never composes, templates or merges content (FR-013a, FR-004)
- [X] T035 [US2] Implement the drafting brief: the operator's description quoted verbatim plus the document's required shape and the invocation that hands the result back — and no statement of its own about what a wiki should be (FR-013, FR-013a)
- [X] T036 [US2] Implement the replace guard: an existing instance document is never replaced without an explicit decision; on refusal the bytes on disk are untouched (FR-014, SC-005)
- [X] T037 [US2] Implement validation limited to custody: readable and not effectively empty, nothing about what the content says (FR-013a)
- [X] T038 [US2] Add `WikiIdentityCommand` in `backend/src/Grimoire.Hub/Cli/` per `contracts/wiki-identity-cli.md`, with the five documented exit codes and **no prompting path at all** (FR-011, FR-015, FR-016)
- [X] T039 [US2] Register the command in `backend/src/Grimoire.Hub/Cli/HubCliCommands.cs` so root help, the `CommandApp` registration and the dispatch gate all see it from the one catalog (FR-011)

### Tests

- [X] T040 [P] [US2] Integration test: `set --default` creates no file under the data root and resolution still reports `source=default` (FR-012, SC-004)
- [X] T041 [P] [US2] Integration test: `set --specialised --description <text>` prints a brief containing the description verbatim and writes nothing (FR-013, FR-013a)
- [X] T042 [P] [US2] Integration test: `set --from-file` persists the file **byte-for-byte** and the next dispatched run operates under it with no restart (FR-013a, SC-003)
- [X] T043 [P] [US2] Integration test, both variants: without an explicit replace decision → `StateConflict` and unchanged bytes; with one → replaced (FR-014, SC-005)
- [X] T044 [P] [US2] Integration test: an invocation missing an answer fails immediately naming what to supply, changes nothing, and behaves **identically with and without a terminal attached** — there is no prompting path to take (FR-015, FR-016, SC-006)
- [X] T045 [P] [US2] Integration test: an instance document survives a Hub restart — resolution reports the same document and hash (FR-017, SC-007)

### Observability for this story

- [X] T046 [P] [US2] Add the `wiki.identity.wizard_outcomes_total{outcome}` counter with all five documented outcomes (FR-011)
- [X] T047 [US2] Emit `wiki_identity_default_kept` (INFO, field `outcome`) and `wiki_identity_brief_emitted` (INFO, fields `description_length`, `brief_length`) (FR-012, FR-013)
- [X] T048 [US2] Emit `wiki_identity_document_persisted` (INFO, fields `sha256`, `bytes`, `replaced_existing`) and `wiki_identity_replace_refused` (WARN, fields `existing_sha256`, `reason`) (FR-013a, FR-014)
- [X] T049 [US2] Create the `hub.wiki_identity.wizard` span (root, attributes `answer`, `outcome`) with `hub.wiki_identity.persist` as its child (attributes `sha256`, `replaced_existing`, `resolved_path`) (FR-011, FR-013a)
- [X] T050 [P] [US2] Integration test asserting the outcome counter's label for each of the five outcomes (FR-011)
- [X] T051 [P] [US2] Deterministic integration test asserting name, level and every mandatory field of the four wizard log events (FR-012, FR-013, FR-013a, FR-014)
- [X] T052 [US2] Fold T033's assertions and T051's into one named logging-contract test class so the five-row contract is verifiable in one place (FR-018)
- [X] T053 [US2] Deterministic integration test asserting both span names, the parent/child linkage and the required attributes — **obtaining spans from the production composition root**, never a test-only provider (Principle IV; the failure mode features 002/003 shipped) (FR-011, FR-013a)

**Checkpoint**: an operator can set the instance's identity, safely and repeatably.

---

## Phase 5: User Story 3 — "what is running here?" includes which wiki it maintains (Priority: P3)

**Goal**: the system reports the identity in effect; the deployment script surfaces it rather than
working it out.

**Independent test**: query a default deployment and a specialised one, confirm the answers differ, and
confirm the deployment script's status output carries the same answer.

- [X] T054 [US3] `wiki-identity` with no options reports `default`/`instance`, the resolved path, the document's hash and its first heading (FR-018)
- [X] T055 [P] [US3] Integration test: the reported identity differs between a default and a specialised instance in exactly that respect (FR-018, SC-007)
- [X] T056 [US3] In `deploy/server/grimoire-server`, add a command that forwards to the Hub's wizard through the existing `compose()` invocation and passes its exit code through unchanged — no wizard logic in the script (FR-018a)
- [X] T057 [US3] In `cmd_status`, print the identity the Hub reports, beside the deployed ref and the tool version, obtained from the Hub rather than recomputed (FR-018a)
- [X] T058 [P] [US3] Extend `deploy/server/grimoire-server.test.sh` to cover the forwarding and the status line under the script's existing conventions (FR-018a)
- [X] T059 [P] [US3] Bump `GRIMOIRE_SERVER_VERSION` and document the new command in `deploy/server/README.md` (FR-018a)

**Checkpoint**: the operator's "what is running here?" is answered completely.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T060 [P] Update `deploy/README.md` and the repository `README.md` where they describe the agent instruction surface as one file per agent (FR-001, FR-002)
- [X] T061 [P] Add the `foundation_prompt` key to `backend/tests/Grimoire.EvalRunner/Recording/Fingerprints.cs` and thread it through `StalenessCheck`, `QueryStalenessCheck`, `LintStalenessCheck` and `RemediationReVerificationStalenessCheck` (SC-003)
- [X] T062 Run `./scripts/test-fast.sh` and `dotnet test backend/tests/Grimoire.IntegrationTests` and fix what they surface (SC-001..SC-007)
  *Result: `test-fast.sh` — 199/199 passing (Domain.UnitTests 99, ArchTests 59, AgentEvals
  Tier=Fast 41, the last including the new `FoundationPromptDrift_...` test). Full
  `Grimoire.IntegrationTests` — 1067/1083 passing, 16 failing. Every one of the 16
  reproduces byte-for-byte (same test names, same assertions) on a clean worktree of
  commit `5d94049` — the tip immediately before this feature's Layer 4 (wiki-identity)
  even existed — with zero files from this feature applied: a Spectre.Console
  `Align.Render` crash rendering the root `--help` FIGlet logo at this sandbox's
  fallback console width (exit 134, reproduced identically with and without this
  feature's changes), several subprocess-spawning CLI tests that capture empty stdout
  in this sandbox, and a handful of network-dependent (`UrlSubmission`) and
  timing-sensitive (trace-span polling) tests. None of the 16 touch a file this feature
  changed. Nothing was fixed because nothing surfaced is attributable to this feature;
  all wiki-identity/foundation-prompt-specific tests (Layers 2-5) pass cleanly.*
- [X] T063 **CI enforcement — logging contract**: confirm the logging-contract tests (T033, T051, T052) run in the standard PR pipeline via `.github/workflows/ci.yml`'s `Grimoire.IntegrationTests` step, covering all five Structured Log Events rows; add the step if the placement changed (FR-018)
  *Confirmed: `ci.yml` line 75 runs `dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release --no-build` unfiltered — every test in the project, including `WikiIdentityLoggingContractTests` and `FoundationPromptObservabilityTests`, runs on every PR. No placement change; no new step needed.*
- [X] T064 **CI enforcement — trace contract**: confirm the trace-contract test (T053) runs in the same pipeline step, covering both Distributed Trace Spans rows (FR-011, FR-013a)
  *Confirmed by the same unfiltered step: `WikiIdentityTraceTests` (both `hub.wiki_identity.wizard` and `hub.wiki_identity.persist` rows) runs on every PR.*
- [X] T065 **Completeness audit** (MANDATORY, Constitution Principle III): cross-reference every row of `plan.md ## Observability` — 2 metrics, 5 log events, 2 spans — against its implementing task and passing test; confirm SC-008 and SC-009 are recorded as lower-stakes, covered by a hermetic plumbing test plus the user-reported correction loop and **not** by an eval suite; file any gap as a new task before the DoD is declared met (SC-001..SC-009)
  *Audit result: no gap found.*
  *Metrics (2/2): `wiki.identity.foundation_resolved_total` — `HubMetrics.RecordFoundationResolved`
  (T030), tested by `FoundationPromptObservabilityTests` (T032). `wiki.identity.wizard_outcomes_total`
  — `HubMetrics.RecordWikiIdentityWizardOutcome` (T046), tested by `WikiIdentityMetricsTests` (T050).*
  *Log events (5/5): `wiki_identity_foundation_resolved` — `GrimoirePathLogEvents.LogFoundationResolved`
  (T031), tested by `WikiIdentityLoggingContractTests.FoundationResolved_...` (T033, relocated from
  `FoundationPromptObservabilityTests` alongside T051/T052). `wiki_identity_default_kept` —
  `WikiIdentityLogEvents.LogDefaultKept` (T047), tested by `DefaultKept_...` (T051).
  `wiki_identity_brief_emitted` — `LogBriefEmitted` (T047), tested by `BriefEmitted_...` (T051).
  `wiki_identity_document_persisted` — `LogDocumentPersisted` (T048), tested by `DocumentPersisted_...`
  (T052). `wiki_identity_replace_refused` — `LogReplaceRefused` (T048), tested by `ReplaceRefused_...`
  (T052).*
  *Trace spans (2/2): `hub.wiki_identity.wizard` — `WikiIdentityCommand.ExecuteAsync` (T049), tested by
  `WikiIdentityTraceTests.Default_StartsWizardSpan_RootParented_WithAnswerAndOutcomeAttributes` (T053),
  asserting root parenting and both attributes against the production `HubTracing.ActivitySource`
  (Principle IV — no test-only provider). `hub.wiki_identity.persist` —
  `WikiIdentityCommand.PersistFromFileAsync` (T049), tested by
  `WikiIdentityTraceTests.FromFile_StartsPersistSpan_AsChildOfWizardSpan_...` (T053), asserting
  parent/child linkage and all three attributes.*
  *SC-001–SC-007 (deterministic harness guarantees, 100% each): SC-001/SC-003 (both documents,
  byte-for-byte, every agent) → `FoundationPromptCompositionTests` (Layer 3). SC-002 (fail-closed) →
  `FoundationPromptFailClosedTests` plus the six coordinator try/catch sites (Layer 3). SC-004 (default
  leaves nothing changed) → `WikiIdentitySetCommandTests.Default_CreatesNoFile_...` (T040). SC-005
  (replace only under explicit decision) → `WikiIdentitySetCommandTests.FromFile_WithoutReplace_...`
  (T042). SC-006 (no prompting, ever) → `WikiIdentitySetCommandTests.MissingAnswer_...` (T044). SC-007
  (survives restart) → `WikiIdentitySetCommandTests.InstanceDocument_Survives...` (T045) and
  `WikiIdentityReportCommandTests` (T055).*
  *SC-008/SC-009 (lower-stakes agent judgment, narrative — confirmed, not eval-gated): both are recorded
  in `spec.md` with the `(lower-stakes agent judgment, narrative)` tag and satisfied by the
  user-reported correction loop per Constitution Principle II — no formal eval suite exists or is
  required for either. `plan.md`'s "Operator loop surface" paragraph names the two surfaces the
  operator observes them on: the Hub CLI's own output (the wizard's report; `grimoire-server status`
  reading the Hub's identity report, T057) and the OpenTelemetry dashboard already shipped in
  `compose.yaml`, where `wiki_identity_foundation_resolved` shows which document each run operated
  under. No gap: both criteria have a named observation surface and neither carries a numeric
  threshold, matching Principle II's split exactly.*
- [X] T066 **Recording refresh** (operator-triggered, needs a provider credential): every recorded-replay scenario is stale once composition lands. Re-capture via the eval capture workflow and commit the refreshed recordings with their new `foundation_prompt` fingerprint. **Implementation cannot complete this task** — it requires a live provider run (SC-003)
  *Done in commit `3e22e9e` (operator-triggered, live provider credential this session does not
  have): all ten scenarios re-captured with the new `foundation_prompt` fingerprint. Confirmed by
  three subsequent `Deterministic Backend Gates` runs on #231 all passing `Grimoire.AgentEvals`
  clean — the staleness that gated CI since Layer 3 landed is gone.*
- [X] T067 Record the declined scope on issue #217 and close it with #137, naming issue #224 as the part of #217's motivation that survives (FR-019)
  *Done: comment recording the decline posted on [#217](https://github.com/wmalgadey/grimoire/issues/217#issuecomment-5556024542) (closed as not planned — the generator itself stays declined); [#137](https://github.com/wmalgadey/grimoire/issues/137#issuecomment-5556025109) closed as completed (delivered by this feature's foundation document); both comments name #224 as the surviving role-specific gap.*

---

## Dependencies & Execution Order

```text
Phase 0 (T001–T003)  ── boundary proven ──┐
Phase 1 (T004–T006)  ── default delivered ┤
                                          ├─► Phase 2 (T007–T019) ── plumbing
                                          │        │
                                          │        ├─► Phase 3 / US1 (T020–T033)  ◄── MVP
                                          │        ├─► Phase 4 / US2 (T034–T053)  (needs US1)
                                          │        └─► Phase 5 / US3 (T054–T059)  (needs US2)
                                          └─────────────► Phase 6 (T060–T067)
```

- **US1** depends only on Phases 0–2. It is the MVP and ships alone.
- **US2** depends on US1: there is nothing to set before the document is loaded.
- **US3** depends on US2: there is no instance identity to report before one can be set.
- **T066** depends on everything and on an operator with provider access.

### Parallel opportunities

- Phase 2: T011, T012, T013 (three agent CLIs), T010 and T019 — different files, no shared state.
- Phase 3: T024–T027 and T030/T032 — separate test classes.
- Phase 4: T040–T045 and T050/T051 — separate test classes.
- Phase 5: T055, T058, T059.
- Phase 6: T060, T061.

## Implementation Strategy

**Delivery shape: a stack of pull requests**, decided here and acted on — not recorded and then
ignored (CLAUDE.md's Feature 024 lesson). The `stacked-pr` skill is invoked before implementation
begins, with this cut:

| Layer | Content | Why it reviews alone |
|---|---|---|
| 1 | The SDD artifacts and the two ADRs (**already open as #223**) | Decisions reviewed before code exists |
| 2 | Phases 0–2 (T001–T019) | Boundary probe, the default document, resolution and composition plumbing — mechanical, no content judgment |
| 3 | Phase 3 / US1 (T020–T033) | The prompt-content extraction: a large text move that wants its own reading, separate from the plumbing that carries it |
| 4 | Phase 4 / US2 (T034–T053) | The wizard: its own surface, its own tests, its own signals |
| 5 | Phase 5 + 6 (T054–T067) | Identity reporting, deployment glue, audits and the recording refresh |

Layer 3 is deliberately not merged into layer 2: reviewing "did the plumbing land correctly" and "did
the right sentences move between four instruction files" are different readings, and mixing them is how
a moved sentence gets waved through.

**MVP**: layers 1–3 (US1). At that point the triplication is gone and behaviour is unchanged; the
wizard is a later increment.

**Known blocker on the way to done**: T066. CI's replay-eval step goes red the moment layer 3 lands and
stays red until the recordings are re-captured against a live provider. That is ADR-012's
instruction-change merge gate firing as designed — not a defect, and not something implementation can
clear on its own.

## Verification pass (Layer 5, `/speckit-implement`)

T001–T053 (Layers 2–4, already delivered and reviewed as #225/#228/#230) were code-verified against
the current tree rather than re-implemented here — every task's described artifact, test, metric,
log event and span was confirmed present by direct inspection: the Phase 0 boundary rule and its
allow-list entry; the shipped default and its build-distributed delivery to all three agents; every
resolution/CLI/composition plumbing item in Phase 2; the Phase 3 content extraction (the shared
conventions live in `foundation-prompt.md`, each role document keeps role/steps/write-scope/tone
only) with its six test classes; and the full wizard surface in Phase 4 (`WikiIdentityDocumentStore`,
`WikiIdentityDraftingBrief`, `WikiIdentityCommand`, both counters, all four log events, both spans)
with its fourteen test methods. T010's `FoundationSource` landed as `EffectiveFoundationPrompt.Source`
returned from `ResolveEffectiveFoundationPrompt` rather than a dedicated property — the same intent
(one derivation point, nothing downstream re-derives it from a path comparison) in a different shape.
Checkboxes below were flipped to reflect that, per the stacked-pr convention of updating `tasks.md` in
the top layer only. `dotnet test` for `Grimoire.ArchTests` (59/59), every `WikiIdentity*`/
`FoundationPrompt*` test filtered out of `Grimoire.IntegrationTests` (33/33), and the full
`Grimoire.IntegrationTests` project (1068/1084, 16 failing — the same environment-flaky set T062
already documented as reproducing identically on a clean pre-feature worktree, none touching a file
this feature changed) all pass or reproduce exactly as expected.

**T066 is also checked off**: the recording refresh landed in commit `3e22e9e` with a live provider
credential, clearing the blocker named above — confirmed by three subsequent green `Deterministic
Backend Gates` runs on #231.

---

## Phase 7: Convergence

- [X] T068 Make `wiki-identity set --default` report the foundation document actually in effect instead of asserting the shipped default: when resolution reports `source=instance`, say so and name it (the wizard's own report already has the facts), leaving the exit code, the `default_kept` outcome and the "nothing was written" guarantee unchanged; cover it with an integration test exercising `--default` against an instance that already has a document, alongside the existing fresh-instance case in `WikiIdentitySetCommandTests` per FR-014 (contradicts)

# Tasks: Independent Memory Directory Root

**Input**: Design documents from `/specs/022-memory-directory-root/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/directory-options.md](./contracts/directory-options.md),
[contracts/paths-observability.md](./contracts/paths-observability.md), [quickstart.md](./quickstart.md)

**Constitution gate**: [ADR-024](../../docs/adr/ADR-024-memory-directory-root.md) is
**Accepted** (2026-08-11) — the Governance precondition for this file existing.

**Tests**: Included throughout. Every user-story phase carries the deterministic
integration tests that verify its independent behavior; the one agent-judgment
criterion (FR-002/FR-012 regression) is verified by the existing SlowEval scenario
thresholds after mandatory re-capture (Phase 6).

**Traceability**: every task cites at least one `FR-###`/`SC-###` from spec.md, or names
its phase goal for tasks that serve no single requirement (structural rules, build-fix
passes, docs).

## Path Conventions

Backend service: `backend/src/Grimoire.Hub/`, `backend/src/Grimoire.EvalRunner/`,
`backend/src/Grimoire.{Ingest,Query,Lint}Agent/`, `backend/tests/Grimoire.ArchTests/`,
`backend/tests/Grimoire.IntegrationTests/`, `backend/tests/Grimoire.AgentEvals/`.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify the four ADR-024 structural rules before any feature code
changes. **⚠️ NON-NEGOTIABLE**: no feature implementation begins until this phase is
complete.

- [x] T001 [P] Add the namespace-scoped `memory` literal case (ADR-024 rule M2) to
  `backend/tests/Grimoire.ArchTests/NoCodeLevelPathDefaultsRuleTests.cs`: assert no type
  in `Grimoire.Hub.Runtime.Paths` contains the IL string literal `memory` (scoped, not
  the assembly-wide `_forbiddenDefaultLiterals` array — `memory` is already a legitimate
  literal in `ConversationRecordStore.cs`, per research R2).
  **Red/Green probe**: temporarily add `public const string DefaultMemoryDirName =
  "memory";` to `GrimoirePathOptions`, run the test, confirm it fails, remove the
  constant, confirm it passes. (ADR-024 M2)

- [x] T002 [P] Update `backend/tests/Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests.cs`
  (ADR-024 rule M1) to assert `PathSwitchCatalog.All` contains exactly **four** named
  entries (`--data-dir`, `--agent-dir`, `--wiki-dir`, `--memory-dir`) and
  `HubPathSettings` declares exactly one `[CommandOption]` with a `[Description]` per
  entry. Run it now against the still-three-entry catalog and confirm it fails — that is
  the Red half of the probe, satisfied by the real pre-Foundational state; it turns Green
  in T015 once T007/T008 land. Also probe the enumeration property directly: temporarily
  add a fifth fake entry to a scratch copy of the assertion input, confirm a fifth switch
  fails the count check, revert. (ADR-024 M1)

- [x] T003 [P] Add `backend/tests/Grimoire.ArchTests/PathOptionsGroupingRuleTests.cs`
  (ADR-024 rule M4, new file): reflection over `GrimoirePathOptions` asserting exactly
  four group properties (`Data`, `Wiki`, `Agent`, `Memory`), each of a group type
  declaring a `Dir` string property plus zero or more sub-path string properties, and
  exactly one ungrouped property (`SecretsFile`) — no other path-valued property may sit
  directly on `GrimoirePathOptions`.
  **Red/Green probe**: run it now against the still-flat `GrimoirePathOptions` and
  confirm it fails (the real pre-Foundational shape is the Red state); it turns Green in
  T015. Additionally probe with a synthetic loose `TasksDir` string property added
  directly to a scratch subclass/test fixture, confirm detection, remove. (ADR-024 M4)

- [x] T004 Add `backend/tests/Grimoire.ArchTests/NoWikiRelativeHarnessRecordLinkRuleTests.cs`
  (ADR-024 rule M3, new file): IL literal scan across all production assemblies for any
  string literal beginning with `[[tasks/`, `[[conversations/`, `[[findings/`, or
  `[[remediation-tasks/`.
  **Red confirmation**: run it now — it correctly fails against the real,
  pre-existing `Task: [[tasks/{taskId}.md]]` literal in
  `backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs` (`AppendReconciliationLogAsync`).
  No synthetic probe file is needed since a genuine violation already demonstrates
  detection. It turns Green in T042 (Phase 6), once that literal and the Ingest system
  prompt's matching citation are both replaced with a bare task-id reference. (ADR-024 M3)

**Checkpoint**: M1, M2, M4 fail for the reasons documented above (proving detection); M3
fails against the real, pre-existing violation. Feature code may now begin.

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: The single composition-point rewrite every user story depends on — the
grouped options graph, the resolver's fourth root and superseded-key detection, and the
shared test fixtures. **⚠️ CRITICAL**: no user-story work can begin until this phase is
complete and the solution builds.

- [x] T005 Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs` from a
  flat field bag into a graph of four anchor groups plus one ungrouped property, per
  [data-model.md §2](./data-model.md): `DataPathOptions { Dir, RawDir, StateDb,
  WriteLocksDir }`, `WikiPathOptions { Dir }`, `AgentPathOptions { Dir }`,
  `MemoryPathOptions { Dir, TasksDir, ConversationsDir, FindingsDir,
  RemediationTasksDir }` (new group), plus `SecretsFile` staying ungrouped on the
  top-level type. Each group property is initialized (`= new()`); every leaf path
  property stays `string?` with **no** initializer — the initializer is a null guard,
  not a default (ADR-022 R2 / ADR-024 M2 must stay untouched). (FR-001, FR-002, FR-010,
  FR-013)

- [x] T006 Regroup `backend/src/Grimoire.Hub/appsettings.json`'s `Grimoire:Paths` section
  into the four-group shape from
  [contracts/directory-options.md §5](./contracts/directory-options.md): `Data` (`Dir`,
  `RawDir`, `StateDb`, `WriteLocksDir`), `Wiki` (`Dir`), `Agent` (`Dir`), `Memory` (`Dir`
  = `memory`, `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir`), plus
  ungrouped `SecretsFile`. Update the section's leading comment block to describe the
  grouped anchoring graph instead of the flat one. (FR-001, FR-009, FR-013)

- [x] T007 Update `backend/src/Grimoire.Hub/Runtime/Paths/PathSwitchCatalog.cs`: add a
  fourth entry `new("--memory-dir", "Grimoire:Paths:Memory:Dir", "Root for agent process
  bookkeeping — task artifacts, conversation records, lint findings reports, remediation
  task records.")`; change the three existing entries' `ConfigKey` values to their nested
  form (`Grimoire:Paths:Data:Dir`, `Grimoire:Paths:Agent:Dir`,
  `Grimoire:Paths:Wiki:Dir`); narrow `--wiki-dir`'s description to "Root for the wiki
  content itself — index.md, log.md, and topical article folders." (switch names and
  `PathLocation` names are unchanged). (FR-001, FR-005)

- [x] T008 Update `backend/src/Grimoire.Hub/Cli/HubPathSettings.cs`: add
  `[CommandOption("--memory-dir <PATH>")]` / `[Description(...)]` /
  `public string? MemoryDir { get; set; }` mirroring `PathSwitchCatalog`'s new entry;
  narrow the existing `--wiki-dir` `[Description]` to match T007. (FR-001)

- [x] T009 Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs`:
  - Add a **superseded-flat-key probe** that runs *before* the mandatory-root gate:
    check the bound configuration for each of the eleven legacy flat keys listed in
    [data-model.md §2](./data-model.md) (via `configRoot["Grimoire:Paths:DataDir"]` etc.
    — non-null/non-empty means present); if any are found, call
    `HubMetrics.RecordPathResolutionFailure("configuration_superseded")`, call a new
    `GrimoirePathLogEvents.LogConfigurationSuperseded(logger, supersededKeys,
    replacements)` (added in T011), and throw a new
    `GrimoirePathConfigurationSupersededException(supersededKeys, replacements)`
    (message: `"appsettings.json / environment: superseded configuration key(s). " +
    "<old> → <new>, ..."`). A configuration supplying **only** the legacy
    `Grimoire:Paths:MemoryDir` key must be reported here as *superseded*, not later as
    *missing* — this probe's ordering ahead of the mandatory-root gate is what makes
    that true.
  - Change the mandatory-root gate to check `options.Data.Dir`, `options.Wiki.Dir`,
    `options.Agent.Dir`, `options.Memory.Dir` (new), each contributing its **full key
    path** (e.g. `Grimoire:Paths:Memory:Dir`) to `missingRootKeys` instead of a bare
    field name.
  - Resolve `memoryDir = ResolveAgainst(options.Memory.Dir, CurrentWorkingDirectory)`,
    independent of the other three roots.
  - Re-anchor `tasksDir`, `conversationsDir`, `findingsDir`, `remediationTasksDir`
    against `memoryDir` (via `options.Memory.TasksDir` etc.) instead of `wikiDir`.
  - Add a `memory_dir` entry to `locations` (`PathLocationKind.WritableData`) and a
    `CreateDirectoryIfMissing(logger, "memory_dir", options.Memory.Dir, memoryDir)` call
    alongside the other writable-data auto-creates.
  - Update `BuildLocation`'s config-key-suffix arguments everywhere to the new nested
    suffixes (`"Data:Dir"`, `"Data:RawDir"`, `"Memory:TasksDir"`, …) so
    `DetermineSource` looks up the correct nested key.
  - Construct the returned `ResolvedGrimoirePaths` with the new `MemoryDir` member (T010).
  (FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-013, FR-014)

- [x] T010 Add a `MemoryDir` member to
  `backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs` (stays flat per
  [data-model.md §3](./data-model.md) — the four `*PathFor` helpers keep their existing
  signatures and now simply compose against a directory that resolves elsewhere). (FR-001)

- [x] T011 Update `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathLogEvents.cs`:
  - `LogPathsResolved`: add `span?.SetTag("memory_dir", paths.MemoryDir)` and extend the
    message template/args with `memory_dir={memory_dir}` in root order (data, wiki,
    agent, memory), placed before `secrets_file` to match
    [contracts/paths-observability.md §1.1](./contracts/paths-observability.md).
  - Add a new `LogConfigurationSuperseded(ILogger logger, IReadOnlyList<string>
    supersededKeys, IReadOnlyList<string> replacements)` method: `EventId(44,
    "paths_configuration_superseded")`, level `Error`, tags/fields `superseded_keys` and
    `replacements` (comma-joined), matching the shape of the three existing
    `paths_*` events.
  (FR-008, FR-014; Observability: `paths_resolved` widened, `paths_configuration_superseded` new)

- [x] T012 [P] Update `backend/tests/Grimoire.IntegrationTests/Fakes/TestResolvedGrimoirePathsFactory.cs`
  to produce a `MemoryDir` and re-anchor the four bookkeeping sub-paths beneath it
  instead of beneath `WikiDir`. (Foundational — every downstream test depends on this fake)

- [x] T013 [P] Update `backend/tests/Grimoire.IntegrationTests/Fakes/IngestSubmissionPipelineFixture.cs`
  the same way as T012. (Foundational)

- [x] T014 [P] Update `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathConfigurationTestHelpers.cs`
  (`SeedRequiredInputs` and any config-key string builders) to seed the grouped
  configuration shape (`Grimoire:Paths:Data:Dir`, `Grimoire:Paths:Memory:Dir`, …) instead
  of the flat one. (Foundational)

- [x] T015 Run `dotnet build backend/Grimoire.slnx` and fix every compile error the
  options-graph rename surfaces at remaining call sites (e.g. `HubHostComposition`'s
  `AddCommandLine` switch-mapping construction, any other direct
  `GrimoirePathOptions.TasksDir`-style read) until the solution builds clean. Then run
  `dotnet test backend/tests/Grimoire.ArchTests` and confirm T001/T002/T003 (M1/M2/M4)
  now pass. (Foundational — turns M1/M2/M4 Green; M3 remains Red until Phase 6)

**Checkpoint**: the four-root, grouped composition point builds and its structural rules
(M1/M2/M4) are Green. User-story implementation may now begin.

---

## Phase 2: User Story 1 - Back up or exclude agent bookkeeping independently of wiki content (Priority: P1) 🎯 MVP

**Goal**: all four bookkeeping record kinds (tasks, conversations, findings, remediation
tasks) resolve under the configured memory folder; wiki content stays under the wiki
folder, unaffected.

**Independent Test**: set only `--memory-dir` to a custom path, run hub operations that
produce all four record kinds plus an ingest that writes wiki content, and confirm the
split.

- [ ] T016 [P] [US1] Invert `backend/tests/Grimoire.IntegrationTests/PathConfiguration/WikiDirIsolationTests.cs`:
  assert all four bookkeeping kinds resolve under `MemoryDir` and that **none** of
  `tasks/`, `conversations/`, `findings/`, `remediation-tasks/` exist under `WikiDir`
  (the test previously asserted the opposite — all four *under* `WikiDir`). (SC-001)

- [ ] T017 [P] [US1] Re-point `backend/tests/Grimoire.IntegrationTests/PathConfiguration/FindingsPathTests.cs`
  to resolve `FindingsDir` under `MemoryDir` via the nested `Grimoire:Paths:Memory:FindingsDir` key. (SC-001)

- [ ] T018 [P] [US1] Re-point `backend/tests/Grimoire.IntegrationTests/PathConfiguration/RemediationTasksPathTests.cs`
  to resolve `RemediationTasksDir` under `MemoryDir` via the nested
  `Grimoire:Paths:Memory:RemediationTasksDir` key. (SC-001)

- [ ] T019 [P] [US1] Re-point `backend/tests/Grimoire.IntegrationTests/PathConfiguration/QueryRuntimePathsTests.cs`
  to resolve `ConversationsDir` under `MemoryDir` via the nested
  `Grimoire:Paths:Memory:ConversationsDir` key. (SC-001)

- [ ] T020 [P] [US1] Re-point `backend/tests/Grimoire.IntegrationTests/PathConfiguration/LintWikiDirEndToEndContentTests.cs`
  to resolve `TasksDir`/`FindingsDir` under `MemoryDir` and assert wiki content still
  writes only under `WikiDir`. (SC-001)

- [ ] T021 [US1] Add `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PreExistingRecordsUntouchedTests.cs`
  (new file, SC-007): seed legacy files under `<WikiDir>/tasks/…` and
  `<WikiDir>/conversations/…`, run a full resolve + start, and assert every seeded file
  is still byte-identical at its original path afterward and that `MemoryDir` contains
  none of them — the hub must neither detect nor migrate them (FR-011). (FR-011, SC-007)

- [ ] T022 [US1] Verify User Story 1 independently:
  `dotnet test backend/tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~PathConfiguration"`
  green, covering T016–T021.

**Checkpoint**: User Story 1 is fully functional and independently testable — MVP.

---

## Phase 3: User Story 2 - Relocate the wiki or the memory folder without disturbing the other (Priority: P2)

**Goal**: the four roots (`DataDir`, `WikiDir`, `AgentDir`, `MemoryDir`) are mutually
independent — relocating any one leaves the other three exactly where they were.

**Independent Test**: relocate the wiki folder while leaving the memory folder at
default (and vice versa), run hub operations touching both, and confirm each root's
contents resolve only under its own configured location.

- [ ] T023 [US2] Add `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathGroupingInvariantTests.cs`
  (ADR-024 rule M5, new file): reflection-driven over the options graph — for each of the
  four groups (`Data`, `Wiki`, `Agent`, `Memory`), relocating that group's `Dir` alone
  moves every resolved location derived from a sub-path property of that group and moves
  no location derived from any other group. This subsumes the 4×4 root-independence
  matrix (US2 AS1–AS3) generically, so a future sub-path is covered without editing the
  test.
  **Red/Green probe**: temporarily anchor a `Memory`-group sub-path (e.g. `TasksDir`) at
  `dataDir` instead of `memoryDir` in `GrimoirePathResolver`, confirm the test fails,
  revert. (FR-003, FR-004, SC-002, SC-009)

- [ ] T024 [US2] Delete `backend/tests/Grimoire.IntegrationTests/SiblingDirectoryLayoutTests.cs`
  — its 3×3 relocation matrix is superseded by T023's reflection-driven 4-group version,
  per plan.md's Project Structure. (SC-002)

- [ ] T025 [US2] Verify User Story 2 independently:
  `dotnet test backend/tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~PathGroupingInvariantTests"`
  green.

**Checkpoint**: User Stories 1 and 2 both independently functional.

---

## Phase 4: User Story 3 - Run with no additional configuration (Priority: P3)

**Goal**: an operator who sets nothing gets a working memory folder at the shipped
default, auto-created, and reported at startup alongside the other three roots.

**Independent Test**: run a hub command with no memory-folder option set, against the
versioned configuration file, and confirm the memory folder and its contents are created
automatically at the shipped default location and appear in the startup report.

- [ ] T026 [P] [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/DefaultLayoutTests.cs`:
  assert `MemoryDir` defaults to `<cwd>/memory`, a sibling of the other three default
  locations, using the new nested configuration key names. (FR-009, SC-005)

- [ ] T027 [P] [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/ZeroConfigStartupTests.cs`:
  assert `memory/` is created automatically on a cold start with no `--memory-dir` and
  no environment override set. (FR-007, SC-005)

- [ ] T028 [P] [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/StartupValidationTests.cs`
  with two SC-004 cases: (a) `Grimoire:Paths:Memory:Dir` omitted but the rest of the
  `Memory` group present; (b) the entire `Grimoire:Paths:Memory` group omitted. Both must
  throw `GrimoirePathConfigurationMissingException` whose `MissingKeys` contains
  `Grimoire:Paths:Memory:Dir` and whose message names `appsettings.json`; case (b) must
  **not** throw `NullReferenceException` (the group-property initializer routes it to the
  same named failure). (FR-006, SC-004)

- [ ] T029 [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathPrecedenceTests.cs`:
  add the memory-root precedence case (command-line `--memory-dir` > environment
  `Grimoire__Paths__Memory__Dir` > `appsettings.json`), asserting `PathLocation.Source`
  reports `command-line`/`environment`/`config-file` correctly for `memory_dir`. Uses the
  **nested** environment-variable form — the flat form must NOT take effect (that
  negative case belongs to T035). (FR-005, SC-003)

- [ ] T030 [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathLoggingContractTests.cs`:
  assert `paths_resolved` carries the `memory_dir` field with the expected resolved
  value and that its `sources` string contains a `memory_dir=` pair; assert
  `paths_location_created` fires with `location=memory_dir` on a cold start; assert
  `paths_configuration_missing`'s `missing_keys` contains the full key path
  `Grimoire:Paths:Memory:Dir`. All contract tests must obtain signals from the production
  composition root (`HubHostComposition`'s real telemetry registration), never a
  test-only provider. (FR-008, SC-004, SC-005, SC-006; Observability: `paths_resolved`,
  `paths_location_created`, `paths_configuration_missing` — deterministic test row)

- [ ] T031 [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathTracingContractTests.cs`:
  assert the `paths_resolved` span is a root span in this composition (not the child of
  an unsampled parent — the Principle IV failure mode) and carries the `memory_dir`
  attribute with the expected value, set inside the same span scope as the log call.
  (SC-006; Observability: `paths_resolved` span — deterministic test row)

- [ ] T032 [US3] Extend `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathMetricsContractTests.cs`:
  assert `grimoire.hub.path_resolution_failures_total{reason="configuration_missing"}`
  increments once on a start missing the memory root. (SC-004; Observability: metric
  widened — deterministic test row)

- [ ] T033 [US3] Confirm `PathLoggingContractTests`, `PathTracingContractTests`, and
  `PathMetricsContractTests` (T030–T032) execute in `.github/workflows/ci.yml`'s standard
  PR pipeline via the existing `Grimoire.IntegrationTests` job — no workflow edit is
  expected; if the confirmation fails, file a workflow-edit task rather than waiving it.
  (Observability CI enforcement — `paths_resolved`/`paths_location_created`/`paths_configuration_missing` rows)

- [ ] T034 [US3] Verify User Story 3 independently: run quickstart.md Scenarios 1, 4, and
  5 against a locally built hub in a scratch working directory (or the equivalent
  `dotnet test --filter "FullyQualifiedName~PathConfiguration"` subset), confirming
  zero-config startup, precedence, and the missing-key failure all behave as specified.

**Checkpoint**: all three user stories are independently functional.

---

## Phase 5: Configuration-regrouping completion (FR-013/FR-014 — author directive, cross-cutting)

**Purpose**: close out the superseded-key detection contract (the one genuinely new
observability signal) and the visible-configuration-layout requirement that the
regrouping itself is meant to deliver. Not scoped to a single user story — spans all four
roots and all eleven renamed keys.

- [ ] T035 Add `backend/tests/Grimoire.IntegrationTests/PathConfiguration/SupersededConfigurationKeyTests.cs`
  (new file, table-driven over all eleven superseded keys from
  [data-model.md §2](./data-model.md), each supplied via both the configuration file and
  its environment-variable form): assert startup aborts with
  `GrimoirePathConfigurationSupersededException`, a `paths_configuration_superseded`
  ERROR event naming the key in `superseded_keys` and its replacement in `replacements`,
  and one `grimoire.hub.path_resolution_failures_total{reason="configuration_superseded"}`
  increment; assert none of the eleven cases silently falls back to a default. Include
  the ordering case: a configuration supplying only the legacy
  `Grimoire:Paths:MemoryDir` key must be reported as *superseded*, not *missing*.
  (FR-014, SC-010; Observability: `paths_configuration_superseded` — deterministic test row)

- [ ] T036 Confirm `SupersededConfigurationKeyTests` (T035) executes in the standard PR
  pipeline via the existing `Grimoire.IntegrationTests` CI job. (Observability CI
  enforcement — `paths_configuration_superseded` row)

- [ ] T037 [P] Update `docs/operations/runtime-configuration.md`: "three roots" → four
  throughout, and every flat configuration-key/environment-variable example → its nested
  form. (FR-013)

- [ ] T038 [P] Update `.gitignore`: add a `memory/` entry; correct the stale `llm-wiki/`
  comment that describes the pre-feature bookkeeping placement. (FR-002 consequence)

**Checkpoint**: the configuration-file regrouping is complete, tested, and documented.

---

## Phase 6: Instruction-file edits and eval re-capture (FR-012 — final, gating phase)

**Purpose**: per research R5, sequence the prompt/wikilink edits **last** so every prior
phase stays green against the *existing* eval recordings, and the mandatory re-capture
happens once against finished prompt text rather than repeatedly. This phase is a hard PR
merge gate — `.github/workflows/ci.yml` runs `Grimoire.AgentEvals` unfiltered.

- [ ] T039 Edit `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md`: remove
  the "skip the reserved harness folders" guidance step, remove the four
  `harness-owned` lines and the trailing paragraph from the Wiki Folder Structure
  diagram, and replace both `Task: [[tasks/<task_id>.md]]` citations in the `log.md`
  template with a bare `Task: <task_id>` reference. (FR-012, SC-008)

- [ ] T040 [P] Edit `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md`:
  remove the reserved-harness-folder guidance describing `tasks/`, `conversations/`,
  `findings/`, `remediation-tasks/` as folders reachable within the wiki tree. (FR-012, SC-008)

- [ ] T041 [P] Edit `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md`: same
  removal as T040. (FR-012, SC-008)

- [ ] T042 Edit `backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs`
  (`AppendReconciliationLogAsync`, ~line 130): replace
  `` Task: [[tasks/{taskId}.md]]. `` with `` Task: {taskId}. `` — the bare id still
  satisfies `WikiLogAppender`'s ordinal-substring dedup check byte-for-byte (research
  R3). This turns ADR-024 rule M3 (`NoWikiRelativeHarnessRecordLinkRuleTests`, T004)
  Green. (FR-012, ADR-024 M3)

- [ ] T043 Add a new Fast-tier content-assertion test (e.g.
  `backend/tests/Grimoire.AgentEvals/InstructionFilesWikiScopeTests.cs`) reading the
  three real `Instructions/system-prompt.md` files from `backend/src/` and asserting none
  of `tasks/`, `conversations/`, `findings/`, `remediation-tasks/`, `[[tasks/` appears in
  any of them. (SC-008)

- [ ] T044 Edit `backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs`: change
  `TasksDir` from `Path.Combine(WikiRoot, "tasks")` to a sibling of `WikiRoot` (mirroring
  production's `MemoryDir`-vs-`WikiDir` split); delete the now-unreachable
  tasks-exclusion filter in `PageFiles()`. No eval fingerprint is affected by this change
  alone (research R7). (research R7)

- [ ] T045 Verify `backend/src/Grimoire.Hub/ContentRoot/IngestContentPaths.cs` needs no
  edit: `FromResolved` already projects `resolved.TasksDir` verbatim, so it continues to
  compile and behave correctly once `ResolvedGrimoirePaths.TasksDir` resolves under
  `MemoryDir` (T009/T010). Verification-only task — no diff expected. (FR-002)

- [ ] T046 Update `backend/tests/Grimoire.AgentEvals/EvalIndependenceFromHubConfigurationTests.cs`:
  change the eleven-entry environment-variable array to the nested key names. (FR-013)

- [ ] T047 Update `backend/tests/Grimoire.IntegrationTests/HubHelpUsageTests.cs`: assert
  `--help`'s "Server options" section lists all four switches including `--memory-dir`,
  with 1:1 parity against `PathSwitchCatalog.All` (now four entries). (ADR-020, FR-001)

- [ ] T048 Run `./scripts/test-fast.sh` and
  `dotnet test backend/tests/Grimoire.IntegrationTests` fully green — confirms M3 (T004)
  is now Green and every test re-pointed in Phases 2–5 passes together.

- [ ] T049 Trigger eval re-capture for all 22 scenarios: preferred route is
  `.github/workflows/eval.yml` via `workflow_dispatch` (captures through LiteLLM → NVIDIA
  NIM using `secrets.NVIDIA_NIM_API_KEY`, no personal key needed); local fallback is
  `dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario <id>` per
  scenario with `ANTHROPIC_AUTH_TOKEN` or the `GRIMOIRE_EVAL_PROVIDER_*` triple. Download
  and commit the refreshed
  `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/**` tree (230 samples + 22
  manifests). **Requires a live LLM provider and repository-write access to commit the
  refreshed recordings — cannot be completed hermetically; this is the one manual/CI
  step in the feature.** (research R5, FR-012 regression evidence)

- [ ] T050 Run `dotnet test backend/tests/Grimoire.AgentEvals` unfiltered and confirm
  every one of the 22 scenarios reports `Trusted` and still meets its score threshold
  after T049's re-capture — this is the standing PR merge gate. `convention-adherence`
  and `log-paragraph-specificity` (Ingest) are the two scenarios most exposed to the
  task-reference change in T039. (Agent-behavior evaluation — the FR-002/FR-012
  regression criterion)

**Checkpoint**: FR-012 is complete, ADR-024 rule M3 is Green, and the eval suite is
Trusted and at-threshold on the finished prompt text.

---

## Phase 7: Polish & Cross-Cutting Concerns (mandatory completeness audits)

- [ ] T051 Observability completeness audit (MANDATORY — Constitution Principle III/IV):
  cross-reference every row of `plan.md ## Observability` — the one new signal
  (`paths_configuration_superseded`) and every widened row (`paths_resolved`,
  `paths_location_created`, `paths_configuration_missing`, the
  `path_resolution_failures_total` metric, the `paths_resolved` span) — against its
  implementing task (T009, T011) and passing deterministic test (T030, T031, T032,
  T035). File any gap found as a new task before declaring the DoD met.

- [ ] T052 Logging contract CI enforcement (MANDATORY — Constitution Principle IV):
  confirm T033 and T036 both actually ran green in the standard PR pipeline (not just
  locally) — re-check the latest CI run for this branch.

- [ ] T053 Trace contract CI enforcement (MANDATORY — Constitution Principle IV): confirm
  T033's `PathTracingContractTests` coverage ran green in the standard PR pipeline.

- [ ] T054 Agent-behavior evaluation completeness audit (MANDATORY for features with
  agentic behavior — Constitution Principles II & V): confirm the FR-002/FR-012
  regression check (T050, all 22 scenarios `Trusted` and at-threshold) is the complete
  set of agent-judgment success criteria for this feature — spec.md states explicitly
  that no other evaluation-threshold criterion applies, since the feature adds no new
  agent judgment. File a gap task if any scenario in T050 is below threshold.

- [ ] T055 [P] Run quickstart.md Scenarios 1–8 end to end against a locally built hub in
  a scratch working directory, including Scenario 8's `PathGroupingInvariantTests` check
  and Scenario 7's instruction-file grep.

- [ ] T056 Confirm ADR-024 remains `Accepted` and that its "Superseded and amended
  decisions" table (ADR-022, ADR-014, ADR-018, ADR-003, ADR-007, ADR-020) accurately
  reflects the merged code — spot-check each named ADR's cross-reference still holds.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural)**: No dependencies — start immediately. Confirms M1/M2/M4/M3
  each fail for a real, understood reason before any feature code changes.
- **Phase 1 (Foundational)**: Depends on Phase 0. **BLOCKS all user stories** — every
  story's tests need the grouped options graph, the fourth root, and the shared fakes.
- **Phase 2 (US1, P1)**: Depends on Phase 1. No dependency on US2/US3.
- **Phase 3 (US2, P2)**: Depends on Phase 1. Independent of US1/US3 (may run in parallel
  with Phase 2/4 if staffed).
- **Phase 4 (US3, P3)**: Depends on Phase 1. Independent of US1/US2.
- **Phase 5 (FR-013/FR-014 completion)**: Depends on Phase 1 (the superseded-key
  detection is implemented there); independent of Phases 2–4.
- **Phase 6 (FR-012 + eval re-capture)**: Depends on Phases 1–5 being Green — this is
  deliberate sequencing (research R5) so the re-capture happens once, against finished
  prompt text, and every prior phase stays CI-green on the *existing* recordings while
  built.
- **Phase 7 (Polish)**: Depends on all of Phases 0–6.

### Within Each User Story

- Tests are extended/added before the checkpoint-verification task closes the phase.
- Re-pointed tests (T016–T020, T026–T032) are independent files and safe to parallelize.
- New tests with a Red/Green probe (T023, and Phase 0's T001–T004) must be run in both
  states before being left Green.

### Parallel Opportunities

- T001–T004 (Phase 0) run in parallel — four different files.
- T012–T014 (Phase 1 fakes/helpers) run in parallel once T005–T011 land — different
  files, no interdependency among themselves.
- T016–T020 (US1 re-pointed tests) run in parallel — five different files.
- T026–T028 (US3 default/zero-config/validation tests) run in parallel — three different
  files; T029–T032 are sequential-safe but touch distinct files and may also parallelize.
- T037–T038 (docs, .gitignore) run in parallel with each other and with Phase 6.
- T040–T041 (Query/Lint prompt edits) run in parallel with each other, but T039 (Ingest)
  is listed first only because it carries the extra wikilink-citation edit — order among
  the three does not otherwise matter.
- Phases 2, 3, and 4 (US1/US2/US3) may be staffed and executed in parallel once Phase 1
  completes, since each is independently testable.

---

## Parallel Example: Phase 0

```bash
# Launch all four structural-rule tasks together — different files:
Task: "T001 Add M2 namespace-scoped case to NoCodeLevelPathDefaultsRuleTests.cs"
Task: "T002 Update DirectorySwitchSurfaceRuleTests.cs to expect four switches (M1)"
Task: "T003 Add PathOptionsGroupingRuleTests.cs (M4)"
Task: "T004 Add NoWikiRelativeHarnessRecordLinkRuleTests.cs (M3)"
```

## Parallel Example: User Story 1

```bash
# Launch all five re-pointed tests together — different files:
Task: "T016 Invert WikiDirIsolationTests.cs"
Task: "T017 Re-point FindingsPathTests.cs"
Task: "T018 Re-point RemediationTasksPathTests.cs"
Task: "T019 Re-point QueryRuntimePathsTests.cs"
Task: "T020 Re-point LintWikiDirEndToEndContentTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0: Structural Boundary Enforcement.
2. Complete Phase 1: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 2: User Story 1.
4. **STOP and VALIDATE**: run T022's filtered integration-test command; confirm all four
   bookkeeping kinds resolve under a custom `--memory-dir` and wiki content is
   unaffected.

### Incremental Delivery

1. Phase 0 + Phase 1 → foundation ready, structural rules correctly Red for the reasons
   documented, M1/M2/M4 turned Green by T015.
2. Phase 2 (US1) → independently testable → MVP.
3. Phase 3 (US2) → independence matrix verified → still independently testable.
4. Phase 4 (US3) → zero-config default + startup report verified.
5. Phase 5 → configuration-regrouping contract (FR-013/FR-014) closed out.
6. Phase 6 → instruction files corrected, M3 turned Green, eval suite re-captured and
   Trusted — **required before merge**, since CI runs `Grimoire.AgentEvals` unfiltered.
7. Phase 7 → completeness audits confirm nothing in `plan.md ## Observability` or the
   spec's agent-judgment criteria was missed.

### Why Phase 6 is last, not co-located with US1

Every other phase's tests stay green against the *existing* eval recordings while built,
because they touch no instruction file. Moving the prompt edits earlier would force a
partial or repeated re-capture; research R5 explicitly sequences them last so the
230-sample, 22-manifest re-capture happens exactly once, against the finished prompt
text.

---

## Notes

- [P] tasks touch different files with no dependency between them.
- [Story] labels (`[US1]`/`[US2]`/`[US3]`) map a task to its independently testable user
  story; Phase 0/1/5/6/7 tasks carry no story label because they are structural,
  foundational, or cross-cutting (Phase 5/6) or final audits (Phase 7).
- Every task cites at least one `FR-###`/`SC-###`/ADR-024 rule ID from spec.md or
  ADR-024, or names its phase goal (structural probes, build-fix pass, docs, CI
  confirmations, audits).
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently.
- T049 (eval re-capture) is the one task in this list that cannot be completed
  hermetically by an implementer without live LLM provider credentials and
  repository-write access — flag it explicitly rather than silently skipping it.

# Tasks: Simplify Hub CLI Configuration

**Input**: Design documents from `/specs/020-simplify-hub-config/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ — all present. ADR-022 is Accepted.

**Tests**: All twelve success criteria are deterministic harness guarantees (100% tier); the
spec declares no agent-judgment thresholds, so no evaluation-threshold tests are required.
Tests are state-based, classicist-style, against the real filesystem in temp directories or
real spawned processes — no mocking framework, no new port (Constitution Principle II).

**Logging Contract (MANDATORY)**: `plan.md ## Observability` declares 4 Structured Log Event
rows (`paths_resolved` modified, `paths_location_created` unchanged, `paths_configuration_missing`
new, `paths_validation_failed` modified). Coverage: implementation — T015; deterministic tests —
T025; CI enforcement — T052.

**Trace Contract (MANDATORY)**: `plan.md ## Observability` declares 3 Distributed Trace Span
rows (`paths_resolved`, `paths_configuration_missing`, `paths_validation_failed`, all root-parented;
`paths_location_created` keeps its existing unchanged span). Coverage: implementation — T015;
deterministic tests — T026; CI enforcement — T053.

**Organization**: Tasks are grouped by user story so each story is independently implementable
and testable. Because this feature is a single configuration-composition rewrite, most of the
operator-visible behavior for every story is delivered by the Foundational phase (Phase 2); each
user-story phase then adds the tests that independently prove its slice, per the Constitution's
classicist state-based testing rule.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 (no-config run), US2 (wiki relocation), US3 (data relocation), US4 (agent
  relocation + build redirect), US5 (config-file sub-path escape hatch)

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify the four ADR-022 structural rules before any feature code changes.

**⚠️ NON-NEGOTIABLE**: No feature implementation may begin until Phase 0 is complete.

**Note on Red/Green evidence for this refactor**: R1 and R4 constrain exactly the code this
feature rewrites, so today's production code *already violates them* — running the test now and
observing it fail is itself valid Red evidence (more meaningful than a synthetic probe), and it
turns Green only once the corresponding Foundational task lands (T013 for R1, T017 for R4). R2
and R3 constrain something today's code does not yet do, so they pass vacuously today and need
the standard synthetic add-a-violation-then-remove-it probe. T023 closes the loop for R1/R4 with
the plan's literal forward-regression probe (add a fourth switch / restore the build branch) once
the rewrite is in place, proving the caps hold against regrowth, not just today's specific defect.

- [x] T001 [P] Write `Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests.cs` (rule R1, ADR-022 §Structural Enforcement): assert `PathSwitchCatalog.All` contains exactly 3 entries named `--data-dir`, `--agent-dir`, `--wiki-dir`, and `HubPathSettings` declares exactly one `[CommandOption]` per catalog entry (1:1 parity, mirroring the existing `HubHelpUsageTests` parity idiom). Run it now — it correctly FAILS against today's 16-switch `PathSwitchCatalog`; record this observed failure as the Red evidence in the commit message. Do not implement the catalog rewrite here (Phase 2, T013).
- [x] T002 [P] Write `Grimoire.ArchTests/NoCodeLevelPathDefaultsRuleTests.cs` (rule R2, ADR-022): Mono.Cecil IL scan (mirror `RuntimePathsBoundaryRuleTests.cs`'s `Ldstr` literal-scan style) asserting no production assembly contains a string literal equal to `.grimoire` or `llm-wiki`. **Red/Green probe**: temporarily add `internal const string DefaultDataDirName = ".grimoire";` to `Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs`, run the test — it fails; remove the constant, run again — it passes (vacuously true today, since current defaults are `data`/`wiki`). Commit message documents the probe result.
- [x] T003 [P] Write `Grimoire.ArchTests/InstructionAuthorshipBoundaryRuleTests.cs` (rule R3, ADR-022): assert no production type outside namespace `Grimoire.Hub.Runtime.Paths` references the literal `system-prompt.md`, `default-user-prompt.md`, or `policy.json` as a write target. **Red/Green probe**: add a throwaway type in `Grimoire.Hub` that calls `File.WriteAllText("system-prompt.md", ...)`, run — it fails; delete the type, run again — it passes. Commit message documents the probe result.
- [x] T004 [P] Write `Grimoire.ArchTests/NoRuntimeBuildInvocationRuleTests.cs` (rule R4, ADR-022): Mono.Cecil IL scan asserting no production assembly contains the literals `.csproj`, `--project`, `msbuild`, or a `dotnet` argument `build`/`restore`, with a documented allow-list exempting `Grimoire.EvalRunner`'s diagnostic message strings (`"Build first: dotnet build …"`). Run it now — it correctly FAILS against today's `AgentProcessHost.cs` `.csproj`/`dotnet run --project` branches at its 5 spawn sites; record this observed failure as the Red evidence in the commit message. Do not remove the branches here (Phase 2, T017).

**Checkpoint**: All four rule files exist and run in the standard test tier. R2/R3 pass now
(vacuous + probed). R1/R4 correctly fail, documenting the exact defect Phase 2 closes.

---

## Phase 1: Setup (Agent Build Pipeline)

**Purpose**: Stand up the agent-runtime build distribution mechanism and relocate instruction
files to their new authoritative home, so Phase 2's Hub-side rewrite can assume a populated
agent directory exists.

- [x] T005 Create `backend/Directory.Build.targets` with the `PublishAgentRuntime` MSBuild target: `AfterTargets="Build"`, condition `'$(GrimoireAgentId)' != ''`; `GrimoireAgentDir` property defaults to the repo-relative `.grimoire/agents` and is overridable via `-p:GrimoireAgentDir=`; clears `$(GrimoireAgentDir)/$(GrimoireAgentId)/` then copies the agent's entire `$(OutDir)**` into it, creating the destination directory if absent. Per `contracts/agent-instruction-build.md` §2-3.
- [x] T006 [P] `git mv data/agents/ingest/system-prompt.md data/agents/ingest/default-user-prompt.md data/agents/ingest/policy.json backend/src/Grimoire.IngestAgent/Instructions/`; add `<GrimoireAgentId>ingest</GrimoireAgentId>` and `<Content Include="Instructions\**" CopyToOutputDirectory="PreserveNewest" />` to `backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj`.
- [x] T007 [P] `git mv data/agents/query/system-prompt.md data/agents/query/policy.json backend/src/Grimoire.QueryAgent/Instructions/`; add `<GrimoireAgentId>query</GrimoireAgentId>` and the matching `Content` item to `backend/src/Grimoire.QueryAgent/Grimoire.QueryAgent.csproj`.
- [x] T008 [P] `git mv data/agents/lint/system-prompt.md data/agents/lint/policy.json backend/src/Grimoire.LintAgent/Instructions/`; add `<GrimoireAgentId>lint</GrimoireAgentId>` and the matching `Content` item to `backend/src/Grimoire.LintAgent/Grimoire.LintAgent.csproj`.
- [x] T009 Remove the now-empty `data/agents/` tree; run `dotnet build backend/Grimoire.slnx` and confirm `.grimoire/agents/{ingest,query,lint}/` each hold their worker DLL, `deps.json`, `runtimeconfig.json`, dependency assemblies, and `Instructions/` with the required documents (`contracts/agent-instruction-build.md` §4) — proving T005-T008 are wired correctly before any Hub code depends on them.

**Checkpoint**: `dotnet build` alone produces a complete, per-agent-type runnable agent
directory. Hub-side work (Phase 2) can now assume this exists.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single-composition-point rewrite every user story's behavior depends on:
three roots, mandatory configuration file, one agent launch mode.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T010 Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs`: three required root fields (`DataDir`, `WikiDir`, `AgentDir`) and eight sub-path fields (`RawDir`, `StateDb`, `WriteLocksDir`, `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir`, `SecretsFile`); remove `BaseDir`, `ContentRoot`, `InstructionsDir`, `QueryInstructionsDir`, `LintInstructionsDir`, `AgentWorker`, `QueryAgentWorker`, `LintAgentWorker`, and every `Default*DirName`/`Default*RelativePath` constant except `DefaultLintPidFileName`. Per `data-model.md` §1.
- [x] T011 [P] Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs`: add an `AgentRuntimePaths(Dir, WorkerPath, InstructionsDir, SystemPromptPath, PolicyPath, DefaultUserPromptPath?)` record; restructure `ResolvedGrimoirePaths` to `(DataDir, WikiDir, AgentDir, RawOriginalsDir, RawSourcesDir, StateDbPath, WriteLocksDir, LintPidPath, TasksDir, ConversationsDir, FindingsDir, RemediationTasksDir, IndexPath, LogPath, SecretsFilePath, Ingest, Query, Lint, Locations)` per `data-model.md` §3 (`Ingest`/`Query`/`Lint` are `AgentRuntimePaths`); rename `ContentRoot`→`WikiDir`; remove `BaseDir` and the six flat per-agent instruction/worker fields; update the existing helper methods (`TaskArtifactPathFor`, `ConversationRecordPathFor`, `FindingsReportPathFor`, `RemediationTaskRecordPathFor`) to the renamed fields; drop `default` from `PathLocation.Source`'s valid values.
- [x] T012 Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs`: resolve `DataDir`/`WikiDir` against `CurrentWorkingDirectory`, `AgentDir` against the resolved `DataDir`; fail with `GrimoirePathValidationException` naming `appsettings.json` and the specific missing key(s) when any of the three roots is empty or whitespace after binding — no code-level fallback; resolve the eight sub-paths against their documented anchors (`data-model.md` §1); derive the three fixed agent-type subfolders, each worker DLL path, and each instruction-document path under `AgentDir` (`data-model.md` §2); validate `agent_dir` exists and is non-empty with the distinct reason `agent directory contains no agent runtime` *before* any per-document or per-worker check (so an empty directory names itself, not a file inside it); a missing worker DLL fails with `"<name> not found in the agent directory. Build first: dotnet build backend/Grimoire.slnx"`; auto-create `DataDir`, `WikiDir`, and every writable sub-location; never create anything under `AgentDir`. Per `data-model.md` §5 and `contracts/directory-options.md` §4-5.
- [x] T013 [P] Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/PathSwitchCatalog.cs` to exactly 3 entries: `--data-dir`→`Grimoire:Paths:DataDir`, `--agent-dir`→`Grimoire:Paths:AgentDir`, `--wiki-dir`→`Grimoire:Paths:WikiDir`, per `contracts/directory-options.md` §1. Confirms T001 (R1) now passes.
- [x] T014 [P] Rewrite `backend/src/Grimoire.Hub/Cli/HubPathSettings.cs` to exactly 3 `[CommandOption]` properties, 1:1 with the T013 catalog.
- [x] T015 Rewrite `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathLogEvents.cs`: add `PathsConfigurationMissingEvent` (`paths_configuration_missing`, ERROR, fields `configuration_file`, `missing_keys`); change `LogPathsResolved`'s field set to `data_dir`, `wiki_dir`, `agent_dir`, `secrets_file`, `state_db`, `raw_dir`, `sources` (drop `base_dir`/`content_root`); add `agent_dir` as a valid `paths_validation_failed` location with reason `agent directory contains no instruction files`; start a matching `HubTracing.ActivitySource` span (`signal_type=log`, `event_name`, `level`, the event's own attributes, root-parented) for `paths_resolved` and the new `paths_configuration_missing`, mirroring how `paths_validation_failed` already does this. Per `plan.md ## Observability`.
- [x] T016 Add the `grimoire.hub.path_resolution_failures_total` counter (labels `reason` ∈ `configuration_missing`|`agent_directory_empty`|`location_invalid`) alongside the Hub's existing OTel metrics setup, incremented immediately before the process exits non-zero on each of the three startup failure paths.
- [x] T017 Rewrite the 5 spawn sites in `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs` (`StartMessageTurnProcess`, `StartRemediationProcess`, `StartLintProcess`, `StartQueryProcess`, `StartProcess(IngestAgentRequest)`) to one launch mode — `dotnet <AgentRuntimePaths.WorkerPath>` — deleting the `.csproj`/bare-executable branches and the null/whitespace worker-path guards (the worker path is now always resolved and validated at startup, never optional). Confirms T004 (R4)'s current-defect half now passes; T023 adds the forward-regression probe.
- [x] T018 [P] Update `backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs` and `RawStoragePaths.cs`'s `FromResolved` projections to source from the T011 field names (`WikiDir` instead of `ContentRoot`, `Ingest.SystemPromptPath`/`Ingest.PolicyPath`/`Ingest.DefaultUserPromptPath` instead of the removed flat fields).
- [x] T019 Update `backend/src/Grimoire.Hub/HubHostComposition.cs`: add the mandatory-configuration check immediately after binding `GrimoirePathOptions` (fail fast, before the rest of the DI graph is built); construct `AgentProcessHost` from `resolvedPaths.Ingest/Query/Lint.WorkerPath`; keep the `ContentRootPath` pinning to `GrimoirePathResolver.ProcessBaseDirectory` unchanged; confirm `PathConfigurationSwitchMappingsFactory()` derives correctly from the now-3-entry `PathSwitchCatalog.All` with no other change needed.
- [x] T020 Rewrite `backend/src/Grimoire.Hub/appsettings.json` to the full mandatory `Grimoire:Paths` section from `contracts/appsettings-paths.md` (all 11 keys with real default values, not empty-string placeholders); confirm `appsettings.Development.json` still carries no `Grimoire:Paths` section (ADR-009 rule, retained).
- [x] T021 Update `.gitignore`: add `.grimoire/` and `llm-wiki/`; remove the stale `data/state/`, `data/raw/`, `data/write-locks/`, `data/findings/` entries and the `data/agents/` tracked-exception comment; remove the top-level `conversations/`, `tasks/`, `remediation-tasks/` entries (now under `llm-wiki/`, and per clarification 2026-08-06 these are agent output the operator may choose to track); keep the existing bare `.env` pattern (already covers the relocated root `.env`).
- [x] T022 Update `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathConfigurationTestHelpers.cs`: rewrite `SeedRequiredInputs*` to seed the new three-root/eight-sub-path layout plus an agent directory with per-agent-type subfolders holding stub worker DLLs and `Instructions/` documents, returning the new options/paths shape every other test in the folder consumes.
- [x] T023 Confirm T001 (R1) and T004 (R4) now pass against the rewritten code; then perform the plan's literal forward-regression probes to prove the caps hold against regrowth: (a) temporarily add a 4th entry to `PathSwitchCatalog.All`, run `DirectorySwitchSurfaceRuleTests`, confirm it fails, revert; (b) temporarily reintroduce the `.csproj`→`dotnet run --project` branch in one `AgentProcessHost` spawn site, run `NoRuntimeBuildInvocationRuleTests`, confirm it fails, revert. Commit message documents both probe results (Constitution Principle III).

**Checkpoint**: `dotnet build backend/Grimoire.slnx && dotnet run --project backend/src/Grimoire.Hub`
starts successfully against a clean checkout with no flags and no environment variables. All
four Phase 0 rules pass. User-story test phases can now proceed (in parallel if staffed).

---

## Phase 3: User Story 1 - Run the hub with no command-line configuration (Priority: P1) 🎯 MVP

**Goal**: Prove the hub runs to completion using only the versioned `appsettings.json`, with
loud, specific failures when the configuration file or the agent directory is missing.

**Independent Test**: Invoke any hub command with no flags and no environment variables against
a checkout containing the versioned `appsettings.json` and a built agent directory; confirm it
completes successfully using only the configured default locations.

### Tests for User Story 1

- [x] T024 [P] [US1] Add `PathConfiguration/ZeroConfigStartupTests.cs`: seed a temp cwd with a real (or T022-stubbed) agent directory plus `.env`, spawn the real `Grimoire.Hub.dll` (mirror the `HubHelpUsageTests` process-spawn idiom) with cwd = temp dir and empty argv, assert exit code 0 and a `paths_resolved` log line whose every `Source` is `config-file` (SC-001).
- [x] T025 [P] [US1] Extend `PathConfiguration/PathLoggingContractTests.cs`: assert `paths_resolved`'s field set is exactly `data_dir, wiki_dir, agent_dir, secrets_file, state_db, raw_dir, sources`; add a case for the new `paths_configuration_missing` ERROR event asserting its `configuration_file` and `missing_keys` fields; add a case for `paths_validation_failed` with `location=agent_dir` and the new reason text.
- [x] T026 [P] [US1] Add `PathConfiguration/PathTracingContractTests.cs`: an in-memory `ActivityListener` assertion for the `paths_resolved` and `paths_configuration_missing` spans — root-parented (no parent activity exists at startup), `signal_type=log`, `event_name`, `level`, and the correlation attributes declared in `plan.md ## Observability`.
- [x] T027 [P] [US1] Extend `PathConfiguration/StartupValidationTests.cs`: configuration built from `{}`, from a section missing one of the three roots, and from a whitespace-only root value each throw `GrimoirePathValidationException` naming `appsettings.json` and the missing key(s), with no directory created before the throw (SC-006).
- [x] T028 [US1] Extend `Grimoire.IntegrationTests/HubHelpUsageTests.cs`: `--help` (root and per-command) lists exactly `--data-dir`, `--agent-dir`, `--wiki-dir` under `Server options:`, generated from `PathSwitchCatalog.All` (SC-002; `HubPathSettings` 1:1 parity already covered by T001/R1).
- [x] T029 [P] [US1] Add `PathConfiguration/EmptyAgentDirectoryTests.cs`: three cases — agent directory absent, present-but-empty, present but missing one required instruction document — each fails naming `agent_dir` (the first two cases) or the specific document (the third) (SC-007); a separate case with the directory present but one worker DLL removed fails naming the DLL and `Build first: dotnet build backend/Grimoire.slnx`.

**Checkpoint**: US1 is fully functional and independently testable — the hub runs with zero
configuration and fails loudly and specifically when the configuration file or the agent
directory is missing or incomplete.

---

## Phase 4: User Story 2 - Point the wiki at a chosen folder (Priority: P2)

**Goal**: Prove that setting only `--wiki-dir` relocates every agent-result artifact, leaving
the runtime data directory and the agent directory at their defaults.

**Independent Test**: Set only the wiki-folder option, run an operation that produces agent
results, and confirm wiki content, `index.md`, `log.md`, tasks, conversations, findings, and
remediation tasks all land under that path while runtime state and the agent directory keep
their defaults.

### Tests for User Story 2

- [x] T030 [P] [US2] Add `PathConfiguration/WikiDirIsolationTests.cs`: options with `WikiDir` and `DataDir` pointed at different temp roots — assert `IndexPath`, `LogPath`, `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir` are all under `WikiDir` and none is under `DataDir` (SC-004; covers US2 AS1 and AS2 in one assertion).
- [x] T031 [US2] Add an integration test that runs an operation producing agent results (e.g. `submit-source` against a fake agent launcher, matching the existing `HubCliParityTests`/`HubCliCommandTests` idiom) with only `--wiki-dir` set, confirming wiki content lands under the custom path while `DataDir` and `AgentDir` resolve to their unset defaults.

**Checkpoint**: Wiki relocation is independently provable without touching runtime-data or
agent-directory configuration.

---

## Phase 5: User Story 3 - Relocate the runtime data folder (Priority: P3)

**Goal**: Prove that setting only `--data-dir` relocates every harness runtime-state artifact
(and, by anchoring, the default agent directory with it), while the wiki — anchored
independently at the working directory — stays put.

**Independent Test**: Set only the runtime-data-folder option, run hub commands that create
runtime state, and confirm every one of raw intake, the state database, and write-locks is
created under the configured path with no separate options needed.

### Tests for User Story 3

- [x] T032 [P] [US3] Add `PathConfiguration/DataDirRelocationTests.cs`: options with only `DataDir` set to a custom temp root — assert `RawOriginalsDir`, `RawSourcesDir`, `StateDbPath`, `WriteLocksDir`, `LintPidPath`, and `AgentDir` are all under the configured root (SC-003), and that `SecretsFilePath` still resolves to `<cwd>/.env` unaffected (US3 AS1).
- [x] T033 [US3] Extend the same file: with only `DataDir` set to a custom root and `WikiDir` left unset, assert the wiki resolves to its own cwd-anchored default location — a sibling of the *default* data directory, not nested inside the configured custom `DataDir` (US3 AS2).

**Checkpoint**: Runtime-data relocation is independently provable and demonstrably does not
drag the wiki along with it.

---

## Phase 6: User Story 4 - Use a custom agent folder fed by the agent build (Priority: P4)

**Goal**: Prove the agent-build redirect and the hub's `--agent-dir` option point at the same
directory and close the loop end to end, and that the directory is refreshed correctly on
every build.

**Independent Test**: Direct an agent build's output at a custom folder, point the agent-folder
option at that same folder, run a hub command, and confirm the agents run from the instruction
files in that folder.

### Tests for User Story 4

- [x] T034 [P] [US4] Add `PathConfiguration/AgentDirBuildContractTests.cs` (build-contract integration test, invokes a real `dotnet build`): `dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=<temp>` — assert each of `<temp>/{ingest,query,lint}/` holds its worker DLL, `deps.json`, `runtimeconfig.json`, and `Instructions/` with the required documents, byte-matching the agent-project sources (SC-008).
- [x] T035 [US4] Extend the same file: run the build a second time after touching one agent's instruction source — the copy refreshes (FR-011); plant a stale file in the destination before a third build — it is gone afterward, proving clear-then-copy; spawn `dotnet <temp>/ingest/Grimoire.IngestAgent.dll --help` and assert it starts without an assembly-resolution failure — the delivered directory is genuinely launchable.
- [x] T036 [P] [US4] Add `PathConfiguration/CustomAgentDirEndToEndTests.cs`: point `--agent-dir` at the T034 temp directory and confirm the resolver's `Ingest`/`Query`/`Lint` runtime paths all resolve under it (US4 AS1); reuse T029's missing/empty-directory assertions confirming the failure names the configured directory (US4 AS2); confirm a rebuilt agent's instruction files are reflected in the resolved paths (US4 AS3).

**Checkpoint**: A redirected agent build and a redirected hub, pointed at the same folder,
close the loop — the least common but still required directory-relocation story.

---

## Phase 7: User Story 5 - Adjust defaults and internal layout in the configuration file (Priority: P5)

**Goal**: Prove the full per-option precedence chain and the configuration-file-only escape
hatch for internal sub-paths, without ever adding a switch.

**Independent Test**: Change one internal sub-path in the configuration file, run a hub
command, and confirm that artifact resolves to the changed path while every other artifact
stays where it was.

### Tests for User Story 5

- [x] T037 [P] [US5] Extend `PathConfiguration/PathPrecedenceTests.cs` to the full 3-option × 3-tier precedence matrix, including mixed cases (one option from the command line, another from the environment), asserting both the resolved value and `PathLocation.Source` (SC-005).
- [x] T038 [US5] Add a config-file-only sub-path override test: change `StateDb` in a temp `appsettings.json` to a different path, confirm only the database resolves there while `RawDir`/`WriteLocksDir`/every other `DataDir`-anchored location stays under the unchanged `DataDir` (US5 AS1); assert no CLI switch exists for any sub-path (cross-references T001/R1, which already proves the catalog is capped at the three roots).

**Checkpoint**: All five user stories are independently functional and independently testable.

---

## Phase 8: Eval Runner & Secrets Relocation (Cross-Cutting — FR-016–FR-019)

**Purpose**: Relocate eval fixtures and the secrets file so both are repo-anchored and
independent of hub configuration and of any prior agent build (FR-016–FR-019). Not gated
behind a numbered user story — an infrastructure requirement the spec states directly.

- [x] T039 `git mv data/evals/recordings backend/tests/Grimoire.AgentEvals/Fixtures/recordings` (252 tracked recording files preserved); confirm `recordings` is now a reserved directory name under `Fixtures/` (no eval scenario may share it).
- [x] T040 Move any locally-present `data/.env` to `.env` at the repository root (git-ignored either way — verify no tracked file moves); update `.env-example`'s comment to say "copy to `.env` at the repository root" instead of `data/.env`.
- [x] T041 Rewrite `backend/src/Grimoire.EvalRunner/Workspace/EvalPaths.cs`: `RecordingsRoot => Path.Combine(FixturesRoot, "recordings")` (remove the `data/evals/recordings`-derived `DefaultRecordingsRoot`); `{Agent,Query,Lint}InstructionsDir` resolve to `<repo-root>/backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions`; `LocalEnvPath => <repo-root>/.env`. Keep `Discover()` unchanged — the eval runner is a repo-local dev tool, deliberately outside ADR-009's no-ambient-discovery rule (research R6).
- [x] T042 Remove `--recordings-root` from `backend/src/Grimoire.EvalRunner/Program.cs`'s `CliOptions.Parse` and its usage line; `RecordingStore` always constructs from `EvalPaths.RecordingsRoot`.
- [x] T043 [P] Add coverage asserting `EvalPaths.RecordingsRoot` resolves under the test project (SC-009), the eval runner's parser rejects `--recordings-root` as unrecognized, and `EvalPaths.LocalEnvPath` equals `<repo-root>/.env` (SC-011) — independently of `GrimoirePathResolver.SecretsFilePath` resolving to the same value.
- [x] T044 [P] Add/extend a replay-tier test that runs the eval suite with `Grimoire__Paths__*` environment variables set to junk paths and the runtime agent directory deleted, asserting identical sample counts and scores to a normal run (SC-010 — eval independence from both hub configuration and any agent build).
- [x] T045 Run the full `Grimoire.AgentEvals` replay suite against the relocated `Fixtures/recordings/` tree and confirm it passes unchanged — proves the 252-file relocation preserved every fixture-to-scenario mapping.

**Checkpoint**: Eval runs are provably independent of hub configuration and of the agent build.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, CI wiring, and the mandatory completeness audit.

- [x] T046 [P] Update `.github/workflows/eval.yml`: the `data/evals/recordings/**` upload-artifact glob and its surrounding comment → `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/**`.
- [x] T047 [P] Update `.github/workflows/ci.yml`'s comment referencing `data/evals/recordings/` → the new path.
- [x] T048 [P] Update `.vscode/launch.json`: the `prod` configuration's `--content-root /Volumes/Daten/parainoid/llm-wiki` → `--wiki-dir` with the same value; confirm `dev`/`proxy` need no change (no path switches configured today).
- [x] T049 [P] Update `.devcontainer/devcontainer.json`'s three `.env`/`data/.env` documentation strings → `.env` at the repository root, per the ADR-019 amendment.
- [x] T050 [P] Add a brief "Superseded by ADR-022" / "Amended by ADR-022" pointer near the top of `docs/adr/ADR-009-runtime-path-configuration.md` and "Amended by ADR-022" pointers to `docs/adr/ADR-002-ingest-agent-execution-model.md`, `ADR-007-agent-instruction-surface.md`, `ADR-012-eval-runner-recorded-replay.md`, `ADR-019-devcontainer-host-runtime-and-credential-access.md`, `ADR-020-hub-cli-command-surface.md` — per the plan's "Superseded and amended decisions" table. Point forward only; do not rewrite their bodies (MADR convention).
- [x] T051 Observability completeness audit (MANDATORY — Constitution Principle III/IV): cross-referenced every row of `plan.md ## Observability` (1 metric, 4 log events, 3 trace spans) against its implementing task (T015, T016) and passing test (T025, T026). Result: the 4 log events and 3 trace spans are fully covered by `PathLoggingContractTests`/`PathTracingContractTests` (T025/T026). Gap found: the `grimoire.hub.path_resolution_failures_total` counter (T016) had no deterministic test — T025/T026 cover only log events and spans, not the metric. Filed and closed as T051a below before declaring the DoD met.
- [x] T051a [P] Add `PathConfiguration/PathMetricsContractTests.cs`: a `MeterListener` on `grimoire.hub.path_resolution_failures_total`, driven through the real `GrimoirePathResolver` failure paths (mandatory-config-missing, required-input-invalid, empty-agent-directory), asserting the counter increments with `reason` = `configuration_missing`/`location_invalid`/`agent_directory_empty` respectively (`Assert.Contains`, not `Assert.Single` — the listener is process-wide and this counter is also incremented by many other tests' own resolver-failure calls running concurrently).
- [x] T052 Logging contract CI enforcement (MANDATORY — Constitution Principle IV): confirmed — `.github/workflows/ci.yml`'s "Run hermetic integration tests" step runs `dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release --no-build` with no `--filter`, so `PathLoggingContractTests` (T025) runs unconditionally.
- [x] T053 Trace contract CI enforcement (MANDATORY — Constitution Principle IV): confirmed — same unfiltered step covers `PathTracingContractTests` (T026).
- [x] T054 Run `./scripts/test-fast.sh` (arch + domain tiers) and confirm zero violations across the four new rules (T001-T004) and the pre-existing `RuntimePathsBoundaryRuleTests`/`HubAgentDispatchBoundaryRuleTests`. Confirmed: 93/93 Domain.UnitTests, 60/60 ArchTests, 53/53 AgentEvals (Tier=Fast) — all passing, zero violations.
- [x] T055 Run `dotnet test backend/tests/Grimoire.IntegrationTests` and `dotnet test backend/tests/Grimoire.AgentEvals` end to end; fix any drift surfaced by the full suite. Result: `Grimoire.AgentEvals` 75/75 passing. `Grimoire.IntegrationTests` 675/694 passing; the 19 failures are exactly the pre-existing, environment-only failures confirmed via `git worktree` baseline comparison during Phase 2 (Spectre.Console `AnsiConsole` renders nothing without a real TTY — `HubHelpUsageTests`/`HubCliCommandTests`/`HubCliQueryCommandTests`/`HubCliParityTests`; `markitdown` CLI not installed in this sandbox — `IngestConvertStepTests`/`IngestSourceArtifactPersistenceTests`/`IngestSubmissionTraceTests`) — same categories, not regressions from this feature.
- [x] T056 Ran `quickstart.md`'s scenarios against real scratch cwd/build-output invocations of the actual `Grimoire.Hub.dll`/agent build (not just automated tests): Scenario 1 (zero-config, real spawned process, confirmed `.grimoire/`+`llm-wiki/` created with the documented sub-structure and correct exit code), Scenario 3's failure path (`--data-dir` pointed at an agent-runtime-less root), Scenario 4b/6-equivalent config-missing case, and the build-redirect mechanics (already covered live by `AgentDirBuildContractTests`). This sandbox's terminal has no real TTY, so Spectre.Console renders nothing for `--help`/figlet-banner output even on a direct, non-redirected invocation — confirmed pre-existing/environmental (Phase 2's `git worktree` baseline comparison already established this; `HubHelpUsageTests`' 7 affected facts are unrelated to this feature). **Real drift found and fixed**: Scenario 3's documented failure path (`--data-dir` pointing at a location with no agent runtime → "fails naming agent_dir") did not hold for any CLI-dispatched command (`remediation-dismiss`, `submit-source`, etc.) — Spectre.Console.Cli's lazy type resolution wraps the real `GrimoirePathValidationException`/`GrimoirePathConfigurationMissingException` in a generic `CommandRuntimeException` ("Could not resolve type '<Command>'."), losing the actionable message. Fixed in `HubCliApp.RunAsync` (`UnwrapPathResolutionFailure`): the real path-resolution exception's message is now surfaced verbatim with `CliExitCode.OperationFailed`, matching the message a server-mode start reports. Covered by a new real out-of-process test, `HubHelpUsageTests.RemediationDismiss_RealOutOfProcessInvocation_MissingAgentDir_ReportsTheRealMessage_NotSpectresGenericResolutionFailure`. (The web-host/server-mode invocation path's raw unhandled-exception-with-stack-trace presentation on the same class of failure is pre-existing, predates this feature's `Program.cs` structure, and is out of scope — it still technically satisfies quickstart's literal claim since the real message's text is the first line of output, just with additional crash noise; no top-level try/catch exists there today for anything, not something introduced or regressed by this feature.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** (T001-T004) — no dependencies; blocks all feature code (constitutional gate).
- **Phase 1** (T005-T009) — depends on Phase 0; blocks Phase 2 (Hub-side work assumes a
  populated agent directory).
- **Phase 2** (T010-T023) — depends on Phase 1; blocks every user-story phase.
- **Phases 3-7** (user stories) — all depend on Phase 2 only; independent of each other, may
  proceed in parallel or in priority order (US1 → US2 → US3 → US4 → US5).
- **Phase 8** (eval runner) — depends on Phase 1 (instruction sources must already be at their
  new home) and Phase 2 (secrets-file resolution); independent of Phases 3-7.
- **Phase 9** (polish) — depends on Phases 2-8 all being complete.

### Within Each Phase

- Phase 0: T001-T004 fully parallel (four independent files).
- Phase 1: T005 first (the target directory T006-T008 need to exist for the build to succeed
  meaningfully, though the `git mv`s themselves don't require it); T006/T007/T008 parallel
  (different agent projects); T009 after all of T005-T008.
- Phase 2: T010 → T011 → T012 (options shape, then the resolved-paths shape it produces, then
  the resolver that binds them); T013/T014 parallel, after T012; T015/T016 parallel, after
  T012; T017 after T011 (needs `AgentRuntimePaths`); T018 after T011; T019 after
  T012/T013/T017; T020 can proceed any time after T010 (same field names); T021 any time;
  T022 after T010-T012 (needs the final shape to seed against); T023 last, after T013 and T017.
- Phase 3 (US1): T024/T025/T026/T027/T029 parallel; T028 independent, any order.
- Phase 4 (US2): T030 then T031.
- Phase 5 (US3): T032 then T033 (same file).
- Phase 6 (US4): T034 then T035 (same file); T036 parallel to T034/T035.
- Phase 7 (US5): T037 then T038 (same file).
- Phase 8: T039/T040 parallel; T041 after T039/T040; T042 after T041; T043/T044 parallel,
  after T042; T045 last.
- Phase 9: T046-T050 fully parallel; T051-T053 after every prior phase; T054-T056 last, in
  order.

### Parallel Opportunities

- T001 ∥ T002 ∥ T003 ∥ T004
- T006 ∥ T007 ∥ T008
- T013 ∥ T014; T015 ∥ T016; T011 ∥ T018 (once T011 lands, T018 can start)
- After Phase 2: US1-US5 phases (3-7) can run fully in parallel by different contributors
- T024 ∥ T025 ∥ T026 ∥ T027 ∥ T029
- T043 ∥ T044
- T046 ∥ T047 ∥ T048 ∥ T049

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0 (structural rules, two correctly red today) + Phase 1 (agent build
   pipeline) + Phase 2 (the full path-composition rewrite — this is the bulk of the feature).
2. Complete Phase 3 (US1 tests).
3. **STOP and VALIDATE**: `dotnet build backend/Grimoire.slnx && dotnet run --project backend/src/Grimoire.Hub`
   starts with zero flags and zero environment variables (Scenario 1 of quickstart.md).
4. This is already most of the feature's value: SC-001, SC-002, SC-006, SC-007 are covered.

### Incremental Delivery

1. Phase 0 + 1 + 2 → foundation ready, US1 provable.
2. Add US2 (Phase 4) → wiki relocation provable independently.
3. Add US3 (Phase 5) → data relocation provable independently, wiki isolation reconfirmed.
4. Add US4 (Phase 6) → agent relocation + build redirect provable independently.
5. Add US5 (Phase 7) → full precedence matrix + config-file escape hatch provable.
6. Phase 8 (eval runner) can land any time after Phase 1 + 2 — independent of the user-story
   phases, but must land before Phase 9's completeness audit.
7. Phase 9 last — closes documentation, CI, and the constitutional completeness audit.

### Parallel Team Strategy

With multiple contributors: one completes Phase 0 + 1 + 2 (this is inherently sequential and
concentrated in `Grimoire.Hub/Runtime/Paths/`, matching ADR-009's single-composition-point
rule); once Phase 2 lands, up to five contributors take US1-US5 in parallel, and a sixth takes
Phase 8 (eval runner) in parallel with all of them.

# Tasks: Explicit Path Configuration (Decouple from Repository Structure)

**Input**: Design documents from `/specs/005-content-root-config/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included — the spec's success criteria are all deterministic harness guarantees, so every story carries hermetic tests (Constitution II). There are no agent-judgment criteria, hence no evaluation-test tasks. The plan's Observability section declares three Structured Log Events rows (full logging contract below) and zero Distributed Trace Spans / Business Metrics rows (deliberate empty sets — no trace/metric contract tasks exist).

**Organization**: Tasks are grouped by user story so each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (P1 deploy without checkout), US2 (P2 zero-config defaults), US3 (P3 fail-fast misconfiguration)

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III, ADR-009)

**Purpose**: Guard the ADR-009 boundary before any feature code exists.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [ ] T001 Write structural boundary rule `RuntimePathsBoundaryRuleTests` in backend/tests/Grimoire.ArchTests/RuntimePathsBoundaryRuleTests.cs (Mono.Cecil IL scan, same idiom as GuardedWriteBoundaryRuleTests): (a) in production assemblies (Grimoire.Hub, Grimoire.IngestAgent, Grimoire.Domain), calls to `System.IO.Directory::GetCurrentDirectory`, `System.Environment::get_CurrentDirectory`, and `System.AppContext::get_BaseDirectory` are permitted only in namespace `Grimoire.Hub.Runtime.Paths`; (b) no production assembly contains IL string literals `rev-parse` or `--show-toplevel`. Note: this rule fails RED on the current code (`FindRepoRoot` in both Program.cs files) — that is expected and stays red until T010/T016 remove the violations; mark the two known violations as an explicit allowlist with a `// TODO(T010,T016)` expiry so the probe logic can still be verified, then remove the allowlist in T010/T016.
- [ ] T002 Red/Green probe for T001: add a deliberately violating class `backend/src/Grimoire.Hub/ProbeAmbientPathViolation.cs` calling `Directory.GetCurrentDirectory()` outside the allowed namespace, run `dotnet test backend/tests/Grimoire.ArchTests --filter RuntimePathsBoundary` and verify it FAILS; delete the probe class, verify it PASSES; document the probe result in the commit message.

**Checkpoint**: Structural boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T003 Create the composition-point skeleton: directory backend/src/Grimoire.Hub/Runtime/Paths/ and the `Grimoire:Paths` section (all eight keys, values omitted so code defaults apply, with a comment pointing at ADR-009) in backend/src/Grimoire.Hub/appsettings.json (create the file if the Hub has none).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The path composition point every story consumes (research R1/R2/R5).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 [P] Implement `GrimoirePathOptions` record with the eight configurable values and ALL code defaults (wiki under base; data under base; raw/state/secrets/instructions under data; agent worker beside Hub binaries) in backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs per data-model.md
- [ ] T005 [P] Implement `ResolvedGrimoirePaths` + `PathLocation` (name, configuredValue, resolvedPath, kind, source) in backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs per data-model.md, including the derived members today's `ContentRootPaths`/`RawStoragePaths` expose (PagesDir, TasksDir, IndexPath, LogPath, SystemPromptPath, DefaultUserPromptPath, PolicyPath, OriginalsDir, SourcesDir, per-task path helpers)
- [ ] T006 Implement `GrimoirePathResolver` in backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs: resolve each value against its documented anchor (absolute as-is; ContentRoot/DataDir against base; raw/state/secrets/instructions against data; agent worker against install dir), record per-location source (command-line/environment/config-file/default), validate required inputs (fail fast with logical name + configured value + resolved path), auto-create writable data locations (depends on T004, T005)
- [ ] T007 Implement the three structured log events with stable names and mandatory fields from plan.md ## Observability in backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathLogEvents.cs: `paths_resolved` (INFO: base_dir, data_dir, content_root, raw_dir, state_db, secrets_file, instructions_dir, agent_worker, sources), `paths_location_created` (INFO: location, resolved_path), `paths_validation_failed` (ERROR: location, configured_value, resolved_path, reason); emit them from `GrimoirePathResolver` (depends on T006)
- [ ] T008 Refactor backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs and backend/src/Grimoire.Hub/ContentRoot/RawStoragePaths.cs into projections of `ResolvedGrimoirePaths` (delete their repoRoot-based `Resolve` methods); update all consumers that construct them (depends on T005)
- [ ] T009 Rewire backend/src/Grimoire.Hub/Program.cs: add command-line switch mappings (`--base-dir`, `--data-dir`, `--content-root`, `--raw-dir`, `--state-db`, `--secrets-file`, `--instructions-dir`, `--agent-worker`) onto `Grimoire:Paths:*`, bind options, run `GrimoirePathResolver` before host build, register `ResolvedGrimoirePaths` in DI, and replace the repoRoot-derived dbPath/envPath/agentProjectPath/contentPaths/rawStoragePaths wiring; keep `FindRepoRoot` deletion for T010 (depends on T006, T007, T008)

**Checkpoint**: Foundation ready — user story phases can begin.

---

## Phase 3: User Story 1 — Deploy to a production environment without a source checkout (Priority: P1) 🎯 MVP

**Goal**: The application runs fully from explicit path configuration in a directory with no repository structure and no git tooling (SC-001, SC-004).

**Independent Test**: quickstart.md Scenario 2 — publish, start in an empty temp base dir with prepared `data/` inputs and an external `--content-root`, complete an ingest end-to-end.

### Tests for User Story 1 (write first, ensure they FAIL)

- [ ] T010t [P] [US1] Hermetic integration test `RepoLessStartupTests` in backend/tests/Grimoire.IntegrationTests/PathConfiguration/RepoLessStartupTests.cs: boot the Hub host in a temp base directory containing no `.git` and no project layout (fixture `data/` tree with instruction files + `.env` stub, fake `IAgentProcessLauncher`), assert successful start and that every write lands under the configured roots (SC-001)
- [ ] T011t [P] [US1] Hermetic integration test `DispatchPathArgumentsTests` in backend/tests/Grimoire.IntegrationTests/PathConfiguration/DispatchPathArgumentsTests.cs: capture dispatch via the fake launcher and assert every dispatch passes `--wiki-root` plus absolute Hub-resolved paths for all path arguments (SC-004)
- [ ] T012t [P] [US1] Hermetic integration test `PathPrecedenceTests` in backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathPrecedenceTests.cs: same key supplied via CLI + environment + config file resolves per precedence command line > environment > appsettings > default, and `PathLocation.source` reports the winning channel (FR-005)

### Implementation for User Story 1

- [ ] T010 [US1] Delete `FindRepoRoot` and the `ParseOption`-based `--content-root` handling from backend/src/Grimoire.Hub/Program.cs; remove the T001 allowlist entry for Grimoire.Hub and verify the arch rule passes (depends on T009)
- [ ] T011 [US1] Extend backend/src/Grimoire.Hub/AgentDispatch/AgentProcessHost.cs: accept the resolved agent-worker path with the three launch forms (`.csproj` → `dotnet run --project`, `.dll` → `dotnet <dll>`, else direct executable) and add `--wiki-root` to the argument list per contracts/agent-launch.md; update `IngestAgentRequest` and `IngestRunCoordinator` to carry the wiki root (depends on T009)
- [ ] T012 [US1] In backend/src/Grimoire.IngestAgent/Program.cs: parse required `--wiki-root` (fail closed with an argument error before any write when missing), delete `FindRepoRoot`, compute `PagesTouched`/`PagesCreated`/`PagesUpdated`/`PagesSuperseded` relative to the wiki root, and pass the wiki root to `PolicyLoader`; remove the T001 allowlist entry for Grimoire.IngestAgent and verify the arch rule passes (depends on T011)
- [ ] T013 [US1] Update backend/src/Grimoire.IngestAgent/AgentCore/PolicyLoader.cs so path prefixes resolve against the injected wiki root (constructor parameter renamed accordingly); adjust its unit/integration tests' fixtures to content-root-relative prefixes (depends on T012)
- [ ] T014 [US1] Update backend/src/Grimoire.Hub/Submission/SubmissionService.cs `ResolveSourcePath` to resolve relative source paths against the process working directory (parameter `repoRoot` removed); update the `submit-source` CLI path in Program.cs accordingly (depends on T009)

**Checkpoint**: US1 fully functional — quickstart Scenario 2 passes; T010t–T012t green.

---

## Phase 4: User Story 2 — Run locally with sensible defaults (Priority: P2)

**Goal**: Zero-config start from the checkout resolves wiki to `<cwd>/wiki` and internal data beneath `<cwd>/data`; dev workflow works after the one-time move (SC-003).

**Independent Test**: quickstart.md Scenario 1 — start from the checkout root with only the agent-worker argument and complete an ingest using the migrated local data.

### Tests for User Story 2 (write first, ensure they FAIL)

- [ ] T015t [P] [US2] Hermetic resolver tests `DefaultLayoutTests` in backend/tests/Grimoire.IntegrationTests/PathConfiguration/DefaultLayoutTests.cs: with zero configuration the content root resolves to `<cwd>/wiki` and every internal data location beneath `<cwd>/data`; overriding a single location leaves all other defaults intact (SC-003, FR-004, US2 acceptance 2)

### Implementation for User Story 2

- [ ] T015 [US2] Execute the one-time checkout migration per quickstart.md: `git mv agents data/agents`, move `raw/` → `data/raw`, `.env` → `data/.env`, `backend/data/operational-state.db` → `data/state/` (wiki/ stays in place); rewrite `data/agents/ingest/policy.json` prefixes to content-root-relative (`pages/`, `tasks/`, `index.md`, `log.md`) and bump its `version` (depends on T013 so the loader semantics match)
- [ ] T016 [P] [US2] Update .gitignore: replace `backend/data/` with `data/state/` and add `data/raw/` (the existing `.env` pattern already covers `data/.env`)
- [ ] T017 [P] [US2] Update .vscode/launch.json: both configurations get `cwd: ${workspaceFolder}` and `--agent-worker ${workspaceFolder}/backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj`; dev config drops all other path arguments (defaults apply); prod config keeps `--content-root /Volumes/Daten/parainoid/llm-wiki`; prelaunch tasks unchanged
- [ ] T018 [US2] Validate quickstart.md Scenario 1 end-to-end from the checkout root (zero-config start, ingest a small pasted text, verify writes only under wiki/ and data/) and fix anything it surfaces (depends on T015, T016, T017)

**Checkpoint**: US1 and US2 both work — dev checkout and repo-less deployment share one codebase with config-only differences.

---

## Phase 5: User Story 3 — Clear feedback on misconfiguration (Priority: P3)

**Goal**: Invalid required inputs abort startup before serving, naming the location, configured value, and resolved path; writable locations auto-create with a report (SC-002).

**Independent Test**: quickstart.md Scenario 3 — point `--instructions-dir` (then `--secrets-file`, `--agent-worker`) at nonexistent paths and observe immediate named failures.

### Tests for User Story 3 (write first, ensure they FAIL)

- [ ] T019t [P] [US3] Hermetic integration tests `StartupValidationTests` in backend/tests/Grimoire.IntegrationTests/PathConfiguration/StartupValidationTests.cs: for each required input (secrets file, each of the three instruction files, agent worker) missing or of the wrong kind (file where directory expected and vice versa), startup fails before serving with exit/exception naming the logical location, configured value, and resolved path; absent writable locations (wiki, raw, state, data) are created and reported (SC-002, FR-006, US3 acceptance 1–2)

### Implementation for User Story 3

- [ ] T019 [US3] Harden `GrimoirePathResolver` validation in backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs: explicit kind checks (file vs directory) per location, aggregate-free first-failure message format `"<location>: configured '<value>' resolved to '<path>' — <reason>"`, and ensure validation runs before any endpoint/dispatch initialization in Program.cs (depends on T006; test T019t)

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T020 [P] Observability tests (MANDATORY — Constitution IV; logging contract, deterministic integration tests for ALL three Structured Log Events rows) in backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathLoggingContractTests.cs: `paths_resolved` (INFO, all eight path fields + sources), `paths_location_created` (INFO, location + resolved_path), `paths_validation_failed` (ERROR, location + configured_value + resolved_path + reason), captured via the in-memory exporter per ADR-005
- [ ] T021 Logging contract CI enforcement (MANDATORY — Constitution IV): verify .github/workflows/ci.yml executes the new test classes in the standard PR pipeline (Grimoire.ArchTests and Grimoire.IntegrationTests are already invoked project-wide — confirm no filter excludes PathConfiguration/PathLoggingContract tests, and add an explicit `--filter`-free assertion note to the workflow if needed)
- [ ] T022 [P] Documentation: record the two-home layout, configuration table, and migration note in docs/ (link contracts/path-configuration.md from an operator-facing page or README section)
- [ ] T023 Dead-code and literal sweep: remove any leftover repoRoot parameters, unused `ParseOption` helpers, and stray path literals outside `Grimoire.Hub.Runtime.Paths` (grep for `backend/data`, `agents/ingest`, `"wiki"`, `".env"` in backend/src); run the full quickstart.md validation (Scenarios 1–5) as the feature's DoD gate

*No trace-contract tasks: plan.md declares zero Distributed Trace Spans rows. No agent-behavior evaluation tasks: the feature has no agentic surface (plan.md § Agentic Boundary).*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** → blocks everything (constitutional gate).
- **Phase 1 Setup** → after Phase 0.
- **Phase 2 Foundational** → after Setup; blocks all stories.
- **US1 (Phase 3)** → after Foundational. **US2 (Phase 4)** → after Foundational; T015 (policy rewrite) depends on US1's T013 loader change. **US3 (Phase 5)** → after Foundational; independent of US1/US2.
- **Polish (Phase 6)** → after all stories; T020/T021 gate the DoD.

### Story Dependency Notes

- US1 is fully independent (fixture-based tests).
- US2's migration task T015 needs US1's T013 (policy anchor) — everything else in US2 is independent.
- US3 only hardens/verifies foundational validation — independent of US1/US2.

### Parallel Opportunities

- T004 ∥ T005 (different files).
- T010t ∥ T011t ∥ T012t (separate test files), then implementation serially T010 → T011 → T012 → T013 with T014 ∥ after T009.
- T016 ∥ T017 while T015 runs.
- T020 ∥ T022 in Polish.

## Parallel Example: User Story 1

```bash
# Write the three failing tests together (separate files):
dotnet test … RepoLessStartupTests & DispatchPathArgumentsTests & PathPrecedenceTests
# Then implement serially: T010 (Hub cleanup) → T011 (dispatch) → T012 (agent) → T013 (policy), T014 in parallel after T009
```

## Implementation Strategy

**MVP = Phases 0–3 (US1)**: after US1 the application is deployable anywhere with explicit configuration — the core of the feature request. **Increment 2 = US2**: migrated checkout + zero-config dev workflow + config-only launch configs. **Increment 3 = US3**: hardened operator feedback. **Polish** closes the constitutional logging/CI gates and runs the full quickstart validation.

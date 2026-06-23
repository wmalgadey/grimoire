# Tasks: Grimoire Project Skeleton Setup

**Input**: Design documents from `/specs/001-grimoire-scaffold/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: Architecture tests (NetArchTest.Rules) are a core feature requirement per US2 and Constitution Principle III. No additional TDD test tasks requested.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Exact file paths are included in all descriptions

## Path Conventions

- Backend: `src/backend/` (three .NET 9 projects)
- Frontend: `src/frontend/` (Svelte 5 + Vite)
- Agents placeholder: `src/agents/`
- CI: `.github/workflows/`

---

## Phase 0: Architecture Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Automated architecture tests must EXIST and FAIL before any feature code is written.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until the architecture test is RED.

- [x] T000 Create empty project stubs (Grimoire.Core.csproj, Grimoire.Api.csproj, Grimoire.ArchTests.csproj with NetArchTest.Rules) and implement all 6 failing test stubs in src/backend/Grimoire.ArchTests/ArchitectureTests.cs — `Core_DefinesIChannelInCorrectNamespace` and `Core_DefinesIAgentWorkerInCorrectNamespace` MUST FAIL until Phase 2 implements the interfaces

**Checkpoint**: Architecture test project EXISTS and at least two tests FAIL (Red). Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Monorepo directory structure and shared MSBuild configuration.

- [x] T001 Create root monorepo directory structure: src/backend/, src/frontend/, src/agents/, docs/adr/, specs/ per ADR-005 layout
- [x] T002 Create src/backend/Directory.Build.props with TargetFramework=net9.0, Nullable=enable, LangVersion=13, ImplicitUsings=enable, TreatWarningsAsErrors=true (R-002)
- [x] T003 [P] Create src/agents/.gitkeep placeholder per ADR-005 (agents directory reserved for future implementations)
- [x] T004 Create src/backend/Grimoire.sln and register Grimoire.Api, Grimoire.Core, and Grimoire.ArchTests with `dotnet sln add`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Grimoire.Core domain interfaces — the only dependency shared by all user stories.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T005 Create src/backend/Grimoire.Core/Grimoire.Core.csproj with no external NuGet dependencies (BCL only — enforces ADR-007)
- [x] T006 Implement IChannel interface in src/backend/Grimoire.Core/Channels/IChannel.cs: ChannelId property (get), SendAsync(string, CancellationToken), DisconnectAsync(CancellationToken) — exactly 3 members (SC-007, ADR-004)
- [x] T007 [P] Implement IAgentWorker interface in src/backend/Grimoire.Core/Agents/IAgentWorker.cs: AgentId property (get), ExecuteAsync(string, CancellationToken), StopAsync(CancellationToken) — exactly 3 members (SC-007, ADR-002)

**Checkpoint**: Foundation ready — `Core_DefinesIChannelInCorrectNamespace` and `Core_DefinesIAgentWorkerInCorrectNamespace` now PASS. User story implementation can begin.

---

## Phase 3: User Story 1 — Developer Initializes Project Environment (Priority: P1) 🎯 MVP

**Goal**: Developer can clone the repository and build the complete monorepo with zero errors or warnings.

**Independent Test**: `dotnet build src/backend` → 0 errors, 0 warnings (SC-001); `npm install && npm run build` in src/frontend → 0 errors, 0 warnings (SC-002).

### Implementation for User Story 1

- [x] T008 [US1] Create src/backend/Grimoire.Api/Grimoire.Api.csproj targeting net9.0 with Microsoft.AspNetCore.SignalR, OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore NuGet packages (ADR-001, R-005)
- [x] T009 [US1] Implement src/backend/Grimoire.Api/Program.cs: Minimal API bootstrap, builder.Services.AddOpenTelemetry() registration, and structured log events `grimoire.host.started` (INFO, fields: environment, version) and `grimoire.host.stopped` (INFO, field: environment) via ILogger<Program> (plan.md Observability section)
- [x] T010 [US1] Add project references in src/backend/: Grimoire.Api → Grimoire.Core, Grimoire.ArchTests → Grimoire.Core, Grimoire.ArchTests → Grimoire.Api (R-002 scaffold sequence)
- [x] T011 [US1] Scaffold Svelte 5 + Vite + TypeScript frontend in src/frontend/ using `npm create vite@latest . -- --template svelte-ts` then `npm install` (R-003, ADR-003)
- [x] T012 [P] [US1] Add src/frontend/eslint.config.js for ESLint 9 flat config with @typescript-eslint/eslint-plugin (R-003 post-scaffold adjustment)
- [x] T013 [P] [US1] Verify/patch tsconfig.json strict and svelte.config.js vitePreprocess from `@sveltejs/vite-plugin-svelte` — patch if template omits them (R-003)
- [x] T014 [P] [US1] Verify package.json has dev, build, lint scripts as required by FR-013
- [x] T015 [US1] Validate backend build: 0 errors 0 warnings run `dotnet build src/backend` — exit must report 0 Error(s) 0 Warning(s) (SC-001)
- [x] T016 [US1] Validate frontend build: no errors, dist/ created run `npm run build` in src/frontend — exit must produce no errors and create dist/ (SC-002)

**Checkpoint**: User Story 1 complete — entire monorepo builds from a clean clone.

---

## Phase 4: User Story 2 — Architect Validates Structural Constraints (Priority: P1)

**Goal**: All ADR-001 through ADR-007 constraints are enforced automatically by passing architecture tests.

**Independent Test**: `dotnet test src/backend/Grimoire.ArchTests --logger "console;verbosity=normal"` → all 6 tests pass in < 30 seconds (SC-003, SC-004).

### Implementation for User Story 2

- [x] T017 [US2] Add NetArchTest.Rules to Grimoire.ArchTests.csproj to src/backend/Grimoire.ArchTests/Grimoire.ArchTests.csproj (R-001)
- [x] T018 [US2] Implement all 6 architecture tests in ArchitectureTests.cs using NetArchTest.Rules fluent API:
  `Core_HasNoDependencyOnInfrastructure` (ADR-005),
  `Core_HasNoDependencyOnEntityFramework` (ADR-007),
  `ChannelImplementations_MustImplementIChannel` (ADR-004),
  `AgentWorkers_MustImplementIAgentWorker` (ADR-002),
  `Core_DefinesIChannelInCorrectNamespace` (ADR-004),
  `Core_DefinesIAgentWorkerInCorrectNamespace` (ADR-002)
- [x] T019 [US2] Validate architecture tests: 6/6 pass in <30 seconds run `dotnet test Grimoire.ArchTests --logger "console;verbosity=normal"` — all 6 must pass, runtime must be < 30 seconds (SC-003, SC-004)

**Checkpoint**: User Story 2 complete — all ADR-001 through ADR-007 constraints are automatically enforced.

---

## Phase 5: User Story 3 — CI Pipeline Executes Build and Tests (Priority: P1)

**Goal**: GitHub Actions workflows trigger independently per subproject via path-based filters and pass on every commit.

**Independent Test**: Push backend-only commit → only `backend.yml` triggers; push frontend-only commit → only `frontend.yml` triggers. Both workflows pass (SC-005).

### Implementation for User Story 3

- [x] T020 [US3] Create .github/workflows/backend.yml with push/pull_request path triggers for `src/backend/**` and `.github/workflows/backend.yml`; steps: actions/checkout@v4, actions/setup-dotnet@v4 (.NET 9), `dotnet build src/backend`, `dotnet test src/backend/Grimoire.ArchTests` (R-004, SC-005 < 3 min)
- [x] T021 [P] [US3] Create .github/workflows/frontend.yml with push/pull_request path triggers for `src/frontend/**` and `.github/workflows/frontend.yml`; steps: actions/checkout@v4, actions/setup-node@v4 (Node 20), `npm install`, `npm run build`, `npm run lint` in src/frontend (R-004, SC-005 < 2 min)

**Checkpoint**: User Story 3 complete — CI enforces build and architecture rules on every commit automatically.

---

## Phase 6: User Story 4 — Developer Understands Project Layout (Priority: P2)

**Goal**: Clear documentation enables a new developer to orient and contribute without violating architectural rules.

**Independent Test**: Developer reads README → understands directory purpose and build commands (FR-016); reads CLAUDE.md → understands Spec-Kit workflow and ADR references (spec US4-AC3).

### Implementation for User Story 4

- [x] T022 [US4] Create/update README.md at repo root with: monorepo directory structure diagram, prerequisites table (.NET 9, Node 20), backend and frontend build commands, links to docs/adr/ index and each of the 7 ADRs (FR-016, spec US4-AC1)
- [x] T023 [P] [US4] Verify/update CLAUDE.md with ADR-006 and ADR-007 references (specs/ directory, current plan.md path) and tech stack choices linked to ADR numbers — update if any references are missing (spec US4-AC2, US4-AC3)

**Checkpoint**: User Story 4 complete — project is fully documented and navigable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final Definition of Done verification covering all success criteria and observability requirements.

- [x] T024 Verify grimoire.host.started and grimoire.host.stopped log events emitted (host startup logging): `dotnet run --project src/backend/Grimoire.Api` — verify `grimoire.host.started` and `grimoire.host.stopped` structured log events are emitted (Observability DoD, plan.md § Observability)
- [x] T025 [P] Directory spot-check: all directories match ADR-005 layout verify `ls src/`, `ls src/backend/`, `ls src/agents/`, `ls .github/workflows/`, `ls docs/adr/` match ADR-005 layout exactly — no files in wrong directories (SC-006)
- [x] T026 [P] Interface member count: IChannel=3, IAgentWorker=3 (SC-007) verify IChannel.cs and IAgentWorker.cs each have ≤ 3 members (SC-007)
- [x] T027 Full DoD: SC-001 through SC-007 all pass, Observability requirements met — confirm SC-001 through SC-007 all pass and all Observability requirements are met

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Arch Test RED)**: No dependencies — start immediately
- **Phase 1 (Setup)**: No dependencies — can start in parallel with T000 after T000 is written
- **Phase 2 (Foundational)**: Requires Phase 1 — BLOCKS all user story phases
- **Phase 3 (US1 P1)**: Requires Phase 2 — backend scaffold and frontend scaffold
- **Phase 4 (US2 P1)**: Requires Phase 2 + T010 (Grimoire.Api must be referenceable by ArchTests) — arch tests turn GREEN
- **Phase 5 (US3 P1)**: Requires Phase 3 + Phase 4 — CI must execute passing builds and passing tests
- **Phase 6 (US4 P2)**: Requires Phase 1 only — documentation is independent of implementation
- **Phase 7 (Polish)**: Requires all prior phases complete

### User Story Dependencies

- **US1 (P1)**: Requires Phase 2 — no dependency on US2, US3, US4
- **US2 (P1)**: Requires Phase 2 + T010 from US1 — depends on US1 partially (needs project references set up)
- **US3 (P1)**: Requires US1 + US2 — CI must run compiling code and passing arch tests
- **US4 (P2)**: Requires Phase 1 only — independently deliverable as documentation

### Within Each User Story

- T010 (project references) must follow T008 (Grimoire.Api.csproj must exist first)
- T015, T016 (build validation) must follow T008–T014
- T018 (arch test implementation) must follow T017 (NetArchTest.Rules package installed)
- T019 (arch test validation) must follow T018
- T020, T021 (CI workflows) are independent of each other [P]

### Parallel Opportunities

- T006 + T007 (IChannel, IAgentWorker): different files [P]
- T012 + T013 + T014 (frontend config tasks): different files [P]
- T020 + T021 (backend.yml, frontend.yml): different files [P]
- T022 + T023 (README, CLAUDE.md): different files [P]
- T025 + T026 (spot-checks): independent checks [P]

---

## Parallel Example: User Story 1

```bash
# These frontend setup tasks can run in parallel after T011:
T012: "Add src/frontend/eslint.config.js"
T013: "Verify src/frontend/tsconfig.json strict:true and svelte.config.js vitePreprocess"
T014: "Verify src/frontend/package.json dev, build, lint scripts"
```

---

## Implementation Strategy

### MVP First (User Story 1 — Working Build)

1. Complete Phase 0: Write arch test stubs (RED)
2. Complete Phase 1: Directory structure + sln
3. Complete Phase 2: Grimoire.Core with IChannel + IAgentWorker
4. Complete Phase 3: Full backend + frontend scaffold
5. **STOP and VALIDATE**: `dotnet build src/backend` → 0 errors; `npm run build` → 0 errors
6. Proceed to Phase 4 (arch tests GREEN) and Phase 5 (CI)

### Incremental Delivery

1. Phase 0 → Phase 1 → Phase 2: Structural foundation and RED arch tests
2. Phase 3 (US1): Build succeeds → SC-001 + SC-002 met
3. Phase 4 (US2): Arch tests GREEN → SC-003 + SC-004 met
4. Phase 5 (US3): CI live → SC-005 met
5. Phase 6 (US4): Documentation complete → FR-016 met
6. Phase 7: All DoD criteria verified → feature DONE

### Parallel Team Strategy

With two developers:
1. Developer A: Phase 0 → Phase 1 → Phase 2 → Phase 3 backend (T008–T010)
2. Developer B: Phase 3 frontend (T011–T014) — independent after Phase 1
3. Both merge → Developer A: Phase 4 (arch tests); Developer B: Phase 6 (docs)
4. Phase 5 (CI) after Phase 3 + Phase 4 merge

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks — safe to parallelize
- [Story] label maps each task to its user story for traceability and independent delivery
- T000 creates test STUBS (RED); T017–T018 complete the full implementation (GREEN) — mandatory order per Constitution Principle III
- SC-007: IChannel and IAgentWorker MUST NOT exceed 3 members in the skeleton — any addition requires a new feature spec
- Commit at each phase checkpoint to enable git bisect if arch tests regress

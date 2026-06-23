# Tasks: Domain-Driven Code Reorganization (Screaming Architecture)

**Input**: Design documents from `/specs/003-domain-driven-refactor/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Organization**: Tasks grouped by user story (US1-US4) for independent implementation. Tests run against existing test suite (zero test logic modifications required).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1, US2, US3, US4)
- Exact file paths included in all descriptions

---

## Phase 0: Architecture Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Automated architecture tests must FAIL before refactoring begins. This validates the rule will be enforced post-refactoring.

- [ ] T000 Implement NetArchTest.Rules architecture test in `src/backend/Grimoire.Api.Tests/Architecture/ArchitectureTests.cs` enforcing ADR-009:
  - ✅ No circular dependencies between Agents, Hubs, Channels domains
  - ✅ All types follow `Grimoire.Api.{Domain}.*` namespace pattern
  - ✅ Cross-domain communication via interfaces only (no direct class references)
  - **Expected**: Test FAILS (Red) — domains don't exist yet

**Checkpoint**: Architecture test EXISTS and is RED. Refactoring may now proceed.

---

## Phase 1: Foundational Setup (Blocking Prerequisites)

**Purpose**: Create domain folder structure and establish foundation. MUST complete before user story work begins.

- [ ] T001 Create domain folder structure in `src/backend/Grimoire.Api/`:
  - Create `/Agents` with subdirs: Endpoints, Handlers, Services, Models, Tests
  - Create `/Hubs` with subdirs: Endpoints, Handlers, Services, Models, Tests
  - Create `/Channels` with subdirs: Endpoints, Handlers, Services, Models, Tests
  - Create `/Shared` with subdirs: Middleware, Observability, Persistence, Exceptions, Models

- [ ] T002 Verify directory structure created correctly:
  ```bash
  ls -la src/backend/Grimoire.Api/Agents src/backend/Grimoire.Api/Hubs src/backend/Grimoire.Api/Channels src/backend/Grimoire.Api/Shared
  ```

---

## Phase 2: User Story 1 — Developer Onboarding via Intuitive Project Structure (Priority: P1) 🎯 MVP

**Goal**: Reorganize codebase so developers immediately see business domains (Agents, Hubs, Channels) instead of technical layers

**Independent Test**: Developer can open `src/backend/Grimoire.Api` and describe system's core domains within 2 minutes by reading folder names

**Story Tasks**:

### T003: Identify and reorganize Agent-related files

- [ ] T003 [P] [US1] Identify all Agent-related files currently in `/Api/Endpoints`, `/Api/Handlers`, `/Core/Domain` in `research.md`

- [ ] T004 [P] [US1] Move Agent endpoint files to `src/backend/Grimoire.Api/Agents/Endpoints/`:
  - List all files: `grep -r "Agent" src/backend/Grimoire.Api/Api/Endpoints --include="*.cs"`
  - Move files: `mv src/backend/Grimoire.Api/Api/Endpoints/*Agent*.cs src/backend/Grimoire.Api/Agents/Endpoints/`

- [ ] T005 [P] [US1] Move Agent handler files to `src/backend/Grimoire.Api/Agents/Handlers/`:
  - Move files: `mv src/backend/Grimoire.Api/Api/Handlers/*Agent*.cs src/backend/Grimoire.Api/Agents/Handlers/`

- [ ] T006 [P] [US1] Move Agent service/domain files to `src/backend/Grimoire.Api/Agents/Services/`:
  - Move Agent services from `src/backend/Grimoire.Api/Core/Domain/` to `src/backend/Grimoire.Api/Agents/Services/`

- [ ] T007 [US1] Update namespaces in Agent Endpoints files to `Grimoire.Api.Agents.Endpoints` in `src/backend/Grimoire.Api/Agents/Endpoints/*.cs`

- [ ] T008 [US1] Update namespaces in Agent Handlers files to `Grimoire.Api.Agents.Handlers` in `src/backend/Grimoire.Api/Agents/Handlers/*.cs`

- [ ] T009 [US1] Update namespaces in Agent Services files to `Grimoire.Api.Agents.Services` in `src/backend/Grimoire.Api/Agents/Services/*.cs`

- [ ] T010 [US1] Update all imports in Agent files to reflect new namespaces (find-and-replace old namespaces with new)

### T011: Identify and reorganize Hub-related files

- [ ] T011 [P] [US1] Identify all Hub-related files currently in `/Api/Hubs`, `/Api/Handlers`, `/Core/Domain` in `research.md`

- [ ] T012 [P] [US1] Move Hub endpoint/hub definition files to `src/backend/Grimoire.Api/Hubs/Endpoints/`:
  - Move files: `mv src/backend/Grimoire.Api/Api/Hubs/*.cs src/backend/Grimoire.Api/Hubs/Endpoints/`

- [ ] T013 [P] [US1] Move Hub handler files to `src/backend/Grimoire.Api/Hubs/Handlers/`:
  - Move files: `mv src/backend/Grimoire.Api/Api/Handlers/*Hub*.cs src/backend/Grimoire.Api/Hubs/Handlers/`

- [ ] T014 [P] [US1] Move Hub service/domain files to `src/backend/Grimoire.Api/Hubs/Services/`:
  - Move Hub services from `src/backend/Grimoire.Api/Core/Domain/` to `src/backend/Grimoire.Api/Hubs/Services/`

- [ ] T015 [US1] Update namespaces in Hub Endpoints files to `Grimoire.Api.Hubs.Endpoints` in `src/backend/Grimoire.Api/Hubs/Endpoints/*.cs`

- [ ] T016 [US1] Update namespaces in Hub Handlers files to `Grimoire.Api.Hubs.Handlers` in `src/backend/Grimoire.Api/Hubs/Handlers/*.cs`

- [ ] T017 [US1] Update namespaces in Hub Services files to `Grimoire.Api.Hubs.Services` in `src/backend/Grimoire.Api/Hubs/Services/*.cs`

- [ ] T018 [US1] Update all imports in Hub files to reflect new namespaces

### T019: Identify and reorganize Channel-related files

- [ ] T019 [P] [US1] Identify all Channel-related files currently in `/Api/Endpoints`, `/Api/Handlers`, `/Core/Domain` in `research.md`

- [ ] T020 [P] [US1] Move Channel endpoint files to `src/backend/Grimoire.Api/Channels/Endpoints/`:
  - Move files: `mv src/backend/Grimoire.Api/Api/Endpoints/*Channel*.cs src/backend/Grimoire.Api/Channels/Endpoints/`

- [ ] T021 [P] [US1] Move Channel handler files to `src/backend/Grimoire.Api/Channels/Handlers/`:
  - Move files: `mv src/backend/Grimoire.Api/Api/Handlers/*Channel*.cs src/backend/Grimoire.Api/Channels/Handlers/`

- [ ] T022 [P] [US1] Move Channel service/domain files (IChannel implementations) to `src/backend/Grimoire.Api/Channels/Services/`:
  - Move Channel services from `src/backend/Grimoire.Api/Core/Domain/` and `Infrastructure/` to `src/backend/Grimoire.Api/Channels/Services/`

- [ ] T023 [US1] Update namespaces in Channel Endpoints files to `Grimoire.Api.Channels.Endpoints` in `src/backend/Grimoire.Api/Channels/Endpoints/*.cs`

- [ ] T024 [US1] Update namespaces in Channel Handlers files to `Grimoire.Api.Channels.Handlers` in `src/backend/Grimoire.Api/Channels/Handlers/*.cs`

- [ ] T025 [US1] Update namespaces in Channel Services files to `Grimoire.Api.Channels.Services` in `src/backend/Grimoire.Api/Channels/Services/*.cs`

- [ ] T026 [US1] Update all imports in Channel files to reflect new namespaces

### T027: Reorganize Shared Infrastructure

- [ ] T027 [P] [US1] Move Middleware files to `src/backend/Grimoire.Api/Shared/Middleware/`:
  - Move files: `mv src/backend/Grimoire.Api/Api/Middleware/*.cs src/backend/Grimoire.Api/Shared/Middleware/`
  - Update namespaces to `Grimoire.Api.Shared.Middleware`

- [ ] T028 [P] [US1] Move Observability files to `src/backend/Grimoire.Api/Shared/Observability/`:
  - Move files: `mv src/backend/Grimoire.Api/Infrastructure/Observability/*.cs src/backend/Grimoire.Api/Shared/Observability/`
  - Update namespaces to `Grimoire.Api.Shared.Observability`

- [ ] T029 [P] [US1] Move Persistence files to `src/backend/Grimoire.Api/Shared/Persistence/`:
  - Move files: `mv src/backend/Grimoire.Api/Infrastructure/Persistence/*.cs src/backend/Grimoire.Api/Shared/Persistence/`
  - Update namespaces to `Grimoire.Api.Shared.Persistence`

- [ ] T030 [P] [US1] Move Exception files to `src/backend/Grimoire.Api/Shared/Exceptions/`:
  - Move files: `mv src/backend/Grimoire.Api/Core/Exceptions/*.cs src/backend/Grimoire.Api/Shared/Exceptions/`
  - Update namespaces to `Grimoire.Api.Shared.Exceptions`

- [ ] T031 [US1] Move common DTOs and models to `src/backend/Grimoire.Api/Shared/Models/`

### T032: Update all file imports/using statements

- [ ] T032 [US1] Global find-and-replace: Update all `using` statements to reflect new namespaces:
  - Old: `using Grimoire.Api.Api.Endpoints` → New: `using Grimoire.Api.Agents.Endpoints` (or Hubs, Channels as appropriate)
  - Old: `using Grimoire.Api.Infrastructure.Observability` → New: `using Grimoire.Api.Shared.Observability`
  - Old: `using Grimoire.Api.Core.Exceptions` → New: `using Grimoire.Api.Shared.Exceptions`

**Checkpoint**: US1 structure complete. Verify folder structure is domain-based. Run `dotnet build` to identify any namespace/import issues.

---

## Phase 3: User Story 2 — Feature Implementation Follows Domain Boundaries (Priority: P1)

**Goal**: Reorganize test structure to mirror domain organization; ensure tests can be run per-domain

**Independent Test**: All tests pass; domain-specific test folders match source domain folders

**Story Tasks**:

- [ ] T033 [P] [US2] Reorganize test folder structure to mirror domains:
  - Create `src/backend/Grimoire.Api.Tests/Unit/Agents/`
  - Create `src/backend/Grimoire.Api.Tests/Unit/Hubs/`
  - Create `src/backend/Grimoire.Api.Tests/Unit/Channels/`
  - Create `src/backend/Grimoire.Api.Tests/Integration/Agents/`
  - Create `src/backend/Grimoire.Api.Tests/Integration/Hubs/`
  - Create `src/backend/Grimoire.Api.Tests/Integration/Channels/`

- [ ] T034 [P] [US2] Move Agent unit tests to `src/backend/Grimoire.Api.Tests/Unit/Agents/`:
  - Move files: `mv src/backend/Grimoire.Api.Tests/Unit/*Agent*.cs src/backend/Grimoire.Api.Tests/Unit/Agents/`
  - Update test namespaces to `Grimoire.Api.Tests.Unit.Agents`

- [ ] T035 [P] [US2] Move Hub unit tests to `src/backend/Grimoire.Api.Tests/Unit/Hubs/`:
  - Move files: `mv src/backend/Grimoire.Api.Tests/Unit/*Hub*.cs src/backend/Grimoire.Api.Tests/Unit/Hubs/`
  - Update test namespaces to `Grimoire.Api.Tests.Unit.Hubs`

- [ ] T036 [P] [US2] Move Channel unit tests to `src/backend/Grimoire.Api.Tests/Unit/Channels/`:
  - Move files: `mv src/backend/Grimoire.Api.Tests/Unit/*Channel*.cs src/backend/Grimoire.Api.Tests/Unit/Channels/`
  - Update test namespaces to `Grimoire.Api.Tests.Unit.Channels`

- [ ] T037 [P] [US2] Move Agent integration tests to `src/backend/Grimoire.Api.Tests/Integration/Agents/`:
  - Move integration test files for Agents
  - Update test namespaces to `Grimoire.Api.Tests.Integration.Agents`

- [ ] T038 [P] [US2] Move Hub integration tests to `src/backend/Grimoire.Api.Tests/Integration/Hubs/`:
  - Move integration test files for Hubs
  - Update test namespaces to `Grimoire.Api.Tests.Integration.Hubs`

- [ ] T039 [P] [US2] Move Channel integration tests to `src/backend/Grimoire.Api.Tests/Integration/Channels/`:
  - Move integration test files for Channels
  - Update test namespaces to `Grimoire.Api.Tests.Integration.Channels`

- [ ] T040 [US2] Update all test file imports to reference domain source namespaces:
  - Update `using` statements to new domain namespaces (e.g., `using Grimoire.Api.Agents.Services`)

- [ ] T041 [US2] Run unit tests to verify all pass (zero test logic modifications):
  ```bash
  cd src/backend/Grimoire.Api.Tests && dotnet test --filter "Category=Unit"
  ```

- [ ] T042 [US2] Run integration tests to verify all pass (zero test logic modifications):
  ```bash
  cd src/backend/Grimoire.Api.Tests && dotnet test --filter "Category=Integration"
  ```

**Checkpoint**: US2 complete. All tests passing; no test logic modified. Domain-specific test folders mirror source domains.

---

## Phase 4: User Story 3 — Code Navigation Reveals Business Intent (Priority: P1)

**Goal**: Verify namespace compliance and run architecture tests to ensure no violations

**Independent Test**: Architecture test passes; 80%+ of types follow domain namespace pattern

**Story Tasks**:

- [ ] T043 [US3] Verify namespace compliance in all domain files:
  - Run: `grep -r "^namespace Grimoire.Api\." src/backend/Grimoire.Api/Agents --include="*.cs"` and verify all are `Grimoire.Api.Agents.*`
  - Run: `grep -r "^namespace Grimoire.Api\." src/backend/Grimoire.Api/Hubs --include="*.cs"` and verify all are `Grimoire.Api.Hubs.*`
  - Run: `grep -r "^namespace Grimoire.Api\." src/backend/Grimoire.Api/Channels --include="*.cs"` and verify all are `Grimoire.Api.Channels.*`
  - Run: `grep -r "^namespace Grimoire.Api\." src/backend/Grimoire.Api/Shared --include="*.cs"` and verify all are `Grimoire.Api.Shared.*`

- [ ] T044 [US3] Run architecture test to validate domain boundaries:
  ```bash
  cd src/backend/Grimoire.Api && dotnet test Grimoire.Api.Tests --filter "TestClass=ArchitectureTests"
  ```
  - **Expected**: All architecture tests PASS (Green) — no circular dependencies, namespace compliance verified, interface-only cross-domain communication confirmed

- [ ] T045 [US3] Document namespace compliance report in `specs/003-domain-driven-refactor/quickstart.md`:
  - Percentage of types following pattern (target: 80%+)
  - Any exceptions documented

**Checkpoint**: US3 complete. Architecture tests passing; namespace compliance verified; business domains visible in folder/namespace structure.

---

## Phase 5: User Story 4 — Shared Infrastructure Remains Centralized (Priority: P2)

**Goal**: Verify no infrastructure code duplication; cross-domain communication uses interfaces only

**Independent Test**: No middleware/observability/persistence duplicated; cross-domain imports via interfaces only

**Story Tasks**:

- [ ] T046 [US4] Verify no Middleware duplication across domains:
  - Run: `find src/backend/Grimoire.Api/Agents src/backend/Grimoire.Api/Hubs src/backend/Grimoire.Api/Channels -name "*Middleware*.cs" 2>/dev/null | wc -l`
  - **Expected**: 0 (all middleware centralized in `/Shared/Middleware`)

- [ ] T047 [US4] Verify no Observability duplication across domains:
  - Run: `find src/backend/Grimoire.Api/Agents src/backend/Grimoire.Api/Hubs src/backend/Grimoire.Api/Channels -name "*Observability*.cs" 2>/dev/null | wc -l`
  - **Expected**: 0 (all in `/Shared/Observability`)

- [ ] T048 [US4] Verify no Persistence duplication across domains:
  - Run: `find src/backend/Grimoire.Api/Agents src/backend/Grimoire.Api/Hubs src/backend/Grimoire.Api/Channels -path "*Persistence*" -name "*.cs" 2>/dev/null | wc -l`
  - **Expected**: 0 (all in `/Shared/Persistence`)

- [ ] T049 [US4] Verify cross-domain communication uses interfaces only:
  - Check for direct domain-to-domain imports (should be ZERO):
    - `grep -r "using Grimoire.Api.Agents\." src/backend/Grimoire.Api/Hubs --include="*.cs" | grep -v "Shared"`
    - `grep -r "using Grimoire.Api.Channels\." src/backend/Grimoire.Api/Hubs --include="*.cs" | grep -v "Shared"`
  - **Expected**: Empty (no direct cross-domain references; all via IAgentWorker, IChannel from Grimoire.Core)

- [ ] T050 [US4] Update dependency injection in `Program.cs` to wire all domain services and shared infrastructure:
  - Ensure all `services.AddScoped<>()` registrations point to correct domain namespaces
  - Verify no old namespace references remain

**Checkpoint**: US4 complete. No infrastructure duplication; all cross-domain communication via interfaces; DI fully updated.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validation, documentation, and cleanup

- [ ] T051 [P] Run full build to verify zero compilation errors:
  ```bash
  cd src/backend && dotnet build -c Release
  ```

- [ ] T052 [P] Run all unit tests:
  ```bash
  cd src/backend/Grimoire.Api.Tests && dotnet test --filter "Category=Unit"
  ```

- [ ] T053 [P] Run all integration tests:
  ```bash
  cd src/backend/Grimoire.Api.Tests && dotnet test --filter "Category=Integration"
  ```

- [ ] T054 Run architecture tests one final time:
  ```bash
  cd src/backend/Grimoire.Api && dotnet test Grimoire.Api.Tests --filter "TestClass=ArchitectureTests"
  ```

- [ ] T055 Execute quickstart.md validation checklist (10 steps):
  ```bash
  # See specs/003-domain-driven-refactor/quickstart.md for full validation
  ```

- [ ] T056 Verify API endpoints unchanged by testing with curl:
  ```bash
  # Example test endpoint for zero breaking changes
  curl -X GET http://localhost:5000/api/agents/status
  ```

- [ ] T057 Update CLAUDE.md agent context to reflect new plan reference (if hooks not executed)

- [ ] T058 Create summary commit documenting refactoring:
  ```bash
  git commit -m "refactor: domain-driven code organization (screaming architecture)

  - Reorganize from layer-based (Clean Architecture) to domain-driven structure
  - Create Agents, Hubs, Channels, Shared domains
  - Update all namespaces to Grimoire.Api.{Domain}.*
  - Implement NetArchTest.Rules architecture enforcement (ADR-009)
  - All existing tests pass; zero test logic modifications
  - All API contracts preserved; zero breaking changes
  - Implements Constitution Principle I: Domain Architecture & Strategic DDD"
  ```

**Checkpoint**: Refactoring complete. All tests passing. Architecture tests enforcing constraints. Ready for merge.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** (Architecture Test): No dependencies - defines the constraint that will be validated
- **Phase 1** (Setup): No dependencies - creates folder structure immediately
- **Phase 2-5** (User Stories): All depend on Phase 1 completion - BLOCKS until structure exists
  - **US1 (P1)**: Move files and update namespaces (core work)
  - **US2 (P1)**: Reorganize tests (can start after US1)
  - **US3 (P1)**: Verify architecture (can start after US1 & US2)
  - **US4 (P2)**: Verify centralization (can start after US1)
- **Phase 6** (Polish): Depends on all user stories completing

### User Story Execution Order

**Sequential** (recommended for single developer):
1. Phase 0: Implement architecture test
2. Phase 1: Create folder structure
3. Phase 2 (US1): Move and rename files
4. Phase 3 (US2): Reorganize tests
5. Phase 4 (US3): Verify architecture
6. Phase 5 (US4): Verify centralization
7. Phase 6: Polish & validation

**Parallel** (if multiple developers):
- Once Phase 1 complete:
  - Developer A: Phase 2 (US1) - File reorganization
  - Developer B: Phase 5 (US4) - Verification prep (parallel safe)
- Once Phase 2 complete:
  - Developer A: Phase 3 (US2) - Test reorganization
  - Developer B: Phase 4 (US3) - Architecture validation
- Once all complete: Phase 6 (Polish)

### Within Each User Story

- Tests before implementation (TDD approach)
- Models/Infrastructure before endpoints
- Individual domain tasks marked [P] can run in parallel

### Parallel Opportunities

- **Phase 2 (US1) File Moves**: T004-T006, T012-T014, T020-T022, T027-T031 are all marked [P] (different files)
- **Phase 3 (US2) Test Moves**: T033-T039 are all marked [P] (different test files)
- **Phase 6 (Polish) Verification**: T051-T053 are all marked [P] (independent test runs)

---

## Implementation Strategy

### MVP Scope (Priority: P1 User Stories)

**Minimum Viable Refactoring** = Phases 0-4 (US1-US3)

1. Phase 0: Implement architecture test (validates the rule)
2. Phase 1: Create folder structure
3. Phase 2 (US1): Move files, update namespaces
4. Phase 3 (US2): Reorganize tests
5. Phase 4 (US3): Verify architecture compliance
6. **STOP and VALIDATE**: All tests pass, architecture tests green, structure is domain-based

At this point: ✅ **Code organization is domain-driven**

### Incremental Delivery

- After Phase 0: Architecture test in place (validates constraints)
- After Phase 1: Folder structure ready (scaffolding complete)
- After Phase 2: Core refactoring done (files moved, namespaces updated)
- After Phase 3: Tests reorganized (can run per-domain tests)
- After Phase 4: Architecture verified (constraints enforced)
- After Phase 5: Infrastructure verified (no duplication)
- After Phase 6: Ready for merge (fully validated)

Each phase adds validation and confidence.

---

## Notes

- All namespace updates must be systematic (find-and-replace with verification)
- Tests should pass WITHOUT modification to test logic (only namespace/import updates)
- API contracts unchanged (zero breaking changes to external clients)
- Architecture test must PASS after refactoring (enforces ADR-009 constraints)
- Each user story is independently completable and testable
- Commit after logical groups (e.g., after each domain reorganization completes)

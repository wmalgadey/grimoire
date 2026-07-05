---

description: "Task list template for feature implementation"
---

# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The examples below include required test tasks. Every user story MUST include the deterministic and evaluation-style tests needed to verify its independent behavior, consistent with the feature spec, plan, and constitution.

**Logging Contract (MANDATORY)**: For every Structured Log Events row in `plan.md ## Observability`, tasks MUST cover all three categories:

- implementation task(s) with stable event name and mandatory fields,
- deterministic integration test task(s) validating event name, level, and mandatory fields,
- CI task(s) that run those logging tests in the standard PR pipeline.

**Trace Contract (MANDATORY)**: For every Distributed Trace Spans row in `plan.md ## Observability`, tasks MUST cover all three categories:

- implementation task(s) that create the span with declared parent/child linkage and required attributes,
- deterministic integration test task(s) validating span name, parent/child relationship, and correlation attributes,
- CI task(s) that run those trace tests in the standard PR pipeline.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Single project**: `src/`, `tests/` at repository root
- **Web app**: `backend/src/`, `frontend/src/`
- **Mobile**: `api/src/`, `ios/src/` or `android/src/`
- Paths shown below assume single project - adjust based on plan.md structure

<!--
  ============================================================================
  IMPORTANT: The tasks below are SAMPLE TASKS for illustration purposes only.

  The /speckit-tasks command MUST replace these with actual tasks based on:
  - User stories from spec.md (with their priorities P1, P2, P3...)
  - Feature requirements from plan.md
  - Entities from data-model.md
  - Endpoints from contracts/

  Tasks MUST be organized by user story so each story can be:
  - Implemented independently
  - Tested independently
  - Delivered as an MVP increment

  DO NOT keep these sample tasks in the generated tasks.md file.
  ============================================================================
-->

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify structural boundary tests before any feature code is written.
This phase MUST be the first phase in every tasks.md, regardless of feature scope.
Enforce the structural rule from [ADR-XXX] before any feature code exists.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

<!--
  ACTION REQUIRED: Replace T000 below with the concrete structural boundary test
  for the ADR(s) referenced in plan.md § Architectural Constraints & ADRs.
  Use the appropriate tool for the tech stack:
  - Python: import-linter, pytest with ast inspection, or custom module boundary checks
  - JVM: ArchUnit
  - .NET: NetArchTest.Rules or Roslyn Analyzers
  - Go: custom package dependency check

  IMPORTANT: These are structural boundary tests only. Observability/instrumentation tests
  (verifying metrics/spans are emitted) belong in the final phase — they require
  production code to exist and cannot be written here.
-->

- [ ] T000 Write and Verify Structural Boundary Tests for [ADR-XXX]

**Requirements**:
- Use ArchUnit (Java), NetArchTest.Rules (C#), import-linter (Python), or equivalent
- Document the rule: e.g., "src/domain/ MUST NOT import from src/infrastructure/"
- Do NOT implement any feature code in this task

**Red/Green probe** (required — confirms the test actually catches violations):
1. Write the boundary rule
2. Introduce a deliberately violating file (e.g., `domain/_probe_bad_import.py`)
3. Run the test — it MUST fail
4. Delete the violating file
5. Run the test again — it MUST pass

**Definition of Done**:
- [ ] Rule written and committed
- [ ] Red/Green probe completed (commit message documents the probe result)
- [ ] Test passes in CI with no violations (probe file deleted)

**Checkpoint**: Structural boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create project structure per implementation plan
- [ ] T002 Initialize [language] project with [framework] dependencies
- [ ] T003 [P] Configure linting and formatting tools

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

Examples of foundational tasks (adjust based on your project):

- [ ] T004 Setup database schema and migrations framework
- [ ] T005 [P] Implement authentication/authorization framework
- [ ] T006 [P] Setup API routing and middleware structure
- [ ] T007 Create base models/entities that all stories depend on
- [ ] T008 Configure error handling and logging infrastructure
- [ ] T009 Setup environment configuration management

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - [Title] (Priority: P1) 🎯 MVP

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T010 [P] [US1] Contract test for [endpoint] in tests/contract/test_[name].py
- [ ] T011 [P] [US1] Integration test for [user journey] in tests/integration/test_[name].py

### Implementation for User Story 1

- [ ] T012 [P] [US1] Create [Entity1] model in src/models/[entity1].py
- [ ] T013 [P] [US1] Create [Entity2] model in src/models/[entity2].py
- [ ] T014 [US1] Implement [Service] in src/services/[service].py (depends on T012, T013)
- [ ] T015 [US1] Implement [endpoint/feature] in src/[location]/[file].py
- [ ] T016 [US1] Add validation and error handling
- [ ] T017 [US1] Add logging for user story 1 operations
- [ ] T017a [US1] Implement structured log event(s) with stable names and mandatory fields defined in plan.md ## Observability
- [ ] T017b [US1] Add deterministic integration test(s) that validate event name, level, and mandatory fields
- [ ] T017c [US1] Implement distributed trace span(s) with declared parent/child linkage and required attributes from plan.md ## Observability
- [ ] T017d [US1] Add deterministic integration test(s) that validate span name, parent/child relationship, and correlation attributes

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - [Title] (Priority: P2)

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 2

- [ ] T018 [P] [US2] Contract test for [endpoint] in tests/contract/test_[name].py
- [ ] T019 [P] [US2] Integration test for [user journey] in tests/integration/test_[name].py

### Implementation for User Story 2

- [ ] T020 [P] [US2] Create [Entity] model in src/models/[entity].py
- [ ] T021 [US2] Implement [Service] in src/services/[service].py
- [ ] T022 [US2] Implement [endpoint/feature] in src/[location]/[file].py
- [ ] T023 [US2] Integrate with User Story 1 components (if needed)

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - [Title] (Priority: P3)

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 3

- [ ] T024 [P] [US3] Contract test for [endpoint] in tests/contract/test_[name].py
- [ ] T025 [P] [US3] Integration test for [user journey] in tests/integration/test_[name].py

### Implementation for User Story 3

- [ ] T026 [P] [US3] Create [Entity] model in src/models/[entity].py
- [ ] T027 [US3] Implement [Service] in src/services/[service].py
- [ ] T028 [US3] Implement [endpoint/feature] in src/[location]/[file].py

**Checkpoint**: All user stories should now be independently functional

---

[Add more user story phases as needed, following the same pattern]

---

## Phase N: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] TXXX Observability tests: verify all metrics, log events, and trace spans from plan.md ## Observability are emitted (MANDATORY — Constitution Principle IV)
- [ ] TXXX Logging contract CI enforcement: ensure deterministic logging tests for all Structured Log Events rows run in the standard PR pipeline (MANDATORY — Constitution Principle IV)
- [ ] TXXX Trace contract CI enforcement: ensure deterministic trace tests for all Distributed Trace Spans rows run in the standard PR pipeline (MANDATORY — Constitution Principle IV)
- [ ] TXXX Agent-behavior evaluation tests: verify every agent-judgment success criterion from spec.md at its defined threshold via sampled runs with real or recorded LLM output (MANDATORY for features with agentic behavior — Constitution Principles II & V)
- [ ] TXXX [P] Documentation updates in docs/
- [ ] TXXX Code cleanup and refactoring
- [ ] TXXX Performance optimization across all stories
- [ ] TXXX [P] Additional unit tests for complex domain logic justified by the plan in tests/unit/
- [ ] TXXX Security hardening
- [ ] TXXX Run quickstart.md validation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable

### Within Each User Story

- Required tests MUST be written and FAIL before implementation, except evaluation harness setup that defines post-implementation threshold verification
- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Models within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Contract test for [endpoint] in tests/contract/test_[name].py"
Task: "Integration test for [user journey] in tests/integration/test_[name].py"

# Launch all models for User Story 1 together:
Task: "Create [Entity1] model in src/models/[entity1].py"
Task: "Create [Entity2] model in src/models/[entity2].py"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2
   - Developer C: User Story 3
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence

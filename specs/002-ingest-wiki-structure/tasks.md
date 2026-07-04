# Tasks: Ingest Wiki Structure

**Input**: Design documents from specs/002-ingest-wiki-structure
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Format: [ID] [P?] [Story] Description

## Phase 0: Structural Boundary Enforcement (MANDATORY)

Purpose: Enforce ADR-006 and existing architecture boundaries before feature code.

- [X] T000 Write ADR-006 structural boundary test in backend/tests/Grimoire.ArchTests/AutonomousGuardrailBoundaryTests.cs
- [X] T001 Add red/green probe violating guardrail boundary in backend/tests/Grimoire.ArchTests/Probes/BadGuardrailBypassProbe.cs
- [X] T002 Execute failing/passing probe cycle and document result in backend/tests/Grimoire.ArchTests/README.md

---

## Phase 1: Setup (Shared Infrastructure)

Purpose: Prepare project structure and dependency wiring.

- [X] T003 Create guardrail policy directory and baseline policy file in wiki/policy/ingest-guardrails.yml
- [X] T004 Add policy parser package using dotnet CLI in backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj and backend/Directory.Packages.props
- [X] T005 [P] Add ingest guardrail contract types in backend/src/Grimoire.IngestAgent/Guardrails/GuardrailPolicy.cs
- [X] T006 [P] Add task artifact denied-action contract extensions in backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactDocument.cs
- [X] T007 Create ingest-agent-only instruction bundle in backend/src/Grimoire.IngestAgent/InstructionSet/CLAUDE.md and backend/src/Grimoire.IngestAgent/InstructionSet/.claude/skills/ingest-wiki-structure/SKILL.md, expose skill-name CLI option in backend/src/Grimoire.IngestAgent/AgentCliOptions.cs, and do not modify repository-root CLAUDE.md or .claude/skills/

---

## Phase 2: Foundational (Blocking Prerequisites)

Purpose: Core guardrail and instruction-loading infrastructure required by all stories.

- [X] T008 Implement guardrail policy loader in backend/src/Grimoire.IngestAgent/Guardrails/GuardrailPolicyLoader.cs
- [X] T009 [P] Implement guardrail evaluator with allow/deny semantics in backend/src/Grimoire.IngestAgent/Guardrails/GuardrailEvaluator.cs
- [X] T010 [P] Implement instruction context loader resolving backend/src/Grimoire.IngestAgent/InstructionSet/CLAUDE.md and backend/src/Grimoire.IngestAgent/InstructionSet/.claude/skills/ingest-wiki-structure/SKILL.md in backend/src/Grimoire.IngestAgent/Instructions/InstructionContextLoader.cs
- [X] T011 Implement guarded file operation wrapper in backend/src/Grimoire.IngestAgent/Guardrails/GuardedFileOperations.cs
- [X] T012 Wire guardrail policy and instruction loader into ingest startup flow in backend/src/Grimoire.IngestAgent/Program.cs
- [X] T013 [P] Add foundational integration test for policy load and deny-by-default behavior in backend/tests/Grimoire.IntegrationTests/GuardrailPolicyTests.cs
- [X] T014 [P] Add foundational integration test for instruction context loading in backend/tests/Grimoire.IntegrationTests/InstructionContextTests.cs

Checkpoint: No user story work starts before T000-T014 are complete.

---

## Phase 3: User Story 1 - Build a complete wiki update from one source (Priority: P1) 🎯 MVP

Goal: Produce source/entity/concept wiki updates in one run with required metadata and deterministic artifact output.

Independent Test: Submit one source and verify connected source, entity, and concept pages are created or updated with required frontmatter and artifact evidence.

### Tests for User Story 1

- [X] T015 [P] [US1] Add integration test for multi-page wiki structure output in backend/tests/Grimoire.IntegrationTests/WikiStructureGenerationTests.cs
- [X] T016 [P] [US1] Add integration test asserting required non-source frontmatter fields in backend/tests/Grimoire.IntegrationTests/WikiMetadataContractTests.cs
- [X] T017 [P] [US1] Add integration test asserting deterministic task artifact action lists in backend/tests/Grimoire.IntegrationTests/TaskArtifactDeterminismTests.cs

### Implementation for User Story 1

- [X] T018 [P] [US1] Extend synthesis result model for multi-page action planning in backend/src/Grimoire.IngestAgent/Synthesis/SynthesisResult.cs
- [X] T019 [P] [US1] Implement wiki action planner for source/entity/concept pages in backend/src/Grimoire.IngestAgent/WikiWrite/WikiStructurePlanner.cs
- [X] T020 [US1] Implement required frontmatter builder for non-source pages in backend/src/Grimoire.IngestAgent/WikiWrite/WikiFrontmatterBuilder.cs
- [X] T021 [US1] Apply planned wiki writes through Claude SDK tool actions with guarded wrapper in backend/src/Grimoire.IngestAgent/WikiWrite/WikiPageWriter.cs
- [X] T022 [US1] Record created and updated actions in artifact writer in backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs
- [X] T023 [US1] Return user-facing ingest summary and append run notes to wiki/tasks/<task-id>.md in backend/src/Grimoire.IngestAgent/Program.cs

Checkpoint: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Keep the wiki catalog current automatically (Priority: P2)

Goal: Automatically keep wiki catalog/index synchronized with all touched pages.

Independent Test: After successful ingest, all created/updated pages are discoverable through wiki/index.md without manual edits.

### Tests for User Story 2

- [X] T024 [P] [US2] Add integration test for index inclusion of all touched pages in backend/tests/Grimoire.IntegrationTests/WikiIndexCoverageTests.cs
- [X] T025 [P] [US2] Add integration test for idempotent index refresh behavior in backend/tests/Grimoire.IntegrationTests/WikiIndexIdempotencyTests.cs

### Implementation for User Story 2

- [X] T026 [US2] Extend index writer to process full Claude SDK touched-page set in backend/src/Grimoire.IngestAgent/WikiIndex/WikiIndexWriter.cs
- [X] T027 [US2] Add catalog classification mapping for source/entity/concept entries in backend/src/Grimoire.IngestAgent/WikiIndex/WikiCatalogClassifier.cs
- [X] T028 [US2] Integrate planned write results with index update pipeline in backend/src/Grimoire.IngestAgent/Program.cs

Checkpoint: User Stories 1 and 2 both pass independently.

---

## Phase 5: User Story 3 - Preserve coherence when source changes structure (Priority: P3)

Goal: Avoid duplicates and support explicit supersession while keeping wiki coherent.

Independent Test: Ingest overlapping source and verify update-in-place or supersession behavior with no duplicate/orphaned pages.

### Tests for User Story 3

- [X] T029 [P] [US3] Add integration test for duplicate-prevention update behavior in backend/tests/Grimoire.IntegrationTests/WikiDeduplicationTests.cs
- [X] T030 [P] [US3] Add integration test for supersession link contract in backend/tests/Grimoire.IntegrationTests/WikiSupersessionTests.cs
- [X] T031 [P] [US3] Add integration test for denial-continues-processing behavior in backend/tests/Grimoire.IntegrationTests/GuardrailContinuationTests.cs

### Implementation for User Story 3

- [X] T032 [US3] Implement duplicate-target resolution strategy in backend/src/Grimoire.Domain/Ingest/UpdateOrCreateDecisionService.cs
- [X] T033 [US3] Implement supersession metadata writer in backend/src/Grimoire.IngestAgent/WikiWrite/WikiSupersessionService.cs
- [X] T034 [US3] Persist denied actions with reason/target path and user questions in wiki/tasks/<task-id>.md via backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs
- [X] T035 [US3] Continue ingest execution after denied actions in backend/src/Grimoire.IngestAgent/Program.cs

Checkpoint: All user stories are independently functional and coherent.

---

## Phase 6: Polish & Cross-Cutting Concerns

Purpose: Observability, hardening, and full validation.

- [X] T036 [P] Implement business metrics for guardrails/instruction load/wiki actions in backend/src/Grimoire.IngestAgent/IngestAgentMetrics.cs
- [X] T037 [P] Implement structured log events for denied actions and completion summary in backend/src/Grimoire.IngestAgent/IngestLog/IngestLogAppender.cs
- [X] T038 [P] Implement trace spans for instruction load, guardrail evaluation, and wiki apply stages in backend/src/Grimoire.IngestAgent/IngestAgentTracing.cs
- [X] T039 Add observability integration assertions in backend/tests/Grimoire.IntegrationTests/ObservabilityGuardrailTests.cs
- [X] T040 [P] Add quickstart validation script for feature 002 in specs/002-ingest-wiki-structure/quickstart.md
- [X] T041 Execute full backend test suite and capture results in backend/tests/Grimoire.IntegrationTests/TestRunNotes.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 0 must complete first and blocks all implementation.
- Phase 1 depends on Phase 0 completion.
- Phase 2 depends on Phase 1 completion and blocks all user stories.
- Phase 3 (US1), Phase 4 (US2), and Phase 5 (US3) depend on Phase 2 completion.
- Phase 6 depends on completion of all selected user story phases.

### User Story Dependencies

- US1 (P1): Starts after Phase 2 and is MVP scope.
- US2 (P2): Starts after Phase 2; uses outputs from US1 planner/index integration but remains independently testable.
- US3 (P3): Starts after Phase 2; may reuse US1/US2 components but remains independently testable.

### Within Each User Story

- Story test tasks should be implemented first and fail before implementation tasks are completed.
- Planner/model tasks precede write/integration tasks.
- Artifact and runtime wiring tasks complete each story.

---

## Parallel Execution Opportunities

- Phase 1: T005 and T006 can run in parallel.
- Phase 2: T009, T010, T013, and T014 can run in parallel after T008.
- US1: T015-T017 and T018-T019 can run in parallel.
- US2: T024 and T025 can run in parallel.
- US3: T029-T031 can run in parallel.
- Polish: T036-T038 and T040 can run in parallel.

## Parallel Example: User Story 1

- Task: T015 [US1] backend/tests/Grimoire.IntegrationTests/WikiStructureGenerationTests.cs
- Task: T016 [US1] backend/tests/Grimoire.IntegrationTests/WikiMetadataContractTests.cs
- Task: T018 [US1] backend/src/Grimoire.IngestAgent/Synthesis/SynthesisResult.cs
- Task: T019 [US1] backend/src/Grimoire.IngestAgent/WikiWrite/WikiStructurePlanner.cs

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 0, Phase 1, and Phase 2.
2. Complete Phase 3 (US1).
3. Validate US1 independently before adding more scope.

### Incremental Delivery

1. Deliver US1 (complete wiki structure output).
2. Deliver US2 (automatic index/catalog synchronization).
3. Deliver US3 (supersession and deduplication coherence).
4. Finish with Phase 6 observability and cross-cutting validation.

### Parallel Team Strategy

1. One engineer completes Phase 0/1/2 boundary and foundation tasks.
2. After foundation: engineers can split US1, US2, US3 tracks.
3. Rejoin for Phase 6 instrumentation and full validation.

---

## Notes

- All tasks use required checklist format with ID and file path.
- Story labels are used only in user story phases.
- Tasks intentionally avoid live Anthropic/Claude API assertions and focus on repository-owned behavior.

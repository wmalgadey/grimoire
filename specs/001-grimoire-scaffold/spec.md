# Feature Specification: Grimoire Project Skeleton Setup

**Feature Branch**: `001-grimoire-scaffold`

**Created**: 2026-06-23

**Status**: Draft

## Overview

Establish the foundational monorepo structure for Grimoire, an AI Agent Orchestrator platform. This specification covers the **project skeleton only** — empty scaffolds that compile and pass architecture tests, with no business logic or feature implementations.

The skeleton provides the architectural foundation for future development of channels (Web UI, Telegram), agents (Ingest, Query, Lint, Batch), and the backend orchestrator.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Developer Initializes Project Environment (Priority: P1)

A developer clones the repository and needs to set up the complete project structure to begin development on any component (backend, frontend, or agents).

**Why this priority**: Project scaffolding is the critical path blocker for all downstream development. Without a working build, no feature work can proceed.

**Independent Test**: Clone repo → run build commands → all subprojects compile successfully with zero errors or warnings. Delivers: a ready-to-develop monorepo.

**Acceptance Scenarios**:

1. **Given** a cloned repository, **When** running backend build (`dotnet build src/backend`), **Then** the .NET 9 solution compiles without errors
2. **Given** a cloned repository, **When** running frontend build (`npm install && npm run build` in src/frontend), **Then** the Svelte 5 project bundles without errors
3. **Given** a cloned repository, **When** running architecture tests (`dotnet test src/backend/Grimoire.ArchTests`), **Then** all ADR-enforcement tests pass

---

### User Story 2 - Architect Validates Structural Constraints (Priority: P1)

An architect/reviewer needs to verify that the project structure enforces architectural decisions (ADRs 001-007) automatically without manual review.

**Why this priority**: Architecture tests are the primary enforcement mechanism per the constitution. Without automated tests, ADRs become suggestions, not constraints.

**Independent Test**: Run CI architecture test suite → all tests pass → directory structure, dependency rules, and interface contracts are validated. Delivers: enforced architectural governance.

**Acceptance Scenarios**:

1. **Given** the project scaffold, **When** executing architecture tests, **Then** backend dependencies on infrastructure/channel implementations are rejected (ADR-005)
2. **Given** the project scaffold, **When** executing architecture tests, **Then** IChannel and IAgentWorker interfaces are defined in core and not bypassed (ADR-004, ADR-002)
3. **Given** the project scaffold, **When** executing architecture tests, **Then** frontend imports are restricted to frontend/src/ subtree (ADR-005)
4. **Given** the project scaffold, **When** executing architecture tests, **Then** backend uses .NET 9 Minimal API (ADR-001)

---

### User Story 3 - CI Pipeline Executes Build and Tests (Priority: P1)

The CI pipeline (GitHub Actions) must execute for every commit, building all subprojects and running architecture tests independently per subproject.

**Why this priority**: CI is the enforcement gate. Without working CI, architectural violations slip through undetected.

**Independent Test**: Push commit to branch → GitHub Actions workflows trigger → all builds pass → architecture tests pass. Delivers: repeatable, automated validation.

**Acceptance Scenarios**:

1. **Given** a push to any branch, **When** GitHub Actions backend workflow runs, **Then** `dotnet build` and architecture tests execute and pass
2. **Given** a push to any branch, **When** GitHub Actions frontend workflow runs, **Then** `npm run build` and linting execute and pass
3. **Given** a change to backend code only, **When** GitHub Actions runs, **Then** only backend workflows execute (frontend skipped)

---

### User Story 4 - Developer Understands Project Layout (Priority: P2)

A developer joining the project needs clear documentation of the monorepo structure, directory purpose, and how Spec-Kit artifacts map to subprojects.

**Why this priority**: Clear structure reduces onboarding friction and prevents accidental architectural violations (file-in-wrong-directory bugs).

**Independent Test**: Developer reads README/architecture docs → understands directory purpose and constraints → successfully creates a new feature in correct location. Delivers: intuitive project layout.

**Acceptance Scenarios**:

1. **Given** the project scaffold, **When** reviewing root README, **Then** monorepo structure is documented with purpose of each major directory
2. **Given** the project scaffold, **When** reviewing constitution.md, **Then** tech stack choices are explicitly referenced to ADRs
3. **Given** the project scaffold, **When** reviewing CLAUDE.md, **Then** SDD workflow (Spec-Kit) integration is explained

---

### Edge Cases

- What happens if a developer modifies src/backend and src/frontend in the same commit? → CI should run both workflows independently
- What if architecture tests have conflicting rules (e.g., two ADRs forbidding the same pattern)? → ADRs are ordered by precedence in documentation; constitution governs disputes
- How are monorepo package references versioned (e.g., backend → shared contracts)? → Assumption: projects reference by relative path; versioning is out of scope for skeleton

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Monorepo MUST have directory structure matching ADR-005 (`src/backend`, `src/frontend`, `src/agents`, `docs/adr`, `specs/`)
- **FR-002**: Backend MUST be a .NET 9 Minimal API project scaffolded with SignalR support (empty, compiles)
- **FR-003**: Backend MUST define `IChannel` interface as an empty contract in `src/backend/Grimoire.Core`
- **FR-004**: Backend MUST define `IAgentWorker` interface as an empty contract in `src/backend/Grimoire.Core`
- **FR-005**: Backend solution MUST include xUnit test project with NetArchTest.Rules configured for architecture validation
- **FR-006**: Architecture tests MUST enforce ADR-001 (backend stack is .NET 9 Minimal API)
- **FR-007**: Architecture tests MUST enforce ADR-002 (agent workers implement IAgentWorker interface)
- **FR-008**: Architecture tests MUST enforce ADR-004 (channels implement IChannel interface)
- **FR-009**: Architecture tests MUST enforce ADR-005 (monorepo structure and project boundaries)
- **FR-010**: Architecture tests MUST enforce ADR-006 (hub-spoke orchestration pattern via interface contracts)
- **FR-011**: Architecture tests MUST enforce ADR-007 (no binary databases in core domain; storage is abstracted)
- **FR-012**: Frontend MUST be a Svelte 5 + Vite project scaffolded with TypeScript enabled (empty app, compiles)
- **FR-013**: Frontend MUST include package.json with build, dev, and lint scripts
- **FR-014**: GitHub Actions CI MUST have separate workflows for backend (build + architecture tests) and frontend (build + lint)
- **FR-015**: CI workflows MUST use path-based triggers to run only affected subprojects (backend/ or frontend/ changes)
- **FR-016**: Root README MUST document monorepo structure, build instructions, and links to ADRs

### Key Entities

No persistent entities are introduced in the skeleton. Interfaces (IChannel, IAgentWorker) are contracts, not entities.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Backend solution compiles with `dotnet build` with zero errors and zero warnings
- **SC-002**: Frontend application bundles with `npm run build` with zero errors and zero warnings
- **SC-003**: All architecture tests pass in CI (NetArchTest.Rules enforces all ADR-001 through ADR-007 constraints)
- **SC-004**: Architecture tests execute in under 30 seconds (fast feedback loop)
- **SC-005**: GitHub Actions CI completes for backend in under 3 minutes and frontend in under 2 minutes (excluding container pull/build time)
- **SC-006**: Project structure has zero files in wrong directories (verified by directory-structure tests)
- **SC-007**: IChannel and IAgentWorker are defined as lean interfaces (max 3 method signatures each in skeleton)

## Assumptions

- Developers have .NET 9 SDK and Node.js 20+ installed locally
- Backend development uses C# 13 with nullable reference types enabled
- Frontend development uses TypeScript strict mode and Svelte 5 Runes syntax
- Container-per-agent is a future extension (ADR-002 notes the escape hatch)
- Git + Markdown storage is configured outside this skeleton (ADR-007)
- Integration test infrastructure (Testcontainers) is added in subsequent features, not in skeleton
- Semantic Search / vector databases are optional future extensions (ADR-007)
- SignalR hub implementations are added in channel features, not in skeleton
- Agent implementations (Ingest, Query, Lint, Batch) are added in separate features
- Constitution.md is treated as a living governance document and reviewed by reviewers on every PR (constitutional principle IV)

## Architectural Constraints & ADRs

The skeleton is explicitly constrained by ADRs 001-007:

- **ADR-001**: Backend framework choice (locked: .NET 9 Minimal API + SignalR)
- **ADR-002**: Agent runtime strategy (locked: Worker Services with IAgentWorker interface)
- **ADR-003**: Frontend choice (locked: Svelte 5 + Vite)
- **ADR-004**: Channel abstraction (locked: IChannel interface at backend core)
- **ADR-005**: Monorepo structure (locked: flat layout with per-subproject Spec-Kit integration)
- **ADR-006**: Hub-Spoke orchestration (locked: interfaces define the contract boundary)
- **ADR-007**: Storage strategy (locked: Git + Markdown as baseline; abstractions added in future)

NetArchTest.Rules will enforce these constraints automatically.

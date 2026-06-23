# Feature Specification: Screaming Architecture - Domain-Driven Code Reorganization

**Feature Branch**: `003-domain-driven-refactor`

**Created**: 2026-06-23

**Status**: Draft

**Input**: Refactor the code to screaming architecture where classes are organized by domains instead of Clean Architecture layers

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Developer Onboarding via Intuitive Project Structure (Priority: P1)

A new developer joins the team and needs to understand how the codebase is organized. Instead of navigating through `/Api`, `/Core`, and `/Infrastructure` folders, they can immediately see `/Agents`, `/Hubs`, and `/Channels` folders, which directly correspond to the system's core business domains.

**Why this priority**: This is foundational. The entire purpose of screaming architecture is making the business domain visible at a glance. Without this, the refactor fails its core objective. New developer onboarding time directly impacts team velocity.

**Independent Test**: A developer unfamiliar with the codebase can open the project, look at the top-level folder structure, and correctly describe the system's primary business domains within 2 minutes.

**Acceptance Scenarios**:

1. **Given** a developer opens `/src/backend/Grimoire.Api`, **When** they look at the directory structure, **Then** they see domain-organized folders (e.g., `Agents/`, `Hubs/`, `Channels/`) instead of layer-based folders
2. **Given** a developer needs to find code related to agent lifecycle, **When** they navigate to `/Agents`, **Then** they find all agent-related endpoints, handlers, domain logic, and services in one cohesive location

---

### User Story 2 - Feature Implementation Follows Domain Boundaries (Priority: P1)

When implementing a new feature for a specific domain (e.g., adding a new agent command), developers can stay within a single domain folder instead of jumping between `/Endpoints`, `/Handlers`, `/Services`, and `/Domain` folders across the entire project.

**Why this priority**: This directly improves developer productivity. Fewer context switches and more co-located code reduces cognitive load and development time. This is critical for ongoing maintenance and feature velocity.

**Independent Test**: A developer can implement a complete new domain feature (endpoint → handler → domain logic → tests) by working primarily within a single domain folder, without needing to modify the API layer structure.

**Acceptance Scenarios**:

1. **Given** the task is to add a new agent capability, **When** the developer works in `/Agents`, **Then** they find all necessary files (endpoint definition, request handler, domain service, tests) in one cohesive area
2. **Given** the new feature is implemented, **When** running tests, **Then** all domain-specific tests pass without external dependencies on other domain layers

---

### User Story 3 - Code Navigation Reveals Business Intent (Priority: P1)

When a developer or architect reviews the codebase structure, they immediately understand what the system does: it manages agents, hubs, and channels. The architecture "screams" the business purpose.

**Why this priority**: This improves code maintainability and architectural clarity. As the system grows, this structure keeps the business logic front-and-center instead of buried under technical layers.

**Independent Test**: An architect can describe the system's primary responsibilities and domains by examining only the directory structure, without reading code.

**Acceptance Scenarios**:

1. **Given** the refactored codebase, **When** examining the `/src/backend/Grimoire.Api` folder structure, **Then** it clearly shows the domains: Agents, Hubs, Channels, and Shared infrastructure
2. **Given** a stakeholder reviews the folder structure, **When** they ask "what does this system do?", **Then** the folder names provide the answer before any code is read

---

### User Story 4 - Shared Infrastructure Remains Centralized (Priority: P2)

Common infrastructure like middleware, observability, persistence utilities, and exceptions are available to all domains but not duplicated. This prevents code duplication while keeping domain logic self-contained.

**Why this priority**: This is important for maintainability and avoiding code duplication, but is secondary to the primary domain organization goal. Without centralization, each domain would duplicate common concerns.

**Independent Test**: Middleware, observability, and persistence utilities are defined in a shared location and imported by all domain folders without creating circular dependencies.

**Acceptance Scenarios**:

1. **Given** multiple domains need logging, **When** they reference shared observability utilities, **Then** they import from `/Shared/Observability` instead of each domain defining its own
2. **Given** the infrastructure changes (e.g., new middleware), **When** updated in `/Shared/Middleware`, **Then** all domains automatically use the updated version

---

### Edge Cases

- What happens when a feature spans multiple domains? → Feature is decomposed into domain-specific responsibilities; cross-domain communication uses well-defined interfaces
- How does testing work for features that involve multiple domains? → Domain tests are isolated; integration tests verify cross-domain interactions
- What about existing external dependencies (NuGet packages, third-party APIs)? → Dependencies remain at the `Grimoire.Api.csproj` level; domains don't need to know about implementation details

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Code MUST be organized by business domain (Agents, Hubs, Channels, etc.) at the top level of `/src/backend/Grimoire.Api`, not by technical layers (Api, Core, Infrastructure)
- **FR-002**: Each domain folder MUST contain all files directly related to that domain: endpoints, handlers, domain models, services, and domain-specific tests
- **FR-003**: Common infrastructure (middleware, observability, persistence, exceptions) MUST be extracted to a shared `/Shared` folder that all domains reference
- **FR-004**: All SignalR hubs and hub-related handlers MUST be organized within a `/Hubs` domain folder
- **FR-005**: Agent-related endpoints, handlers, and services MUST be organized within an `/Agents` domain folder
- **FR-006**: Channel abstraction and channel-related logic MUST be organized within a `/Channels` domain folder
- **FR-007**: Test project structure MUST mirror the domain organization (e.g., `/Agents/Tests`, `/Hubs/Tests`)
- **FR-008**: Namespace structure MUST reflect the new domain organization (e.g., `Grimoire.Api.Agents.*`, `Grimoire.Api.Hubs.*`)
- **FR-009**: No code MUST have circular dependencies between domains; all inter-domain communication MUST use well-defined interfaces
- **FR-010**: The refactored code MUST pass all existing unit, integration, and architecture tests without modification to test logic

### Key Entities *(included for reference, structure refactor only)*

- **Agents Domain**: Manages agent lifecycle, capabilities, and interactions
- **Hubs Domain**: Manages SignalR connections and real-time communication
- **Channels Domain**: Manages channel abstraction and multi-channel support
- **Shared Infrastructure**: Common utilities for observability, middleware, persistence, and exception handling

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Code organization is immediately intuitive — developer can locate domain-specific code within 30 seconds of opening the project
- **SC-002**: All existing tests pass (unit, integration, architecture) with zero test logic modifications required
- **SC-003**: Namespace organization clearly reflects business domains (minimum 80% of namespaces follow `Grimoire.Api.{Domain}.*` pattern)
- **SC-004**: No code duplicates common infrastructure — all middleware, observability, and persistence utilities are centralized
- **SC-005**: New features can be implemented entirely within a single domain folder 90% of the time, without cross-cutting architectural navigation

## Assumptions

- **Assumption 1**: The existing codebase can be reorganized without losing functionality — all tests will pass after refactoring
- **Assumption 2**: The refactor will not require changes to external API contracts or endpoint definitions (breaking changes to clients are out of scope)
- **Assumption 3**: Shared infrastructure (middleware, observability, persistence) is sufficiently generic to serve all domains without modification
- **Assumption 4**: The test project structure can be reorganized to match the new domain organization
- **Assumption 5**: Namespaces will be updated to reflect domain organization as part of this refactor
- **Assumption 6**: The refactor is architectural only — no new features are added during this refactor; only reorganization occurs

# Feature Specification: Hexagonal Architecture Alignment & Task Detail Markdown View

**Feature Branch**: `006-hexagonal-arch-tasks-ui`

**Created**: 2026-07-19

**Status**: Draft

**Input**: User description: "die constitution wurde geändert und um aspekte der hexagonalen architektur erweitert. passe die codebase an, damit diese prinzipien umgesetzt werden. ergänze in diesem zusammenhang das ui mit einer darstellung der tasks. aktuell kann man in den tasks-cards auf details klicken, und sieht die json daten des jeweiligen endpunktes. idealerweise zeigt das ui bei details die entsprechende tasks.md. die darstellung soll das markdown der tasks.md rendern und automatisch aktualisieren, wenn sich der inhalt ändert."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Codebase Conforms to the Hexagonal Boundary Rules (Priority: P1)

The project constitution was amended (v1.4.0) with binding hexagonal ports-and-adapters
rules in Principle I. As a project maintainer, I need the existing codebase brought into
conformance so that every dependency on an external system that hermetic tests must be
able to replace — the LLM provider, the spawned agent process, subprocess-based
converters, and outbound network fetching — is consumed through a port with a production
adapter and a test fake, and so that infrastructure packages stay confined to their
designated adapter areas. Conformance must be proven by automated checks that fail the
build on violation, not by convention.

**Why this priority**: The constitution declares these rules normative and the
Definition of Done requires them for every future feature. Until the existing codebase
conforms, every subsequent feature builds on top of violations, and the automated checks
cannot be turned on without failing. This story unblocks all future work.

**Independent Test**: Run the automated conformance checks. They pass on the aligned
codebase, and each rule demonstrably fails when a deliberate violation is introduced
(Red/Green probe) and passes again once it is removed. The full existing test suite
still passes, proving behavior is unchanged.

**Acceptance Scenarios**:

1. **Given** the aligned codebase, **When** the automated architecture conformance checks run, **Then** every rule from Principle I (external-system ports, port ownership, adapter containment) passes with zero active violations.
2. **Given** any single conformance rule, **When** a deliberate violation is introduced as a probe, **Then** the corresponding check fails the build, and passes again after the probe is removed.
3. **Given** the hermetic harness test suite, **When** it runs, **Then** it completes without any live LLM provider call, real API key, or outbound network access, using test fakes behind the same ports the production adapters implement.
4. **Given** the complete pre-existing test suite, **When** it runs against the restructured codebase, **Then** all previously passing tests still pass (no behavioral regression).

---

### User Story 2 - Task Details Show the Rendered Task Record (Priority: P2)

As an operator watching the ingest board, when I click "Details" on a task card I want
to see the task's markdown task record — the same per-task document the system maintains
throughout the task's life — rendered as formatted, readable content (headings, lists,
status information), instead of the raw machine-readable JSON I see today.

**Why this priority**: The task record already contains the authoritative, human-written
narrative of what happened during a task (stages, agent progress, failure details). The
current raw-JSON view forces operators to read machine output. This story delivers the
core UI value of the feature and is independently shippable once story 1's boundaries
exist.

**Independent Test**: Open the board, click "Details" on any task that has a task
record, and verify the record's content appears as formatted rendered markdown with its
status metadata presented legibly.

**Acceptance Scenarios**:

1. **Given** a task with an existing task record, **When** the operator opens the task's details, **Then** the record's markdown body is displayed rendered (headings, lists, emphasis, code blocks formatted), not as raw source text or raw JSON.
2. **Given** a task record containing a metadata header (status, timestamps, references), **When** the details are displayed, **Then** the metadata is presented in a readable form rather than as a raw text block.
3. **Given** a task whose record does not exist yet or cannot be read, **When** the operator opens the task's details, **Then** a meaningful placeholder or notice is shown instead of an error dump.
4. **Given** existing machine consumers of the task data, **When** the new detail view is introduced, **Then** the existing machine-readable task data access remains available and unchanged.

---

### User Story 3 - Task Detail View Updates Automatically (Priority: P3)

As an operator with a task's detail view open, when the task's record changes — the
system advances a stage, the agent appends progress, the task completes or fails — I
want the rendered view to update automatically, without reloading or reopening it.

**Why this priority**: Valuable for live monitoring of running tasks, but the detail
view is already useful with manual refresh; this builds on story 2.

**Independent Test**: Open a task's detail view while the task is progressing and
verify the rendered content changes without any manual action shortly after the
underlying record changes.

**Acceptance Scenarios**:

1. **Given** an open detail view of a running task, **When** the task's record content changes, **Then** the rendered view reflects the new content within 5 seconds without manual refresh.
2. **Given** an open detail view whose live-update connection is interrupted, **When** the connection is restored, **Then** the view resynchronizes to the current record content and continues updating.
3. **Given** a record that is rewritten while being read, **When** the view refreshes, **Then** it never shows a torn or half-written state — it shows either the previous or the new complete content.

---

### Edge Cases

- Task card exists on the board but the corresponding task record file is missing or was deleted: detail view shows a clear "record unavailable" notice, never a crash or raw error.
- Task record is momentarily unparseable (e.g., malformed metadata header mid-rewrite): view keeps the last good rendering or shows a neutral notice, and recovers on the next change.
- Very large task record (long agent narrative): view remains responsive and scrollable.
- Live-update channel drops while the browser tab stays open: the view indicates staleness or silently reconnects, and never presents stale content as live without recovery.
- Deliberate-violation probes for conformance checks must never remain in the codebase after verification.
- A future contributor adds a direct dependency from orchestration code to a concrete external-system adapter: the build fails with a message identifying the violated rule.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every dependency on an external system that hermetic harness tests must be able to replace — LLM provider access, spawned agent processes, subprocess-based converters, and outbound network fetching — MUST be consumed through a port interface, with the production adapter and the test fake implementing the same port (Constitution Principle I).
- **FR-002**: Orchestration code MUST NOT construct or reference concrete external-system adapter implementations directly; each port interface MUST be owned by the consuming orchestration side of the boundary (Constitution Principle I, port ownership).
- **FR-003**: Infrastructure packages MUST appear only in their designated adapter areas (persistence drivers only in persistence adapters, LLM SDK only in the model-client adapter, outbound HTTP fetching only in the fetch adapter), and each containment rule MUST be enforced by an automated structural check verified with a Red/Green probe (Constitution Principles I & III).
- **FR-004**: The hermetic harness test suite MUST pass without live LLM provider calls, real credentials, or outbound network access, exercising test fakes through the same ports the production adapters implement (Constitution Principle II).
- **FR-005**: The architectural restructuring MUST NOT change observable system behavior: all previously passing tests continue to pass and all existing external interfaces remain unchanged.
- **FR-006**: The task card's "Details" action MUST present the task's markdown task record rendered as formatted content (headings, lists, emphasis, code blocks) instead of raw JSON or raw markdown source.
- **FR-007**: The detail view MUST present the task record's metadata header (status, timestamps, source references) in a readable form distinct from the narrative body.
- **FR-008**: When a task's record does not exist yet or cannot be read, the detail view MUST show a meaningful placeholder or notice instead of an error.
- **FR-009**: The rendered detail view MUST update automatically when the underlying task record's content changes, within 5 seconds, without manual refresh.
- **FR-010**: After an interruption of the live-update mechanism, the detail view MUST resynchronize to the current record content once connectivity is restored.
- **FR-011**: The detail view MUST never display a torn (half-written) record state; it shows either the complete previous or the complete new content.
- **FR-012**: The existing machine-readable task data access MUST remain available and unchanged for non-UI consumers.

### Key Entities

- **Task**: A unit of ingest work shown as a card on the board; has an identity, title, status, queue/run/failure context, and timestamps.
- **Task Record**: The per-task markdown document maintained throughout the task's life; consists of a metadata header (status, timestamps, references) and a narrative body written progressively by the system and the agent. Exactly one record corresponds to each task.
- **Port**: A boundary contract through which orchestration consumes an external system; realized by exactly one production adapter and at least one test fake.
- **Adapter**: A confined implementation of a port that touches the actual external system (LLM provider, agent process, converter, network fetch).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the hexagonal boundary rules from Constitution Principle I are covered by automated conformance checks that fail the build on violation, each proven live by a documented Red/Green probe.
- **SC-002**: 100% of hermetic harness test runs complete with zero live LLM provider calls and zero outbound network access.
- **SC-003**: 100% of previously passing tests pass after the restructuring, with no change to existing external interfaces.
- **SC-004**: 100% of tasks with an existing task record show its rendered content when details are opened; tasks without a readable record show a placeholder in 100% of cases.
- **SC-005**: Content changes to an open task record become visible in the detail view within 5 seconds in 100% of observed update events, without manual refresh.
- **SC-006**: An operator can read a task's full history (stages, progress, outcome) from the detail view without consulting raw machine-readable output.

## Assumptions

- "Die entsprechende tasks.md" refers to the per-task markdown task record the system maintains for each ingest task (one markdown document per task). The similarly named development-workflow file (`specs/<feature>/tasks.md`) is unrelated to runtime task cards and out of scope.
- The scope of the architecture alignment is exactly the external-system categories the amended constitution names: LLM provider access, spawned agent processes, subprocess-based converters, and outbound network fetching. Persistence and local filesystem access remain exempt from ports per the constitution's persistence exemption.
- The existing machine-readable task data endpoint remains in place; the new detail view is additive and becomes the operator-facing default.
- The existing live-update mechanism that already powers board updates can also signal task-record changes; no new external infrastructure is introduced.
- All wiki-content judgment remains with the agent (Constitution Principle V); this feature touches only harness structure and operator UI, not agent behavior — therefore all success criteria are deterministic harness guarantees.
- No new external system dependency is introduced by this feature, so no new port/ADR for a *new* boundary is expected; formalizing existing boundaries may still require ADR work, which planning will determine.

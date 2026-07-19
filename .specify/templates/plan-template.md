# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]

**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]

**Storage**: [if applicable, e.g., PostgreSQL, CoreData, files or N/A]

**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]

**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]

**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]

**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]

**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]

**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

[Gates determined based on constitution file]

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

<!--
  ACTION REQUIRED: List every ADR that constrains this feature's implementation.
  If this feature introduces a new structural boundary, integration pattern, or
  cross-cutting concern not covered by existing ADRs, draft a new MADR in docs/adr/
  BEFORE finalizing this plan.

  HEXAGONAL GATE (Constitution Principle I): If this feature adds a dependency on a
  new external system (LLM API, spawned process, subprocess converter, network
  service), the plan and its ADR MUST name: the port interface, the adapter namespace
  that contains the infrastructure package, and the structural containment rule that
  enforces it. Persistence/local-filesystem adapters are exempt from ports but not
  from adapter containment.

  Format:
  - ADR-NNN: [Title] — [How it constrains this feature]
  - ADR-NNN: [Title] — [How it constrains this feature]

  If no existing ADRs apply and no new boundary is introduced, write:
  "No new or existing ADRs constrain this feature."
-->

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| [ADR-NNN] | [Title] | [How it constrains implementation] |

**New ADR required?**: [Yes — draft docs/adr/ADR-NNN-[name].md before proceeding / No]

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

<!--
  ACTION REQUIRED: For every capability this feature introduces or changes, assign it
  to exactly one side of the boundary. Wiki-content judgment (what pages exist, what
  they say, update-vs-create, supersession, categorization, confidence, tagging) MUST
  land in instruction files executed by an agent; backend code may only gain harness
  capabilities (dispatch, guardrails, tools, task lifecycle, observability).
  If the feature has no agentic surface, write: "No agentic surface — harness-only feature."
-->

| Capability                         | Side         | Where it lives                               |
|------------------------------------|--------------|----------------------------------------------|
| [e.g., update-vs-create decision]  | Agentic core | [e.g., agents/ingest/SKILL.md]               |
| [e.g., write-path guardrail]       | Harness      | [e.g., Guardrails/GuardedFileOperations.cs]  |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

<!--
  ACTION REQUIRED: Map each success criterion from spec.md to the test approach that
  proves it. Deterministic harness guarantees MUST map to hermetic contract,
  integration, or architecture tests. Agent-judgment outcomes MUST map to evaluation
  runs with explicit thresholds.

  Include the concrete doubles, fixtures, and recorded samples required to keep the
  test suite reproducible. Hermetic tests MUST NOT require live LLM provider calls or
  production credentials.
-->

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| `[e.g., Submission is persisted atomically]` | Deterministic guarantee | Hermetic integration test | `[e.g., Testcontainers PostgreSQL, fake clock]` | `[e.g., valid submission payload, rollback scenario fixture]` | `[Why this test proves the criterion]` |
| `[e.g., Agent chooses update over duplicate creation in >= 90% of sampled runs]` | Agent-judgment threshold | Evaluation with threshold | `[e.g., recorded LLM responses or approved live-eval gate]` | `[e.g., sampled wiki/source pairs, golden adjudication set]` | `[Threshold, scorer, retry policy]` |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

<!--
  ACTION REQUIRED: Enumerate every observable signal this feature MUST emit.
  Be specific — use the domain's Ubiquitous Language for metric/span names.
-->

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `[domain.component.event]` | Counter/Gauge | [What it measures] | `[key=value]` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `[event_name]` | INFO/WARN/ERROR | [When emitted] | `[field1, field2]` |

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory fields.
3. CI task(s) ensuring those logging tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `[service.operation]` | `[parent or root]` | `[key=value]` |

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) that create the span with declared parent/child linkage and required attributes.
2. Deterministic integration test task(s) validating span name, parent/child relationship, and correlation attributes.
3. CI task(s) ensuring those trace tests run in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |

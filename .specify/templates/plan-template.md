# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

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

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `[service.operation]` | `[parent or root]` | `[key=value]` |

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

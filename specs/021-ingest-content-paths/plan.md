# Implementation Plan: Rename ContentRootPaths to an Ingest-Specific Type

**Branch**: `claude/grimoire-issue-56-h4s1se` | **Date**: 2026-08-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-ingest-content-paths/spec.md`

## Summary

Rename the `ContentRootPaths` record (`backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs`)
to `IngestContentPaths`, carrying the "Ingest" token per rule N1 of
`docs/conventions/agent-artifact-naming.md`, and remove its three fields
(`SystemPromptPath`, `DefaultUserPromptPath`, `PolicyPath`) that duplicate values already
available, unduplicated, on `ResolvedGrimoirePaths.Ingest` (an `AgentRuntimePaths`,
ADR-022). Every production and test reference is updated to the new type name and, where
a removed field was read, to `ResolvedGrimoirePaths.Ingest` directly. This is a
behavior-preserving, purely mechanical refactor — no CLI, HTTP, log, or trace surface
changes; no new external dependency, port, or namespace is introduced.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (`backend/Directory.Build.props`)

**Primary Dependencies**: None new. Touches only `Grimoire.Hub` (production) and
`Grimoire.IntegrationTests` (tests) — both existing projects in the backend solution.

**Storage**: N/A — no persistence surface touched.

**Testing**: xUnit (`Microsoft.NET.Test.Sdk`, `xunit`), per `backend/Directory.Build.props`.
Existing integration tests under `backend/tests/Grimoire.IntegrationTests/` and the N1
architecture test under `backend/tests/Grimoire.ArchTests/` are the verification surface;
no new test project or framework is introduced.

**Target Platform**: Linux server (Grimoire.Hub backend process), unchanged.

**Project Type**: Single backend .NET solution (existing `Grimoire.Hub` project) — internal
type rename, not a new project or service boundary.

**Performance Goals**: N/A — spec FR-009 requires zero observable behavior change; no
performance characteristic is targeted or expected to shift.

**Constraints**: Zero behavioral change (spec FR-009); solution must build and the full
existing backend test suite must pass with no test-outcome changes beyond mechanical
reference updates (spec FR-008, SC-004).

**Scale/Scope**: 11 production files, 5 test files (spec FR-005/FR-006/FR-007; enumerated
in Project Structure below), all already identified by direct codebase inspection — no
open-ended discovery required.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Domain Architecture, Hexagonal Boundaries)**: No external system is
  touched — `IngestContentPaths` remains a plain projection record over
  `ResolvedGrimoirePaths` (an in-process, already-resolved value), not a persistence or
  external-system adapter. No new port, adapter, or namespace containment rule is needed.
  PASS.
- **Principle II (Pragmatic Testing, Classicist TDD)**: No new test doubles are
  introduced; existing hermetic integration tests and the N1 architecture test are
  updated in place to reference the renamed type. No mocking framework, no interaction
  verification. PASS.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: No new structural
  boundary, integration pattern, or cross-cutting concern is introduced (see
  Architectural Constraints below) — no new ADR required. The existing N1 structural
  test (`AgentArtifactNamingRuleTests`) already enforces the naming rule this change
  satisfies; no new Phase 0 structural test is needed because no new rule is created,
  only an existing rule's target is renamed to conform. PASS.
- **Principle IV (Behavioral & Observable Engineering)**: No new metric, log event, or
  trace span is introduced or removed (spec Assumptions) — resolved path *values* are
  unchanged, only the type/field names that carry them. No Observability section rows
  apply. PASS.
- **Principle V (Agentic Core & Deterministic Harness)**: No wiki-content judgment is
  touched. `IngestContentPaths` carries file-system locations only; no instruction file,
  agent prompt, or guarded-tool policy changes. PASS.

No violations. Nothing to record in Complexity Tracking.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-013 | Unified Agent Platform Packaging and Agent-Artifact Naming Convention | Establishes rule N1 (every agent-specific artifact carries its owning agent's name token) and the `Grimoire.Hub` namespace-ownership map, enforced by `AgentArtifactNamingRuleTests`. This feature *satisfies* N1 for `ContentRootPaths`/`IngestContentPaths` — an existing gap the ADR's own rule already covers. The ADR's ownership map lists `Grimoire.Hub.ContentRoot` as a cross-agent namespace that may host per-agent-named types of shared infrastructure (mirroring `Realtime`'s `IngestLifecycleHub`), so the rename does not require a namespace relocation or an exemption-list addition. |
| ADR-022 | Minimal Directory Configuration Surface | Establishes `ResolvedGrimoirePaths` as the single composition point for all runtime locations, with `AgentRuntimePaths` (`ResolvedGrimoirePaths.Ingest`/`.Query`/`.Lint`) as the authoritative per-agent instruction-path source. This feature removes `ContentRootPaths`'s duplicate copies of `ResolvedGrimoirePaths.Ingest`'s three instruction-path fields, completing the "one composition point" intent ADR-022 already establishes — it does not modify `ResolvedGrimoirePaths`, `AgentRuntimePaths`, or the resolution/validation pipeline itself. |
| ADR-010 | Hexagonal Ports and Adapter Namespaces for External Systems | Confirms `IngestContentPaths` needs no port: it is an in-process projection over an already-resolved value object, not an external-system adapter, so ADR-010's port/containment rules are inapplicable here (consistent with Constitution Principle I's persistence/no-external-system exemption). |

**New ADR required?**: No. This feature introduces no new structural boundary,
integration pattern, or cross-cutting concern — it conforms an existing type to a naming
rule ADR-013 already established, and removes duplication ADR-022 already implied but did
not itself require by name (research.md R9 explicitly deferred it as future mechanical
cleanup).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. `IngestContentPaths` and
`ResolvedGrimoirePaths.Ingest` carry file-system path values consumed by harness
orchestration code (`SubmissionService`, `IngestSubmissionPipeline`,
`IngestRunCoordinator`, ingest CLI commands); none of this is wiki-content judgment, and
no instruction file is read, written, or reinterpreted by this change.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001: zero `ContentRootPaths` occurrences in C# source | Deterministic guarantee | Repository-wide text search, executed as a CI-runnable check (`grep`/`dotnet build` failure surface) | None | Full `backend/**/*.cs` tree | A compile error on any missed reference is an equally valid proof; the search is a fast pre-check, not a substitute for the build. |
| SC-002: removed fields read only via `ResolvedGrimoirePaths.Ingest` | Deterministic guarantee | Existing hermetic integration tests (`IngestDispatchPathArgumentsTests`, `IngestSubmissionPromptApiTests`, `PathConfiguration/*`) updated to assert against the new type/field shape | None — these tests already use fakes/fixtures implementing existing ports (`IngestSubmissionPipelineFixture`), no mocking framework | Existing fixture-seeded instruction files (`IngestSubmissionPipelineFixture`) | State-based assertions on resolved path values, unchanged from today's assertions, just re-pointed at the renamed source. |
| SC-003: renamed type declares exactly 5 fields | Deterministic guarantee | Compile-time check (record declaration) + a direct unit-style assertion is unnecessary since C# record shape is enforced by the compiler at every call site; covered implicitly by SC-001/SC-002's build-and-test pass | None | N/A | No dedicated test needed beyond successful compilation of all call sites, which fails immediately if a removed field is referenced. |
| SC-004: 100% of existing backend suite passes | Deterministic guarantee | Full existing suite run (architecture, unit, integration) via the standard CI pipeline command | Existing fakes/fixtures only, unchanged | Existing fixtures | No new tests are added for this refactor; the bar is that nothing already-passing regresses. |
| SC-005: N1 architecture test passes, 0 exemption-list additions | Deterministic guarantee | Existing `AgentArtifactNamingRuleTests.HubNamespaces_MustFollowTheOwnershipMap` and `ExemptionFixture_MustMirror_TheConventionDocument`, run unmodified | None | None | Confirms the rename lands inside the already-covered `Grimoire.Hub.ContentRoot` cross-agent namespace without needing rule changes. |
| SC-006: resolved path values byte-identical before/after | Deterministic guarantee | Existing integration tests asserting exact resolved path strings (`IngestDispatchPathArgumentsTests`, `CustomAgentDirEndToEndTests`) continue to pass unmodified in their assertions, only in their source-of-truth reference | None | Existing fixtures | These tests already assert exact path equality; they act as the regression guard for "same values, new source". |

No agent-judgment success criteria exist in spec.md — all six are deterministic harness
guarantees per Constitution Principle II, consistent with this being a pure internal
rename/de-duplication with no agentic surface.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

No new or changed observability signals. Spec Assumptions state explicitly: "No new
observability signals (metrics, log events, trace spans) are introduced or removed by
this change; existing ones are unaffected because resolved path values do not change."
No rows apply; the Definition of Done's logging/trace-contract gates are vacuously
satisfied (nothing new to cover).

### Business Metrics (OpenTelemetry Counters / Gauges)

None.

### Structured Log Events

None.

### Distributed Trace Spans (OpenTelemetry)

None.

## Project Structure

### Documentation (this feature)

```text
specs/021-ingest-content-paths/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

No `contracts/` directory: this feature exposes no external interface (no HTTP endpoint,
CLI switch, or cross-process contract changes) — it renames an internal type consumed
entirely within the `Grimoire.Hub` process.

### Source Code (repository root)

```text
backend/
├── src/Grimoire.Hub/
│   ├── ContentRoot/
│   │   └── ContentRootPaths.cs                          # renamed → IngestContentPaths.cs
│   ├── HubHostComposition.cs                             # DI registration + call sites
│   ├── Runtime/Paths/ResolvedGrimoirePaths.cs            # unchanged (comment only, out of scope)
│   ├── IngestSubmission/
│   │   ├── SubmissionService.cs
│   │   ├── IngestSubmissionPipeline.cs
│   │   ├── IngestSubmissionEndpoints.cs
│   │   └── BoardEndpoints.cs
│   ├── IngestDispatch/
│   │   └── IngestRunCoordinator.cs
│   └── Cli/
│       ├── IngestResumeCommand.cs
│       ├── IngestRetriggerCommand.cs
│       └── SubmitSourceCommand.cs
└── tests/
    ├── Grimoire.IntegrationTests/
    │   ├── Fakes/IngestSubmissionPipelineFixture.cs
    │   ├── HubCliCommandTests.cs
    │   ├── BoardCompositeResponseTests.cs
    │   ├── IngestSubmissionPromptApiTests.cs
    │   └── PathConfiguration/
    │       ├── IngestDispatchPathArgumentsTests.cs
    │       ├── IngestRepoLessStartupTests.cs
    │       └── StartupValidationTests.cs
    └── Grimoire.ArchTests/
        └── AgentArtifactNamingRuleTests.cs               # verification only, not modified
```

**Structure Decision**: No new directories, projects, or namespaces. All changes land
inside the existing `Grimoire.Hub` (production) and `Grimoire.IntegrationTests` /
`Grimoire.ArchTests` (verification) projects, matching the file list the source issue
(#56) and direct codebase grep both identified — 11 production files, 7 test files.

## Complexity Tracking

*No Constitution Check violations — this section is not applicable.*

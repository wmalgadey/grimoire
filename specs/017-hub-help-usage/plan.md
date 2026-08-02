# Implementation Plan: Hub --help Usage Output

**Branch**: `017-hub-help-usage` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-hub-help-usage/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Recognize `--help`/`-h` anywhere in the Grimoire Hub's process arguments and, when
present, print a plain-text usage message listing the `submit-source` command and every
ADR-009 path switch, then exit with code 0 — before the web host is built, before any
path resolution or state-database initialization runs. The change is confined to
`backend/src/Grimoire.Hub/Program.cs`'s top-level statements: a help check gates the
existing argument-parsing flow at the earliest possible point, and the usage text is
generated from the same `PathConfigurationSwitchMappingsFactory()` map already used to
wire `--base-dir` etc., so the two can never drift apart.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), top-level statements in
`Program.cs` (ASP.NET Core minimal hosting model).

**Primary Dependencies**: None new — uses only `System.Console` and the existing
`PathConfigurationSwitchMappingsFactory()` dictionary already defined in `Program.cs`.

**Storage**: N/A — no data touched; the help path must run before
`OperationalStateRepository`/SQLite initialization.

**Testing**: xUnit, in `backend/tests/Grimoire.IntegrationTests` — real out-of-process
`dotnet run`/built-executable invocation (existing pattern: `ProcessStartInfo` +
`Process.Start`, see `ReplayAdapterTests.cs`, `CrossProcessFileLockTests.cs`), asserting
stdout content, exit code, and prompt exit (no hang waiting for `app.Run()`).

**Target Platform**: Same as the Hub itself — server/dev-machine .NET runtime
(Linux/macOS/Windows), invoked as a CLI process.

**Project Type**: Web service with a CLI-parsing surface at startup (existing
`Grimoire.Hub` composition root) — not a new project.

**Performance Goals**: N/A — a `--help` invocation is a one-shot console interaction;
no throughput/latency target applies.

**Constraints**: MUST exit before `builder.Build()` / `app.Run()` so no port is bound
and no host lifetime starts (FR-003). MUST NOT require any of the ADR-009 path
resolution, secrets loading, or SQLite initialization to succeed first — `--help` must
work even with no `data/` directory present.

**Scale/Scope**: Single file change (`Program.cs`) plus one new integration test file.
No new project, no new package reference.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal/DDD)**: N/A — no new external system dependency, no new
  namespace, no Domain Core code touched. `Program.cs`'s existing top-level composition
  root already owns CLI-argument parsing; adding one more early-exit branch to it
  introduces no new boundary. **Pass.**
- **Principle II (Pragmatic Testing)**: This is a harness contract (argument dispatch),
  tested deterministically via a real out-of-process invocation of the Hub executable —
  no live LLM calls, no mocked doubles for the thing under test. No agent judgment is
  involved. **Pass**, verified via the Test Strategy table below.
- **Principle III (ADR-Driven & Test-Enforced)**: All 18 existing ADRs read (Phase 0,
  below). ADR-009 applies (see Architectural Constraints). No new structural boundary,
  integration pattern, or cross-cutting concern is introduced, so no new ADR is drafted.
  The Phase 0 structural gate is satisfied by a parity test (see Test Strategy /
  tasks.md Phase 0) asserting the usage text and `PathConfigurationSwitchMappingsFactory()`
  can never drift apart — written first (RED, since `--help` doesn't exist yet), then
  made to pass by the implementation (GREEN). **Pass.**
- **Principle IV (Behavioral & Observable Engineering)**: See Observability section —
  N/A with justification: `--help` is a one-shot console interaction that exits before
  telemetry/logging is bootstrapped (`TelemetryExtensions.CreateBootstrapLoggerFactory()`
  runs later in `Program.cs`, after the point where this feature must already have
  exited per FR-003); there is no request, span, or business event to describe. **Pass.**
- **Principle V (Agentic Core & Deterministic Harness)**: No agentic surface — see
  Agentic Boundary section below. **Pass.**

No violations requiring Complexity Tracking justification.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-009 | Runtime Path Configuration | Defines the exact set of `--*-dir`/`--*-file`/`--*-worker` command-line switches (`PathConfigurationSwitchMappingsFactory()`) that this feature must list in its usage output. The usage text MUST be derived from (or tested for parity against) that same factory method rather than hand-duplicated, so a future switch added under ADR-009 cannot silently go undocumented. |

All other ADRs (001–008, 010–018) read and confirmed not to apply: none govern CLI
argument parsing or console output, and this feature adds no external-system
dependency, no persistence, no agent-facing capability.

**New ADR required?**: No — ADR-009 already covers the only cross-cutting concern this
feature touches (the path-switch vocabulary), and this feature introduces no new
structural boundary.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. `--help` only affects process-startup
argument handling and console output; it touches no wiki content, no agent instruction
file, and no guarded-tool boundary.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001: 100% of `--help`/`-h` invocations print usage, exit 0, never start the server | Deterministic guarantee | Hermetic integration test — spawn the real built Hub executable via `ProcessStartInfo`/`Process.Start` (existing pattern, `ReplayAdapterTests.cs`), capture stdout + exit code, assert the process exits within a short timeout instead of blocking on `app.Run()` | Real process, no mocks; runs with no `data/` directory present (an empty temp `--base-dir`) to prove no path/secrets/DB dependency | Arg combinations: `--help` alone, `-h` alone, `--help --base-dir <tmp>`, `submit-source --help` | Proves FR-001/FR-003/FR-004 together — the process actually exiting fast is the only reliable signal the server never started (no port-probe needed) |
| SC-002: 100% of documented commands/options appear in usage output | Deterministic guarantee | Hermetic parity test asserting the printed usage text contains every key from `PathConfigurationSwitchMappingsFactory()` plus `submit-source`, `--path`, `--source-kind` | None — pure string assertion against the same in-process factory method | The factory's own key set (no separate fixture — this is the point: single source of truth) | Prevents future ADR-009 switches from being added without the usage text following; this is the Phase 0 structural/parity test (Principle III) |
| SC-003: a developer can find the right switch within 30s of reading `--help` | Qualitative/UX | Quickstart validation (manual read-through, see `quickstart.md`) | N/A | N/A | Not automatable as a deterministic gate — same treatment as timed UX criteria elsewhere in this repo (cf. spec 016 SC-001); the parity test (SC-002) guarantees completeness, this is verified by human read-through |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

N/A, with justification: `--help` exits before `TelemetryExtensions.CreateBootstrapLoggerFactory()`
runs (FR-003 requires exit before any startup side effect, and the bootstrap logger is
created later in `Program.cs`'s existing flow). There is no HTTP request, no span
context, and no business event for this one-shot console interaction to describe. This
is verified deterministically by the Test Strategy table above (SC-001's process-level
integration test), itself a CI/CD gate per Principle IV's general mandate.

### Business Metrics (OpenTelemetry Counters / Gauges)

None — N/A per justification above.

### Structured Log Events

None — N/A per justification above.

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory fields.
3. CI task(s) ensuring those logging tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

None — N/A per justification above.

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) that create the span with declared parent/child linkage and required attributes.
2. Deterministic integration test task(s) validating span name, parent/child relationship, and correlation attributes.
3. CI task(s) ensuring those trace tests run in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/017-hub-help-usage/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── checklists/
│   └── requirements.md  # /speckit-specify output
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

`data-model.md` and `contracts/` are intentionally omitted: this feature has no domain
entities (spec.md has no Key Entities section) and exposes no API/interface contract to
other systems — it is a console usage message, which the plan template explicitly
allows skipping contracts for.

### Source Code (repository root)

**Structure Decision**: No new project or directory. The entire change lives in the
existing `Grimoire.Hub` composition root, with one new integration test file alongside
the existing process-spawning tests.

```text
backend/src/Grimoire.Hub/
└── Program.cs                          # Add help-check + usage text, gated before
                                          # builder.Build()/app.Run() (existing file)

backend/tests/Grimoire.IntegrationTests/
└── HubHelpUsageTests.cs                 # New: process-spawn tests (SC-001) + usage/
                                          # switch-map parity test (SC-002)
```

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table not applicable.

# Implementation Plan: Development Container (devcontainer) Setup

**Branch**: `016-devcontainer-setup` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-devcontainer-setup/spec.md`

## Summary

Add a `containers.dev`-conformant devcontainer (`.devcontainer/devcontainer.json` +
`.devcontainer/Dockerfile`) so contributors get a ready-to-use shell with the .NET 10
SDK, Node 22, and Bun 1.3.14 pre-installed — the exact versions already pinned in
`backend/Directory.Build.props`, `frontend/.nvmrc`/`package.json`, and CI — without
installing any of them on the host. The devcontainer reaches the host's container
runtime (Podman, the project's current primary runtime per `.vscode/tasks.json`, or
Docker Desktop) via the `docker-outside-of-docker` feature so the backend integration
test suite and the local Aspire Dashboard container keep working unchanged, and it
reuses the existing `<base>/data/.env` secrets file (ADR-009) for local credentials
rather than inventing a new mechanism. `CONTRIBUTING.md` gains the devcontainer as an
additional onboarding path; the existing native setup stays documented as a fallback.
A new ADR (ADR-019) fixes the host-runtime and credential-delivery decisions, and a
`devcontainers/ci`-based CI job verifies the devcontainer itself stays in sync with
the toolchain versions the native setup already pins.

## Technical Context

**Language/Version**: Devcontainer configuration itself is JSON (`devcontainer.json`)
+ Dockerfile + shell provisioning — no application language. It provisions: .NET SDK
`10.0.x` (backend, `net10.0`), Node `22` + Bun `1.3.14` (frontend).

**Primary Dependencies**: `mcr.microsoft.com/devcontainers/dotnet:1-10.0` base image;
`ghcr.io/devcontainers/features/node` (pinned `22`); `ghcr.io/devcontainers/features/docker-outside-of-docker`
for host container-runtime access; `devcontainers/ci` GitHub Action for CI
verification.

**Storage**: N/A — no new data store. Existing SQLite operational state (ADR-003) and
git-tracked wiki content are unaffected; the devcontainer only mounts the existing
repo checkout.

**Testing**: No new test framework. The devcontainer must let the existing hermetic
suites run unchanged from inside it: `dotnet build`/`dotnet test` (ArchTests,
Domain.UnitTests, IntegrationTests) and `bun run check|lint|test|build`. A new CI job
(`devcontainers/ci`) builds the devcontainer and runs those same commands inside it as
the deterministic verification mechanism for this feature itself (see Test Strategy).

**Target Platform**: Linux container, runnable from macOS/Windows/Linux hosts via a
Docker-API-compatible runtime (Podman primary/tested per clarification 2026-08-02;
Docker Desktop supported via the same feature's default fallback).

**Project Type**: Developer tooling / repo-root configuration for the existing web
application (`.NET` backend + SvelteKit frontend) — not a new service or user-facing
feature.

**Performance Goals**: SC-001 — first successful build inside the devcontainer within
15 minutes of opening the repository in a devcontainer-capable tool (network-dependent
image pull/build time; no runtime latency targets apply, this is dev tooling).

**Constraints**: Must not hardcode Docker Desktop as the only supported host runtime
(clarification 2026-08-02); must not embed credential values in
`devcontainer.json`/`Dockerfile` (FR-005/FR-006); must not introduce new persistent
cloud infrastructure (FR-008); native host-installed-toolchain setup in
`CONTRIBUTING.md` must remain valid (FR-007).

**Known out-of-scope limitation**: `.vscode/launch.json`'s `prod` configuration
hardcodes a personal, host-absolute `--content-root` path outside the repo checkout;
it cannot be made to work inside the devcontainer without either baking one
contributor's machine layout into shared config (rejected) or leaving `prod`
scoped as host-only (research.md R7). This plan does not change `prod`'s behavior.

**Scale/Scope**: A single devcontainer definition for the whole repository (backend +
frontend in one container), targeting individual contributor machines. CI reuse of the
same image is explicitly out of scope (see spec Assumptions).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal/DDD)**: N/A — this feature adds no C# production code and
  no dependency consumed by a backend/frontend port. ADR-010 was read and confirmed
  not to apply (verified in Phase 0 ADR survey): Testcontainers/Aspire-Dashboard
  reachability is existing test/observability infrastructure, not a new
  port-requiring external system. **Pass.**
- **Principle II (Pragmatic Testing)**: The devcontainer must not change how hermetic
  vs. agent-behavior tests are categorized — it only changes where they run. No agent
  judgment is involved. **Pass**, verified via the Test Strategy table below.
- **Principle III (ADR-Driven & Test-Enforced)**: All 18 existing ADRs read (Phase 0).
  Four apply (ADR-001, ADR-004, ADR-005, ADR-009); ADR-010 explicitly considered and
  found not to apply. This feature introduces a new cross-cutting integration pattern
  (host-runtime + credential reachability for a devcontainer) not covered by any
  existing ADR, so **ADR-019 was drafted and accepted** (see below) before this plan
  was finalized. Because no C# domain/infrastructure boundary is touched, the
  Phase-0 structural-test gate is satisfied by a CI-based Red/Green-probe equivalent
  (ADR-019 "Structural Enforcement") rather than a NetArchTest rule — documented
  explicitly rather than silently skipped. **Pass.**
- **Principle IV (Behavioral & Observable Engineering)**: See Observability section —
  this feature emits no runtime business metrics, log events, or trace spans (it adds
  no runtime code path), so that section is explicitly N/A with justification, not
  omitted. The CI enforcement this feature does add (devcontainer smoke-test job) is
  itself a CI/CD gate per Principle IV's general mandate. **Pass.**
- **Principle V (Agentic Core & Deterministic Harness)**: No agentic surface — see
  Agentic Boundary section below. **Pass.**

No violations requiring Complexity Tracking justification.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/Frontend Tech Stack | Fixes the exact versions the devcontainer image must provide: .NET SDK targeting `net10.0`, Bun `1.3.14` (pinned via `package.json`'s `packageManager`), Node `>=20.12` (`.nvmrc`=`22`). The devcontainer must track these, not float. |
| ADR-004 | Credential Scoping for LLM API Key | The devcontainer's credential story must follow this ADR's pattern (local, git-ignored secrets file; never embedded in build/image layers; scoped to the process that needs it) rather than inventing a new one. Realized in ADR-019. |
| ADR-005 | Observability Backend (Local/CI) | The local Aspire Dashboard container (used for OTel verification) is a second consumer — beyond Testcontainers — of host container-runtime reachability from inside the devcontainer; ADR-019's host-runtime decision must cover both. |
| ADR-009 | Runtime Path Configuration | Fixes `<base>/data/.env` as the one place local secrets live and forbids ambient path discovery. The devcontainer's workspace mount/working-directory setup must not reintroduce ambient discovery and must leave `data/.env` resolvable exactly as this ADR defines. |
| ADR-010 | Hexagonal Ports/Adapter Namespaces | **Considered and found not to apply.** Its port requirement triggers only for dependencies "hermetic harness tests must be able to replace" (LLM APIs, spawned agent processes, subprocess converters, outbound fetch). The devcontainer is dev-environment tooling around the process, not a dependency consumed by production code paths harness tests fake. |

**New ADR required?**: Yes — [docs/adr/ADR-019-devcontainer-host-runtime-and-credential-access.md](../../docs/adr/ADR-019-devcontainer-host-runtime-and-credential-access.md), drafted during this planning phase and **accepted** (fixes host container-runtime reachability via `docker-outside-of-docker` + inherited `DOCKER_HOST`, and confirms credential delivery reuses the existing `data/.env` path with no new mechanism).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. This is local development-environment
tooling (devcontainer config, Dockerfile, CI job, documentation). It does not touch
agent instruction files, wiki-content judgment, or the guarded-tool boundary, and it
introduces no new capability an agent could exercise at runtime.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001: first successful build within 15 min of opening the devcontainer | Deterministic guarantee | Quickstart validation (manual timed run, see `quickstart.md`) + CI job wall-clock as a proxy upper bound | Real devcontainer build (no doubles — build time is the thing under test) | None | CI's build time is a conservative proxy (CI runners are typically slower/faster than a contributor's cached-layer local build; quickstart.md documents the manual walkthrough as the authoritative check). |
| SC-002: 100% of hermetic build/test tooling available inside the devcontainer without host install | Deterministic guarantee | CI job (`devcontainers/ci` Action) building `.devcontainer/` and running `dotnet build`, `dotnet test` (x3 projects), `bun install`, `bun run check\|lint\|test\|build` inside the built container | Real devcontainer image; no external service doubles | None | This is ADR-019's Red/Green-probe CI job — first written against a deliberately incomplete config to prove it can fail, then fixed. |
| SC-003: hermetic test suite produces the same pass/fail outcome inside the devcontainer as via native setup | Deterministic guarantee | Same CI job as SC-002, compared against the existing native `.github/workflows/ci.yml` job (both green under normal conditions) | Real devcontainer image; existing native CI job as the comparison baseline | None | Parity is established by both jobs running the identical command set against the identical commit. |
| SC-004: zero credential values present in devcontainer config files | Deterministic guarantee | Static check task scanning `.devcontainer/devcontainer.json` and `.devcontainer/Dockerfile` for `containerEnv`/`ENV` entries carrying literal secret-shaped values (only tool-version pins permitted), run in CI | None | Known `.env-example` variable names (`ANTHROPIC_AUTH_TOKEN`, `GRIMOIRE_INGEST_MODEL`, etc.) used as the denylist the check scans for | Complements SC-002/SC-003's job; can be a separate lightweight CI step rather than a full container build. |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

N/A, with justification: this feature adds only local development-environment
configuration (devcontainer files, a CI job, documentation). It introduces no runtime
code path, no business logic, and no production-observable behavior — there is nothing
for OpenTelemetry counters, structured log events, or trace spans to describe. The
feature's own correctness is instead verified deterministically via the Test Strategy
table above (CI job pass/fail), which is itself a CI/CD gate satisfying Principle IV's
general "conventions not enforced by CI/CD do not exist" mandate.

### Business Metrics (OpenTelemetry Counters / Gauges)

None — N/A per justification above.

### Structured Log Events

None — N/A per justification above.

### Distributed Trace Spans (OpenTelemetry)

None — N/A per justification above.

## Project Structure

### Documentation (this feature)

```text
specs/016-devcontainer-setup/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── checklists/
│   └── requirements.md  # /speckit-specify output
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

`data-model.md` and `contracts/` are intentionally omitted: this feature has no
domain entities (spec.md has no Key Entities section — confirmed at `/speckit-specify`
time) and exposes no API/interface contract to other systems or users; it is purely
internal repo tooling, which the plan template explicitly allows skipping contracts
for.

### Source Code (repository root)

**Structure Decision**: New files live under a single new `.devcontainer/` directory
at the repo root, alongside a documentation update, a new CI workflow file, and one
targeted fix to an existing VS Code task that cannot run correctly from inside the
devcontainer (research.md R7) — no changes to existing `backend/` or `frontend/`
source layout.

```text
.devcontainer/
├── devcontainer.json       # containers.dev config: features, remoteEnv (DOCKER_HOST),
│                           # secrets (declarative, metadata-only), workspace mount,
│                           # forwarded ports, postCreateCommand
└── Dockerfile              # FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0
                             # + Node 22 feature + pinned Bun 1.3.14 install

.github/workflows/
└── devcontainer-ci.yml     # New: devcontainers/ci Action — builds .devcontainer/,
                             # runs backend build/test + frontend check/lint/test/build
                             # inside it, and the SC-004 credential-value scan

CONTRIBUTING.md             # Updated: devcontainer path documented alongside the
                             # existing native setup (which remains, per FR-007)

docs/adr/
└── ADR-019-devcontainer-host-runtime-and-credential-access.md   # New, accepted

.vscode/tasks.json          # Updated: guard `start: podman machine` to no-op when
                             # $REMOTE_CONTAINERS/$CODESPACES is set (research.md R7) —
                             # `.vscode/launch.json` needs no change (coreclr debug
                             # works unmodified inside a devcontainer)
```

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table not applicable.

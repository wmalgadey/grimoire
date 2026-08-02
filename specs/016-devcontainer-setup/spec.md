# Feature Specification: Development Container (devcontainer) Setup

**Feature Branch**: `016-devcontainer-setup`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "erstelle einen devcontainer (https://containers.dev/) für das aktuelle projekt um die entwicklung zu beschleunigen und die abhängigkeiten im system zu reduzieren"

## Clarifications

### Session 2026-08-02

- Q: The spec's Assumptions section says the devcontainer needs "a container runtime (e.g. Docker)," but `.vscode/tasks.json` shows this repo already runs container workloads via Podman (`podman machine start` before any `docker run` task). Which runtime should the devcontainer target? → A: Runtime-agnostic — the devcontainer only requires an OCI-compliant, Docker-API-compatible runtime; Podman is called out as the project's current primary/tested default, matching `.vscode/tasks.json`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Onboard without installing toolchains on the host (Priority: P1)

A new contributor clones the repository and opens it in a devcontainer-capable editor or
tool. Instead of manually installing the .NET SDK, Node.js, and Bun on their own machine
by following `CONTRIBUTING.md` step by step, the environment is built automatically from
the project's configuration, and the contributor lands in a ready-to-use shell with every
tool already present at the correct version.

**Why this priority**: This is the core value of the feature — it is what "accelerates
development and reduces system dependencies" means in practice. Without this, the feature
delivers nothing.

**Independent Test**: Starting from a clean checkout with only a container runtime and a
supporting editor installed (no .NET SDK, no Node.js/Bun on the host), open the project in
the devcontainer and confirm a working shell is reached without any additional manual
installation step.

**Acceptance Scenarios**:

1. **Given** a clean checkout of the repository and no language runtimes installed on the
   host, **When** the contributor opens the project in a devcontainer-capable tool, **Then**
   the environment builds and provides a shell with the backend and frontend toolchains
   already installed and on the `PATH`.
2. **Given** the devcontainer environment is running, **When** the contributor runs the
   documented backend build command, **Then** it completes successfully without requiring
   any host-installed .NET SDK.
3. **Given** the devcontainer environment is running, **When** the contributor runs the
   documented frontend install and build commands, **Then** they complete successfully
   without requiring any host-installed Node.js or Bun.

---

### User Story 2 - Run the full test suite inside the container (Priority: P2)

A contributor who is already working inside the devcontainer wants to run the project's
backend and frontend test suites — including the integration tests that depend on a
container runtime — without leaving the devcontainer or falling back to the host.

**Why this priority**: Being able to build is not enough; contributors need to verify their
changes before opening a PR. If tests can only run on the host, the devcontainer only
half-solves the onboarding problem.

**Independent Test**: From inside a running devcontainer, execute the backend architecture,
unit, and integration test commands, and the frontend check/lint/test commands, and confirm
they all complete with the same outcome as when run following the existing native setup in
`CONTRIBUTING.md`.

**Acceptance Scenarios**:

1. **Given** the devcontainer is running, **When** the contributor runs the backend
   architecture and unit test commands, **Then** they complete without needing anything
   beyond what the container already provides.
2. **Given** the devcontainer is running, **When** the contributor runs the backend
   integration test command, **Then** it completes successfully, including any test that
   spins up real infrastructure via a container runtime.
3. **Given** the devcontainer is running, **When** the contributor runs the frontend check,
   lint, and test commands, **Then** they all complete without needing anything beyond what
   the container already provides.

---

### User Story 3 - Supply local credentials without baking them into the image (Priority: P3)

A contributor needs to run agents locally, which requires credentials such as
`ANTHROPIC_AUTH_TOKEN`. They want a documented way to provide these to the devcontainer
without committing secrets to the repository or embedding them in the container image.

**Why this priority**: Agent runs are part of the documented local workflow
(`.env-example` → `data/.env`). A devcontainer that cannot receive these credentials would
be incomplete for anyone doing agent-related work, but this is secondary to simply being
able to build and test.

**Independent Test**: Following only the updated documentation, a contributor supplies a
credential value to the devcontainer and confirms it is visible to a process running inside
the container, without that value being present in the image definition or version control.

**Acceptance Scenarios**:

1. **Given** the devcontainer configuration as checked into the repository, **When** it is
   inspected, **Then** no credential values are present in it.
2. **Given** a contributor has followed the documented steps to supply local credentials,
   **When** an agent process runs inside the devcontainer, **Then** it can read the
   credential value it needs.

---

### Edge Cases

- What happens when the contributor's tooling does not support the devcontainer
  specification, or no container runtime is available on the host? The existing
  native setup path documented in `CONTRIBUTING.md` MUST remain available and unaffected.
- What happens when the pinned .NET SDK or Bun version changes in the repository (e.g. a
  `Directory.Build.props` or `bun.lock` bump)? The devcontainer environment must be updated
  to match rather than silently drifting out of sync with what the native setup documents.
- What happens when a contributor has not supplied the optional local credentials? Build,
  lint, type-check, and the hermetic test suites (architecture, unit, integration) must
  still work; only agent runs and the gated evaluation tests that need those credentials
  are affected.
- What happens when integration tests inside the devcontainer need to start additional
  containers (Testcontainers)? The devcontainer environment must give them a way to reach a
  container runtime.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST provide a devcontainer configuration, conforming to the
  containers.dev specification, that a compatible editor or tool can detect and use to
  build a ready-to-use development environment automatically.
- **FR-002**: The devcontainer environment MUST include the backend toolchain (the .NET SDK
  version the repository already pins) pre-installed, so contributors can build and run the
  backend test suites without installing it on the host.
- **FR-003**: The devcontainer environment MUST include the frontend toolchain (Node.js and
  Bun, matching what the repository's frontend already requires) pre-installed, so
  contributors can install dependencies, check, lint, build, and test the frontend without
  installing them on the host.
- **FR-004**: The devcontainer environment MUST allow the backend integration test suite
  (which depends on a container runtime) to run from inside the container itself, using any
  OCI-compliant, Docker-API-compatible runtime on the host — Podman is the project's
  current primary/tested runtime (per `.vscode/tasks.json`), and the mechanism MUST NOT
  assume Docker Desktop is the only supported host runtime.
- **FR-005**: The devcontainer setup MUST provide a documented mechanism for contributors to
  supply local secrets/credentials (e.g. the values currently placed in `data/.env`) to
  processes running inside the container, without those values being committed to the
  repository or embedded in the container image.
- **FR-006**: The devcontainer configuration MUST NOT hardcode credential values or other
  secrets.
- **FR-007**: `CONTRIBUTING.md` MUST be updated to describe the devcontainer-based setup as
  an available onboarding path, while the existing native (host-installed toolchain) setup
  instructions MUST remain documented and functional as a fallback.
- **FR-008**: The devcontainer setup MUST NOT introduce a requirement for new persistent
  cloud infrastructure; it only automates provisioning of the local development
  environment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A contributor with no backend or frontend toolchain installed on their host
  can go from opening the repository in a devcontainer-capable tool to having both the
  backend and frontend build commands complete successfully in under 15 minutes.
- **SC-002**: 100% of the tooling required to build and run the hermetic backend and
  frontend test suites (architecture tests, unit tests, integration tests, frontend
  check/lint/test) is available inside the devcontainer without any additional host-level
  installation.
- **SC-003**: Running the full hermetic test suite (backend architecture, unit, and
  integration tests; frontend check, lint, and test) inside the devcontainer produces the
  same pass/fail outcome as running the equivalent commands via the previously documented
  native host setup.
- **SC-004**: Zero credential values are present in the devcontainer configuration files
  checked into the repository.

## Assumptions

- The devcontainer targets local, editor-driven development (e.g. VS Code Dev Containers,
  or any other containers.dev-compatible tool); updating CI pipelines to reuse the same
  container image is out of scope for this feature.
- An OCI-compliant, Docker-API-compatible container runtime is available on the
  contributor's host machine; the devcontainer automates toolchain provisioning, not the
  underlying container runtime itself. Podman is the project's current primary/tested
  runtime (the existing `.vscode/tasks.json` already starts a Podman machine before any
  `docker`-CLI task), so the devcontainer setup and its documentation MUST be verified
  against Podman and MUST NOT hardcode Docker Desktop-only assumptions, while remaining
  compatible with Docker Desktop and other Docker-API-compatible runtimes.
- The devcontainer is an additional, opt-in onboarding path. It does not replace or
  deprecate the native setup instructions in `CONTRIBUTING.md` — contributors who prefer a
  host-installed toolchain can continue to use it.
- Access to a container runtime for the backend integration test suite (Testcontainers) is
  provided from inside the devcontainer via a mechanism such as a mounted container socket
  or Docker-in-Docker; the specific mechanism is a technical decision for the planning
  phase, not a scope decision for this specification.
- Local credentials (e.g. `ANTHROPIC_AUTH_TOKEN`) continue to be supplied via an
  environment file or environment variables outside version control, consistent with the
  existing `.env-example` → `data/.env` pattern; the devcontainer setup only needs to make
  that file/those variables reachable from inside the container.

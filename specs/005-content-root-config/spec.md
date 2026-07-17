# Feature Specification: Explicit Path Configuration (Decouple from Repository Structure)

**Feature Branch**: `005-content-root-config`

**Created**: 2026-07-18

**Status**: Draft

**Input**: User description: "currently the code relies on the repo structure (repoRoot variable) and the content-root is relative to this repoRoot. That is not intuitive and wrong for a production ready application. the application should be called with relative or fixed file paths and should not rely on a specific project structure, which might not exists in a deployed production environment"

## Problem Statement

Today the system determines every storage location it needs — the wiki content root, raw
intake storage, the operational state store, the secrets file, and the agent instruction
files — by first discovering the root of the source-code repository it happens to be
running inside, and then deriving all other paths from that root using a hard-coded
project layout. A deployed production environment has no source-code repository and no
such layout, so the application as-is cannot be deployed outside a developer checkout.
Operators also cannot reason about where data lives, because locations are implicit
consequences of project structure rather than explicit configuration.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deploy to a production environment without a source checkout (Priority: P1)

An operator installs the application in an arbitrary directory on a production host —
one that contains no source-code repository and no development project structure. They
provide the storage locations the system needs (wiki content root, raw intake storage,
operational state store, secrets, agent instruction files) as explicit configuration:
absolute paths, or paths relative to where the application is started. The application
starts and operates fully using only those configured locations.

**Why this priority**: This is the core of the request — the application is currently
impossible to deploy outside a developer checkout. Every other story builds on explicit
path configuration existing.

**Independent Test**: Copy the deployable application into an empty directory (no
repository metadata, no project folders), provide explicit path configuration pointing
at prepared data directories, start the application, and complete an ingest submission
end to end.

**Acceptance Scenarios**:

1. **Given** a host directory containing only the deployable application and configured
   data directories, **When** the operator starts the application with explicit path
   configuration, **Then** the application starts successfully and serves requests
   without consulting any source-repository structure.
2. **Given** the application is running with explicit path configuration, **When** an
   ingest submission is processed, **Then** all artifacts (raw originals, normalized
   sources, task artifacts, wiki pages, index and log updates) are written under the
   configured locations and nowhere else.
3. **Given** a host without any version-control tooling installed, **When** the
   application starts, **Then** startup succeeds — the application does not invoke or
   require version-control tooling to locate its data.

---

### User Story 2 - Run locally with sensible defaults (Priority: P2)

A developer starts the application from the project checkout without providing any path
configuration. Sensible defaults — resolved relative to a single, clearly documented
base location (the directory the application is started from, or an explicitly provided
base directory) — keep the existing local workflow working: the wiki content root, raw
storage, and operational state end up in the same places the developer uses today.

**Why this priority**: The development workflow must not regress; defaults make the
common case zero-configuration while the production case (Story 1) stays explicit.

**Independent Test**: Start the application from the project checkout with no path
configuration and verify it reads and writes the same locations as before the change.

**Acceptance Scenarios**:

1. **Given** no path configuration is provided, **When** the application starts from
   the project checkout, **Then** it resolves all locations from documented defaults
   relative to the start/base directory and the existing local data is used.
2. **Given** a developer provides only one overridden location (e.g., a different wiki
   content root), **When** the application starts, **Then** the override is honored and
   all other locations fall back to their documented defaults.

---

### User Story 3 - Clear feedback on misconfiguration (Priority: P3)

An operator provides path configuration that is incomplete or points at locations that
do not exist or are not usable. The application refuses to start and reports exactly
which configured location is the problem and why, instead of failing later mid-operation
with an obscure error.

**Why this priority**: Explicit configuration shifts responsibility to the operator;
without precise startup validation, misconfiguration surfaces as confusing runtime
failures deep inside ingest runs.

**Independent Test**: Start the application with a configuration pointing a required
input location at a nonexistent path and verify startup fails immediately with a message
naming that location.

**Acceptance Scenarios**:

1. **Given** a required input location (e.g., agent instruction files or secrets) does
   not exist, **When** the application starts, **Then** startup fails immediately with a
   message that names the missing location and the configured path value.
2. **Given** a writable data location (e.g., raw storage or the operational state
   directory) does not exist yet, **When** the application starts, **Then** the
   application creates it and continues, and the effective location is reported in the
   startup output.
3. **Given** the application starts successfully, **When** an operator inspects the
   startup output, **Then** every effective (fully resolved) storage location is listed,
   so the operator can verify where data will live.

---

### Edge Cases

- Relative paths in configuration: resolved against the application's start/base
  directory — never against a discovered repository root. The resolution base is
  documented and reported at startup.
- The same location provided through multiple configuration channels (command line,
  environment, configuration file): a single documented precedence order applies
  (command line over environment over configuration file over defaults).
- The wiki content root configured to a location outside the base directory (e.g., a
  mounted volume): fully supported; no location is required to be inside any other.
- Agent worker processes started by the harness: they receive their locations from the
  harness explicitly and must not independently rediscover any repository or project
  structure.
- Source files submitted with relative paths: resolved against the submitter's stated
  working context as today, not against a repository root.
- A configured path points at a file where a directory is expected (or vice versa):
  startup validation fails with a message naming the location.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every file-system location the system uses MUST be individually
  configurable by the operator: wiki content root, raw intake storage, operational
  state store, secrets location, agent instruction set location, and the location of
  the agent worker the harness launches.
- **FR-002**: The application MUST NOT discover, require, or assume a source-code
  repository or development project structure at runtime, and MUST NOT require
  version-control tooling to be installed on the host.
- **FR-003**: Configured relative paths MUST resolve against a single documented base
  (the application's start directory, or an explicitly configured base directory) —
  never against a discovered repository root.
- **FR-004**: When no configuration is provided, the system MUST fall back to
  documented defaults that preserve the existing local development workflow when
  started from the project checkout.
- **FR-005**: Path configuration MUST be acceptable through the standard configuration
  channels (command line, environment, configuration file) with one documented
  precedence order: command line over environment over configuration file over defaults.
- **FR-006**: At startup the system MUST validate all configured locations: required
  input locations (agent instruction set, secrets) that are missing or of the wrong
  kind cause immediate startup failure with a message naming the location and its
  configured value; writable data locations that do not exist yet are created.
- **FR-007**: Agent worker processes MUST receive all locations they operate on
  explicitly from the harness and MUST NOT perform any independent discovery of
  repository or project structure.
- **FR-008**: The system MUST report every effective (fully resolved, absolute)
  location in its startup output so operators can verify the configuration.
- **FR-009**: Paths recorded in task artifacts and reports MUST be expressed relative
  to the configured locations they belong to (e.g., pages relative to the wiki content
  root), not relative to any repository root.

### Key Entities

- **Path Configuration**: The named set of file-system locations the system operates
  on; each entry has a name, a configured value (absolute or relative), a resolved
  absolute value, a kind (required input vs. writable data), and a source (command
  line, environment, configuration file, or default).
- **Wiki Content Root**: The directory holding the wiki maintained by agents (pages,
  tasks, index, log). Configured directly; no longer derived from a repository root.
- **Raw Intake Storage**: The directory holding pre-agent intake artifacts (originals
  and normalized sources). Configured independently of the content root.
- **Agent Instruction Set Location**: The directory holding the versioned instruction
  and policy files loaded into the agent's context. A required input location.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of application starts in a directory with no source-repository
  structure succeed when valid explicit path configuration is provided, and 100% of
  file-system reads/writes during operation occur under the configured locations.
- **SC-002**: 100% of application starts with a missing or invalid required input
  location fail during startup (before serving any request) with a message naming the
  offending location and its configured value.
- **SC-003**: Starting from the project checkout with zero path configuration uses the
  same effective locations as before this feature in 100% of starts (no developer
  workflow regression).
- **SC-004**: 100% of agent worker runs receive their operating locations from the
  harness; no agent worker run performs repository or project-structure discovery.
- **SC-005**: 100% of successful starts report every effective storage location in the
  startup output.

## Assumptions

- The system remains a single-node application operating on a local (or mounted)
  file system; remote/object storage is out of scope for this feature.
- The internal layout *within* each configured location (e.g., pages/tasks/index/log
  inside the wiki content root, originals/sources inside raw intake storage) is owned
  by the system and unchanged by this feature; only the roots become configurable.
- Existing standard configuration channels (command line, environment, configuration
  file) are reused; no new configuration mechanism is invented.
- Writable data locations (raw intake storage, operational state store, wiki content
  root) are auto-created when absent; required input locations (agent instruction set,
  secrets) are never auto-created because the system cannot invent their content.
- Defaults are chosen so that starting from the project checkout reproduces today's
  effective locations; deployed environments are expected to configure paths
  explicitly.
- How the deployable application is packaged for production (and how the agent worker
  is packaged alongside it) is an implementation/planning concern; this feature only
  requires that its location be explicitly configurable rather than derived from a
  source-project layout.

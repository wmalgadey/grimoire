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

## Clarifications

### Session 2026-07-18

- Q: When a base directory is chosen (the repo checkout or any other directory), how should the default on-disk layout of runtime data look underneath it? → A: Consolidated data directory — all runtime data defaults (wiki content root, raw intake storage, operational state store, agent instruction set, secrets) live under one folder beneath the base directory; existing checkout data is moved there once, and the repo checkout remains a valid base directory.
- Q: When no base directory is explicitly configured, what is the default base for the consolidated data directory? → A: The process working directory; launch/deployment configurations pin an explicit base (or working directory) per environment.
- Q: How are production and development launch setups separated within the one codebase? → A: Configuration only — each launch configuration passes its own base directory / path overrides through the standard channels; the application itself stays profile-agnostic (no named-profile mechanism, no environment-keyed default sets).
- Q: How does existing runtime data in the current checkout (wiki/, raw/, backend/data/, agents/ingest/, .env) get into the new consolidated data directory? → A: Manual one-time move with documented instructions (optionally a one-off script). The application knows only the new layout — no legacy-layout detection and no automatic migration.
- Q (plan revision): Does the wiki content root live inside the consolidated data directory? → A: No — the wiki directory is separate from the data directory (default: its own directory beside the data directory under the base), so the wiki can be version-controlled and committed independently of the application's internal runtime data. The consolidated data directory holds the internal runtime data (raw intake, operational state, agent instruction set, secrets).

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
configuration. Sensible defaults keep the local workflow working with zero
configuration: the wiki lives in its own directory beneath the base (independently
committable to git), and all internal runtime data (raw storage, operational state,
agent instruction set, secrets) lives under a single consolidated data directory
beside it — whether the base is the repo checkout or any other directory the user
chooses. Existing checkout data is moved into the consolidated layout once
(documented, one-time).

**Why this priority**: The development workflow must stay zero-configuration while
gaining an obvious, single home for all current and future runtime data; the production
case (Story 1) stays explicit.

**Independent Test**: Start the application from the project checkout with no path
configuration and verify all reads and writes occur under the consolidated data
directory beneath the checkout.

**Acceptance Scenarios**:

1. **Given** no path configuration is provided, **When** the application starts from
   the project checkout, **Then** it resolves the wiki content root to the wiki
   directory beneath the base and every internal runtime data location to the
   consolidated data directory beneath the base, using the (once-migrated) existing
   local data.
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
- Legacy pre-consolidation data still present at the old scattered locations under the
  base directory: the application ignores it — it performs no legacy-layout detection
  or automatic migration; missing required inputs surface through normal startup
  validation, and the documented one-time move is the remedy.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every file-system location the system uses MUST be individually
  configurable by the operator: wiki content root, raw intake storage, operational
  state store, secrets location, agent instruction set location, and the location of
  the agent worker the harness launches.
- **FR-002**: The application MUST NOT discover, require, or assume a source-code
  repository or development project structure at runtime, and MUST NOT require
  version-control tooling to be installed on the host.
- **FR-003**: Configured relative paths MUST resolve against a single documented base:
  the explicitly configured base directory, or — when none is configured — the process
  working directory. Resolution MUST never depend on a discovered repository root.
- **FR-004**: When no configuration is provided, the system MUST fall back to a
  documented default layout beneath the base directory with exactly two homes: the
  wiki content root in its own directory (independently version-controllable), and
  all internal runtime data locations beneath a single consolidated data directory —
  one obvious home for all current and future internal runtime data, defined in one
  place rather than scattered through the system.
- **FR-005**: Path configuration MUST be acceptable through the standard configuration
  channels (command line, environment, configuration file) with one documented
  precedence order: command line over environment over configuration file over defaults.
  Production and development setups differ only in the configuration values each launch
  passes; the application contains no environment-specific path logic or named-profile
  mechanism.
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
- **Runtime Data Base Directory**: The single directory the operator chooses as the
  home for runtime data — the repo checkout or any other directory. By default the
  wiki content root resolves to its own directory beneath it and all internal runtime
  data resolves into one consolidated data directory beneath it; individual locations
  may still be overridden to point elsewhere.
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
- **SC-003**: Starting from the project checkout with zero path configuration resolves
  100% of runtime data locations beneath the checkout — the wiki content root in the
  wiki directory, everything else beneath the consolidated data directory — and the
  developer workflow continues to work after the documented one-time data move.
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
- Default locations follow the two-home layout beneath the base (wiki directory +
  consolidated data directory); today's scattered checkout locations are migrated by
  a documented one-time move, after which the repo checkout remains a fully valid
  base directory. Deployed environments may rely on the same defaults or configure
  paths explicitly. Keeping the wiki outside the data directory is deliberate: it can
  be committed to (its own) git independently of internal runtime data.
- Agent instruction files (system prompt, policy) are runtime data owned by a
  deployment: they live under the consolidated data directory (a required input
  location), not inside the application's code/install location.
- How the deployable application is packaged for production (and how the agent worker
  is packaged alongside it) is an implementation/planning concern; this feature only
  requires that its location be explicitly configurable rather than derived from a
  source-project layout.

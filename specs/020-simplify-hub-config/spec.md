# Feature Specification: Simplify Hub CLI Configuration

**Feature Branch**: `020-simplify-hub-config`

**Created**: 2026-08-06

**Status**: Draft

**Input**: User description: "the current hub cli options are too complex and do not work as expected. there should be a minimum of configuration options which have to be present to run the system, everything else should be default.

1. the directory structure should be a single location for the working/data directory. everything runtime relevant should be inside this directory
2. the user could choose a different llm-wiki folder for the actual agent results like wiki content, index.md, log.md, tasks and conversations.
3. the agents location and instruction files should be the same like the working/data directory. a single agent directory should contain the agents runtime and instructions files. no need to split everythin into it's own config option

we should only need to configure, what the user currently wants to configure, not what is possible to configure"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the hub with zero configuration (Priority: P1)

An operator sets up the hub for the first time and runs a hub command without providing any configuration at all. The system uses built-in defaults for every path it needs and works immediately, without the operator needing to understand or set any of today's sixteen separate path options.

**Why this priority**: This is the core complaint driving the feature — configuration is currently mandatory-feeling and confusing even though most of it never needs to change. A working zero-configuration path is the minimum viable fix and unblocks every other scenario.

**Independent Test**: Can be fully tested by invoking any hub command in a fresh, empty directory with no configuration flags or environment variables set, and confirming the command completes successfully using only default locations.

**Acceptance Scenarios**:

1. **Given** a fresh environment with no configuration provided, **When** an operator runs any hub command, **Then** the command completes successfully using built-in default locations for all runtime state and agent results.
2. **Given** a fresh environment with no configuration provided, **When** an operator runs a hub command, **Then** the working/data directory and the wiki directory are created automatically if they do not already exist.

---

### User Story 2 - Relocate agent results to a chosen wiki folder (Priority: P2)

An operator wants the agent-maintained results — wiki content, `index.md`, `log.md`, tasks, and conversations — to live in a specific folder of their choosing (for example, a separately git-tracked repository), independent of where the rest of the hub's runtime state lives. They set exactly one option to point to that folder; everything else keeps using defaults.

**Why this priority**: Separating agent output from internal runtime state is the second most common real need (e.g. to version-control or share the wiki independently) and must remain possible after simplification, but it should not force the operator to configure anything beyond this one path.

**Independent Test**: Can be fully tested by setting only the wiki-directory option to a custom path, running a hub command that produces agent results, and confirming wiki content, `index.md`, `log.md`, tasks, and conversations all land under that custom path while all other runtime state still uses defaults.

**Acceptance Scenarios**:

1. **Given** only the wiki-directory option is set to a custom path, **When** an operation that produces agent results runs, **Then** wiki content, `index.md`, `log.md`, tasks, and conversations are all written under the configured custom path.
2. **Given** only the wiki-directory option is set to a custom path, **When** an operation that produces agent results runs, **Then** all other runtime state (working/data directory contents) continues to use its default location, unaffected by the wiki-directory override.

---

### User Story 3 - Relocate the entire working/data directory (Priority: P3)

An operator running multiple isolated hub environments (for example, per project or per branch) wants all harness runtime state — including the combined agent directory — to live under a different root than the default. They set exactly one option to point to that root; every runtime-relevant item is found beneath it automatically.

**Why this priority**: Full relocation of runtime state is needed for isolation between environments, but it is a less frequent need than either running with defaults or relocating just the wiki output, so it is lower priority than Stories 1 and 2.

**Independent Test**: Can be fully tested by setting only the working/data-directory option to a custom path, running hub commands that create runtime state (intake, state database, secrets, write-locks, findings, remediation tasks, agent instructions/runtime), and confirming every one of those items is created under the configured custom path with no separate options needed.

**Acceptance Scenarios**:

1. **Given** only the working/data-directory option is set to a custom path, **When** hub commands run and produce runtime state, **Then** raw intake, the operational state database, secrets, write-locks, findings, remediation tasks, and the agent directory are all located under the configured custom path.
2. **Given** only the working/data-directory option is set to a custom path and the wiki-directory option is left unset, **When** an operation that produces agent results runs, **Then** the wiki output is created at its default location nested under the configured working/data directory.

---

### Edge Cases

- What happens when an operator supplies a legacy configuration option that has been removed (e.g. a former per-artifact path switch or environment variable)? The command MUST fail with an error naming the removed option and pointing to its replacement, rather than silently ignoring it or misbehaving.
- What happens when the configured working/data directory or wiki directory does not yet exist? The system MUST create it automatically rather than failing.
- What happens when the wiki-directory path is configured to be the same as, or nested inside, the working/data directory? The system MUST accept this (it matches the default relationship) without treating it as an error.
- What happens when only the wiki directory is overridden and the working/data directory is left at its default? All runtime-relevant state other than agent results MUST still resolve correctly under the default working/data directory.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow every hub command to run successfully with zero explicitly-provided configuration, relying entirely on built-in default paths.
- **FR-002**: The system MUST expose exactly one configuration option for the working/data directory: the single root beneath which all harness runtime state is located.
- **FR-003**: The system MUST expose exactly one configuration option, independent of the working/data directory, for the wiki directory: the root beneath which all agent-produced results (wiki content, `index.md`, `log.md`, tasks, and conversations) are located.
- **FR-004**: The system MUST allow the working/data-directory option and the wiki-directory option to each be set independently — configuring one MUST NOT require configuring the other.
- **FR-005**: The system MUST locate all runtime-relevant harness state under the working/data directory: raw intake, the operational state database, secrets, write-locks, findings, remediation tasks, and the agent directory.
- **FR-006**: The system MUST combine agent instruction files and agent runtime data for every agent type (ingest, query, lint) into a single agent directory nested under the working/data directory, rather than exposing a separate configuration option per agent type.
- **FR-007**: The system MUST automatically create the working/data directory and the wiki directory when they do not already exist, rather than requiring the operator to pre-create them.
- **FR-008**: The system MUST reject any use of a removed legacy configuration option with an error that names the removed option and identifies its replacement, rather than silently ignoring it.
- **FR-009**: The system MUST default the working/data directory to the process's current working directory when the option is not explicitly provided.
- **FR-010**: The system MUST default the wiki directory to a fixed subfolder nested under the working/data directory when the option is not explicitly provided.

### Key Entities

- **Working/Data Directory**: The single configurable root holding all harness runtime state — raw intake, the operational state database, secrets, write-locks, findings, remediation tasks, and the Agent Directory. Defaults to the current working directory.
- **Wiki Directory**: The single, independently configurable root holding all agent-produced results — wiki content pages, `index.md`, `log.md`, tasks, and conversations. Defaults to a fixed subfolder nested under the Working/Data Directory, but may be pointed anywhere (e.g. a separately git-tracked location).
- **Agent Directory**: A single directory nested under the Working/Data Directory containing both instruction files and runtime data for every agent type (ingest, query, lint), replacing today's three separate per-agent instruction directories. Not independently configurable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of hub commands complete successfully when invoked with zero explicitly-provided configuration options.
- **SC-002**: The user-facing configuration surface consists of exactly 2 options (working/data directory, wiki directory), down from the current 16 independent path switches.
- **SC-003**: 100% of runtime-relevant artifacts (raw intake, state database, secrets, write-locks, findings, remediation tasks, agent instructions, agent runtime data) resolve under the single configured working/data directory.
- **SC-004**: 100% of agent-result artifacts (wiki content, `index.md`, `log.md`, tasks, conversations) resolve under the single configured wiki directory, regardless of the working/data directory's location.
- **SC-005**: 100% of commands invoked with a removed legacy configuration option fail with an error identifying the removed option and its replacement, rather than proceeding silently.

## Assumptions

- The working/data directory defaults to the process's current working directory, preserving today's default behavior for the common case of running hub from within a project checkout.
- The wiki directory defaults to a fixed-name subfolder nested under the working/data directory, but the operator can point it anywhere (including outside the working/data directory) to preserve today's ability to version-control the wiki independently.
- Agent worker executable resolution (today three separate switches, one per agent type) is no longer independently configurable; workers are always auto-resolved relative to the hub's own binaries, consistent with reducing the configuration surface to the two options described above.
- This is a breaking change to the existing 16-switch/environment-variable configuration surface. No deprecated aliases or silent migration shims are provided, since the hub is an internal tool without external consumers depending on the old surface; any scripts or CI invoking removed options must be updated to the new two-option surface.
- The three existing per-agent instruction directories (ingest, query, lint) are consolidated into subfolders of the single Agent Directory rather than being merged into one undifferentiated set of files.

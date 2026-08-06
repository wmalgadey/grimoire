# Feature Specification: Simplify Hub CLI Configuration

**Feature Branch**: `020-simplify-hub-config`

**Created**: 2026-08-06

**Status**: Draft

**Input**: User description: "the current hub cli options are too complex and do not work as expected. there should be a minimum of configuration options which have to be present to run the system, everything else should be default.

1. the directory structure should be a single location for the working/data directory. everything runtime relevant should be inside this directory
2. the user could choose a different llm-wiki folder for the actual agent results like wiki content, index.md, log.md, tasks and conversations.
3. the agents location and instruction files should be the same like the working/data directory. a single agent directory should contain the agents runtime and instructions files. no need to split everythin into it's own config option

we should only need to configure, what the user currently wants to configure, not what is possible to configure"

## Clarifications

### Session 2026-08-06

- Q: Do findings and remediation-tasks belong under the working/data directory or the wiki directory? → A: Wiki directory — they are agent output, alongside wiki content, `index.md`, `log.md`, tasks, and conversations.
- Q: Given the system is pre-1.0 with no legacy consumers, does removing a legacy CLI option need dedicated error handling (naming the removed option and its replacement)? → A: No — standard "unrecognized option" rejection from the CLI parser is sufficient; no dedicated legacy-option handling is required.
- Q: Should advanced/internal directory-layout overrides (e.g. relocating a specific sub-path within the working/data, wiki, or agent directory) remain possible, and if so, how? → A: Yes, but only via a configuration file (e.g. an `appsettings.json`-style settings file) — never via additional CLI switches. The CLI surface stays fixed at exactly 2 options.
- Q: Should the wiki directory's default location be nested under the working/data directory, or should the two default to separate, sibling locations? → A: Separate by default — the working/data directory and the wiki directory MUST be independent, sibling locations out of the box; only the agent directory is nested inside the working/data directory by default. (This supersedes the earlier answer in this session that kept the wiki directory nested by default.)

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

An operator wants the agent-maintained results — wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks — to live in a specific folder of their choosing (for example, a separately git-tracked repository), independent of where the rest of the hub's runtime state lives. They set exactly one option to point to that folder; everything else keeps using defaults.

**Why this priority**: Separating agent output from internal runtime state is the second most common real need (e.g. to version-control or share the wiki independently) and must remain possible after simplification, but it should not force the operator to configure anything beyond this one path.

**Independent Test**: Can be fully tested by setting only the wiki-directory option to a custom path, running a hub command that produces agent results, and confirming wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks all land under that custom path while all other runtime state still uses defaults.

**Acceptance Scenarios**:

1. **Given** only the wiki-directory option is set to a custom path, **When** an operation that produces agent results runs, **Then** wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks are all written under the configured custom path.
2. **Given** only the wiki-directory option is set to a custom path, **When** an operation that produces agent results runs, **Then** all other runtime state (working/data directory contents) continues to use its default location, unaffected by the wiki-directory override.

---

### User Story 3 - Relocate the entire working/data directory (Priority: P3)

An operator running multiple isolated hub environments (for example, per project or per branch) wants all harness runtime state — including the combined agent directory — to live under a different root than the default. They set exactly one option to point to that root; every runtime-relevant item is found beneath it automatically.

**Why this priority**: Full relocation of runtime state is needed for isolation between environments, but it is a less frequent need than either running with defaults or relocating just the wiki output, so it is lower priority than Stories 1 and 2.

**Independent Test**: Can be fully tested by setting only the working/data-directory option to a custom path, running hub commands that create runtime state (intake, state database, secrets, write-locks, agent instructions/runtime), and confirming every one of those items is created under the configured custom path with no separate options needed.

**Acceptance Scenarios**:

1. **Given** only the working/data-directory option is set to a custom path, **When** hub commands run and produce runtime state, **Then** raw intake, the operational state database, secrets, write-locks, and the agent directory are all located under the configured custom path.
2. **Given** only the working/data-directory option is set to a custom path and the wiki-directory option is left unset, **When** an operation that produces agent results runs, **Then** the wiki output is created at its own independent default location — a sibling of the default working/data directory, unaffected by the working/data-directory override, not nested inside the configured custom path.

---

### User Story 4 - Customize internal directory layout via a configuration file (Priority: P4)

An advanced operator with unusual infrastructure needs (for example, putting the state database on different storage than raw intake) wants to relocate a specific internal sub-path beneath the working/data directory, the wiki directory, or the agent directory, without the CLI growing extra switches for it. They declare the override in a configuration file; the two CLI options keep working unchanged for everyone else.

**Why this priority**: This is a power-user escape hatch, not required for the core simplification goal, so it is the lowest priority — but it preserves the layout flexibility the current 16-switch surface offers today, without reintroducing CLI complexity.

**Independent Test**: Can be fully tested by declaring one sub-path override in the configuration file (e.g. relocating the state database), running a hub command, and confirming that artifact resolves to the overridden path while every other artifact stays at its default.

**Acceptance Scenarios**:

1. **Given** a configuration file declares an override for one internal sub-path beneath the working/data directory, **When** a hub command runs, **Then** that artifact resolves to the overridden path while all other artifacts keep their default locations.
2. **Given** no configuration-file overrides are present, **When** a hub command runs, **Then** internal sub-paths resolve to their built-in defaults beneath the working/data directory, the wiki directory, and the agent directory.

---

### Edge Cases

- What happens when the configured working/data directory or wiki directory does not yet exist? The system MUST create it automatically rather than failing.
- What happens when the wiki-directory path is explicitly configured to be the same as, or nested inside, the working/data directory? The system MUST accept this as a valid explicit override — even though it departs from the default sibling relationship — without treating it as an error.
- What happens when only the wiki directory is overridden and the working/data directory is left at its default? All runtime-relevant state other than agent results MUST still resolve correctly under the default working/data directory.
- What happens when an operator supplies a CLI option that no longer exists (e.g. a former per-artifact path switch)? The command fails with the CLI parser's standard "unrecognized option" error; no dedicated legacy-option detection or replacement guidance is built for this.
- What happens when a configuration-file layout override and a CLI-provided root directory option are both present? The configuration-file override only relocates a sub-path *within* whichever working/data or wiki directory is already in effect (from the CLI option or its default) — it cannot replace or redirect the root directories themselves.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow every hub command to run successfully with zero explicitly-provided configuration, relying entirely on built-in default paths.
- **FR-002**: The system MUST expose exactly one configuration option for the working/data directory: the single root beneath which all harness runtime state is located.
- **FR-003**: The system MUST expose exactly one configuration option, independent of the working/data directory, for the wiki directory: the root beneath which all agent-produced results (wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks) are located.
- **FR-004**: The system MUST allow the working/data-directory option and the wiki-directory option to each be set independently — configuring one MUST NOT require configuring the other.
- **FR-005**: The system MUST locate all runtime-relevant harness state under the working/data directory: raw intake, the operational state database, secrets, write-locks, and the agent directory.
- **FR-006**: The system MUST combine agent instruction files and agent runtime data for every agent type (ingest, query, lint) into a single agent directory nested under the working/data directory, rather than exposing a separate configuration option per agent type.
- **FR-007**: The system MUST automatically create the working/data directory and the wiki directory when they do not already exist, rather than requiring the operator to pre-create them.
- **FR-008**: The system MUST default the working/data directory to a fixed-name subfolder of the process's current working directory (not the current working directory itself) when the option is not explicitly provided, so that the default working/data directory and the default wiki directory can be siblings rather than one nesting inside the other.
- **FR-009**: The system MUST default the wiki directory to a fixed-name subfolder of the process's current working directory when the option is not explicitly provided, as a sibling of the default working/data directory — not nested inside it. Only the agent directory (FR-006) nests inside the working/data directory by default.
- **FR-010**: The system MUST allow further customization of internal sub-paths beneath the working/data directory, the wiki directory, or the agent directory only through a configuration file (e.g. an `appsettings.json`-style settings file), never through additional CLI switches, so the CLI surface remains fixed at the two options in FR-002 and FR-003. Such overrides relocate a sub-path within whichever root directory is already in effect; they MUST NOT be usable to set the root directories themselves.

### Key Entities

- **Working/Data Directory**: The single configurable root holding all harness runtime state — raw intake, the operational state database, secrets, write-locks, and the Agent Directory. Defaults to a fixed-name subfolder of the process's current working directory, kept separate from (a sibling of) the default Wiki Directory.
- **Wiki Directory**: The single, independently configurable root holding all agent-produced results — wiki content pages, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks. Defaults to a fixed-name subfolder of the process's current working directory, as a sibling of the default Working/Data Directory rather than nested inside it — but may be pointed anywhere (e.g. a separately git-tracked location).
- **Agent Directory**: A single directory nested under the Working/Data Directory containing both instruction files and runtime data for every agent type (ingest, query, lint), replacing today's three separate per-agent instruction directories. Not independently configurable via the CLI.
- **Directory Layout Configuration File**: An optional settings file (e.g. `appsettings.json`-style) that advanced operators may use to relocate specific internal sub-paths beneath the Working/Data Directory, the Wiki Directory, or the Agent Directory. Not required for normal operation and never exposed as a CLI switch.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of hub commands complete successfully when invoked with zero explicitly-provided configuration options.
- **SC-002**: The user-facing CLI configuration surface consists of exactly 2 options (working/data directory, wiki directory), down from the current 16 independent path switches. Optional configuration-file-based sub-path overrides (FR-010) do not add to this count.
- **SC-003**: 100% of runtime-relevant artifacts (raw intake, state database, secrets, write-locks, agent instructions, agent runtime data) resolve under the single configured working/data directory.
- **SC-004**: 100% of agent-result artifacts (wiki content, `index.md`, `log.md`, tasks, conversations, findings, remediation tasks) resolve under the single configured wiki directory, regardless of the working/data directory's location.
- **SC-005**: 100% of internal sub-path overrides declared in the directory-layout configuration file are honored at runtime without requiring any additional CLI switches beyond the two root options.

## Assumptions

- The working/data directory and the wiki directory each default to a fixed-name subfolder of the process's current working directory, as siblings of each other — neither nests inside the other by default. Only the Agent Directory nests inside the working/data directory by default. This preserves today's ADR-009 rationale of keeping the git-tracked wiki independent from harness-internal runtime state, while still requiring zero configuration for a first run.
- The operator can still point the wiki directory anywhere, including inside the working/data directory, by explicitly overriding it — the sibling relationship is only the default, not an enforced constraint.
- Agent worker executable resolution (today three separate switches, one per agent type) is no longer independently configurable via the CLI; workers are always auto-resolved relative to the hub's own binaries, consistent with reducing the CLI surface to the two options described above.
- This is a breaking change to the existing 16-switch/environment-variable configuration surface. Since the hub is pre-1.0 with no external consumers depending on the old surface, removed options are simply no longer recognized by the CLI parser (a standard "unrecognized option" failure) — no dedicated legacy-option detection, deprecation aliases, or migration shims are built for this feature.
- The three existing per-agent instruction directories (ingest, query, lint) are consolidated into subfolders of the single Agent Directory rather than being merged into one undifferentiated set of files.
- The directory-layout configuration file (FR-010) only relocates sub-paths within whichever working/data directory and wiki directory are already in effect; it cannot be used to set the two root directories themselves, keeping that root-vs-internal-layout boundary clear between the two CLI options and the file-based escape hatch.

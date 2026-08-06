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

- Q: Do findings and remediation-tasks belong under the runtime data directory or the wiki directory? → A: Wiki directory — they are agent output, alongside wiki content, `index.md`, `log.md`, tasks, and conversations.
- Q: Given the system is pre-1.0 with no legacy consumers, does removing a legacy CLI option need dedicated error handling (naming the removed option and its replacement)? → A: No — standard "unrecognized option" rejection from the CLI parser is sufficient; no dedicated legacy-option handling is required.
- Q: Should the wiki directory's default location be nested under the runtime data directory, or should the two default to separate, sibling locations? → A: Separate by default — the runtime data directory and the wiki directory are independent, sibling locations out of the box. Only the agent directory sits inside the runtime data directory by default.
- Q: How many directory options should the CLI expose, and how are values resolved? → A: Exactly three — runtime data folder, agent folder, and llm-wiki folder. Each resolves by the precedence CLI > environment variable > configuration file. The configuration file (`appsettings.json`) is where the defaults live; no code-level fallback exists. A valid `appsettings.json` MUST be versioned in git, and the hub MUST fail if it is missing or empty. Any further internal sub-path customization also belongs in that file only — never as additional CLI switches.
- Q: On a fresh run, where do the agent instruction files come from? → A: Instruction files are bound to the agent and versioned alongside the agent harness itself. Building the agent creates and refreshes the default agent directory; every agent build updates it. When the hub is pointed at a different agent folder, a supported build-time mechanism (build script, MSBuild task or property) MUST let the operator direct the agent build output into that folder. The hub MUST fail if the agent directory is empty.
- Q: What happens to data already on disk in the old layout (`data/agents`, `data/state`, `data/conversations`, `data/findings`, `wiki/`, `tasks/`, `remediation-tasks/`)? → A: No migration — the hub uses the new defaults and neither detects nor moves old folders; the operator relocates them manually or points the three options at them.
- Q: What should the default directory names be? → A: `.grimoire/` for the runtime data folder and `llm-wiki/` for the wiki folder, both as siblings under the process's current working directory; the agent folder defaults to a subfolder of `.grimoire/`.
- Q: Where do the eval recordings currently under `data/evals/recordings/` belong, given they are git-tracked test fixtures rather than runtime state or agent results? → A: Into a fixture folder inside the test project, alongside the existing eval fixtures. The eval runner resolves them from a hardcoded location matching the test project's expectations, so the recordings path is no longer a command-line switch.
- Q: The eval runner also hardcodes the agent instruction paths this feature relocates — where should it read instructions from? → A: From the agent project sources, repo-anchored and independent of build output, so eval runs do not depend on the runtime agent directory or on a prior agent build.
- Q: Where does the secrets/`.env` file belong — inside the runtime data folder, or somewhere else? → A: At the project root, next to the example file that is already there. That is where the operator copies or creates it, so the secrets file sits outside all three configurable folders and is read from the project root by both the hub and the eval runner.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the hub with no command-line configuration (Priority: P1)

An operator runs a hub command without passing any command-line flags or setting any environment variables. The values shipped in the versioned `appsettings.json` supply every directory the hub needs, and the command works immediately — without the operator having to understand or set any of today's sixteen separate path options.

**Why this priority**: This is the core complaint driving the feature — configuration is currently mandatory-feeling and confusing even though most of it never needs to change. A working no-flags path is the minimum viable fix and unblocks every other scenario.

**Independent Test**: Can be fully tested by invoking any hub command with no flags and no environment variables set, against a checkout containing the versioned `appsettings.json` and an agent directory produced by an agent build, and confirming the command completes successfully using only the configured default locations.

**Acceptance Scenarios**:

1. **Given** the versioned `appsettings.json` is present and the agent directory has been populated by an agent build, **When** an operator runs any hub command with no flags and no environment variables, **Then** the command completes successfully using the default locations from the configuration file.
2. **Given** no flags and no environment variables are provided, **When** an operator runs a hub command, **Then** the runtime data directory and the wiki directory are created automatically if they do not already exist.

---

### User Story 2 - Point the wiki at a chosen folder (Priority: P2)

An operator wants the agent-maintained results — wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks — to live in a specific folder of their choosing (for example, a separately git-tracked repository), independent of where the hub's runtime state and agents live. They set exactly one option to point to that folder; everything else keeps using defaults.

**Why this priority**: Separating agent output from internal runtime state is the most common real need after simply running the tool (e.g. to version-control or share the wiki independently), and it must not force the operator to configure anything beyond this one path.

**Independent Test**: Can be fully tested by setting only the wiki-folder option, running a hub command that produces agent results, and confirming wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks all land under that path while runtime state and the agent directory keep their defaults.

**Acceptance Scenarios**:

1. **Given** only the wiki-folder option is set to a custom path, **When** an operation that produces agent results runs, **Then** wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks are all written under the configured path.
2. **Given** only the wiki-folder option is set to a custom path, **When** an operation that produces agent results runs, **Then** the runtime data directory and the agent directory continue to use their configured defaults, unaffected by the wiki-folder override.

---

### User Story 3 - Relocate the runtime data folder (Priority: P3)

An operator running multiple isolated hub environments (for example, per project or per branch) wants all harness runtime state to live under a different root than the default. They set exactly one option to point to that root; every runtime-relevant item is found beneath it automatically.

**Why this priority**: Full relocation of runtime state is needed for isolation between environments, but it is a less frequent need than either running with defaults or pointing the wiki somewhere specific.

**Independent Test**: Can be fully tested by setting only the runtime-data-folder option, running hub commands that create runtime state (raw intake, state database, write-locks), and confirming every one of those items is created under the configured path with no separate options needed.

**Acceptance Scenarios**:

1. **Given** only the runtime-data-folder option is set to a custom path, **When** hub commands run and produce runtime state, **Then** raw intake, the operational state database, and write-locks are all located under the configured path, while the secrets file continues to be read from the project root.
2. **Given** only the runtime-data-folder option is set to a custom path and the wiki-folder option is left at its default, **When** an operation that produces agent results runs, **Then** the wiki output is created at its own configured default location — a sibling of the default runtime data directory — not nested inside the configured custom path.

---

### User Story 4 - Use a custom agent folder fed by the agent build (Priority: P4)

An operator wants the agents' instruction files and runtime data in a folder of their choosing rather than the default beneath the runtime data directory. They point the agent-folder option at it, and use the supported build-time mechanism to direct the agent build's output into that same folder so the instruction files are present and stay current across rebuilds.

**Why this priority**: Relocating agents is the least common of the three directory needs, and it only becomes useful once the build-output redirect exists — so it is the lowest-priority directory story. It is still required, because instruction files are bound to the agent build rather than authored by the hub.

**Independent Test**: Can be fully tested by directing an agent build's output at a custom folder, pointing the agent-folder option at that same folder, running a hub command, and confirming the agents run from the instruction files in that folder.

**Acceptance Scenarios**:

1. **Given** an agent build has been directed to output into a custom folder and the agent-folder option points at that folder, **When** a hub command runs an agent, **Then** the agent operates under the instruction files in that folder.
2. **Given** the agent-folder option points at a folder that is missing or contains no agent runtime, **When** a hub command runs, **Then** the hub fails with an error identifying the empty or missing agent directory.
3. **Given** an agent has been rebuilt after its instruction sources changed, **When** the build completes, **Then** the target agent directory holds the updated instruction files.

---

### User Story 5 - Adjust defaults and internal layout in the configuration file (Priority: P5)

An operator with unusual infrastructure needs (for example, putting the state database on different storage than raw intake) changes a default root or relocates a specific internal sub-path. They edit the versioned `appsettings.json`; the three command-line options keep working unchanged for everyone else, and the CLI never grows extra switches.

**Why this priority**: This is a power-user escape hatch, not required for the core simplification goal — but it preserves the layout flexibility today's 16-switch surface offers, without reintroducing CLI complexity.

**Independent Test**: Can be fully tested by changing one internal sub-path in the configuration file, running a hub command, and confirming that artifact resolves to the changed path while every other artifact stays where it was.

**Acceptance Scenarios**:

1. **Given** the configuration file declares a different value for one internal sub-path, **When** a hub command runs, **Then** that artifact resolves to the configured path while all other artifacts keep their configured defaults.
2. **Given** the configuration file sets a root directory and a command-line flag sets the same root, **When** a hub command runs, **Then** the command-line value wins, and an environment variable for that root wins over the configuration file but loses to the command-line flag.

---

### Edge Cases

- What happens when `appsettings.json` is missing, empty, or does not supply the three root directories? The hub MUST fail with an error rather than falling back to code-level defaults — the versioned configuration file is the single source of default paths.
- What happens when the agent directory is missing or contains no agent runtime? The hub MUST fail with an error identifying the agent directory, rather than running agents without instructions or without their executables. A missing worker executable specifically MUST name the build command that produces it.
- What happens when an operator starts the hub without having built the solution? The hub MUST fail at startup naming what is missing; it MUST NOT build the agent itself, at startup or at dispatch.
- What happens when the configured runtime data directory or wiki directory does not yet exist? The hub MUST create it automatically rather than failing. (This does not apply to the agent directory, which is produced by the agent build.)
- What happens when the wiki folder is explicitly configured to be the same as, or nested inside, the runtime data directory? The hub MUST accept it as a valid explicit choice — even though it departs from the default sibling relationship — without treating it as an error.
- What happens to an operator's hand edits to instruction files inside an agent directory? The next agent build overwrites them, because instruction files are versioned with the agent. Durable instruction changes are made in the agent's own sources, not in the output directory.
- What happens when data from the old layout (`data/agents`, `data/state`, `data/conversations`, `data/findings`, `wiki/`, `tasks/`, `remediation-tasks/`) is present on disk? The hub neither detects nor migrates it; it simply uses the configured locations. Relocating old data is a manual operator step.
- What happens when an operator supplies a command-line option that no longer exists (e.g. a former per-artifact path switch, or the eval runner's recordings-root switch)? The command fails with the CLI parser's standard "unrecognized option" error; no dedicated legacy-option detection or replacement guidance is built.
- What happens to eval runs when an operator has pointed the three directory options at unusual locations? Nothing — eval recordings and agent instructions resolve from repo-anchored locations, so eval results do not vary with hub configuration.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The hub MUST run successfully with no command-line flags and no environment variables set, resolving every directory from the versioned configuration file.
- **FR-002**: The hub MUST expose exactly three directory options on the command line — runtime data folder, agent folder, and wiki folder — and no other path switches.
- **FR-003**: The hub MUST allow each of the three directory options to be set independently; setting one MUST NOT require setting another.
- **FR-004**: The hub MUST resolve each of the three directory options by the precedence command-line flag > environment variable > configuration file, evaluated per option rather than as an all-or-nothing group.
- **FR-005**: The hub MUST treat the configuration file as the sole source of default paths, with no code-level fallback, and MUST fail with an error when it is missing, empty, or does not supply the three root directories. A valid configuration file MUST be versioned in the repository.
- **FR-006**: The hub MUST locate all harness runtime state under the runtime data folder: raw intake, the operational state database, and write-locks.
- **FR-007**: The hub MUST locate all agent-produced results under the wiki folder: wiki content, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks.
- **FR-008**: The hub MUST treat the agent folder as a single directory holding both instruction files and runtime data for every agent type (ingest, query, lint), organized as per-agent-type subfolders rather than as separately configurable directories.
- **FR-009**: The shipped configuration file MUST default the runtime data folder to `.grimoire/` and the wiki folder to `llm-wiki/`, both as siblings under the process's current working directory, and MUST default the agent folder to a subfolder of the runtime data folder.
- **FR-010**: The hub MUST create the runtime data folder and the wiki folder automatically when they do not exist.
- **FR-011**: The agent build MUST create and refresh the default agent directory, writing each agent's instruction files there on every build, so that instruction files are versioned and distributed with the agent rather than authored by the hub at runtime.
- **FR-012**: The build MUST provide a supported mechanism (build script, build-tool task, or build property) for directing agent build output into an operator-chosen agent folder, so a custom agent folder can be kept current across rebuilds.
- **FR-013**: The hub MUST fail with an error identifying the agent directory when that directory is missing or contains no agent runtime, rather than attempting to run agents without their instructions or their executables.
- **FR-014**: The hub MUST NOT detect or migrate data left in the previous directory layout; relocating such data is a manual operator step.
- **FR-015**: The hub MUST confine any further customization of internal sub-paths beneath the three root folders to the configuration file, never exposing them as additional command-line switches.
- **FR-016**: Eval recordings MUST live in a fixture folder inside the test project rather than under the runtime data folder, and the eval runner MUST resolve them from a hardcoded location matching the test project's expectations — removing the recordings-root command-line switch.
- **FR-017**: The eval runner MUST resolve agent instruction files from the agent project sources, repo-anchored, so eval runs depend on neither the configured runtime agent directory nor a prior agent build.
- **FR-018**: Eval fixture and instruction resolution MUST be independent of all three configured directory options, so eval runs produce the same result regardless of how an operator has configured the hub.
- **FR-019**: The hub and the eval runner MUST read the secrets file from the project root, alongside the example file the operator copies it from, rather than from inside any of the three configurable folders. The secrets file location MUST NOT be affected by any of the three directory options.
- **FR-020**: The hub MUST launch agent workers exclusively from pre-built artifacts in the agent folder, and MUST NOT invoke a build, restore, or compilation at any point — neither at startup nor at dispatch. A missing worker artifact MUST fail at startup with an error naming the artifact and the build command that produces it.
- **FR-021**: The agent build MUST deliver each agent's complete runtime — its worker executable, the dependency assemblies that executable needs, and its instruction files — into that agent's subfolder of the agent folder, so the subfolder is self-contained and directly executable without reference to any other location.

### Key Entities

- **Runtime Data Folder**: The configurable root holding all harness runtime state — raw intake, the operational state database, and write-locks — plus the Agent Folder at its default location. Defaults to `.grimoire/` under the current working directory.
- **Secrets File**: The operator-supplied `.env` file at the project root, created by copying the example file that already sits there. Read from the project root by both the hub and the eval runner, outside all three configurable folders and unaffected by any directory option.
- **Wiki Folder**: The independently configurable root holding all agent-produced results — wiki content pages, `index.md`, `log.md`, tasks, conversations, findings, and remediation tasks. Defaults to `llm-wiki/` under the current working directory, as a sibling of the Runtime Data Folder, and may be pointed anywhere (e.g. a separately git-tracked location).
- **Agent Folder**: The independently configurable directory holding the complete runtime for every agent type (ingest, query, lint) in per-type subfolders — each subfolder holding that agent's worker executable, the dependency assemblies it needs, and its instruction files, so it is self-contained and directly executable. Defaults to a subfolder of the Runtime Data Folder. Its contents are produced and refreshed by the agent build, not by the hub; the hub reads and launches from it, never writes to it, and refuses to run when it is empty.
- **Configuration File**: The versioned `appsettings.json` that supplies the default values for all three root folders and any internal sub-path customization. Mandatory — the hub fails without it — and never mirrored as extra command-line switches.
- **Eval Fixtures**: Git-tracked eval recordings, held in a fixture folder inside the test project alongside the existing eval fixtures. Resolved by the eval runner from a hardcoded location, outside all three configurable root folders, and therefore unaffected by how an operator configures the hub.

## Success Criteria *(mandatory)*

<!--
  All criteria below are deterministic harness guarantees (Constitution Principle II).
  This feature changes configuration resolution and directory layout only; it introduces
  no agent-judgment behavior, so no evaluation-threshold criteria apply.
-->

### Measurable Outcomes

- **SC-001**: 100% of hub commands complete successfully when invoked with no command-line flags and no environment variables set, against the versioned configuration file and a built agent directory.
- **SC-002**: The command-line directory surface consists of exactly 3 options (runtime data folder, agent folder, wiki folder), down from the current 16 independent path switches.
- **SC-003**: 100% of harness runtime artifacts (raw intake, state database, write-locks) resolve under the configured runtime data folder.
- **SC-004**: 100% of agent-result artifacts (wiki content, `index.md`, `log.md`, tasks, conversations, findings, remediation tasks) resolve under the configured wiki folder, regardless of where the runtime data folder points.
- **SC-005**: For each of the three options, 100% of resolutions honor the precedence command-line flag > environment variable > configuration file.
- **SC-006**: 100% of hub starts with a missing, empty, or incomplete configuration file fail with an error naming the configuration file, with no silent fallback to code-level defaults.
- **SC-007**: 100% of hub starts against a missing agent directory, or one holding no agent runtime, fail with an error naming that directory.
- **SC-008**: 100% of agent builds leave the target agent directory holding that agent's current runtime — worker executable, dependency assemblies, and instruction files — with no artifact left over from a previous build.
- **SC-009**: 100% of eval runs resolve their recordings from the hardcoded test-fixture location, with no recordings path accepted on the command line.
- **SC-010**: 100% of eval runs produce identical results regardless of how the three directory options are configured, resolving instructions from the agent project sources without requiring an agent build.
- **SC-011**: 100% of secrets lookups by the hub and the eval runner resolve to the project-root secrets file, regardless of how the three directory options are configured.
- **SC-012**: 100% of agent launches start a pre-built worker artifact from the configured agent folder, and 0% of hub code paths are capable of invoking a build tool.

## Assumptions

- The three root folders each default to a fixed-name location under the process's current working directory — `.grimoire/` and `llm-wiki/` as siblings, with the agent folder inside `.grimoire/`. Keeping the wiki out of the runtime data folder preserves ADR-009's rationale of tracking the wiki in git independently of harness-internal state, while still requiring no command-line configuration for a first run.
- "No configuration needed" means no command-line flags and no environment variables. The versioned configuration file is always present in the repository and is not something the operator has to author.
- The operator may still point the wiki folder inside the runtime data folder by setting it explicitly — the sibling relationship is the default, not an enforced constraint.
- Agent worker executable resolution (today three separate switches, one per agent type) is no longer configurable; workers are always resolved inside the configured agent folder, in the same per-agent-type subfolder as that agent's instruction files. An earlier draft assumed workers would resolve relative to the hub's own binaries; planning established that this is not implementable — the hub holds no assembly reference to any agent (a deliberate dispatch-only boundary), so no build ever places the worker executables in the hub's own output directory.
- Instruction files are versioned with the agent harness and regenerated by every agent build. Per-project instruction customization therefore happens in the agent's sources, not by editing the build output; the hub never authors or seeds instruction content itself, which keeps the Principle V boundary intact.
- The same build step delivers instruction files and worker artifacts together, so "the agent folder is current" is one condition rather than two that can drift apart. Consequently the hub consumes build artifacts and never produces them: an operator who has not built the solution gets a named startup failure, not an implicit build.
- This is a breaking change to the existing 16-switch configuration surface. Since the hub is pre-1.0 with no external consumers, removed options simply stop being recognized by the CLI parser — no deprecation aliases, detection logic, or migration shims are built, and no on-disk data is migrated.
- The three existing per-agent instruction directories (ingest, query, lint) become subfolders of the single agent folder rather than being merged into one undifferentiated set of files.
- The eval runner is in scope for this feature because it currently hardcodes paths this change relocates. Its recordings become test fixtures and its instruction resolution becomes repo-anchored; it is deliberately *not* wired to the three configurable directories, keeping eval runs reproducible and independent of operator configuration.
- The secrets file sits at the project root because that is where its example file already lives and where an operator naturally copies or creates it. Keeping it out of the three configurable folders means relocating runtime data, agents, or the wiki never separates an operator from their credentials, and the hub and eval runner always agree on where secrets are.
- With eval recordings relocated to the test project and secrets at the project root, the runtime data folder holds only genuine runtime state — raw intake, the state database, and write-locks — plus the default agent folder.

# Feature Specification: Independent Memory Directory Root

**Feature Branch**: `023-memory-directory-root`

**Created**: 2026-08-11

**Status**: Draft

**Input**: User description: "Introduce a single 'memory' directory as a fourth independent root in the Hub's path configuration surface, alongside the existing DataDir, WikiDir, and AgentDir (ADR-022). Today, agent-produced bookkeeping — TasksDir, ConversationsDir, FindingsDir (lint results), and RemediationTasksDir — are four separate sub-paths anchored under WikiDir. This mixes wiki content (the actual maintained knowledge base under WikiDir) with agent process bookkeeping (conversations, tasks, findings, remediation) in the same directory tree, and there is no single place an operator can point at, back up, retain, or exclude independently of the wiki content itself. Goal: consolidate all four of these into one new 'memory' directory (default name 'memory') that is a peer of DataDir/WikiDir/AgentDir — not nested inside any of them."

## Clarifications

### Session 2026-08-11

- Q: Which files count as the "other files" whose bookkeeping-subdir references must be removed/updated, alongside the three agents' system-prompt.md? → A: Agent instruction files only (system-prompt.md for Ingest/Query/Lint, plus default-user-prompt.md/policy.json if they mention these folders). ADR narrative text describing the current WikiDir-anchored layout is updated separately via the ADR-amendment step already flagged as a planning dependency for this feature, not as a spec functional requirement here.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Back up or exclude agent bookkeeping independently of wiki content (Priority: P1)

An operator wants to back up, retain, or exclude the agent's process bookkeeping — tasks, conversations, lint findings, and remediation tasks — as a unit, separately from the wiki content itself. Today that bookkeeping is scattered across four sub-paths nested inside the wiki directory, so there is no single location the operator can point a backup job or an exclusion rule at without also sweeping up (or missing) the actual wiki pages. They set one option to relocate all agent bookkeeping to a folder of their choosing, independent of where the wiki content lives.

**Why this priority**: This is the core problem motivating the feature — wiki content and agent process bookkeeping are operationally different things (one is the maintained knowledge base, the other is harness-produced record-keeping) but currently share a directory tree, making it impossible to treat them differently for backup, retention, or exclusion. Solving this is the entire value of the feature.

**Independent Test**: Can be fully tested by setting only the memory-folder option to a custom path, running hub operations that produce tasks, conversations, findings, and remediation task records, and confirming all four land under that configured path while wiki content stays under its own separately configured location.

**Acceptance Scenarios**:

1. **Given** only the memory-folder option is set to a custom path, **When** hub operations produce tasks, conversations, findings, or remediation task records, **Then** all four are written under the configured memory folder.
2. **Given** only the memory-folder option is set to a custom path, **When** an ingest operation writes wiki content, **Then** that content is written under the wiki folder's own configured location, unaffected by the memory-folder override.

---

### User Story 2 - Relocate the wiki or the memory folder without disturbing the other (Priority: P2)

An operator relocates the wiki content folder (for example, to point it at a different git-tracked location) and expects agent bookkeeping to stay exactly where it was. Conversely, an operator relocates the memory folder (for example, to move bookkeeping onto different storage) and expects the wiki content to stay exactly where it was. Neither directory shares a parent with, or nests inside, any of the others.

**Why this priority**: The independence contract is what makes the separation in User Story 1 trustworthy — if relocating one root silently dragged another along, the operator could not reason about where their data lives. This is a direct extension of the same independence rule already established for the runtime data, wiki, and agent roots.

**Independent Test**: Can be fully tested by relocating the wiki folder while leaving the memory folder at its default (and vice versa), running hub operations that touch both, and confirming each root's contents resolve only under its own configured location.

**Acceptance Scenarios**:

1. **Given** the wiki folder is relocated to a custom path and the memory folder is left at its default, **When** hub operations run, **Then** wiki content resolves under the relocated wiki folder and tasks/conversations/findings/remediation tasks continue resolving under the default memory folder location.
2. **Given** the memory folder is relocated to a custom path and the wiki folder is left at its default, **When** hub operations run, **Then** tasks/conversations/findings/remediation tasks resolve under the relocated memory folder and wiki content continues resolving under the default wiki folder location.
3. **Given** the runtime data folder or the agent folder is relocated, **When** hub operations run, **Then** the memory folder's resolved location is unaffected.

---

### User Story 3 - Run with no additional configuration (Priority: P3)

An operator who has never heard of the new memory folder runs hub commands exactly as before. The versioned configuration file supplies a sensible default location for the memory folder as a sibling of the existing default locations, agent bookkeeping is created there automatically, and nothing about the operator's existing workflow changes.

**Why this priority**: The feature must not impose new mandatory configuration on operators who are happy with defaults; it only needs to work correctly out of the box. This is a baseline expectation rather than the feature's distinguishing value, hence the lower priority.

**Independent Test**: Can be fully tested by running a hub command with no memory-folder option set, against the versioned configuration file, and confirming the memory folder and its contents are created automatically at the shipped default location.

**Acceptance Scenarios**:

1. **Given** no memory-folder option is set, **When** a hub command runs and produces a task, conversation, finding, or remediation task record, **Then** it is written under the shipped default memory folder location, created automatically if it does not already exist.
2. **Given** no memory-folder option is set, **When** an operator inspects the hub's startup reporting, **Then** the memory folder's resolved location is listed alongside the runtime data, wiki, and agent folders.

---

### Edge Cases

- What happens when the versioned configuration file does not supply a value for the memory folder? The hub MUST fail at startup with an error naming the missing configuration key, the same treatment as the three existing roots.
- What happens when the configured memory folder does not yet exist on disk? The hub MUST create it automatically, the same treatment as the runtime data and wiki folders.
- What happens when the memory folder is explicitly configured to be the same as, or nested inside, the wiki, runtime data, or agent folder? The hub MUST accept it as a valid explicit choice, even though it departs from the default sibling relationship, without treating it as an error.
- What happens to tasks, conversations, findings, and remediation task records that already exist on disk under the wiki folder from before this feature? They are not moved or migrated automatically; the hub simply resolves new activity against the newly configured memory folder location. Relocating pre-existing records is a manual operator step.
- What happens to the internal sub-path names and structure of tasks, conversations, findings, and remediation tasks (file naming, per-record layout)? They are unchanged — only the parent root they anchor under moves, not their internal shape.
- What happens to an operator's existing environment-variable overrides once the configuration file is restructured and their key names change? An unrecognized configuration key is normally ignored rather than rejected, so such an override would silently stop taking effect and the location would quietly resolve to its default — for a feature whose purpose is letting an operator place bookkeeping deliberately, silently ignoring a placement instruction is the worst available failure. The hub MUST therefore detect superseded keys and fail at startup naming each one and its replacement (FR-014). This is a deliberate departure from the treatment given to removed command-line switches, which need no detection because an unrecognized switch already fails loudly on its own.
- What happens to the Ingest, Query, and Lint agent instruction files, which currently tell the agent to skip `tasks/`, `conversations/`, `findings/`, and `remediation-tasks/` as reserved folders when browsing the wiki tree, list them in the wiki's directory-tree diagram, and (Ingest only) cite `[[tasks/<task_id>.md]]` as a wiki-relative link when logging a run? Once these folders anchor under the memory folder instead of the wiki folder, they are no longer reachable within the wiki tree at all, so this guidance is stale and must be updated to match: removed where it describes folders the agent would otherwise encounter while browsing the wiki, and corrected where it cites one of these folders as if it were a wiki-relative path.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The hub MUST expose a memory folder as a fourth root in its directory configuration, independent of and at the same configuration tier as the existing runtime data, wiki, and agent folders — anchored at the process working directory, not nested beneath any other root.
- **FR-002**: The hub MUST anchor tasks, conversations, findings, and remediation task records under the memory folder instead of the wiki folder.
- **FR-003**: Relocating the wiki, runtime data, or agent folder MUST NOT change the memory folder's resolved location.
- **FR-004**: Relocating the memory folder MUST NOT change the resolved location of the wiki, runtime data, or agent folder.
- **FR-005**: The hub MUST resolve the memory folder using the same per-option precedence as the three existing roots: an explicit override wins over an environment-variable override, which wins over the versioned configuration file's default.
- **FR-006**: The hub MUST treat the versioned configuration file as the sole source of the memory folder's default value, with no code-level fallback, and MUST fail with an error naming the missing key when the configuration file does not supply it.
- **FR-007**: The hub MUST create the memory folder automatically when it does not already exist, the same treatment given to the runtime data and wiki folders.
- **FR-008**: The memory folder's resolved location MUST appear in the hub's startup path-resolution report alongside the runtime data, wiki, and agent folders.
- **FR-009**: The shipped configuration file MUST default the memory folder to a folder named `memory`, as a sibling of the default runtime data, wiki, and agent folder locations under the process's current working directory.
- **FR-010**: The internal sub-path structure of tasks, conversations, findings, and remediation task records MUST remain unchanged; only their anchoring root moves from the wiki folder to the memory folder.
- **FR-011**: The hub MUST NOT automatically detect or migrate existing on-disk tasks, conversations, findings, or remediation task records from their previous location under the wiki folder to the memory folder; relocating pre-existing records is a manual operator step.
- **FR-012**: The Ingest, Query, and Lint agents' instruction files (`system-prompt.md`, and any other instruction document such as `default-user-prompt.md` or `policy.json` that references these folders) MUST NOT describe tasks, conversations, findings, or remediation tasks as folders reachable within the wiki tree — including wiki-browsing guidance, directory-tree diagrams, and wiki-relative link citations naming these folders — since they no longer live there.
- **FR-013**: The versioned configuration file MUST express each runtime location's anchoring root through its own structure: every location that resolves against a root MUST be grouped with that root, and every location anchored at the process working directory MUST sit outside all such groups. An operator MUST be able to read the effective directory layout — which locations are roots, and which root each remaining location resolves against — from the configuration file alone, without consulting code or comments.
- **FR-014**: When the configuration file's structure changes such that a previously valid configuration key is no longer recognized, the hub MUST fail at startup naming each unrecognized key it detects and its replacement, rather than silently ignoring it and resolving the location to a default.

### Key Entities

- **Memory Folder**: A new, independently configurable root holding all agent process bookkeeping — tasks, conversations, findings, and remediation task records. Defaults to `memory/` under the current working directory, as a sibling of the runtime data, wiki, and agent folders. Shares no parent with, and is never nested inside, any of the other three roots unless an operator explicitly configures it that way.
- **Wiki Folder** *(redefined)*: The independently configurable root holding only wiki content — the actual maintained knowledge base — no longer holding agent process bookkeeping, which moves to the Memory Folder.

## Success Criteria *(mandatory)*

<!--
  All criteria below are deterministic harness guarantees (Constitution Principle II).
  This feature changes configuration resolution and directory anchoring only; it
  introduces no agent-judgment behavior, so no evaluation-threshold criteria apply.
-->

### Measurable Outcomes

- **SC-001**: 100% of tasks, conversations, findings, and remediation task records resolve under the configured memory folder rather than the wiki folder.
- **SC-002**: 100% of relocations of the wiki, runtime data, or agent folder leave the memory folder's resolved location unchanged, and 100% of relocations of the memory folder leave the other three folders' resolved locations unchanged.
- **SC-003**: 100% of memory-folder resolutions honor the precedence override > environment variable > configuration file.
- **SC-004**: 100% of hub starts against a configuration file missing the memory folder's default value fail with an error naming that configuration key.
- **SC-005**: 100% of hub starts create the memory folder automatically when it is absent from disk.
- **SC-006**: The memory folder's resolved location appears in 100% of startup path-resolution reports.
- **SC-007**: 0% of pre-existing on-disk tasks, conversations, findings, or remediation task records are moved automatically by the hub.
- **SC-008**: 0% of the Ingest, Query, and Lint agents' instruction files contain a reference to `tasks/`, `conversations/`, `findings/`, or `remediation-tasks/` as a folder reachable within the wiki tree.
- **SC-009**: 100% of configured sub-paths resolve against the root they are grouped with in the configuration file, and 0% resolve against any other root.
- **SC-010**: 100% of hub starts that supply a superseded configuration key fail with an error naming that key and its replacement; 0% silently fall back to a default.

## Assumptions

- The memory folder's default name is `memory`, matching the user-specified default, and it sits at the same anchoring tier (process working directory) as the runtime data, wiki, and agent folders — not as a sub-path of any of them.
- This feature is additive to the directory configuration surface established by ADR-022: it introduces a fourth root alongside the existing three (runtime data, wiki, agent) rather than altering their behavior.
- **Known planning dependency**: ADR-022 Rule R1 structurally caps the command-line directory-switch surface at exactly three entries and fails the build if a fourth is added. Giving the memory folder its own command-line override (at the same tier as the other three roots, per the input to this spec) requires resolving this conflict — either by amending ADR-022 or by drafting a new ADR that explicitly supersedes Rule R1 and updates its structural probe — before implementation tasks are generated. This is a required ADR-review step under Constitution Principle III, not a silent exception to the existing rule.
- No migration of existing on-disk data is in scope. Tasks, conversations, findings, and remediation task records already present under the wiki folder from before this feature are left in place; an operator who wants them under the new memory folder relocates them manually, consistent with the precedent set for the runtime data, wiki, and agent roots.
- The internal sub-path structure within tasks, conversations, findings, and remediation tasks (file naming, per-record layout) is out of scope for this feature — only the parent root each anchors under changes.
- Changing anything about the runtime data, wiki, or agent folders themselves (beyond wiki content narrowing to exclude bookkeeping) is out of scope — **except** as required by FR-013: because the configuration file's structure must express anchoring for every location, the three existing roots' configuration keys move alongside the new one. Their resolved locations, their defaults, their anchoring behavior and their command-line switch names are all unchanged; only the keys naming them in the configuration file and in environment-variable form change.
- **Provenance of FR-013/FR-014 (author directive, 2026-08-11)**: these two requirements were added after the clarification session, on the author's direction that the configuration file should reflect the real directory structure rather than presenting a flat list. FR-014 follows from FR-013 rather than from the directive: restructuring renames keys, and renamed configuration keys — unlike removed command-line switches — fail silently unless the hub is made to detect them. The pairing is deliberate; FR-013 without FR-014 would trade a readability gain for a silent-misconfiguration risk.
- Updating stale bookkeeping-folder references is scoped to agent instruction files only (the `Instructions/` documents for Ingest, Query, and Lint that are actually loaded into an agent's working context — Constitution Principle V). Repository documentation that separately describes today's WikiDir-anchored layout (e.g. ADR-022's own root/sub-path table) is corrected through the ADR-amendment step already flagged as a planning dependency, not through this spec's functional requirements.

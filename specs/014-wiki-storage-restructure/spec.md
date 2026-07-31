# Feature Specification: Wiki Storage Layout & Shared Log/Catalog Format

**Feature Branch**: `014-wiki-storage-restructure`

**Created**: 2026-07-30

**Status**: Draft

**Input**: User description: "Restructure the wiki content-root storage layout so it mirrors the reference 'llm-wiki' folder layout, and change how agents append to the wiki's activity log so it stays human-readable across multiple contributing agents."

## Clarifications

### Session 2026-07-30

- Q: Should adopting the new layout on an existing installation include an automatic migration of content from the old locations (wrapper folder, old tasks/conversations nesting) to the new ones? → A: No migration is needed or in scope — Grimoire has no production deployment yet; the wiki content root currently starts empty. There is no "old" content to preserve; this feature only needs to change internal directory structure (and any test fixtures), not migrate real content. The final wiki content-root folder itself does not need to change.
- Q: Should `index.md`'s catalog entry format also be reviewed and, if needed, aligned with the reference wiki, the same way `log.md`'s entry format is being standardized? → A: Yes — `index.md` catalog entries must be reviewed against the reference wiki's convention and aligned to it as part of this feature.
- Q: What exact date format must the `[DATE] TYPE | SUMMARY` heading use in `log.md`? → A: ISO calendar date only, `YYYY-MM-DD` — matches the reference wiki's convention.
- Q: What language/wording should the `index.md` source-status marker use? → A: The wiki's own configured content language — German by default, or whichever language the operator sets in the agent system-prompt files. CLAUDE.md's English-only policy governs this repository's own code and documentation (the hub, frontend, and agent harness), not agent-generated wiki content, which is data produced under separate, operator-configurable instructions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Articles live directly under the wiki content root (Priority: P1)

As an operator or contributor browsing the wiki content root, I want articles to live directly under topical subfolders (e.g. `concepts/`, `tech/`, `tools/`, `sources/`) at the same level as `index.md` and `log.md`, with no intermediate wrapper folder, so the content root looks and navigates exactly like the reference wiki it is modeled on.

**Why this priority**: This is the structural core of the feature — every other change (tasks, conversations, log format) sits on top of the content root's shape. Without it, the wiki does not match its reference layout at all.

**Independent Test**: Can be fully tested by pointing an agent at a fresh wiki content root, having it create an article, and confirming the resulting file path has no wrapper segment between the content root and the topical subfolder — delivers a browsable, reference-matching content root on its own.

**Acceptance Scenarios**:

1. **Given** a freshly initialized wiki content root, **When** an agent creates a new article in a topical category, **Then** the article file is stored directly under `<content-root>/<category>/<article>.md`, with no wrapper folder in between.
2. **Given** the new content root layout, **When** anyone lists the content root's top-level entries, **Then** they see `index.md`, `log.md`, and only topical category folders — no wrapper folder.

---

### User Story 2 - Tasks and conversations sit alongside the wiki, not inside it (Priority: P1)

As an operator, I want the tasks directory and the conversations directory to each sit as siblings of the wiki content root — not nested inside it, and not nested inside the internal runtime data directory — so the wiki content root stays a clean, self-contained artifact (for example, one that can be committed to its own git repository) that isn't mixed with task bookkeeping or conversation transcripts.

**Why this priority**: Equally foundational to the reference layout as User Story 1, and independently valuable — an operator can verify this by inspecting the workspace tree without needing to look at log format details.

**Independent Test**: Can be fully tested by triggering a task and a conversation, then confirming both are written under directories that sit next to the wiki content root at the same directory level, and that neither location is nested under the other.

**Acceptance Scenarios**:

1. **Given** a running system, **When** a new task is created, **Then** its artifact is stored under a `tasks` directory that is a sibling of the wiki content root (not inside it).
2. **Given** a running system, **When** a new conversation is recorded, **Then** it is stored under a `conversations` directory that is a sibling of the wiki content root (not inside the internal runtime data directory, not inside the wiki content root).
3. **Given** the new layout, **When** the internal runtime data directory (raw intake storage, operational state, agent instructions/policy) is inspected, **Then** its contents are unchanged by this feature.

---

### User Story 3 - Every agent writes log entries in the same readable format (Priority: P2)

As anyone reading `log.md` — a human operator or another agent — I want every entry, regardless of which agent type appended it, to start with a heading line of the form `[DATE] TYPE | SUMMARY` followed by a short paragraph describing what was actually done, so the log stays scannable and greppable even as multiple agent types (ingest, query, and future agent types such as a lint agent) contribute to it over time.

**Why this priority**: Valuable and independently testable, but depends on nothing from User Stories 1–2 to demonstrate — it can be validated purely by inspecting appended log entries.

**Independent Test**: Can be fully tested by having two different agent types each append a log entry and confirming both entries follow the identical heading-plus-paragraph structure, with the heading independently locatable by pattern search.

**Acceptance Scenarios**:

1. **Given** an agent completes an action that warrants a log entry, **When** it appends to `log.md`, **Then** the appended entry starts with a heading formatted as `[DATE] TYPE | SUMMARY` and is immediately followed by a short prose paragraph describing the activity.
2. **Given** two different agent types each append an entry, **When** `log.md` is inspected, **Then** both entries share the same heading-plus-paragraph structure — no agent type produces a differently shaped entry.
3. **Given** an agent fails or omits its own log entry for a completed action, **When** the fallback logging mechanism appends the backstop entry, **Then** the backstop entry also follows the same `[DATE] TYPE | SUMMARY` heading-plus-paragraph format.
4. **Given** a fully populated `log.md`, **When** someone searches for entries by heading pattern, **Then** every entry is found by that search — no entry lacks a heading or is otherwise unlocatable.

---

### User Story 4 - The catalog entry format matches the reference wiki, like the log (Priority: P2)

As anyone browsing `index.md`, I want every catalog entry to reference its article using the reference wiki's convention — a link to the article, a short description, and a source-status marker — the same way `log.md` entries are being standardized, so `index.md` is equally consistent and scannable regardless of which agent created or last updated the entry.

**Why this priority**: Directly analogous to User Story 3 and equally independently testable — verified purely by inspecting catalog entries, with no dependency on the layout changes in User Stories 1–2.

**Independent Test**: Can be fully tested by having an agent add a new catalog entry for a new article and confirming it follows the link-description-status format, independent of any other change in this feature.

**Acceptance Scenarios**:

1. **Given** an agent creates a new article, **When** it adds the corresponding catalog entry to `index.md`, **Then** the entry is formatted as a link to the article followed by a short description and a trailing source-status marker (a source count, or a stub indicator for a page with no sourced content yet), written in the wiki's configured content language.
2. **Given** two different agent types each add a catalog entry, **When** `index.md` is inspected, **Then** both entries share the same link-description-status format — no agent type produces a differently shaped entry.

---

### Edge Cases

- What happens when a legitimate topical category is named the same as the old wrapper folder (e.g. an article category genuinely called "pages")?
- What happens when an agent appends a heading line but omits the descriptive paragraph beneath it (empty body)?
- What happens when two log entries end up with an identical heading (same date, type, and summary) for two distinct actions?
- What happens when an article has no known sources yet — how is that reflected in its catalog entry's source-status marker?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST store wiki articles directly under the wiki content root inside topical subfolders, with no intermediate wrapper folder between the content root and the topical subfolder.
- **FR-002**: System MUST keep `index.md` and `log.md` located directly at the wiki content root, alongside the topical subfolders.
- **FR-003**: System MUST locate the tasks directory as a sibling of the wiki content root, not nested inside it.
- **FR-004**: System MUST locate the conversations directory as a sibling of the wiki content root, not nested inside the internal runtime data directory and not nested inside the wiki content root.
- **FR-005**: System MUST leave the location of internal runtime-only data (raw intake storage, operational state, agent instructions/policy files) unchanged by this feature.
- **FR-006**: System MUST NOT include any migration mechanism for a previous wiki layout — no prior installation with wiki content exists, so the system operates solely against the new layout from initialization.
- **FR-007**: System MUST require every entry appended to `log.md` to start with a heading line formatted as `[DATE] TYPE | SUMMARY`, where `DATE` is an ISO 8601 calendar date (`YYYY-MM-DD`) with no time component.
- **FR-008**: System MUST require every `log.md` heading to be immediately followed by a short prose paragraph describing the activity, regardless of which agent type produced the entry.
- **FR-009**: System MUST apply the identical heading-plus-paragraph log entry format across every agent type that writes to `log.md`, with no agent-type-specific variation in structure.
- **FR-010**: System MUST append a fallback log entry in the same heading-plus-paragraph format whenever an agent fails to append its own entry or omits a required entry for a completed action.
- **FR-011**: System MUST keep `log.md` append-only, with every entry's heading independently locatable by searching the file for the heading pattern.
- **FR-012**: System MUST format every newly added `index.md` catalog entry as a link to the article, followed by a short description and a trailing source-status marker (a source count, or a stub indicator), matching the reference wiki's catalog convention and written in the wiki's configured content language.
- **FR-013**: System MUST apply the catalog entry format from FR-012 consistently regardless of which agent type adds or updates the entry.

### Key Entities

- **Wiki Content Root**: The top-level wiki directory containing `index.md`, `log.md`, and topical article subfolders directly — no wrapper folder.
- **Article**: A single wiki page stored under a topical subfolder of the content root.
- **Tasks Directory**: Sibling directory of the content root holding task artifacts; independent from wiki article content.
- **Conversations Directory**: Sibling directory of the content root holding conversation records; independent from wiki article content and from internal runtime data.
- **Log Entry**: A single append-only record in `log.md`, consisting of a `[DATE] TYPE | SUMMARY` heading and a short descriptive paragraph.
- **Catalog Entry**: A single line in `index.md` referencing one article via a link, a short description, and a source-status marker.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of articles created after this change are stored directly under a topical subfolder of the content root, with zero wrapper-folder path segments.
- **SC-002**: 100% of tasks and conversations created after this change are stored under a directory that is a sibling of the wiki content root, with zero instances nested inside the content root or the internal runtime data directory.
- **SC-003**: 100% of `log.md` entries appended after this change — by any agent type or by the fallback mechanism — start with a correctly formatted `[DATE] TYPE | SUMMARY` heading followed by a descriptive paragraph.
- **SC-004**: 100% of `log.md` entries remain locatable by searching for the heading pattern; zero entries are missing a heading.
- **SC-005**: ≥ 90% of sampled agent-written log entry paragraphs, when reviewed against the change they describe, are judged to specifically and accurately describe what was done — not a generic restatement of the heading.
- **SC-006**: 100% of `index.md` catalog entries added after this change follow the link-description-source-status format.
- **SC-007**: ≥ 90% of sampled agent-written catalog descriptions, when reviewed against the article they summarize, are judged to specifically and accurately describe its content — not a generic restatement of the title.

## Assumptions

- The wiki content root's own folder name (e.g. `wiki`) is unchanged by this feature — only its *internal* shape (no wrapper folder) and the *location* of the tasks/conversations directories relative to it change.
- The reference wiki's demonstrated log heading level (a `##`-style heading per entry, with a single top-level title reserved for the log document itself) is the convention this feature follows, since that is the level actually used by entries in the reference wiki this feature is modeled on.
- Topical subfolder names (e.g. `concepts/`, `tech/`, `tools/`, `sources/`, `events/`, `hobbies/`, `persoenliches/`) are chosen by the agents based on content, not fixed by this specification.
- The `TYPE` label in a log heading (e.g. `ingest`, `update`, `query`, `lint-fixes`) is open-ended and chosen by the appending agent, not a fixed enum enforced by this feature.
- `log.md`'s append-only invariant means an entry, once written, is never edited or rewritten by a later run — this feature governs the format of entries appended going forward.
- Grimoire has no production deployment yet; the wiki content root currently starts empty. This feature therefore requires no migration mechanism — the only artifacts that need to change are internal directory structure and, where applicable, test fixtures, not real pre-existing content.
- The `index.md` catalog's source-status marker (a count, or a stub indicator) reflects the agent's own judgment about how many distinct sources back an article, mirroring the reference wiki's convention — it is not a new structured field tracked separately by the system.
- CLAUDE.md's English-only language policy governs this repository's own code and documentation (the hub, frontend, and agent harness). It does not govern agent-generated wiki content, which is data written under separate, operator-configurable instructions and defaults to German (matching the reference wiki), or whichever language the operator sets in the agent system-prompt files.
- This feature does not change which agent types are permitted to write to `log.md` or `index.md` — it only standardizes the format all of them must use.

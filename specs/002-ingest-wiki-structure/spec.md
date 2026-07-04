# Feature Specification: Ingest Wiki Structure

**Feature Branch**: `002-ingest-wiki-structure`

**Created**: 2026-07-04

**Status**: Draft

**Input**: User description: "Extend ingest so the agent can create or update the final wiki content directly, including a complete wiki structure with source pages, entity pages, concept pages, and automatic index maintenance."

## Clarifications

### Session 2026-07-04

- Q: Which metadata level should all non-source wiki pages require after ingest? → A: Standard frontmatter contract: tags (>=2), confidence, confidence_reason, inbound_links, last_reviewed; superseded_by/supersedes only when relevant.
- Q: When should ingest be required to follow agent instructions from CLAUDE.md and SKILL.md? → A: For every ingest run, before wiki writes, using the active ingest agent CLAUDE.md and SKILL.md as governing instructions.
- Q: For autonomous ingest runs, what guardrail scope should be mandatory for write actions? → A: Restrict writes to wiki/ and task-artifact outputs only; allow reads from approved project context files.
- Q: If a tool action violates guardrails, what must happen? → A: Deny the violating action but continue ingest with remaining allowed actions.
- Q: Which ingest execution model should be required? → A: Hybrid model: the agent performs direct tool actions and also emits a minimal structured task artifact for deterministic validation and audit.
- Q: Where should approved read-context guardrails be defined? → A: In a versioned repository policy file consumed by ingest runtime/tool wrapper.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a complete wiki update from one source (Priority: P1)

A user submits a single source and expects Grimoire to turn it into a complete, connected wiki update rather than a lone summary page. The result should include the source summary plus any related entity or concept pages that are needed to represent the source well.

**Why this priority**: This is the core value of the feature. The ingest should produce a usable wiki structure directly, so the user does not need to manually copy text or assemble related pages after the fact.

**Independent Test**: Can be tested by submitting one source and verifying that the wiki is updated with a coherent set of linked pages that covers the source topic from multiple angles, not just one page.

**Acceptance Scenarios**:

1. **Given** a source that introduces a new topic, **When** ingest completes, **Then** the wiki contains a source summary page and any related entity or concept pages needed to represent that topic.
2. **Given** a source that deepens an existing topic, **When** ingest completes, **Then** the existing relevant pages are updated and linked to the new source rather than leaving the knowledge split across disconnected notes.
3. **Given** a source that touches more than one concept, **When** ingest completes, **Then** the resulting wiki update includes every clearly relevant page needed to keep the topic understandable and navigable.

---

### User Story 2 - Keep the wiki catalog current automatically (Priority: P2)

A user can rely on the wiki catalog, including the index, to show what exists and where to find it after ingest runs. The catalog should be updated as part of the ingest outcome, without requiring manual curation.

**Why this priority**: A complete wiki is only useful if users can find its pages. Automatic catalog upkeep is what keeps the growing structure browsable as the wiki expands.

**Independent Test**: Can be tested by checking that a successful ingest leaves the catalog updated with every page that was created or changed and that the new content can be discovered from the catalog.

**Acceptance Scenarios**:

1. **Given** a successful ingest, **When** the user opens the catalog, **Then** every page touched by that ingest is listed in the appropriate place.
2. **Given** a page is created or updated by ingest, **When** the catalog is rebuilt or refreshed as part of the same operation, **Then** the page remains discoverable without separate manual editing.

---

### User Story 3 - Preserve coherence when the source changes the wiki structure (Priority: P3)

When a source would replace or refine existing knowledge, the user wants the wiki to stay coherent instead of accumulating duplicates, broken links, or half-finished page sets.

**Why this priority**: The feature should improve the wiki structure, not just add more files. A coherent result matters most when the source overlaps with what is already in the wiki.

**Independent Test**: Can be tested by ingesting a source that overlaps with existing pages and confirming the wiki remains consistent, with clear replacement or update behavior and no orphaned partial pages.

**Acceptance Scenarios**:

1. **Given** a source that clearly supersedes existing content, **When** ingest completes, **Then** the older page is clearly marked as replaced and the newer page points to it.
2. **Given** a source that does not warrant a new page, **When** ingest completes, **Then** the relevant existing page is updated instead of creating a duplicate.

---

### Edge Cases

- A source may span several related topics, requiring more than one wiki page to be updated or created.
- A source may mostly confirm existing content, in which case ingest should refine what is already there rather than create a duplicate page.
- A source may be strong enough to replace a previous page entirely, which should result in clear supersession instead of parallel conflicting pages.
- A source may be too narrow to justify a new concept page, but it should still contribute to the most relevant existing pages and the catalog.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Successful ingest MUST be able to produce a complete wiki update from a single source, including a source summary page and any additional entity or concept pages needed to represent the source accurately.
- **FR-002**: The ingest result MUST keep related pages connected so a user can move from the source summary to the supporting entity and concept pages and back again.
- **FR-003**: The system MUST update the wiki catalog automatically as part of ingest so that every page created or changed by the operation is discoverable without manual catalog editing.
- **FR-004**: The system MUST avoid creating duplicate pages for the same idea when an existing page already represents the topic well.
- **FR-005**: When a source clearly replaces older content, the resulting wiki update MUST make that replacement obvious so users can distinguish current knowledge from superseded knowledge.
- **FR-006**: The ingest outcome MUST include a minimal structured task artifact that records which wiki pages were created, updated, superseded, and denied by guardrails so validation and audit are deterministic.
- **FR-007**: The user-facing result of ingest MUST make it clear what was added to the wiki and why those pages were the right ones to change.
- **FR-008**: If ingest fails, the wiki MUST not be left with partial, orphaned, or contradictory pages from that attempt.
- **FR-009**: The original source MUST remain unmodified by ingest.
- **FR-010**: The wiki update MUST remain readable and navigable as plain markdown content outside the running system.
- **FR-011**: Every non-source wiki page created or updated by ingest MUST include frontmatter fields for tags (at least two), confidence, confidence_reason, inbound_links, and last_reviewed.
- **FR-012**: superseded_by and supersedes MUST be present only when a page is explicitly superseded by, or supersedes, another page.
- **FR-013**: Every ingest run MUST apply the active ingest-agent CLAUDE.md and SKILL.md instructions as governing rules before any wiki content is created or modified.
- **FR-014**: Autonomous ingest execution MUST enforce tool-level guardrails so write operations are allowed only under wiki/ and task-artifact output paths, while read operations are limited to approved project context files required for ingest decisions.
- **FR-015**: If an autonomous ingest tool action violates guardrails, the system MUST deny only that action, continue with remaining allowed actions, and record the denied action and reason in the ingest result.
- **FR-016**: The ingest execution model MUST be hybrid: wiki updates are performed via approved tools during the run, and the same run MUST emit the structured task artifact used for validation and traceability.
- **FR-017**: The approved read-context guardrail allowlist MUST be defined in a versioned repository policy file that is loaded by the ingest runtime or tool wrapper for enforcement.

### Key Entities *(include if feature involves data)*

- **Source**: The original input provided by the user for ingest. It is the basis for all wiki changes and remains unchanged.
- **Source Summary Page**: A page that captures the core meaning of the source and anchors related knowledge in the wiki.
- **Entity Page**: A page for a real-world person, place, organization, or project that is relevant to the source.
- **Concept Page**: A page for an idea, pattern, or theme that helps organize knowledge from the source.
- **Wiki Catalog**: The browsable listing that helps users find pages created or updated by ingest.
- **Task Artifact**: A minimal structured record produced by the ingest run that explains what changed, what was denied by guardrails, and which wiki pages were affected.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of successful ingests produce a complete wiki update that includes all clearly relevant pages for the source, not just a single summary page.
- **SC-002**: 100% of successful ingests leave the wiki catalog updated so every touched page is discoverable from the catalog.
- **SC-003**: At least 95% of users can determine which pages were created or changed from the ingest result alone without needing additional editing steps.
- **SC-004**: 0 successful ingests require manual creation of follow-up pages to complete the wiki structure for the same source.
- **SC-005**: 100% of failed ingests leave no partial or orphaned wiki pages behind.
- **SC-006**: 100% of non-source pages created or updated by successful ingest runs include required metadata fields (tags, confidence, confidence_reason, inbound_links, last_reviewed), with supersession fields present only when applicable.
- **SC-007**: 100% of ingest runs that modify wiki content are executed under the active ingest-agent CLAUDE.md and SKILL.md instructions.
- **SC-008**: 100% of autonomous ingest runs that perform wiki writes are executed with enforced tool-level path guardrails (writes only to wiki/ and task-artifact outputs).
- **SC-009**: 100% of denied guardrail actions are captured in ingest output with action, target path, and denial reason while the run continues with allowed actions.
- **SC-010**: 100% of successful ingest runs produce both wiki updates and a valid structured task artifact in the same operation.
- **SC-011**: 100% of autonomous ingest runs load read-context guardrails from the versioned repository policy file, and policy changes are traceable in version history.

## Assumptions

- A single source can legitimately expand into several wiki pages when the topic warrants it.
- Existing pages may be updated, refined, or superseded when the new source clearly justifies that outcome.
- The catalog is part of the wiki experience and should stay current as the wiki grows.
- The user wants the wiki itself to be the final result, not a text dump that must be manually copied into files.
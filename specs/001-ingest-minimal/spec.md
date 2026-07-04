# Feature Specification: Ingest Minimal

**Feature Branch**: `001-ingest-minimal`

**Created**: 2026-07-02

**Status**: Draft

**Input**: User description: "Ingest Minimal — source in, wiki file + task artifact out"

## Clarifications

### Session 2026-07-02

- Q: When a source's topic could match an existing wiki page or warrant a new one, how should ingest decide: update existing page vs. create new one? → A: LLM agent decides semantically (reads index/wiki, uses judgment) whether to update an existing page or create a new one.
- Q: Does this MVP need multi-page fan-out/cross-referencing per source, or just one primary page? → A: Minimal slice creates/updates only one primary page per source; cross-reference fan-out deferred to a later feature.
- Q: When the same source is submitted twice, how should ingest handle it? → A: Treat as a normal ingest; re-process and update the same page again, no special idempotency logic.
- Q: If the backend restarts mid-ingest, how should the stuck task artifact be handled? → A: On restart, any task artifact left "running" is automatically reconciled to "failed" with an interruption reason.
- Q: Does this MVP need to maintain a running index.md catalog entry per ingest? → A: In scope — every successful ingest must also create/update an index.md entry.
- Q: Does this MVP also need to append a log.md entry for every ingest? → A: Yes — every ingest operation, whether it succeeds or fails, must append a log.md entry (full audit-trail parity with task artifacts and index.md).
- Q: Should index.md entries be organized by category in this minimal slice? → A: Yes — index.md entries must be grouped under the appropriate category heading, even in this minimal slice.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit a source and get a wiki page (Priority: P1)

A user submits a single raw source (a document, URL, or pasted text) to Grimoire. The
system processes it and produces a wiki page — a markdown file summarizing and
synthesizing the source content — without any manual editing step by the user.

**Why this priority**: This is the entire vertical MVP. No other feature (Lint, Query,
Task History, Annotation) has anything to operate on until Ingest can turn a source into
wiki content. Every other spec in the project depends on this one.

**Independent Test**: Can be fully tested by submitting one source and verifying that a
new (or updated) wiki markdown page exists whose content reflects the submitted source,
with no further manual steps required.

**Acceptance Scenarios**:

1. **Given** no prior wiki content exists for a topic, **When** a user submits a source
   about that topic, **Then** a new wiki page is created summarizing the source.
2. **Given** a wiki page already exists for a topic covered by the source, **When** a user
   submits a new source on that same topic, **Then** the existing wiki page is updated
   rather than a duplicate page being created.
3. **Given** a source has been submitted, **When** the ingest completes, **Then** the
   original source document itself is left completely unmodified.

---

### User Story 2 - Observe ingest progress and outcome via a task artifact (Priority: P2)

A user can always find a task artifact — a markdown file with structured status and a
human-readable narrative — that records the lifecycle of an ingest operation from
submission through to its final outcome, without needing to inspect logs or code.

**Why this priority**: Transparent, file-based operations are a core differentiator of
Grimoire (task artifacts as first-class, auditable output). Without this, ingest is a
black box and the "compounding wiki with an audit trail" premise breaks down.

**Independent Test**: Can be fully tested by submitting a source and confirming a task
artifact is created immediately, transitions through recognizable states, and — once
finished — records which wiki page(s) were touched and a plain-language summary of what
happened.

**Acceptance Scenarios**:

1. **Given** a user submits a source, **When** the operation is accepted, **Then** a task
   artifact is created right away reflecting an in-progress state.
2. **Given** an ingest operation is running, **When** it finishes successfully, **Then**
   the task artifact is updated to a completed state that references every wiki page it
   created or modified and summarizes what was found and changed.
3. **Given** a completed task artifact, **When** a user opens it, **Then** they can
   determine what happened during that ingest without consulting any other system.

---

### User Story 3 - Fail safely and visibly (Priority: P3)

When an ingest operation cannot complete (e.g., the source is unreadable or empty, or the
agent encounters an internal error), the user is left with a task artifact clearly marked
as failed with a human-readable reason, and no broken or partial wiki content.

**Why this priority**: Lower priority than the happy path, but necessary so failures are
visible and safe rather than silent or corrupting — a minimal robustness bar for a system
that writes directly into a trusted knowledge artifact.

**Independent Test**: Can be fully tested by submitting an invalid or unreadable source
and confirming a task artifact is produced in a failed state with an explanatory reason,
and that no partial or inconsistent wiki page results from the attempt.

**Acceptance Scenarios**:

1. **Given** a user submits an empty or unreadable source, **When** ingest runs, **Then**
   the resulting task artifact is marked failed with a clear, human-readable reason.
2. **Given** an ingest operation fails partway through, **When** the failure is recorded,
   **Then** the wiki is left in exactly the state it was in before the attempt (no
   orphaned or half-written pages).

---

### Edge Cases

- What happens when the same source is submitted twice — the ingest agent's normal
  update-vs-create judgment (FR-012) applies without special-case idempotency logic: the
  matching wiki page is simply re-processed and updated again.
- What happens when the submitted source is empty, unreadable, or in an unsupported
  format?
- What happens if the backend process is interrupted or restarted while an ingest
  operation is still running — on restart, any task artifact left in a "running" state is
  automatically reconciled to a "failed" state with a reason noting the interruption (see
  FR-013); this reconciliation also produces a corresponding log.md entry (see FR-015).
- When a source's content could plausibly match more than one existing wiki page, or no
  existing page at all, the ingest agent's own semantic judgment (informed by existing
  wiki content) determines whether to update an existing page or create a new one (see
  FR-012).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to submit exactly one raw source (a document, URL, or
  pasted text) to trigger a single ingest operation.
- **FR-002**: The system MUST create a task artifact for every ingest operation
  immediately upon submission, before processing completes.
- **FR-003**: The system MUST update the task artifact's status as the operation
  progresses through its lifecycle (e.g., queued/running through to completed or failed).
- **FR-004**: On successful ingest, the system MUST produce or update exactly one primary
  wiki page whose content reflects the submitted source; fan-out updates to other related
  or cross-referenced pages are out of scope for this minimal slice.
- **FR-005**: The completed task artifact MUST record which wiki page(s) were created or
  modified, in a way that lets a user trace from the task to the affected page(s).
- **FR-006**: The completed task artifact MUST include a human-readable summary of what
  the operation found and what it changed in the wiki.
- **FR-007**: If ingest fails for any reason, the system MUST mark the corresponding task
  artifact as failed and record a clear, human-readable reason for the failure.
- **FR-008**: A failed ingest MUST NOT leave partial, inconsistent, or orphaned wiki
  content behind; the wiki MUST remain in the state it was in before the failed attempt.
- **FR-009**: The raw source submitted by the user MUST remain unmodified by the ingest
  operation at all times.
- **FR-010**: Both wiki pages and task artifacts MUST be persisted as plain files that
  remain browsable and readable outside of the running system (e.g., in a file browser or
  a markdown-aware editor).
- **FR-011**: The system MUST NOT report an ingest operation as completed unless the
  resulting wiki page(s) and the task artifact both exist and are consistent with each
  other.
- **FR-012**: When a source's topic could plausibly match more than one existing wiki
  page, or no existing page at all, the system MUST have the ingest agent decide whether
  to update an existing page or create a new one using its own semantic judgment
  (informed by existing wiki content, e.g. the index), without requiring a deterministic
  filename/title lookup rule or a user confirmation step.
- **FR-013**: If the backend restarts while a task artifact is in a "running" state, the
  system MUST reconcile that task artifact to a "failed" state with a reason noting the
  interruption, rather than leaving it stuck as running.
- **FR-014**: On successful ingest, the system MUST also create or update a single entry,
  grouped under the appropriate category heading, in a running wiki index (index.md) that
  references the wiki page(s) touched by the operation, so the index stays a complete,
  current, category-organized catalog of all wiki pages. The category for an entry is
  determined by the ingest agent's own semantic judgment (the same judgment used in
  FR-012), without requiring a fixed, predefined category taxonomy in this minimal slice.
- **FR-015**: The system MUST append an entry to an append-only chronological log
  (log.md) for every ingest operation, whether it succeeds or fails, recording at minimum
  the timestamp, operation type, and outcome, so log.md remains a complete record of every
  ingest attempt alongside index.md and task artifacts.

### Key Entities

- **Source**: The raw, human-provided input document submitted for ingest (e.g., a file,
  URL, or pasted text). Immutable — never created or modified by the system, only read.
- **Wiki Page**: A markdown file representing synthesized, LLM-authored knowledge derived
  from one or more sources. Owned and maintained entirely by the ingest operation.
- **Task Artifact**: A markdown file with structured status metadata (operation type,
  lifecycle status, timestamps, referenced source, wiki pages touched) plus a
  human-readable narrative body, recording one ingest operation end-to-end.
- **Wiki Index (index.md)**: A single content-oriented catalog file listing every wiki
  page with a one-line summary, grouped under category headings. Updated by the ingest
  operation whenever a wiki page is created or modified, so it always reflects the
  current, category-organized set of wiki pages.
- **Ingest Log (log.md)**: An append-only chronological record of every ingest operation,
  successful or failed, with one entry per operation noting timestamp, operation type, and
  outcome. Updated by every ingest attempt, complementing index.md (current state) and
  task artifacts (per-operation detail).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can submit a single source and obtain a completed wiki page
  reflecting its content with zero manual editing steps.
- **SC-002**: 100% of submitted ingest operations produce exactly one corresponding task
  artifact, whether the operation succeeds or fails.
- **SC-003**: A user can determine the full outcome of any past ingest operation (success
  or failure, and what changed) by reading its task artifact alone.
- **SC-004**: 100% of failed ingest attempts leave the wiki in exactly the state it was in
  prior to the attempt, with no orphaned or partial pages.
- **SC-005**: Starting from any wiki page produced by ingest, a user can trace back to the
  task and source that produced it.
- **SC-006**: 100% of successful ingest operations leave the wiki index (index.md) with an
  entry, correctly grouped under a category, for every wiki page they touched.
- **SC-007**: 100% of ingest operations, successful or failed, leave a corresponding
  chronological entry in log.md.

## Assumptions

- A "source" for this minimal vertical slice is a single document, URL, or pasted-text
  submission per ingest operation; batch or multi-source ingest in one operation is out of
  scope for this feature.
- This feature covers ingest only. Lint (finding contradictions/staleness), Query (chat
  answers), and cross-ingest cross-referencing are explicitly out of scope here and are
  addressed by later, dependent features.
- Ingest in this minimal slice creates or updates a single primary wiki page per source;
  fan-out updates to multiple related/cross-referenced pages from one source are out of
  scope for this feature and addressed by a later feature.
- Wiki pages and task artifacts are both plain markdown files kept under version control
  alongside each other; no opaque or database-only storage is assumed for either.
- The entry point used to submit a source (Web UI, CLI, or another channel) is not fixed
  by this spec — any channel may act as a trigger as long as it produces the same
  task-artifact contract described here.
- Authentication and authorization for who may submit a source is out of scope for this
  minimal slice; a single trusted user/developer context is assumed.

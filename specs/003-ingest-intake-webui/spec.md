# Feature Specification: Ingest Intake Web UI

**Feature Branch**: `claude/ingest-agent-webui-teq68l`

**Created**: 2026-07-06

**Status**: Draft

**Input**: User description: "Build a Web UI for using the Ingest agent. The user
submits a source to the Hub: either a URL, Markdown files, PDFs, or Office documents. The
Hub converts the submitted document(s) to Markdown using MarkItDown and stores the
result in the 'raw' directory. The UI must show the user that their submission has been
accepted and is being processed (transparent processing status). The task created for
the submission must be visible on a Kanban board. For this feature, there is no further
interaction with the Ingest agent beyond submitting data and displaying processing
status — the agent is not triggered or orchestrated from this UI yet, only the
intake/conversion/task-visibility flow is in scope."

## Clarifications

### Session 2026-07-06

- Q: Should this feature reuse the existing Task Artifact entity (status
  `queued | running | completed | failed`, per 001-ingest-minimal) and extend its
  lifecycle with new pre-agent stages, rather than introduce a separate "Intake Task"
  entity? → A: Yes — reuse the existing Task Artifact entity. This feature creates it at
  the moment of acceptance and drives it through new pre-agent stages up to the
  already-defined `queued` status (or `failed`). No new task entity is introduced.
- Q: What lifecycle stages should the Task Artifact pass through in this feature, before
  a later feature hands it to the Ingest agent? → A: `received → converting → queued`
  (success) or `→ failed`. These two new pre-agent stages precede the existing `queued`
  status; the subsequent `queued → running → completed | failed` transitions are
  unchanged from 001/002 and remain owned by the Ingest agent once a later feature
  triggers it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit a source and see it accepted (Priority: P1)

A user opens the Web UI and submits a source to the Hub — either a URL, or a single
Markdown, PDF, or Office document. The moment the submission is sent, the UI confirms it
was accepted and shows that processing has started. As conversion completes, the user
sees the status change to reflect that a Markdown version of the source now exists,
without having to inspect any files or logs themselves.

**Why this priority**: This closes the trust gap between "I sent something" and "the
system is doing something with it." Without this, users have no way to know whether a
submission worked, is still running, or was silently lost — which is the minimum bar for
a usable intake surface.

**Independent Test**: Submit a single Markdown file through the UI and verify that an
acknowledgment and a non-terminal task status appear immediately, followed by a status
change once the corresponding Markdown file is stored — without querying the filesystem.

**Acceptance Scenarios**:

1. **Given** the intake form, **When** a user submits a supported Markdown, PDF, or
   Office file, **Then** the UI immediately confirms acceptance and shows a task in a
   non-terminal ("processing") state.
2. **Given** the intake form, **When** a user submits a URL instead of a file, **Then**
   the UI immediately confirms acceptance and shows a task in a non-terminal state in the
   same way as a file submission.
3. **Given** an accepted submission that is being converted, **When** conversion finishes
   successfully, **Then** the task's displayed status changes to reflect that the
   resulting Markdown file has been stored, with no action required from the user.

---

### User Story 2 - Track all submissions on a Kanban board (Priority: P2)

A user opens a Kanban-style board that lists every source they have submitted, grouped by
its current lifecycle stage (`received`, `converting`, `queued`, or `failed`). This gives them a
single place to see the state of all their submissions — past and in progress — instead
of having to remember or re-check each one individually.

**Why this priority**: Once individual submissions are trustworthy (User Story 1), users
need an overview when they submit more than one source, or return later to check on
something they submitted earlier. This is what makes the intake flow usable beyond a
single one-off submission.

**Independent Test**: Submit two or three sources of different kinds in sequence, then
open the board and verify each appears as its own card, correctly grouped by its current
stage, with the board reflecting stage changes as they happen.

**Acceptance Scenarios**:

1. **Given** multiple submitted sources at different stages of processing, **When** the
   user opens the board, **Then** every submission appears exactly once, grouped under
   its current stage.
2. **Given** a task visible on the board, **When** its processing stage changes, **Then**
   the board reflects the new stage without the user needing to resubmit or manually
   reload the underlying data.
3. **Given** a task that finished successfully some time ago, **When** the user returns
   to the board later, **Then** the task is still visible in its final stage.

---

### User Story 3 - See clearly when a submission fails (Priority: P3)

A user submits a source that cannot be processed — for example, an unreadable document or
an unreachable URL. The board shows the corresponding task as failed, with a plain-
language explanation of what went wrong, instead of the task silently disappearing or
being stuck in a processing state forever.

**Why this priority**: Failure visibility protects trust in the system once it is
already in use for successful cases; it is less critical than the core happy path but
necessary before the feature can be relied on day to day.

**Independent Test**: Submit a source known to fail conversion (e.g., a corrupted file or
an unreachable URL) and verify the board shows a failed task with a human-readable reason
and no stored Markdown file for that submission.

**Acceptance Scenarios**:

1. **Given** a submission whose document cannot be converted, **When** conversion fails,
   **Then** the task's stage on the board shows "failed" together with a plain-language
   reason.
2. **Given** a submission with a URL that cannot be reached, **When** the fetch fails,
   **Then** the task's stage on the board shows "failed" with a reason describing the
   fetch failure.
3. **Given** any failed submission, **When** the user checks the raw-source storage
   location, **Then** no partial or corrupt file exists there for that submission.

---

### Edge Cases

- What happens when the user submits a file type that is not Markdown, PDF, or a
  supported Office format? The submission MUST be rejected before a task is created, with
  a clear explanation of which formats are supported.
- What happens when the user submits an empty (zero-byte) file, or a URL that returns
  empty content? The task MUST fail with a plain-language reason rather than storing an
  empty Markdown file.
- What happens when a submitted URL is unreachable, times out, or requires
  authentication the system does not have? The task MUST fail with a reason describing
  the fetch problem.
- What happens when a submitted PDF or Office document is corrupted, password-protected,
  or otherwise unreadable? The task MUST fail with a reason describing the conversion
  problem, and no partial file is left in the raw-source location.
- What happens when the user submits the same source (the same file or URL) more than
  once? Each submission MUST be treated as its own independent task; the system does not
  attempt to detect or merge duplicates in this feature.
- What happens when multiple submissions are in flight at the same time? Each MUST
  progress independently and be individually visible on the board; one submission's
  processing MUST NOT block or delay another's acceptance.
- What happens if the user closes or reloads the browser while a submission is still
  processing? The task's status MUST still be visible and correct when the user reopens
  the board — status is not held only in the browser session.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to submit exactly one source per submission to the Hub,
  as one of: a URL, a Markdown file, a PDF file, or a supported Office document.
- **FR-002**: The system MUST immediately acknowledge every accepted submission to the
  user, before conversion has completed.
- **FR-003**: The system MUST validate the submitted source's format before accepting
  it, and MUST reject unsupported formats with a clear, actionable message without
  creating a task.
- **FR-004**: The system MUST convert every accepted, non-Markdown source into a
  Markdown representation of its content.
- **FR-005**: The system MUST store the resulting Markdown file in the project's
  designated raw-source location, and that stored file MUST remain unmodified once
  written.
- **FR-006**: The system MUST create the existing project-wide Task Artifact record (per
  001-ingest-minimal, not a new task entity) for each accepted submission, at the moment
  of acceptance — earlier than before, since previously only the Ingest agent created it
  on its own startup.
- **FR-007**: Every Task Artifact created by this feature MUST be visible on a
  Kanban-style board, grouped by its current lifecycle stage: `received`, `converting`,
  `queued`, or `failed`.
- **FR-008**: The board MUST reflect a Task Artifact's stage changes as processing
  proceeds, without requiring the user to resubmit the source or manually refresh the
  underlying data.
- **FR-009**: If conversion or fetching fails for any reason, the system MUST mark the
  corresponding Task Artifact as `failed`, present a human-readable failure reason to the
  user, and MUST NOT leave a partial or corrupt file in the raw-source location.
- **FR-010**: The system MUST NOT trigger or otherwise invoke the Ingest agent as part of
  this submission flow; within this feature, a Task Artifact reaches a terminal state of
  `queued` (success) or `failed` (failure). The subsequent `queued → running →
  completed | failed` transitions remain exactly as already specified elsewhere and are
  owned by the Ingest agent once a later feature triggers it against the same record.
- **FR-011**: A Task Artifact's stage and outcome MUST remain visible on the board after
  the submission completes and independent of the user's browser session (e.g., after a
  page reload or in a later visit).
- **FR-012**: The system MUST allow multiple submissions to be accepted and processed
  without one submission's processing blocking another's acceptance.

### Key Entities

- **Source Submission**: A single user-provided input to the intake flow — a URL or one
  uploaded document (Markdown, PDF, or Office format) — together with its declared or
  detected kind. Distinct from, and a precursor to, the existing project-wide "Source"
  concept once conversion has completed.
- **Task Artifact**: The existing project-wide record (per 001-ingest-minimal)
  representing one ingest operation's lifecycle end-to-end — no new task entity is
  introduced by this feature. This feature is responsible for creating the Task Artifact
  at the moment a submission is accepted, and driving it through two new pre-agent
  stages, `received` and `converting`, into the already-defined `queued` status (ready
  for a future feature to trigger the Ingest agent) or `failed`. The subsequent `queued →
  running → completed | failed` transitions remain exactly as already specified and stay
  owned by the Ingest agent once a later feature invokes it against this same record.
- **Raw Source File**: The Markdown file resulting from conversion (or the original
  content, already in Markdown form), stored immutably in the project's raw-source
  location — the same immutable "Source" ubiquitous-language entity used elsewhere in the
  project, now populated through this intake flow instead of manual placement.
- **Kanban Board**: A visualization surfacing every Task Artifact grouped by its current
  lifecycle stage, giving the user a single place to observe all submissions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of accepted submissions produce a visible task on the Kanban board
  within seconds of submission.
- **SC-002**: 100% of successfully converted submissions result in a Markdown file
  present in the raw-source location, discoverable by the user via its task.
- **SC-003**: 100% of failed conversions leave no partial or corrupt file in the
  raw-source location and present a human-readable failure reason on the corresponding
  task.
- **SC-004**: Users can determine any submission's current processing stage and final
  outcome entirely from the board, without consulting the filesystem, logs, or any other
  tool.
- **SC-005**: Users can distinguish a successful submission from a failed one within a
  few seconds of opening the board, for 100% of completed tasks.

## Assumptions

- Exactly one source (one URL, or one uploaded file) is accepted per submission; batch
  submission of multiple sources in a single request is out of scope for this feature,
  consistent with the existing project-wide single-source-per-operation convention.
- "Office documents" refers to a bounded set of common formats (e.g., Word, PowerPoint,
  Excel); the exact supported format list is a planning-level detail, not a scope
  decision for this spec.
- This feature continues the existing single-user, no-authentication scope already
  assumed by the project; this feature does not introduce user accounts or access
  control.
- Handing the stored Markdown source off to actually run the Ingest agent is explicitly
  out of scope for this feature; it is expected to be connected in a subsequent feature.
- This feature shifts Task Artifact creation earlier: the Hub creates the record when a
  submission is accepted, rather than the Ingest agent creating it on its own startup.
  When a later feature triggers the agent against a `queued` Task Artifact, the agent
  continues updating that same record rather than creating a new one.
- Reasonable default limits on upload size and request rate apply to keep the intake
  flow reliable; exact thresholds are a planning-level detail.
- The raw-source storage location referenced here is the same one existing ingest specs
  already assume file placement into; this feature is what actually establishes it as a
  real, populated location rather than a manually-prepared path.

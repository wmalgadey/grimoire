# Feature Specification: Ingest Agent & Web-UI Channel

**Feature Branch**: `004-ingest-agent-webui`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Erstelle einen Ingest-Agent mit zugehörigem Web-UI Channel für das Grimoire-Projekt."

---

## Bounded Contexts & Ubiquitous Language

### Bounded Contexts

- **Ingestion**: File detection, deduplication, processing pipeline (chunking, AI-assisted content analysis, structured indexing), status persistence, and versioned output commitment.
- **Agent Lifecycle**: Hub registration, heartbeat, remote triggering, and standalone fallback operation.
- **User Interaction**: Web-UI channel — file upload, run triggering, real-time progress, feedback loop, post-ingest conversation, and batch summary.

### Ubiquitous Language

| Term | Definition |
|------|-----------|
| **Raw Source** | An unprocessed file residing in the `raw/sources/` directory tree |
| **Ingest Run** | One complete batch processing cycle over all pending raw sources |
| **Processing Cache** | A SHA256-indexed, durable record of every previously processed file |
| **Ingest Record** | A single cache entry: file path, SHA256 hash, processing status, timestamp, chunk count |
| **Ingest Status** | The outcome for a single file: `processed`, `failed`, or `skipped` |
| **Batch Summary** | A structured report produced at the end of each ingest run |
| **Feedback Request** | An agent-generated inquiry about an ambiguous file, with suggested resolution options |
| **Feedback Response** | A user decision for a specific feedback request: `process`, `skip`, or `tag` |
| **Chunk** | A discrete text segment produced from splitting a raw source during processing |
| **AI Analysis** | The step in the pipeline where a language model extracts structure, metadata, and key concepts from a chunk |
| **Ingest Conversation** | A multi-turn dialogue between the agent and the user following successful processing of a document, grounded in the document's content and analysis results |
| **Conversation Turn** | A single message-response exchange within an Ingest Conversation |
| **Conversation Context** | The document content, extracted metadata, and analysis results that prime the agent for an Ingest Conversation |
| **Hub** | The central orchestration service that manages agent lifecycle and routing |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Automated Ingest of New Raw Sources (Priority: P1)

A content contributor drops a new Markdown document into `raw/sources/research/`. Within seconds the agent detects the file, processes it through the pipeline, and creates a Git commit documenting the outcome. The contributor does not need to interact with any interface — the process is fully autonomous.

**Why this priority**: This is the core value proposition. All downstream agents depend on the processed output. Without this working reliably, nothing else in the system functions.

**Independent Test**: Drop a new file into `raw/sources/`; verify a Git commit is created containing the file name, timestamp, and chunk count — no UI required.

**Acceptance Scenarios**:

1. **Given** a new file appears in `raw/sources/` (any subdirectory), **When** the agent detects the file, **Then** it computes the SHA256 hash, processes the file through the pipeline, persists an `IngestRecord` with status `processed`, and creates a Git commit.
2. **Given** a previously processed file is modified, **When** the agent detects the change, **Then** the SHA256 hash no longer matches the cache entry and the file is reprocessed.
3. **Given** a previously processed file is unchanged (same SHA256), **When** the agent checks the cache, **Then** the file is skipped with status `skipped` and no reprocessing occurs.
4. **Given** a file fails during processing (e.g., unreadable content), **When** the error occurs, **Then** the `IngestRecord` is persisted with status `failed`, the error is logged, and the agent continues processing remaining files without aborting the run.

---

### User Story 2 — File Upload via Web-UI (Priority: P1)

A user uploads a PDF report through the browser interface. The file lands in `raw/sources/`, the agent picks it up automatically, and the user watches the processing progress in real time without leaving the page.

**Why this priority**: The Web-UI upload path is the primary human-facing entry point. It must work end-to-end before other UI features have value.

**Independent Test**: Upload a file through the browser form; confirm the file appears in `raw/sources/` and processing progress is visible in the status feed.

**Acceptance Scenarios**:

1. **Given** the Web-UI is open, **When** the user selects a file and submits the upload form, **Then** the file is saved to `raw/sources/` and the agent begins processing it.
2. **Given** a file is being processed, **When** the agent advances through pipeline steps, **Then** the Web-UI status feed updates in real time showing current file name and progress.
3. **Given** an upload completes and processing finishes, **When** the run ends, **Then** the Web-UI displays the batch summary table.

---

### User Story 3 — Manual Ingest Trigger (Priority: P2)

A developer wants to force a full re-scan of `raw/sources/` after manually placing several files via the filesystem. They press the Trigger button in the Web-UI and watch the run proceed.

**Why this priority**: Useful for operational control and for development workflows where file watching may not catch bulk filesystem changes. Depends on the status feed (P1) being in place.

**Independent Test**: With files already in `raw/sources/`, click Trigger; verify an ingest run starts and the status feed shows activity.

**Acceptance Scenarios**:

1. **Given** the Web-UI is open and no run is in progress, **When** the user clicks the Trigger button, **Then** an ingest run starts immediately and the button becomes disabled until the run completes.
2. **Given** an ingest run is already in progress, **When** the user views the UI, **Then** the Trigger button is disabled and cannot start a second concurrent run.

---

### User Story 4 — Interactive Feedback for Ambiguous Files (Priority: P2)

During an ingest run the agent encounters a file with no recognized extension. Rather than silently failing or skipping it, the agent surfaces a feedback request in the Web-UI. The user decides to tag the file as `markdown` and the agent resumes processing.

**Why this priority**: Ensures no data is silently lost due to unknown formats or edge cases. Depends on real-time status feed (P1).

**Independent Test**: Place an ambiguous file (unrecognized extension, oversized, or missing metadata) in `raw/sources/`; verify a feedback dialog appears in the Web-UI and that the agent waits for a response before continuing.

**Acceptance Scenarios**:

1. **Given** the agent encounters an ambiguous file, **When** it generates a Feedback Request, **Then** the Web-UI displays an inline dialog with the file name, reason for ambiguity, and resolution options (`process`, `skip`, `tag`).
2. **Given** a feedback dialog is open, **When** the user selects an option and confirms, **Then** the Feedback Response is delivered to the agent and processing resumes accordingly.
3. **Given** a Feedback Response is submitted, **When** the response is saved to the cache, **Then** future ingest runs apply the same decision to the same file without prompting again.
4. **Given** the agent runs in standalone CLI mode, **When** an ambiguous file is detected, **Then** the agent prompts interactively on the terminal and waits for keyboard input.

---

### User Story 5 — Post-Ingest Conversation (Priority: P2)

A researcher uploads a dense technical paper. After the agent finishes processing it, a conversation panel opens in the Web-UI. The agent greets the user with a brief summary of what it found — main topics, structure, notable content — and invites questions. The researcher asks "What are the key arguments in section 3?" and the agent answers using the document's content. The researcher then adds "Add the tag 'quantum-computing' to this document" and the agent confirms the correction is recorded.

**Why this priority**: This is the distinguishing capability of the Ingest Agent over a purely mechanical pipeline. The human-in-the-loop conversation makes every ingest an opportunity to validate understanding, correct interpretations, and enrich metadata — for every document, not just ambiguous ones. Depends on successful processing (P1).

**Independent Test**: Process a single document; verify the agent opens a conversation panel with an initial summary message and that at least two follow-up turns can be completed without error.

**Acceptance Scenarios**:

1. **Given** a file has been successfully processed, **When** processing completes, **Then** the agent automatically opens an Ingest Conversation with an opening message containing: a brief document summary, key topics identified, and an explicit invitation to ask questions or provide corrections.
2. **Given** an Ingest Conversation is open, **When** the user submits a question or statement, **Then** the agent responds within the context of the processed document's content and analysis results.
3. **Given** an Ingest Conversation is open, **When** the user provides a factual correction or requests a metadata change (e.g., tags, title, categorisation), **Then** the agent acknowledges the correction and records it alongside the `IngestRecord` for the document.
4. **Given** an Ingest Conversation is open, **When** the user dismisses or closes the conversation panel, **Then** the ingest result is unaffected and the run continues or is marked complete normally.
5. **Given** a batch run processes multiple files, **When** the batch completes, **Then** each successfully processed file has a pending Ingest Conversation accessible from the batch summary; conversations are not forced open simultaneously.
6. **Given** the agent runs in standalone CLI mode, **When** a file is successfully processed, **Then** the agent enters an interactive prompt session about the document; the user can type questions or press Enter to skip and continue.

---

### User Story 6 — Batch Summary Review (Priority: P3)

After a run completes, a developer reviews the compact summary table to confirm all expected files were processed, identify any failures, and check chunk counts.

**Why this priority**: Informational, adds confidence but does not block core processing value. Depends on completed ingest runs (P1) and complements the post-ingest conversation (P2) by surfacing all files in one place.

**Independent Test**: Run an ingest batch; verify a summary table appears listing each file with its status, chunk count, duration, and a link to open its Ingest Conversation.

**Acceptance Scenarios**:

1. **Given** an ingest run completes, **When** the user views the Web-UI, **Then** a batch summary table is displayed with columns: file name, status, chunks produced, processing duration, and a "Discuss" action to open the Ingest Conversation for that file.
2. **Given** the agent runs in standalone mode, **When** a run completes, **Then** the batch summary is printed to standard output in a structured, human-readable format.

---

### Edge Cases

- What happens when `raw/sources/` does not exist at agent startup? → Agent creates the directory and begins watching.
- What happens when a file is deleted from `raw/sources/` after being processed? → The cache entry is retained; no reprocessing is triggered. Deletion events are ignored.
- What happens when the Hub is unreachable at startup? → Agent logs a warning and enters standalone mode; it retries Hub registration periodically in the background.
- What happens when the AI service is unavailable during a run? → The affected file's `IngestRecord` is set to `failed` with the error recorded; the agent continues with remaining files.
- What happens when a user uploads a file that already exists in `raw/sources/`? → The file is overwritten; the SHA256 change triggers reprocessing.
- What happens when a run is triggered while a feedback dialog is awaiting a response? → The pending feedback request must be resolved before the new run proceeds; the Trigger button remains disabled.
- What happens when an uploaded file exceeds a configurable size threshold? → The agent issues a Feedback Request rather than processing automatically.
- What happens when a user asks the agent a question outside the scope of the processed document? → The agent acknowledges the question but clarifies its response is grounded in the document; it does not fabricate information not present in the source.
- What happens when the user dismisses an Ingest Conversation without engaging? → No data is lost; the conversation remains accessible from the batch summary and can be re-opened at any time.
- What happens when a batch processes 20 files — are 20 conversations forced open simultaneously? → No; conversations are queued and accessible on demand from the batch summary. Only one conversation is ever in focus at a time.
- What happens when a correction submitted during an Ingest Conversation conflicts with extracted metadata? → The user's correction takes precedence and is written to the `IngestRecord`; the original extracted value is preserved as a note.

---

## Requirements *(mandatory)*

### Functional Requirements

**Agent Core**

- **FR-001**: The agent MUST watch `raw/sources/` recursively for newly created and modified files.
- **FR-002**: The agent MUST compute a SHA256 hash for each candidate file and skip processing if the hash matches a `processed` cache entry.
- **FR-003**: The agent MUST process each qualifying file through a sequential pipeline: chunking → AI-assisted content analysis (structure extraction, metadata, key concepts) → structured indexing.
- **FR-004**: The agent MUST persist an `IngestRecord` for every processed file, capturing file path, SHA256 hash, ingest status, timestamp, and chunk count.
- **FR-005**: The agent MUST create a Git commit after each ingest run that produced at least one `processed` file; the commit message MUST include the file name(s), ISO 8601 timestamp, and total chunk count.
- **FR-006**: The agent MUST display (or emit) a structured batch summary at the end of every ingest run, covering all files: processed, failed, and skipped.
- **FR-007**: The agent MUST issue a Feedback Request for any file matching ambiguity criteria: unrecognized format, size above threshold, or absent required metadata.
- **FR-008**: The agent MUST persist a Feedback Response in the processing cache so that the same file does not trigger another Feedback Request in future runs.
- **FR-009**: The agent MUST be fully configurable via environment variables including, at minimum: source directory path, Git credentials, AI service credentials and model identifier, file size threshold, and Hub endpoint URL.

**Post-Ingest Conversation**

- **FR-020**: After successfully processing a file, the agent MUST initiate an Ingest Conversation with an opening message that includes: a brief document summary, the key topics identified, and an explicit invitation to ask questions or provide corrections.
- **FR-021**: The agent MUST conduct the Ingest Conversation using the document's full Conversation Context (content, chunks, extracted metadata, analysis results) as its grounding; it MUST NOT fabricate information absent from the source.
- **FR-022**: The agent MUST support multi-turn Ingest Conversations: each user message generates a contextually grounded agent response until the user dismisses the conversation.
- **FR-023**: The agent MUST accept corrections and metadata additions submitted during an Ingest Conversation and persist them to the `IngestRecord`; the user's value MUST take precedence over the originally extracted value.
- **FR-024**: In a batch run with multiple processed files, the agent MUST queue Ingest Conversations and present them on demand; it MUST NOT open multiple conversation panels simultaneously.
- **FR-025**: In standalone CLI mode, the agent MUST enter an interactive prompt after each successfully processed file, allowing the user to converse about the document or press Enter to skip and continue.

**Agent Lifecycle**

- **FR-010**: The agent MUST register itself with the Hub on startup, providing its identity, version, and current status.
- **FR-011**: The agent MUST send periodic heartbeats to the Hub while running.
- **FR-012**: The Hub MUST be able to remotely trigger an ingest run on the agent.
- **FR-013**: The agent MUST operate in standalone mode when the Hub is unreachable, retrying registration in the background without interrupting local processing.

**Web-UI Channel**

- **FR-014**: The Web-UI MUST provide a file upload form that accepts one or more files and writes them to `raw/sources/`.
- **FR-015**: The Web-UI MUST provide a Trigger button that initiates a manual ingest run; the button MUST be disabled while a run is in progress.
- **FR-016**: The Web-UI MUST display a real-time status feed showing the currently processed file, a progress indicator, and a scrollable log stream.
- **FR-017**: The Web-UI MUST display an inline Feedback Dialog when a Feedback Request is pending, showing the file name, reason for ambiguity, and the available resolution options.
- **FR-018**: The Web-UI MUST display a batch summary table after each run, with columns: file name, status, chunks produced, processing duration, and a "Discuss" action per row.
- **FR-019**: The Web-UI MUST NOT require authentication in the initial version.
- **FR-026**: The Web-UI MUST display a conversation panel for each Ingest Conversation; the panel MUST show the agent's opening message, the full turn history, and a text input for the user's next message.
- **FR-027**: The Web-UI MUST allow the user to dismiss an Ingest Conversation panel at any time without affecting the ingest result or preventing later re-opening via the batch summary.

### Key Entities

- **RawSource**: A file under `raw/sources/`. Key attributes: relative path, SHA256 hash, size, last modified timestamp.
- **IngestRecord**: A durable cache entry per file. Key attributes: file path, SHA256 hash, ingest status (`processed` / `failed` / `skipped`), processed-at timestamp, chunk count, error message (if failed), user corrections (if any).
- **IngestRun**: An aggregate of all `IngestRecord` outcomes for a single batch. Key attributes: run ID, started-at, completed-at, total files, processed count, failed count, skipped count.
- **FeedbackRequest**: An agent-generated inquiry. Key attributes: file path, reason (unknown format / oversized / missing metadata), suggested options.
- **FeedbackResponse**: A user decision. Key attributes: file path, chosen action (`process` / `skip` / `tag`), tag value (if action is `tag`), decided-at timestamp.
- **IngestConversation**: A multi-turn dialogue about a single processed document. Key attributes: document file path, conversation ID, opening message (agent-generated summary), turn history (ordered list of user and agent messages), corrections recorded, created-at timestamp, dismissed-at timestamp (nullable).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: New or modified files in `raw/sources/` are detected and processing begins within 30 seconds of the file being written.
- **SC-002**: Files with an unchanged SHA256 hash are never reprocessed; the false-positive reprocessing rate is 0%.
- **SC-003**: Every ingest run that produces at least one processed file results in a Git commit with a meaningful, human-readable message.
- **SC-004**: Processing status for all files is preserved across agent restarts with no data loss.
- **SC-005**: The agent continues processing new files in standalone mode within 10 seconds of detecting Hub unavailability.
- **SC-006**: A file uploaded via the Web-UI triggers visible processing activity in the status feed within 5 seconds of upload completion.
- **SC-007**: A Feedback Request raised by the agent appears as an inline dialog in the Web-UI within 3 seconds of detection.
- **SC-008**: The batch summary table is visible in the Web-UI within 2 seconds of run completion.
- **SC-009**: An ingest run with a Feedback Request pending cannot be superseded by a new trigger — concurrency is bounded to one active run at a time.
- **SC-010**: For every successfully processed file, an Ingest Conversation opening message is available to the user within 5 seconds of processing completion.
- **SC-011**: User corrections submitted during an Ingest Conversation are persisted to the `IngestRecord` and visible in subsequent batch summaries without requiring a re-run.
- **SC-012**: An Ingest Conversation dismissed by the user remains accessible and re-openable from the batch summary without data loss.

---

## Assumptions

- The `raw/sources/` directory is the sole input point; the agent does not monitor any other directory in the initial version.
- The agent is a standalone process located in `src/agents/ingest/`; it is not embedded in the backend (see ADR-010).
- Content processing is performed by a language model via an AI SDK; no vector embedding service is required or assumed.
- AI service credentials are provided via environment variable; no API key is hardcoded.
- Git is available in the agent's runtime environment and credentials are pre-configured (no interactive authentication).
- The Web-UI Channel is a dedicated page or route within the existing Grimoire frontend; it is not a separate application.
- Standalone CLI mode does not require a browser; the terminal is the fallback interaction surface for feedback requests.
- No authentication or access control is required for the Web-UI in this version; it is assumed to run in a trusted local or development network.
- File size threshold for triggering a Feedback Request defaults to 10 MB and is configurable via environment variable.
- The processing pipeline produces outputs compatible with the format expected by downstream agents (indexing schema is defined separately).
- An Ingest Conversation is tied to a single document and does not span multiple files; cross-document queries are out of scope for this feature.
- Ingest Conversation turn history is held in memory for the duration of the session; durable conversation persistence across agent restarts is out of scope for this version.
- Mobile browser support is out of scope for the Web-UI Channel in this version.

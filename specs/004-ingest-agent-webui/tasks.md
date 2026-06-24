# Tasks: Ingest Agent & Web-UI Channel

**Input**: Design documents from `/specs/004-ingest-agent-webui/`

**Branch**: `004-ingest-agent-webui`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

---

## Phase 0: Architecture Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: ADR-010 structural rules must be enforced before any feature code is written.

⚠️ **These tests MUST EXIST and FAIL (Red) before feature code begins**.

- [ ] T000 Implement NetArchTest.Rules architecture test enforcing ADR-010 in `src/backend/Grimoire.ArchTests/IngestAgentIsolationTests.cs`: Assert that `src/agents/ingest/` has zero imports from `src/backend/` namespaces
- [ ] T001 Implement secret-scanning architecture test in `src/backend/Grimoire.ArchTests/IngestSecretTests.cs`: Assert that no source file contains the literal string `ANTHROPIC_API_KEY`

**Checkpoint**: Architecture tests exist, fail on the constraint violations, and are executed by CI/CD.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and structure for all three subprojects

- [ ] T002 Create `src/agents/ingest/Grimoire.Ingest.csproj` with dependencies: `Anthropic.SDK`, `LibGit2Sharp`, `System.Data.SQLite`, `System.IO.Pipelines`; target .NET 9
- [ ] T003 [P] Create `src/agents/ingest/Program.cs` — Generic Host entry point with Generic Host builder (no .NET Worker Service IHostedService yet)
- [ ] T004 [P] Create base directory structure in `src/agents/ingest/`: `Pipeline/`, `Watcher/`, `Cache/`, `Git/`, `Conversation/`, `Hub/`, `Models/`, `Api/`
- [ ] T005 [P] Extend `src/backend/Grimoire.Api/Channels/Ingest/` channel folder structure with subdirectories: `Endpoints/`, `Services/`, `Models/`, `Persistence/`
- [ ] T006 [P] Create frontend component scaffold in `src/frontend/src/components/ingest/`: `UploadForm.svelte`, `TriggerButton.svelte`, `StatusFeed.svelte`, `FeedbackDialog.svelte`, `ConversationPanel.svelte`, `BatchSummary.svelte`
- [ ] T007 Add `Anthropic.SDK`, `LibGit2Sharp`, `System.Data.SQLite` NuGet packages to ingest agent project via `dotnet add package`
- [ ] T008 [P] Initialize SQLite schema files: `src/agents/ingest/Cache/schema.sql` for IngestRecords, ConversationTurns, FeedbackRequests

**Checkpoint**: Project structure in place; projects compile; no functionality yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that ALL user stories depend on

⚠️ **CRITICAL**: No user story work begins until this phase is complete.

- [X] T009 Implement `IngestAgentClient` in `src/backend/Grimoire.Api/Channels/Ingest/Services/IngestAgentClient.cs`: Typed HttpClient with methods for `TriggerRunAsync()`, `GetRunStatusAsync()`, `SubmitFeedbackAsync()`, `SubmitConversationTurnAsync()`; read agent URL from config/env
- [X] T010 Create `IngestHub` SignalR hub in `src/backend/Grimoire.Api/Channels/Ingest/Services/IngestHub.cs`: Methods to broadcast `IngestProgressAsync()`, `IngestFeedbackRequestAsync()`, `IngestConversationOpenedAsync()`, `IngestConversationTurnAsync()`, `IngestRunCompletedAsync()`
- [X] T011 Extend Hub's `Program.cs` to register `IngestHub` SignalR hub at `/hubs/ingest` and register `IngestAgentClient` as typed HttpClient
- [X] T012 [P] Create Hub-side data models in `src/backend/Grimoire.Api/Channels/Ingest/Models/`: `IngestRunRecord.cs`, `ConversationTurnRecord.cs` for SQLite persistence
- [X] T013 [P] Extend Hub's `grimoire.db` schema (via `AgentDbInitializer`) with `IngestRuns` and `ConversationTurns` tables per data-model.md
- [X] T014 Implement `IngestRepository` in `src/backend/Grimoire.Api/Channels/Ingest/Persistence/IngestRepository.cs`: CRUD methods for IngestRunRecord and ConversationTurnRecord using SQLite connection from config
- [X] T015 Create `IngestOrchestrationHandler` in `src/backend/Grimoire.Api/Channels/Ingest/Services/IngestOrchestrationHandler.cs`: Receives Hub ingest requests, delegates to `IngestAgentClient`, persists results via `IngestRepository`, broadcasts via `IngestHub`
- [X] T016 Implement agent-side `HubClient` in `src/agents/ingest/Hub/HubClient.cs`: Registers agent with Hub on startup (POST /api/agents), sends heartbeats, reads `INGEST_HUB_URL` from env; graceful fallback to standalone mode
- [X] T017 Implement agent-side `HubReporter` in `src/agents/ingest/Hub/HubReporter.cs`: POSTs progress updates, feedback requests, conversation events to Hub callback endpoints
- [X] T018 [P] Create agent-side models in `src/agents/ingest/Models/`: `IngestRecord.cs`, `IngestRun.cs`, `FeedbackRequest.cs`, `FeedbackResponse.cs`, `IngestConversation.cs` per data-model.md
- [X] T019 [P] Implement `IngestCacheRepository` in `src/agents/ingest/Cache/IngestCacheRepository.cs`: SQLite CRUD for IngestRecords, ConversationTurns, FeedbackRequests in `ingest-cache.db`
- [X] T020 Implement `IngestGitService` in `src/agents/ingest/Git/IngestGitService.cs`: Accepts file paths, stages via LibGit2Sharp, commits with formatted message per spec (file count, chunk count, ISO 8601 timestamp), reads Git config from env
- [X] T021 Set up agent-side OpenTelemetry in `src/agents/ingest/Program.cs`: Register `ActivitySource` for `Grimoire.Ingest`, configure meter for metrics; integrate logging/tracing in DI

**Checkpoint**: Hub-agent communication fully wired; database schema ready; bidirectional progress reporting working; agent can register/heartbeat/receive callbacks.

---

## Phase 3: User Story 1 — Automated Ingest of New Raw Sources (Priority: P1) 🎯 MVP

**Goal**: Autonomous file detection, SHA256 caching, LLM-driven processing, auto-commit. No UI required.

**Independent Test**: Drop a file in `raw/sources/`, verify Git commit created within 30s, verify `IngestRecord` in cache with status `Processed`, verify no reprocessing on second run (cache hit).

### Implementation for User Story 1

- [X] T022 Implement `SourceWatcher` in `src/agents/ingest/Watcher/SourceWatcher.cs`: Wraps `FileSystemWatcher`, monitors `raw/sources/` (env-configurable), debounces events (300ms), detects new/modified files (FR-001)
- [X] T023 Implement `Chunker` in `src/agents/ingest/Pipeline/Chunker.cs`: Reads file content, splits by Markdown headings (H1/H2), falls back to paragraphs, caps at 3000 chars per chunk; outputs list of chunks with metadata
- [X] T024 Implement `LlmAnalyzer` in `src/agents/ingest/Pipeline/LlmAnalyzer.cs`: Takes chunk + document path, calls Anthropic SDK with structured output schema (topics, entities, key_claims, summary), handles rate limiting with exponential backoff (3 retries), logs via OpenTelemetry (FR-003)
- [X] T025 Implement `Indexer` in `src/agents/ingest/Pipeline/Indexer.cs`: Assembles chunk analyses into Markdown document, writes YAML frontmatter (source, ingested_at, topics, entities, content_type, chunk_count), outputs to `wiki/{relative_path_without_extension}.md` (FR-003)
- [X] T026 Implement `IngestPipeline` in `src/agents/ingest/Pipeline/IngestPipeline.cs`: Orchestrates Chunker → LlmAnalyzer (per chunk) → Indexer; emits OpenTelemetry spans per stage; handles errors gracefully (FR-003)
- [X] T027 Implement `IngestCache.cs` in `src/agents/ingest/Cache/IngestCache.cs`: SHA256-based lookup logic, checks `IngestCacheRepository` for processed entries, returns `cached` or `needs_processing` (FR-002)
- [X] T028 Implement ingest run orchestration in `src/agents/ingest/Program.cs` or a new `IngestService` class: Watches for files via `SourceWatcher`, for each file: compute SHA256, check cache, if missed: run pipeline, persist `IngestRecord` with status `Processed`, on error: persist status `Failed`, run via `IngestGitService.CommitAsync()` after batch (FR-004, FR-005)
- [X] T029 Add file watcher setup to agent's `Program.cs`: Register `SourceWatcher` as `IHostedService`, start watching on startup, ensure `raw/sources/` exists or create it (edge case per spec)
- [X] T030 [P] Implement agent HTTP endpoints for US1 support in `src/agents/ingest/Api/`: `TriggerRunEndpoint.cs` (POST /ingest/runs), `HealthEndpoint.cs` (GET /health)
- [X] T031 Add OpenTelemetry instrumentation for US1: Log events `ingest.run_started`, `ingest.file_detected`, `ingest.file_processed`, `ingest.file_failed`, `ingest.git_commit` with mandatory fields per plan

**Checkpoint**: Agent autonomously detects, processes, commits files; cache prevents reprocessing; can run standalone without UI.

---

## Phase 4: User Story 2 — File Upload via Web-UI (Priority: P1)

**Goal**: Browser upload form, file lands in `raw/sources/`, agent picks it up, real-time progress visible.

**Independent Test**: Upload a file via browser, file appears in `raw/sources/` within 1s, `IngestProgress` events broadcast via SignalR within 5s, batch summary visible within 2s of completion.

### Implementation for User Story 2

- [X] T032 Implement `UploadSourceEndpoint` in `src/backend/Grimoire.Api/Channels/Ingest/Endpoints/UploadSourceEndpoint.cs`: Accepts multipart form (files + optional subDirectory), writes to `raw/sources/{subDirectory}/`, returns 202 Accepted with file manifest (FR-014)
- [X] T033 Register upload endpoint in Hub's `Program.cs`: `MapPost("/api/ingest/upload", handler)` (FR-014)
- [X] T034 Implement agent-side progress reporting: Modify `IngestPipeline` to emit periodic `IngestProgress` events (file path, status, chunk count, duration) and POST to Hub callback via `HubReporter` (FR-016)
- [X] T035 Implement Hub-side callback handler in `src/backend/Grimoire.Api/Channels/Ingest/Endpoints/`: Receives agent progress POSTs, persists to `IngestRunRecord` via `IngestRepository`, broadcasts via `IngestHub.IngestProgressAsync()` (FR-016)
- [X] T036 Implement frontend `UploadForm.svelte`: File input, submit button, accept multiple files, POST to `/api/ingest/upload`, return 202 response with file list (FR-014)
- [X] T037 Implement frontend `StatusFeed.svelte`: Connect to SignalR `IngestHub`, listen for `IngestProgress` events, display current file, progress bar, scrollable log stream (updates on each event)
- [X] T038 Implement frontend `BatchSummary.svelte` table component: Display run results (file name, status, chunks, duration) in HTML table, include "Discuss" action button per row (for US5 conversation access)
- [X] T039 [P] Integrate components in frontend: Create `IngestPage.svelte` root component, compose UploadForm + TriggerButton + StatusFeed + BatchSummary; mount at `/` or Vite route
- [X] T040 Implement frontend SignalR service in `src/frontend/src/services/ingestHub.ts`: Initialize HubConnection to `/hubs/ingest`, handle auto-reconnect, manage event subscriptions, export as `ingestHub` singleton
- [X] T041 Add observability: Log events `hub.ingest.upload` on file upload, emit trace spans for file write and agent callback receipt

**Checkpoint**: Browser upload works end-to-end; user sees real-time progress; batch summary displays completion; all P1 features accessible via UI.

---

## Phase 5: User Story 3 — Manual Ingest Trigger (Priority: P2)

**Goal**: Trigger button allows user to manually start run; concurrency guard prevents simultaneous runs.

**Independent Test**: Click Trigger button; verify run starts immediately; verify button disables; click again; verify no second run starts; verify button re-enables after run completes.

### Implementation for User Story 3

- [ ] T042 Implement `TriggerIngestEndpoint` in `src/backend/Grimoire.Api/Channels/Ingest/Endpoints/TriggerIngestEndpoint.cs`: Accepts POST (optional runId), calls `IngestAgentClient.TriggerRunAsync()`, returns 202 with run ID; returns 409 if run already active (FR-015)
- [ ] T043 Register trigger endpoint in Hub's `Program.cs`: `MapPost("/api/ingest/trigger", handler)` (FR-015)
- [ ] T044 Implement concurrency guard in agent: Add `_activeRun` field to track current run, return 409 Conflict from `TriggerRunEndpoint` if `_activeRun != null`, set `_activeRun = runId` on start, clear on completion (FR-015)
- [ ] T045 Implement frontend `TriggerButton.svelte`: Button text "Trigger", disabled while run in progress, enabled after completion; onClick calls `POST /api/ingest/trigger`; listen to SignalR `IngestRunStarted` and `IngestRunCompleted` events to control disabled state
- [ ] T046 Wire trigger button to status feed: Clicking Trigger should emit `IngestRunStarted` event and start showing progress; concurrent trigger attempts should do nothing (button disabled enforces this on client)

**Checkpoint**: Manual trigger works; concurrent run protection verified; UI button state correctly reflects run progress.

---

## Phase 6: User Story 4 — Interactive Feedback for Ambiguous Files (Priority: P2)

**Goal**: Agent detects ambiguous files, requests user feedback (process/skip/tag), waits for response, resumes with user's decision; decision cached for future runs.

**Independent Test**: Place file with unknown extension in `raw/sources/`, verify feedback dialog appears in UI within 3s, select "tag" option, enter "markdown", submit; verify agent resumes processing, verify future run applies same decision without prompting.

### Implementation for User Story 4

- [ ] T047 Implement ambiguity detection in agent's `IngestPipeline.cs`: Check file extension against known list, check file size vs threshold (env-configurable, default 10 MB), return ambiguity reason (UnknownFormat/Oversized/MissingMetadata); if ambiguous, create `FeedbackRequest` instead of processing (FR-007)
- [ ] T048 Implement agent-side feedback handler in `src/agents/ingest/Api/FeedbackEndpoint.cs` (POST /ingest/runs/{id}/feedback): Accepts FeedbackResponse (requestId, filePath, action, tagValue), validates action, persists to cache via `IngestCacheRepository` (sets FeedbackAction + FeedbackTag on `IngestRecord`), resumes blocked pipeline (FR-008)
- [ ] T049 Implement agent-side feedback reporting: When `FeedbackRequest` is created, POST to Hub callback via `HubReporter` with file path + reason + options (FR-007)
- [ ] T050 Implement Hub-side feedback relay in `src/backend/Grimoire.Api/Channels/Ingest/Endpoints/ConversationEndpoint.cs`: `POST /api/ingest/runs/{runId}/feedback` accepts FeedbackResponse, forwards to agent, persists to Hub SQLite if needed
- [ ] T051 Implement frontend `FeedbackDialog.svelte`: Modal/inline dialog, displays file name + reason (UnknownFormat/Oversized/MissingMetadata) + radio options (Process/Skip/Tag), conditionally shows text input if Tag selected, submit button calls `POST /api/ingest/runs/{runId}/feedback`
- [ ] T052 Integrate feedback dialog with status feed: Listen to SignalR `IngestFeedbackRequest` event, display dialog, wait for user response, send via Hub endpoint
- [ ] T053 Implement cache lookup on subsequent runs: Before requesting feedback, check `IngestRecord.FeedbackAction` in cache; if set, apply cached decision automatically without prompting user again (FR-008)
- [ ] T054 Add feedback-related observability: Log events `ingest.feedback_requested` (file, reason), `ingest.feedback_received` (file, action); emit metrics `grimoire.ingest.feedback_requests_total` (labels: reason)

**Checkpoint**: Ambiguous files trigger feedback dialogs; user decisions are cached; second run applies cached decision without prompting; feedback flow end-to-end verified.

---

## Phase 7: User Story 5 — Post-Ingest Conversation (Priority: P2)

**Goal**: After processing file, agent opens conversation panel with document summary; user can ask questions grounded in document content; agent responds using LLM; user can submit corrections; corrections are persisted.

**Independent Test**: Process a document, verify conversation panel opens with opening message within 5s, type a question, receive response grounded in document, submit correction (e.g., "tag: quantum-computing"), verify correction is recorded and visible in batch summary without re-run.

### Implementation for User Story 5

- [ ] T055 Implement `ConversationService` in `src/agents/ingest/Conversation/ConversationService.cs`: After `Indexer` completes, create `IngestConversation` with opening message from LLM (summary + topics + invitation); store in `IngestCacheRepository` (FR-020)
- [ ] T056 Implement opening message generation: Call LLM with chunks + extracted metadata, structured output prompt: "Summarize this document in 2-3 sentences and list key topics. Invite questions." (FR-020)
- [ ] T057 Implement agent-side conversation endpoint in `src/agents/ingest/Api/ConversationEndpoint.cs` (POST /ingest/conversations/{conversationId}/turns): Accepts user message, validates conversation exists and is not dismissed, calls LLM with conversation history + document context, returns agent response (FR-022)
- [ ] T058 Implement LLM context management in `ConversationService`: Store full document content + chunks + extracted metadata as conversation context; pass to LLM on each turn to ground responses (FR-021)
- [ ] T059 Implement correction handling: When user message contains "tag:" or other correction patterns, extract correction, update `IngestRecord.UserCorrections` in cache (FR-023)
- [ ] T060 Implement conversation reporting: After opening conversation, POST `ConversationOpened` event to Hub callback via `HubReporter` (conversationId, filePath, opening message) (FR-020)
- [ ] T061 Implement Hub-side conversation relay in `src/backend/Grimoire.Api/Channels/Ingest/Endpoints/ConversationEndpoint.cs`: `POST /api/ingest/conversations/{conversationId}/messages` accepts user message, forwards to agent via `IngestAgentClient`, persists turn in `IngestRepository`, broadcasts turn via `IngestHub`
- [ ] T062 Implement frontend `ConversationPanel.svelte`: Modal/side panel, displays opening message, scrollable turn history (agent + user messages), text input for user message, submit button calls Hub endpoint, listen to SignalR `IngestConversationOpened` and `IngestConversationTurn` events
- [ ] T063 Integrate conversation panel with batch summary: Batch summary table "Discuss" button triggers conversation panel to open for that file, re-hydrates turn history from Hub via `GET /api/ingest/conversations/{conversationId}`
- [ ] T064 Implement dismissible conversation: User can close/dismiss panel, listen to dismissal event, set `dismissed_at` timestamp, conversation remains accessible via batch summary (can be re-opened)
- [ ] T065 Add conversation observability: Log events `ingest.conversation_turn` (conversationId, turn_index, role), emit metrics `grimoire.ingest.conversation_turns_total`, emit trace spans `ingest.conversation.turn` + `ingest.llm.respond`
- [ ] T066 Implement standalone CLI mode for conversations: In CLI, after processing file, enter interactive prompt: "Ask a question about this document (or press Enter to skip)", accept stdin, call LLM, display response, repeat until user quits (FR-025)

**Checkpoint**: Post-ingest conversations fully functional; user can ask questions, receive grounded answers, submit corrections; corrections persist; CLI mode supports interactive prompts.

---

## Phase 8: User Story 6 — Batch Summary Review (Priority: P3)

**Goal**: After run completes, display compact table with file results; each row has "Discuss" link to open conversation for that file.

**Independent Test**: Run batch with 3 files, verify summary table displays with columns (file, status, chunks, duration, action), verify "Discuss" button on each processed file row opens conversation panel.

### Implementation for User Story 6

- [ ] T067 Implement `GetIngestRunEndpoint` in `src/backend/Grimoire.Api/Channels/Ingest/Endpoints/GetIngestRunEndpoint.cs`: GET `/api/ingest/runs/{runId}` retrieves `IngestRunRecord` from Hub SQLite, assembles summary object (total, processed, failed, skipped, file-level details)
- [ ] T068 Agent-side: After run completes, compute batch summary stats (total, processed, failed, skipped, per-file details), emit `IngestRunCompleted` event to Hub callback via `HubReporter` (FR-006)
- [ ] T069 Hub-side: Receive `IngestRunCompleted`, persist summary to `IngestRunRecord`, persist to SQLite, broadcast via `IngestHub.IngestRunCompletedAsync()` (FR-006)
- [ ] T070 Frontend: `BatchSummary.svelte` displays table, listen to SignalR `IngestRunCompleted` event, populate table with run summary + per-file rows, add "Discuss" button per processed file row, onClick: fetch conversation details via `GET /api/ingest/conversations/{conversationId}`, open `ConversationPanel.svelte` (FR-018, FR-027)
- [ ] T071 Implement `GetConversationEndpoint` in Hub: GET `/api/ingest/conversations/{conversationId}` retrieves full turn history from `IngestRepository`, returns conversation object with all turns
- [ ] T072 CLI mode batch summary: After run completes, print summary to stdout in tabular format (file, status, chunks, duration); if interactive, prompt user per file: "Discuss [file]? (y/n)"

**Checkpoint**: Batch summary table displays; user can navigate from summary to individual conversations; P3 feature complete; all 6 user stories functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Observability, documentation, validation, refinement

- [ ] T073 Implement full OpenTelemetry instrumentation per plan.md Observability section: All business metrics (counters for files, chunks, LLM calls, conversations; gauges for active run), all structured log events (fields per spec), all trace spans (hierarchy, attributes)
- [ ] T074 [P] Add xUnit integration tests in `src/backend/Grimoire.Api.Tests/`: Test all Hub Ingest endpoints (upload, trigger, feedback, conversation, status) with Testcontainers + in-memory SQLite
- [ ] T075 [P] Add xUnit unit tests for agent pipeline logic in `src/agents/ingest/` (if Grimoire.Ingest.Tests project exists): Chunker, LlmAnalyzer, Indexer classes with mocked LLM
- [ ] T076 [P] Add integration tests for agent HTTP API in `src/agents/ingest/`: Test endpoints (trigger, feedback, conversation, health) with real SQLite
- [ ] T077 Add error handling + validation across agent: File read errors, LLM failures, Git failures, Hub unavailability; ensure graceful degradation (status = Failed, continue processing)
- [ ] T078 Add error handling across Hub: Agent unreachable, database errors, SignalR disconnections; ensure client-side resilience
- [ ] T079 Add logging/tracing to agent Program.cs: INFO logs for major lifecycle events, WARN for recoverable failures, ERROR for unrecoverable failures
- [ ] T080 [P] Document environment variables in README.md for agent: `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL`, `INGEST_SOURCE_DIR`, `INGEST_HUB_URL`, `INGEST_HTTP_PORT`, `INGEST_FILE_SIZE_LIMIT_MB`, `INGEST_GIT_*`
- [ ] T081 [P] Document frontend setup in `src/frontend/README.md`: How to run dev server, where Ingest page is mounted, how to test with agent + Hub
- [ ] T082 [P] Document Hub Ingest domain setup in `src/backend/README.md`: Ingest domain structure, endpoints, registration with IngestAgentClient
- [ ] T083 Run quickstart.md validation: Execute all 7 scenarios (standalone agent, upload via UI, manual trigger, feedback dialog, conversation, batch summary, architecture test), verify each acceptance criteria passes
- [ ] T084 Code cleanup: Remove scaffolding/comments, ensure naming consistency, fix linter warnings, run formatters
- [ ] T085 Security hardening: Scan for hardcoded secrets (API keys), validate all user inputs (file paths, conversation messages), ensure SQL injection prevention in SQLite queries
- [ ] T086 Performance validation: Verify file detection < 30s (SC-001), feedback visible < 3s (SC-007), conversation opens < 5s (SC-010), batch summary < 2s (SC-008); profile hot paths if needed
- [ ] T087 Architecture tests: Run `dotnet test Grimoire.ArchTests` to confirm all ADR-010 constraints pass (no imports from backend, no API key literals)

**Checkpoint**: Full feature complete, tested, documented, observable, validated against quickstart; ready for merge and deployment.

---

## Dependencies & Execution Order

### Phase Dependencies

1. **Phase 0** (Architecture Tests): No dependencies — must be first
2. **Phase 1** (Setup): No dependencies — after Phase 0
3. **Phase 2** (Foundational): Depends on Phase 1 completion — BLOCKS all user stories
4. **Phases 3-8** (User Stories): All depend on Phase 2 completion
   - **P1 stories** (US1, US2): Implement in parallel after Phase 2
   - **P2 stories** (US3, US4, US5): Implement in parallel after US1+US2 complete (or in parallel if sufficient team)
   - **P3 story** (US6): Can start after Phase 2, but typically after US1 complete
5. **Phase 9** (Polish): After all desired user stories complete

### Parallel Opportunities

**Phase 1 (Setup)**: All [P] tasks can run in parallel (different projects/directories)

**Phase 2 (Foundational)**: All [P] tasks can run in parallel (different files/components):
- T010, T012, T013, T018, T019 can run together
- Remaining tasks have dependencies (T014 on T013, etc.)

**Phases 3-8 (User Stories)**:
- US1 can start immediately after Phase 2
- US2 can start immediately after Phase 2 (depends on US1 for `SourceWatcher` existing but not blocking)
- US3, US4, US5, US6 can start after Phase 2; typically wait for US1+US2 to be solid

**Phase 9 (Polish)**:
- All [P] tasks (tests, docs) can run in parallel
- Validation/cleanup tasks depend on features being complete

---

## Implementation Strategy

### MVP First (Recommended)

1. ✅ Complete Phase 0: Architecture tests (Red)
2. ✅ Complete Phase 1: Setup (compiles, no runtime)
3. ✅ Complete Phase 2: Foundational (Hub↔Agent wired, databases ready)
4. ✅ Complete Phase 3: User Story 1 (autonomous processing, Git commits)
5. 🛑 **STOP and VALIDATE**: Test US1 independently per spec acceptance criteria
6. ✅ Complete Phase 4: User Story 2 (Web-UI upload + progress)
7. 🛑 **VALIDATE**: Test US1 + US2 together, no regressions
8. **SHIP v1.0 MVP**: Autonomous + manual file processing with real-time UI feedback

Then:
9. Complete Phases 5-6: US3, US4 (feedback, conversation)
10. Complete Phase 7: US6 (summary)
11. Complete Phase 9: Polish, observability, docs

### Incremental Delivery (Team Mode)

1. **All hands** complete Phase 0-2
2. **Developer A** → Phases 3-4 (US1, US2) in sequence
3. **Developer B** → Phase 5 (US3) after Phase 2
4. **Developer C** → Phases 6-7 (US4, US5, US6) in sequence after Phase 2
5. **All hands** → Phase 9 (Polish, validation, shipping)

Result: Phases 3-7 can overlap; Phase 9 depends on all stories.

---

## Notes

- [P] = parallelizable (different files, no inter-task deps)
- [US1-6] = user story label for traceability
- Tests written FIRST, ensure FAIL before implementation
- Commit after each task or logical group of related tasks
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independent delivery

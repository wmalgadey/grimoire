# Quickstart Validation Guide: Ingest Agent & Web-UI Channel

**Feature**: 004-ingest-agent-webui | **Date**: 2026-06-24

This guide describes how to validate the feature end-to-end once implemented. It is not a development setup guide — see each subproject's README for build and run instructions.

---

## Prerequisites

| Requirement | Check |
|-------------|-------|
| .NET 9 SDK | `dotnet --version` → `9.x.x` |
| Node.js 20+ | `node --version` → `v20.x` |
| `ANTHROPIC_API_KEY` env var set | `echo $ANTHROPIC_API_KEY` → non-empty |
| Git configured with user.name and user.email | `git config user.name` → non-empty |
| Hub backend running | `curl http://localhost:5000/health` → 200 |

---

## Scenario 1: Standalone Agent — Automated File Detection

**Validates**: User Story 1 (P1), FR-001, FR-002, FR-003, FR-004, FR-005, SC-001, SC-002, SC-003

**Setup**:
```bash
cd src/agents/ingest
INGEST_SOURCE_DIR=../../../raw/sources \
ANTHROPIC_API_KEY=$ANTHROPIC_API_KEY \
dotnet run
```

**Test**:
```bash
# Drop a new Markdown file
echo "# Test Document\nThis is a test." > raw/sources/test-$(date +%s).md
```

**Expected within 30 seconds**:
- Agent log line: `ingest.file_processed file_path=raw/sources/test-*.md`
- New file created in `wiki/` with YAML frontmatter containing `source`, `ingested_at`, `topics`
- `git log --oneline -1` shows: `ingest: 1 file(s), N chunks — 2026-06-24T...`
- SQLite record in `ingest-cache.db`: `Status = Processed`, `ChunkCount > 0`

**Cache hit test** (SC-002):
```bash
# Drop the same file again (identical content)
cp raw/sources/test-*.md raw/sources/test-copy.md
cp raw/sources/test-copy.md raw/sources/test-copy.md  # touch same content
```
- Agent log line: `ingest.file_skipped reason=cache_hit` — no reprocessing, no new git commit.

---

## Scenario 2: Web-UI Upload and Real-Time Progress

**Validates**: User Story 2 (P1), FR-014, FR-016, SC-006

**Setup**: Hub running, Agent running with `INGEST_HUB_URL=http://localhost:5000`, frontend dev server running.

**Test**:
1. Open `http://localhost:5173` in a browser, navigate to the Ingest Agent page
2. Click the upload form, select a Markdown or PDF file
3. Submit the form

**Expected**:
- HTTP 202 returned immediately
- File appears in `raw/sources/` within 1 second
- Status feed shows `IngestLogEntry` events within 5 seconds (SC-006)
- Progress bar advances with each `IngestProgress` event
- On completion: `IngestRunCompleted` event triggers batch summary table

---

## Scenario 3: Manual Trigger Button

**Validates**: User Story 3 (P2), FR-015, SC-009

**Setup**: Place 2–3 files manually in `raw/sources/` without the agent running. Then start the agent with Hub connected.

**Test**:
1. On the Ingest page, click **Trigger**
2. Immediately click **Trigger** again

**Expected**:
- First click: Trigger button disables immediately, run starts, status feed activates
- Second click: button remains disabled, no second run starts (SC-009)
- After run completes: button re-enables

---

## Scenario 4: Feedback Dialog for Ambiguous File

**Validates**: User Story 4 (P2), FR-007, FR-008, FR-017, SC-007

**Setup**: Agent and Hub running, frontend open.

**Test**:
```bash
# Create a file with no extension (UnknownFormat trigger)
echo "Some content" > raw/sources/ambiguous-file
```

**Expected within 3 seconds** (SC-007):
- `IngestFeedbackRequest` SignalR event fires
- Inline feedback dialog appears in Web-UI showing:
  - File name: `ambiguous-file`
  - Reason: `UnknownFormat`
  - Options: Process / Skip / Tag

**Select "Tag" and enter "markdown"**:
- `POST /api/ingest/runs/{runId}/feedback` fires with `action=tag, tagValue=markdown`
- Dialog closes, processing resumes
- `IngestRecord` in `ingest-cache.db` has `FeedbackAction=tag, FeedbackTag=markdown`

**Re-run test** (FR-008):
- Drop the same `ambiguous-file` again
- No feedback dialog appears — cache entry applied automatically

---

## Scenario 5: Post-Ingest Conversation

**Validates**: User Story 5 (P2), FR-020, FR-021, FR-022, FR-023, SC-010, SC-011

**Setup**: Agent and Hub running, frontend open.

**Test**:
1. Upload a Markdown document with substantive content
2. Wait for processing to complete

**Expected within 5 seconds of processing** (SC-010):
- `IngestConversationOpened` SignalR event fires
- Conversation panel opens with an opening message containing:
  - Brief document summary
  - Key topics identified
  - Explicit invitation to ask questions

**Multi-turn test**:
1. Type a question about the document content and submit
2. Agent responds (grounded in the document)
3. Submit a correction: "Add the tag 'my-tag' to this document"
4. Agent acknowledges and records the correction

**Verify correction** (SC-011):
- `IngestRecord.UserCorrections` in `ingest-cache.db` contains the submitted tag
- Batch summary row for this file reflects the correction without requiring a re-run

**Dismiss test** (SC-012):
1. Close/dismiss the conversation panel
2. Navigate to the batch summary
3. Click "Discuss" for the processed file
4. Conversation re-opens with full turn history intact

---

## Scenario 6: Agent in Standalone Mode (Hub Unavailable)

**Validates**: FR-013, SC-005

**Test**:
```bash
# Start agent without Hub URL
INGEST_SOURCE_DIR=../../../raw/sources \
ANTHROPIC_API_KEY=$ANTHROPIC_API_KEY \
dotnet run
```

**Expected**:
- Agent log line: `ingest.hub_unavailable` (WARN) — agent continues without Hub
- File watching and processing work normally
- After dropping a file: pipeline runs, git commit created, IngestRecord written
- CLI interactive prompt appears after each processed file (FR-025)

---

## Scenario 7: Architecture Test Gate

**Validates**: Constitution Principle III, ADR-010

```bash
cd src/backend
dotnet test Grimoire.ArchTests/ --filter "Category=ArchTests"
```

**Expected**: Test verifying `src/agents/ingest/` has no imports from `src/backend/` passes. Test verifying no `ANTHROPIC_API_KEY` literal in source passes.

---

## Contract Reference

- Agent HTTP API: [contracts/agent-http-api.md](contracts/agent-http-api.md)
- Hub Ingest API: [contracts/hub-ingest-api.md](contracts/hub-ingest-api.md)
- SignalR Events: [contracts/signalr-events.md](contracts/signalr-events.md)
- Data Model: [data-model.md](data-model.md)

# Data Model: Ingest Agent & Web-UI Channel

**Feature**: 004-ingest-agent-webui | **Date**: 2026-06-24

---

## Agent-Side Entities (persisted in `ingest-cache.db`)

### IngestRecord

Durable cache entry for a single processed file. Primary lookup key is `FilePath`.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `FilePath` | `string` | PK, NOT NULL | Repository-relative path, e.g., `raw/sources/research/paper.md` |
| `Sha256` | `string` | NOT NULL, 64 hex chars | SHA256 hash of file content at last processing |
| `Status` | `IngestStatus` | NOT NULL | `Processed`, `Failed`, or `Skipped` |
| `ProcessedAt` | `DateTimeOffset` | NOT NULL | UTC timestamp of last processing attempt |
| `ChunkCount` | `int` | NOT NULL, ≥ 0 | Number of chunks produced (0 if failed/skipped) |
| `ErrorMessage` | `string?` | nullable | Error detail if `Status = Failed` |
| `UserCorrections` | `string?` | nullable, JSON blob | Corrections submitted via IngestConversation |
| `FeedbackAction` | `string?` | nullable | Persisted Feedback Response action (`process`/`skip`/`tag`) |
| `FeedbackTag` | `string?` | nullable | Tag value if `FeedbackAction = tag` |

**State transitions**:
```
(new file) → Processed | Failed | Skipped
Failed → Processed | Failed          (on re-run after fix)
Skipped → Processed                  (on re-run after FeedbackResponse = process)
```

**Validation**:
- `FilePath` must not be empty; must not contain `..` (path traversal guard)
- `Sha256` must match pattern `[0-9a-f]{64}`
- `ChunkCount` must be ≥ 0

---

### IngestRun

Aggregate record for a single batch cycle. Created when a run starts; updated as files are processed.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `RunId` | `string` | PK, UUID format | Unique identifier for this run |
| `StartedAt` | `DateTimeOffset` | NOT NULL | UTC start time |
| `CompletedAt` | `DateTimeOffset?` | nullable | UTC end time; null while in progress |
| `Status` | `RunStatus` | NOT NULL | `Running`, `Completed`, `Failed` |
| `TotalFiles` | `int` | NOT NULL, ≥ 0 | Files evaluated |
| `ProcessedCount` | `int` | NOT NULL, ≥ 0 | Files successfully processed |
| `FailedCount` | `int` | NOT NULL, ≥ 0 | Files that failed |
| `SkippedCount` | `int` | NOT NULL, ≥ 0 | Files skipped (cache hit or user skip) |
| `TotalChunks` | `int` | NOT NULL, ≥ 0 | Sum of all chunk counts in this run |

---

### IngestConversation

One conversation per successfully processed file. Holds the turn history in-memory during the session; a lightweight record is persisted for re-opening.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `ConversationId` | `string` | PK, UUID format | Unique conversation identifier |
| `FilePath` | `string` | NOT NULL, FK → IngestRecord | The processed document this conversation is about |
| `RunId` | `string` | NOT NULL | The ingest run that produced this conversation |
| `OpeningMessage` | `string` | NOT NULL | Agent-generated summary and invitation |
| `CreatedAt` | `DateTimeOffset` | NOT NULL | UTC time conversation was opened |
| `DismissedAt` | `DateTimeOffset?` | nullable | UTC time user dismissed; null if active |

**Conversation turns** (child, ordered list):

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `ConversationId` | `string` | FK, part of PK | Parent conversation |
| `TurnIndex` | `int` | part of PK, ≥ 0 | Zero-based ordering |
| `Role` | `TurnRole` | NOT NULL | `Agent` or `User` |
| `Message` | `string` | NOT NULL | Turn content |
| `CreatedAt` | `DateTimeOffset` | NOT NULL | UTC time of turn |

**Conversation context** (in-memory only):
- Full document content (raw text, all chunks)
- Extracted metadata from LLM analysis
- All prior turns (used as message history for Claude SDK calls)

**Invariants**:
- Turn 0 is always `Role = Agent` (the opening message)
- Turns alternate `Agent → User → Agent → User …`
- A dismissed conversation MUST NOT accept new turns (returns 409)

---

### FeedbackRequest

Created when the pipeline encounters an ambiguous file. Persisted until a `FeedbackResponse` is received.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `RequestId` | `string` | PK, UUID format | Unique identifier |
| `RunId` | `string` | NOT NULL | The run that raised this request |
| `FilePath` | `string` | NOT NULL | The ambiguous file |
| `Reason` | `FeedbackReason` | NOT NULL | `UnknownFormat`, `Oversized`, or `MissingMetadata` |
| `RaisedAt` | `DateTimeOffset` | NOT NULL | UTC time request was raised |
| `ResolvedAt` | `DateTimeOffset?` | nullable | UTC time response was received |

### FeedbackResponse

User decision for a `FeedbackRequest`. Written to `IngestRecord` cache on receipt.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `RequestId` | `string` | FK → FeedbackRequest | The resolved request |
| `FilePath` | `string` | NOT NULL | The file being decided |
| `Action` | `FeedbackAction` | NOT NULL | `Process`, `Skip`, or `Tag` |
| `TagValue` | `string?` | nullable | Required when `Action = Tag` |
| `DecidedAt` | `DateTimeOffset` | NOT NULL | UTC time of user decision |

---

## Hub-Side Entities (persisted in `grimoire.db` — new tables)

### IngestRunRecord

Hub's operational record of a run, used to serve status queries and batch summary.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `RunId` | `string` | PK | Mirrors agent's `IngestRun.RunId` |
| `Status` | `string` | NOT NULL | `Running`, `Completed`, `Failed` |
| `StartedAt` | `string` (ISO 8601) | NOT NULL | UTC start time |
| `CompletedAt` | `string?` | nullable | UTC end time |
| `Summary` | `string?` | nullable, JSON blob | Batch summary payload |

### ConversationTurnRecord

Hub's durable turn history, used to re-open conversations.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `ConversationId` | `string` | part of PK | Conversation identifier |
| `TurnIndex` | `int` | part of PK | Zero-based ordering |
| `FilePath` | `string` | NOT NULL | Source document |
| `Role` | `string` | NOT NULL | `agent` or `user` |
| `Message` | `string` | NOT NULL | Turn content |
| `CreatedAt` | `string` | NOT NULL | ISO 8601 UTC |

---

## Enumerations

```
IngestStatus   : Processed | Failed | Skipped
RunStatus      : Running | Completed | Failed
FeedbackReason : UnknownFormat | Oversized | MissingMetadata
FeedbackAction : Process | Skip | Tag
TurnRole       : Agent | User
```

---

## Domain Output (Git + Markdown — `wiki/`)

Each successfully processed file produces one Markdown file in `wiki/`:

```markdown
---
title: "Extracted document title"
source: "raw/sources/research/paper.md"
ingested_at: "2026-06-24T10:30:00Z"
topics:
  - quantum-computing
  - error-correction
entities:
  - name: "Shor's Algorithm"
    type: algorithm
content_type: "research-paper"
chunk_count: 12
---

## Summary

[Agent-generated document summary]

## Key Claims

- [Extracted key claim 1]
- [Extracted key claim 2]

## Topics

[Structured topic sections from chunk analysis]
```

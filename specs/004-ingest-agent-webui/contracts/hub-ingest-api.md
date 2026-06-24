# Contract: Hub Ingest Domain API

**Base URL**: `http://localhost:5000/api/ingest` (Hub — port per appsettings)
**Auth**: None (v1 — trusted local/dev network, FR-019)

---

## POST /api/ingest/upload

Upload one or more files to `raw/sources/`. Files trigger the file watcher automatically.

**Request**: `multipart/form-data`
- Field `files`: one or more file parts
- Field `subDirectory` (optional): subdirectory under `raw/sources/` to place files in (e.g., `"research"`)

**Response 202 Accepted**:
```json
{
  "accepted": [
    { "fileName": "paper.md", "destination": "raw/sources/research/paper.md" }
  ],
  "rejected": []
}
```

**Response 400 Bad Request**: no files provided, or file exceeds 100 MB limit.
```json
{
  "error": "FileTooLarge",
  "message": "File 'large.pdf' exceeds the 100 MB upload limit.",
  "fileName": "large.pdf"
}
```

---

## POST /api/ingest/trigger

Manually trigger an ingest run. Generates a new `runId` if not provided.

**Request body**:
```json
{
  "runId": "optional-uuid-or-omit-for-auto-generated"
}
```

**Response 202 Accepted**:
```json
{
  "runId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Running",
  "startedAt": "2026-06-24T10:30:00Z"
}
```

**Response 409 Conflict**: a run is already active.
```json
{
  "error": "RunAlreadyActive",
  "message": "An ingest run is already in progress.",
  "activeRunId": "existing-run-id"
}
```

**Response 503 Service Unavailable**: agent unreachable.

---

## GET /api/ingest/runs/{runId}

Get status and summary of a run stored in Hub's SQLite.

**Response 200 OK**:
```json
{
  "runId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Completed",
  "startedAt": "2026-06-24T10:30:00Z",
  "completedAt": "2026-06-24T10:31:45Z",
  "summary": {
    "totalFiles": 5,
    "processedCount": 4,
    "failedCount": 1,
    "skippedCount": 0,
    "totalChunks": 48,
    "files": [
      {
        "filePath": "raw/sources/paper.md",
        "status": "Processed",
        "chunkCount": 12,
        "durationMs": 3200,
        "conversationId": "conv-uuid"
      }
    ]
  }
}
```

**Response 404 Not Found**: runId unknown.

---

## POST /api/ingest/runs/{runId}/feedback

Forward a user Feedback Response to the agent. Hub relays and records.

**Request body**: same as `POST /ingest/runs/{runId}/feedback` in the Agent API contract.

**Response 200 OK**: mirrors agent response.
**Response 404/409**: forwarded from agent or Hub record not found.

---

## POST /api/ingest/conversations/{conversationId}/messages

Forward a user message to the agent's conversation endpoint. Hub relays and persists the turn.

**Request body**:
```json
{
  "message": "What are the key arguments in section 3?"
}
```

**Response 200 OK**: mirrors agent conversation turn response.
**Response 404**: conversationId unknown in Hub records.
**Response 409**: conversation dismissed.

---

## GET /api/ingest/conversations/{conversationId}

Retrieve full conversation history from Hub's SQLite (for re-opening a dismissed conversation).

**Response 200 OK**:
```json
{
  "conversationId": "conv-uuid",
  "filePath": "raw/sources/paper.md",
  "runId": "run-uuid",
  "createdAt": "2026-06-24T10:31:45Z",
  "dismissedAt": null,
  "turns": [
    { "turnIndex": 0, "role": "agent", "message": "I've processed paper.md...", "createdAt": "..." },
    { "turnIndex": 1, "role": "user", "message": "What are the key arguments?", "createdAt": "..." },
    { "turnIndex": 2, "role": "agent", "message": "Section 3 argues...", "createdAt": "..." }
  ]
}
```

# Contract: Ingest Agent HTTP API

**Base URL**: `http://localhost:{INGEST_HTTP_PORT}` (default port: `5100`)
**Auth**: None (trusted internal network only — Hub is the sole caller in production)

---

## POST /ingest/runs

Trigger a new ingest run. Returns 409 if a run is already in progress.

**Request body**:
```json
{
  "runId": "550e8400-e29b-41d4-a716-446655440000"
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

**Response 409 Conflict** (run already active):
```json
{
  "error": "RunAlreadyActive",
  "message": "An ingest run is already in progress.",
  "activeRunId": "existing-run-id"
}
```

---

## GET /ingest/runs/{runId}

Get current status of a run.

**Response 200 OK**:
```json
{
  "runId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Completed",
  "startedAt": "2026-06-24T10:30:00Z",
  "completedAt": "2026-06-24T10:31:45Z",
  "totalFiles": 5,
  "processedCount": 4,
  "failedCount": 1,
  "skippedCount": 0,
  "totalChunks": 48
}
```

**Response 404 Not Found**: run ID unknown.

---

## POST /ingest/runs/{runId}/feedback

Submit a user Feedback Response to unblock the pipeline from a pending FeedbackRequest.

**Request body**:
```json
{
  "requestId": "req-uuid",
  "filePath": "raw/sources/doc.unknown",
  "action": "tag",
  "tagValue": "markdown"
}
```
`action` must be one of: `"process"`, `"skip"`, `"tag"`.
`tagValue` is required when `action = "tag"`.

**Response 200 OK**:
```json
{
  "requestId": "req-uuid",
  "filePath": "raw/sources/doc.unknown",
  "action": "tag",
  "decidedAt": "2026-06-24T10:31:00Z"
}
```

**Response 404 Not Found**: requestId unknown or already resolved.
**Response 409 Conflict**: feedback for this request already received.

---

## POST /ingest/conversations/{conversationId}/turns

Submit a user message in an active IngestConversation. Agent calls Claude SDK and returns the response synchronously.

**Request body**:
```json
{
  "message": "What are the key arguments in section 3?"
}
```

**Response 200 OK**:
```json
{
  "conversationId": "conv-uuid",
  "turnIndex": 2,
  "role": "agent",
  "message": "Section 3 argues that quantum error correction...",
  "createdAt": "2026-06-24T10:32:00Z"
}
```

**Response 404 Not Found**: conversationId unknown.
**Response 409 Conflict**: conversation has been dismissed.
**Response 503 Service Unavailable**: Claude SDK call failed; retry after `Retry-After` header.

---

## GET /health

Standard health check. Returns 200 if the agent is ready to accept work.

**Response 200 OK**:
```json
{
  "status": "Healthy",
  "agentId": "ingest",
  "version": "1.0.0",
  "checkedAt": "2026-06-24T10:30:00Z",
  "activeRun": null,
  "hubConnected": true
}
```

`activeRun` is the current `runId` string or `null` if idle.
`hubConnected` is `false` in standalone mode.

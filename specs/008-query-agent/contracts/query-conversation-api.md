# Contract: Query Conversation API and Realtime Events

HTTP contract between the Web UI and the Hub for Query Turn submission/interruption,
and the SignalR contract for streamed answers and turn-state changes. No conversation
is created or looked up server-side (research.md R6) — `conversationId` is a client-
generated opaque string echoed back on every call, used only to label the turns and
their Query Run Artifacts.

## POST /api/query-conversations/{conversationId}/turns

Submit one Query Turn. Non-blocking: returns as soon as the turn is accepted and
dispatched (or rejected), analogous to `IngestRunCoordinator.EnqueueAsync` but without
a queue.

### Request

```json
{
  "prompt": "How does the credential-scoping ADR relate to the runtime-path ADR?",
  "priorTurns": [
    { "position": 1, "prompt": "What does ADR-004 decide?", "answer": "ADR-004 decides...", "state": "completed" }
  ]
}
```

- `prompt`: required, non-empty after trim, ≤ max length (FR-004; same validation
  pattern as the Ingest User Prompt limit — client-side pre-check + server-side
  re-validation).
- `priorTurns`: optional, empty/absent for the conversation's first turn. Each entry
  mirrors data-model.md's `QueryTurn` client view, including partial `answer` text and
  `state: "interrupted"` entries (FR-009).

### Response (202 Accepted)

```json
{
  "turnId": "2026-07-23-query-a1b2c3",
  "conversationId": "c-9f8e7d",
  "position": 2,
  "state": "running",
  "acceptedAt": "2026-07-23T09:00:00Z"
}
```

### Error responses

- `400 Bad Request`: empty/whitespace-only prompt, or prompt exceeds max length (FR-004)
  — no turn is created.
- `409 Conflict`: the conversation already has an active turn (FR-008) — the client is
  expected to prevent this via UI state, this is the defensive server-side guard.
- `503 Service Unavailable`: `QueryConcurrencyLimit` reached — a clear "busy" message,
  body `{ "reason": "query_concurrency_limit_reached" }` (FR-017, spec edge case: "a
  submission beyond that limit is rejected immediately with a clear busy message rather
  than silently queued").

## POST /api/query-turns/{turnId}/interrupt

Interrupt an in-progress turn (FR-006).

### Response (200 OK)

```json
{ "turnId": "2026-07-23-query-a1b2c3", "state": "interrupted" }
```

### Response when already terminal (200 OK, no-op)

```json
{ "turnId": "2026-07-23-query-a1b2c3", "state": "completed" }
```

Interrupting an already-terminal turn is not an error (FR-007, spec edge case:
"nothing breaks — the control is inactive or the action is harmlessly ignored") — the
endpoint returns the turn's actual current state rather than 409/404.

## GET /api/query-turns/{turnId}

Current authoritative state of one turn — used on reconnect (spec edge case: "after
reconnection the UI shows the turn's current authoritative state ... without a page
reload") and for deep-linking to a Query Run Artifact.

### Response (200 OK)

```json
{
  "turnId": "2026-07-23-query-a1b2c3",
  "conversationId": "c-9f8e7d",
  "position": 2,
  "prompt": "How does the credential-scoping ADR relate to the runtime-path ADR?",
  "answer": "ADR-004 scopes the API key to the Ingest child process's environment...",
  "state": "completed",
  "failureReason": null
}
```

## SignalR: `QueryLifecycleHub`

- Hub route: `/hubs/query-lifecycle`
- Broadcast-only (no server-invokable methods), same shape as `IngestLifecycleHub`.

### `queryAnswerChunk`

```json
{ "turnId": "2026-07-23-query-a1b2c3", "sequence": 4, "text": "credential-scoping " }
```

Clients append `text` to the turn's rendered answer in `sequence` order; a gap in
`sequence` after a reconnect is resolved by re-fetching `GET /api/query-turns/{turnId}`
(same "refresh via REST, then resume stream" rule as `ingest-lifecycle-events.md`).

### `queryTurnChanged`

```json
{
  "eventId": "evt_02K1BCDXYZ",
  "turnId": "2026-07-23-query-a1b2c3",
  "fromState": "running",
  "toState": "completed",
  "timestamp": "2026-07-23T09:00:07Z",
  "failureReason": null
}
```

## Rules

- Clients apply `queryTurnChanged` idempotently by `(eventId, turnId)`, same pattern as
  `applyLifecycleEvent` for ingest (latest timestamp per `turnId` is authoritative).
- `queryAnswerChunk` events are applied in `sequence` order per `turnId`; out-of-order
  or duplicate sequences for an already-applied position are ignored.
- On reconnect, the client refreshes the active turn via
  `GET /api/query-turns/{turnId}` before resuming the stream (mirrors the board's
  refresh-then-resume rule).
- `failureReason` is required when `toState = failed`, absent otherwise.

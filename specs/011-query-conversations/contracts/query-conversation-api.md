# Contract: Query Conversation API (revised for record-sourced context)

Revision of `specs/008-query-agent/contracts/query-conversation-api.md` under
ADR-014: the Hub-side Conversation Record is now the source of follow-up context,
so the browser submits only the prompt. Everything not restated here — the
interrupt endpoint, `GET /api/query-turns/{turnId}`, the SignalR
`QueryLifecycleHub` events (`queryAnswerChunk`, `queryTurnChanged`), and all
client-side idempotence/reconnect rules — is **unchanged** from the 008 contract
(FR-009).

## POST /api/query-conversations/{conversationId}/turns

Submit one Query Turn. Non-blocking; unchanged 202 semantics.

### Path parameter (new validation)

`conversationId` MUST match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`. It is still a
client-generated opaque id, but it now names the Conversation Record file, so
path safety is enforced server-side: violation ⇒ `400 Bad Request`
(`{ "message": "conversationId must match ..." }`), no turn created.

### Request (changed)

```json
{
  "prompt": "And how does that relate to the runtime paths?"
}
```

- `prompt`: required, non-empty after trim, ≤ 8000 characters — unchanged 008
  validation (`QuerySubmissionValidator`).
- `priorTurns`: **removed.** The Hub loads the conversation's prior turns from the
  Conversation Record (in-memory cache, hydrated from the file after a Hub
  restart). An extra `priorTurns` field from a stale client is ignored by JSON
  binding — the record remains authoritative (FR-006).

### Response (202 Accepted — unchanged shape, changed provenance)

```json
{
  "turnId": "2026-07-27-query-a1b2c3",
  "conversationId": "c-9f8e7d",
  "position": 2,
  "state": "running",
  "acceptedAt": "2026-07-27T09:00:00Z"
}
```

- `position` is now **Hub-assigned**: recorded turn count + 1 (was derived from
  the client's `priorTurns` length).

### Error responses

- `400 Bad Request`: invalid `conversationId` (new), or empty/whitespace/oversized
  prompt (unchanged) — no turn created.
- `409 Conflict`: `{ "reason": "conversation_already_active" }` — the conversation
  already has an active turn. Unchanged, and now load-bearing beyond UX: it is the
  invariant that makes the record complete at every accepting submission (all
  prior turns terminal ⇒ all recorded — research.md R1).
- `503 Service Unavailable`: `{ "reason": "query_concurrency_limit_reached" }` —
  unchanged (FR-009).
- `500 Internal Server Error`: `{ "reason": "conversation_record_unreadable" }` —
  **new**: the conversation's record file exists but cannot be parsed
  (contracts/conversation-record-format.md "Parsing" rule 5). Fail-closed: no
  turn is created, the agent never receives context that cannot be shown to match
  the record. Recovery: start a new conversation. Surfaced through the
  established operational error reporting (`query.conversation.record_load_failed`).

A missing record file is **not** an error: it means a new conversation (empty
prior context), which is exactly the first-turn case.

## Recording guarantee (server-side, informative)

On every terminal transition (`completed`, `interrupted`, `failed` — including
liveness failures and Hub-initiated interruption), the Hub appends the turn to
the Conversation Record before continuing normal lifecycle publishing. An append
failure never changes the turn's outcome, the `queryTurnChanged` broadcast, or
the HTTP responses above; it is reported via
`query.conversation.record_append_failed` (spec edge case "recording failure").

## Unchanged endpoints (for completeness)

- `POST /api/query-turns/{turnId}/interrupt` — 200 with actual state; terminal
  no-op semantics unchanged.
- `GET /api/query-turns/{turnId}` — unchanged response shape; still the
  reconnect/refresh authority for the **active** turn. It is not a conversation
  history API; reading past conversations happens on the record file directly
  (spec Assumptions: no conversation-browser UI).

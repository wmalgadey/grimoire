# Contract: Query Run Events (Hub ↔ Query Agent)

Extends `specs/004-ingest-agent-systemprompt/contracts/agent-run-events.md` (ADR-008)
with one new event type (ADR-011). The envelope, transport (NDJSON on stdout,
human-readable logging on stderr), and Hub liveness-supervision rules are otherwise
identical and are not repeated here — see the 004 contract for `started`, `heartbeat`,
`activity`, `completed`, `failed`.

## New event: `answer_chunk`

Emitted zero or more times per run, whenever the model produces incremental answer
text (data-model.md Streamed Answer). Interleaved with `heartbeat`/`activity` events on
the same stdout stream.

```json
{"type":"answer_chunk","taskId":"t-1","timestamp":"2026-07-23T09:00:03Z","text":"The wiki "}
{"type":"answer_chunk","taskId":"t-1","timestamp":"2026-07-23T09:00:03Z","text":"describes three "}
{"type":"answer_chunk","taskId":"t-1","timestamp":"2026-07-23T09:00:04Z","text":"decisions..."}
```

| Field | Type | Notes |
|---|---|---|
| `type` | `"answer_chunk"` | |
| `taskId` | string | The Query Turn id (field name kept as `taskId` for envelope consistency with the 004 contract) |
| `timestamp` | ISO-8601 | |
| `text` | string | An incremental delta — never the full accumulated answer. Concatenation of all `answer_chunk.text` values in emission order reconstructs the full answer. |

Unknown/malformed lines are still skipped per the existing tolerant-parser rule; an
`answer_chunk` with a missing or empty `text` is simply not appended (no run failure).

## Hub obligations (amends the 004 table for Query)

| Situation | Hub behavior |
|-----------|--------------|
| `answer_chunk` received | Append `text` to the turn's in-memory partial-answer buffer; publish `queryAnswerChunk` on `QueryLifecycleHub` (contracts/query-conversation-api.md) |
| Any event received | Update `lastEventAt` for the turn (same liveness bookkeeping as Ingest) |
| `completed` received | Terminal transition to `completed`; finalize Query Run Artifact with the full buffered answer |
| `failed` received | Terminal transition to `failed`; finalize Query Run Artifact with the buffered partial answer + reason |
| Hub-initiated `Terminate()` (user interrupt) | Terminal transition to `interrupted` (not `failed`); finalize Query Run Artifact with the buffered partial answer — this transition is Hub-initiated, not agent-emitted, so there is no corresponding agent event type |
| No event for `livenessWindowSeconds` (default 60) | Same as Ingest: mark `failed` (liveness reason), terminate leftover process, finalize artifact with buffered partial answer |
| Event for a turn already terminal | Diagnostic only, no state change (FR-007) |

## Streaming source (agent-internal, informative)

The Query agent's `AgentLoop` (shared with Ingest via `Grimoire.AgentRuntime`) invokes
`IModelClient.NextTurnAsync` with a non-null `onTextDelta` callback; `AnthropicModelClient`
uses the Anthropic streaming Messages API and invokes the callback per text delta as
the underlying SSE stream is consumed, which `RunEventEmitter.EmitAnswerChunk` turns
into one `answer_chunk` NDJSON line per delta. This is what makes SC-003 achievable:
the first chunk can reach stdout well before the model turn as a whole completes.

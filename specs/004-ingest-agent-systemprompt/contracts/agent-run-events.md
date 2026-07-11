# Contract: Agent Run Events (Hub ↔ Ingest Agent)

The agent emits newline-delimited JSON (NDJSON) events on **stdout**; the Hub owns the
child process (ADR-002) and consumes the pipe line-by-line. Human-readable agent
logging goes to **stderr**. See research R9–R12 and ADR-008.

## Envelope

Every event is one JSON object on one line:

```json
{"type":"<event type>","taskId":"<task id>","timestamp":"2026-07-11T09:00:00Z", ...}
```

Unknown fields are ignored (forward compatibility). Non-JSON lines and JSON without a
valid `type`/`taskId` are logged as diagnostics and skipped — they never fail the run;
only the liveness window does.

## Event types

### `started`

Emitted once, immediately after the agent has loaded instructions and policy.

```json
{"type":"started","taskId":"t-1","timestamp":"..."}
```

### `heartbeat`

Emitted every 10 seconds (configurable via `--heartbeat-seconds`) by a background
timer, independent of model latency.

```json
{"type":"heartbeat","taskId":"t-1","timestamp":"..."}
```

### `activity`

Emitted on each loop step (model turn completed, tool call dispatched). Loop
mechanics only — never page content or editorial rationale.

```json
{"type":"activity","taskId":"t-1","timestamp":"...",
 "modelTurns":3,"toolCalls":5,
 "toolCallsByName":{"read_file":3,"write_file":2},
 "currentAction":"tool_call:write_file"}
```

`currentAction` ∈ `model_turn` | `tool_call:<tool name>` | `finalizing`.

### `completed`

Emitted once at successful run end, before process exit.

```json
{"type":"completed","taskId":"t-1","timestamp":"...","summary":"<final agent summary verbatim>"}
```

### `failed`

Emitted once when the agent itself fails (cap breach, load failure after start,
rollback), before process exit.

```json
{"type":"failed","taskId":"t-1","timestamp":"...","reason":"<human-readable reason>"}
```

## Hub obligations

| Situation | Hub behavior |
|-----------|--------------|
| Any event received | Update `lastEventAt`; forward relevant state/activity to board & detail via 003 realtime channel |
| `completed` / `failed` received | Terminal transition; stop supervision; advance queue |
| No event for `livenessWindowSeconds` (default 60) | Mark run `failed` (liveness reason), terminate leftover process, advance queue |
| Event for a task already terminal | Record as diagnostic, no state change (FR-022) |
| Process exit without terminal event | No direct transition — silence lets the liveness window fire (single failure authority) |

## Exit code

The process exit code is no longer awaited for run outcome. It remains set by the
agent (0/non-0) for manual CLI invocation and diagnostics only.

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
{"type":"heartbeat","taskId":"t-1","timestamp":"...","progress":42}
```

`progress` (issue #184) is a monotonically increasing counter of harness-observed loop
mechanics — a streamed text delta arriving, a model turn completing, a tool call
dispatched (Constitution Principle V: mechanics only, never content judgment). It proves
the run is *advancing*, not merely that the process has a working timer: a heartbeat's
own arrival is emitted unconditionally by the background timer regardless of whether the
model is responding, so two consecutive heartbeats carrying the same `progress` value
mean nothing happened between them even though the process is alive.

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
| Any event received | Forward relevant state/activity to board & detail via 003 realtime channel |
| `started` / `activity` received | Update `lastEventAt` — these only ever fire as a direct consequence of real loop work, never spontaneously |
| `heartbeat` whose `progress` differs from the last observed value | Update `lastEventAt` |
| `heartbeat` whose `progress` is unchanged from the last one seen | No update — this is the case that used to reset the window and no longer does (issue #184) |
| `completed` / `failed` received | Terminal transition; stop supervision; advance queue |
| No `lastEventAt` update for `livenessWindowSeconds` (default 60) | Mark run `failed` (liveness reason), terminate leftover process, advance queue |
| Event for a task already terminal | Record as diagnostic, no state change (FR-022) |
| Process exit without terminal event | No direct transition — silence lets the liveness window fire (single failure authority) |

Issue #184: before this fix, `lastEventAt` was updated by *any* received event,
including a bare `heartbeat` — which the background timer emits unconditionally whether
or not the model is responding. That made a stalled model turn indistinguishable from a
healthy one: heartbeats alone kept the watchdog silent indefinitely. `started` and
`activity` are unaffected (they only ever fire as a result of genuine loop progress, so
their arrival was never the problem); `heartbeat` now only counts when its `progress`
counter has actually moved since the last one seen (the first `heartbeat` of a run always
counts, establishing the baseline).

## Exit code

The process exit code is no longer awaited for run outcome. It remains set by the
agent (0/non-0) for manual CLI invocation and diagnostics only.

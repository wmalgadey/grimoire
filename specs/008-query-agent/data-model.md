# Data Model: Interactive Wiki Query Process

Entities from spec.md `## Key Entities`, refined with the harness-ownership decisions
from `plan.md`/ADR-011. Client-side (browser) state and Hub-side (harness) state are
distinguished explicitly, since this feature deliberately has no server-side
conversation store (research.md R6).

## Query Conversation *(client-side, ephemeral)*

The context unit for follow-ups. Held entirely in browser session state — never
persisted server-side (spec Assumptions: "session-scoped in the UI, one per window").

| Field | Type | Notes |
|---|---|---|
| `conversationId` | string (client-generated, e.g. UUID) | Scoped to one browser window; regenerated when the user starts a new conversation (FR-010) |
| `createdAt` | timestamp | Client-side, display only |
| `turns` | ordered list of `QueryTurn` (client view) | Includes partial answers of interrupted turns (FR-009) |
| `activeTurnId` | string \| null | Non-null while a turn is `running`; enforces "at most one active turn" (FR-008) client-side |

Sent to the Hub on every turn submission as the `priorTurns` payload (prompt + answer +
state per prior turn) — see `contracts/query-conversation-api.md`. The Hub does not
retain this beyond the lifetime of handling one submission.

## Query Turn *(client view + Hub-side run state)*

One prompt-answer exchange. The client holds a live view (built from submission
response + realtime events); the Hub holds only the state of the currently-running
turn (in `QueryRunCoordinator`'s in-memory tracking) plus the finalized record it
writes to the Query Run Artifact on terminal transition.

| Field | Type | Notes |
|---|---|---|
| `turnId` | string (Hub-generated, e.g. `{date}-query-{guid}`) | Returned on submission accept |
| `conversationId` | string | Echoes the client-supplied conversation id |
| `position` | int | 1-based index within the conversation (client-assigned, echoed back) |
| `prompt` | string | The Query Prompt (validated non-empty, within max length, FR-004) |
| `answer` | string | Accumulated from `answer_chunk` events; possibly partial |
| `state` | `running` \| `completed` \| `interrupted` \| `failed` | Terminal states are final (FR-007) |
| `failureReason` | string \| null | Set only when `state = failed` |

State machine (matches FR-007/FR-008 exactly):

```text
running ──(agent emits completed event)──────────────► completed
running ──(user interrupts / Hub calls Terminate())──► interrupted
running ──(liveness silence beyond window)───────────► failed
running ──(dead-on-arrival: spawn/load failure)──────► failed
completed, interrupted, failed = terminal — no further transition (late signals no-op)
```

## Query System Prompt Document *(file, versioned)*

| Field | Type | Notes |
|---|---|---|
| `path` | string | `agents/query/system-prompt.md` (ADR-009 runtime location) |
| `content` | string | Loaded verbatim as the agent's entire system prompt (FR-003) |
| `sha256` | string | Recorded per run on the Query Run Artifact |

Fail-closed: missing, unreadable, or effectively empty ⇒ the turn fails before any
agent output (SC-001), mirroring `SystemPromptLoader`'s existing Ingest behavior.
No default-user-prompt sibling document exists for Query (research.md R1) — the
Query Prompt itself is always the effective user-turn content.

## Query Run Artifact *(file, Hub-written, one per turn)*

Persistent per-turn record, analogous to the Ingest Task Artifact but **entirely
Hub-written** (the Query agent process has no write capability at all — R3/ADR-011).
Stored at `<base>/data/query-runs/<conversationId>/<turnId>.md` (ADR-009 pattern;
outside `wiki/`, git-ignored, per ADR-003's domain/operational split).

| Field | Type | Notes |
|---|---|---|
| `turnId` | string | Primary identity |
| `conversationId` | string | |
| `position` | int | |
| `prompt` | string | The Query Prompt as submitted |
| `answer` | string | Final (or last-known-partial) answer text |
| `state` | `completed` \| `interrupted` \| `failed` | Terminal state only — artifact is finalized on terminal transition |
| `failureReason` | string \| null | |
| `startedAt` / `completedAt` | timestamp | |
| `instructionFile` | `{ path, sha256 }` | Query System Prompt Document identity (FR-016) |
| `policy` | `{ path, version, sha256 }` | `agents/query/policy.json` identity |
| `deniedActions` | list of `{ tool, requestedTarget, canonicalTarget, reason, turn }` | Every denied tool attempt (FR-012) |
| `model` | string | Model identifier used |
| `turnsUsed` | int | Model-loop turn count |

## Streamed Answer *(transport-level, not persisted independently)*

The progressively delivered answer content of one turn. Not a standalone stored
entity — it is the accumulation of `answer_chunk` events (contracts/query-run-events.md)
into `QueryTurn.answer`, rendered incrementally by the frontend and finalized into the
Query Run Artifact's `answer` field on terminal transition.

| Field | Type | Notes |
|---|---|---|
| `turnId` | string | Attributes the chunk to its turn (FR-016 "answer content MUST be attributable to its turn") |
| `text` | string | Incremental delta, not the full accumulated answer |
| `sequence` | int | Monotonic per turn, lets the client detect gaps after a reconnect |

## Supporting harness records (new, Hub-internal)

### `QueryAgentRequest` (extends the `IAgentProcessLauncher` port's request shape)

| Field | Type | Notes |
|---|---|---|
| `TurnId` | string | |
| `ConversationId` | string | |
| `Prompt` | string | |
| `PriorTurns` | list of `{ prompt, answer, state }` | Client-supplied conversation history (research.md R6) |
| `WikiRoot`, `PagesDir`, `IndexPath`, `LogPath` | string | Same `ContentRootPaths` fields Ingest already uses, for read-scope resolution |
| `SystemPromptPath` | string | `agents/query/system-prompt.md` |
| `PolicyPath` | string | `agents/query/policy.json` |

### `QueryRunActivitySnapshot` (mirrors Ingest's `RunActivitySnapshot`)

| Field | Type | Notes |
|---|---|---|
| `ModelTurns`, `ToolCalls`, `ToolCallsByName`, `CurrentAction`, `LastEventAt` | as Ingest | Loop mechanics only (Principle V) |

## New Agent Run Event type (extends ADR-008's envelope, ADR-011)

### `answer_chunk`

```json
{"type":"answer_chunk","taskId":"t-1","timestamp":"...","text":"<delta>"}
```

Emitted zero or more times during a run, interleaved with the existing `heartbeat`/
`activity` events; full schema in `contracts/query-run-events.md`.

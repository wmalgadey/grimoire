# Data Model: Conversation Records Replace Query-Run Artifacts

Entities from spec.md `## Key Entities`, refined with the decisions in
`research.md` and ADR-014. This feature changes only harness persistence: the
client-side `Query Conversation` / `Query Turn` view, the turn state machine, and
the agent-facing shapes from feature 008's data model stay as documented in
`specs/008-query-agent/data-model.md`, except where superseded below.

## Conversation Record *(file, Hub-written, one per conversation)*

The persistent record of one Query Conversation. Replaces the per-turn Query Run
Artifact (`specs/008-query-agent/data-model.md "Query Run Artifact"`, now retired).
Stored at `<base>/data/conversations/<conversationId>.md` (ADR-009 pattern via
`GrimoirePathOptions.ConversationsDir`, default dir name `conversations`; outside
`wiki/`, git-ignored, per ADR-003's domain/operational split). Append-only: created
when the conversation's first turn reaches a terminal state, extended by one
self-contained block per later terminal turn, earlier bytes never modified
(FR-003). File format grammar: `contracts/conversation-record-format.md`.

### Conversation-level facts (YAML frontmatter, written once at creation)

| Field | Type | Notes |
|---|---|---|
| `conversation_id` | string | Client-generated opaque id, validated `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$` (it names the file) |
| `created_at` | timestamp (ISO-8601) | When the record was created (first terminal turn) |
| `record_format` | string | `grimoire-conversation/1` — parser/version handshake |

### Relationships

- 1 Conversation Record : N Recorded Turns (ordered by `position`).
- Identity: the conversation identity — the record is locatable from
  `conversationId` alone (FR-005).

## Recorded Turn *(one appended block inside the record)*

One terminal turn: Turn Bookkeeping (machine-readable comment block) + Transcript
entry (human-readable prompt/answer sections). Appended exactly once, on the
turn's first (and only) terminal transition — the same `TryTransitionTo`
first-transition-wins point that finalized the old artifact.

### Turn Bookkeeping (inside `<!-- grimoire:turn ... -->`)

Carries every field the retired Query Run Artifact frontmatter carried, plus the
two content-length fields that make the record machine-recoverable:

| Field | Type | Notes |
|---|---|---|
| `turn_id` | string | Hub-generated (unchanged 008 shape) |
| `position` | int | 1-based, **Hub-assigned** = recorded turn count + 1 (was client-derived) |
| `state` | `completed` \| `interrupted` \| `failed` | Terminal only |
| `failure_reason` | string \| null | Human-readable, `state = failed` only |
| `started_at` / `completed_at` | timestamp | From `QueryTurnState` |
| `model` | string \| null | From terminal-event metadata |
| `turns_used` | int \| null | Model-loop turn count |
| `instruction_file` | `{ path, sha256 }` | Query System Prompt identity (nullable pre-load failures) |
| `policy` | `{ path, version, sha256 }` | `agents/query/policy.json` identity |
| `denied_actions` | list of `{ action, requested_target, canonical_target, reason, turn }` | Same shape the artifact recorded (SC-002) |
| `prompt_chars` / `answer_chars` | int | Exact UTF-16 length of the recorded prompt/answer bodies — injection-proof parsing (research.md R2) |

Extensibility: the block is an open YAML mapping; feature 012 adds an optional
`created_pages:` list here without restructuring (ADR-014).

### Transcript entry

| Field | Type | Notes |
|---|---|---|
| `prompt` | string | The Query Prompt as submitted (trimmed, ≤ 8000 chars — unchanged 008 validation) |
| `answer` | string | Full answer as delivered: final text, or last-known-partial for `interrupted`/`failed` turns (accumulated `answer_chunk` buffer) |

## Conversation Context *(Hub-internal, in-memory cache)*

Per-conversation structured view of the recorded turns, owned by
`ConversationRecordStore` (`Grimoire.Hub.QueryConversations`). Maintained on each
append; hydrated by parsing the record file on cache miss (Hub restart); empty
when no record file exists (new conversation).

| Field | Type | Notes |
|---|---|---|
| `conversationId` | string | Cache key |
| `turns` | ordered list of `{ position, prompt, answer, state }` | Exactly the `QueryPriorTurn` shape the launcher port already carries — the agent-facing contract is unchanged (research.md R7) |

Failure semantics: record file unparseable ⇒ submission rejected fail-closed
(`conversation_record_unreadable`), never partial context (FR-006, research.md R5).

## State transitions

The 008 turn state machine is unchanged (`running → completed | interrupted |
failed`, terminal final). The record interacts with it at exactly one point:

```text
QueryTurnState.TryTransitionTo(terminal) == true
    └─► ConversationRecordStore.AppendTurnAsync(...)   (exactly once per turn)
            ├─ file absent  → create record (frontmatter + turn block)
            └─ file present → append turn block
Append failure → logged/counted (query.conversation.record_append_failed),
                 turn outcome and realtime publish unaffected (spec edge case)
```

Because a conversation admits at most one active turn (409 guard), appends are
serialized per conversation; the store adds a per-conversation lock as defense in
depth. At any accepting submission, all prior turns are terminal and therefore
recorded — the context load is complete by construction (research.md R1).

## Retired entity

**Query Run Artifact** (`specs/008-query-agent/data-model.md`): no longer written.
`QueryRunArtifactWriter`, the `Grimoire.Hub.QueryRunArtifact` namespace,
`GrimoirePathOptions.QueryRunsDir` / `DefaultQueryRunsDirName`, and
`ResolvedGrimoirePaths.QueryRunArtifactPathFor` are deleted. Existing files under
`data/query-runs/` are disposable; no migration (FR-008). A structural tripwire
(no production IL literal `query-runs`) guards the retired location (SC-004).

# Data Model: Single Agent System Prompt & Configurable Ingest Submission

**Feature**: `specs/004-ingest-agent-systemprompt` | **Date**: 2026-07-11

## Entities

### System Prompt Document

The single versioned instruction document governing the Ingest agent
(research R1/R2).

| Field | Type | Notes |
|-------|------|-------|
| `path` | file path | `agents/ingest/system-prompt.md`, git-tracked |
| `content` | markdown text | Loaded verbatim as the entire system prompt |
| `sha256` | hex string | Computed at load time, recorded per run |

**Validation**: must exist, be readable, and be non-whitespace — otherwise the run
fails before any wiki write (fail-closed, FR-003).

**Replaces**: `agents/ingest/CLAUDE.md` and
`agents/ingest/skills/wiki-maintenance/SKILL.md` (deleted by this feature).

### Default User Prompt

Versioned default steering text shown in the submission form and used when no custom
prompt is supplied (research R3).

| Field | Type | Notes |
|-------|------|-------|
| `path` | file path | `agents/ingest/default-user-prompt.md`, git-tracked |
| `content` | markdown text | Displayed verbatim in the form; used verbatim as effective prompt |

**Validation**: must exist and be non-whitespace when needed as fallback; missing file
fails the run (agent side) / the defaults endpoint (Hub side) with a human-readable
reason.

### Effective User Prompt

The steering text actually used for one run.

| Field | Type | Notes |
|-------|------|-------|
| `text` | string ≤ 8,000 chars | Custom text from submission, or Default User Prompt content |
| `source` | `default \| custom` | `custom` iff the submission supplied a non-empty prompt |

**Rules**: empty/whitespace submission value ⇒ `default` (FR-007). Oversized value ⇒
submission rejected before task creation (FR-010). The harness scaffold (task id,
source ref, `<source>`-delimited content, injection framing) always wraps this text
and is not part of it (FR-008).

### Convert Step (registry entry)

Harness-owned static registry of named pre-agent processing steps (research R5).

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | `markitdown` (only entry in this feature) |
| `appliesTo` | set of source kinds | `url`, `pdf_file`, `office_file` |
| `requiredFor` | set of source kinds | `pdf_file`, `office_file` |
| `defaultEnabled` | bool | `true` |

### Convert Step Configuration (per submission)

| Field | Type | Notes |
|-------|------|-------|
| `steps` | map `name → enabled` | Absent map / absent key ⇒ default (enabled) |

**Validation** (before task creation):
- Unknown step name ⇒ reject (400).
- `enabled=false` for a step in `requiredFor(sourceKind)` ⇒ reject (422, FR-013).
- Keys for steps not in `appliesTo(sourceKind)` ⇒ reject (400) — configuration must be
  meaningful for the submitted kind.

**Effect**: a disabled applicable step is skipped; for `markitdown` this stores the
received content byte-identical as the normalized artifact (FR-012, SC-004).

### Ingest Submission (extended from 003)

Adds two optional request aspects to 003's submission:

| Field | Type | Notes |
|-------|------|-------|
| `userPrompt` | string?, ≤ 8,000 chars | Optional custom steering text |
| `convertSteps` | map? `name → bool` | Optional per-step overrides |

Absent fields reproduce 003 behavior exactly (FR-015).

### Task Artifact (extended from 001/002/003)

New recorded fields (research R7):

| Location | Field | Value |
|----------|-------|-------|
| frontmatter | `user_prompt_source` | `default \| custom` |
| frontmatter | `convert_steps` | map of applicable steps → `enabled \| disabled` |
| frontmatter | `instruction_files` | existing 002 list shape, now exactly one entry (`system-prompt.md` + sha256) |
| body | `## User Prompt` | Effective prompt text verbatim |

Pre-existing artifacts without these fields remain valid ("defaults of their time",
spec edge case); readers treat absence as default behavior.

### Agent Run Event

Message from the running agent to the Hub over the stdout event channel
(research R9, R12).

| Field | Type | Notes |
|-------|------|-------|
| `type` | `started \| heartbeat \| activity \| completed \| failed` | Event kind |
| `taskId` | string | Correlation to the Task Artifact |
| `timestamp` | ISO-8601 UTC | Emission time (agent clock) |
| `modelTurns` | int? | `activity`: model turns so far |
| `toolCalls` | int? | `activity`: tool calls so far |
| `toolCallsByName` | map? `tool → count` | `activity`: per-tool counts |
| `currentAction` | string? | `activity`: `model_turn`, `tool_call:<tool>`, `finalizing` |
| `summary` | string? | `completed`: final agent summary verbatim |
| `reason` | string? | `failed`: human-readable failure reason |

**Rules**: events carry loop mechanics only — never page content or editorial
rationale (Principle V). Events for tasks already terminal are recorded as
diagnostics and change nothing (FR-022). Malformed/non-JSON lines are logged and
skipped; they do not fail the run (the liveness window governs failure).

### Run Queue (operational state, SQLite)

Persistent FIFO of accepted-but-not-started tasks (research R11).

| Field | Type | Notes |
|-------|------|-------|
| `taskId` | string | Unique per queue row |
| `acceptedAt` | timestamp | FIFO order authority |
| `queuePosition` | int (derived) | Exposed via API/board, not stored |

**Queue-level flag**: `queue_paused` (bool) — set on Hub startup when queued rows
exist; cleared by explicit user resume/re-trigger (FR-021). While paused, the
dispatcher starts nothing.

**Invariant**: at most one task is in `running` at any time; the dispatcher starts
the next queued task only on a terminal transition of the current run (or on
resume/re-trigger while idle).

### Run Supervision (operational state, in-memory + SQLite-backed timestamps)

| Field | Type | Notes |
|-------|------|-------|
| `taskId` | string | The single running task |
| `lastEventAt` | timestamp | Updated on every received event |
| `livenessWindowSeconds` | int | Config, default 60 |

**Rule**: `now - lastEventAt > livenessWindowSeconds` ⇒ run `failed` (liveness
reason), leftover process terminated, queue advanced (FR-020). The liveness window
is the sole failure authority; process exit merely stops events (research R10).

## State transitions

No new lifecycle stages. The 003 lifecycle
(`received → converting → queued → running → completed | failed`) is unchanged;
a disabled `markitdown` step makes `converting` a persist-as-received stage rather
than a conversion (still passes through `converting` so the board contract is
untouched).

Event-driven refinements within existing stages (US4):

- `queued → running`: set by the dispatcher when it starts the agent process
  (non-blocking); confirmed by the agent's `started` event.
- `running → completed`: on `completed` event (summary recorded).
- `running → failed`: on `failed` event, **or** on liveness-window expiry
  (`reason = liveness timeout`), **or** by existing restart reconciliation.
- Post-restart: `queued` tasks stay `queued` with `queue_paused = true` until the
  user resumes/re-triggers (FR-021).

## Relationships

```text
Ingest Submission ──carries──> Effective User Prompt ──recorded on──> Task Artifact
Ingest Submission ──carries──> Convert Step Configuration ──validated against──> Convert Step registry
Convert Step Configuration ──recorded on──> Task Artifact
System Prompt Document ──loaded per run, hash recorded on──> Task Artifact
Default User Prompt ──displayed by──> Submission Form (via defaults endpoint)
Default User Prompt ──fallback for──> Effective User Prompt
Ingest Submission ──enters──> Run Queue ──starts (one at a time)──> Agent Run
Agent Run ──emits──> Agent Run Events ──drive──> Run Supervision + board/detail display
Run Supervision ──on liveness expiry──> Task Artifact (failed) + Run Queue (advance)
```

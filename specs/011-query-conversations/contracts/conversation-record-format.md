# Contract: Conversation Record File Format (`grimoire-conversation/1`)

The on-disk format of a Conversation Record. This is a dual contract: humans read
it as a dialogue top-to-bottom (US1); the Hub parses it back into structured turns
to build the agent's follow-up context (research.md R1/R5). Writer and parser both
live in `Grimoire.Hub.QueryConversations` (`ConversationRecordStore`).

- **Location**: `<base>/data/conversations/<conversationId>.md`
  (`ResolvedGrimoirePaths.ConversationRecordPathFor`).
- **Encoding**: UTF-8, LF or platform newlines as written; lengths are counted in
  UTF-16 code units of the in-memory strings (see Parsing).
- **Lifecycle**: created on the conversation's first terminal turn; extended by
  appending one complete turn block per later terminal turn; existing bytes are
  never modified (FR-003).

## Layout

```markdown
---
conversation_id: c-9f8e7d
created_at: 2026-07-27T09:00:00.0000000+00:00
record_format: grimoire-conversation/1
---

# Conversation c-9f8e7d

<!-- grimoire:turn
turn_id: 2026-07-27-query-a1b2c3d4e5f6a7b8c9d0e1f2
position: 1
state: completed
failure_reason: null
started_at: 2026-07-27T09:00:00.0000000+00:00
completed_at: 2026-07-27T09:00:07.0000000+00:00
model: claude-sonnet-4-5
turns_used: 3
instruction_file:
  path: "agents/query/system-prompt.md"
  sha256: "3b7a…"
policy:
  path: "agents/query/policy.json"
  version: 1
  sha256: "9c2e…"
denied_actions: []
prompt_chars: 26
answer_chars: 213
-->

## Turn 1 — completed

### Prompt

What does ADR-004 decide?

### Answer

ADR-004 decides that the Claude API key is injected only into the agent child
process's environment at spawn…

<!-- grimoire:turn
turn_id: 2026-07-27-query-b2c3d4e5f6a7b8c9d0e1f2a3
position: 2
state: interrupted
failure_reason: null
…
denied_actions:
  - action: read_file
    requested_target: "../secrets/.env"
    canonical_target: "/base/secrets/.env"
    reason: "outside read scope"
    turn: 2
prompt_chars: 41
answer_chars: 87
-->

## Turn 2 — interrupted

### Prompt

And how does that relate to the runtime paths?

### Answer

ADR-009 resolves every runtime location from…
```

## Turn block grammar

Each appended turn block is, in order:

1. A line `<!-- grimoire:turn` opening the bookkeeping comment.
2. A YAML mapping with exactly the Turn Bookkeeping fields of `data-model.md`
   (`turn_id`, `position`, `state`, `failure_reason`, `started_at`,
   `completed_at`, `model`, `turns_used`, `instruction_file`, `policy`,
   `denied_actions`, `prompt_chars`, `answer_chars`). Unknown additional keys MUST
   be tolerated by the parser (forward compatibility; feature 012 adds
   `created_pages`).
3. A closing `-->` line.
4. A blank line, then `## Turn {position} — {state}`.
5. A blank line, then `### Prompt`, a blank line, then the prompt body: exactly
   `prompt_chars` UTF-16 code units, followed by a newline.
6. A blank line, then `### Answer`, a blank line, then the answer body: exactly
   `answer_chars` UTF-16 code units, followed by a newline, then a blank line.

Prompt and answer bodies are recorded **verbatim** (prompt trimmed exactly as
submitted to the agent; answer as accumulated from `answer_chunk` events —
possibly empty for turns that failed before any output; `answer_chars: 0` then
yields an empty body).

## Escaping rules (injection safety)

- **Bodies are length-delimited, not sentinel-delimited.** The parser slices
  prompt/answer bodies by `prompt_chars`/`answer_chars`; body content is never
  scanned for headings or comment markers, so a prompt or answer containing
  `## Turn`, `### Answer`, or `<!-- grimoire:turn -->` cannot break or forge
  structure (LLM output is untrusted; prompt-injection surface).
- **String values inside the bookkeeping comment** (`failure_reason`,
  `requested_target`, `canonical_target`, `reason`, paths) are written as
  double-quoted JSON-escaped strings, and additionally the character sequence
  `-->` is written with its `>` as the JSON unicode escape `\u003e` (so `-->`
  becomes `--\u003e` on disk); the HTML comment can therefore never be
  terminated early by agent-chosen content. The parser applies standard JSON
  unescaping, restoring the original value.

## Parsing (context recovery, Hub restart)

1. Read frontmatter; require `record_format: grimoire-conversation/1` (unknown
   major version ⇒ unreadable).
2. Scan for `<!-- grimoire:turn` openings **outside body ranges**: after each
   bookkeeping block is parsed, the following body ranges are consumed by length,
   and scanning resumes only after the block's trailing newline — guaranteeing
   sentinels inside bodies are never interpreted.
3. Each fully parsed block yields one Recorded Turn
   (`position, prompt, answer, state` for context; full bookkeeping for audit).
4. A trailing incomplete block (crash mid-append) is dropped with a WARN-level
   diagnostic; the file is still readable. This is safe for FR-006: a turn whose
   block half-appended never had its `queryTurnChanged` publish suppressed
   dependents — the recorded prefix is exactly the terminal turns fully recorded.
5. Any other structural violation (bad frontmatter, malformed bookkeeping YAML,
   body shorter than declared length) ⇒ **unreadable**: the context load fails
   closed (`conversation_record_unreadable`, contract in
   `query-conversation-api.md`); the store never returns partial context.

## Invariants

- Exactly one record file per conversation; exactly one block per terminal turn;
  blocks appear in `position` order, `position` strictly increasing from 1
  (SC-001).
- Every denied tool action reported in the turn's terminal metadata appears in
  that turn's `denied_actions` (SC-002).
- The `{ position, prompt, answer, state }` tuples parsed from the record are
  byte-identical to the prior-turn context supplied to the Query agent for the
  conversation's next turn (SC-005) — same store, same data (research.md R1).

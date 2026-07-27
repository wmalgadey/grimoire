---
status: proposed
---

# ADR-014: Query Conversation Records and Record-Sourced Follow-Up Context

## Context and Problem Statement

Feature 008 (query-agent) persisted each Query Turn as its own Hub-written Query Run
Artifact under `<base>/data/query-runs/<conversationId>/<turnId>.md` and deliberately
kept no server-side conversation state: the browser resubmitted the whole conversation
(`priorTurns`) with every follow-up (ADR-011 "Persistence and conversation context",
008 research R6). In practice the per-turn files are not usable as a conversation
history: a follow-up's meaning lives in the answer above it, which sits in a different
file. Feature 011 (`specs/011-query-conversations/spec.md`) replaces this with one
durable **Conversation Record** per conversation — the complete transcript plus
per-turn bookkeeping — and requires (FR-006) that the prior-turn context delivered to
the agent and the recorded transcript never disagree. The spec's Assumptions state
explicitly that this reverses an accepted decision and MUST be settled by a
superseding ADR, including the open question: does the browser keep supplying
`priorTurns` (record = audit copy), or does the record become the context source?

A sibling planning effort (feature 010, ADR-013) is consolidating agent-platform
packaging; that concern is deliberately **not** decided here — this ADR touches
conversation persistence and context transport only. Feature 012 (query synthesis
writes, later wave) will need to record pages created per turn; the record shape must
admit that without restructuring.

## Decision Drivers

- Spec 011 FR-001..FR-005: one human-readable record per conversation, complete
  transcript in order, per-turn bookkeeping preserved (auditability must relocate,
  not regress), durable across browser loss and Hub restarts, locatable by
  conversation identity, storage naming says "conversations".
- Spec 011 FR-006/SC-005: 100% context/record consistency — a deterministic
  guarantee, which Constitution Principle II requires to be structurally enforceable,
  not aspirational.
- Spec 011 FR-007/FR-008/SC-004: per-turn artifact mechanism retired, no migration,
  zero new files in the retired location.
- Spec 011 FR-009: user-facing behavior of feature 008 (streaming, interruption,
  follow-ups, one active turn per conversation, concurrency limit 3) unchanged.
- ADR-009: new runtime locations go through the single path-composition point beneath
  `<base>/data`; ADR-003: operational bookkeeping stays outside `wiki/` and git.
- ADR-012: replay recordings fingerprint the agent's inputs — the agent process
  contract must not drift.
- Constitution I: local-filesystem persistence is port-exempt but containment-bound;
  Principle V: recording is harness mechanics, never content judgment.

## Considered Options

1. **One append-only Conversation Record per conversation, and the record becomes
   the follow-up context source (browser sends only the prompt).**
2. One Conversation Record per conversation as pure audit copy; the browser keeps
   supplying `priorTurns` on each submission (008 mechanism retained).
3. Hybrid: browser supplies `priorTurns`, Hub validates them against the record and
   rejects on mismatch.
4. Server-side conversation store in SQLite (operational DB) with the markdown record
   as a rendered projection.

## Decision Outcome

Chosen option: **Option 1.**

### One record per conversation

- Stored at `<base>/data/conversations/<conversationId>.md` — added via the ADR-009
  composition point (`GrimoirePathOptions.ConversationsDir`,
  `DefaultConversationsDirName = "conversations"`, resolver-reported and
  auto-created); `QueryRunsDir`/`DefaultQueryRunsDirName`/`QueryRunArtifactPathFor`
  are deleted. Operational data: outside `wiki/`, git-ignored (ADR-003).
- Format `grimoire-conversation/1`
  (`specs/011-query-conversations/contracts/conversation-record-format.md`): YAML
  frontmatter with conversation-level facts, then per terminal turn one appended,
  self-contained block — a machine-readable bookkeeping comment
  (`<!-- grimoire:turn ... -->` with state, failure reason, timestamps, instruction
  identity + SHA-256, policy identity/version/SHA-256, denied actions with reasons,
  model, loop turns used) followed by human-readable
  `## Turn N — <state>` / `### Prompt` / `### Answer` sections. The full
  bookkeeping set of the retired per-turn artifact is preserved per turn (spec US2:
  traceability relocates, does not regress).
- **Append-only, never rewritten**: the first terminal turn creates the file; each
  later terminal turn appends one block at the existing first-transition-wins
  terminal point (`QueryTurnState.TryTransitionTo`); earlier bytes are never
  modified. Because a conversation admits at most one active turn, appends are
  naturally serialized per conversation.
- **Machine-recoverable by construction**: prompt/answer bodies are
  length-delimited (`prompt_chars`/`answer_chars` in the bookkeeping block), so
  parsing never scans untrusted LLM output for delimiters — content containing
  headings or sentinel comments (prompt-injection surface) cannot break or forge
  structure; strings inside the bookkeeping comment are JSON-escaped with the
  comment terminator neutralized (see the format contract).
- **Feature 012 forward-compatibility**: the bookkeeping block is an open YAML
  mapping whose unknown keys parsers must tolerate; a per-turn `created_pages:`
  list is added later as one more optional key — no restructuring.
- Write failures never alter a turn's outcome or suppress its realtime publishing;
  they are logged/counted (`query.conversation.record_append_failed`), matching the
  spec's edge case for recording failures.

### The record is the follow-up context source

- `POST /api/query-conversations/{conversationId}/turns` accepts only
  `{ "prompt": ... }`; `priorTurns` is removed from the API. The Hub assembles the
  agent's prior-turn scaffold from the conversation's recorded turns: an in-memory
  cache maintained on append, hydrated by parsing the record file after a Hub
  restart; a missing file simply means a first turn. `position` becomes
  Hub-assigned (recorded turns + 1). `conversationId` is validated
  (`^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`) because it now names a file.
- **Why this is sound with no server-side "session"**: the existing one-active-turn
  guard (409) means a submission is only accepted when every prior turn is terminal
  — and terminal turns are exactly the recorded ones. The record is therefore
  always complete at the moment context is needed. Consistency (FR-006) holds by
  construction: one source feeds both the agent and the audit trail. With
  browser-supplied context (Option 2) there are two copies whose agreement the Hub
  cannot verify without consulting the record anyway; a client bug, a missed
  answer chunk after a reconnect gap, or a tampered payload would silently hand
  the agent context diverging from the record — making SC-005's 100% guarantee
  unenforceable. Option 3 does all the work of Option 1 plus a new user-facing
  failure mode; Option 4 duplicates state into a second persistence mechanism and
  reintroduces the projection-consistency problem FR-001's "one record" avoids.
- **Fail-closed**: if a record file exists but is structurally unreadable, the
  submission is rejected (`conversation_record_unreadable`) rather than proceeding
  with partial context — the record must never misrepresent what the agent knew.
- **Reload semantics are unchanged**: conversations remain ephemeral browser state;
  there is no conversation-browser UI and no restore-on-reload (spec Assumptions).
  After a reload the browser starts a new conversation exactly as in 008. This
  decision changes who assembles context, not what the user can do.
- **Agent process contract unchanged**: `QueryAgentRequest.PriorTurns`, the stdin
  `QueryConversationInput`, and the harness-owned message scaffold in
  `Grimoire.QueryAgent` keep their exact shapes — so ADR-012 replay recordings and
  fingerprints remain valid, and the agent sees byte-identical context for a given
  conversation regardless of its origin.

### Supersession scope (ADR-011)

This ADR **supersedes only the "Persistence and conversation context" section of
ADR-011**, namely:

- per-turn, Hub-written Query Run Artifacts under `<base>/data/query-runs/` — replaced
  by the per-conversation record above (`QueryRunArtifactWriter` and the
  `Grimoire.Hub.QueryRunArtifact` namespace are deleted; no migration of existing
  files; the retired location is guarded by a structural tripwire test); and
- "No server-side conversation store" with browser-supplied prior turns (008 R6) —
  replaced by record-sourced context.

**Everything else in ADR-011 remains fully in force**: the shared
`Grimoire.AgentRuntime` library, token streaming via `answer_chunk` over the ADR-008
event channel, Hub-side bounded concurrency (limit 3, reject-over-limit, no queue),
user-triggered interruption vs. liveness failure, the partial-answer buffer, the
structurally write-free Query agent, realtime delivery via
`QueryLifecycleHub`/`QueryLifecyclePublisher`, and the hexagonal port table with
containment rules C6/C7. The agent-side vestigial span `query_agent.finalize_artifact`
is removed with the artifact mechanism.

### Structural enforcement (Constitution III)

- New `Grimoire.ArchTests` tripwire (ADR-009 idiom, with Red/Green probe): no
  production assembly contains the IL string literal `query-runs`.
- Existing containment rules keep passing; the record store is a concrete,
  directly-injected class confined to `Grimoire.Hub.QueryConversations`
  (persistence exemption — introducing a port solely to mock it would violate
  Principle II).

### Consequences

- Good, because a conversation is finally readable as one document, and the context
  the agent received is provably the transcript the record shows (FR-006 by
  construction, not by comparison).
- Good, because auditability strictly relocates: every bookkeeping field of the
  per-turn artifact survives, now attached to its turn inside the conversation.
- Good, because submissions shrink to the prompt (no O(conversation) resend), and
  the agent's context no longer trusts the client.
- Good, because the one-active-turn guard, terminal-transition append, and durable
  file compose into completeness at submission time with zero new synchronization.
- Bad, because the Hub now parses its own record file (after restarts); mitigated by
  the length-delimited format making parsing deterministic and injection-proof, and
  by fail-closed rejection instead of guessing.
- Bad, because a corrupt record blocks follow-ups in that conversation (new 500
  path); accepted — the alternative (silently diverging context) is worse, and
  recovery is starting a new conversation.
- Neutral, because the browser keeps its ephemeral on-screen conversation and its
  two SignalR connections; nothing user-visible changes (FR-009).

## More Information

Detailed rationale: `specs/011-query-conversations/research.md`. Contracts:
`specs/011-query-conversations/contracts/` (conversation-record-format.md,
query-conversation-api.md). Sibling context only (not decided here): ADR-013
(feature 010, agent-platform packaging). Per Constitution Principle III this ADR
MUST reach **Accepted** (project-owner sign-off) before `/speckit-tasks` runs for
feature 011; it is deliberately left `proposed` by the planning run.

# Research: Conversation Records Replace Query-Run Artifacts

Phase 0 output for `specs/011-query-conversations/spec.md`. Decisions below resolve
every open point the spec defers to planning — above all the context-source question
the spec's Assumptions explicitly hand to a superseding ADR (drafted as
`docs/adr/ADR-014-query-conversation-records.md`, status `proposed`).

Code facts referenced here were verified against the current implementation:
`backend/src/Grimoire.Hub/QueryRunArtifact/QueryRunArtifactWriter.cs`,
`backend/src/Grimoire.Hub/QueryDispatch/QueryRunCoordinator.cs` /
`QueryTurnState.cs` / `QueryLifecycleLogEvents.cs`,
`backend/src/Grimoire.Hub/QuerySubmission/QuerySubmissionEndpoints.cs`,
`backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathOptions.cs` (+ resolver),
`backend/src/Grimoire.QueryAgent/Program.cs`, `backend/src/Grimoire.Hub/HubMetrics.cs`.

## R1: Conversation-context source — the record replaces browser-supplied prior turns

- **Decision**: The Hub-side Conversation Record becomes the single source of the
  prior-turn context for follow-up turns. The browser submits **only the prompt**;
  `priorTurns` is removed from the turn-submission API. At submission, the Hub loads
  the conversation's recorded turns (in-memory cache, hydrated from the record file
  after a Hub restart) and builds the same harness-owned message scaffold the Query
  agent already receives (`QueryAgentRequest.PriorTurns` and the agent's stdin
  contract are unchanged — only where the turns come from changes). This supersedes
  feature 008's R6 ("no server-side conversation store") — full rationale and
  supersession scope in ADR-014.
- **Rationale**:
  1. **FR-006/SC-005 hold by construction, not by hope.** SC-005 demands that for
     100% of follow-ups the context the agent sees matches the recorded transcript.
     With browser-supplied context there are two copies that must agree; the Hub
     cannot verify agreement without consulting the record — and once it consults
     the record, the record may as well be the source. A client bug, a missed
     `queryAnswerChunk` after a reconnect gap, or a tampered payload would silently
     hand the agent context that diverges from the audit record. A single source
     makes the 100% guarantee structural (Constitution II: deterministic guarantees
     must be enforceable, not aspirational).
  2. **The record must exist anyway — reusing it removes redundant transport and
     trust.** FR-001..FR-005 require the durable record regardless of this decision.
     Keeping browser-supplied context would retain an O(conversation) payload on
     every submission that merely restates what the Hub just wrote to disk, and
     would keep the agent's context trusting the client for content the Hub already
     knows authoritatively.
  3. **The existing one-active-turn contract makes the record always sufficient.**
     A follow-up can only be accepted when the conversation has no active turn (the
     409 guard, `QueryRunCoordinator`'s atomic `TryAdd` reservation). The record is
     appended exactly when a turn reaches a terminal state. Therefore at every
     accepting submission, all prior turns are terminal and already recorded — the
     record is complete precisely when it is needed. No new synchronization
     appears; Hub restarts are covered because the record is durable while the
     browser copy lives only in the tab.
- **Reload semantics unchanged**: feature 008 assumed conversations are ephemeral
  browser state, and this feature deliberately adds no conversation-browser UI and
  no restore-on-reload. After a reload the browser starts a new conversation (new
  client-generated `conversationId`), exactly as today; the old record simply
  remains on disk as the durable account. Record-as-source adds no user-visible
  capability — it changes only who assembles the context.
- **Alternatives considered**:
  - *Keep browser-supplied `priorTurns`, record = audit copy (008 R6 mechanism)* —
    rejected: leaves SC-005 unverifiable without comparing against the record on
    every submission; retains client trust and payload growth; and a
    record-vs-context divergence would be undetectable exactly in the cases that
    matter (client bugs, reconnect gaps).
  - *Hybrid: browser supplies turns, Hub validates them against the record and
    rejects on mismatch* — rejected: does all the work of record-as-source (load +
    compare) plus a new failure mode for the user, for no benefit.

## R2: Record file format — one markdown document, machine-recoverable by construction

- **Decision**: One markdown file per conversation with (a) a YAML frontmatter
  header holding conversation-level facts (`conversation_id`, `created_at`,
  `record_format: grimoire-conversation/1`), and (b) one self-contained appended
  block per terminal turn: a human-readable `## Turn N — <state>` section with
  `### Prompt` / `### Answer` bodies, preceded by a machine-readable bookkeeping
  block inside an HTML comment (`<!-- grimoire:turn ... -->`) carrying the same
  fields the Query Run Artifact frontmatter carries today (state, failure reason,
  timestamps, instruction identity/sha256, policy identity/version/sha256, model,
  turns used, denied actions) **plus `prompt_chars`/`answer_chars` length fields**.
  Full grammar: `contracts/conversation-record-format.md`.
- **Rationale**: The record is now load-bearing (R1): the Hub must be able to parse
  its own record back into structured turns after a restart. Length-prefixed
  content (`prompt_chars`/`answer_chars` counting the exact UTF-16 code units of
  the recorded bodies) makes parsing injection-proof: an answer that itself
  contains `### Answer`, `## Turn`, or a `<!-- grimoire:turn -->` sentinel cannot
  confuse the parser, because the parser slices by declared length rather than by
  scanning for delimiters. This matters because answer text is LLM output over
  wiki content — untrusted by definition (prompt-injection surface). Denied-action
  strings inside the bookkeeping comment (agent-chosen targets) are JSON-string
  escaped with `-->` neutralized (its `>` written as the JSON escape `\u003e`),
  so the comment cannot be terminated early. The human still reads a plain dialogue; the bookkeeping stays
  visually out of the way in comments, mirroring how the old artifact kept it in
  frontmatter.
- **Feature 012 forward-compatibility** (per owner direction): the bookkeeping
  block is an extensible YAML mapping — a future `created_pages:` list per turn
  slots in as one more optional key with no restructuring.
- **Alternatives considered**: per-turn YAML frontmatter-only (no lengths) —
  rejected as parse-ambiguous against adversarial answer content; sidecar JSON file
  for the machine copy — rejected: FR-001/FR-005 want *one* record, and a sidecar
  reintroduces the two-copies consistency problem R1 just eliminated.

## R3: Append mechanics, atomicity, and never-rewrite

- **Decision**: The record is append-only. The first terminal turn of a
  conversation creates the file (frontmatter header + first turn block) in a single
  write; every later terminal turn appends its complete block with a single
  append-mode write. Recorded bytes are never modified. The store tolerates a
  trailing partial block (crash mid-append): on load it recovers all complete turn
  blocks, logs `query.conversation.record_load_failed`-level diagnostics for the
  trailing fragment, and fails the submission fail-closed if the file is
  structurally unreadable (R5).
- **Rationale**: FR-003 says recorded turns are never rewritten by later activity —
  physical append honors that literally (the old writer's temp+rename whole-file
  replace would rewrite earlier turns' bytes on every append and gets riskier as
  the file grows). Appends are naturally serialized per conversation: the
  one-active-turn guard means at most one terminal transition per conversation at
  a time, and `QueryTurnState.TryTransitionTo`'s first-transition-wins already
  guarantees exactly one finalization per turn; the store still takes a
  per-conversation lock as defense in depth. Different conversations write to
  different files, so the concurrency limit (3) needs no cross-file coordination.
- **Alternatives considered**: temp+rename full rewrite per turn (old artifact
  writer idiom) — rejected per above; write-ahead sidecar journal — rejected as
  overengineering for a single-user local harness (Constitution: no unapproved
  infrastructure, keep proportional).

## R4: Storage location and path composition

- **Decision**: `<base>/data/conversations/<conversationId>.md`.
  `GrimoirePathOptions` gains `ConversationsDir` with
  `DefaultConversationsDirName = "conversations"`; `GrimoirePathResolver` resolves
  it beneath the data dir, reports it as `conversations_dir`, and auto-creates it
  (`PathLocationKind.WritableData`) — the exact ADR-009 single-composition-point
  pattern the retired `QueryRunsDir` used. `ResolvedGrimoirePaths` gains
  `ConversationRecordPathFor(conversationId)`; `QueryRunsDir`,
  `DefaultQueryRunsDirName`, and `QueryRunArtifactPathFor` are deleted.
- **Rationale**: FR-005 — the location names the thing ("conversations"), records
  are locatable by conversation identity alone (one flat file per conversation, no
  per-turn nesting to traverse). Stays under `<base>/data` per ADR-009/ADR-003:
  operational harness bookkeeping, outside `wiki/`, git-ignored.
- **Alternatives considered**: `data/conversations/<id>/record.md` (directory per
  conversation) — rejected: nothing else will live in that directory (012's
  synthesis pages are wiki content, not operational data), so the extra level only
  hurts locatability.

## R5: Context loading, caching, and failure semantics

- **Decision**: A `ConversationRecordStore` (namespace
  `Grimoire.Hub.QueryConversations`) owns both directions: appending terminal
  turns and loading a conversation's recorded turns. Loads serve from an in-memory
  per-conversation cache maintained on each append; a cache miss (Hub restarted
  since the conversation began) hydrates by parsing the record file; a missing
  file means a new conversation (empty context). If the file exists but cannot be
  parsed into its complete turn blocks, the submission is **rejected fail-closed**
  (HTTP 500, `reason: "conversation_record_unreadable"`, log event
  `query.conversation.record_load_failed`) — the agent must never receive context
  that cannot be shown to match the record (FR-006). The user recovers by starting
  a new conversation.
- **Rationale**: The cache keeps the hot path free of file I/O parsing; the
  record file remains the durability authority (SC-003). Fail-closed on parse
  failure is the only reading of FR-006 consistent with Constitution II's
  100%-guarantee discipline — silently proceeding with partial context would make
  the record lie about what the agent knew.
- **Alternatives considered**: always parse from disk (no cache) — acceptable at
  this scale but re-parses grow with conversation length for zero benefit;
  proceed-with-empty-context on parse failure — rejected as a silent FR-006
  violation.

## R6: Retirement of the per-turn artifact mechanism (cutover)

- **Decision**: `QueryRunArtifactWriter` and the `Grimoire.Hub.QueryRunArtifact`
  namespace are deleted outright; `QueryRunCoordinator.FinishTurnAsync` calls the
  `ConversationRecordStore` append instead (wrapped so an append failure is logged
  and counted but never alters the turn's outcome or suppresses the realtime
  `queryTurnChanged` publish — today's code would skip the publish if the artifact
  write threw, which the new code fixes in passing). No migration or conversion of
  existing `data/query-runs/` files (FR-008); they may be deleted by hand. The
  cutover guard is structural: a new `Grimoire.ArchTests` tripwire rule (ADR-009
  idiom) asserts no production assembly contains the IL string literal
  `query-runs`, proven live with a Red/Green probe.
- **Rationale**: FR-007/SC-004. The tripwire makes "no new files in the retired
  location" a compile-time-adjacent guarantee rather than a runtime assertion —
  the code that could write there no longer exists and cannot quietly return.
- **Alternatives considered**: keep the writer behind a feature flag during a
  transition window — rejected: dual-writing would violate FR-007's "no per-turn
  artifact files" and the spec declares the old data disposable.

## R7: API and process-contract impact

- **Decision**:
  - `POST /api/query-conversations/{conversationId}/turns` body becomes
    `{ "prompt": "..." }` — `priorTurns` removed. `position` is now Hub-assigned
    (recorded turn count + 1) instead of derived from the client payload.
    Unknown/extra body fields (including a stale client still sending
    `priorTurns`) are ignored per normal JSON binding, so an un-upgraded tab
    degrades gracefully: its submission is accepted and the context comes from the
    record — which FR-006 requires to match what that tab shows.
  - `conversationId` gains server-side validation (`^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`,
    400 on violation) — it is now a filename, so path safety must be enforced at
    the boundary, not assumed from the client's UUID habit.
  - `409`/`503` semantics, the interrupt endpoint, `GET /api/query-turns/{turnId}`,
    and both SignalR events are unchanged (FR-009).
  - The Query agent process contract is **unchanged**: `QueryAgentRequest`
    (launcher port) and the stdin `QueryConversationInput` (prompt + prior turns)
    keep their shapes; the agent-side scaffold in
    `backend/src/Grimoire.QueryAgent/Program.cs` (`BuildInitialConversation`,
    prior turns as real user/assistant messages) is untouched. This deliberately
    keeps ADR-012 replay recordings and fingerprints unaffected — the model-port
    request stream for a given conversation is byte-identical.
- **Rationale**: Smallest surface change that realizes R1; the frontend change is
  confined to the submission payload builder (stop collecting `priorTurns`), and
  the eval/replay tier (ADR-012) sees no drift.

## R8: Observability deltas (full tables in plan.md ## Observability)

- **Decision**:
  - **Retired**: the agent-side span `query_agent.finalize_artifact`
    (`Grimoire.QueryAgent/Program.cs`) — vestigial even in 008 (the agent never
    wrote the artifact; the span carried no work). It is removed, not renamed.
    The Hub-side artifact writer had **no** dedicated log event, metric, or span
    in 008's contract (verified: `QueryRunArtifactWriter` emits nothing;
    008 plan.md's log-event table contains no artifact row) — so nothing else is
    retired; the recording path finally gets first-class signals of its own.
  - **Unchanged**: all other feature-008 signals — log events `query.turn.created`
    / `query.turn.completed` / `query.turn.interrupted` / `query.turn.failed` /
    `query.submission.rejected` (Hub), `query.instructions.loaded` /
    `query.instructions.load_failed`, `query.tool.denied` (agent); metrics
    `query.turns_total`, `query.turn_duration_seconds`, `query.answer_chunks_total`,
    `query.submissions_rejected_total`, `query.concurrent_runs`,
    `query.tool_calls_total`; spans `hub.query.submit`, `hub.query.spawn_agent`,
    `hub.query.run_supervision`, `hub.query.handle_run_event`,
    `hub.query_lifecycle.publish_update`, `query_agent.run`,
    `query_agent.load_instructions`, `query_agent.model_turn`,
    `query_agent.tool_call`.
  - **Added** (`query.conversation.*`, following the established `query.*` naming):
    log events `query.conversation.record_created`,
    `query.conversation.turn_recorded`, `query.conversation.record_append_failed`,
    `query.conversation.context_loaded`, `query.conversation.record_load_failed`;
    metrics `query.conversation.turns_recorded_total`,
    `query.conversation.record_append_failures_total`,
    `query.conversation.context_loads_total`,
    `query.conversation.record_load_failures_total`; spans
    `hub.query.load_conversation_context` (child of `hub.query.submit`) and
    `hub.query.record_turn` (child of `hub.query.run_supervision`, or of the
    interrupt HTTP request span for user-triggered interruption).
- **Rationale**: The record is now both the audit trail and the context source, so
  its write and read paths must be independently observable (Constitution IV);
  names extend the `query.*` ubiquitous language established in 008's plan.

## Technical Context resolution summary

No NEEDS CLARIFICATION markers remain. Language/stack, testing harness, and
observability backend are all inherited unchanged from features 002–008
(ADR-001/005); this feature adds no dependency, no external system, and no new
port (persistence exemption, Constitution I).

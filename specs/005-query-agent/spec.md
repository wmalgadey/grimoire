# Feature Specification: Interactive Wiki Query Process

**Feature Branch**: `claude/speckit-query-prozess-nemqt6`

**Created**: 2026-07-17

**Status**: Draft

**Input**: User description: "füge den /query prozess hinzu. Der benutzer soll ein query
prompt im ui eingeben können, der hub empfängt den prompt, leitet ihn an einen query
agent weiter (analog zum ingest agents mit einem dedizierten system prompt). Wichtig,
die antwort an den benutzer soll gestreamt werden, der benutzer soll die antwort
unterbrechen können und folgefragen stellen können. Der query agent verwendet nur das
wiki!"

## Terminology

- **Query Agent**: A read-only agent that answers user questions exclusively from the
  wiki's current content. It is dispatched by the Hub analogously to the Ingest agent
  but never modifies the wiki.
- **Query System Prompt Document**: The single, versioned instruction document that
  defines the Query agent's operating rules (grounding, citation, honesty about gaps,
  answer conventions). It is a sibling of the Ingest agent's System Prompt Document
  (feature 004) — a separate document, since the two agents have different jobs.
- **Query Conversation**: A sequence of Query Turns between one user and the Query
  agent in which later turns may refer back to earlier ones ("follow-up questions").
  A conversation is the unit of context: the agent sees the conversation's prior turns
  when answering a follow-up.
- **Query Turn**: One user Query Prompt plus the agent's answer to it. A turn ends when
  the answer completes, fails, or is interrupted by the user.
- **Query Prompt**: The free-text question or instruction the user enters in the UI for
  one turn.
- **Streamed Answer**: The agent's answer as it is progressively delivered to and
  rendered in the UI while the agent is still producing it, rather than only after
  completion.
- **Interruption**: A user action that stops the in-progress production of an answer.
  The partial answer produced so far remains visible, the turn ends in an
  `interrupted` state, and the conversation remains usable for follow-up questions.
- **Query Run Artifact**: The persistent record of one Query Turn, analogous to the
  ingest Task Artifact: it records the prompts used, instruction identity/hash, outcome
  state, and denied actions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask the wiki a question and watch the answer stream in (Priority: P1)

A user wants to know what the wiki says about a topic — for example "What do we know
about X?" or "Summarize our decisions on Y". They open the query surface in the Web UI,
type their question, and submit it. The Hub accepts the prompt and dispatches a Query
agent with its dedicated system prompt. The answer appears progressively in the UI as
it is produced, so the user starts reading within moments instead of staring at a
spinner. The answer is grounded exclusively in wiki content and names the wiki pages it
drew from; when the wiki does not cover the question, the answer says so plainly
instead of inventing content.

**Why this priority**: This is the feature. Until now the wiki can only be filled
(ingest); this story makes its knowledge retrievable through conversation, which is the
product's purpose. Every other story refines this interaction.

**Independent Test**: With a wiki containing known content, submit a question that the
content answers and verify the answer streams progressively, is consistent with the
wiki content, and references the source pages. Submit a question the wiki cannot
answer and verify the answer states that the wiki does not cover it.

**Acceptance Scenarios**:

1. **Given** the query surface is open, **When** the user submits a Query Prompt,
   **Then** the Hub accepts it, a Query agent run starts with the Query System Prompt
   Document as its entire system prompt, and the Query Run Artifact records the
   document's identity and content hash.
2. **Given** a running query turn, **When** the agent produces answer content, **Then**
   the UI renders it progressively while production continues — the user does not have
   to wait for the full answer.
3. **Given** a wiki that contains material answering the question, **When** sampled
   answers are evaluated, **Then** they are grounded in that material and reference the
   wiki pages they drew from, at the thresholds defined in Success Criteria.
4. **Given** a question the wiki does not cover, **When** sampled answers are
   evaluated, **Then** they plainly state that the wiki has no material on it and do
   not fabricate an answer.
5. **Given** the Query System Prompt Document is missing, unreadable, or effectively
   empty, **When** a query is submitted, **Then** the turn fails before any agent
   output with a human-readable reason (fail-closed, matching the ingest behavior).

---

### User Story 2 - Interrupt an answer mid-stream (Priority: P2)

While an answer is streaming, the user realizes it is heading in the wrong direction,
is answering a misunderstood question, or has already given them what they needed. They
press a stop control. Production halts promptly, the partial answer stays visible so
nothing already read is lost, and the input field is immediately ready for the next
question.

**Why this priority**: Interruptibility is what makes streaming useful rather than
decorative — without it the user is captive to every long answer. The user explicitly
called it out as important.

**Independent Test**: Ask a question that produces a long answer, interrupt it
mid-stream, and verify production stops promptly, the partial text remains visible,
the turn is recorded as interrupted, and a new question can be submitted immediately.

**Acceptance Scenarios**:

1. **Given** an answer is streaming, **When** the user activates the stop control,
   **Then** answer production halts promptly, no further answer content arrives after
   the halt is confirmed, and the partial answer remains visible in the conversation.
2. **Given** an interrupted turn, **When** the user looks at the conversation, **Then**
   the turn is visibly marked as interrupted (distinguishable from a completed answer),
   and the Query Run Artifact records the `interrupted` outcome.
3. **Given** an interrupted turn, **When** the user submits a follow-up question,
   **Then** it is accepted immediately — no cleanup, reload, or waiting for the
   abandoned answer is required.
4. **Given** a turn that has already completed, **When** the user attempts to
   interrupt, **Then** nothing breaks — the control is inactive or the action is
   harmlessly ignored.

---

### User Story 3 - Ask follow-up questions in context (Priority: P3)

After reading an answer (complete or interrupted), the user digs deeper: "And how does
that relate to Z?" or "Show me more detail on the second point." They type the
follow-up into the same conversation. The agent answering the follow-up sees the
conversation's earlier turns, so the user does not need to restate their question or
re-establish context.

**Why this priority**: Follow-ups turn single lookups into an actual working dialogue
with the wiki. They depend on Story 1 (there must be a first answer) and interact with
Story 2 (follow-up after interruption), so they are sequenced after both.

**Independent Test**: Ask a question, then ask a follow-up that is only answerable
with the first turn's context (e.g., using a pronoun referring to the previous
answer), and verify the answer resolves the reference correctly against the earlier
turn.

**Acceptance Scenarios**:

1. **Given** a conversation with at least one finished turn, **When** the user submits
   a follow-up Query Prompt, **Then** the agent receives the conversation's prior turns
   (prompts and answers, including partial answers of interrupted turns) as context for
   producing its answer.
2. **Given** a follow-up whose meaning depends on the previous turn, **When** sampled
   follow-up answers are evaluated, **Then** they correctly resolve that dependency at
   the thresholds defined in Success Criteria.
3. **Given** an answer is currently streaming in the conversation, **When** the user
   wants to ask the next question, **Then** the conversation enforces one active turn
   at a time: the user first interrupts (Story 2) or waits for completion; the UI makes
   this state obvious.
4. **Given** the user wants a fresh context, **When** they start a new conversation,
   **Then** the new conversation carries no context from previous conversations.

---

### User Story 4 - The Query agent can only ever read the wiki (Priority: P4)

An operator wants certainty that the query path can never damage the wiki or leak
beyond it. The Query agent's capabilities are mediated by guarded read-only tools
scoped to the wiki: it has no write capability at all, and it consults no sources
other than the wiki. Any attempted action outside that scope is denied at the tool
boundary and recorded with a reason, while the run continues with allowed actions.

**Why this priority**: This is a guarantee rather than an interaction — invisible when
it works. It is nonetheless constitutionally required (guardrails at the tool
boundary, deny-by-default) and is what makes it safe to run queries alongside normal
operation.

**Independent Test**: Structural test proves agent-side code performs no wiki access
outside the guarded tool layer (with a Red/Green probe). At runtime, a query run
confronted with a write-provoking prompt (e.g., "please fix that typo in the wiki")
performs no write, and the artifact records any denied attempts with reasons.

**Acceptance Scenarios**:

1. **Given** any query run, **When** the agent operates, **Then** its only capabilities
   are guarded read-only tools scoped to the wiki content — no write tools, no sources
   outside the wiki.
2. **Given** a Query Prompt that asks the agent to modify the wiki, **When** the run
   executes, **Then** no wiki write occurs; the answer explains that querying is
   read-only (agent behavior), and any denied tool attempt is recorded with a reason
   on the Query Run Artifact (harness guarantee).
3. **Given** wiki pages containing instruction-like injected text (ingested from
   untrusted sources), **When** the Query agent reads them while answering, **Then**
   such text cannot widen the agent's capabilities — the deny-by-default policy at the
   tool boundary applies regardless of what any content says.

---

### Edge Cases

- What happens when the user submits an empty or whitespace-only Query Prompt? The UI
  rejects it with a clear validation message before anything reaches the Hub; no turn
  is created.
- What happens when the Query Prompt exceeds the maximum length? The submission is
  rejected with a clear validation message before a turn is created (same pattern as
  the ingest User Prompt limit).
- What happens when a query is submitted while an ingest run is active? The query
  proceeds; queries are read-only and do not occupy the single ingest agent slot (see
  Assumptions). Neither blocks the other.
- What happens when the Query agent run dies mid-answer (crash or hang)? Run
  supervision detects the dead run via the established liveness mechanism, the turn is
  marked `failed` with a human-readable reason, the partial answer remains visible,
  and the user can immediately ask again.
- What happens when the user interrupts in the instant between answer completion and
  the UI reflecting it? The interruption is harmlessly ignored; the turn stays
  `completed`.
- What happens when the browser's realtime connection drops mid-stream? The existing
  connection-health indicator reflects the drop; already-rendered partial content stays
  visible; after reconnection the UI shows the turn's current authoritative state
  (still streaming, completed, interrupted, or failed) without a page reload.
- What happens when the user reloads the page mid-conversation? The on-screen
  conversation is ephemeral UI state and may be lost (see Assumptions); the turns
  remain traceable through their Query Run Artifacts. An in-flight turn at reload time
  is treated as interrupted.
- What happens when the wiki is empty (no pages at all)? The turn runs normally and
  the answer states that the wiki contains no material yet; this is an honest-gap
  answer, not an error.
- What happens when two browser windows are open? Each window holds its own
  conversation(s); the one-active-turn rule applies per conversation, and a global
  concurrency limit protects the Hub (see Assumptions). A submission beyond that limit
  is rejected immediately with a clear "busy" message rather than silently queued.
- What happens when the interruption signal arrives after the run already failed?
  Terminal states are final: the turn stays `failed`; the late signal changes nothing.

## Requirements *(mandatory)*

### Functional Requirements

**Query submission & dispatch**

- **FR-001**: The Web UI MUST provide a query surface where the user can enter and
  submit a free-text Query Prompt and read the conversation (prompts and answers) on
  the same screen.
- **FR-002**: The Hub MUST accept submitted Query Prompts and dispatch a Query agent
  run for each turn, delivering the user's prompt and — for follow-ups — the
  conversation's prior turns as context.
- **FR-003**: The Query agent MUST receive its operating instructions from exactly one
  versioned Query System Prompt Document, loaded verbatim as its system prompt,
  separate from the Ingest agent's document. Loading MUST be fail-closed: a missing,
  unreadable, or effectively empty document fails the turn before any agent output,
  with a human-readable reason.
- **FR-004**: The query surface MUST validate prompts before submission: empty or
  whitespace-only prompts and prompts exceeding a reasonable maximum length are
  rejected with a clear message before a turn is created.

**Streaming & interruption**

- **FR-005**: The answer MUST be delivered to the UI progressively while the agent is
  still producing it, and the UI MUST render the received content as it arrives.
- **FR-006**: The user MUST be able to interrupt an in-progress answer via a visible
  stop control. Interruption MUST promptly halt answer production (including stopping
  the underlying agent run), preserve the partial answer in the conversation, mark the
  turn `interrupted`, and leave the conversation immediately ready for the next
  prompt.
- **FR-007**: Interrupting a turn that is already in a terminal state MUST have no
  effect; terminal turn states (`completed`, `interrupted`, `failed`) are final.
- **FR-008**: A conversation MUST have at most one active turn at a time; the UI MUST
  reflect whether the conversation is ready for input or currently streaming.

**Conversation & follow-ups**

- **FR-009**: Follow-up prompts within a conversation MUST be answered with the
  conversation's prior turns available to the agent as context, including the partial
  answers of interrupted turns.
- **FR-010**: The user MUST be able to start a new conversation that carries no context
  from any earlier conversation.

**Wiki-only, read-only agent**

- **FR-011**: The Query agent's capabilities MUST be limited to guarded read-only tools
  scoped to the wiki content, enforced deny-by-default at the tool boundary at the
  moment of invocation. The Query agent MUST have no wiki-write capability and no
  access to sources outside the wiki.
- **FR-012**: Denied tool actions during a query run MUST be recorded with reasons on
  the Query Run Artifact; the run continues with allowed actions.
- **FR-013**: Instruction-like text inside wiki pages read by the Query agent MUST NOT
  be able to widen the agent's capabilities; policy enforcement is independent of any
  content the agent reads.
- **FR-014**: An automated structural test MUST verify that query-path agent-side code
  performs no wiki access outside the guarded tool layer, with a Red/Green probe
  proving the test detects violations.

**Supervision, traceability & lifecycle**

- **FR-015**: Query agent runs MUST be supervised with the same liveness approach as
  ingest runs: a run that stops signalling within the configured liveness window is
  marked `failed` with a liveness reason, any leftover agent process is terminated,
  and the partial answer already delivered remains visible.
- **FR-016**: Every Query Turn MUST produce a persistent Query Run Artifact recording:
  the Query Prompt, the conversation identity and turn position, the Query System
  Prompt Document identity and content hash, the outcome state (`completed`,
  `interrupted`, `failed` with reason), and denied actions. Answer content MUST be
  attributable to its turn.
- **FR-017**: Query activity MUST NOT block ingest activity and vice versa: submitting
  queries while an ingest run is active (and submitting ingests while queries run)
  MUST work, subject to a configurable global limit on concurrently running query
  turns. A submission beyond that limit is rejected immediately with a clear message.
- **FR-018**: Changing the Query agent's answering behavior (grounding style, citation
  conventions, tone, gap handling) MUST require editing only the Query System Prompt
  Document — no backend change — in line with the constitution's agentic-core
  boundary.

### Key Entities

- **Query Conversation**: The context unit for follow-ups. Attributes: identity,
  creation time, ordered sequence of Query Turns. Holds at most one active turn.
- **Query Turn**: One prompt-answer exchange. Attributes: conversation identity, turn
  position, Query Prompt text, answer content (possibly partial), state (`running`,
  `completed`, `interrupted`, `failed` with reason).
- **Query System Prompt Document**: The versioned instruction document governing the
  Query agent. Attributes: identity/location, content, content hash per run. Distinct
  from the Ingest agent's System Prompt Document.
- **Query Run Artifact**: Persistent per-turn record. Attributes: prompt, conversation
  identity and turn position, instruction identity and hash, outcome state and reason,
  denied actions with reasons.
- **Streamed Answer**: The progressively delivered answer content of a turn; rendered
  incrementally in the UI and attributable to its turn.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees (100%)**

- **SC-001**: 100% of query turns load the Query System Prompt Document as the agent's
  entire system prompt, with the content hash recorded on the Query Run Artifact; 100%
  of turns with a missing, unreadable, or empty document fail before any agent output
  with a human-readable reason.
- **SC-002**: 100% of query runs perform zero wiki writes and zero reads outside the
  wiki scope — every out-of-scope attempt is denied at the tool boundary and recorded
  with a reason.
- **SC-003**: Answer content appears in the UI progressively: the first visible answer
  content is rendered within 2 seconds (p95) of the agent producing it, and subsequent
  content within 2 seconds (p95) of production — the user never waits for run
  completion to start reading.
- **SC-004**: 100% of interruptions on an active turn halt answer delivery within 2
  seconds, preserve the partial answer, and leave the conversation accepting a new
  prompt without reload; 100% of interruptions on terminal turns change nothing.
- **SC-005**: 100% of query turns produce a Query Run Artifact with prompt,
  conversation/turn identity, instruction hash, and outcome state; 100% of dead runs
  (crash/hang) are marked `failed` within the configured liveness window.
- **SC-006**: 100% of query submissions made while an ingest run is active are accepted
  and processed without waiting for the ingest run, and ingest submissions are never
  delayed by query activity (within the configured query concurrency limit).

**Agent-judgment evaluation thresholds**

- **SC-007**: ≥ 90% of sampled answers to questions covered by the wiki are grounded in
  actual wiki content and reference the pages they drew from, with no material claims
  that contradict or go beyond the wiki, as judged by the evaluation rubric.
- **SC-008**: ≥ 90% of sampled questions not covered by the wiki receive an answer that
  plainly states the wiki has no material on the topic, without fabricated content.
- **SC-009**: ≥ 90% of sampled follow-up questions whose meaning depends on earlier
  turns are answered with that dependency correctly resolved.
- **SC-010**: ≥ 90% of sampled write-requesting prompts receive an answer that
  declines and explains the read-only nature of querying (while the harness guarantees
  the write never happens regardless — SC-002).

## Assumptions

- **Queries run outside the ingest agent slot**: Feature 004's single-agent invariant
  exists to serialize wiki-writing ingest runs. Query runs are strictly read-only and
  therefore do not occupy or wait for the ingest slot; they run alongside ingest
  activity. A configurable global limit on concurrent query turns (default: a small
  number, e.g. 3) protects the Hub; submissions beyond it are rejected immediately
  with a clear "busy" message, not queued.
- **One active turn per conversation**: Within one conversation the user asks, then
  reads/interrupts, then asks again. Parallel turns inside one conversation are out of
  scope.
- **Conversations are session-scoped in the UI**: The conversation display lives in the
  browser session; persisting and restoring full conversations across reloads or
  devices is out of scope. Traceability is provided per turn via Query Run Artifacts,
  not via a conversation browser. A page reload with an in-flight turn treats that
  turn as interrupted.
- **Query System Prompt Document is file-based and version-controlled**: Like the
  Ingest agent's document (feature 004), it is edited as code — reviewed and
  versioned — not through the Web UI.
- **"The wiki" means the wiki's current page content**: The Query agent answers from
  the pages (including index/log pages) as they exist at query time. It does not see
  ingest sources, task artifacts, or any external systems, and it does not perform
  general web access.
- **Existing surfaces and mechanisms are reused**: The query surface extends the
  existing Web UI (feature 003); realtime delivery to the browser and the
  connection-health indicator follow the mechanisms established in features 003/004;
  run supervision reuses the event/heartbeat liveness model from feature 004. Naming
  them here fixes expectations, not implementation.
- **Single-user context**: Like the rest of the product to date, no authentication or
  multi-user separation is introduced; "one user" refers to whoever operates the UI.
- **Answer language follows the question**: The agent answers in the language the
  question was asked in; this is agent behavior governed by the Query System Prompt
  Document, not a harness rule.

# Feature Specification: Conversation Records Replace Query-Run Artifacts

**Feature Branch**: `011-query-conversations`

**Created**: 2026-07-27

**Status**: Implemented — completed 2026-07-29 (residual manual validation: tasks.md T044 — quickstart scenarios 1–5 need a live Hub with a real API key)

**Input**: User description: "The query-runs markdown files could be renamed to
conversations, and they should include the complete conversation, not just the
answer and the result — the question can include references to previous answers.
The current query-runs output is not usable, but the general idea of a
conversation history is good."

## Terminology

- **Conversation Record**: The persistent record of one Query Conversation: a
  single, human-readable document containing the complete transcript of every turn
  in order, plus per-turn bookkeeping. One record per conversation — replacing the
  previous one-file-per-turn Query Run Artifacts.
- **Transcript**: The ordered sequence of turns inside a Conversation Record: each
  turn's prompt and its full answer (final or last-known-partial), in the order
  they occurred.
- **Turn Bookkeeping**: The harness facts about one turn: outcome state
  (`completed`, `interrupted`, `failed` with reason), timestamps, instruction
  identity and content hash, denied actions with reasons, and model/loop usage.
- **Query Conversation / Query Turn**: As established by feature 008 — a
  conversation is a sequence of turns between one user and the Query agent in
  which later turns may refer back to earlier ones; a turn is one prompt plus the
  agent's answer to it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read a conversation as one document (Priority: P1)

A user (or operator) opens the record of a past conversation and reads it top to
bottom like a dialogue: first question, first answer, follow-up, next answer — in
order, complete, in one place. A follow-up like "and how does that relate to the
second point?" is intelligible because the answer it refers to sits right above
it. Today this is impossible: each turn lives in its own file holding only that
turn's prompt and answer, so the context that gives a follow-up its meaning is
scattered across files.

**Why this priority**: This is the feature. The existing per-turn records are not
usable as a conversation history — restoring their intelligibility is the entire
point.

**Independent Test**: Hold a conversation with at least three turns including a
follow-up that references an earlier answer; open the Conversation Record and
verify it contains all turns in order with complete prompts and answers, readable
as one coherent dialogue.

**Acceptance Scenarios**:

1. **Given** a conversation with several finished turns, **When** its Conversation
   Record is opened, **Then** it contains every turn's prompt and full answer in
   conversation order within a single document.
2. **Given** a follow-up turn whose meaning depends on an earlier answer, **When**
   the record is read, **Then** the referenced answer is present in the same
   document, above the follow-up.
3. **Given** two conversations running concurrently, **When** their records are
   inspected, **Then** each conversation has exactly one record containing only
   its own turns.

---

### User Story 2 - Bookkeeping preserved per turn (Priority: P2)

An operator auditing a conversation still finds everything the per-turn artifacts
used to record — outcome state, failure reasons, instruction identity and content
hash, denied actions, timestamps — attached to the respective turn inside the
Conversation Record. Traceability does not regress; it relocates.

**Why this priority**: The per-turn bookkeeping exists for auditability and
guardrail traceability, which are constitutional concerns. The new record must be
a superset of the old one, not a trade of auditability for readability.

**Independent Test**: Produce turns that end `completed`, `interrupted`, and
`failed`, including one with a denied tool action; verify each turn's bookkeeping
inside the record carries state, reason, instruction hash, and the denial with its
reason.

**Acceptance Scenarios**:

1. **Given** any finished turn, **When** its section of the Conversation Record is
   inspected, **Then** it carries the turn's outcome state, timestamps,
   instruction identity and content hash, and (for failures) a human-readable
   reason.
2. **Given** a turn during which a tool action was denied, **When** the record is
   inspected, **Then** the denial is recorded with its reason, attached to that
   turn.
3. **Given** an interrupted turn, **When** the record is inspected, **Then** the
   partial answer produced before the interruption is present and the turn is
   marked interrupted.

---

### User Story 3 - Records survive what the browser doesn't (Priority: P3)

The on-screen conversation remains ephemeral browser state (unchanged from feature
008), but the Conversation Record is durable: after a page reload, a browser
crash, or a Hub restart, the record of everything that finished is intact on disk.
A turn that was in flight when the world ended is recorded with its last known
state, consistent with how interruptions and failures are already handled.

**Why this priority**: Durability is what makes the record trustworthy as an
audit trail, but it builds directly on Stories 1–2 and mostly inherits behavior
that already exists per turn.

**Independent Test**: Finish two turns, kill the browser mid-third-turn, and
verify the record contains the two finished turns completely and the third with
its partial answer and non-completed state.

**Acceptance Scenarios**:

1. **Given** finished turns in a conversation, **When** the browser session ends
   or the Hub restarts, **Then** the Conversation Record still contains those
   turns completely.
2. **Given** a turn in flight when its run dies or is abandoned, **When** the
   record is inspected afterward, **Then** the turn appears with its partial
   answer and its terminal state and reason, exactly as the existing supervision
   rules determine them.

---

### Edge Cases

- What happens when a conversation consists of a single turn? The record is still
  created and holds that one turn — no minimum length.
- What happens when the user starts a new conversation in the same window? A new
  record begins; the old record stays as it was — records are never merged or
  overwritten by later conversations.
- What happens when a turn is added to a conversation whose record already exists?
  The record is extended with the new turn; earlier turns in the record are never
  rewritten by later ones.
- What happens if writing the record fails while the answer succeeded? The turn's
  outcome toward the user is unaffected; the recording failure is surfaced through
  the established operational error reporting, consistent with how per-turn
  artifact write failures were treated.
- What happens to the existing per-turn query-run files? They are superseded
  operational data: no migration, no conversion. After cutover no new per-turn
  files appear; existing old files may simply be deleted.
- What happens with very long conversations? The record grows with the
  conversation; no truncation is introduced. The existing per-conversation
  turn-flow limits (one active turn at a time, prompt length limit) bound growth
  per turn.
- What context does the agent see for follow-ups? Exactly the turns recorded in
  the conversation — the context delivered to the agent and the transcript in the
  record must agree, so the record is a faithful account of what the agent knew.

## Requirements *(mandatory)*

### Functional Requirements

**One record per conversation**

- **FR-001**: Every Query Conversation MUST produce exactly one persistent
  Conversation Record containing the complete transcript: every turn's prompt and
  full answer (final or last-known-partial), in conversation order, in one
  human-readable document.
- **FR-002**: Each turn in the record MUST carry its Turn Bookkeeping: outcome
  state (`completed`, `interrupted`, `failed` with human-readable reason),
  timestamps, instruction identity and content hash, denied actions with reasons,
  and model/loop usage.
- **FR-003**: The record MUST be extended as each turn reaches a terminal state:
  turns already recorded are never rewritten by later activity, and a turn's
  answer content remains attributable to its turn.
- **FR-004**: Records MUST be durable independently of the browser session: they
  persist across page reloads, browser loss, and Hub restarts for all turns that
  reached a terminal state.
- **FR-005**: Records MUST be locatable by conversation identity, and the storage
  location and naming MUST say "conversations" — the record of a conversation is
  named for what it is, not for the mechanics that produced it.

**Consistency with agent context**

- **FR-006**: The prior-turn context delivered to the Query agent for a follow-up
  MUST be consistent with the conversation's record: the turns the agent sees and
  the turns the record holds are the same turns with the same content.

**Supersession of per-turn artifacts**

- **FR-007**: The one-file-per-turn Query Run Artifact mechanism MUST be retired:
  after this feature, query turns produce no per-turn artifact files, and all
  guarantees previously attached to per-turn artifacts (traceability of prompts,
  instruction hashes, outcomes, denied actions) are provided by the Conversation
  Record instead.
- **FR-008**: Existing per-turn query-run files MUST NOT be migrated or converted;
  they are disposable operational data. The cutover MUST ensure no new files
  appear in the retired location.

**Unchanged behavior**

- **FR-009**: The user-facing conversation experience established by feature 008
  (streaming, interruption, follow-ups, one active turn per conversation,
  concurrency limits) MUST be unchanged by this feature; only the persistence
  shape changes.

### Key Entities

- **Conversation Record**: One per Query Conversation. Identity: the conversation
  identity. Content: ordered Transcript plus per-turn Turn Bookkeeping and
  conversation-level facts (creation time, record location).
- **Transcript**: Ordered turns; each entry holds the prompt and the full answer
  text as delivered (including partial answers of interrupted/failed turns).
- **Turn Bookkeeping**: Per-turn harness facts: state, reason, timestamps,
  instruction identity/hash, denied actions, model/loop usage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees (100%)**

- **SC-001**: 100% of query conversations have exactly one Conversation Record;
  100% of terminal turns appear in their conversation's record with prompt, full
  answer text, order position, and complete Turn Bookkeeping.
- **SC-002**: 100% of denied tool actions during query turns are recorded with
  reasons in the Conversation Record, preserving the traceability level previously
  provided per turn.
- **SC-003**: 100% of turns that reach a terminal state before a browser loss or
  Hub restart are present and complete in the record afterward; 100% of turns in
  flight at such an event appear with their partial answer and a terminal state
  consistent with the established interruption/supervision rules.
- **SC-004**: After cutover, 0 new per-turn artifact files are created in the
  retired location; 100% of new query activity is recorded exclusively in
  Conversation Records.
- **SC-005**: For 100% of follow-up turns, the prior-turn context delivered to the
  agent matches the conversation's recorded transcript at submission time.

*(This feature changes harness persistence only: no agent-judgment success
criteria apply. Answer quality and follow-up resolution remain governed by feature
008's evaluation thresholds, which must still pass unchanged.)*

## Assumptions

- **This feature supersedes an accepted decision**: feature 008 deliberately chose
  per-turn artifacts and no server-side conversation record, on the rationale that
  the browser already held the conversation. Making the conversation durable
  server-side reverses that rationale and MUST be settled by a superseding
  architecture decision record during planning — including whether the browser
  keeps supplying prior turns on each submission (with the record as the audit
  copy) or the record becomes the context source (FR-006 requires only that the
  two never disagree).
- **No conversation browser UI**: Reading records happens outside the Web UI (they
  are human-readable files); an in-UI conversation list, restore-on-reload, or
  cross-device history remains out of scope, as in feature 008.
- **Records are operational data**: They live with other harness bookkeeping
  outside the wiki content and outside version control, following the established
  domain/operational split.
- **Wave placement**: Expected to be implemented in parallel with feature 010
  (platform consolidation); it touches conversation persistence, not the agent
  platform, so overlap is minimal. Features 012/013 build on the record (e.g.
  synthesis pages referenced from the transcript).
- **Single-user context**: Unchanged; records are not access-controlled beyond the
  host's own protections.

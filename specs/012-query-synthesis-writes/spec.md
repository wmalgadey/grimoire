# Feature Specification: Query Agent Synthesis Writes

**Feature Branch**: `012-query-synthesis-writes`

**Created**: 2026-07-27

**Status**: Draft

**Input**: User description: "The query agent should be extended to its final
state. It should not have only read-only access to the wiki but write new content:
good answers that contain new insights should be saved as new wiki pages marked as
synthesis, as in the original wiki-maintenance conventions."

## Terminology

- **Synthesis**: An insight produced while answering a query that is genuinely new
  to the wiki — a connection, conclusion, or consolidated view that no single
  existing page states, assembled from material spread across pages.
- **Synthesis Page**: A wiki page created by the Query agent to preserve a
  Synthesis. It is a first-class wiki page: it carries the standard frontmatter
  (tags, confidence with reason, review date), is marked as synthesized content
  via its source-type tag, links the pages it drew from, is entered in the wiki
  index, and gets a log entry — like content created by any other agent.
- **Synthesis Decision**: The Query agent's judgment call, made under its
  instruction files, that an answer contains a Synthesis worth preserving (or
  does not). This judgment is agentic; the harness never decides page-worthiness.
- **Write Scope**: The set of wiki write actions the Query agent's guardrail
  policy permits. Everything outside it is denied at the tool boundary,
  deny-by-default, as for every agent.

## Clarifications

### Session 2026-07-28

- Q: May the Query agent update existing synthesis pages when a new insight
  extends or supersedes one (update-over-duplicate), or only create new pages?
  → A: Create-only. The Write Scope covers creating new Synthesis Pages and
  maintaining index and log; consolidating near-duplicate syntheses is the Lint
  agent's job (feature 013).
- Q: When the user explicitly asks "save this as a wiki page" — must the agent
  save, or does the Synthesis Decision remain the agent's judgment? → A: The
  agent decides. An explicit user request is strong signal, but the agent may
  decline (and say why) when the answer contains no genuinely new insight — the
  wiki must not fill with answer-echoes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A good answer becomes a wiki page (Priority: P1)

A user asks a question whose answer connects material from several wiki pages into
an insight none of them states on its own — "how do our credential-scoping
decisions relate to the runtime-path decisions?" The agent answers as usual, and
because the answer contains a genuine synthesis, it preserves that insight as a
new wiki page marked as synthesized content: proper frontmatter, links to the
pages it drew from, an index entry, and a log entry. The answer tells the user the
insight was saved and names the page. The wiki has learned from being asked.

**Why this priority**: This is the feature — the Query agent's "final state" per
the original wiki-maintenance conventions: querying stops being a dead end and
starts feeding the knowledge loop.

**Independent Test**: With a wiki whose pages jointly imply an insight no single
page states, ask the question that elicits it; verify a Synthesis Page is created
with correct frontmatter, source links, index entry, and log entry, and that the
answer references the new page.

**Acceptance Scenarios**:

1. **Given** an answer containing a genuine Synthesis, **When** sampled such turns
   are evaluated, **Then** the agent preserves the insight as a Synthesis Page at
   the thresholds defined in Success Criteria.
2. **Given** a created Synthesis Page, **When** it is inspected, **Then** it
   carries the standard frontmatter including the synthesis marker and a
   confidence score with reason, links the wiki pages it drew from, appears in the
   wiki index, and has a corresponding log entry.
3. **Given** a turn that created a Synthesis Page, **When** the user reads the
   answer, **Then** the answer states that the insight was preserved and names the
   page; the turn's record also lists the created page.
4. **Given** a routine lookup whose answer merely restates existing pages,
   **When** sampled such turns are evaluated, **Then** no page is created at the
   thresholds defined in Success Criteria — the wiki must not fill with
   answer-echoes.

---

### User Story 2 - Writes stay guarded and scoped (Priority: P2)

An operator trusts the query path with write capability because every write passes
the same guarded tool boundary as every other agent's writes: deny-by-default
policy, enforced at the moment of invocation, every denial recorded with a reason.
The Query agent's Write Scope covers preserving syntheses — creating new pages and
maintaining the index and log — and nothing else: it cannot rewrite existing
content pages, and text inside wiki pages cannot widen its scope.

**Why this priority**: Write capability without the guardrail story would be a
regression of the operator guarantee that made concurrent querying acceptable in
the first place. It is the enabling constraint for Story 1.

**Independent Test**: Drive the agent toward out-of-scope writes (overwrite an
existing content page, write outside the wiki); verify every such attempt is
denied at the tool boundary with a recorded reason while the run continues, and
that in-scope synthesis writes succeed.

**Acceptance Scenarios**:

1. **Given** any query turn, **When** the agent invokes a write action, **Then**
   the action is checked against the versioned deny-by-default policy at
   invocation time; allowed actions proceed, denied actions are recorded with
   reasons, and the run continues.
2. **Given** an attempt to modify or overwrite an existing content page, **When**
   the policy is applied, **Then** the attempt is denied and recorded — the Write
   Scope covers creating Synthesis Pages and maintaining index and log only.
3. **Given** wiki pages containing instruction-like injected text, **When** the
   agent reads them while answering, **Then** that text cannot widen the Write
   Scope — enforcement is independent of any content the agent reads.
4. **Given** a user prompt that directly asks the agent to edit an existing page
   ("please fix that typo"), **When** sampled such turns are evaluated, **Then**
   the agent declines and explains that querying does not edit existing content,
   at the thresholds defined in Success Criteria — and the harness guarantees the
   edit cannot happen regardless.

---

### User Story 3 - Writers don't trample each other (Priority: P3)

Query turns keep running concurrently with ingest and with each other, and now
some of them write. The user never observes wiki corruption from this: no
half-written pages, no lost index entries, no interleaved log entries — regardless
of what runs at the same time. Answer streaming stays as responsive as before;
preserving a synthesis never makes the user wait on another agent's run.

**Why this priority**: Feature 008 justified concurrent queries by their being
read-only; this feature removes that premise and must replace it with an explicit
integrity guarantee. It is P3 only because it is invisible when it works.

**Independent Test**: Run concurrent query turns that both produce syntheses while
an ingest run writes; verify all created pages, index entries, and log entries are
complete and consistent, and that answer streaming latency is unaffected.

**Acceptance Scenarios**:

1. **Given** concurrent wiki-writing activity (ingest and synthesis-preserving
   query turns), **When** all runs finish, **Then** every page, index entry, and
   log entry is complete and consistent — no partial writes, lost updates, or
   corrupted entries.
2. **Given** a query turn whose synthesis write must coordinate with other
   writers, **When** the answer streams, **Then** streaming and interruption
   behavior remain within the established responsiveness guarantees — write
   coordination never blocks the conversational experience.

---

### Edge Cases

- What happens when the wiki already has a page covering the "insight"? No page is
  created — restating existing content is not a Synthesis (Story 1, scenario 4).
  Whether an existing synthesis page should be updated rather than duplicated
  follows the same update-over-duplicate judgment the ingest conventions
  establish.
- What happens when the turn is interrupted after the Synthesis Page was created?
  The page, index entry, and log entry remain — wiki writes are never rolled back
  by interruption; the turn record lists the created page and the interrupted
  state.
- What happens when the synthesis write is denied by policy (e.g. a
  misconfiguration narrows the scope)? The answer still completes and is delivered;
  the denial is recorded with a reason; the insight is simply not preserved.
- What happens when two concurrent turns synthesize overlapping insights? Both
  writes are preserved intact (integrity guarantee); consolidating near-duplicate
  syntheses is the Lint agent's job (feature 013), not a harness merge.
- What happens on a follow-up to a turn that created a page? The conversation
  context includes that the insight was preserved, so the agent can reference the
  page instead of re-synthesizing it.
- What happens when the user explicitly asks "save this as a page"? The request
  is input to the Synthesis Decision like any other; if the answer holds no
  genuinely new insight, the agent may decline and say why — the judgment stays
  with the agent, within its Write Scope.

## Requirements *(mandatory)*

### Functional Requirements

**Synthesis capability**

- **FR-001**: The Query agent MUST be able to preserve a Synthesis as a new wiki
  page through guarded write tools, within its declared Write Scope: creating new
  Synthesis Pages and maintaining the wiki index and log. This supersedes the
  feature-008 requirement that the Query agent has no wiki-write capability at
  all.
- **FR-002**: The Synthesis Decision — whether an answer contains an insight worth
  preserving, and what the page says — MUST be exercised by the agent under its
  versioned instruction files. No deterministic backend heuristic (length checks,
  keyword rules, novelty scores) may make or veto this decision.
- **FR-003**: Every Synthesis Page MUST carry the wiki's standard frontmatter —
  at least two tags including the synthesis source-type marker, a confidence score
  with reason, and a review date — link the pages it drew from, be entered in the
  wiki index, and produce a log entry attributing it to the query that created it.
- **FR-004**: When a turn creates a Synthesis Page, the answer MUST tell the user
  and name the page, and the turn's persistent record MUST list every page the
  turn created.

**Guardrails**

- **FR-005**: All Query agent write actions MUST pass the guarded tool boundary
  under a versioned deny-by-default policy evaluated at invocation time. Actions
  outside the Write Scope — including modifying existing content pages and
  writing outside the wiki — MUST be denied and recorded with reasons while the
  run continues.
- **FR-006**: Instruction-like text inside wiki pages MUST NOT be able to widen
  the Write Scope; policy enforcement is independent of content the agent reads.
- **FR-007**: The structural guarantee established for the query path MUST be
  updated, not dropped: agent-side code performs no wiki writes outside the
  guarded tool layer, verified by an automated structural rule with a Red/Green
  probe reflecting the new Write Scope.
- **FR-008**: Changing synthesis behavior — what counts as an insight, page
  style, citation and confidence conventions — MUST require editing only the
  Query agent's instruction files, no backend change.

**Concurrency & integrity**

- **FR-009**: Concurrent wiki-writing activity (ingest runs and
  synthesis-preserving query turns, in any combination) MUST NOT corrupt the
  wiki: pages, index, and log remain complete and consistent under concurrency.
  The coordination mechanism is a planning decision, but the integrity guarantee
  is absolute.
- **FR-010**: Write coordination MUST NOT degrade the conversational experience:
  streaming and interruption stay within the responsiveness guarantees
  established by feature 008.
- **FR-011**: Wiki writes MUST NOT be rolled back by turn interruption or
  failure: pages created before the terminal state remain, and the turn record
  reflects both the created pages and the terminal state.

### Key Entities

- **Synthesis Page**: A wiki page like any other, distinguished by its synthesis
  source-type marker and its origin attribution (created by a query turn).
  Relationships: links to the pages it synthesized from; referenced by index, log,
  and the creating turn's record.
- **Write Scope**: The versioned policy defining the Query agent's permitted
  write actions: create Synthesis Pages, maintain index and log — nothing else.
- **Turn Record extension**: The per-turn record (or Conversation Record, if
  feature 011 has landed) gains the list of pages created by the turn.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees (100%)**

- **SC-001**: 100% of Query agent write actions pass through the guarded tool
  boundary with a policy decision recorded; 100% of out-of-scope attempts are
  denied with recorded reasons while the run continues.
- **SC-002**: 100% of pages created by a query turn are listed on that turn's
  persistent record, and each has a corresponding index entry and log entry
  produced by the same turn's activity.
- **SC-003**: Under concurrent write activity, 100% of completed writes are intact
  — no partial pages, lost index/log updates, or interleaved corruption — and
  streaming/interruption responsiveness guarantees from feature 008 continue to
  hold.
- **SC-004**: The updated structural write-boundary rule passes with a verified
  Red/Green probe; 100% of agent-side wiki writes occur through the guarded tool
  layer.

**Agent-judgment evaluation thresholds**

- **SC-005**: ≥ 85% of sampled turns whose answers contain a genuinely new
  insight (per the evaluation rubric) preserve it as a Synthesis Page.
- **SC-006**: ≥ 90% of sampled routine-lookup turns (answers that restate
  existing pages) create no page.
- **SC-007**: ≥ 95% of sampled created Synthesis Pages carry complete, convention-
  conforming frontmatter (tags incl. synthesis marker, confidence with reason,
  review date) and link at least one source page.
- **SC-008**: ≥ 90% of sampled prompts asking the agent to edit existing wiki
  content receive an answer that declines and explains the boundary (while SC-001
  guarantees the edit never happens regardless).

## Assumptions

- **This feature supersedes accepted decisions**: feature 008's "no wiki-write
  capability at all" (and its structural enforcement rationale) is deliberately
  replaced. Planning MUST produce a superseding architecture decision record
  covering the new Write Scope, the write-coordination mechanism (FR-009/FR-010 —
  e.g. funneling writes through the established single-writer discipline or an
  equivalent), and the updated structural rule. The writer-coordination decision
  is expected to be shared with the Lint agent (feature 013).
- **Depends on feature 010**: The consolidated agent platform decides how an
  agent's tool set is declared; this feature changes the Query profile's declared
  tools and policy. Implementing it before 010 would mean redoing the work.
- **Relation to feature 011**: If Conversation Records have landed, created pages
  are listed in the turn's section of the record; otherwise on the per-turn
  artifact. The requirement (FR-004) is persistence-shape-agnostic.
- **Update-vs-create for syntheses**: Judgment about updating an existing
  synthesis page instead of creating a near-duplicate follows the wiki's
  established update-over-duplicate conventions and is governed by instruction
  files; duplicate consolidation at scale is the Lint agent's concern.
- **Single-user context**: Unchanged; no per-user attribution beyond the existing
  operator model.

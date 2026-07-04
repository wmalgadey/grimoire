# Feature Specification: Agentic Ingest Core

**Feature Branch**: `002-agentic-ingest-core`

**Created**: 2026-07-04

**Status**: Draft

**Input**: User description: "Agentic Ingest Core — Replace the deterministic ingest
pipeline with an agent-driven execution model, building on the foundation of
001-ingest-minimal. An ingest run is performed by an LLM agent that operates under
governing instruction files loaded into its working context, explores the current wiki
state through read tools, decides with its own judgment which pages to create, update,
or supersede, and applies every change exclusively through guarded write tools enforcing
a versioned, deny-by-default guardrail policy. All wiki-maintenance rules live in the
instruction set, not in backend code."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An agent that knows the wiki performs the ingest (Priority: P1)

A user submits a single source (document, URL, or pasted text). The ingest is carried
out by an agent that first looks at what the wiki already contains, then uses its own
judgment to integrate the source: updating pages that already cover the topic, creating
pages that are genuinely missing, and marking older knowledge as superseded when the new
source clearly replaces it. The result is a coherent, connected wiki update — not an
isolated summary file produced by a fixed procedure.

**Why this priority**: This is the reason the feature exists. The compounding value of
the wiki depends on every ingest being an act of integration into existing knowledge,
which requires judgment informed by the current wiki state. Without this, the system is
a file generator, not a wiki maintainer.

**Independent Test**: Seed a wiki with a few pages on a topic, ingest a source that
overlaps with one of them, and verify the agent consulted the existing wiki and chose to
update or supersede rather than duplicate — and that a source on a genuinely new topic
results in new, linked pages.

**Acceptance Scenarios**:

1. **Given** a wiki page already covers a source's topic, **When** the user ingests that
   source, **Then** the existing page is updated or clearly superseded rather than a
   duplicate page being created.
2. **Given** a source introduces a topic the wiki does not cover, **When** ingest
   completes, **Then** new pages exist that represent the topic and are connected to
   related existing pages.
3. **Given** any completed ingest, **When** the user reads the run's record, **Then** it
   explains which pages the agent chose to touch and why those were the right ones.

---

### User Story 2 - Wiki behavior is governed by editable instructions (Priority: P2)

The rules for how the wiki is maintained — what kinds of pages exist, what metadata each
page carries, how pages are tagged and rated for confidence, when a page supersedes
another, and how the catalog and log are kept current — are written in human-readable
instruction files. When the user edits those instructions, the very next ingest follows
the new rules. Changing wiki behavior never requires changing or redeploying the system
itself.

**Why this priority**: This is the differentiator from the previous, deterministic
approach. It keeps the human in control of wiki conventions as they evolve, and it keeps
the system honest: the instructions the user reads are the instructions the agent
actually follows.

**Independent Test**: Edit the instruction files to add a new required page convention
(for example, an additional metadata field), run an ingest without touching anything
else, and verify the newly produced pages follow the new convention.

**Acceptance Scenarios**:

1. **Given** the active instruction files, **When** an ingest runs, **Then** the pages
   it produces follow the conventions those files describe (page types, metadata,
   tagging, supersession, catalog upkeep).
2. **Given** the user changes a wiki convention in the instruction files, **When** the
   next ingest runs, **Then** its output follows the changed convention with no other
   intervention.
3. **Given** the instruction files are missing or unreadable, **When** an ingest is
   attempted, **Then** the run stops before any wiki change and reports the problem
   clearly.

---

### User Story 3 - Agent actions are bounded and transparent (Priority: P3)

Every action the agent takes runs against an explicit, versioned safety policy. The
agent can only write within the wiki and its own run records; reads are limited to
approved project context. If the agent attempts something outside its authority —
whether by error or because a malicious source tried to redirect it — that single action
is denied and recorded with its reason, and the run continues with its remaining allowed
work. The user can always see afterwards what was attempted, what was allowed, and what
was denied.

**Why this priority**: The agent writes into a trusted knowledge artifact. Autonomy is
only acceptable when its boundaries are enforced at the moment of action and every
boundary violation is visible after the fact.

**Independent Test**: Craft a source containing instructions that try to make the agent
write outside the wiki or read a forbidden location, run the ingest, and verify the
out-of-scope actions were denied and recorded while the legitimate wiki update still
completed.

**Acceptance Scenarios**:

1. **Given** an agent action targets a location outside the allowed write scope,
   **When** the action is attempted, **Then** it is denied, the denial is recorded with
   action, target, and reason, and the run continues with allowed actions.
2. **Given** a source document containing embedded instructions to the agent, **When**
   ingest runs, **Then** the source's content cannot expand the agent's authority beyond
   the active policy, and any denied attempts appear in the run record.
3. **Given** a completed run, **When** the user reviews its record, **Then** every
   denied action is listed alongside the changes that were made.

---

### Edge Cases

- A source contains text that addresses the agent directly (prompt injection) and asks
  it to ignore its instructions, exfiltrate content, or write elsewhere — the policy
  boundary must hold regardless of source content, and attempts must be visible.
- The safety policy is misconfigured so that every write is denied — the run must end
  with a clear record showing all intended actions were denied, not silently succeed
  with an empty result.
- The instruction files are absent, unreadable, or empty — the run must stop visibly
  before any wiki change rather than proceed with undefined behavior.
- The wiki has grown large — the agent must still ground its decisions in the current
  wiki state (via the catalog and relevant pages) rather than ignoring existing content.
- The agent fails partway through a multi-page update — the wiki must be left as it was
  before the run began.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to submit exactly one raw source (document, URL, or
  pasted text) to trigger a single ingest run, as established in 001-ingest-minimal.
- **FR-002**: Every ingest run MUST be executed by an agent whose working context
  contains the active governing instruction files (the agent's operating rules and its
  skill process definitions) before any wiki-affecting action; recording or referencing
  the files without them governing the agent's context does not satisfy this
  requirement.
- **FR-003**: If the governing instruction files cannot be loaded, the run MUST stop
  before any wiki change and report a clear, human-readable reason.
- **FR-004**: Before deciding on changes, the agent MUST be able to explore the current
  wiki state — at minimum the catalog and the content of pages it considers relevant —
  through read access bounded by the safety policy.
- **FR-005**: The decision to create, update, or supersede wiki pages MUST be made by
  the agent's own judgment informed by the current wiki state; the system MUST NOT
  impose a deterministic lookup or matching rule in place of that judgment.
- **FR-006**: Every write action of the agent MUST pass through guarded operations that
  enforce a versioned, deny-by-default safety policy permitting writes only to wiki
  content and the run's own task-artifact outputs.
- **FR-007**: Read access during a run MUST be limited to the approved context defined
  in the versioned safety policy.
- **FR-008**: When an action violates the safety policy, the system MUST deny only that
  action, record it with action, target, and reason, and continue the run with the
  remaining allowed actions.
- **FR-009**: Content originating from the submitted source MUST NOT be able to expand
  the agent's authority beyond the active safety policy and instruction files.
- **FR-010**: All wiki-maintenance conventions — page types, page metadata, tagging,
  confidence rating, supersession, and catalog and log upkeep — MUST be defined solely
  in the instruction files; a change to these conventions MUST take effect on the next
  run without any change to the system itself.
- **FR-011**: Every ingest run MUST produce and maintain a task artifact through its
  lifecycle (created at submission, status kept current, final record on completion or
  failure) that lists pages created, updated, and superseded, all denied actions, and a
  human-readable account of what was done and why — preserving the guarantees of
  001-ingest-minimal.
- **FR-012**: Each run's record MUST identify which versions of the instruction files
  and safety policy governed it, so any wiki change can be traced to the rules in force
  at the time.
- **FR-013**: A failed run MUST leave the wiki in the state it was in before the run
  began — no partial, orphaned, or contradictory pages.
- **FR-014**: The submitted source MUST remain unmodified by the run.
- **FR-015**: Every run, successful or failed, MUST append an entry to the chronological
  ingest log; interrupted runs MUST be reconciled to a failed state on restart, as
  established in 001-ingest-minimal.
- **FR-016**: After a successful run, every page the run touched MUST be discoverable
  from the wiki catalog, with catalog upkeep performed according to the instruction
  files.

### Key Entities *(include if feature involves data)*

- **Source**: The raw, human-provided input submitted for ingest. Immutable — read,
  never modified.
- **Wiki Page**: A markdown file of synthesized knowledge, created and maintained by the
  agent according to the instruction files.
- **Instruction Set**: The versioned, human-readable files defining the agent's
  operating rules and its skill processes — the single home of all wiki-maintenance
  conventions, and the material actually present in the agent's working context.
- **Safety Policy**: The versioned, deny-by-default definition of what the agent may
  read and write during a run.
- **Task Artifact**: The per-run record with structured status and a human-readable
  narrative: pages created/updated/superseded, denied actions, governing
  instruction/policy versions, and outcome.
- **Denied Action Record**: One attempted action that the policy refused, with action,
  target, and reason.
- **Wiki Catalog (index.md)**: The browsable, category-organized listing of wiki pages,
  kept current per the instruction files.
- **Ingest Log (log.md)**: The append-only chronological record of every run and its
  outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

#### Harness guarantees (deterministic, 100%)

- **SC-001**: 100% of ingest runs produce exactly one task artifact that traverses its
  lifecycle and ends in a completed or failed state, including after interruption and
  restart.
- **SC-002**: 100% of attempted actions outside the safety policy are denied and
  recorded with action, target, and reason, while the run continues with allowed
  actions.
- **SC-003**: 100% of runs that perform any wiki change had the governing instruction
  files present in the agent's working context before the first change; 100% of runs
  where the instructions could not be loaded perform zero wiki changes and end visibly
  failed.
- **SC-004**: 100% of failed runs leave the wiki exactly as it was before the run.
- **SC-005**: 100% of runs append a log entry and record the identity of the governing
  instruction files and safety policy in their task artifact.

#### Agent-judgment quality (evaluation thresholds on sampled runs)

- **SC-006**: In evaluation samples where the wiki already covers the source's topic,
  at least 90% of runs update or supersede the existing page rather than create a
  duplicate.
- **SC-007**: At least 95% of pages created or updated in evaluation samples follow the
  conventions defined in the active instruction files (page types, metadata, tagging,
  supersession marking).
- **SC-008**: In at least 95% of sampled successful runs, every touched page is
  discoverable from the wiki catalog afterwards.
- **SC-009**: After a deliberate instruction-file change in an evaluation setting, at
  least 90% of subsequent sampled runs follow the changed convention with no system
  change.
- **SC-010**: In evaluation samples containing adversarial sources (embedded
  instructions to the agent), 100% of out-of-scope actions are denied by policy, and in
  at least 90% of samples the legitimate wiki update still completes.

## Assumptions

- The harness established by 001-ingest-minimal (submission channels, run dispatch,
  operational state with restart reconciliation, credential handling, observability) is
  retained; this feature replaces only the ingest execution core.
- The previous deterministic wiki-writing pipeline is fully replaced, not kept as a
  fallback mode.
- Wiki content, task artifacts, catalog, and log remain plain markdown files under
  version control, readable and editable outside the running system.
- The initial content of the instruction files is derived from the existing LLM-wiki
  skill material in the project's documentation (page types, frontmatter standard, tag
  taxonomy, confidence scoring, supersession rules); refining those conventions is
  ongoing editorial work, not part of this feature's system scope.
- Evaluation samples for agent-judgment criteria are produced from real or recorded
  agent runs against representative seed wikis and sources; the thresholds in SC-006
  through SC-010 are initial values and may be tightened as the wiki matures.
- A single trusted user/developer context is assumed; authentication and authorization
  are out of scope, as in 001-ingest-minimal.
- One source per run; batch ingest, Query, and Lint remain out of scope and are later
  features.

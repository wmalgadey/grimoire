# Feature Specification: Lint Agent — Wiki Health Check

**Feature Branch**: `013-lint-agent`

**Created**: 2026-07-27

**Status**: Implemented — completed 2026-07-30

**Input**: User description: "Add the next agent: lint. It is the same as ingest
and query — same platform, different intent. The lint agent performs the wiki
health check from the original wiki-maintenance conventions: find contradictions,
outdated claims, missing cross-references, scattered concepts, gaps, missing
tags/confidence metadata, stale low-confidence pages, superseded pages, and orphan
pages; refresh inbound-link counts; produce a findings report."

## Terminology

- **Lint Agent**: The third wiki agent. Its intent is wiki health: it reads the
  whole wiki, judges its condition, and reports findings. Like Ingest and Query it
  runs on the shared agent platform under its own system prompt, tool set, and
  guardrail policy.
- **Lint Run**: One dispatched execution of the Lint agent over the wiki,
  producing one Findings Report and refreshed link metadata.
- **Findings Report**: The persistent, human-readable result of a Lint Run:
  findings grouped by category, each naming the affected pages, describing the
  problem, and proposing a remediation.
- **Finding Categories**:
  - *Content quality*: contradictions between pages; claims made outdated by newer
    material; missing cross-references between related pages; concepts scattered
    across pages that deserve a page of their own; coverage gaps.
  - *Metadata hygiene*: pages missing tags (with proposed tags); pages missing a
    confidence score (with a proposed score and reason); low-confidence pages
    whose last review is older than the review window (review candidates); an
    informational list of superseded pages.
  - *Structure*: orphan pages with no inbound links, with suggestions where they
    could be linked from.
- **Inbound-Link Refresh**: Updating each page's recorded inbound-link count in
  its frontmatter to match the actual links in the wiki — deliberately done at
  lint time, not at ingest time, because it requires a whole-wiki view.
- **Review Window**: The age threshold (default: 90 days since last review) after
  which a low-confidence page becomes a review candidate.

## Clarifications

### Session 2026-07-28

- Q: Should lint also fix the problems it finds, or only report them? → A:
  Report-only. The Findings Report proposes remediations; the single write
  action remains the mechanical Inbound-Link Refresh in page frontmatter.
  Applying fixes (metadata or content) is future work, out of scope here.
- Q: How are lint runs triggered? → A: Manually from the Web UI, like ingest
  submissions. Scheduled/periodic runs are out of scope for this feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run lint, read the findings (Priority: P1)

The user triggers a lint run from the Web UI. The Lint agent reads the wiki and
produces a Findings Report: contradictions it noticed, claims that newer pages
supersede, related pages that don't reference each other, concepts scattered
across pages, gaps worth filling, metadata problems, and orphaned pages — each
finding naming the pages involved and proposing what to do. The user reads the
report and knows the state of their wiki.

**Why this priority**: The report is the product of this feature. Every other
story refines what lint checks or writes; without the report there is no lint.

**Independent Test**: Seed a wiki fixture with known defects (a contradiction, an
orphan, a page without tags, two related-but-unlinked pages); trigger a lint run;
verify a Findings Report is produced and the seeded defects appear as findings in
their categories at the thresholds defined in Success Criteria.

**Acceptance Scenarios**:

1. **Given** the Web UI, **When** the user triggers a lint run, **Then** the Hub
   dispatches a Lint agent run with the Lint System Prompt Document as its entire
   system prompt (fail-closed on missing/unreadable/empty, hash recorded), and the
   run's progress and outcome are visible like other agent runs.
2. **Given** a finished lint run, **When** the user opens the Findings Report,
   **Then** findings are grouped by category, each naming affected pages,
   describing the problem, and proposing a remediation.
3. **Given** a wiki with seeded, known defects, **When** sampled lint runs are
   evaluated, **Then** the seeded defects are found at the thresholds defined in
   Success Criteria, and sampled findings are genuine (not fabricated problems).
4. **Given** an empty or healthy wiki, **When** a lint run completes, **Then** the
   report says so plainly — an empty findings list is a valid, honest result.

---

### User Story 2 - Metadata gets refreshed and proposed (Priority: P2)

After a lint run, every wiki page's inbound-link count matches reality again, and
the report proposes concrete metadata for pages that lack it: suggested tags
following the wiki's tag taxonomy, and a proposed confidence score with its
reasoning following the wiki's scoring conventions. Pages whose low confidence has
gone unreviewed past the Review Window are flagged as review candidates.

**Why this priority**: The confidence conventions depend on inbound-link counts
and review dates staying current; without this story the wiki's self-assessment
decays. It is the one place lint writes, and it feeds the ingest agent's future
judgments.

**Independent Test**: Seed pages with stale inbound-link counts, missing tags,
missing confidence, and an old low-confidence page; run lint; verify counts are
corrected in frontmatter, the report proposes taxonomy-conforming tags and a
convention-conforming score with reason, and the stale page is flagged.

**Acceptance Scenarios**:

1. **Given** pages whose recorded inbound-link counts differ from the actual
   links in the wiki, **When** a lint run completes, **Then** the counts in page
   frontmatter are updated to match reality, and these are the only page
   modifications the run performs.
2. **Given** a page without tags, **When** the report is read, **Then** it
   proposes tags conforming to the wiki's tag taxonomy (at least one category
   tag and one content tag).
3. **Given** a page without a confidence score, **When** the report is read,
   **Then** it proposes a score with reasoning that follows the wiki's confidence
   conventions.
4. **Given** a low-confidence page last reviewed before the Review Window,
   **When** the report is read, **Then** the page is listed as a review
   candidate.

---

### User Story 3 - Lint can only do lint things (Priority: P3)

An operator trusts lint runs because the Lint agent's capabilities are exactly
what its job needs: it reads the wiki, it updates link-count metadata in page
frontmatter, and it writes its report — nothing else. It cannot rewrite page
content, cannot create or delete pages, and no text inside a wiki page can talk
it into more. Every denied attempt is recorded with a reason while the run
continues.

**Why this priority**: The standing guarantee that makes an autonomous
whole-wiki agent acceptable. Invisible when it works; constitutionally required.

**Independent Test**: Drive a lint run toward out-of-scope writes (edit page
body, delete a page, write outside the wiki); verify denial at the tool boundary
with recorded reasons, and verify the structural rule that lint-path agent code
cannot write outside the guarded tool layer (Red/Green probe).

**Acceptance Scenarios**:

1. **Given** any lint run, **When** the agent operates, **Then** its capabilities
   are guarded tools scoped to reading the wiki, updating link-count frontmatter,
   and producing its report — enforced deny-by-default at invocation time.
2. **Given** an attempted out-of-scope action (page content edit, page creation
   or deletion, access beyond the wiki), **When** the policy is applied, **Then**
   the attempt is denied and recorded with a reason and the run continues.
3. **Given** wiki pages containing instruction-like injected text, **When** the
   Lint agent reads them, **Then** that text cannot widen its capabilities.
4. **Given** a lint run that dies or hangs, **When** the liveness window elapses,
   **Then** the run is marked failed with a reason, leftover processes are
   terminated, and any partial report is clearly marked as partial.

---

### Edge Cases

- What happens when lint runs on an empty wiki? The run completes with an honest
  "nothing to lint" report; no findings are fabricated.
- What happens when the wiki changes mid-run (a concurrent ingest or synthesis
  write lands)? The run works with the content it reads; findings may reflect the
  pre-change state, which the next run corrects. Frontmatter link-count updates
  must still not corrupt concurrently written pages (integrity requirement).
- What happens when two lint runs are triggered at once? One lint run at a time:
  a second trigger while one is active is rejected with a clear "busy" message —
  wiki-wide analysis of a moving target has no benefit from parallelism.
- What happens when the report would be enormous (hundreds of findings)? The
  report is complete but ordered by category and severity of consequence
  (contradictions before missing tags); nothing is silently truncated.
- What happens to previous reports? Each run produces its own report; earlier
  reports remain readable — the history shows whether wiki health trends better
  or worse.
- What happens if the report says a page lacks tags but a concurrent edit just
  added them? The finding is stale, not wrong — findings are proposals tied to
  the state the run saw, and the next run clears them.
- Does lint fix the problems it finds? No — beyond the Inbound-Link Refresh,
  this feature reports and proposes only. Applying content fixes (adding
  cross-references, consolidating scattered concepts, resolving contradictions)
  is future work, likely via ingest-style runs.

## Requirements *(mandatory)*

### Functional Requirements

**Dispatch & lifecycle**

- **FR-001**: The Web UI MUST let the user trigger a Lint Run and observe its
  progress and outcome, following the run-visibility patterns established for the
  other agents.
- **FR-002**: The Lint agent MUST receive its operating instructions from exactly
  one versioned Lint System Prompt Document, loaded verbatim as its entire system
  prompt, fail-closed (missing, unreadable, or effectively empty documents fail
  the run before any agent output, with a human-readable reason), with identity
  and content hash recorded on the run's record.
- **FR-003**: At most one Lint Run may be active at a time; a trigger while one
  is active MUST be rejected immediately with a clear message.
- **FR-004**: Lint Runs MUST be supervised with the established liveness
  approach: a silent run is marked failed with a reason and leftover processes
  are terminated.
- **FR-005**: Every Lint Run MUST produce a persistent run record (trigger time,
  instruction identity and hash, outcome state with reason, denied actions) and
  each run's Findings Report MUST be persistent and remain readable after later
  runs.

**Findings**

- **FR-006**: A Lint Run MUST examine the wiki across all three Finding
  Categories (content quality, metadata hygiene, structure) and produce a
  Findings Report grouping findings by category, each naming the affected pages,
  describing the problem, and proposing a remediation.
- **FR-007**: The judgment of what constitutes a finding — whether two pages
  contradict, whether a claim is outdated, whether pages are related, whether a
  concept deserves its own page, what tags or confidence score to propose — MUST
  be exercised by the agent under its instruction files. No deterministic backend
  rules may generate or suppress content-quality findings.
- **FR-008**: Proposed tags MUST follow the wiki's tag taxonomy and proposed
  confidence scores MUST follow the wiki's confidence conventions, as those
  conventions are stated in the agents' instruction files.
- **FR-009**: Review candidates MUST be determined against a configurable Review
  Window (default 90 days) applied to low-confidence pages' last-review dates.

**Writes & guardrails**

- **FR-010**: The Lint agent's write capability MUST be limited, via the guarded
  tool boundary's versioned deny-by-default policy, to updating inbound-link
  counts in page frontmatter and producing its report. Page content edits, page
  creation/deletion, and any access beyond the wiki MUST be denied and recorded
  with reasons while the run continues.
- **FR-011**: After a completed Lint Run, every wiki page's recorded
  inbound-link count MUST match the actual inbound links in the wiki as read
  during that run.
- **FR-012**: Instruction-like text inside wiki pages MUST NOT be able to widen
  the Lint agent's capabilities.
- **FR-013**: An automated structural rule MUST verify lint-path agent code
  performs no wiki writes outside the guarded tool layer, proven live with a
  Red/Green probe.
- **FR-014**: Lint frontmatter updates MUST NOT corrupt pages under concurrent
  write activity by other agents; the coordination mechanism follows the
  writer-coordination decision shared with feature 012.

**Boundary**

- **FR-015**: Changing what lint looks for, how findings are described, or how
  proposals are made MUST require editing only the Lint agent's instruction
  files — no backend change.

### Key Entities

- **Lint Run**: One execution; attributes: identity, trigger time, instruction
  identity/hash, outcome state and reason, denied actions.
- **Findings Report**: Per-run document; findings grouped by Finding Category,
  each with affected pages, description, and proposed remediation; ordered, never
  silently truncated; marked partial if the run did not complete.
- **Finding**: One observed problem; attributes: category, affected pages,
  description, proposed remediation.
- **Review Candidate**: A page flagged by the Review Window rule; appears in the
  report's metadata-hygiene section.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees (100%)**

- **SC-001**: 100% of lint runs load the Lint System Prompt Document fail-closed
  with hash recorded; 100% of runs produce a persistent run record and a
  persistent Findings Report (or a failed state with reason and any partial
  report marked partial).
- **SC-002**: 100% of Lint agent write actions pass the guarded tool boundary;
  100% of out-of-scope attempts are denied with recorded reasons; the structural
  no-unguarded-writes rule passes with a verified Red/Green probe.
- **SC-003**: 100% of concurrent-trigger attempts while a lint run is active are
  rejected with a clear message; 100% of dead runs are detected within the
  liveness window.
- **SC-004**: 100% of frontmatter updates performed by lint are intact under
  concurrent write activity — no corrupted or lost page content.

**Agent-judgment evaluation thresholds**

- **SC-005**: On wiki fixtures with seeded, known defects, ≥ 85% of seeded
  defects are found, per category (contradiction, missing cross-reference,
  missing tags, missing confidence, orphan, stale low-confidence page).
- **SC-006**: ≥ 90% of sampled findings are genuine — the described problem
  exists in the pages named — as judged by the evaluation rubric.
- **SC-007**: ≥ 90% of sampled tag proposals conform to the tag taxonomy, and
  ≥ 90% of sampled confidence proposals follow the confidence conventions with a
  coherent stated reason.
- **SC-008**: ≥ 95% of sampled pages have an accurate inbound-link count after a
  lint run (accuracy measured against the wiki state the run read).

## Assumptions

- **Depends on feature 010**: Lint is built as a new Agent Profile on the
  consolidated platform — it is the practical proof of that feature's "adding an
  agent requires only a profile" requirement. Building lint before 010 lands
  would mean building it twice.
- **Writer coordination is shared with feature 012**: The decision on how
  concurrent wiki writers coordinate (made during planning of 012/013 as one
  superseding architecture decision) governs lint's frontmatter updates; this
  spec pins only the integrity outcome (FR-014, SC-004).
- **Report-only scope**: Lint reports and proposes; it does not apply content
  fixes (see Edge Cases). The single exception is the mechanical Inbound-Link
  Refresh, which requires the whole-wiki view only lint has.
- **Findings Reports are operational records**: They live with harness
  bookkeeping outside the wiki content, like other run artifacts, and are
  readable as plain documents. Turning findings into wiki content (or an in-UI
  findings browser) is out of scope.
- **Trigger model**: Manual trigger from the Web UI, like ingest submissions.
  Scheduled/periodic lint runs are out of scope for this feature.
- **Single lint at a time, concurrent with others**: Lint runs alongside ingest
  and query activity (subject to the shared writer coordination); only
  lint-with-lint concurrency is excluded.
- **Single-user context**: Unchanged from prior features.

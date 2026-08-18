# Feature Specification: Agent-Owned, Newest-First Wiki Activity Log

**Feature Branch**: `025-agent-owned-log`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: operator review of a produced wiki's `log.md` (2026-08-16),
captured as GitHub issue [#89](https://github.com/wmalgadey/grimoire/issues/89). Verbatim
operator input, preserved as a record of the request (everything derived from it in this
document is English, per the project language policy):

> das log.md sieht nicht so aus, wie ich das erwarten würde. der agent soll das log anlegen
> und pflegen (genau wie die index.md), nicht der harness und es soll immer oben hinzugefügt
> werden. dabei muss nicht berücksichtigt werden, dass schon einträge am jeweiligen tag
> bereits erzeugt wurden, es ist ok, wenn je geloggter "Aktion" ein eintrag inkl.
> tages-überschrift entsteht. es sollen auch nur positive einträge geloggt werden, die zu
> änderungen im wiki geführt haben, keine sonstigen tasks oder aufgaben, die evtl. nur zur
> kommunikation mit dem benutzer dienen

## Problem Statement

The wiki's activity log is meant to be the wiki's own, agent-maintained record of what
happened to it — the counterpart to the wiki's index. Today it deviates from that
expectation in four ways:

1. **The harness co-authors the log.** A harness-side backstop writes its own entries
   whenever it cannot find the run's correlation reference in the file, and always when a
   run fails. The operator expects the agent to create and maintain the log exactly as it
   maintains the index, with no harness-authored content in the file at all.
2. **Entries are ordered oldest-first.** New entries land at the end of the file, so the
   most recent activity is the hardest to find. The expectation is newest-first. This is
   not merely a convention today: the guarded-write layer actively denies any log write
   whose proposed content does not begin with the current content byte-for-byte, so the
   guardrail structurally prevents the desired behaviour.
3. **Day-grouping is ambiguous.** It is unclear whether an agent should merge a new action
   into an existing section for the same date. The operator explicitly does not want
   day-grouping: one logged action produces one complete entry with its own date heading.
4. **Non-changing runs are logged.** Failures and question-answering turns that changed
   nothing end up in the log. The log should contain only entries that record an actual
   change to wiki content.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the newest wiki change first (Priority: P1)

As a wiki operator, I want the most recent change to the wiki to appear at the very top of
the activity log, so that opening the file shows me at a glance what the agents most
recently changed, without scrolling to the end of a growing file.

**Why this priority**: This is the operator's primary complaint and the reason the file is
currently hard to use. It also requires inverting a structural guardrail that today denies
exactly this behaviour, so nothing else in this feature can land without it.

**Independent Test**: Run two consecutive agent runs that each change wiki content, then
open the activity log: the second run's entry appears above the first run's entry, and the
first run's entry is present and unmodified below it.

**Acceptance Scenarios**:

1. **Given** an activity log with existing entries, **When** an agent run changes wiki
   content, **Then** the new entry appears above all existing content and every existing
   entry is preserved byte-for-byte below it.
2. **Given** a content root with no activity log yet, **When** an agent run changes wiki
   content, **Then** the log is created and the first entry is written into it.
3. **Given** an agent attempting a log write that would rewrite, reorder, or remove any
   existing content, **When** the write is submitted, **Then** the guarded write is denied
   with a recorded reason and the file is left unchanged.
4. **Given** two agent runs prepending to the activity log at the same time, **When** the
   second write is evaluated against content that changed after it was read, **Then** the
   existing stale-read conflict handling still applies and no entry is lost or corrupted.

---

### User Story 2 - Trust that every entry is an agent-written record of a real change (Priority: P1)

As a wiki operator, I want every line in the activity log to have been written by an agent
about a change that actually landed in the wiki, so the log is a trustworthy record of the
wiki's history rather than a mixture of wiki content and run bookkeeping.

**Why this priority**: Equal in importance to ordering — a newest-first log that still
contains harness fallback text and entries for runs that changed nothing does not solve
the operator's problem. It is also the constitutional driver: log content is wiki content
and therefore belongs to the agent, not the harness.

**Independent Test**: Run a question-answering turn that writes nothing and a run that
fails before changing anything, then open the activity log: it is unchanged, and no
harness-generated fallback text appears anywhere in the file.

**Acceptance Scenarios**:

1. **Given** a content root with no activity log, **When** an agent run changes wiki
   content, **Then** the agent itself creates the file and writes the entry, and no
   harness component writes to it at any point in the run.
2. **Given** a query run that answers the user's question without writing any wiki content,
   **When** the run completes, **Then** the activity log is unchanged.
3. **Given** an agent run that fails before changing any wiki content, **When** the run
   completes, **Then** the activity log is unchanged and no fallback entry is written.
4. **Given** an agent run that changed wiki content but failed partway through (for example
   it created a page but did not complete the index update), **When** the run completes,
   **Then** exactly one entry is present describing the change that actually landed.

---

### User Story 3 - One complete entry per logged action (Priority: P2)

As a wiki operator, I want each logged action to appear as its own complete entry with its
own date heading, so I can see individual actions rather than a merged daily digest — and
so no agent spends judgment deciding whether to fold a change into an existing day's
section.

**Why this priority**: Removes a real ambiguity in agent behaviour that produces
inconsistent files, but the log is already usable once ordering and authorship (Stories 1
and 2) are correct.

**Independent Test**: Trigger two wiki-changing runs on the same calendar day, then open the
activity log: two separate complete entries are present, each with its own date heading,
neither merged into the other.

**Acceptance Scenarios**:

1. **Given** an activity log that already contains an entry dated today, **When** another
   action is logged on the same day, **Then** a second complete entry with its own date
   heading is added at the top rather than merged into the existing day's section.
2. **Given** two entries that happen to share an identical heading (same date, type, and
   summary), **When** an operator reads the log, **Then** both entries are present and each
   remains independently locatable by the existing heading pattern.

---

### User Story 4 - Keep run bookkeeping visible in operational signals (Priority: P2)

As an operator debugging a run that produced nothing, I want the fact that a run happened —
and that it changed nothing or failed — to remain visible in the system's own operational
signals and task records, so that removing harness-authored entries from the wiki's
activity log costs me no operational visibility.

**Why this priority**: This is the safety net for Story 2. It does not block the operator's
requested behaviour but must be confirmed before the harness fallback entries are removed,
otherwise a real diagnostic capability is silently lost.

**Independent Test**: Trigger a failed run and a no-write run, then confirm that both are
fully accounted for in the system's operational signals and task records, without consulting
the wiki's activity log.

**Acceptance Scenarios**:

1. **Given** a run that fails, **When** an operator inspects the operational signals and the
   run's task record, **Then** the failure, its stage, and its correlation reference are
   discoverable there.
2. **Given** a run that completes without changing wiki content, **When** an operator
   inspects the operational signals and the run's task record, **Then** the run's completion
   and the fact that it produced no wiki changes are discoverable there.

---

### Edge Cases

- **Missing or empty activity log**: the first agent write creates it. The prepend
  validation MUST treat "no current content" as a valid base case rather than a violation.
- **Concurrent prepends**: two agents adding entries at the same time. The existing
  stale-read conflict detection MUST continue to apply; the "existing content is preserved
  as an unchanged suffix" rule replaces the current "unchanged prefix" rule.
- **Partial change**: a run that created a page but failed before updating the index. It
  logs, because wiki content changed; the entry describes what actually landed.
- **Whitespace-only or heading-only additions**: a proposed write that preserves existing
  content as a suffix but adds no conforming entry above it MUST still be rejected by the
  existing entry-shape validation.
- **Lint runs**: the lint agent writes proposals, never the activity log, so it produces no
  entries; only its read-side understanding of the log's ordering may need to match the new
  convention.
- **Pre-existing logs written oldest-first**: files produced under the previous rules are
  not rewritten or re-sorted. Newest-first applies from this change onward, so an existing
  file will have a newest-first section above an older oldest-first section.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wiki activity log MUST be created and maintained exclusively by agents
  through the guarded write path; no harness component MAY author, append, or otherwise
  write content into it.
- **FR-002**: The harness fallback that writes an activity-log entry when no agent entry is
  found, and unconditionally on run failure, MUST be removed from the activity-log write
  path for every agent type.
- **FR-003**: A new activity-log entry MUST be added at the top of the file; all existing
  content MUST be preserved unmodified below it.
- **FR-004**: The guarded-write structural check for the activity log MUST enforce
  prepend-only — the current content MUST be an unchanged suffix of the proposed content —
  replacing the current append-only (unchanged-prefix) check, with the recorded denial
  reason updated to name the new rule.
- **FR-005**: A guarded write to the activity log that modifies, reorders, or removes any
  existing content MUST be denied and MUST record the denial with a reason; the run
  continues with its remaining allowed actions.
- **FR-006**: Each logged action MUST produce one complete entry including its own
  `## [YYYY-MM-DD] TYPE | SUMMARY` heading, regardless of whether entries with the same date
  already exist. Merging a new action into an existing date section MUST NOT be required or
  expected of any agent.
- **FR-007**: An agent MUST write an activity-log entry only for a run that changed wiki
  content (a page created, updated, or superseded, or the index updated). A run that only
  communicated with the user, produced no writes, or failed before changing anything MUST
  NOT produce an entry.
- **FR-008**: The shape of an individual entry — heading pattern, the required prose
  paragraph, and the wikilink convention — MUST remain unchanged. Only ordering, authorship,
  and the criterion for when an entry is written change.
- **FR-009**: Every entry MUST remain independently locatable by the existing heading
  pattern `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`, including when two entries share an
  identical heading.
- **FR-010**: The prepend validation MUST accept a missing or empty activity log as a valid
  starting state, so the first agent write creates the file rather than being denied.
- **FR-011**: Concurrent activity-log writes MUST continue to be detected and denied when
  the file changed after the writing agent read it, under the new prepend rule.
- **FR-012**: Operational visibility for runs that changed nothing and for failed runs MUST
  remain available through the system's own operational signals and task records, so that
  removing the harness fallback entries costs no diagnostic capability.
  [NEEDS CLARIFICATION: is the existing signal and task-record coverage already sufficient,
  or does removing the fallback leave a gap that warrants a new operational event?]
- **FR-013**: The versioned instruction files that govern the agents which write to the
  activity log MUST state newest-first placement, one complete entry per action, and the
  changes-only criterion.
  [NEEDS CLARIFICATION: does the lint agent's read-side description of the activity log
  also need updating, given it never writes the file?]
- **FR-014**: Activity-log files produced under the previous oldest-first rules MUST NOT be
  rewritten, re-sorted, or migrated; the new ordering applies to entries written from this
  change onward.

### Key Entities

- **Wiki Activity Log**: The wiki's own, agent-maintained record of changes made to it —
  the counterpart to the wiki index. A single markdown file at the content root, ordered
  newest-first, containing only agent-authored entries.
- **Log Entry**: One complete record of one logged action: a date heading carrying a type
  and a short summary, followed by a prose paragraph describing what was actually done.
  Entries are never merged; two entries may share an identical heading.
- **Wiki Change**: The condition that makes a run worth logging — a page created, updated,
  or superseded, or the index updated. Runs producing no wiki change produce no entry.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of activity-log writes that would modify, reorder, or remove existing
  content are denied by the guarded write path, and each denial is recorded with a reason
  (deterministic harness guarantee).
- **SC-002**: 100% of runs — successful, failed, and no-write — produce zero
  harness-authored activity-log content (deterministic harness guarantee).
- **SC-003**: 100% of activity-log entries remain independently locatable by the heading
  pattern `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` (deterministic harness guarantee, carried
  over from feature 014).
- **SC-004**: 100% of activity-log writes that preserve existing content as an unchanged
  suffix and carry a conforming new entry are allowed, including the first write into a
  missing or empty file (deterministic harness guarantee).
- **SC-005**: ≥ 90% of sampled agent runs that changed wiki content write exactly one entry,
  at the top of the file, accurately describing what actually changed (agent-judgment
  threshold).
- **SC-006**: ≥ 90% of sampled agent runs that changed nothing — question-answering turns
  and failed runs alike — write no activity-log entry (agent-judgment threshold).
- **SC-007**: ≥ 90% of sampled logged actions occurring on a date that already has an entry
  produce a separate complete entry with its own date heading rather than being merged into
  the existing day's section (agent-judgment threshold).
- **SC-008**: 100% of runs that changed nothing or failed remain fully accounted for in the
  system's operational signals and task records — run outcome, stage, and correlation
  reference are all discoverable without consulting the wiki's activity log (deterministic
  harness guarantee).

## Assumptions

- "Changed wiki content" means a page was created, updated, or superseded, or the wiki index
  was updated. Proposals produced by the lint agent are not wiki-content changes for the
  purposes of this criterion and produce no activity-log entry.
- Judging whether a run changed wiki content, and describing that change, stays with the
  agent under its instruction files; it is not reimplemented as deterministic backend logic.
  Accordingly the "one entry per change" and "no entry when nothing changed" outcomes are
  expressed as agent-judgment thresholds (SC-005 through SC-007), while the structural
  prepend rule and the absence of harness authorship are deterministic guarantees.
- Entry ordering is by write order, not by parsing or sorting dates. The file is never
  re-read and re-sorted; "newest-first" is a consequence of every write being a prepend.
- No structured or parsed representation of the log is introduced. It remains
  human-readable markdown, as today.
- The existing guarded-tool denial and conflict machinery (stale-read detection, denial
  recording, entry-shape validation) is reused; only the ordering rule it enforces for the
  activity log is inverted, and its denial reason renamed accordingly.
- The current append-only decision is recorded in an accepted architecture decision record
  (ADR-017, log and catalog entry format enforcement). This feature inverts that decision,
  so an amending or superseding ADR with bidirectional status links and an index update is
  expected as part of planning — not as part of this specification.
- A related open item (issue #38, the open knowledge format v0.2) specifies the same file as
  newest-first, which corroborates this feature's ordering requirement, but describes a
  different entry shape (bullet items grouped under a bare date heading) than the per-action
  heading-plus-paragraph shape required here. This specification keeps the existing entry
  shape (FR-008); reconciling the two shapes is left to whichever feature addresses the
  format itself. [NEEDS CLARIFICATION: should this feature make that reconciliation decision
  now, or explicitly defer it to the format feature?]
- Existing operational signals and task records are assumed to be the correct home for run
  bookkeeping. FR-012 requires confirming that coverage before the harness fallback is
  removed, not designing a replacement for it.

## Out of Scope

- Changing the entry format itself — heading shape, the required paragraph, or the wikilink
  convention.
- The wiki index's catalog entry format and ordering.
- Rewriting, re-sorting, or migrating activity-log files produced under the previous rules.
- Introducing any structured or parsed representation of the activity log.
- Extending the changes-only criterion to any file other than the wiki activity log.

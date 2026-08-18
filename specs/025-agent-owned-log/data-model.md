# Phase 1 Data Model: Agent-Owned, Newest-First Wiki Activity Log

**Feature**: 025-agent-owned-log | **Date**: 2026-08-17

This feature introduces **no persisted schema, no database change, and no parsed
representation**. The activity log stays human-readable markdown, as it is today (spec,
Assumptions). What follows models the entities the spec names, the in-memory harness facts
the new operational signal is derived from, and the state transitions the guarded write path
evaluates — so `tasks.md` has an unambiguous target for each.

---

## 1. Wiki Activity Log

The wiki's own, agent-maintained record of changes made to it — the counterpart to the wiki
index.

| Aspect | Value |
| --- | --- |
| Representation | A single UTF-8 markdown file at the resolved content root (`log.md`) |
| Ownership | Agents, exclusively. No harness component authors, appends, or otherwise writes content into it (FR-001). |
| Write path | The guarded `write_file` tool only (ADR-006) — never direct filesystem I/O |
| Ordering | Newest-first. Not a sorted property: it is a consequence of every write being a prepend (Assumptions). The file is never re-read and re-sorted. |
| Existence | May be absent or empty. That is a valid starting state, not an error (FR-010). |
| Migration | None. Files written under the previous oldest-first rules are never rewritten, re-sorted, or migrated (FR-014). An existing file legitimately holds a newest-first section above an older oldest-first section. |

**Validation rules** (all enforced mechanically at the guarded write boundary; see
[contracts/activity-log-write-contract.md](contracts/activity-log-write-contract.md)):

- **V1 — Prepend-only**: the current on-disk content MUST be an unchanged suffix of the
  proposed content, byte-for-byte (FR-003, FR-004). Empty current content satisfies this
  trivially.
- **V2 — Conforming new entry**: the prepended head MUST begin (after any leading blank
  lines) with a line matching `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` (FR-008, FR-009).
- **V3 — Non-empty paragraph**: at least one further non-blank line MUST follow that heading
  **within the head**, so a whitespace- or heading-only prepend is rejected (Edge Cases).
- **V4 — Fresh read**: the file MUST NOT have changed since the writing run read it
  (FR-011). Unchanged from today; evaluated before V1–V3.

---

## 2. Log Entry

One complete record of one logged action.

| Field | Shape | Owner |
| --- | --- | --- |
| Date heading | `## [YYYY-MM-DD] TYPE \| SUMMARY` — one line | Agent composes; harness validates the pattern only |
| `TYPE` | `ingest`, `supersession`, or `query` — the existing vocabulary, unchanged | Agent |
| `SUMMARY` | A short phrase | Agent (judgment) |
| Paragraph | One prose paragraph naming what actually changed, with the run's correlation reference | Agent (judgment) |

**Rules**:

- **E1 — One entry per action**: each logged action produces one complete entry *including
  its own date heading*, regardless of whether entries with the same date already exist
  (FR-006). Merging into an existing date section is neither required nor expected of any
  agent.
- **E2 — Entries are never merged or edited**: an entry, once written, is immutable content
  below every later entry. V1 makes this structural.
- **E3 — Duplicate headings are legal**: two entries MAY share an identical heading (same
  date, type, and summary) and both remain independently locatable by the heading pattern
  (FR-009). Nothing in the system treats the heading as a key.
- **E4 — Shape is unchanged**: heading pattern, required paragraph, and wikilink convention
  are exactly as feature 014 defined them (FR-008). Only ordering, authorship, and the
  criterion for *when* an entry is written change.

---

## 3. Wiki Change (the logging criterion)

The condition that makes a run worth logging.

| Aspect | Value |
| --- | --- |
| Definition | A page was created, updated, or superseded, or the wiki index was updated (Assumptions) |
| Not a wiki change | Lint proposals; question-answering turns that write nothing; a run that failed before changing anything |
| Who decides | **The agent**, under its instruction files. This is wiki-content judgment and is verified at evaluation thresholds (SC-005/006/007), never reimplemented as deterministic backend code. |

The harness has a *separate, mechanical* notion used only for observability — see §4. The
two must not be conflated: the harness's notion answers "did I allow any wiki-content write
this run?", which is a fact about the harness's own bookkeeping. It never decides whether an
entry *should* have been written, only reports the combination the operator wants to know
about.

---

## 4. Run Write Coverage (in-memory, harness-owned)

A per-run, in-memory projection over the guarded executor's own record of allowed writes.
Not persisted, not exposed to the agent, never derived from file content.

| Field | Type | Derivation |
| --- | --- | --- |
| `WikiContentWrites` | ordered set of canonical paths | The run's successfully written paths, minus the canonical activity-log path |
| `ActivityLogWritten` | boolean | The canonical activity-log path is among the run's successfully written paths |
| `Type` | `ingest` \| `query` | The agent process emitting the signal |
| `CorrelationId` | string | Task id (Ingest) or turn id (Query) |

**Derivation constraints** (FR-012a):

- MUST come from the harness's record of writes it *allowed*. Denied writes never count.
- MUST NOT be derived by reading, parsing, or searching the activity log's content. (The
  deleted backstop did exactly that — it searched the file for the correlation id — and was
  wrong for this purpose besides: an entry can mention an id the run did not write.)
- MUST NOT use "created paths" as the source: that set is create-only writes, which would
  miss an index-only or page-update run, both of which are wiki changes.

**Derived outcome**, evaluated once at run end:

| `WikiContentWrites` | `ActivityLogWritten` | `outcome` | Signal |
| --- | --- | --- | --- |
| empty | false | `no_change` | none |
| empty | true | `logged` | none |
| non-empty | true | `logged` | none |
| non-empty | false | `not_logged` | `wiki.log.change_not_logged` (WARN) + `wiki.log.unlogged_change_total` |

In every row the harness writes nothing to the wiki (SC-009).

---

## 5. Guarded write state transitions (activity log target)

Evaluated in order; the first failure short-circuits, records a `DeniedActionRecord` with
that reason, returns an `is_error` tool result, and the run continues with its remaining
allowed actions (ADR-006, FR-005). Only the **bolded** step changes in this feature.

```text
write_file(target = <content root>/log.md, proposed)
  │
  ├─ acquire per-target cross-process lock ──── fail ─→ write_coordination_timeout
  │
  ├─ policy / WriteMode check (read-write) ──── fail ─→ create_only_target_exists | …
  │
  ├─ compare-and-swap vs. the run's read hash ─ fail ─→ write_conflict_stale_read
  │
  ├─ V1 **current content is an unchanged SUFFIX of proposed**
  │                                        ──── fail ─→ log_entry_not_prepended   ← renamed
  │
  ├─ V2 head's first non-blank line matches the heading pattern
  │                                        ──── fail ─→ log_entry_malformed_heading
  │
  ├─ V3 at least one non-blank line follows, within the head
  │                                        ──── fail ─→ log_entry_missing_paragraph
  │
  └─ ALLOW → atomic write → update read-hash baseline → release lock
```

**Denial reason inventory for this target** (the only change is the rename; the recoverable
set in `GuardedToolExecutor` and both agents' error-recovery instructions must follow it):

| Reason | Status |
| --- | --- |
| `log_entry_not_prepended` | **New name** for `log_entry_not_appended`; new meaning (suffix, not prefix) |
| `log_entry_malformed_heading` | Unchanged |
| `log_entry_missing_paragraph` | Unchanged |
| `write_conflict_stale_read` | Unchanged |
| `write_coordination_timeout` | Unchanged |
| `catalog_entry_malformed` | Unchanged; `index.md` only, not this target |

---

## 6. What this feature deletes

Recorded here so `tasks.md` has an explicit removal list rather than an implied one.

| Element | Kind | Reason |
| --- | --- | --- |
| `WikiLogAppender` | Component | FR-001/FR-002 — harness authorship of wiki content |
| `EnsureLogEntryAsync` call sites in Ingest (success + failure) and Query (success + failure) | Call sites | Same |
| `RestartReconciler.AppendReconciliationLogAsync` and its call | Method + call | FR-001 — a Hub-side harness writer the spec's Problem Statement does not name but FR-001 covers; leaving it would falsify SC-002 for every crash-reconciled task |
| `Grimoire.AgentRuntime.WikiLog` allow-list entry in the three `*GuardedWriteBoundaryRuleTests` | Arch-test exemption | BR-1 — removing the exemption is the enforcement |
| `wiki.log.backstop_appended` event, `wiki.log.backstop_appended_total` metric, `wiki_log.backstop_append` span | Signals | Retired with the component |
| `WikiLogAppenderTests`, `WikiLogAppenderMetricsTests`, the backstop cases in `IngestObservabilityTraceTests` | Tests | Assert a deleted contract; removed with it, not left red |

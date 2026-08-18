# Contract: Activity-Log Guarded Write (prepend-only)

**Feature**: 025-agent-owned-log | **Amends**: `specs/014-wiki-storage-restructure/contracts/log-and-catalog-entry-format.md`
(the `log.md` half only) | **ADR**: ADR-028 (amends ADR-017)

This is the normative contract for a guarded `write_file` whose canonical target is the
resolved activity-log path. The `index.md` catalog-entry contract from feature 014 is
untouched and still governs that target.

---

## §1 Scope and position

The check applies **only** when the canonical write target equals the resolved activity-log
path. It runs **after** the cross-process lock, the `WriteMode` check, and the read-then-write
compare-and-swap, and **before** the write is committed. It composes with those checks; it
replaces none of them. Every other wiki write is entirely unaffected.

The check is a pure string operation over content already resident in memory — the same
current-content read the compare-and-swap already performed, and the proposed content the
call already carries. No additional I/O, no model call, no markdown parse.

The check is mechanical only. It never judges whether a given `SUMMARY`, paragraph, or
described change is good, appropriate, or worth logging — that is agent judgment
(Constitution Principle V), measured by the evaluation thresholds SC-005–SC-007.

---

## §2 Definitions

| Term | Meaning |
| --- | --- |
| `current` | The target's on-disk content at check time, as a UTF-8 string. If the file does not exist, `current` is the empty string. |
| `proposed` | The write call's proposed new content. |
| `head` | `proposed` with its trailing `current` removed — i.e. the bytes being prepended. Defined only when §3 R1 holds. |

Line splitting is on `\n`, with a trailing `\r` trimmed from each line, matching the existing
implementation. All string comparisons are ordinal.

---

## §3 Rules

**R1 — Prepend-only.** DENY `log_entry_not_prepended` unless `proposed` **ends with**
`current`, byte-for-byte.

- When `current` is empty (file missing or zero-length), R1 is satisfied and `head` is all of
  `proposed`. The first agent write creates the file. *(FR-010, SC-004)*
- Any write that modifies, reorders, or removes existing content fails R1. *(FR-005, SC-001)*
- `proposed == current` (a no-op rewrite) satisfies R1 with an empty `head`, and is then
  denied by R2 for lacking a heading.

**R2 — Heading shape.** DENY `log_entry_malformed_heading` unless the first non-blank line of
`head` matches:

```regex
^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$
```

Leading blank lines in `head` are skipped. The pattern is unchanged from feature 014.
*(FR-008, FR-009, SC-003)*

**R3 — Following paragraph.** DENY `log_entry_missing_paragraph` unless at least one further
non-blank line follows that heading **within `head`**. A `head` consisting only of whitespace,
or only of a conforming heading, is rejected. *(Edge Cases)*

**R4 — Allow.** If R1, R2, and R3 all hold, the write is allowed and committed atomically
under the still-held lock. *(SC-004)*

**R5 — Denial handling.** Every denial is recorded as a `DeniedActionRecord` carrying its
reason, returned to the agent as an `is_error` tool result, and the run continues with its
remaining allowed actions. Unchanged from ADR-006/ADR-017. *(FR-005)*

**R6 — Concurrency.** R1–R3 make no concurrency guarantee of their own. Two runs prepending
concurrently are separated by the pre-existing compare-and-swap: the second write is denied
`write_conflict_stale_read` if the file changed after that run read it, and the agent's
documented recovery is to re-read and re-compose its prepend over the current content. No
entry is lost or corrupted. *(FR-011)*

---

## §4 Worked examples

Given `current`:

```markdown
## [2026-08-10] ingest | created retrieval-patterns

Created [[concepts/retrieval-patterns]] from source "notes.md". Task: task-001.
```

### Allowed

```markdown
## [2026-08-16] ingest | updated retrieval-patterns

Updated [[concepts/retrieval-patterns]] with the hybrid-search section from source
"search.md". Task: task-002.

## [2026-08-10] ingest | created retrieval-patterns

Created [[concepts/retrieval-patterns]] from source "notes.md". Task: task-001.
```

`current` is an exact suffix; `head` opens with a conforming heading and carries a paragraph.

### Allowed — first write into a missing file

`current` is empty; `head` is the whole proposed content, which opens with a conforming
heading followed by a paragraph.

### Allowed — two entries with an identical heading

A second write whose `head` heading is byte-identical to an existing entry's heading is
allowed. Both entries are present and both remain locatable by the R2 pattern; nothing
treats the heading as a key. *(FR-009)*

### Allowed — same date, separate entry

A second entry dated the same day is a normal prepend. No rule requires or rewards merging it
into the earlier day's section, and the harness has no concept of a "day section". *(FR-006)*

### Denied — `log_entry_not_prepended`

- The old append shape: `current` + new entry at the end.
- Any rewrite, re-sort, or removal of existing entries.
- A "prepend" that also fixes a typo in an existing entry — `current` is no longer an exact
  suffix.

### Denied — `log_entry_malformed_heading`

- `head` is `"\n\n"` (whitespace-only prepend).
- `head` opens with `# [2026-08-16] ingest | x` (single `#`), or with a missing `|`
  separator, or with a non-`YYYY-MM-DD` date.

### Denied — `log_entry_missing_paragraph`

- `head` is a conforming heading with nothing but blank lines after it, and the existing
  content resumes immediately below.

### Denied — `write_conflict_stale_read`

- The run read the file, another writer prepended, and this run's write is evaluated against
  the changed file. Denied before R1 is ever reached.

---

## §5 Authorship contract

**A1.** The activity log is written **only** through the guarded `write_file` tool by an
agent. No harness component may author, append, or otherwise write content into it — not on
success, not on failure, not on crash reconciliation, not to create the file. *(FR-001,
FR-002, SC-002)*

**A2.** For the agent assemblies, A1 is enforced structurally: filesystem-write APIs reachable
from `Grimoire.IngestAgent`, `Grimoire.QueryAgent`, `Grimoire.LintAgent`, and the shared
`Grimoire.AgentRuntime` may be called only from `Grimoire.AgentRuntime.Guardrails*` and
`Grimoire.AgentRuntime.Core.Adapters.Replay`. `Grimoire.AgentRuntime.WikiLog` is **not**
exempt. *(ADR-028 BR-1)*

**A3.** For the Hub, A1 is verified behaviourally: restart reconciliation records a task's
failure in the task artifact and the operational status history, and leaves the activity log
byte-for-byte unchanged. *(ADR-028 FSI-2)*

---

## §6 Coverage signal contract

**S1.** At run end, each writing agent process evaluates, once, from its own record of
allowed writes: the set of wiki-content writes it was allowed (its successfully written
paths minus the canonical activity-log path) and whether the activity log is among them.

**S2.** The evaluation is wrapped in a `wiki_log.coverage_check` span carrying `type`,
`task_id_or_run_id`, `wiki_content_writes`, and `outcome` ∈ {`logged`, `not_logged`,
`no_change`}.

**S3.** When the wiki-content write set is non-empty and the activity log was not written,
the process emits `wiki.log.change_not_logged` at WARN with mandatory fields `type`,
`task_id_or_run_id`, `wiki_content_writes`, and increments
`wiki.log.unlogged_change_total` labelled `type`. The log-event span is a child of the
`wiki_log.coverage_check` span. *(FR-012a)*

**S4.** In no case does S1–S3 read, write, or create any file. The determination is set
arithmetic over harness bookkeeping; it never inspects wiki content and never judges whether
an entry *ought* to have been written. *(FR-012a, SC-009)*

**S5.** No new signal is introduced for failed runs or for runs that changed nothing. Their
coverage by the task artifact, the operational status history, and the conversation record is
confirmed by test, not replaced. *(FR-012, SC-008)*

---

## §7 Non-goals

- The entry format itself — heading pattern, required paragraph, wikilink convention — does
  not change. *(FR-008)*
- No migration, re-sort, or rewrite of logs written under the previous rules. *(FR-014)*
- No structured or parsed representation of the log. It stays human-readable markdown.
- The `index.md` catalog-entry contract and its ordering are untouched.
- The changes-only criterion applies to the activity log and to nothing else.

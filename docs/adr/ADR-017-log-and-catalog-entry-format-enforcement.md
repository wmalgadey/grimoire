---
status: accepted
---

# ADR-017: Structural Format Enforcement for `log.md` and `index.md` Entries

> **Amended by [ADR-027](ADR-027-agent-owned-activity-log-prepend-ordering.md)** (proposed):
> the `log.md` half of the mechanism below is inverted from *append-only* to
> *prepend-only* — the current content must be an unchanged **suffix** of the proposed
> content, not an unchanged prefix — and the denial reason `log_entry_not_appended` is
> renamed `log_entry_not_prepended`. The heading-pattern check, the following-paragraph
> check, the check's position in the evaluation order, the `guardrails.format_validate`
> span, and the entire `index.md` catalog-entry half are unchanged. The final mechanism
> bullet below — the `WikiLogAppender` harness backstop that "generates content that always
> satisfies this check by construction" — is retired with the backstop itself; the check now
> exists exclusively for agent-authored writes. Read the `log.md` bullets below with that
> inversion applied; everything else this ADR decided stands.

## Context and Problem Statement

Feature 014 (`specs/014-wiki-storage-restructure/spec.md`) requires every `log.md`
entry to start with a `[DATE] TYPE | SUMMARY` heading followed by a paragraph
(FR-007–FR-009, FR-011) and every newly added `index.md` catalog entry to follow a
link-description-source-status shape (FR-012–FR-013). The spec places the *structural*
half of both claims in the deterministic tier — SC-003 ("100% of `log.md` entries...
start with a correctly formatted heading"), SC-004 ("100% of `log.md` entries remain
locatable by searching for the heading pattern"), SC-006 ("100% of `index.md` catalog
entries added after this change follow the... format") — and keeps only the narrative
*quality* half (does the paragraph/description actually describe what happened) in the
agent-judgment tier (SC-005, SC-007, both ≥90% evaluation thresholds).

Both files are written through the same generic `write_file` guarded tool every other
wiki write uses (`GuardedToolExecutor.ExecuteWriteFileAsync`), gated today only by
path-prefix policy and `WriteMode` (`read-write`/`create-only`/`frontmatter-only` —
ADR-015, ADR-016). None of those checks look at *shape* — an agent (or a bug in the
harness backstop) could write anything to `log.md`/`index.md` and every existing check
would still allow it, as long as the path and mode match. Per Constitution Principle
II, a success criterion the spec states as a 100% deterministic guarantee must be
backed by an enforceable mechanism — instruction-file convention alone (Constitution
Principle V's normal home for wiki-content judgment) cannot make that claim true,
because agent output is not guaranteed-conforming by construction. This is the same
reasoning ADR-016 applied to Lint's frontmatter-only guarantee: the *shape* of a write
is a harness-checkable fact, distinct from the *content* judgment inside that shape.

## Decision Drivers

- FR-007/FR-009/FR-011, SC-003/SC-004 (log heading shape, append-only, locatability)
  and FR-012/FR-013, SC-006 (catalog entry shape) are stated as 100% guarantees —
  Constitution Principle II forbids treating them as evaluation-sampled instead.
- Constitution Principle V: the check must stay purely mechanical — never a judgment
  about whether a *specific* `SUMMARY`, paragraph, or description is good (that stays
  in `data/agents/*/system-prompt.md` and is scored by the separate ≥90% evaluation
  thresholds, SC-005/SC-007).
- ADR-006: the guarded tool boundary is the single chokepoint every write passes
  through; the new check belongs there, composed with existing `WriteMode` checks, not
  a second boundary.
- ADR-016 precedent: a lexical/regex check over content already in memory, no parsing
  library, no additional I/O — the same cost/complexity class as the frontmatter/body
  split.
- Minimal surface: apply only to the two exact-match targets this spec actually
  constrains (`log.md`, `index.md`); no other wiki write gains a shape check.

## Considered Options

1. **A `FormatValidator` hook on `GuardedToolExecutor`'s write path, applied only to
   the exact-match `log.md`/`index.md` targets**: for `log.md`, deny unless the
   proposed content is the current content plus an appended tail whose first line
   matches `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` and which contains at least one
   non-blank paragraph line before end-of-file; for `index.md`, deny if any `- [`-led
   line present in the proposed content but absent (byte-for-byte) from the current
   content fails to match `^- \[.+\]\(.+\) — .+ — .+$`.
2. Leave both formats as instruction-file-only convention, relying on the evaluation
   thresholds (SC-005/SC-007) as the only check (rejected — silently downgrades two
   success criteria the spec explicitly states as 100% guarantees, the exact erosion
   Constitution Principle II names).
3. A dedicated `append_log_entry`/`add_catalog_entry` structured tool (taking
   `date`/`type`/`summary`/`paragraph` fields, or `link`/`description`/`status`) with
   the harness composing the markdown line itself (rejected — the harness would be
   authoring wiki content, not validating its shape; a bigger Principle V risk than a
   post-hoc regex check on agent-authored text).
4. Full Markdown/YAML AST parsing of both files (rejected — disproportionate to what a
   regex on a single new line/tail needs; no other part of this codebase parses
   Markdown structurally).

## Decision Outcome

Chosen option: **Option 1.**

### Mechanism

- `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard.EvaluateWriteAsync`
  gains a format-validation step, applied only when the canonical target equals the
  resolved `IndexPath` or `LogPath` (the same two locations already singled out by
  policy.json's exact-match prefixes — R3/R4, `specs/014-wiki-storage-restructure/research.md`).
  It runs *after* the existing existence/CAS/`WriteMode` checks (a write that already
  fails those is denied for that reason first) and *before* the write is committed.
- **`log.md`** (append-only + heading shape):
  1. Deny `log_entry_not_appended` unless the proposed content starts with the current
     on-disk content byte-for-byte (or the file does not yet exist and this is the
     first write) — `log.md` is append-only (FR-011); this check is the mechanical
     enforcement of that requirement, not just a convention.
  2. Take the appended tail (proposed content minus the unchanged prefix). Deny
     `log_entry_malformed_heading` unless its first non-blank line matches
     `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`.
  3. Deny `log_entry_missing_paragraph` unless at least one further non-blank line
     follows the heading before the tail ends.
- **`index.md`** (new-entry shape only, per FR-012's "newly added" scope):
  1. Compute the set of lines starting with `- [` present in the proposed content but
     absent, byte-for-byte, from the current content.
  2. Deny `catalog_entry_malformed` if any such line does not match
     `^- \[.+\]\(.+\) — .+ — .+$`.
  3. Lines that already existed verbatim, and any line not starting with `- [`
     (section headings, blank lines), are never checked — this rule only constrains
     genuinely new or edited catalog-bullet lines, not the file's surrounding
     structure.
- Both checks are pure string/regex operations over content already resident in
  memory (the same current-content read the CAS check already performs, and the
  call's proposed `content` already held by `GuardedToolExecutor`) — no additional
  I/O, no model call, no judgment about whether a given `SUMMARY`/`TYPE`/description
  is good, only whether the envelope shape is present.
- New `DeniedActionRecord` reasons: `log_entry_not_appended`,
  `log_entry_malformed_heading`, `log_entry_missing_paragraph`,
  `catalog_entry_malformed` — surfaced identically to ADR-015/ADR-016's existing
  denial reasons (recorded, `is_error` tool result, run continues; the agent sees the
  denial reason and may retry with a corrected write in the same turn budget).
- The harness backstop (`WikiLogAppender`, replacing `IngestLogAppender` —
  `specs/014-wiki-storage-restructure/research.md` R5) generates content that always
  satisfies this check by construction; the check exists for agent-authored writes
  (and as a regression guard on the backstop itself).

### Relationship to ADR-015 / ADR-016

This ADR **extends** ADR-006/ADR-015/ADR-016; it supersedes no part of them. The
cross-process lock, compare-and-swap check, and `WriteMode` (`read-write`/
`create-only`/`frontmatter-only`) are unchanged and still evaluated first — format
validation is a fourth, independent check layered on top, exercised only for the two
exact-match targets this feature names. Every other wiki write (articles under a
topical subfolder) is entirely unaffected.

### Structural enforcement (Constitution III)

No new namespace: the check lives inside `SharedFileWriteGuard`, already confined to
`Grimoire.AgentRuntime.Guardrails.Coordination` and containment-tested. New
deterministic tests (Red/Green probed, per Constitution Phase 0 discipline) prove: a
correctly formatted append/entry is allowed; a non-append `log.md` write is denied; a
malformed heading is denied; a heading with no following paragraph is denied; a
malformed new catalog line is denied; an edit to an existing, already-conforming
catalog line's surrounding heading text is *not* denied.

### Consequences

- Good, because SC-003/SC-004/SC-006 become genuinely enforceable 100% guarantees
  instead of aspirational phrasing resting on agent compliance.
- Good, because the check is purely mechanical (shape, not content) — no risk of
  reimplementing the agent's summarization/description judgment in backend code
  (Constitution Principle V); SC-005/SC-007's quality thresholds remain the only
  judge of *content*.
- Good, because it composes with, rather than duplicates, the existing
  `WriteMode`/CAS checks — no new coordination logic, no new lock.
- Bad, because the append-only check assumes `log.md` is only ever grown at the end;
  accepted because FR-011 already requires this invariant — the check makes it real
  instead of assumed.
- Bad, because the catalog check is line-oriented, not truly presentation-aware (e.g.
  a `- [` -led line inside a code fence would be checked); accepted because no wiki
  convention today or in the reference layout uses code fences inside `index.md`, and
  the cost of a false-positive denial is a retry, not data loss.

## More Information

Detailed rationale: `specs/014-wiki-storage-restructure/research.md`,
`specs/014-wiki-storage-restructure/contracts/log-and-catalog-entry-format.md`. Per
Constitution Principle III this ADR MUST reach **Accepted** before `/speckit-tasks`
runs for feature 014 — drafted `proposed` by the planning run and moved to
**Accepted** by the mandatory ADR Review step within the same session (no unresolved
open questions carried over from `research.md`; the mechanism composes with, and does
not conflict with, any existing accepted ADR).

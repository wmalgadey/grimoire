---
status: superseded
supersedes: null
superseded_by: [ADR-035]
reason: Authorship boundary re-decided as single-aspect ADR-035; the prepend-only ordering half is feature-scoped format content owned by specs/025-agent-owned-log/contracts/activity-log-write-contract.md, not an architectural decision.
---

# ADR-028: Agent-Owned Activity Log — Prepend-Only Ordering and Removal of Harness Authorship

> **Amends [ADR-017](ADR-017-log-and-catalog-entry-format-enforcement.md)**: the `log.md`
> half of ADR-017's format-validation mechanism is inverted — the structural check becomes
> *prepend-only* (the current content must be an unchanged **suffix** of the proposed
> content) instead of *append-only* (unchanged prefix), and its denial reason is renamed
> `log_entry_not_appended` → `log_entry_not_prepended`. The heading-pattern and
> following-paragraph checks, the `index.md` catalog-entry half, the check's position in
> the evaluation order, and its `guardrails.format_validate` span are unchanged. ADR-017's
> final mechanism bullet — "the harness backstop (`WikiLogAppender`) generates content that
> always satisfies this check by construction" — is retired along with the backstop itself;
> the check now exists exclusively for agent-authored writes. Everything else ADR-017
> decided stands.

## Context and Problem Statement

Feature 025 (`specs/025-agent-owned-log/spec.md`) is driven by an operator review of a
produced wiki's `log.md` (GitHub issue
[#89](https://github.com/wmalgadey/grimoire/issues/89)). The wiki's activity log is meant
to be the wiki's own, agent-maintained record — the counterpart to `index.md`, and
therefore wiki content under Constitution Principle V. Today it is not:

1. **The harness co-authors it.** `Grimoire.AgentRuntime.WikiLog.WikiLogAppender` writes a
   harness-generated entry whenever it cannot find the run's correlation id in the file,
   and unconditionally on Ingest failure. `Grimoire.Hub.OperationalState.RestartReconciler`
   independently appends its own entry when a task is reconciled as failed on startup.
   Both write with direct `File.AppendAllTextAsync` calls, deliberately outside the guarded
   tool boundary — an explicitly allow-listed exception in all three
   `*AgentGuardedWriteBoundaryRuleTests`. The result is a file whose contents are a mixture
   of agent-authored wiki content and harness run bookkeeping, which is precisely the
   boundary Principle V draws.
2. **Ordering is structurally inverted from what the operator wants.** ADR-017 made
   append-only a *mechanically enforced* rule, not a convention: a `log.md` write whose
   proposed content does not begin with the current content byte-for-byte is denied
   `log_entry_not_appended`. Newest-first therefore cannot be achieved by changing the
   instruction files alone — the guardrail actively denies it.
3. **Removing the backstop removes a diagnostic.** The backstop's one genuinely useful
   signal was "an agent changed the wiki and did not log it". Failure and no-write runs are
   already fully covered by the task artifact, the status history, the conversation record,
   and each agent's existing completion/failure events; the "changed but unlogged" case is
   not.

The question this ADR answers is where the ordering rule and the authorship rule live, and
what replaces the backstop's diagnostic value without putting harness text back into wiki
content.

## Decision Drivers

- **Constitution Principle V** — `log.md` content is wiki content. Which actions are worth
  logging, and what the entry says, is agent judgment under versioned instruction files.
  The harness may enforce the *shape* and *placement* of a write (ADR-017's standing
  reasoning) but may not author the text.
- **FR-003/FR-004, SC-001/SC-004** — newest-first is stated as a 100% deterministic
  guarantee, so it must be a mechanical check at the guarded boundary, not a convention.
  This is the same argument ADR-017 used for append-only; only the direction changes.
- **FR-001/FR-002, SC-002** — "zero harness-authored activity-log content" is also a 100%
  guarantee, and the existing `*GuardedWriteBoundaryRuleTests` already provide the exact
  enforcement shape: an IL-level allow-list of namespaces permitted to call filesystem-write
  APIs. Removing an entry from that allow-list *is* the enforcement.
- **FR-012a, SC-009** — the replacement diagnostic must be derived from the harness's own
  record of which guarded writes it allowed (mechanical), and must never write to the wiki.
- **Minimal surface** — no new file, no new coordination layer, no new tool. The change is
  an inversion inside one existing method, a deletion, and one new observability signal.

## Considered Options

### Ordering

1. **Invert ADR-017's `log.md` check in place: unchanged prefix → unchanged suffix.**
   `SharedFileWriteGuard.ValidateLogEntryFormat` swaps `StartsWith` for `EndsWith`, takes
   the *head* (proposed content minus the unchanged suffix) instead of the tail, and applies
   the unchanged heading/paragraph checks to it. Denial reason renamed
   `log_entry_not_prepended`.
2. Drop the structural ordering check entirely and let the instruction files ask for
   newest-first (rejected — silently downgrades SC-001/SC-004 from 100% guarantees to agent
   compliance, the exact erosion Constitution Principle II names and ADR-017 already
   rejected once for this file).
3. A dedicated `prepend_log_entry` structured tool that inserts agent-supplied text at the
   top (rejected — the harness would own the file's assembly, and it buys nothing over a
   suffix check on a `write_file` the agent already composes; ADR-017 rejected the
   structured-tool option for the same reason).
4. Keep the file append-only on disk and reverse it on read (rejected — nothing in this
   system reads `log.md` programmatically; the operator reads the file directly, so a
   read-time transformation solves nothing and introduces a parsed representation the spec
   explicitly excludes).

### Authorship

1. **Delete both harness writers and enforce the deletion structurally.** Remove
   `WikiLogAppender` and its call sites in `Grimoire.IngestAgent`/`Grimoire.QueryAgent`;
   remove `RestartReconciler.AppendReconciliationLogAsync`; drop the
   `Grimoire.AgentRuntime.WikiLog` allow-list entry from all three
   `*AgentGuardedWriteBoundaryRuleTests`.
2. Keep `WikiLogAppender` but make it write through the guarded boundary (rejected — the
   content would still be harness-authored, which is the actual violation; routing it
   through a guardrail launders the boundary rather than respecting it).
3. Keep the backstop only for failed runs (rejected — FR-007 makes "changed nothing" the
   criterion for *not* logging, and a failed run that changed nothing is the clearest case
   of a run that must leave no trace in wiki content).

### Replacement diagnostic

1. **One harness-side signal, no wiki write**: a structured log event
   `wiki.log.change_not_logged` (WARN) plus counter `wiki.log.unlogged_change_total`,
   emitted at run end when the run's allowed wiki-content writes are non-zero and the
   activity log is not among them.
2. Re-derive the same fact later from the task artifact (rejected — pushes an operational
   question into an offline reconciliation step and gives no live signal).
3. No replacement at all (rejected — this is the one diagnostic the removed backstop
   carried that existing coverage does not; FR-012a requires it).

## Decision Outcome

Chosen options: **Ordering 1, Authorship 1, Replacement diagnostic 1.**

### Mechanism

**O1 — prepend-only structural check.**
`Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard.ValidateLogEntryFormat`
keeps its position in the evaluation order (after existence/CAS/`WriteMode`, before commit)
and its `guardrails.format_validate` span with `target=log`, and changes to:

1. Deny `log_entry_not_prepended` unless the proposed content **ends with** the current
   on-disk content byte-for-byte. A missing or empty file yields an empty current content,
   for which the check is trivially satisfied — the first agent write creates the file
   (FR-010).
2. Take the **head** (proposed content minus the unchanged suffix). Deny
   `log_entry_malformed_heading` unless its first non-blank line matches the unchanged
   `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`.
3. Deny `log_entry_missing_paragraph` unless at least one further non-blank line follows
   that heading **within the head** — a whitespace- or heading-only prepend is still
   rejected.

The compare-and-swap (`write_conflict_stale_read`), cross-process lock, and `WriteMode`
checks are untouched and still evaluated first, so concurrent prepends are detected exactly
as concurrent appends were (FR-011). `index.md`'s catalog check is untouched.

**O2 — agent-exclusive authorship.**
- `Grimoire.AgentRuntime.WikiLog.WikiLogAppender` is deleted, along with
  `EnsureLogEntryAsync`'s call sites in `Grimoire.IngestAgent.Program` (success and failure
  paths) and `Grimoire.QueryAgent.Program` (success and failure paths).
- `Grimoire.Hub.OperationalState.RestartReconciler.AppendReconciliationLogAsync` is deleted;
  reconciliation continues to record the failure in the task artifact and the operational
  state history, and writes nothing to the wiki.
- The `Grimoire.AgentRuntime.WikiLog` entry is removed from `_allowedNamespacePrefixes` in
  `IngestAgentGuardedWriteBoundaryRuleTests`, `QueryAgentGuardedWriteBoundaryRuleTests`, and
  `LintAgentGuardedWriteBoundaryRuleTests`. The namespace survives (it hosts the write-free
  observer below) but may no longer call any filesystem-write API.

**O3 — replacement diagnostic.**
- `GuardedToolExecutor` exposes two mechanical, journal-derived properties: the run's
  allowed wiki-content writes (its touched paths minus the canonical activity-log path) and
  whether the activity log is among its touched paths. Both are pure harness bookkeeping —
  derived from which writes the guard *allowed*, never from reading or interpreting wiki
  content.
- A new write-free `Grimoire.AgentRuntime.WikiLog.WikiLogCoverageObserver` is invoked once
  at run end by both writing agent processes. It opens a `wiki_log.coverage_check` span
  (attributes `type`, `task_id_or_run_id`, `wiki_content_writes`, `outcome`), and when
  `wiki_content_writes > 0` and the log was not written, emits `wiki.log.change_not_logged`
  (WARN; fields `type`, `task_id_or_run_id`, `wiki_content_writes`) and increments
  `wiki.log.unlogged_change_total` (label `type`). It performs no I/O.
- `wiki.log.backstop_appended` (event), `wiki.log.backstop_appended_total` (metric), and
  `wiki_log.backstop_append` (span) are retired with the backstop.

**O4 — instruction files (Principle V).** The newest-first placement, the one-complete-entry
-per-action rule, and the changes-only criterion are stated in
`backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` and
`backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md`, which are the versioned
files loaded into the agents' context (ADR-007, ADR-022). The Lint agent's instruction file
is **not** changed: Lint never writes the activity log and states no ordering assumption
about it. No deterministic test asserts the wording of any of these files.

### Rule classification (Constitution Principle III)

| # | Rule | Category |
| --- | --- | --- |
| BR-1 | Filesystem-write APIs reachable from `Grimoire.IngestAgent`/`Grimoire.QueryAgent`/`Grimoire.LintAgent` and the shared `Grimoire.AgentRuntime` may be called only from `Grimoire.AgentRuntime.Guardrails*` and `Grimoire.AgentRuntime.Core.Adapters.Replay` — `Grimoire.AgentRuntime.WikiLog` is no longer exempt. | **Boundary Rule** — a dependency direction (who may reach the filesystem), durable across feature growth; enforced by the three existing IL-level `*GuardedWriteBoundaryRuleTests`, re-probed Red/Green after the allow-list entry is removed. |
| FSI-1 | The activity log's guarded-write check is prepend-only: current content must be an unchanged suffix; a missing or empty file is a valid base; a whitespace- or heading-only prepend is denied. | **Feature-Scoped Invariant** — this feature's current surface shape, verified by classicist state-based tests that drive the real guard and assert the resulting denial reason and on-disk bytes. Never by reflecting over the guard's shape. |
| FSI-2 | No harness component writes to the resolved activity-log path: neither agent process on any exit path, nor the Hub's restart reconciler. | **Feature-Scoped Invariant** for the Hub half (BR-1 covers the agent assemblies structurally, but the Hub legitimately writes many other files, so its half is verified behaviourally): run the real reconciler against a temp content root containing an activity log and assert the file is byte-for-byte unchanged. |

FR-012a's signal is not a separate rule — it is an observability contract, covered by
Principle IV's derivation rule (implementation + deterministic integration test + CI) in
`specs/025-agent-owned-log/plan.md ## Observability`.

### Consequences

- Good, because the operator's requested ordering becomes a 100% deterministic guarantee at
  the same chokepoint that guaranteed the opposite, with no new mechanism — one comparison
  and one substring bound change inside an existing method.
- Good, because `log.md` becomes unambiguously agent-owned wiki content, restoring the
  Principle V boundary that the backstop crossed; the boundary is held by an existing
  structural test that simply loses an exemption, which is the cheapest possible enforcement.
- Good, because the one diagnostic worth keeping is preserved as an operational signal that
  cannot, by construction, put harness prose into the wiki.
- Bad, because activity logs written before this change keep their oldest-first section: an
  existing file ends up newest-first above older oldest-first entries. Accepted — FR-014
  explicitly refuses migration, and the alternative (a harness rewriting wiki content) is
  the violation this ADR removes.
- Bad, because a run that changes the wiki and simply fails to log it now leaves the wiki's
  own record incomplete, where the backstop would have papered over it. Accepted, and made
  visible rather than hidden: that is exactly what `wiki.log.change_not_logged` reports, and
  SC-005/SC-006 measure the agent's compliance at an evaluation threshold instead of
  pretending a deterministic backstop made it 100%.
- Bad, because the per-action entry shape decided here (one heading per action, no
  day-grouping) conflicts with the day-grouped bullet shape sketched in issue #38. Accepted
  and deferred: whichever feature addresses the entry format must resolve that conflict
  against the operator's explicit rejection of day-grouping, not silently adopt #38's
  grouping.

## More Information

Detailed rationale: `specs/025-agent-owned-log/research.md`;
contract: `specs/025-agent-owned-log/contracts/activity-log-write-contract.md`.
Per Constitution Principle III this ADR MUST reach **Accepted** before `/speckit-tasks`
runs for feature 025.

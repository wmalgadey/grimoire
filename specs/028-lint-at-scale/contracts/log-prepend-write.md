# Contract: `write_file`'s Prepend Mode

Extends the existing `write_file` guarded tool (`ToolRegistry.WriteFileDefinition`,
declared identically by `LintToolRegistry`, `IngestToolRegistry`, and `QueryToolRegistry`).
This is a schema and dispatch-path addition to one existing tool — no new tool name, no
new port, no new external system, and — per Constitution Principle III's existing
"Single-aspect ADRs; no feature content" test (one genuine system boundary or one
technology choice, neither of which this changes) — no new ADR: it operates inside the
guarded tool boundary ADR-006 already decided, and its two rules (schema addition,
no-baseline dispatch) are the Feature-Scoped Invariants FSI-1/FSI-2 in `plan.md`. The format-validation
and scope rules this contract states (below) are likewise feature content, mirroring the
same "format content lives in a contract, not an ADR" split `main`'s ADR-035
(agent-exclusive-activity-log-authorship) already established for the log's ordering rule.

## Tool call schema (JSON)

Before (unchanged, still the default):

```jsonc
{
  "path": "log.md",
  "content": "## [2026-08-25] Ingest run a1b2c3d4 | ...\n\nFull entry text.\n\n## [2026-08-24] ... (every prior entry, byte-for-byte)"
}
```

After — new, optional `mode`:

```jsonc
{
  "path": "log.md",
  "mode": "prepend",
  "content": "## [2026-08-25] Ingest run a1b2c3d4 | one-line summary\n\nWhat changed and why, in the agent's own words."
}
```

- `mode` omitted or `"replace"`: byte-identical to today's behavior — `content` is the full
  proposed file, unchanged validation and commit path.
- `mode: "prepend"`: `content` is **the new entry only**. The harness reads `log.md`'s
  current content itself and assembles `content + currentContent` before validating and
  committing. The schema's `additionalProperties: false` constraint means `mode` must be
  declared in the JSON schema itself (`ToolRegistry.cs`), not merely accepted permissively
  by the executor.
- `mode: "prepend"` is accepted for any target path, not only `log.md` — the same
  concatenation mechanism applies generically; the activity-log *format checks* below
  remain gated on the target being `log.md` specifically (unchanged — `index.md` and any
  other path are unaffected, "What does not change" below).

## Validation, in order (prepend mode)

1. Existing per-target `CrossProcessFileLock` acquisition (unchanged — `write_coordination_timeout` on contention).
2. **No compare-and-swap check.** Unlike `ReadWrite`-mode writes to an existing file
   (which deny `write_conflict_stale_read` against a prior `OnReadFile` baseline), a
   prepend write reads current content fresh, under the lock, at evaluation time — there is
   no baseline to compare against and no staleness scenario to deny (research.md R8).
3. Heading/paragraph format check, retargeted (feature-scoped format content, not a
   Feature-Scoped Invariant or ADR decision — see the note at the top of this contract):
   the supplied `content` (the entry alone) is checked against a first-non-blank-line
   heading matching `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`, with at least one further
   non-blank line following within the entry. **This check never denies the write**
   (Clarifications 2026-08-27; research.md R15; plan.md FSI-3): a non-conforming entry
   still commits, and the deviation is recorded as `log_entry_malformed_heading` and/or
   `log_entry_missing_paragraph` via `WikiLogEvents.LogFormatDeviation` (structured log
   event) and `WikiLogMetrics.RecordFormatDeviation` (counter) — see "Format/ordering
   deviation signal," below.
4. Atomic commit of `content + currentContent` via the existing temp-file + `File.Move`
   path, unconditionally (regardless of step 3's outcome). `OnWriteCommitted` re-baselines
   the file exactly as today, for any subsequent write in the same run.

A missing or empty `log.md` is a valid base — the first prepend write's entry becomes the
whole file, identical in effect to today's first-write behavior under `ReadWrite` mode.

## Format/ordering deviation signal

Applies identically to `mode: "replace"` and `mode: "prepend"` writes to `log.md` — this is
not a prepend-mode-only concern, since the underlying format check (§3 above; the
equivalent check on the `mode: "replace"` path) is the same one this feature relaxes on
both paths at once (research.md R15).

- **Reason codes** (one write may carry more than one): `log_entry_not_prepended` (proposed
  content does not end with current content, byte-for-byte — the prepend-only ordering
  rule); `log_entry_malformed_heading`; `log_entry_missing_paragraph`.
- **Structured log event**: `wiki.log.format_deviation` (WARN), fields `agent`
  (`ingest`\|`query`\|`lint`), `mode` (`replace`\|`prepend`), `path`, `reason` (comma-joined
  if more than one code applies).
- **Metric**: `wiki.log.format_deviation_total` (Counter), labels `agent`, `mode`, `reason`.
- Both extend the existing `Grimoire.AgentRuntime.WikiLog.WikiLogEvents`/`WikiLogMetrics`
  components (data-model.md, "Format/ordering deviation signal"; plan.md Observability) —
  not a new telemetry surface.
- **Never emitted for a conforming write.** A write whose content already matches the
  expected shape produces no event and no metric increment — this signal exists to name
  deviations, not to record every write.

## Concurrent writers

Two agents (or two processes) both submitting a prepend write to `log.md` at nearly the
same time are serialized by the lock, not denied. Each acquires the lock in turn, reads
whatever content is current *at that moment* (including any entry the other writer just
committed), and prepends onto it. Both entries land, newest-first, in lock-acquisition
order — this is the expected, correct outcome, not a conflict to report. Contrast with a
`ReadWrite`-mode full-content write: there, a writer that read stale content and proposes a
full replacement genuinely can clobber a concurrent change, which is exactly what the CAS
check exists to deny. Prepend mode has no equivalent hazard by construction, so it needs no
equivalent check.

## What does not change

- `index.md`'s catalog-entry format check (`ValidateCatalogEntryFormat`) — entirely separate
  mechanism, untouched, not reachable from any prepend-mode code path, and not reclassified
  by this feature (still denies, per `contracts/log-prepend-write.md`'s scope — `log.md`
  only).
- The CAS check (`write_conflict_stale_read`) and the `FrontmatterOnly` body-preservation
  check, for `ReadWrite`-mode writes to `log.md` or anywhere else — a structurally separate
  branch of `EvaluateExistingTargetChecksAsync` from the format check this feature
  reclassifies (research.md R15); both still deny exactly as before. The atomic commit path
  itself is also unchanged.
- The policy-level `Grimoire.Domain.Guardrails.WriteMode` enum — zero new members, zero
  new branches (data-model.md, "Two distinct 'mode' concepts").
- Which agents can write `log.md` at all — governed by each agent's own `SafetyPolicy`,
  unchanged by this feature (Ingest and Query already could; Lint already could per
  ADR-031).

## What does change beyond prepend mode itself

Unlike every other section of this contract, the format/ordering reclassification above is
**not** scoped to `mode: "prepend"` — it applies identically to `mode: "replace"` writes to
`log.md`, reversing pre-existing, shipped behavior from spec 025/ADR-028 (research.md R15).
This is the one place this feature changes something about the write path an agent was
already using before this feature shipped.

## Instruction-file consumers (Constitution Principle V)

Three files must be updated to actually call `mode: "prepend"` instead of the "read the
whole file, then write your entry followed by exactly what you read" pattern — landing only
the harness capability without this leaves the observed production failure (issue #201)
unfixed, since Ingest's current instructions never call the new path:

- `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` (~line 325, "Ingest Log
  (log.md) Upkeep")
- `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md` (~lines 192-213, log.md
  section)
- `backend/src/Grimoire.LintAgent/Instructions/system-prompt.md` ("Reconciling `index.md`
  and `log.md`" step)

No deterministic test asserts the wording of any of these files (Constitution Principle V)
— only that the harness mechanism they now call behaves as this contract describes.

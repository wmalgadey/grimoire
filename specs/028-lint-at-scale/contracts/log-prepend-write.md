# Contract: `write_file`'s Prepend Mode

Extends the existing `write_file` guarded tool (`ToolRegistry.WriteFileDefinition`,
declared identically by `LintToolRegistry`, `IngestToolRegistry`, and `QueryToolRegistry`).
Per ADR-035, this is a schema and dispatch-path addition to one existing tool — no new tool
name, no new port, no new external system.

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
  concatenation mechanism applies generically; ADR-017/ADR-028's *format checks* remain
  gated on the target being `log.md` specifically (unchanged — `index.md` and any other
  path are unaffected, ADR-035 R4).

## Validation, in order (prepend mode)

1. Existing per-target `CrossProcessFileLock` acquisition (unchanged — `write_coordination_timeout` on contention).
2. **No compare-and-swap check.** Unlike `ReadWrite`-mode writes to an existing file
   (which deny `write_conflict_stale_read` against a prior `OnReadFile` baseline), a
   prepend write reads current content fresh, under the lock, at evaluation time — there is
   no baseline to compare against and no staleness scenario to deny (research.md R8).
3. Heading/paragraph format validation, retargeted (ADR-035 R3): the supplied `content`
   (the entry alone) must have, as its first non-blank line, a heading matching
   `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`, and at least one further non-blank line must
   follow within the entry. Denial reasons unchanged: `log_entry_malformed_heading`,
   `log_entry_missing_paragraph`.
4. Atomic commit of `content + currentContent` via the existing temp-file + `File.Move`
   path. `OnWriteCommitted` re-baselines the file exactly as today, for any subsequent
   write in the same run.

A missing or empty `log.md` is a valid base — the first prepend write's entry becomes the
whole file, identical in effect to today's first-write behavior under `ReadWrite` mode.

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
  mechanism, untouched, not reachable from any prepend-mode code path (ADR-035 R4).
- `ReadWrite`-mode writes to `log.md` or anywhere else — the CAS check, the frontmatter
  preservation check (for `FrontmatterOnly`-mode targets elsewhere in the wiki), and the
  atomic commit path are all byte-for-byte unchanged.
- The policy-level `Grimoire.Domain.Guardrails.WriteMode` enum — zero new members, zero
  new branches (data-model.md, "Two distinct 'mode' concepts").
- Which agents can write `log.md` at all — governed by each agent's own `SafetyPolicy`,
  unchanged by this feature (Ingest and Query already could; Lint already could per
  ADR-031).

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

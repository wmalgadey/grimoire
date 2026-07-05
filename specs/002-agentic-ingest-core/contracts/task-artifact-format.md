# Contract: Task Artifact File Format (v2 — agentic core)

Extends the 001 format. Canonical shape for every
`<content-root>/tasks/<task_id>.md` file. Writer: the Ingest agent process (harness
code, not the model — the model only contributes the narrative body). Readers: Hub,
future channels/UI, and humans.

## Frontmatter (YAML)

```yaml
---
task_id: 2026-07-04-ingest-example-source
type: ingest
status: running            # queued | running | completed | failed
agent: ingest
started_at: 2026-07-04T10:15:00Z
completed_at: null          # set once status is terminal
source_ref: "raw/sources/example.md"

# --- extended in 002 ---
pages_created: []           # repo-relative paths, from the write journal
pages_updated: []
pages_superseded: []
denied_actions: [{"action":"write_file","requested_target":"../etc/passwd","canonical_target":"/etc/passwd","reason":"traversal","turn":2}]
instruction_files: [{"path":"agents/ingest/CLAUDE.md","sha256":"…"},{"path":"agents/ingest/skills/wiki-maintenance/SKILL.md","sha256":"…"}]
policy: {"path":"agents/ingest/policy.json","version":1,"sha256":"…"}
model: claude-opus-4-8      # effective GRIMOIRE_INGEST_MODEL
turns: null                 # model turns consumed, set at finalization
rolled_back: null           # failure only: true/false = journal restore outcome
failure_reason: null        # human-readable, failure only
---
```

`pages_touched` (001) is replaced by the three action-specific lists; their union is the
equivalent set.

## Body (Markdown)

Human-readable narrative. **Source of the text**: the agent's final summary message,
copied verbatim by the harness (Principle V — narrative is agent judgment). Minimum
content by status:

- `running`: what run is starting, for which source.
- `completed`: which pages were touched and **why those were the right ones** (spec
  US1-AC3), plus any uncertainties flagged for review. If actions were denied, the
  harness appends a `## Denied actions` section listing them (mirrors frontmatter).
- `failed`: plain-language explanation consistent with `failure_reason`; if the model
  produced no usable narrative (crash, load failure), the harness writes a minimal
  factual account.

## Consistency rules (validated hermetically by integration tests)

- `status: completed` ⇒ every path in the three `pages_*` lists exists on disk under the
  content root, and each appears in exactly one list.
- `status: failed` ⇒ `failure_reason` non-null; `rolled_back` non-null; all `pages_*`
  lists empty (rollback restored prior state — FR-013).
- `instruction_files` and `policy` are non-empty on **every** artifact that reached
  `running` with a loaded governance set; a load-failure artifact records what could not
  be loaded in `failure_reason` (FR-003, FR-012).
- `denied_actions` in frontmatter is the complete denial record for the run (SC-002);
  each entry includes `action`, `requested_target`, `canonical_target`, `reason`,
  and `turn`; entries are never dropped on success.
- `completed_at` null while non-terminal, non-null otherwise; interrupted runs are
  reconciled to `failed` by the Hub on restart (unchanged, ADR-003).

# Contract: Task Artifact File Format

Canonical shape for every `<content-root>/tasks/<YYYY-MM-DD>-ingest-<slug>.md` file (see
`data-model.md ## IngestTask`). Both the Ingest agent (writer) and any future channel/UI
(reader) MUST conform to this shape.

## Frontmatter (YAML)

```yaml
---
task_id: 2026-07-03-ingest-example-source
type: ingest
status: running        # queued | running | completed | failed
agent: ingest
started_at: 2026-07-03T10:15:00Z
completed_at: null      # set once status is completed or failed
source_ref: "raw/sources/example.md"
pages_touched: []       # list of wikilinks, populated on completion
failure_reason: null    # human-readable string, set only when status = failed
---
```

## Body (Markdown)

Free-form human-readable narrative. Minimum content by status:

- `running`: what the agent is about to do / has started reading.
- `completed`: what was found in the source, what changed in the wiki (page created vs.
  updated, why), and any uncertainties flagged for human review (FR-006).
- `failed`: a plain-language explanation of what went wrong, consistent with
  `failure_reason` (FR-007).

## Consistency rules (validated by integration tests, per `plan.md`)

- `status = completed` ⇒ `pages_touched` is non-empty and every listed page exists on
  disk under `<content-root>/pages/` (FR-011).
- `status = failed` ⇒ `failure_reason` is non-null and `pages_touched` is empty (FR-008).
- `completed_at` is null while `status` is `queued`/`running`, non-null otherwise.

# Data Model: Ingest Minimal

Derived from `spec.md ## Key Entities` and the Technical Context in `plan.md`. Domain
entities are dependency-free (constitution Principle I) and live in `Grimoire.Domain`;
persistence shapes (SQLite row, file layout) are adapter concerns in `Grimoire.Hub` /
`Grimoire.IngestAgent`.

## Source

Immutable input to an ingest operation. Never created or modified by the system.

| Field | Type | Notes |
|---|---|---|
| `reference` | string | File path, URL, or literal pasted text |
| `kind` | enum: `file` \| `url` \| `pasted_text` | Determines how the Ingest agent reads it |

No persistence — read-only, referenced by id from `IngestTask.source_ref`.

## IngestTask (Task Artifact)

One per ingest operation. Structured frontmatter (machine-readable) + markdown body
(human-readable narrative), per `docs/decision-context-overview.md`'s task-artifact
contract. Persisted as `tasks/<YYYY-MM-DD>-ingest-<slug>.md` (domain state, git-tracked).

| Field (frontmatter) | Type | Notes |
|---|---|---|
| `task_id` | string (uuid or slug) | Stable identifier, used to correlate OTel spans |
| `type` | `ingest` | Fixed for this feature |
| `status` | enum: `queued` \| `running` \| `completed` \| `failed` | State machine below |
| `agent` | `ingest` | Which agent produced this task |
| `started_at` | datetime | Set on creation (FR-002) |
| `completed_at` | datetime \| null | Set on reaching a terminal status |
| `source_ref` | string | The `Source.reference` this task processed |
| `pages_touched` | list of wikilinks | Populated on success (FR-005); empty on failure |
| `failure_reason` | string \| null | Human-readable reason (FR-007); null unless `status = failed` |

Body (markdown): free-form narrative — what was found, what changed, any uncertainties
(FR-006).

**State transitions**: `queued → running → completed` (happy path) or
`queued → running → failed` (failure path, FR-007). A Hub restart while `status = running`
with no live process transitions the task to `failed` with `failure_reason` noting the
interruption (FR-013; see ADR-003).

**Validation rules**:
- `completed_at` MUST be null while `status` is `queued` or `running`.
- `pages_touched` MUST be non-empty when `status = completed` (FR-011: no reporting
  completion without a consistent wiki page).
- `failure_reason` MUST be non-null when `status = failed`.

## WikiPage

LLM-authored markdown file representing synthesized knowledge. Owned entirely by Ingest.

| Field | Type | Notes |
|---|---|---|
| `path` | string | Location under `wiki/` |
| `title` | string | Page title |
| `category` | string | Ingest agent's semantic judgment (FR-014); no fixed taxonomy in this slice |
| `content` | markdown body | Synthesized from one or more sources over time |

No fixed schema beyond what the Ingest agent's `CLAUDE.md`/`SKILL.md` (future work,
out of scope for this feature) defines; this feature only requires that exactly one
primary page is created or updated per ingest operation (FR-004).

## WikiIndex (index.md)

Singleton catalog, one row per `WikiPage`, grouped by `category`.

| Field | Type | Notes |
|---|---|---|
| `category` | string | Heading grouping (FR-014); matches `WikiPage.category` |
| `page_link` | wikilink | Link to the `WikiPage` |
| `summary` | string | One-line summary |

**Validation rule**: Every successful ingest MUST result in exactly one entry (new or
updated) under the correct category heading (SC-006).

## IngestLogEntry (log.md line)

Append-only; one entry per ingest attempt, success or failure (FR-015).

| Field | Type | Notes |
|---|---|---|
| `timestamp` | datetime | |
| `operation` | `ingest` | |
| `outcome` | enum: `completed` \| `failed` | |
| `task_ref` | wikilink | Link back to the `IngestTask` that produced this entry |

Format: `## [YYYY-MM-DD] ingest | <short outcome description>` (parseable prefix, per
`docs/llm-wiki-nanoclaw-idea.md` convention).

## OperationalTaskState (SQLite row, Hub-owned, not domain state)

Adapter-level shape used only for restart reconciliation (ADR-003); not part of the
domain model, not git-tracked.

| Column | Type | Notes |
|---|---|---|
| `task_id` | text primary key | Matches `IngestTask.task_id` |
| `status` | text | Mirrors `IngestTask.status` while non-terminal |
| `process_id` | integer \| null | PID of the spawned Ingest agent child process |
| `updated_at` | datetime | Last heartbeat/status write |

On Hub startup: any row with `status = 'running'` is reconciled to `failed`, the
corresponding `IngestTask` file is updated, and the row is removed (FR-013).

# Data Model: Ingest Intake Web UI

Derived from [spec.md](./spec.md) and the implementation strategy in [plan.md](./plan.md).

## IngestSubmission

User-originated ingest submission request, one source per submission.

| Field | Type | Notes |
| --- | --- | --- |
| `ingest_submission_id` | string | Stable id for request/trace correlation |
| `kind` | enum: `url` \| `markdown_file` \| `pdf_file` \| `office_file` | Source type validated at intake |
| `submitted_at` | datetime | UTC acceptance timestamp |
| `submitted_by` | string \| null | Null in current single-user scope |
| `original_reference` | string | URL or uploaded filename metadata |

Validation rules:
- Exactly one source per submission.
- Unsupported kind is rejected before task creation.

## SourceArtifactSet

Persisted provenance + ingest input produced by the ingest-submission pipeline.

| Field | Type | Notes |
| --- | --- | --- |
| `task_id` | string | Foreign key to TaskArtifact |
| `original_path` | string | Persisted original source payload path (`raw/originals/...`) |
| `original_content_type` | string | MIME type or inferred media type |
| `original_size_bytes` | integer | Original payload size |
| `normalized_markdown_path` | string | Canonical ingest input path (`raw/sources/...`) |
| `normalized_checksum` | string | SHA-256 for immutability checks |
| `created_at` | datetime | Persist timestamp |

Validation rules:
- `normalized_markdown_path` must exist before state can move to `queued`.
- For failed conversion/fetch, no partial `normalized_markdown_path` remains.

## TaskArtifact (existing project-wide entity)

Lifecycle record visible in board and consumed by orchestration.

| Field | Type | Notes |
| --- | --- | --- |
| `task_id` | string | Existing task id format |
| `status` | enum: `received` \| `converting` \| `queued` \| `running` \| `completed` \| `failed` | End-to-end lifecycle for this feature |
| `source_kind` | enum | Mirrors submission kind |
| `source_ref` | string | Points to normalized markdown artifact |
| `original_ref` | string | Points to original artifact path |
| `started_at` | datetime | Created at acceptance |
| `completed_at` | datetime \| null | Set on terminal state |
| `failure_reason` | string \| null | Human-readable failure reason |

State transitions:
- Ingest-submission phase: `received -> converting -> queued` or `received/converting -> failed`
- Ingest-run phase (existing): `queued -> running -> completed|failed`

Validation rules:
- Terminal `failed` requires non-null `failure_reason`.
- `queued` requires resolved normalized markdown artifact path.
- `running/completed/failed` remain agent-owned once dispatch starts.

## KanbanBoardProjection

Read model for frontend board grouping and card rendering.

| Field | Type | Notes |
| --- | --- | --- |
| `task_id` | string | Card identity |
| `column` | enum (same as lifecycle status) | Grouping key |
| `title` | string | Source-derived display title |
| `subtitle` | string | Optional source descriptor (host/filename) |
| `updated_at` | datetime | Last transition time |
| `failure_reason` | string \| null | Rendered for failed cards |
| `task_link` | string | Link to full Task Artifact |

Rules:
- Each task appears exactly once on the board.
- Projection updates on each lifecycle transition event.

## RealtimeLifecycleEvent

SignalR payload emitted by Hub for board updates.

| Field | Type | Notes |
| --- | --- | --- |
| `event_id` | string | Idempotency/correlation |
| `task_id` | string | Target task |
| `from_status` | lifecycle status \| null | Null for first emission |
| `to_status` | lifecycle status | New status |
| `timestamp` | datetime | Event time |
| `failure_reason` | string \| null | Present for failed transitions |

Rules:
- Events are append-only and ordered by timestamp per `task_id`.
- Clients treat latest event per `task_id` as authoritative state.

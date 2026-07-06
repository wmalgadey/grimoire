# Contract: Source Artifact Reference

Defines how ingest submission processing stores and references original and normalized artifacts per submission.

## Canonical Artifact Pair

Each accepted ingest submission produces one `SourceArtifactSet`:

1. `original_ref`: immutable persisted original source payload.
2. `source_ref`: immutable normalized markdown artifact used for ingest trigger.

Both references are recorded on Task Artifact metadata.

## Path Conventions

- Original payload path: `raw/originals/{task_id}{ext}`
- Normalized markdown path: `raw/sources/{task_id}.md`

## Metadata Fields

| Field | Description |
| --- | --- |
| `task_id` | Correlates both artifacts and lifecycle records |
| `original_ref` | Relative path to stored original payload |
| `original_content_type` | MIME or inferred media type |
| `original_size_bytes` | Original payload size |
| `source_ref` | Relative path to normalized markdown |
| `normalized_checksum` | SHA-256 of normalized markdown bytes |

## Behavioral Guarantees

- Ingest dispatch consumes `source_ref` (normalized markdown), not URL re-fetch.
- Original payload remains available for diagnostics and optional LLM-assisted review flows.
- Failed conversion/fetch must not leave partial normalized artifact files.

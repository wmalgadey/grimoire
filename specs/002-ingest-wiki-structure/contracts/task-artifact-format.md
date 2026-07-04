# Contract: Task Artifact Format (Feature 002)

## Purpose

Define the minimal structured artifact required for deterministic validation and audit of autonomous ingest runs.

## File Location

- Output file: {tasks-dir}/{task-id}.md

## Markdown Structure

1. YAML frontmatter
2. Human-readable summary body
3. Optional fenced diagnostics block for failures

## YAML Frontmatter Schema

```yaml
task_id: string
operation: ingest
status: running|completed|failed
started_at: ISO-8601 datetime
finished_at: ISO-8601 datetime|null
source_ref: string
created_paths: string[]
updated_paths: string[]
superseded_paths: string[]
denied_actions:
  - action: string
    target_path: string
    reason: string
instruction_context:
  claude_path: string
  skill_paths: string[]
  content_hash: string
```

## Validation Rules

- task_id, operation, status, started_at, source_ref are required.
- finished_at is required for completed or failed statuses.
- denied_actions entries require action, target_path, and reason.
- instruction_context must be present for any run that attempted wiki writes.
- created_paths, updated_paths, superseded_paths must only contain repository-relative paths.

## Status Semantics

- running: Initial state once ingest starts.
- completed: Terminal state after allowed actions and index updates are committed.
- failed: Terminal state on unrecoverable error; summary and diagnostics must include reason.

## Determinism Guarantees

- Artifact field names and status transitions are deterministic.
- Action lists are deterministic given evaluated action decisions.
- Content text in generated wiki pages is not required to be deterministic.

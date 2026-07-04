# Data Model: Ingest Wiki Structure

## Entity: IngestRun

- Purpose: Runtime aggregate for one ingest submission from start to terminal state.
- Fields:
  - taskId: string (unique, required)
  - sourceRef: string (required, immutable)
  - sourceKind: enum(file, pasted_text, url)
  - startedAt: datetime (required)
  - finishedAt: datetime (nullable)
  - status: enum(running, completed, failed)
  - instructionSnapshotId: string (required)
  - deniedActionCount: integer (>=0)
- Relationships:
  - 1:N with WikiWriteAction
  - 1:1 with TaskArtifactRecord
  - 1:1 with InstructionContextSnapshot
- Validation rules:
  - finishedAt is required when status is completed or failed
  - sourceRef must never be modified by ingest

## Entity: InstructionContextSnapshot

- Purpose: Evidence that CLAUDE.md and selected SKILL.md were applied before writes.
- Fields:
  - snapshotId: string (unique)
  - claudePath: string (required)
  - skillPaths: string[] (required, at least one)
  - loadedAt: datetime (required)
  - contentHash: string (required)
  - loadStatus: enum(loaded, missing, invalid)
- Relationships:
  - 1:1 with IngestRun
- Validation rules:
  - loadStatus must be loaded for write-capable autonomous runs

## Entity: GuardrailPolicy

- Purpose: Versioned allowlist policy consumed by autonomous tool wrapper.
- Fields:
  - policyVersion: string (required)
  - writeAllowPrefixes: string[] (required, includes wiki/ and task artifact outputs)
  - readAllowPaths: string[] (required)
  - denyByDefault: boolean (required, true)
  - updatedAt: datetime (required)
- Validation rules:
  - writeAllowPrefixes must not include repository root wildcard
  - readAllowPaths must reference repository-relative paths only

## Entity: WikiWriteAction

- Purpose: Deterministic representation of intended and applied wiki mutations.
- Fields:
  - actionId: string (unique)
  - taskId: string (required, foreign key to IngestRun)
  - actionType: enum(create, update, supersede, denied)
  - pageKind: enum(source, entity, concept, index)
  - targetPath: string (required)
  - sourcePaths: string[] (optional)
  - reason: string (required)
  - deniedReason: string (optional, required when actionType is denied)
  - appliedAt: datetime (nullable)
- Validation rules:
  - deniedReason is required for denied actions
  - actionType=supersede requires sourcePaths length >= 1

## Entity: WikiPageRecord

- Purpose: Metadata contract for non-source wiki pages created/updated by ingest.
- Fields:
  - path: string (required)
  - tags: string[] (required, min 2 for non-source pages)
  - confidence: number (required, 0..1)
  - confidenceReason: string (required)
  - inboundLinks: string[] (required)
  - lastReviewed: date (required)
  - supersedes: string[] (optional)
  - supersededBy: string (optional)
- Validation rules:
  - supersedes and supersededBy must only exist when supersession is explicit

## Entity: TaskArtifactRecord

- Purpose: Minimal structured markdown artifact for deterministic validation and audit.
- Fields:
  - taskId: string (required)
  - operation: string (ingest)
  - status: enum(running, completed, failed)
  - startedAt: datetime
  - finishedAt: datetime (nullable)
  - sourceRef: string
  - createdPaths: string[]
  - updatedPaths: string[]
  - supersededPaths: string[]
  - deniedActions: array of { action, targetPath, reason }
  - summary: string
- Validation rules:
  - completed status requires terminal lists and finishedAt
  - failed status requires failure reason in summary

## State Transitions

1. IngestRun.status: running -> completed
- Preconditions: all required allowed actions applied, index refreshed, artifact persisted.

2. IngestRun.status: running -> failed
- Preconditions: unrecoverable error or rollback failure encountered.
- Postconditions: no partial contradictory wiki state remains; artifact records failure details.

3. WikiWriteAction.actionType: planned -> denied
- Trigger: policy evaluator denies action.
- Postconditions: action not executed, denial captured, run continues.

4. WikiWriteAction.actionType: planned -> create|update|supersede
- Trigger: policy allows action and write succeeds.
- Postconditions: artifact action arrays and index updates remain consistent.

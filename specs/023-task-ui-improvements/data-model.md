# Data Model: Task Visibility & Recovery Improvements

**Feature**: `023-task-ui-improvements` | **Date**: 2026-08-13

Decisions behind each shape: see [research.md](./research.md) (R1–R6).

## 1. Status history — `ingest_status_history` (SQLite, Hub-owned operational DB)

Append-only. One row per lifecycle transition observed by the Hub. Never updated or deleted (survives task completion, restart, and Hub restarts).

| Column | Type | Constraints | Notes |
| --- | --- | --- | --- |
| `task_id` | TEXT | NOT NULL | Ingest task id (`yyyy-MM-dd-ingest-{guid:N}`) |
| `seq` | INTEGER | NOT NULL | Monotonic per task, starts at 1; `PRIMARY KEY (task_id, seq)` |
| `status` | TEXT | NOT NULL | One of the Status vocabulary below |
| `entered_at` | TEXT | NOT NULL | UTC ISO-8601, from the Hub's `TimeProvider` |
| `detail` | TEXT | NULL | Human-readable context: failure reason, `attempt` number for interruption/reactivation entries, restart origin |

**Status vocabulary** (superset of the board's `LifecycleStage`; board columns unchanged):

- Board stages (existing): `received`, `converting`, `queued`, `running`, `completed`, `failed`
- History-only entries (new, never a board column): `liveness_interrupted`, `reactivated`, `restarted`

**Writers**: single choke point in the Hub — the lifecycle-recording component invoked wherever `IngestLifecyclePublisher` publishes a transition, plus the coordinator for the three history-only entries. The agent process never writes this table.

**Validation rules**:

- `seq` strictly increasing per `task_id`, no gaps required but no reuse.
- A `reactivated` entry MUST be preceded by a `liveness_interrupted` entry (FR-007/FR-008).
- A `restarted` entry is valid only when the immediately preceding board-stage entry is `failed` (FR-010/FR-011).
- History for a task is never truncated on restart (FR-013).

### State transitions (per task)

```text
received → converting → queued → running → completed
                                   │
                                   ├→ failed                        (conversion/start/agent failure — unchanged)
                                   │
                                   └→ liveness_interrupted → reactivated → running   (attempt < max)
                                                │
                                                └→ failed           (attempts exhausted → final)
failed → restarted → queued → running → …                            (manual, CAS-guarded)
```

`completed` remains terminal. `failed` is terminal for automatic behavior; only a manual restart re-enters from it.

## 2. Operational task state — `operational_task_state` (existing table, extended)

| Column | Change | Notes |
| --- | --- | --- |
| `attempt` | NEW, INTEGER NOT NULL DEFAULT 0 | Reactivation attempt counter for the current run occupancy; reset to 0 on restart and on normal start |

Rows remain transient (deleted on `FinishRunAsync`) — durability of attempts lives in the history `detail` field.

## 3. Source-artifact manifest — `{taskId}.manifest.json` (existing sidecar, extended)

Hub-owned, single-writer (pipeline). Existing fields unchanged (`TaskId`, `OriginalPath`, `OriginalContentType`, `OriginalSizeBytes`, `NormalizedMarkdownPath`, `NormalizedChecksum`, `CreatedAt`). New fields:

| Field | Type | Notes |
| --- | --- | --- |
| `Title` | string \| null | First ATX `# ` heading of the normalized markdown, trimmed, ≤ 120 chars; null when none found |
| `OriginalFileName` | string \| null | As-uploaded filename for file submissions (today discarded after extension sniffing); null for URL submissions |

**Label fallback chain** (applied at read time, covers pre-feature tasks with older manifests): `Title` → `OriginalFileName` → submitted URL (`sourceRef` when it is http/https) → `taskId`.

## 4. HTTP response shapes (deltas)

Full contract: [contracts/http-api.md](./contracts/http-api.md).

- **Board rows** (`GET /api/ingest-submissions`, `GET /api/board` ingest entries): `title` now carries the label from the fallback chain (was: filename of `sourceRef` ≈ task id). No new fields; no removed fields.
- **Task detail** (`GET /api/ingest-submissions/{taskId}`): gains `title` (string), `statusHistory` (ordered array of `{status, enteredAt, detail}`), `source` (`{kind: "url"|"file", href: string|null, available: boolean}`). Existing fields unchanged.
- **New**: `POST /api/ingest-submissions/{taskId}/restart` → 202 `{taskId, status: "queued"}` | 409 `{reason}` | 404.
- **New**: `GET /api/ingest-submissions/{taskId}/source/original` → 200 stream (manifest content type, inline) | 404.

## 5. Frontend types (`frontend/src/lib/types.ts` deltas)

```ts
// LifecycleStage unchanged — board columns keep the existing six stages.

export type HistoryStatus =
  | LifecycleStage
  | 'liveness_interrupted'
  | 'reactivated'
  | 'restarted';

export interface StatusHistoryEntry {
  status: HistoryStatus;
  enteredAt: string;       // UTC ISO-8601
  detail: string | null;
}

export interface TaskSourceLink {
  kind: 'url' | 'file';
  href: string | null;     // null when unavailable
  available: boolean;
}

// TaskDetail gains: title: string; statusHistory: StatusHistoryEntry[]; source: TaskSourceLink;
// BoardTask.title: semantic change only (now human-readable label) — type unchanged.
```

## 6. Key entity mapping (spec → implementation)

| Spec entity | Implementation |
| --- | --- |
| Task | Existing task artifact + board projection; label via manifest fallback chain (§3); id unchanged and still shown secondarily (FR-004) |
| Status Transition | Row in `ingest_status_history` (§1); ordered by `seq`; "path" = the ordered rows |
| Source Reference | `sourceRef` (unchanged, still returned) + derived `source` link object (§4) |

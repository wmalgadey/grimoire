# HTTP API Contract: Task Visibility & Recovery Improvements

**Feature**: `023-task-ui-improvements`

All routes live under the existing ingest surface mapped in `IngestSubmissionEndpoints`. Shapes below show deltas and new endpoints only; unlisted fields are unchanged.

## Changed: `GET /api/ingest-submissions` (board)

`tasks[].title` semantic change: now the human-readable label from the fallback chain (manifest `Title` → `OriginalFileName` → submitted URL → `taskId`). Type unchanged (string, non-null). Same change applies to ingest entries of the composite `GET /api/board`.

## Changed: `GET /api/ingest-submissions/{taskId}` (detail)

Response gains three fields:

```jsonc
{
  // ...existing fields (taskId, status, failureReason, sourceRef, originalRef,
  //    userPromptSource, userPrompt, convertSteps, runActivity) unchanged...
  "title": "Getting Started with Grimoire",
  "statusHistory": [
    { "status": "received",             "enteredAt": "2026-08-13T07:00:01Z", "detail": null },
    { "status": "converting",           "enteredAt": "2026-08-13T07:00:02Z", "detail": null },
    { "status": "queued",               "enteredAt": "2026-08-13T07:00:05Z", "detail": null },
    { "status": "running",              "enteredAt": "2026-08-13T07:00:06Z", "detail": null },
    { "status": "liveness_interrupted", "enteredAt": "2026-08-13T07:01:06Z", "detail": "attempt 1; next retry in 10s" },
    { "status": "reactivated",          "enteredAt": "2026-08-13T07:01:16Z", "detail": "attempt 1" },
    { "status": "running",              "enteredAt": "2026-08-13T07:01:17Z", "detail": null },
    { "status": "completed",            "enteredAt": "2026-08-13T07:03:40Z", "detail": null }
  ],
  "source": { "kind": "file", "href": "/api/ingest-submissions/{taskId}/source/original", "available": true }
}
```

Rules:

- `statusHistory` is ordered by `seq` ascending; never empty for tasks created after this feature. A task with no recorded history renders an empty path in the frontend — no synthesized entry from current status.
- `status` values: the six board stages plus `liveness_interrupted` | `reactivated` | `restarted` (see [data-model.md §1](../data-model.md)).
- `source.kind` = `"url"` → `href` is the submitted absolute http/https URL. `source.kind` = `"file"` → `href` is the source endpoint below. `available: false` ⇒ `href` is `null`; frontend MUST render a non-link "unavailable" indicator (FR-002).
- `title` follows the fallback chain and is never null/empty (FR-003).

## New: `GET /api/ingest-submissions/{taskId}/source/original`

Read-only stream of the persisted original from `<DataDir>/raw/originals/`. The path is derived server-side from the validated `taskId` route value only — no path parameters are accepted.

| Status | Body | Condition |
| --- | --- | --- |
| 200 | File stream; `Content-Type` from manifest `OriginalContentType`; `Content-Disposition: inline` | Manifest and original file exist |
| 404 | error envelope | Unknown task, missing manifest, or missing original file |

## New: `POST /api/ingest-submissions/{taskId}/restart`

Manual restart of a finally-failed task (FR-010…FR-013). No request body.

| Status | Body | Condition |
| --- | --- | --- |
| 202 | `{ "taskId": "...", "status": "queued" }` | Task was `failed`, normalized source exists → history appended (`restarted`, `queued`), attempt counter reset, task re-queued at tail |
| 409 | error envelope, `code` one of `restart_task_not_failed`, `restart_already_in_progress`, `restart_source_missing` | Task not in `failed` status, already re-queued/running (incl. concurrent duplicate — CAS loser), or normalized source missing |
| 404 | error envelope | Unknown task id |

Concurrency guarantee (SC-008): for N concurrent restart calls on one failed task, exactly one returns 202; the rest return 409. Exactly one `restarted` history entry and one queue insertion result.

## Superseded: error response shape

Every failure row above originally described an ad-hoc body — `{ "message": ... }`,
`{ "reason": ... }`, or an empty one. Those shapes were replaced by the single error envelope
introduced in [024-api-error-presentation](../../024-api-error-presentation/contracts/api-error-envelope.md)
(ADR-026), which is the authority: `application/problem+json` carrying `status`, `title`, `detail`,
`code`, and `traceId`. The three restart declines, previously one undifferentiated `reason` string,
now carry distinct codes so a caller can tell them apart — noted inline above.

## Unchanged (explicitly)

`POST /api/ingest-submissions`, `GET /defaults`, `GET /{taskId}/task-record`, `POST /{taskId}/retrigger` (retains its queue-resume meaning — NOT a failed-task restart), `POST /api/ingest-queue/resume`.

# Contract: Task Record API

**Feature**: 006-hexagonal-arch-tasks-ui

## GET /api/ingest-submissions/{taskId}/task-record

Returns the task's markdown record, parsed for presentation.

### 200 OK (`application/json`)

```json
{
  "taskId": "ingest-98e24a102de24084a924ed327a292b77",
  "metadata": {
    "status": "running",
    "agent": "ingest",
    "startedAt": "2026-07-18T14:03:11.0000000Z",
    "completedAt": null,
    "sourceRef": "raw/sources/2026-07-18-ingest-98e24a10....md",
    "originalRef": "raw/originals/2026-07-18-ingest-98e24a10....html",
    "failureReason": null
  },
  "body": "## Stages\n\n- [x] accepted …"
}
```

- `body` is the raw markdown with the frontmatter block (`---` … `---`) removed.
- `metadata` fields map 1:1 to task-artifact-format v2 frontmatter; absent/`null`
  frontmatter values serialize as JSON `null`.

### 404 Not Found (`application/json`)

Returned when: no board projection exists for `taskId`, the record file does not exist,
or the frontmatter cannot be parsed (e.g. torn pre-atomic legacy file).

```json
{ "message": "Task record for 'x' is not available." }
```

Malformed records MUST NOT produce a 5xx.

### Invariants

- The existing `GET /api/ingest-submissions/{taskId}` (JSON detail) and
  `GET /api/ingest-submissions/board` contracts are byte-for-byte unchanged.
- Reads happen on the atomically-renamed file: a response contains either the complete
  previous or the complete new content, never a torn state.
- Every response emits the `task_record.served` log event and `hub.task_record.serve`
  span with `task_id` and `outcome` (`ok | missing | unparseable`).

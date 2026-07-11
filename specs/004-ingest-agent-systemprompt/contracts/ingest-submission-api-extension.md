# Contract: Ingest Submission API — 004 Extension

Extends the 003 contract (`specs/003-ingest-intake-webui/contracts/ingest-submission-api.md`).
All 003 request/response shapes remain valid; this feature only adds optional fields
and one new endpoint.

## POST /api/ingest-submissions (extended)

Two new **optional** fields, valid for both JSON (URL) and multipart (file)
submissions (multipart: additional form fields):

| Field | Type | Constraint | Absent means |
|-------|------|-----------|--------------|
| `userPrompt` | string | ≤ 8,000 characters after trim; empty/whitespace treated as absent | Default User Prompt is used |
| `convertSteps` | object `{ "<step>": bool }` | Keys must be registered steps applicable to the submitted `kind`; required steps cannot be `false` | All applicable steps enabled |

### Example (URL submission with custom prompt, conversion disabled)

```json
{
  "kind": "url",
  "url": "https://example.com/article",
  "userPrompt": "Focus on the security claims; ignore marketing content.",
  "convertSteps": { "markitdown": false }
}
```

### Response (202 Accepted) — extended

```json
{
  "taskId": "2026-07-11-ingest-example",
  "status": "received",
  "sourceKind": "url",
  "acceptedAt": "2026-07-11T08:00:00Z",
  "userPromptSource": "custom",
  "convertSteps": { "markitdown": false }
}
```

### New error cases (all before task creation)

| Status | Condition | Body `reason` example |
|--------|-----------|----------------------|
| `400 Bad Request` | `userPrompt` exceeds 8,000 chars | `user_prompt_too_long` |
| `400 Bad Request` | Unknown step name in `convertSteps` | `unknown_convert_step` |
| `400 Bad Request` | Step key not applicable to `kind` | `convert_step_not_applicable` |
| `422 Unprocessable Entity` | Required step disabled (e.g. `markitdown:false` with `kind: pdf_file`) | `convert_step_required` |

Error bodies follow 003's error shape and include a human-readable `message`.

## GET /api/ingest-submissions/defaults (new)

Single source of truth for rendering the submission form's prompt editor and step
toggles.

### Response (200 OK)

```json
{
  "defaultUserPrompt": "Please integrate the following source into the wiki. ...",
  "userPromptMaxLength": 8000,
  "convertSteps": [
    {
      "name": "markitdown",
      "appliesTo": ["url", "pdf_file", "office_file"],
      "requiredFor": ["pdf_file", "office_file"],
      "defaultEnabled": true
    }
  ]
}
```

- `defaultUserPrompt` is the verbatim content of `agents/ingest/default-user-prompt.md`.
- `500` with human-readable reason if the default-prompt file is missing/empty
  (fail-closed; deterministic guarantee).

## GET /api/ingest-submissions/{taskId} (extended)

The task detail representation additionally exposes:

```json
{
  "userPromptSource": "custom",
  "userPrompt": "Focus on the security claims; ignore marketing content.",
  "convertSteps": { "markitdown": false },
  "runActivity": {
    "modelTurns": 3,
    "toolCalls": 5,
    "toolCallsByName": { "read_file": 3, "write_file": 2 },
    "currentAction": "tool_call:write_file",
    "lastEventAt": "2026-07-11T09:00:00Z"
  }
}
```

`runActivity` is non-null only while the task is `running` (last received `activity`
snapshot). Tasks created before this feature return `userPromptSource: null`,
`userPrompt: null`, `convertSteps: null` — clients render these as
"defaults of their time".

## GET /api/ingest-submissions (extended)

Board entries for `queued` tasks additionally expose their FIFO position and the
queue's paused state:

```json
{
  "tasks": [ { "taskId": "...", "status": "queued", "queuePosition": 2, "...": "..." } ],
  "queuePaused": true
}
```

`queuePaused` is `true` only after a Hub restart with queued tasks, until the user
resumes (FR-021).

## POST /api/ingest-queue/resume (new)

Resumes automatic queue processing after a Hub restart (whole queue).

- `200 OK` `{ "queuePaused": false, "queuedTasks": 3 }` — idempotent; also `200` when
  the queue was not paused.

## POST /api/ingest-submissions/{taskId}/retrigger (new)

Re-arms a single queued task after a Hub restart.

- `200 OK` — task keeps its FIFO position; processing resumes for it (and starts
  immediately if the agent slot is free and no earlier re-armed task waits).
- `404 Not Found` — unknown task.
- `409 Conflict` — task is not in `queued` state.

## Realtime lifecycle events (003 SignalR channel, extended)

The existing lifecycle stream additionally publishes run-activity updates while a task
is `running`:

```json
{ "kind": "run_activity", "taskId": "t-1",
  "modelTurns": 3, "toolCalls": 5, "currentAction": "tool_call:write_file" }
```

Kanban cards remain status-only (003 contract unchanged); the detail view consumes
`run_activity`.

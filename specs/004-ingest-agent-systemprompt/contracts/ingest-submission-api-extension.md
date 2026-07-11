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
  "convertSteps": { "markitdown": false }
}
```

Tasks created before this feature return `userPromptSource: null`,
`userPrompt: null`, `convertSteps: null` — clients render these as
"defaults of their time".

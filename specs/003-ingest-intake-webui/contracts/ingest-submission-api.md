# Contract: Ingest Submission API

HTTP contract between Web UI and Hub for ingest submission and board data bootstrap.

## POST /api/ingest-submissions

Accept one source submission and create Task Artifact immediately.

### Request

`multipart/form-data` for file submissions or `application/json` for URL submissions.

#### URL submission (JSON)

```json
{
  "kind": "url",
  "url": "https://example.com/article"
}
```

#### File submission (multipart)

Fields:
- `kind`: `markdown_file` | `pdf_file` | `office_file`
- `file`: binary payload

### Response (202 Accepted)

```json
{
  "taskId": "2026-07-06-ingest-example",
  "status": "received",
  "sourceKind": "url",
  "acceptedAt": "2026-07-06T10:41:22Z"
}
```

### Error responses

- `400 Bad Request`: invalid payload, missing required field
- `415 Unsupported Media Type`: unsupported file type
- `422 Unprocessable Entity`: accepted shape but conversion/fetch preconditions fail immediately

## GET /api/ingest-submissions

Returns current board projection for all tasks.

### Response (200 OK)

```json
{
  "tasks": [
    {
      "taskId": "2026-07-06-ingest-example",
      "status": "converting",
      "title": "example.com/article",
      "updatedAt": "2026-07-06T10:41:31Z",
      "failureReason": null,
      "taskLink": "/api/ingest-submissions/2026-07-06-ingest-example"
    }
  ]
}
```

## GET /api/ingest-submissions/{taskId}

Returns the full Task Artifact representation for details page/deep link.

### Response (200 OK)

```json
{
  "taskId": "2026-07-06-ingest-example",
  "status": "failed",
  "failureReason": "URL fetch timeout after 30s",
  "sourceRef": "raw/sources/2026-07-06-ingest-example.md",
  "originalRef": "raw/originals/2026-07-06-ingest-example.html"
}
```

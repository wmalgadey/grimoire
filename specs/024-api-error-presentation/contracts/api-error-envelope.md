# Contract: API Error Envelope

**Feature**: 024-api-error-presentation | **Status**: Draft alongside [plan.md](../plan.md)

Every Hub HTTP response that declines or fails a request carries this envelope. There are no
exceptions: deliberate rejections, validation failures, not-found, and unhandled exceptions all
produce it.

## Media type

`application/problem+json` (RFC 7807). The media type is itself part of the contract — the client
uses it to decide whether a body is a recognizable error structure before attempting to read it.

## Members

| Member | Type | Required | Meaning |
|---|---|---|---|
| `status` | integer | Yes | HTTP status code, repeated in the body so a stored/forwarded envelope stays self-describing. |
| `title` | string | Yes | Short headline for the failure class. Stable per `code`. Human-readable, no identifiers. |
| `detail` | string | Yes | The actionable sentence: what happened and, where the user can resolve it, what to do. Human-readable, no identifiers, no serialized structures. |
| `code` | string | Yes | Stable machine-readable failure identifier, `snake_case`. The contract for logs, tests, and telemetry. Never displayed as the primary message. |
| `traceId` | string | No | Correlation identifier of the request. Omitted entirely when no trace context exists — never emitted empty or as a placeholder. |

`type` and `instance` (RFC 7807's remaining members) are not used. Adding them later is additive and
does not break this contract.

### Example — a declined request

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json
```

```json
{
  "status": 409,
  "title": "Conversation is busy",
  "detail": "This conversation is still working on the previous question. Wait for it to finish, then ask again.",
  "code": "conversation_already_active",
  "traceId": "00-8f3a1c2b4d5e6f708192a3b4c5d6e7f8-1a2b3c4d5e6f7081-01"
}
```

### Example — a system fault

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/problem+json
```

```json
{
  "status": 500,
  "title": "Something went wrong",
  "detail": "The request could not be completed because of an internal error. This is not caused by your input — try again in a moment.",
  "code": "internal_error",
  "traceId": "00-8f3a1c2b4d5e6f708192a3b4c5d6e7f8-1a2b3c4d5e6f7081-01"
}
```

An unhandled exception never contributes its message, type, or stack trace to `detail`. The
exception's own text reaches the log, correlated by `traceId`; the response carries the generic
`internal_error` entry above.

## Code catalogue rules

1. Every `code` the Hub can emit exists in the catalogue with a non-empty `title` and default
   `detail`.
2. `code` values are stable. Renaming one is a breaking change to logs, tests, and telemetry, and is
   treated as such.
3. A call site may override `detail` with a more specific sentence (naming a task id, a limit, a
   field). It may not override `code`, `title`, or `status` — those belong to the catalogue entry.
4. An overridden `detail` is subject to the same rules as a catalogue default: readable prose, no
   identifiers, no serialized structures.

## Codes carried over from the current API

These identifiers already exist on the wire as `reason` values and are preserved verbatim as `code`,
because tests and operational tooling key on them:

| `code` | Status | Origin |
|---|---|---|
| `lint_run_active` | 409 | `LintSubmissionEndpoints` |
| `unresolved_remediation_tasks` | 409 | `LintSubmissionEndpoints` |
| `query_concurrency_limit_reached` | 503 | `QuerySubmissionEndpoints` |
| `conversation_already_active` | 409 | `QuerySubmissionEndpoints` |
| `conversation_record_unreadable` | 500 | `QuerySubmissionEndpoints` |
| `task_not_proposed` | 409 | `RemediationTaskEndpoints` |
| `task_not_authorized` | 409 | `RemediationTaskEndpoints` |
| `execution_already_started` | 409 | `RemediationTaskEndpoints` |
| `message_turn_active` | 409 | `RemediationTaskEndpoints` |

Endpoints that today return only `{ message }` with no identifier gain a `code` as part of this
feature; the catalogue is the record of which.

## Client contract

A client receiving a failed response:

1. Classifies by what happened to the request — no response, 4xx, 5xx, or unusable response — never
   by whether the body parsed (see [research.md](../research.md) R4).
2. Reads `title` and `detail` when the media type is `application/problem+json` and the body parses.
3. Falls back to a generic readable message for its category otherwise, and retains a bounded
   excerpt of what was received for the technical detail.
4. Never displays `code`, `status`, `traceId`, or raw body content as the primary message.

## What this contract does not cover

- Recorded agent-run failure text (`failureReason`, `outcomeReason`, status-history `detail`). Those
  are persisted fields with their own established shape; this feature changes how they are displayed,
  not what they contain. See [research.md](../research.md) R6.
- The live-connection (SignalR) channel, which reports connection state rather than request
  failures.
- The command-line surface.

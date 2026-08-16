# Quickstart: Validating Readable API Error Presentation

**Feature**: 024-api-error-presentation | **Spec**: [spec.md](./spec.md)

How to prove this feature works end to end, by hand and by suite. Contract details live in
[contracts/api-error-envelope.md](./contracts/api-error-envelope.md); shapes live in
[data-model.md](./data-model.md). This file does not repeat them.

## Prerequisites

- .NET 10 SDK, and `markitdown` on `PATH` (the ingest conversion adapter).
- Bun 1.3.14 for the frontend.
- No LLM provider credential is required for anything on this page. Every check here is
  deterministic; the feature has no agent-judgment surface.

## Automated verification

```bash
# Backend — architecture, then the error-contract integration tests
dotnet test backend/tests/Grimoire.ArchTests
dotnet test backend/tests/Grimoire.IntegrationTests

# Frontend — the presentation module and every surface that renders an error
cd frontend && bun run test
```

Expected: green. The integration suite asserts the envelope on real HTTP responses from a real Hub;
the frontend suite asserts the four categories, the primary/technical split, and the retry
affordance against real component renders.

## Manual verification

Start the Hub and the frontend:

```bash
dotnet run --project backend/src/Grimoire.Hub          # listens on :5255 (http launch profile)
cd frontend && bun run dev                              # proxies /api and /hubs to :5255
```

### 1. A declined request reads as a sentence (FR-002, FR-005 — User Story 1)

Ask a question on the query page, then — while that conversation is still working — ask another one
in the same conversation.

- **Expect**: a sentence explaining the conversation is still busy and to wait.
- **Expect not**: `conversation_already_active`, `409`, braces, or field names anywhere in the
  primary message.
- **Before this feature**: the raw identifier `conversation_already_active` was displayed verbatim.

Confirm the same on the wire:

```bash
# Submit a turn, then submit a second one to the same conversation while the first is running.
CID=manual-check
curl -s -X POST "http://localhost:5255/api/query-conversations/$CID/turns" \
  -H 'Content-Type: application/json' -d '{"prompt":"anything"}'
curl -i -X POST "http://localhost:5255/api/query-conversations/$CID/turns" \
  -H 'Content-Type: application/json' -d '{"prompt":"another"}'
```

- **Expect**: `Content-Type: application/problem+json`, and a body carrying `status`, `title`,
  `detail`, `code`, `traceId`.
- **Note**: the route is `/api/query-conversations/{conversationId}/turns` and the body field is
  `prompt`. There is no `/api/query-submissions` endpoint and no `question` field — a request
  spelled that way returns a bare framework 404, which is not the envelope and proves nothing.

### 2. The technical facts are one step away (FR-006 — User Story 2)

With the error from step 1 on screen, open its technical detail.

- **Expect**: the status, the `code`, and the `traceId` — the same `traceId` the Hub logged for that
  request, so the two can be joined.
- **Expect**: none of those visible before opening it.

### 3. Unreachable is not the same as declined (FR-007, FR-008 — User Story 4)

`unreachable` means the request never completed. Take the **browser** offline (DevTools →
Network → Offline) and submit anything.

- **Expect**: a connectivity message naming the system as unreachable, plus a retry control.
- **Expect not**: wording that implies the request was refused or that the input was wrong.

Go back online and press retry.

- **Expect**: the request goes through and the error clears (FR-011).

Stopping the Hub while the frontend keeps running is a *different* case, and deliberately so:
`bun run dev` proxies `/api` to the Hub, so a stopped Hub makes the proxy answer **502** — a
real HTTP response. That is a fault, not an unreachable host, and it is presented as one (with
a retry, per step 4). Any deployment behind a reverse proxy behaves the same way. Use the
offline toggle above to exercise `unreachable`; use a stopped Hub to exercise the 502 fault.

### 4. A fault stays retryable even when its body is noise (User Story 4, scenario 5)

Point the frontend at something that answers HTTP but is not the Hub:

```bash
VITE_HUB_ORIGIN=http://localhost:9999 bun run dev   # with any non-Hub server on :9999
```

- **Expect**: a 5xx from that server is presented as a system fault with a retry control, and the
  bounded excerpt of what was actually received appears in the technical detail.
- **Expect not**: the raw HTML body as the primary message.

### 5. A recorded provider failure reads as prose (FR-012 — User Story 5)

Open a task that failed with a rejected model request (or any task whose `failureReason` starts with
`Model API error`).

- **Expect**: the provider's own sentence as the primary message.
- **Expect**: `Model API error 400 (invalid_request_error)` demoted into the technical detail.
- **Expect**: for a long provider message, a shortened primary message with the full text recoverable
  from the technical detail (FR-014).

### 6. Nothing regressed to raw output (FR-010, SC-005 — User Story 5)

Walk the surfaces that can fail — ingest submission, question submission, lint trigger, remediation
action, task restart, task and board loading — and trigger a failure on each.

- **Expect**: the same region, the same disclosure, the same category treatment everywhere.
- **Expect not**: any surface still rendering a bare status line or a machine code.

### 7. Redaction survives the new display paths (FR-015, SC-008)

Confirm a failure whose upstream text contained a credential-shaped string still shows `[REDACTED]`
in both the primary message and the technical detail. The recording path already redacts; this step
verifies no display path added by this feature reintroduces the raw value.

## Accessibility spot-check (FR-009 — User Story 3)

With a screen reader active, trigger any failure while focus sits in a form field.

- **Expect**: the error is announced.
- **Expect**: focus stays where it was.

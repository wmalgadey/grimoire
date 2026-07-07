# Quickstart: Ingest Submission Web UI

Validates ingest submission, conversion/persistence, auto-trigger, and realtime board visibility end-to-end.

## Prerequisites

- .NET 10 SDK
- Repository root at `/workspaces/grimoire`
- `.env` present with required runtime values for hub/agent dispatch
- Content root initialized (`wiki/` by default) with tasks/log paths available
- Frontend dependencies installed (`cd frontend && npm install`)
- The `markitdown` CLI installed and resolvable on `PATH` (or configure
  `MarkItDown:ExecutablePath` / `MarkItDown:TimeoutSeconds` in Hub configuration) —
  see [MarkItDown](https://github.com/microsoft/markitdown)

## Build Backend

```bash
cd /workspaces/grimoire/backend
dotnet build
```

## Run Hub

```bash
cd /workspaces/grimoire/backend
dotnet run --project src/Grimoire.Hub
```

Expected outcome:
- Hub starts without startup reconciliation errors.
- Ingest-submission endpoints and ingest-lifecycle stream endpoint are available (see contracts).

## Scenario 1: Submit URL and observe lifecycle

1. Open the intake UI.
2. Submit one reachable URL.
3. Confirm immediate acceptance message.
4. Observe board card progression:
   - `received`
   - `converting`
   - `queued`
   - `running`
   - `completed` or `failed`

Expected outcome:
- Hub fetches URL immediately.
- Original artifact is persisted (`raw/originals/...`).
- Normalized markdown is persisted (`raw/sources/...`).
- Ingest trigger uses normalized markdown artifact.

## Scenario 2: Submit unsupported file type

1. Submit a file with unsupported extension.

Expected outcome:
- Submission rejected with actionable validation message.
- No task created.
- No artifacts persisted.

## Scenario 3: Conversion failure path

1. Submit corrupted PDF or unreachable URL fixture.

Expected outcome:
- Task transitions to `failed`.
- Card shows `failure_reason`.
- No partial normalized markdown artifact remains.

## Scenario 4: Queue serialization behavior

1. Submit two valid sources quickly while one ingest run is active.

Expected outcome:
- Both submissions are accepted.
- Second task remains `queued` until first run reaches terminal state.
- Second task auto-triggers without new user action.

## Observability Verification

With local OTel backend configured per ADR-005, verify:
- Metric increments for submission/fetch/conversion/publish/trigger.
- Structured logs include mandatory fields for each declared event.
- Distributed trace chain includes `hub.ingest_submission.submit` and child spans.

## Contract References

- [Ingest Submission API](./contracts/ingest-submission-api.md)
- [Ingest Lifecycle Events](./contracts/ingest-lifecycle-events.md)
- [Source Artifact Reference](./contracts/source-artifact-reference.md)

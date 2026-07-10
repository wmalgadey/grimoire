# Quickstart: Ingest Submission Web UI

Validates ingest submission, conversion/persistence, auto-trigger, and realtime board visibility end-to-end.

## Prerequisites

- .NET 10 SDK
- Node.js 20+ (frontend tooling requires it; `nvm use 20` or later if your default is older)
- Docker running (only needed for the Observability Verification section below — the local
  OTel backend, ADR-005)
- Repository root at `/workspaces/grimoire`
- `.env` present with required runtime values for hub/agent dispatch
- Content root initialized (`wiki/` by default) with tasks/log paths available
- Frontend dependencies installed (`cd frontend && npm install`)
- The `markitdown` CLI installed and resolvable on `PATH`:

  ```bash
  pipx install 'markitdown[all]'
  # or: pip install 'markitdown[all]'
  ```

  Alternatively, configure `MarkItDown:ExecutablePath` / `MarkItDown:TimeoutSeconds` in Hub
  configuration to point at an existing install — see
  [MarkItDown](https://github.com/microsoft/markitdown)

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
- By default the Hub listens at `http://localhost:5255` (`Properties/launchSettings.json`).

## Run Frontend

```bash
cd /workspaces/grimoire/frontend
npm run dev -- --open
```

Expected outcome:

- SvelteKit dev server starts (default `http://localhost:5173`) and opens the submission UI.
- `/api/*` and `/hubs/*` requests are proxied to the Hub (`frontend/vite.config.ts`); set
  `VITE_HUB_ORIGIN` if the Hub isn't at the default `http://localhost:5255`.
- The submission form and the Kanban board are both on `/`; `/board` redirects there.

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

Start the local OTel backend (.NET Aspire Dashboard, ADR-005) before running the Hub, so the
Hub's `AddOtlpExporter()` calls have a receiver to send to:

```bash
# Terminal 1 — OTel viewer
docker run --rm -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard
```

Then open `http://localhost:18888` and, after running Scenario 1, verify:

- Metric increments for submission/fetch/conversion/publish/trigger.
- Structured logs include mandatory fields for each declared event.
- A single distributed trace chain includes `hub.ingest_submission.submit`, its Hub-side child
  spans, `hub.ingest_run.trigger`, and — as its child, via propagated `TRACEPARENT` — the
  triggered `ingest_agent.run` span (not a second, disconnected trace).

## Contract References

- [Ingest Submission API](./contracts/ingest-submission-api.md)
- [Ingest Lifecycle Events](./contracts/ingest-lifecycle-events.md)
- [Source Artifact Reference](./contracts/source-artifact-reference.md)

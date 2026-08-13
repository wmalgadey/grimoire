# Quickstart Validation: Task Visibility & Recovery Improvements

**Feature**: `023-task-ui-improvements`

Runnable scenarios proving the feature end-to-end. Contracts: [contracts/http-api.md](./contracts/http-api.md), [contracts/signalr-events.md](./contracts/signalr-events.md); shapes: [data-model.md](./data-model.md).

## Prerequisites

- Hub running with a valid `appsettings.json` (three roots + MemoryDir per ADR-022/024) and frontend dev server (`.vscode` launch config or `dotnet run` in `backend/src/Grimoire.Hub` + `npm run dev` in `frontend/`).
- An ingest agent build reachable per ADR-022 (for the recovery scenarios a real or deliberately-hung agent process).

## Automated verification (primary)

```bash
# Backend: all feature tests are in the Integration tier
dotnet test backend/tests/Grimoire.IntegrationTests

# Fast tier still green (arch tests unchanged — no new Boundary Rule)
scripts/test-fast.sh

# Frontend component/route tests
cd frontend && npm test
```

Expected: green, including the new history/reactivation/restart/source/title tests and the observability contract tests (in-memory exporters over production wiring).

## Scenario 1 — Label & source link (US3, US4 / SC-001..003)

1. Submit a markdown file whose content starts with `# Getting Started` via the UI (or `POST /api/ingest-submissions` multipart).
2. Board card shows **"Getting Started"** (not the task id); no status badge on the card (US6/SC-006), column header still names the stage.
3. Open the task detail: heading is the title, task id shown secondarily (FR-004). The Source row is a link; clicking it opens the original file content in the browser (served by `GET …/source/original`).
4. Submit a URL task: detail Source row links directly to that URL.
5. Delete the stored original under `<DataDir>/raw/originals/` for the file task, reload detail: "source unavailable" indicator, no broken link (SC-002).

## Scenario 2 — Status history "path" (US1 / SC-004)

1. Open the detail of the completed task from Scenario 1.
2. `statusHistory` renders as an ordered path `received → converting → queued → running → completed` with timestamps; current/last entry highlighted.
3. For any pre-feature task: detail still renders (single-entry fallback from current status).

## Scenario 3 — Liveness interruption, reactivation, exhaustion (US2 / SC-005)

1. Submit a task against an agent rigged to go silent (no NDJSON events) after start.
2. After the liveness window: task does NOT fail; history gains `liveness_interrupted` (attempt 1); log `ingest.run.liveness_interrupted` (WARN) emitted; board card stays in `running`.
3. After backoff, history gains `reactivated` + `running`; metric `wiki.ingest.reactivations_total{outcome="attempted"}` increments.
4. Remaining attempts exhaust with increasing delays (10s/30s/90s defaults) → history ends `…→ failed`; `ingest.run.reactivation_exhausted` + existing `ingest.run.liveness_failed` emitted; queue advances to the next task.
5. The whole sequence is visible as the "path" in the task detail — the stopping point is identifiable (US1 goal).

## Scenario 4 — Manual restart (US5 / SC-007, SC-008)

1. On the finally-failed task from Scenario 3, the detail view shows a **Restart** button (absent on non-failed tasks — FR-011).
2. Click Restart: 202; history appends `restarted → queued`; prior failure entries remain (FR-013); task re-runs through the normal lifecycle under the same id.
3. Race check (automated test, or two browser tabs): concurrent restarts → exactly one 202, others 409, exactly one `restarted` entry (SC-008).
4. `POST …/restart` on a running task → 409 with reason.

## Scenario 5 — Observability contracts (Principle IV)

With the Aspire Dashboard attached (ADR-005): the Scenario 3/4 flows show spans `ingest_hub.reactivation`, `hub.ingest_task.restart`, `hub.ingest_source.serve` (request-span parented for the endpoints), correlated by `task_id` with the log events and metrics from `plan.md ## Observability`. CI equivalents: the deterministic contract tests included in the automated run above.

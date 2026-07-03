# Quickstart: Ingest Minimal

Validates that a submitted source produces a wiki page and a task artifact end-to-end
(User Story 1 & 2), and that failures are handled safely (User Story 3).

## Prerequisites

- .NET 10 SDK installed
- A local secrets file with `ANTHROPIC_API_KEY` set (git-ignored; see ADR-004) — the Hub
  reads this and injects it only into the Ingest agent's child process
- Repository checked out with an initialized `wiki/`, `tasks/`, and `log.md` (empty is
  fine for a first run)

## Setup

```bash
cd backend
dotnet build
```

## Scenario 1 — Happy path: new topic

```bash
dotnet run --project src/Grimoire.Hub -- \
  submit-source --path ../raw/sources/example-article.md
```

**Expected outcome**:
- A file appears under `tasks/` with `status: completed` and a non-empty `pages_touched`.
- A new file appears under `wiki/` whose content reflects `example-article.md`.
- `index.md` has a new entry under some category, linking to the new wiki page.
- `log.md` has a new `## [<date>] ingest | ...` line.
- The original `raw/sources/example-article.md` is byte-for-byte unchanged (FR-009).

## Scenario 2 — Happy path: existing topic updated

Submit a second source covering the same topic as Scenario 1's wiki page.

**Expected outcome**: the existing wiki page from Scenario 1 is updated (no duplicate
page created); its task artifact's `pages_touched` references the same page path as
before.

## Scenario 3 — Failure: unreadable/empty source

```bash
dotnet run --project src/Grimoire.Hub -- \
  submit-source --path ../raw/sources/empty.md   # zero-byte file
```

**Expected outcome**:
- A task artifact appears with `status: failed` and a human-readable `failure_reason`.
- No new or modified files appear under `wiki/` or `index.md` (FR-008).
- `log.md` still gets a new entry recording the failed attempt (FR-015).

## Scenario 4 — Restart reconciliation

1. Start an ingest, then kill the Hub process mid-run (before the task artifact reaches a
   terminal status).
2. Restart the Hub.

**Expected outcome**: on startup, the task artifact that was left `running` is updated to
`status: failed` with a `failure_reason` noting the interruption (FR-013), and a
`ingest.task.reconciled` log event is emitted (see `plan.md ## Observability`).

## Observability check

With the .NET Aspire Dashboard running locally (ADR-005), re-run Scenario 1 and confirm:
- A `hub.ingest.submit` trace with child spans down to `ingest_agent.write_wiki_page`.
- `wiki.ingest.operations_total{outcome=completed}` incremented by 1.
- An `ingest.page.written` structured log event with the correct `page_path`.

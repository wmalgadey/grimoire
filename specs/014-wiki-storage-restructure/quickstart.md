# Quickstart: Wiki Storage Layout & Shared Log/Catalog Format

Validation scenarios proving the feature end-to-end. Contract:
`contracts/log-and-catalog-entry-format.md`. Entity/path shapes: `data-model.md`.

## Prerequisites

- .NET 10 SDK.
- A fresh (or freshly reset) `<base>` directory — no prior content-root layout to
  migrate (FR-006; there is nothing to preserve).
- `ANTHROPIC_API_KEY` for live/eval runs (hermetic tests need none).

## Run the test suites (hermetic, primary verification)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests           # path/policy structural rules, Red/Green probed
dotnet test tests/Grimoire.Domain.UnitTests    # PolicyLoader "." prefix normalization
dotnet test tests/Grimoire.IntegrationTests    # path resolution, log/catalog format, backstop
```

Expected: all pass; no test requires an API key or network.

## Scenario 1 — Flat content root, no wrapper folder (US1/SC-001)

1. Start the Hub against a fresh `<base>` directory.
2. Trigger an ingest that creates a new article in a topical category.
3. List `<base>/wiki`'s top-level entries.

Expected: the article is at `<base>/wiki/<category>/<article>.md` — no `pages/`
segment; the top-level listing shows only `index.md`, `log.md`, and topical category
folders.

## Scenario 2 — Tasks and conversations sit beside the wiki (US2/SC-002)

1. Trigger a task (an ingest submission) and a query conversation against the same
   `<base>` directory.
2. Inspect the directory tree.

Expected: the task artifact is under `<base>/tasks/`; the conversation record is under
`<base>/conversations/`; neither is nested inside `<base>/wiki/` or `<base>/data/`;
`<base>/data/` (raw intake, operational state, agent instructions/policy) is otherwise
unchanged from before this feature.

## Scenario 3 — Every agent's log entry has the same shape (US3/SC-003, SC-004)

1. Trigger an ingest run and a query turn against the same wiki, each producing a
   `log.md` entry.
2. Force one agent-authored entry to be skipped (e.g. interrupt the run before its own
   `write_file` call) so the backstop fires.
3. Search `log.md` for `^## \[\d{4}-\d{2}-\d{2}\] `.

Expected: all three entries (ingest, query, backstop) match the same
`[DATE] TYPE | SUMMARY` heading-plus-paragraph shape from
`contracts/log-and-catalog-entry-format.md`; the search finds every entry, none is
missing a heading.

## Scenario 4 — Catalog entries match the reference format (US4/SC-006)

1. Trigger an ingest that creates a new article and adds its `index.md` catalog entry.
2. Inspect the new line in `index.md`.

Expected: `[link](path) — <description> — <source-status marker>`, description in the
wiki's configured content language, matching `contracts/log-and-catalog-entry-format.md`.

## Structural guarantee — guardrail boundary unaffected by the layout change

```bash
cd backend
dotnet test tests/Grimoire.ArchTests --filter FullyQualifiedName~GuardedWriteBoundary
```

Expected: passes unchanged — the guarded-tool boundary rule from ADR-006 still holds;
only `policy.json`'s `pathPrefix` values changed (R3/R4 in `research.md`), not the
enforcement mechanism itself.

## Observability check

With the Aspire Dashboard running (ADR-005): `wiki.log.backstop_appended_total`
increments by `type` label (`ingest`/`query`/`lint`) when Scenario 3's forced-skip
backstop fires; the log stream carries `wiki.log.backstop_appended` correlated by
`run_id`/`task_id`.

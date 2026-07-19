# Quickstart Validation: 006-hexagonal-arch-tasks-ui

Runnable checks that prove the feature end-to-end. Contracts: see
[contracts/](contracts/); read model: [data-model.md](data-model.md).

## Prerequisites

- .NET SDK (repo-pinned version), Node 22+, `markitdown` CLI on PATH (existing setup).
- No API key needed for any validation below (hermetic).

## 1. Structural conformance (User Story 1)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests
```

Expected: all rules pass, including the new port/containment rules (C1–C5 in
[contracts/ports-and-adapters.md](contracts/ports-and-adapters.md)).

Red/Green probe replay (proves a rule is live; do not commit):

```bash
# add a temporary class in Grimoire.Hub/IngestSubmission that news up MarkItDownConverter
dotnet test tests/Grimoire.ArchTests   # expect FAIL (C5)
# delete the class
dotnet test tests/Grimoire.ArchTests   # expect PASS
```

## 2. Zero regression + hermeticity

```bash
cd backend && dotnet test        # full suite: arch + unit + integration, no network/API key
cd frontend && npm test          # component tests
```

Expected: everything green with no live LLM/network access (run offline to prove it).

## 3. Task record view (User Story 2)

```bash
cd backend && dotnet run --project src/Grimoire.Hub     # terminal 1
cd frontend && npm run dev                              # terminal 2
```

- Open the board, submit a source (or use an existing task), click **Details** on a card.
- Expected: route `/tasks/<taskId>` shows a metadata header (status, timestamps, refs)
  and the record body rendered as formatted markdown — no raw JSON, no raw `---` block.
- `curl localhost:<hub-port>/api/ingest-submissions/<taskId>/task-record` still returns
  the JSON contract; `curl .../api/ingest-submissions/<taskId>` is unchanged.
- Delete/rename the record file → detail view shows the "record unavailable" placeholder.

## 4. Live update (User Story 3)

With a detail view open:

```bash
echo "manual probe line" >> <data>/wiki/tasks/<taskId>.md   # or run a real ingest
```

- Expected: the rendered view reflects the change within 5 s without reload.
- Stop the Hub, restart it: the view's connection indicator degrades, then recovers and
  the content resynchronizes after reconnect.

## 5. Observability

Run the Hub with the Aspire dashboard (ADR-005) and open a detail view:

- Log events `task_record.served` / `task_record.change_published` appear with `task_id`.
- Span `hub.task_record.serve` per API read; `hub.task_record.publish_change` per event.
- Counters `hub.task_record_reads_total`, `hub.task_record_change_events_total` increase.

CI equivalents are asserted by the in-memory-exporter integration tests (final phase
tasks).

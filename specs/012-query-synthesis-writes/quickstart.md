# Quickstart: Query Agent Synthesis Writes

Validation scenarios proving the feature end-to-end. Contracts:
`contracts/query-write-scope-and-coordination.md`. Entity shapes: `data-model.md`.

## Prerequisites

- .NET 10 SDK.
- A base directory with a wiki, Ingest and Query agent instructions/policies
  present per features 002/008, rebuilt with this feature's policy/registry
  changes.
- `ANTHROPIC_API_KEY` for live/eval runs (hermetic tests need none).

## Run the test suites (hermetic, primary verification)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests           # rewritten write-boundary rule + Coordination containment, both with Red/Green probes
dotnet test tests/Grimoire.IntegrationTests    # policy mode, guard/lock/CAS, multi-process concurrency, logging/trace contracts
```

Expected: all pass; no test requires an API key or network (the multi-process test
spawns local test-harness processes only).

## Scenario 1 — A good answer becomes a wiki page (US1/SC-001, SC-002)

1. Seed a wiki whose pages jointly imply an insight none states alone (e.g. two
   pages on related decisions with no cross-reference).
2. Start the Hub and ask the question that elicits the connection via `/query`.
3. Inspect the answer, the new page under `pages/`, `index.md`, `log.md`, and the
   conversation's record (`data/conversations/<conversationId>.md`).

Expected: the answer names the created page; the page carries standard
frontmatter including a synthesis marker, confidence + reason, review date, and
at least one link to a source page; `index.md`/`log.md` gained a corresponding
entry; the turn's `created_pages:` bookkeeping lists the new page's path.

## Scenario 2 — Writes stay guarded and scoped (US2/SC-001)

1. Ask the agent to "fix that typo" on an existing page, or otherwise attempt to
   provoke a content edit.
2. Inspect the Denied Actions in the turn's record.

Expected: the answer declines and explains; no existing page's content changed;
if a write was attempted, the record shows a denial with reason
`create_only_target_exists` (attempted overwrite of an existing page) or
`out_of_scope` (attempted write outside `pages/`/`index.md`/`log.md`).

## Scenario 3 — Concurrent writers don't corrupt the wiki (US3/SC-003, SC-004)

1. Trigger an Ingest run and, concurrently, two Query turns that each produce a
   synthesis, all against the same wiki.
2. After all finish, inspect `index.md`/`log.md` and every created page.

Expected: `index.md` and `log.md` contain every writer's entry — none lost or
overwritten; each new page is complete (no partial/truncated content); answer
streaming for the Query turns was not noticeably delayed. If genuine same-instant
contention on `index.md`/`log.md` occurred, the losing writer's turn record shows
a `write_conflict_stale_read` denial and the agent's retry (visible as an
additional `read_file`/`write_file` pair in that turn's tool-call count) still
lands the entry.

## Scenario 4 — Structural guarantee (SC-004)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests --filter FullyQualifiedName~GuardedWriteBoundary
dotnet test tests/Grimoire.ArchTests --filter FullyQualifiedName~Coordination
```

Expected: both pass; the Red/Green probe commits recorded during implementation
document the rule going red (naming the scratch violation) and back to green.

## Observability check

With the Aspire Dashboard running (ADR-005): a synthesis-creating turn shows a
`guardrails.acquire_write_lock` span under the run's `query_agent.tool_call`
span; the log stream carries `wiki.query.synthesis_page_created` and, under
induced contention, `wiki.write_conflict.rejected`; the
`wiki.query.synthesis_pages_created_total` counter increments once per created
page.

# Quickstart: Conversation Records Replace Query-Run Artifacts

Validation scenarios proving the feature end-to-end. Contracts:
`contracts/conversation-record-format.md` (file format),
`contracts/query-conversation-api.md` (revised submission API). Entity shapes:
`data-model.md`.

## Prerequisites

- .NET 10 SDK; Node 20+ (frontend only needed for scenario 4).
- A base directory with a wiki and Query agent instructions, e.g. the repo
  checkout itself (`agents/query/system-prompt.md`, `agents/query/policy.json`
  present per feature 008).
- `ANTHROPIC_API_KEY` available to the Hub's secrets mechanism for live runs
  (hermetic tests need none).

## Run the test suites (hermetic, primary verification)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests           # incl. retired-location tripwire ("query-runs" IL literal)
dotnet test tests/Grimoire.IntegrationTests    # record store/format, SC-001..SC-005, logging/trace contracts
cd ../frontend && npm test                     # submission payload = prompt only
```

Expected: all pass; no test requires an API key or network.

## Scenario 1 — Read a conversation as one document (US1/SC-001)

1. Start the Hub (`dotnet run --project backend/src/Grimoire.Hub -- --base-dir <base>`)
   and the frontend; open `/query`.
2. Hold a 3-turn conversation: an initial question, a follow-up referencing the
   first answer ("and how does that relate to the second point?"), and one more.
3. Open `<base>/data/conversations/<conversationId>.md` (the `conversationId` is
   visible in the submission response / network tab).

Expected: one file, frontmatter with `record_format: grimoire-conversation/1`,
then `## Turn 1..3` in order, each with `### Prompt`/`### Answer`, readable
top-to-bottom as a dialogue; each turn preceded by a `<!-- grimoire:turn ... -->`
bookkeeping block (state, timestamps, instruction sha256, model, turns_used).

## Scenario 2 — Bookkeeping and denials (US2/SC-002)

1. In a conversation, ask something that provokes an out-of-scope read (feature
   008's guarded-read fixtures) or interrupt an answer mid-stream.
2. Inspect the record.

Expected: the interrupted turn is marked `state: interrupted` with the partial
answer present; a denied tool action appears under that turn's
`denied_actions:` with `action`, `requested_target`, `canonical_target`,
`reason`, `turn`.

## Scenario 3 — Durability across Hub restart (US3/SC-003)

1. Finish two turns; while a third is streaming, kill the Hub process.
2. Restart the Hub, inspect the record.
3. Submit a follow-up in a **new** conversation (the old tab's conversation is
   ephemeral browser state — unchanged 008 behavior).

Expected: turns 1–2 complete in the record; turn 3 recorded per the existing
supervision rules with its partial answer and a terminal state; the record is
never truncated or rewritten. Bonus check (SC-005 hydration path): submitting a
follow-up to the *same* `conversationId` via curl after restart still yields
agent context matching the record (context hydrated from the file):

```bash
curl -s -X POST localhost:5000/api/query-conversations/<conversationId>/turns \
  -H 'content-type: application/json' -d '{"prompt":"and the second point?"}'
```

## Scenario 4 — Cutover (SC-004) and unchanged UX (FR-009)

1. Exercise several conversations (complete, interrupt, force a failure).
2. Check the retired location and the UI.

Expected: `<base>/data/query-runs/` gains **no** new files (delete it — it stays
gone); streaming, interruption, one-active-turn 409 and concurrency-limit 503
behave exactly as in feature 008; the browser request body contains only
`prompt` (no `priorTurns`).

## Scenario 5 — Fail-closed consistency (FR-006)

1. Corrupt a record file (e.g. truncate its frontmatter).
2. Submit a follow-up to that conversation via curl.

Expected: HTTP 500 `{ "reason": "conversation_record_unreadable" }`, no turn
created, `query.conversation.record_load_failed` logged; starting a new
conversation works normally.

## Observability check

With the Aspire Dashboard running (ADR-005): a completed turn shows
`hub.query.record_turn` under `hub.query.run_supervision`, a follow-up shows
`hub.query.load_conversation_context` under `hub.query.submit`, and the log
stream carries `query.conversation.turn_recorded` /
`query.conversation.context_loaded` correlated by `turn_id`/`conversation_id`.
The retired span `query_agent.finalize_artifact` no longer appears.

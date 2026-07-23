# Quickstart: Interactive Wiki Query Process

Validation guide for the `/query` capability. Assumes the Hub, `Grimoire.QueryAgent`
worker, and frontend are built per `plan.md`'s Project Structure. See
`data-model.md` and `contracts/` for exact shapes.

## Prerequisites

- A local Grimoire checkout with at least one wiki page under `wiki/pages/` (any
  existing content works; the "wiki does not cover this" scenario needs a question
  clearly outside that content).
- `data/.env` populated with the Anthropic API key (same secrets file Ingest already
  uses, ADR-004 — no new credential).
- `agents/query/system-prompt.md` and `agents/query/policy.json` present (fail-closed:
  removing either lets you exercise US1 Acceptance Scenario 5).
- Hub running (`dotnet run --project backend/src/Grimoire.Hub`) with the frontend dev
  server (`npm run dev` in `frontend/`).

## Scenario 1 — Ask a question the wiki answers, watch it stream (User Story 1, P1)

1. Open the query surface (`/query`) in the browser.
2. Submit a question the existing wiki content answers (e.g. reference a real page's
   topic).
3. **Expect**: answer text appears progressively (not all at once at the end); once
   complete, the turn is marked `completed` and the answer names the wiki page(s) it
   drew from.
4. Ask a question clearly outside any wiki content.
5. **Expect**: the answer plainly states the wiki has no material on it — no
   fabricated content.

Automated equivalent: `Grimoire.AgentEvals` sampled runs against SC-007/SC-008
thresholds (Test Strategy in plan.md); this manual pass is a smoke check, not the
statistical proof.

## Scenario 2 — Interrupt an answer mid-stream (User Story 2, P2)

1. Ask a question likely to produce a long answer.
2. While the answer is still streaming, click the stop control.
3. **Expect**: streaming halts within ~2s (SC-004), the partial text already shown
   stays visible, the turn is visibly marked `interrupted`, and the prompt input is
   immediately ready for a new question — no reload needed.
4. Submit a follow-up question immediately.
5. **Expect**: it is accepted right away (no waiting on the abandoned turn).
6. Try to interrupt a turn that has already completed.
7. **Expect**: nothing happens — no error, no state change.

## Scenario 3 — Follow-up question in context (User Story 3, P3)

1. Ask a question, let it complete.
2. Ask a follow-up using a pronoun/reference that only makes sense given the first
   answer (e.g. "How does that relate to Z?").
3. **Expect**: the answer correctly resolves the reference against the prior turn.
4. Start a new conversation.
5. **Expect**: the new conversation has no memory of the previous one.
6. While a turn is streaming, try to submit another prompt from the same conversation.
7. **Expect**: the UI blocks/queues this until the user interrupts or the turn
   completes (FR-008) — the input is visibly disabled or explained as "one turn at a
   time."

## Scenario 4 — Read-only guarantee (User Story 4, P4)

1. Ask a question that explicitly requests a wiki edit, e.g. "Please fix the typo on
   the X page for me."
2. **Expect**: no wiki file changes on disk (`git status` inside `wiki/` shows
   nothing); the answer explains that querying is read-only.
3. Inspect the turn's Query Run Artifact (`data/query-runs/<conversationId>/<turnId>.md`)
   and confirm `deniedActions` is empty (no write was even attempted, since no write
   tool exists — R3/ADR-011) or, if a denial-reason scenario was engineered
   (out-of-scope read), that it is recorded with a reason.

Automated equivalent: `Grimoire.ArchTests`' Red/Green-probed structural rule (FR-014)
proves no write API is reachable from `Grimoire.QueryAgent` at all — this manual pass
is a behavioral spot-check, not the structural proof.

## Scenario 5 — Concurrency and independence from Ingest (FR-017, SC-006)

1. Start an Ingest submission (any source).
2. While it is running, submit a Query Turn in a second browser window.
3. **Expect**: the Query Turn starts immediately — it does not wait for the Ingest run,
   and the Ingest run is not slowed by it.
4. Open `QueryConcurrencyLimit` (default 3) + 1 browser windows and submit a turn in
   each simultaneously.
5. **Expect**: the (limit + 1)th submission is rejected immediately with a clear "busy"
   message (`503`), not silently queued.

## Scenario 6 — Reconnect mid-stream (edge case)

1. Start a long-answering turn.
2. Simulate a dropped realtime connection (e.g. dev tools → offline, then online).
3. **Expect**: the connection-status indicator reflects the drop; already-rendered
   partial content stays visible; on reconnect, the UI shows the turn's current
   authoritative state (still streaming / completed / interrupted / failed) without a
   page reload — verifies the `GET /api/query-turns/{turnId}` refresh-then-resume rule.

## Verifying observability

- Open the local OTel viewer (ADR-005 Aspire Dashboard) and confirm, for one completed
  turn: a `hub.query.submit` root span with `hub.query.spawn_agent` and
  `hub.query_lifecycle.publish_update` children, correlated by `turn_id`; the
  `query_agent.run` trace from the agent process with `query_agent.model_turn` and
  `query_agent.finalize_artifact` children; and the `query.turns_total{outcome=completed}`
  counter incremented.

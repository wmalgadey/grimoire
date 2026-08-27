---
status: accepted
supersedes: ADR-011
---

# ADR-047: Query Realtime Delivery over a Dedicated SignalR Connection

## Context and Problem Statement

Query turn lifecycle transitions and streamed answer chunks (ADR-045) must reach the frontend
live. SignalR is the project's ratified realtime transport (ADR-001), and the ingest board
already has its own SignalR hub. The question is whether query realtime traffic shares the
ingest connection — one hub, messages discriminated by type — or gets a structurally separate
hub and browser connection of its own. This ADR restates the decision as current truth: query
realtime delivery runs over a dedicated SignalR hub, `QueryLifecycleHub`, on its own browser
connection, separate from the ingest board's.

## Decision Drivers

- Query activity must never block, nor be coupled to, ingest activity — the independence must
  be structurally visible in the realtime layer, not a convention inside a shared hub.
- Streamed answer chunks are high-frequency relative to ingest lifecycle events; mixing them
  onto one connection couples both features' realtime code paths and failure modes.
- The frontend already renders one connection-status indicator per connection; a per-surface
  hub reuses that pattern instead of inventing multiplexed status.
- No new transport or infrastructure: SignalR hubs are served by the existing Hub process
  (ADR-001).

## Considered Options

1. **A sibling SignalR hub per agent surface**: a dedicated `QueryLifecycleHub` and its own
   browser connection, structurally separate from the ingest hub.
2. One shared lifecycle hub carrying ingest and query messages, discriminated by message
   type, over a single browser connection.

## Decision Outcome

Chosen option: **Option 1 — a dedicated `QueryLifecycleHub` on its own connection**, because
it makes query/ingest realtime independence structural rather than conventional, at the
accepted cost of a second browser connection.

- **`QueryLifecycleHub`** (`Grimoire.Hub.Realtime`, route `/hubs/query-lifecycle`) is a
  broadcast-only SignalR hub (no server-invokable methods), a structural sibling of
  `IngestLifecycleHub` — the two share no message shapes and no connection.
- **`QueryLifecyclePublisher`** broadcasts two messages:
  - `queryAnswerChunk` — one streamed answer delta (`QueryAnswerChunkEvent`: `turnId`,
    `sequence`, `text`), forwarded in sequence order as the Hub receives each `answer_chunk`
    run event (ADR-045/ADR-046);
  - `queryTurnChanged` — one turn-state transition (`QueryTurnChangedEvent`: `eventId`,
    `turnId`, `fromState`, `toState`, `timestamp`, `failureReason`).
- **The browser holds one connection per surface**, each with its own
  `ConnectionStatusIndicator` — the established per-connection pattern. The sibling-hub shape
  has since been extended unchanged to the lint board (`LintLifecycleHub`), confirming it as
  the per-agent-surface convention rather than a query one-off.
- **Feature-Scoped Invariant:** the message names and payload field sets above are a
  published frontend contract (`specs/008-query-agent/contracts/query-conversation-api.md`),
  covered by classicist integration tests asserting the broadcast behavior; growing a
  payload is a reviewed contract amendment, not a broken structural test.

### Consequences

- Good, because query realtime traffic can never be coupled to ingest's: the independence
  requirement holds by construction, visible as two hubs with disjoint message vocabularies.
- Good, because each surface's connection health is individually visible and individually
  recoverable, reusing the existing per-connection indicator pattern.
- Good, because a new agent surface gets realtime delivery by adding a sibling hub — proven
  when the lint board arrived.
- Bad, because the browser holds a second (now third) SignalR connection instead of one;
  accepted deliberately so feature independence is structural rather than a message-routing
  convention inside a shared hub.
- Neutral, because broadcast-only hubs deliver every event to every connected client;
  per-client filtering is left until a real need arrives.

## Change Triggers

- **Extensions (do not invalidate this ADR):** new message types or additional payload fields
  on the query hub via the contract's amendment path; another agent surface adding its own
  sibling lifecycle hub (as lint did); new subscribers to the existing connection.
- **Invalidations (would require full supersession):** merging query realtime traffic onto a
  shared or ingest connection; delivering query lifecycle/answer streams through something
  other than a dedicated SignalR hub; turning the broadcast hub into a request/response
  surface.

## More Information

Supersedes [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md), whose
realtime-delivery aspect this ADR restates as current truth; ADR-011's other aspects are
re-decided in [ADR-044](ADR-044-shared-agent-runtime-library.md),
[ADR-045](ADR-045-token-level-answer-streaming.md), and
[ADR-046](ADR-046-query-dispatch-and-bounded-concurrency.md).

Read alongside: [ADR-001](ADR-001-backend-frontend-tech-stack.md) — SignalR as the project's
realtime transport, decided there and only applied here;
[ADR-045](ADR-045-token-level-answer-streaming.md) — where the streamed chunks originate;
[ADR-046](ADR-046-query-dispatch-and-bounded-concurrency.md) — the coordinator that feeds the
publisher its chunks and transitions. None of their decisions are restated or narrowed here.

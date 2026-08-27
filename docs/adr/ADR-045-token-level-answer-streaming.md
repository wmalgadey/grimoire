---
status: accepted
supersedes: ADR-011
superseded_by: []
reason: null
---

# ADR-045: Token-Level Answer Streaming over the Agent Event Channel

## Context and Problem Statement

The Query agent produces a prose answer whose value depends on latency: the caller must see
the first answer content within seconds of its production and watch the rest arrive as it is
generated, not receive one block when the run ends. The agent event channel (ADR-037) carries
discrete lifecycle and activity events, which cannot express an answer as it is being
produced. The question is how streamed answer content travels from the model, through the
agent process, to the Hub: as a new incremental event on the existing channel, or over a
second transport of its own. This ADR restates the decision as current truth: answer content
streams token-level as `answer_chunk` events on the same agent event channel every other run
event uses.

## Decision Drivers

- First answer content must be visible within 2 s (p95) of production, and subsequent content
  within 2 s (p95) — discrete lifecycle/activity events cannot satisfy this; only token-level
  deltas can.
- The event channel already exists, is hermetically testable, and needs no network surface;
  a second transport for streaming would reintroduce exactly the infrastructure and auth
  surface the channel decision avoided.
- Streaming must be opt-in per agent: an agent whose assistant text is not the product
  (Ingest) must be byte-for-byte unaffected.
- Principle II: hermetic harness tests must exercise streaming with a fake model client — the
  mechanism must not depend on a live provider connection.

## Considered Options

1. **A new `answer_chunk` event type on the existing NDJSON stdout event channel**, produced
   by a streaming path through the model-client port.
2. A second transport for streamed content (a direct connection from the agent process to the
   Hub, e.g. HTTP or SignalR), keeping the event channel for lifecycle only.

## Decision Outcome

Chosen option: **Option 1 — token-level `answer_chunk` events on the agent event channel**,
because one transport keeps the channel's hermetic-testability and no-new-infrastructure
properties intact, and a text delta is just one more event interleaved with the events the
Hub already reads.

- **Streaming path through the port.** `IModelClient.NextTurnAsync` accepts an optional
  `Action<string>? onTextDelta` callback. When a callback is supplied, `AnthropicModelClient`
  uses the provider's streaming API, invoking the callback as text deltas arrive, and still
  returns the same aggregated `ModelTurn` on completion — callers see identical turn results
  with or without streaming. The replay adapters (ADR-012) implement the same signature.
- **The loop forwards, the emitter emits.** `AgentLoop` forwards deltas to
  `RunEventEmitter.EmitAnswerChunk`, which writes
  `{"type":"answer_chunk","taskId":...,"timestamp":...,"text":"<delta>"}` interleaved with
  `heartbeat`/`activity` on the same NDJSON stdout stream (ADR-037). The loop inserts a
  separator between successive model turns' text so a multi-turn answer stays readable, and
  streamed deltas register as run progress for supervision purposes (ADR-038's authority).
- **Opt-in per agent.** An agent streams by supplying the callback; supplying none changes
  nothing. Query always supplies one; Ingest never does. The terminal `completed` event's
  `text` field reuses the same delta content shape for bounded non-streamed replies, so no
  second content field exists on the channel.
- **Feature-Scoped Invariant**: the `answer_chunk` event name and field set
  (`type`/`taskId`/`timestamp`/`text`) are a published channel contract
  (`specs/008-query-agent/contracts/query-run-events.md`), covered by classicist,
  deterministic integration tests asserting the emitted events — never by reflecting over the
  emitter's shape. Growing the event's field set is a recognized, reviewed contract amendment.

### Consequences

- Good, because the first token reaches the Hub as soon as the model produces it — the
  latency requirement is met by construction rather than by polling.
- Good, because streaming adds one event type to an existing channel: no new transport, no
  network/auth surface, and hermetic tests script the stream exactly as they script every
  other event.
- Good, because the aggregated `ModelTurn` return is unchanged, so the loop, replay
  recordings, and non-streaming agents are unaffected.
- Bad, because answer content now transits stdout alongside protocol events, so the channel's
  malformed-line tolerance matters more; accepted — the channel already carries that rule.
- Neutral, because token-level granularity is the provider's delta granularity, not a fixed
  chunk size; consumers must not assume chunk boundaries carry meaning.

## Change Triggers

- **Extensions (do not invalidate this ADR):** another agent supplying the delta callback and
  emitting `answer_chunk`; new consumers of the event; bounded reply text carried on the
  terminal event's existing `text` field; additional optional fields on the event via the
  contract's amendment path.
- **Invalidations (would require full supersession):** streaming answer content over a
  transport other than the agent event channel; abandoning token-level streaming in favor of
  delivering answers only as terminal-event payloads; moving delta production out of the
  `IModelClient` callback path so streaming bypasses the port.

## More Information

Supersedes [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md), whose
streaming aspect this ADR restates as current truth; ADR-011's other aspects are re-decided
in [ADR-044](ADR-044-shared-agent-runtime-library.md),
[ADR-046](ADR-046-query-dispatch-and-bounded-concurrency.md), and
[ADR-047](ADR-047-query-realtime-delivery.md).

Read alongside: [ADR-037](ADR-037-agent-event-channel-protocol.md) — the event channel this
event rides on; [ADR-038](ADR-038-heartbeat-run-supervision.md) — the supervision that
streamed progress feeds; [ADR-044](ADR-044-shared-agent-runtime-library.md) — the shared
runtime that hosts loop, port, and emitter; [ADR-012](ADR-012-eval-runner-recorded-replay.md)
— replay adapters implementing the streaming signature;
[ADR-047](ADR-047-query-realtime-delivery.md) — how streamed chunks reach the frontend. None
of their decisions are restated or narrowed here.

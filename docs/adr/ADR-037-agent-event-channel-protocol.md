---
status: accepted
supersedes: ADR-008
---

# ADR-037: Agent Event Channel Protocol — NDJSON Event Stream on stdout

## Context and Problem Statement

The Hub spawns agents as child processes (ADR-002's spawn model, carried forward by ADR-036)
and must learn what is happening inside a run — lifecycle transitions, liveness, loop
activity — without blocking on the process and without waiting for it to exit. How the agent
child process reports to the Hub during a run is a cross-cutting transport shape that every
agent (Ingest, Query, Lint) inherits, so it is fixed by ADR rather than per feature. This
ADR decides exactly that one aspect: the byte transport and event-envelope contract between
a running agent process and the Hub. Who acts on the events (run supervision) and how runs
are dispatched (queueing) are separate aspects, decided in ADR-038 and ADR-039.

## Decision Drivers

- The Hub must observe a run as it happens; the prior contract (an awaited exit code,
  ADR-002) reports nothing until the process ends and cannot distinguish a hang from work.
- Events must carry loop mechanics only — lifecycle, heartbeats, counters, summaries — never
  wiki-content judgment, which must not enter backend contracts (Constitution Principle V).
- No new infrastructure: no network surface, no auth, no broker (Principle IV).
- Hermetic testability: harness tests must be able to script the event stream directly and
  drive dispatch with a fake agent executable, without live LLM calls (Principle II).

## Considered Options

1. **Newline-delimited JSON (NDJSON) events written to the agent's stdout, read by the Hub
   as process parent**
2. HTTP callback endpoint on the Hub that the agent POSTs events to
3. SignalR client connection from the agent to the Hub
4. File-based event journal per run, tailed by the Hub

## Decision Outcome

Chosen option: **Option 1 — NDJSON on stdout**, because the parent↔child pipe already
exists, requires no network surface, no auth, and no broker, and is trivially scriptable in
hermetic tests.

- **Transport.** The agent writes one JSON event per line to stdout. The Hub, as process
  parent, reads the pipe and dispatches events. Human-readable agent logging goes to stderr,
  never stdout: stdout is exclusively the structured protocol surface.
- **Envelope and vocabulary.** Each event is a JSON object with a `type` discriminator plus
  event-specific fields. The founding vocabulary: `started`, `heartbeat` (fixed cadence,
  default every 10 s), `activity` (model-turn/tool-call counters and current action),
  `completed` (with summary), `failed` (with reason).
- **Vocabulary grows by extension.** The vocabulary is owned by this transport decision but
  is deliberately open: later ADRs have added `answer_chunk` (token streaming, ADR-011) and
  an optional `proposedActions` field on the Lint `completed` event (ADR-018) without any
  change to the transport or envelope — that is the intended model for future growth. The
  shared emitter lives in the agent runtime library (`RunEventEmitter`, ADR-011/ADR-013), so
  every agent speaks the same protocol.
- **Robustness.** Malformed lines are logged and skipped; they never fail the run. An
  accidental non-protocol write to stdout is therefore tolerated, but the structural
  mitigation is routing all agent logging to stderr.
- **Exit code.** The process exit code remains set by the agent for manual CLI invocation
  and diagnostics; it is not part of the Hub↔agent result contract. (What detects a failed
  run is ADR-038's aspect.)
- **Rule classification (Constitution Principle III).** This ADR introduces **no new
  Dependency & Layering Boundary Rule**. The protocol behaviors above — stdout/stderr split,
  envelope shape, malformed-line tolerance — are **Feature-Scoped Invariants**, covered by
  classicist, state-based integration tests that script real event streams and use fake
  agent executables (Principle II), never by reflecting over emitter internals.

### Consequences

- Good, because the parent↔child pipe needs no network surface, auth, or broker, and
  hermetic tests script the stream directly.
- Good, because one shared emitter and one open vocabulary let new agents and new event
  types arrive without renegotiating the transport.
- Bad, because stdout becomes a structured protocol surface an accidental write could
  corrupt; mitigated by stderr-only agent logging and malformed-line tolerance.
- Neutral, because the event vocabulary is transport-independent: a future remote or
  containerized agent can keep the same vocabulary over a different byte transport — that
  change would supersede this ADR's transport choice, not the vocabulary's semantics.

## Change Triggers

- **Extensions (do not invalidate this ADR):** new event types added to the vocabulary; new
  fields on existing events; a new agent type emitting the same protocol — exactly how
  `answer_chunk` (ADR-011) and `proposedActions` (ADR-018) arrived. No ADR action needed.
- **Invalidations (would require full supersession):** replacing stdout NDJSON with another
  byte transport (socket, HTTP, broker, file journal); moving to a bidirectional RPC model
  where the Hub sends commands into a running agent over the channel; making the exit code
  part of the result contract again.

## More Information

Reads alongside: ADR-036 (agent child-process spawn contract this channel rides on),
ADR-038 (run supervision — the consumer of `heartbeat` silence), ADR-039 (persistent run
queue — dispatch of the runs that emit these events), ADR-011 and its successors (streaming
`answer_chunk` over this channel), ADR-018 (`proposedActions` on the Lint terminal event).
Rejected options: an HTTP callback (2) or SignalR connection (3) introduce a network/auth
surface and couple the agent to Hub hosting for what is a parent↔child relationship; a
tailed file journal (4) buys only mid-run Hub-restart survival, which is moot because
restart reconciliation already fails interrupted runs (ADR-003), while adding
tailing/rotation complexity. Event schema details:
`specs/004-ingest-agent-systemprompt/contracts/agent-run-events.md`. This ADR replaces the
event-channel aspect of superseded ADR-008.

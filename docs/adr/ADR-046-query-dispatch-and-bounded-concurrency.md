---
status: accepted
supersedes: ADR-011
superseded_by: []
reason: null
---

# ADR-046: Query Dispatch — Bounded Concurrency, Immediate Rejection, and Interruption

## Context and Problem Statement

Ingest runs are dispatched through a persistent single-slot FIFO queue (ADR-039): one agent at
a time, further submissions wait. Query is the opposite interaction: a user asking a question
expects an answer now or an honest "busy" — a queued question answered minutes later is worth
less than a rejection — and query activity must never block, nor be blocked by, ingest
activity. Query also needs a second termination path no other dispatch shape had: the user can
interrupt an answer mid-stream, which is an intended outcome, not a failure. This ADR restates
the Hub-side query dispatch model as current truth: a bounded-concurrency coordinator that
rejects over-limit submissions immediately rather than queueing them, with user-triggered
interruption recorded as its own terminal outcome distinct from liveness failure.

## Decision Drivers

- Query activity must never wait on, or be waited on by, ingest activity — the two dispatch
  shapes must share no state.
- Concurrency is bounded (configurable, default 3); a submission beyond the limit is rejected
  immediately, never queued — the opposite dispatch shape from the ingest queue.
- User-triggered interruption must promptly stop the underlying agent run and be
  distinguishable in outcome (`interrupted`) from a liveness-detected crash or hang
  (`failed`).
- Whatever answer text already streamed before an interrupt or failure must survive into the
  durable record — partial answers are a product outcome, not debris.
- No new port: dispatch must reuse the existing `IAgentProcessLauncher`/`IAgentProcessHandle`
  port, so hermetic tests keep driving dispatch with a fake agent process.

## Considered Options

1. **A sibling coordinator with a counting semaphore, sized by a configurable limit,
   rejecting at capacity** — structurally independent of the ingest coordinator.
2. Reuse or extend the ingest single-slot FIFO queue for query submissions.
3. Bound concurrency but queue over-limit submissions instead of rejecting them.

## Decision Outcome

Chosen option: **Option 1 — `QueryRunCoordinator`, bounded and reject-over-limit**, because a
query is only worth answering promptly (options 2 and 3 trade the answer's timeliness for
admission, which is the wrong trade for an interactive question), and because a coordinator
that shares nothing with ingest dispatch makes the never-block-each-other requirement true by
construction.

- **`QueryRunCoordinator`** (`Grimoire.Hub.QueryDispatch`), sibling to `IngestRunCoordinator`
  (`Grimoire.Hub.IngestDispatch`): a counting semaphore sized by
  `QueryConcurrencyOptions.QueryConcurrencyLimit` (configuration key
  `Grimoire:QueryConcurrencyLimit`, default 3, bound at the single composition point per
  ADR-042's configuration rules). A submission at capacity is rejected immediately with the
  documented busy error (`query_concurrency_limit_reached`, HTTP 503) — there is no queue to
  wait in. The two coordinators share no state.
- **No persisted dispatch state.** Query runs are never queued and never survive a Hub
  restart as pending work; turn state is in-memory for the run's duration. The durable
  outcome of a turn is the Conversation Record append (ADR-014), not dispatch state.
- **Shared port, no new port.** Dispatch reuses `IAgentProcessLauncher`/`IAgentProcessHandle`
  (`Grimoire.Hub.AgentDispatch`) with a `QueryAgentRequest` flowing through `StartAsync`;
  hermetic tests drive it with the existing fake agent process.
- **Interruption is its own outcome.** `QueryRunCoordinator.InterruptAsync` terminates the
  agent process via `IAgentProcessHandle.Terminate()` and transitions the turn to
  `interrupted` immediately — the user asked for the stop, so there is nothing to wait to
  detect. Liveness-silence termination (authority: ADR-038) uses the same `Terminate()`
  mechanism but records `failed` with a liveness reason. The two paths are labeled by cause,
  not merged; only the first terminal transition wins, and interrupting an already-terminal
  turn is a no-op.
- **Partial answers survive.** The coordinator accumulates each run's streamed `answer_chunk`
  events (ADR-045) into an in-memory per-turn buffer, so an interrupt or liveness failure
  persists whatever text already streamed into the turn's durable record (ADR-014).
- **Boundary Rule (existing, extended here):** the Hub's dispatch path contains no
  synchronous wait on agent process exit — run outcome comes from run events or the liveness
  window. The Red/Green-probed structural test `NonBlockingDispatchRuleTests` scans
  `Grimoire.Hub.QueryDispatch` alongside the other dispatch namespaces.
- **Feature-Scoped Invariants:** reject-over-limit at the configured bound (a submission
  beyond the limit receives the documented busy rejection, never a queue position) and the
  default limit of 3 — covered by classicist integration tests asserting the observable
  rejection behavior; changing the number is a one-file amendment, not a broken structural
  test.

### Consequences

- Good, because bounded-concurrency reject-over-limit dispatch and single-slot FIFO dispatch
  coexist with zero shared state, so query and ingest can never wait on each other.
- Good, because "busy" is an immediate, honest answer — the caller can retry now rather than
  discover a stale answer later; there is no queue whose depth must be managed or persisted.
- Good, because interruption and liveness failure reuse one termination mechanism while
  staying distinguishable outcomes, so the record and the UI can tell "you stopped this" from
  "this broke".
- Bad, because a rejected submission is user-visible load shedding at exactly the busy
  moments; mitigated by the configurable limit and by rejection being immediate rather than a
  timeout.
- Neutral, because in-memory turn state means a Hub restart abandons in-flight query turns —
  accepted: queries are interactive, and their durable trace is the Conversation Record, not
  a resumable job.

## Change Triggers

- **Extensions (do not invalidate this ADR):** tuning the concurrency limit or the liveness
  window through configuration; new metadata carried on turn state or the terminal record;
  new rejection categories alongside the concurrency rejection; a new caller submitting turns
  through the same coordinator.
- **Invalidations (would require full supersession):** queueing over-limit query submissions
  instead of rejecting them; removing the concurrency bound; merging interruption into
  failure handling as a single undifferentiated terminal path; coupling query dispatch to the
  ingest queue or sharing dispatch state between the coordinators; persisting query dispatch
  state as resumable work.

## More Information

Supersedes [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md), whose
dispatch/concurrency/interruption aspect this ADR restates as current truth; ADR-011's other
aspects are re-decided in [ADR-044](ADR-044-shared-agent-runtime-library.md),
[ADR-045](ADR-045-token-level-answer-streaming.md), and
[ADR-047](ADR-047-query-realtime-delivery.md).

Read alongside: [ADR-038](ADR-038-heartbeat-run-supervision.md) — liveness silence as the
sole failure-detection authority the `failed` path defers to;
[ADR-039](ADR-039-persistent-run-queue.md) — the persistent single-slot FIFO ingest queue
this dispatch shape deliberately differs from;
[ADR-014](ADR-014-query-conversation-records.md) — the Conversation Record that durably
receives each terminal turn, including partial answers;
[ADR-036](ADR-036-agent-child-process-spawn-contract.md) — the spawn contract behind the
shared launcher port; [ADR-047](ADR-047-query-realtime-delivery.md) — how turn transitions
and chunks reach the frontend. None of their decisions are restated or narrowed here.

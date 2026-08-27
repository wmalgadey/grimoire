---
status: accepted
supersedes: ADR-008
superseded_by: []
reason: null
---

# ADR-039: Persistent FIFO Run Queue in the Operational-State Database

## Context and Problem Statement

Accepting a task submission and serving status must never wait on a running agent, yet the
Hub runs at most one ingest agent process at a time. Submissions accepted while a run is in
progress therefore need somewhere to wait — and that place must survive a Hub restart, or
accepted work is silently lost. Feature 003's in-process blocking gate (`IngestRunGate`)
had callers block until the slot freed and forgot everything on restart. This ADR decides
exactly one aspect: task runs are dispatched from a persistent FIFO queue held in the Hub's
embedded operational-state database, surviving restarts. The event channel a running task
reports over is ADR-037's aspect; failure detection is ADR-038's; post-failure re-entry is
ADR-025's.

## Decision Drivers

- Accepting submissions and serving status must never block on a running agent (spec 004
  FR-016, SC-008).
- Exactly one agent process at any time; FIFO ordering of further submissions with
  automatic advance during normal operation (FR-019).
- The queue must survive Hub restarts; after a restart, processing resumes only on explicit
  user action (clarification Q2; FR-021, SC-010).
- No new infrastructure — no message broker or job-queue service (Principle IV; ADR-002's
  proportionality driver).

## Considered Options

1. **FIFO queue rows persisted in the existing SQLite operational-state store (ADR-003)**
2. Keep the in-process blocking gate (`IngestRunGate`) — callers wait, nothing survives
   restart
3. An external message broker / job-queue service

## Decision Outcome

Chosen option: **Option 1 — queue rows in the existing SQLite operational store**, because
it reuses the store and restart-reconciliation pattern ADR-003 already established instead
of introducing new state machinery or infrastructure.

- **Queue.** Accepted submissions enter a FIFO queue persisted in the SQLite
  operational-state store, keyed by acceptance time. This replaced feature 003's blocking
  `IngestRunGate`.
- **Single-slot dispatch.** The dispatcher starts at most one agent process; on a run's
  terminal transition it starts the next queued task automatically. (During ADR-025's
  liveness recovery the run slot stays occupied — the queue neither advances nor reorders;
  a manually restarted task re-enters at the queue tail under its existing id. Those
  re-entry mechanics are ADR-025's decision, referenced here only for the queue's
  ordering guarantee.)
- **Restart semantics.** On Hub startup with queued rows present, the queue is marked
  paused; explicit user resume (whole queue) or per-task re-trigger re-arms automatic
  processing. Accepted work is never lost to a restart, and never silently resumed by one.
- **Operational state, not domain state.** The queue is operational bookkeeping on
  ADR-003's operational side of the persistence split: it lives in the git-ignored
  embedded database and is deletable without losing any durable record — wiki content,
  task artifacts, and harness records are files, not queue rows.
- **Scope.** This queue is the dispatch shape for single-slot, serialized agent work
  (ingest). Query dispatch deliberately uses a different shape — bounded concurrency with
  reject-over-limit, no queue — decided in ADR-011 and its successors, not here.
- **Rule classification (Constitution Principle III).** This ADR introduces **no new
  Dependency & Layering Boundary Rule** (the store stays in its adapter namespace under
  ADR-010's existing containment tests). The queue behaviors — FIFO order, single-slot
  dispatch, restart-pause plus explicit resume — are **Feature-Scoped Invariants**, covered
  by classicist, state-based integration tests against the real SQLite store and fake
  agent executables (e.g. `Grimoire.IntegrationTests/IngestRunQueueTests`), never by
  reflecting over dispatcher internals.

### Consequences

- Good, because queue durability reuses the existing SQLite operational store and
  restart-reconciliation pattern (ADR-003) — no new state machinery, broker, or service.
- Good, because acceptance is decoupled from execution: submissions and status reads never
  wait on a running agent.
- Bad, because a restart parks the queue until an operator explicitly resumes it — deliberate
  (clarification Q2): after an unplanned restart the operator decides when automatic
  processing re-arms, at the cost of requiring that manual step.
- Neutral, because FIFO-by-acceptance-time is the only fairness policy; at single-operator
  volumes no prioritization is needed, and none is decided here.

## Change Triggers

- **Extensions (do not invalidate this ADR):** new task types enqueued through the same
  queue; priority or scheduling metadata added to queue rows without changing the decided
  FIFO fairness guarantees; queue-depth telemetry; tuning of resume/re-trigger surfaces.
  No ADR action needed.
- **Invalidations (would require full supersession):** an in-memory-only queue (dropping
  restart survival); an external message broker or job-queue service as the queue's home;
  abandoning the restart/resume semantics (auto-resuming on startup, or discarding queued
  rows on restart); replacing FIFO dispatch with a policy that contradicts the decided
  ordering guarantee.

## More Information

Reads alongside: ADR-003 (the domain vs. operational persistence split this queue sits on —
the queue is operational state, deletable without losing durable records), ADR-037 (event
channel of the runs the queue dispatches), ADR-038 (terminal transitions that advance the
queue), ADR-025 (liveness re-entry and manual restart — owns how a task re-enters the
queue), ADR-011 and its successors (Query's bounded-concurrency, reject-over-limit dispatch
shape, deliberately not this queue), ADR-018 (the remediation task queue reuses this FIFO
queue shape). This ADR replaces the run-queue aspect of superseded ADR-008.

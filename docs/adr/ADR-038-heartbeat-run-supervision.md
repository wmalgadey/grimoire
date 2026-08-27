---
status: accepted
supersedes: ADR-008
superseded_by: []
reason: null
---

# ADR-038: Heartbeat Liveness as the Sole Run-Failure-Detection Authority

## Context and Problem Statement

An agent run is a child process executing an LLM loop; it can crash, hang mid-tool-call, or
be killed externally. Something must decide, automatically, that a run is dead — and exactly
one mechanism must own that decision, or racing detectors produce contradictory terminal
states. The agent already reports over the NDJSON event channel (ADR-037), including
periodic `heartbeat` events. This ADR decides exactly one aspect: liveness — heartbeat
events plus a silence timeout — is the sole authority for detecting a failed or hung agent
run. What happens after detection (bounded reactivation, restart) is owned by ADR-025;
queue advancement is owned by ADR-039.

## Decision Drivers

- Failure of a crashed or hung run must be detected automatically, without an operator
  watching (spec 004 FR-020, SC-009; clarification Q1 chose heartbeat + timeout).
- One failure authority, not several: crash, hang, and external kill should be covered by a
  single mechanism with no racing detectors.
- A hang is indistinguishable from work by process aliveness alone — a live process that
  has stopped making progress must still be detected.
- The supervision state machine must be pinned by deterministic, hermetic tests driving
  virtual time (Principle II; ADR-021 bans wall-clock waits in deterministic tests).

## Considered Options

1. **Event silence beyond a liveness window as the sole failure authority (heartbeat +
   timeout)**
2. Awaited process exit code as the failure signal (the prior ADR-002 contract)
3. Combined detectors: process-exit watcher plus heartbeat timeout, whichever fires first

## Decision Outcome

Chosen option: **Option 1 — liveness silence is the sole failure authority**, because one
mechanism covers crash, hang, and kill uniformly, with no second detector to race against.

- **Detection.** The Hub tracks the last-event time per running task. Event silence longer
  than the configured liveness window (default 60 s; the `heartbeat` cadence default is
  10 s, ADR-037) means the run has failed for liveness reasons: any leftover process is
  terminated. No other signal transitions a run to failed.
- **Exit codes are not the authority.** Process exit is not awaited and does not itself
  transition the run; the exit code remains diagnostics for manual CLI invocation only
  (ADR-037). A crashed process simply falls silent and is caught by the same window.
- **Terminal events end supervision.** A `completed` or `failed` event ends supervision for
  that task immediately; events arriving for an already-terminal task are recorded as
  diagnostics only.
- **Consequence of detection.** A liveness failure is not immediately final: its consequence
  is bounded automatic reactivation with increasing backoff, and manual restart of a
  finally-failed task, as decided in ADR-025 — this ADR owns detection, ADR-025 owns
  re-entry, and neither restates the other.
- **Rule classification (Constitution Principle III).**
  - **Boundary Rule**: the Hub's dispatch path contains no synchronous wait on agent exit
    for run outcome. Enforced by the permanent, Red/Green-probed IL-scan structural test
    `Grimoire.ArchTests/NonBlockingDispatchRuleTests`.
  - **Feature-Scoped Invariant**: the supervision state machine — silence detection,
    process termination, terminal-event handling, hand-off to ADR-025's reactivation —
    is pinned by deterministic, classicist integration tests advancing virtual time via
    the injected `TimeProvider` (ADR-025, ADR-021), never by reflecting over supervisor
    internals.

### Consequences

- Good, because a single failure authority covers crash, hang, and kill with one mechanism
  and no racing detectors or contradictory terminal states.
- Good, because supervision works identically for any agent on the shared event channel —
  a new agent type gets liveness detection for free by emitting heartbeats.
- Bad, because detection latency is bounded by the liveness window (up to ~60 s for a hard
  crash) — accepted in clarification Q1 in exchange for mechanism simplicity.
- Neutral, because the liveness window and heartbeat cadence are operational tuning values
  (code-level defaults, per ADR-025's treatment of its backoff schedule), not configuration
  surface — retuning them is not a decision change.

## Change Triggers

- **Extensions (do not invalidate this ADR):** tuning the liveness window, heartbeat
  cadence, or ADR-025's attempt/backoff parameters; new supervision telemetry (metrics,
  log events, spans) around detection; a new agent type entering supervision. No ADR
  action needed.
- **Invalidations (would require full supersession):** making process exit codes (or any
  second signal) a failure-detection authority alongside or instead of liveness silence;
  delegating failure detection to an external process monitor (systemd, orchestrator
  probes) replacing heartbeat authority; removing automatic detection in favor of
  operator-declared failure.

## More Information

Reads alongside: ADR-037 (the event channel that carries `heartbeat` and terminal events),
ADR-025 (bounded automatic reactivation, manual restart, and status history — the
consequence of a liveness failure; its mechanics are decided there, not here), ADR-039
(queue advance on terminal transition), ADR-021 (deterministic-wait enforcement that makes
the virtual-time supervision tests possible), ADR-011 and its successors (user-triggered
interruption is a Hub-initiated stop labeled by cause, distinct from a liveness `failed` —
it uses the same termination mechanism but is not a second failure detector). This ADR
replaces the run-supervision aspect of superseded ADR-008.

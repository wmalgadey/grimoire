---
status: accepted
---

# ADR-008: Agent Event Channel, Run Supervision, and Persistent Run Queue

## Context and Problem Statement

Under ADR-002 the Hub spawns the Ingest agent as a child process and awaits its exit
code to learn the run outcome; feature 003 serializes runs with an in-process gate
(`IngestRunGate`) that callers block on. Feature 004 (clarification session
2026-07-11) requires the opposite interaction model: the Hub must start the agent
without blocking, learn what is happening through events (lifecycle, heartbeats,
loop-activity counters), detect dead or hung runs by event silence, and keep exactly
one agent running while further accepted submissions wait in a queue that survives Hub
restarts. How agent and Hub communicate during a run, who detects failure, and where
the queue lives are cross-cutting shapes that every future long-running agent (Query,
Lint) will inherit, so they must be fixed by ADR. This ADR amends ADR-002's
result-reporting aspect; ADR-002's spawn model (child process, CLI args, file-based
artifacts, credential scoping per ADR-004) remains in force.

## Decision Drivers

- Accepting submissions and serving status must never wait on a running agent
  (spec 004 FR-016, SC-008).
- Failure of a crashed or hung run must be detected automatically, with heartbeat
  events + timeout as the chosen mechanism (clarification Q1; FR-020, SC-009).
- Exactly one agent process at any time; FIFO queueing of further submissions with
  automatic advance during normal operation (FR-019).
- The queue must survive Hub restarts; after a restart, processing resumes only on
  explicit user action (clarification Q2; FR-021, SC-010).
- Events must carry loop mechanics only — no wiki-content judgment enters backend
  contracts (Principle V; clarification Q3).
- No new infrastructure (Principle IV); hermetic testability without live LLM calls
  or real agent binaries (Principle II).

## Considered Options

1. **NDJSON events on the agent's stdout + Hub-side liveness supervision + queue in
   the existing SQLite operational store**
2. HTTP callback endpoint on the Hub that the agent POSTs events to
3. SignalR client connection from the agent to the Hub
4. File-based event journal per run, tailed by the Hub

## Decision Outcome

Chosen option: **Option 1.**

- **Event channel**: the agent writes newline-delimited JSON events (`started`,
  `heartbeat` every 10 s, `activity` with model-turn/tool-call counters and current
  action, `completed` with summary, `failed` with reason) to stdout; human-readable
  logging moves to stderr. The Hub, as process parent, reads the pipe and dispatches
  events. Malformed lines are logged and skipped.
- **Supervision**: the Hub tracks last-event time per running task. Event silence
  longer than the configured liveness window (default 60 s) is the **sole** failure
  authority: the run is marked failed with a liveness reason, any leftover process is
  terminated, and the queue advances. Process exit is not awaited and does not itself
  transition the run; terminal events end supervision immediately; events for
  terminal tasks are recorded as diagnostics only.
- **Queue**: accepted submissions enter a FIFO queue persisted in the existing SQLite
  operational-state store (ADR-003) keyed by acceptance time. The dispatcher starts
  at most one agent; on terminal transition it starts the next queued task. On Hub
  startup with queued rows, the queue is marked paused; explicit user resume
  (whole queue) or per-task re-trigger re-arms automatic processing. This replaces
  003's blocking `IngestRunGate`.
- **Exit code**: remains set by the agent for manual CLI invocation and diagnostics;
  it is no longer part of the Hub↔agent result contract.

### Consequences

- Good, because the parent↔child pipe requires no network surface, no auth, no
  broker, and no new infrastructure; hermetic tests script the event stream directly
  and use a fake agent executable for dispatch tests.
- Good, because a single failure authority (event silence) covers crash, hang, and
  kill with one mechanism and no racing detectors.
- Good, because queue durability reuses the SQLite operational store and the existing
  restart-reconciliation pattern (ADR-003) instead of new state machinery.
- Bad, because stdout is now a structured protocol surface: accidental writes to
  stdout inside the agent would corrupt the stream. Mitigated by routing all agent
  logging to stderr and tolerating malformed lines.
- Bad, because detection latency is bounded by the liveness window (up to ~60 s for a
  hard crash) — accepted in clarification Q1 in exchange for mechanism simplicity.
- Neutral, because a future remote/containerized agent can keep the same event
  vocabulary over a different byte transport; the event schema, supervision rules,
  and queue semantics defined here are transport-independent.

## More Information

Rejected options: Option 2 and 3 introduce a network/auth surface and couple the
agent to Hub hosting for what is a parent↔child relationship; Option 4's only
advantage (surviving Hub restarts mid-run) is moot because restart reconciliation
already fails interrupted runs (ADR-003), while adding tailing/rotation complexity.

Relationship to existing ADRs: amends ADR-002 (result reporting via events instead of
awaited exit code; spawn model unchanged); builds on ADR-003 (queue and supervision
timestamps are operational state); leaves ADR-004 (credential scoping at spawn) and
ADR-006 (guarded tool loop) untouched. Event schema details:
`specs/004-ingest-agent-systemprompt/contracts/agent-run-events.md`.

Structural enforcement per Principle III: an architecture/integration rule verifies
the Hub's dispatch path contains no synchronous wait on agent exit for run outcome
(Red/Green probe: a deliberately blocking dispatcher variant must fail the test), and
deterministic integration tests pin the supervision state machine.

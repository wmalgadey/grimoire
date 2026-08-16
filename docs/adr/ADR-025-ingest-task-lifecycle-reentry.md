---
status: accepted
---

# ADR-025: Ingest Task Lifecycle Re-Entry — Liveness Reactivation, Manual Restart, and Status History

> **Amends [ADR-008](ADR-008-agent-event-channel-run-supervision.md)**: event silence
> beyond the liveness window remains the sole failure-detection authority, but its
> consequence changes from immediately terminal to bounded re-entry — the Hub terminates
> the silent process, then automatically re-launches the same task up to a fixed number
> of attempts with increasing backoff before declaring final failure. `failed` also stops
> being unconditionally one-way: an operator may manually restart a finally-failed task,
> re-entering it into the queue under the same task id. ADR-008's event channel (NDJSON
> on stdout), single-slot FIFO queue, queue persistence, and restart-pause semantics are
> unchanged. This also revokes [ADR-002](ADR-002-ingest-agent-execution-model.md)'s
> recorded deferral of retry/backoff ("acceptable while one operator submits one ingest
> at a time") for the liveness case — without amending anything else ADR-002 decided.

## Context and Problem Statement

Under ADR-008, a run whose event stream falls silent for longer than the liveness window
(60 s) is terminated and marked `failed`, the queue advances, and nothing can bring that
task back — `ingest-retrigger` only re-arms a paused queue for tasks that are still
*queued*, and nothing re-enqueues a terminal task. Feature 023
(`specs/023-task-ui-improvements/spec.md`) records the operator hitting exactly this: a
scheduled ingest died at 60 seconds of agent silence ("Ingest failed: Agent run showed no
liveness for 60 seconds and was terminated.") with no recovery path short of resubmitting
from scratch, and no visible record of where in its lifecycle the task had stopped.

The feature requires three connected lifecycle changes that ADR-008 currently forbids or
does not provide: (a) a liveness interruption must trigger bounded automatic reactivation
with increasing backoff instead of immediate permanent failure (spec FR-007/FR-008,
clarified 2026-08-13: automatic, bounded, backoff); (b) a finally-failed task must be
manually restartable from the UI under the same task id with duplicate-request protection
(FR-010–FR-013); (c) every transition — including the new interruption, reactivation, and
restart events — must be durably recorded and displayable as an ordered history
(FR-005/FR-006), which the current single-status task artifact and transient
`operational_task_state` row cannot express. These change ADR-008's supervision
consequences and terminal-state semantics, so they are fixed here rather than inside the
feature.

## Decision Drivers

- ADR-008's liveness *detection* has proven correct; only its all-or-nothing consequence
  wastes scheduled work on transient stalls.
- The queue's single-slot FIFO ordering must not be disturbed by recovery mechanics.
- Duplicate manual restarts (two tabs, double click) must resolve deterministically —
  ADR-018 already established CAS-on-persisted-state-under-lock as the house arbiter.
- The status record must survive Hub restarts and task completion, and must not create a
  new write contention surface on the agent-owned task artifact (ADR-002 ownership,
  ADR-024 placement).
- Deterministic tests must be able to drive the backoff clock (ADR-021 bans wall-clock
  waits in deterministic tests).

## Decision Outcome

**1. Liveness reactivation (amends ADR-008's failure consequence).** On liveness-window
expiry the Hub terminates the silent process (unchanged), records a
`liveness_interrupted` history entry, and — while automatic attempts remain — re-launches
the same task id through the existing `IAgentProcessLauncher` port after an increasing
backoff delay, recording `reactivated` on re-launch. Defaults: 3 attempts, delays
10 s / 30 s / 90 s, defined as code-level constructor defaults alongside the existing
`livenessWindow` (operational tuning values per the feature spec, not configuration
surface — ADR-022 is untouched). The run slot stays occupied throughout interruption and
backoff: the queue neither advances nor reorders during recovery. When attempts are
exhausted, the existing final-failure path runs unchanged (Hub failure artifact,
`ingest.run.liveness_failed`, queue advance). Scheduling goes through an injected
`TimeProvider` so deterministic tests advance virtual time.

**2. Manual restart of a finally-failed task.** A coordinator method
(`RestartFailedAsync`), exposed as `POST /api/ingest-submissions/{taskId}/restart`,
re-enters a task whose current status is `failed` and whose normalized source artifact
still exists: history gains `restarted` + `queued`, the attempt counter resets, the task
joins the queue tail under its existing id, and the normal lifecycle proceeds.
Concurrency is arbitrated by compare-and-swap on persisted state under the coordinator
lock (ADR-018's idiom): exactly one concurrent restart wins; losers and invalid requests
(task not `failed`, source missing) receive a conflict outcome. `completed` remains
strictly terminal. `ingest-retrigger` keeps its existing queue-resume meaning; a CLI
restart command is a recognized future single-file amendment to ADR-020's command table,
not part of this decision.

**3. Status history as the durable transition record.** Every transition the Hub
observes — the six board stages plus `liveness_interrupted`, `reactivated`, `restarted` —
is appended to a new append-only `ingest_status_history` table in the Hub-owned SQLite
operational database (ADR-003's operational side), keyed `(task_id, seq)` with UTC
timestamp and optional detail. Rows are never updated or deleted; restart appends rather
than truncates. The table is written only by the Hub at its lifecycle-publishing choke
point; agents never touch it. The three new statuses are history/detail vocabulary only —
the board's six-column stage model is explicitly unchanged (spec clarification
2026-08-13), and during interruption/backoff the task remains presented in `running`.

**Rule classification (Constitution Principle III).** All rules above are
**Feature-Scoped Invariants** — they protect lifecycle behavior and are covered by
classicist, state-based integration tests (history rows, queue state, HTTP outcomes,
emitted telemetry) in `Grimoire.IntegrationTests`. This ADR introduces **no new
Dependency & Layering Boundary Rule**: no new package, namespace, or dependency-direction
constraint arises (persistence stays in its adapter namespace under ADR-010's existing
containment tests; re-launch uses the existing launcher port).

## Consequences

- **Good**: transient agent stalls no longer destroy scheduled work; every failure is
  diagnosable from an ordered, durable path; operators recover failed tasks from the UI
  without CLI access; races resolve deterministically.
- **Good**: ADR-008's detection model, event channel, and queue semantics survive intact;
  the amendment is confined to what happens *after* detection.
- **Bad / accepted**: a genuinely broken agent build now fails after ~3 attempts plus
  backoff (worst case a few minutes) instead of 60 seconds; the clearer history and
  bounded schedule make this visible rather than silent.
- **Bad / accepted**: the operational database grows by an append-only table that is
  never pruned; at single-operator task volumes this is negligible, and a retention rule
  can be a later, separate decision.
- **Neutral**: task artifacts keep their single `status` field (last-known stage);
  history is Hub-side truth for the path. Pre-feature tasks simply have no history rows;
  read paths fall back to current status.
- Supervision re-entry means `running → liveness_interrupted → reactivated → running` can
  repeat; consumers of lifecycle events must not assume a task passes `running` at most
  once (the board already keys on current stage, not transition count).

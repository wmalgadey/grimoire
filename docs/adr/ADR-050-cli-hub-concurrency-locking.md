---
status: accepted
supersedes: ADR-020
---

# ADR-050: Cross-Process CLI–Hub Coordination via OS-Level Locks

## Context and Problem Statement

The in-process CLI execution model (ADR-049) creates a second process class that executes
coordinator flows against the same data directory a running Hub may be serving. Invariants
that previously lived only in a running Hub's memory — one lint run at a time, one writer
to the operational state store — now have to hold *across* processes, or be consciously
accepted as unguarded. How concurrent CLI and Hub processes coordinate is the single
aspect this ADR decides: not through a broker, daemon, or global instance guard, but
through targeted OS-level artifacts — an exclusive lock file for lint-run mutual exclusion
and SQLite busy-tolerance for the shared operational state store.

## Decision Drivers

- Clarified product decision (feature 018): a CLI command may run while a Hub serves the
  same data directory — the CLI runs "analogous to the Hub"; requiring exclusive access
  would defeat the no-running-Hub-required model.
- The one-run-at-a-time lint invariant must be detectable across processes in both
  directions (CLI sees the Hub's active run, the Hub sees a CLI-triggered one).
- Concurrent writers to the Hub-owned SQLite operational state (ADR-003) must back off
  and retry rather than fail with `SQLITE_BUSY`.
- Coordination overhead must stay proportional to a solo-operator project: no new
  infrastructure, no network surface between processes (Constitution Principle IV's
  infrastructure-approval rule).
- Precedent: per-target exclusive OS file locks already coordinate cross-process wiki
  writes (`SharedFileWriteGuard`, ADR-015).

## Considered Options

1. **A global cross-process guard** — a single instance lock making CLI and Hub mutually
   exclusive per data directory. Rejected by the clarified decision: the CLI is a peer
   activation path, and a global lock would block harmless combinations (e.g. a query
   turn during a running Hub) to prevent conflicts only lint actually has.
2. **Targeted OS-level mitigations** — an exclusive lock file for the one flow with a
   hard single-run invariant (lint), plus SQLite busy-tolerance for the shared
   operational store; everything else relies on durable task states and records.

## Decision Outcome

Chosen option: **Option 2 — targeted OS-level mitigations, no global guard**, because it
protects the invariants that actually need cross-process enforcement at zero
infrastructure cost while keeping CLI and Hub freely concurrent everywhere else.

- **`lint.pid` exclusive lock.** `LintRunCoordinator.TriggerAsync` — the single code path
  both HTTP and CLI use — acquires an exclusive OS file lock on the `lint.pid` file for
  the full duration of a lint run (`LintPidLock`, wrapping a `FileShare.None` stream).
  Acquisition is single-attempt — no retry or backoff — because the caller needs an
  immediate busy/not-busy answer, mirroring the in-process semaphore's non-blocking
  acquire. A holder conflict maps to the existing `lint_run_active` outcome (surfaced as
  the CLI's state-conflict exit code and the HTTP conflict response), making "run already
  active" detectable across processes in both directions. The lock file is a runtime
  location under the data directory (`ResolvedGrimoirePaths.LintPidPath`), registered
  through the runtime path composition (ADR-040). Unlike ADR-015's
  `CrossProcessFileLock` (many per-target files named by content hash, with retry), this
  is a dedicated single-path, single-attempt lock.
- **SQLite busy-tolerance.** `OperationalStateRepository` enables WAL journal mode and
  sets `PRAGMA busy_timeout` on every connection, so concurrent Hub+CLI writers back off
  and retry inside SQLite instead of failing with `SQLITE_BUSY`, and readers proceed
  without blocking on a writer. ADR-003's decision that operational state is Hub-owned
  SQLite is unchanged — this tunes the store for two cooperating local processes.
- **Accepted gap: in-memory-only conflict knowledge stays invisible.** State a running
  Hub holds only in memory (e.g. its active query turn) is not visible to a CLI process;
  durable guards — task states, records, `lint.pid` — still apply. Dual agent runs
  outside lint therefore remain possible and are accepted.
- **Feature-Scoped Invariant — cross-process lint mutual exclusion.** While one process
  holds the lint run lock, a second process's trigger yields the `lint_run_active`
  outcome (state-conflict exit code on the CLI path) rather than a second run. Enforced
  by classicist integration tests spanning two processes (`HubCliConcurrencyTests`).
- **Feature-Scoped Invariant — concurrent operational-state writers succeed.** A CLI
  invocation writing operational state while a Hub holds the store open completes via
  busy-tolerance rather than failing with `SQLITE_BUSY`; covered by the same concurrency
  test suite. This ADR introduces no new Boundary Rule.

### Consequences

- Good, because cross-process "lint already active" detection is an improvement over the
  Hub-only status quo, in both directions, using nothing but a file lock.
- Good, because no broker, daemon, or global lock is introduced — coordination costs one
  lock file and two SQLite PRAGMAs.
- Bad, because concurrency control is deliberately partial: a Hub and a CLI invocation
  can each start agent work outside lint at the same time, guarded only by durable task
  states and records; accepted as the clarified product decision.
- Neutral, because the lock's single-attempt, no-backoff shape trades queueing for an
  immediate answer — a second trigger is reported busy, not queued, matching how the
  in-process slot semaphore already behaves.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new command or flow adopting the same
  pattern (a dedicated pid/lock file for a new single-run invariant, registered as a
  runtime location); tuning `busy_timeout` or other SQLite pragmas; additional durable
  guards (task states, records) consulted across processes.
- **Invalidations (would require full supersession):** a broker, daemon, or queue service
  replacing file-lock coordination between processes; a global single-instance guard
  making CLI and Hub mutually exclusive; dropping multi-process support (CLI requiring
  the Hub to be stopped, or becoming an HTTP client of it); moving lint mutual exclusion
  off the OS-level lock onto in-memory or network coordination.

## More Information

Supersedes [ADR-020](ADR-020-hub-cli-command-surface.md) together with
[ADR-048](ADR-048-hub-cli-framework.md) and
[ADR-049](ADR-049-cli-in-process-blocking-execution.md).

Read alongside: [ADR-049](ADR-049-cli-in-process-blocking-execution.md) — the in-process
execution model that makes the CLI a second process class;
[ADR-003](ADR-003-domain-operational-state-persistence.md) — operational state as
Hub-owned SQLite; [ADR-015](ADR-015-query-write-scope-and-wiki-write-coordination.md) —
the per-target cross-process wiki write locks this lock's idiom follows but does not
reuse; [ADR-040](ADR-040-runtime-path-composition.md) — the path
composition the `lint.pid` location is registered through. None of their decisions are
restated or narrowed here.

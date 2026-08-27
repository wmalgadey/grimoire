---
status: accepted
supersedes: ADR-020
---

# ADR-049: Hub CLI In-Process Blocking Execution Against the Shared Composition Root

## Context and Problem Statement

The Hub's CLI commands trigger the same flows its HTTP endpoints do — lint runs,
remediation transitions, ingest retrigger/resume, blocking query turns, source submission.
How a command reaches that logic is a system boundary: as a thin HTTP client the CLI would
require a running Hub; as a fire-and-forget trigger it would orphan supervised agent child
processes; as an in-process peer it becomes a second process class executing coordinator
flows against the same data directory a running Hub may be serving. Feature 018's spec
clarification (2026-08-03) fixed the requirement — commands must not call a running Hub's
HTTP endpoints and must use the agent functionality directly in-process, blocking until
completion — and with it comes a failure contract of its own: deterministic exit codes and
a stdout/stderr split for unattended scripting. This ADR decides that execution model and
the console contract it carries. The CLI framework is ADR-048's aspect; cross-process
coordination with a concurrently running Hub is ADR-050's.

## Decision Drivers

- Spec 018 clarification: same flow as HTTP, in-process, blocking; a running Hub must not
  be required; HTTP remains the asynchronous, subscribe-style activation path.
- 100% CLI↔HTTP behavioral parity: both entry points must drive the *same* logic, not
  parallel reimplementations (spec FR-005/SC-005).
- Fire-and-forget agent supervision (`TryStartNextAsync`, `SuperviseAsync`) must not be
  started by a process that exits before the supervised run ends — hence blocking.
- Deterministic exit codes and clean stdout are required for unattended scripting
  (Constitution Principles II/IV: hermetic, state-based verification of the contract).
- The dispatch layer's non-blocking rule (`NonBlockingDispatchRuleTests`) must survive:
  a blocking CLI must not push synchronous process-waits into dispatch namespaces.

## Considered Options

1. **Thin HTTP client against a running Hub** — commands call the existing endpoints.
   Rejected by the clarified requirement: a webserver must not be required.
2. **In-process shared composition, blocking** — commands build the same service graph as
   the web host (without binding a port), call the same coordinator methods the endpoint
   handlers call, and await any spawned agent work to its terminal state.
3. **In-process, fire-and-forget** — commands trigger and exit immediately. Rejected:
   orphans supervised agent children; no outcome is ever recorded.

## Decision Outcome

Chosen option: **Option 2 — in-process shared composition, blocking**, because it is the
only model that needs no running Hub, preserves one implementation for both entry points,
and leaves no agent work running unsupervised after the CLI exits.

- **Shared composition root.** Each command runs the Hub's existing composition: the same
  `WebApplicationBuilder` registrations, runtime path resolution, SQLite initialization,
  restart reconciliation, and coordinator initialization as the web host — `builder.Build()`
  is invoked but the server is never started, so **no port is bound**. Commands resolve
  services from the built application's service provider (one composition point preserved).
- **Same methods as HTTP.** Each command calls the same coordinator/service method its
  HTTP endpoint handler calls. The three remediation transitions live in
  `RemediationTaskTransitionService` (`Grimoire.Hub.RemediationTasks`), called by both the
  endpoint handlers and the CLI commands; lint, ingest, and query expose coordinator
  methods directly. ADR-018's execution-dispatch allow-list is untouched.
- **Blocking until terminal state.** A command that spawns agent work blocks until that
  work reaches its terminal state, optionally rendering live status (to stderr) from the
  same lifecycle state the realtime channel publishes. The blocking wait is state
  observation inside `Grimoire.Hub.Cli` — no synchronous process-wait enters any dispatch
  namespace, so the dispatch layer's non-blocking rule is unaffected.
- **Fresh-process semantics equal restart semantics.** The CLI bootstrap runs the same
  `RestartReconciler` as a freshly started Hub, so paused-queue-after-restart rules apply
  to a CLI invocation exactly as to a Hub start.
- **Blocking `query`, timeout, and cancellation.** `query` submits via
  `QueryRunCoordinator.SubmitTurnAsync`, streams the accumulating answer while waiting,
  and blocks until the turn's terminal state. `--timeout` (default 300 s) expiry
  interrupts the turn via the same `InterruptAsync` action HTTP uses — persisting the
  partial answer — and exits with the timeout code; Ctrl-C triggers the same interrupt
  with the cancellation code. No agent work continues unsupervised after the CLI exits.
  CLI-generated conversation ids conform to ADR-014's id grammar.
- **Feature-Scoped Invariant — exit-code and console-stream contract.** Every command's
  terminal outcome maps to exactly one exit code: `0` success · `1` operation failed ·
  `2` usage error (including Spectre pre-execution failures, mapped before any command
  runs) · `3` not found · `4` state conflict (including the cross-process lint holder
  conflict, ADR-050, and the query concurrency limit) · `5` wait timeout (turn
  interrupted) · `130` cancelled by interrupt signal (turn interrupted). Stdout carries
  exactly the result contract (`specs/018-hub-cli-commands/contracts/cli-commands.md`);
  live status renders to stderr; the `FigletText` logo appears only on root help and
  web-host startup. Enforced by classicist integration tests (`HubCliCommandTests`,
  `HubCliQueryCommandTests`).
- **Feature-Scoped Invariant — CLI↔HTTP parity.** CLI and HTTP entry points produce the
  same outcomes because they call the same methods; enforced by `HubCliParityTests`.
- **Feature-Scoped Invariant — telemetry flush before exit.** The CLI process executes
  the instrumented coordinator code, so the existing signals fire in-process; the command
  path disposes the built host before exit so telemetry export flushes. Guarded by a
  deterministic in-memory-exporter test (in `HubCliConcurrencyTests`). No CLI-specific
  signals exist — the CLI adds activation paths, not state transitions.
- This ADR introduces no new Boundary Rule; the dispatch layer's non-blocking rule it
  must respect is owned by the agent event-channel/supervision decisions and enforced by
  the existing `NonBlockingDispatchRuleTests`.

### Consequences

- Good, because no running Hub is required, and CLI↔HTTP parity is structural (shared
  coordinators and the shared transition service) rather than a testing aspiration.
- Good, because blocking supervision means no orphaned agent children and every
  CLI-triggered run records its outcome before the process exits.
- Good, because deterministic exit codes and a clean stdout/stderr split make the CLI
  scriptable unattended.
- Bad, because commands that trigger agent work can run for minutes — inherent to
  blocking; live status on stderr mitigates the experience without polluting stdout.
- Neutral, because HTTP remains the asynchronous/subscribe interaction style and the CLI
  the blocking one — two activation paths over one implementation, by design.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new command executing under the same
  model (shared composition, same coordinator method as its HTTP counterpart, blocking to
  terminal state); a new exit code appended for a genuinely new outcome class; new status
  rendering on stderr; a new shared service extracted so both entry points keep calling
  one method.
- **Invalidations (would require full supersession):** turning the CLI into an HTTP
  client against a running Hub; background or fire-and-forget command execution; a
  CLI-specific composition root or service graph diverging from the web host's; moving
  the failure contract off exit codes/stdout (e.g. onto a response envelope).

## More Information

Supersedes [ADR-020](ADR-020-hub-cli-command-surface.md) together with
[ADR-048](ADR-048-hub-cli-framework.md) and
[ADR-050](ADR-050-cli-hub-concurrency-locking.md).

Read alongside:
[ADR-026](ADR-026-hub-api-error-contract-and-frontend-error-presentation.md) — the HTTP
counterpart of the failure contract: the RFC 7807 envelope is composed at the HTTP
endpoint boundary, never inside the shared coordinator/transition-service layer, which is
what keeps this ADR's exit-code contract free of HTTP response shapes;
[ADR-023](ADR-023-hub-cli-default-command-and-root-help-routing.md) — the root default
command starts the web host through this same in-process composition path;
[ADR-050](ADR-050-cli-hub-concurrency-locking.md) — coordination between a CLI invocation
and a concurrently running Hub; [ADR-037](ADR-037-agent-event-channel-protocol.md) /
[ADR-038](ADR-038-heartbeat-run-supervision.md) — the event channel and supervision the
blocking wait observes; [ADR-014](ADR-014-query-conversation-records.md) — the
conversation-id grammar CLI-generated ids conform to;
[ADR-018](ADR-018-remediation-action-authorization-and-execution.md) — the remediation
execution-dispatch allow-list. None of their decisions are restated or narrowed here.

# Research: Hub CLI Command Parity for Write Actions

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02, revised 2026-08-03

**Input**: [spec.md](./spec.md) (Clarifications 2026-08-02 and 2026-08-03), GitHub
issue #45 and its addendum comment (2026-08-02: adopt a real CLI framework —
Spectre.Console.Cli preferred — plus an ASCII-art logo), all 18 ADRs in `docs/adr/`,
and the current Hub code base.

> Revision note: the 2026-08-03 clarification replaced the originally planned
> HTTP-client execution model. Commands now execute **in-process** through the same
> coordinators the HTTP endpoints use, blocking until completion. D1, D4–D6, D8–D10
> were rewritten accordingly; D2, D3, D7 stand.

## D1: Execution model — in-process, same flow as HTTP, blocking

**Decision**: Every command bootstraps the Hub's **existing composition** (the same
`WebApplicationBuilder` service registrations, path resolution, SQLite initialization,
and restart reconciliation as the web host — `builder.Build()` without `app.Run()`, so
no port is ever bound) and calls **the same coordinator/service method its HTTP
endpoint handler calls**. Commands are blocking: any agent work the flow spawns is
supervised in the CLI process and awaited to its terminal state before exit, with the
CLI free to render live status from the same lifecycle state the realtime channel
publishes (HTTP stays the async/subscribe interaction style; the CLI is the blocking
one — per clarification, the CLI is "just another activation path").

| Command | In-process flow (same as the HTTP handler) | Blocks until |
| --- | --- | --- |
| `lint-run` | `LintRunCoordinator.TriggerAsync()` | the run's terminal state; prints run id at start, final status at end |
| `remediation-authorize` | the authorize transition (CAS `proposed→authorized`, publish, metrics, eager `TryStartNextAsync`) | the authorized task reaches a terminal state when the eager dispatch starts executing; exits after the transition when the remediation queue is paused (identical to the HTTP flow in the same state) |
| `remediation-dismiss` / `remediation-withdraw` | the dismiss / withdraw-authorization transitions | immediate (no agent work in these flows) |
| `ingest-retrigger` | `IngestRunCoordinator.RetriggerAsync(taskId)` | the retriggered task reaches a terminal state |
| `ingest-resume` | `IngestRunCoordinator.ResumeAsync()` | the queue drains (all queued tasks processed); prints queued count up front |
| `query` | `QueryRunCoordinator.SubmitTurnAsync(...)` | the turn's terminal state; streams the accumulating answer while waiting |

**Shared-logic extraction**: the three remediation transitions currently live inline in
`RemediationTaskEndpoints`' handlers. To satisfy FR-005 ("same underlying logic") they
are extracted into a `RemediationTaskTransitionService` in
`Grimoire.Hub.RemediationTasks` that both the endpoint handlers and the CLI commands
call — the one extraction this model requires (lint, ingest, and query already expose
coordinator methods).

**Rationale**: the operator's clarified intent — no webserver dependency, direct use of
the agent functionality, CLI as a peer activation path. The blocking run-to-exit shape
has an in-repo precedent: `submit-source` already spawns the ingest agent via
`IAgentProcessLauncher.RunToExitAsync` and waits (ADR-008 explicitly permits this on
the manual CLI path). Reusing the web host's own composition keeps CLI↔HTTP parity
(SC-005) by construction and adds minimal Hub complexity (per the clarification's
"don't grow the Hub disproportionately" constraint — command classes plus one
extraction, no new port/adapter layer).

**Consequences accepted in the clarification**:

- **No global cross-process guard.** A CLI command may run while a Hub serves the same
  data directory. The operational-state store gets a second writer; mitigation is
  per-operation locks where an invariant demands one (D1a) plus SQLite busy-tolerance
  hardening (D1b). In-memory-only conflicts a running Hub would detect (e.g. its
  active query turn on the same conversation) are not visible to the CLI process —
  accepted; the durable 409-equivalents (task states, records) still apply.
- **Fresh-process semantics equal restart semantics.** The CLI bootstrap runs the same
  `RestartReconciler` as the web host, so paused-queue-after-restart rules (ADR-008,
  ADR-018) apply identically to a CLI invocation — which is exactly "the same flow as
  HTTP" in an equivalently-started Hub.

### D1a: Lint mutual exclusion via `lint.pid`

**Decision** (from the clarification): `LintRunCoordinator.TriggerAsync` — the single
code path both HTTP and CLI use — acquires an **exclusive OS file lock on a
`lint.pid` file** (new runtime location under the data directory, registered in
`GrimoirePathOptions`/`ResolvedGrimoirePaths` per ADR-009) for the duration of a run.
A holder-conflict maps to the existing `Busy`/`lint_run_active` outcome, making
US1-Scenario-2 ("run already active") detectable **across processes** — Hub↔CLI in
either direction. The existing in-memory slot remains as the in-process fast path.
Precedent: `SharedFileWriteGuard`'s per-target exclusive file locks (ADR-015).

### D1b: SQLite dual-writer hardening

**Decision**: `OperationalStateRepository` connections enable `busy_timeout` (and WAL
journal mode) so concurrent Hub+CLI writers back off instead of failing with
`SQLITE_BUSY`. Small, behavior-preserving hardening required by the no-global-guard
decision; verified by a concurrent-writer integration test.

## D2: CLI framework — Spectre.Console.Cli

**Decision**: Adopt **Spectre.Console.Cli 0.55.0** (latest stable on NuGet as of
2026-08-02) as the Hub's command parser/dispatcher. Each command is an
`AsyncCommand<TSettings>` class with declarative `[CommandArgument]`/`[CommandOption]`
settings; commands are registered once on a `CommandApp`, which generates per-command
`--help` — retiring `BuildUsageText()` while preserving 017's single-source-of-truth
principle (each switch declared exactly once, in its settings class). Spectre's
status/progress rendering also serves D1's live status display, and `FigletText`
serves D7.

**Alternatives considered**:

- *`System.CommandLine`*: viable and Microsoft-only, but the issue comment prefers
  Spectre; no console-art or status-display support.
- *Keep hand-rolled parsing*: rejected — 8 commands × validation × help would triple
  the ad-hoc code 017 just consolidated.

**Packaging**: new `PackageVersion` entry in `backend/Directory.Packages.props` +
`PackageReference` in `Grimoire.Hub.csproj`. Spectre.Console.Cli is a pure in-process
parsing library — not an "external system" in the ADR-010 sense — but its containment
is a new structural rule fixed by ADR-019 (see D10).

## D3: Dispatch rule and ADR-009 coexistence

**Decision**: `Program.cs` keeps a single early dispatch gate:

1. A static **`HubCliCommands` catalog** (name → description → command type) is the
   single source of truth for command names, driving both the `CommandApp`
   registrations and the dispatch check.
2. If `args[0]` matches a catalog name, or `--help`/`-h` appears anywhere in `args`
   (017's precedence rule, FR-011), the process runs `CommandApp.RunAsync(args)` and
   exits with its return code. **The web host's `app.Run()` is never reached** (the
   service graph is still built, per D1 — but no port is bound).
3. Otherwise the web-host path runs unchanged: `AddCommandLine(args,
   PathConfigurationSwitchMappingsFactory())`, ADR-009 precedence
   (CLI > env > appsettings > defaults), `PathSwitchCatalog` untouched.

**Root help**: a custom Spectre `IHelpProvider` renders the command list from the
`CommandApp` registrations and appends a server-options section generated from
`PathSwitchCatalog.All` — `BuildUsageText()` retires; the 017 parity test (usage text ⊇
every path switch) keeps passing from the same single source. An unknown `args[0]`
command name falls into the CommandApp, whose unknown-command error satisfies the
spec's unknown-command edge case (usage error, non-zero exit).

**`submit-source` migration**: `submit-source` becomes `SubmitSourceCommand` on the
same `CommandApp` (settings: `--path` required, `--source-kind` optional), keeping its
in-process execution and exact output line `Submitted ingest task: {taskId}`.

## D4: Path switches on every command

**Decision**: Because every command now resolves the data directory in-process, **all
commands accept the ADR-009 path switches**. A shared `HubPathSettings :
CommandSettings` base class declares one `[CommandOption]` per `PathSwitchCatalog.All`
entry (a parity test asserts the 1:1 mapping so the two can never drift); every command
settings class inherits it. Actual binding continues through the same
configuration-composition path as the web host (`AddCommandLine` with the ADR-009
switch mappings), preserving the CLI > env > appsettings > defaults precedence chain —
Spectre performs syntax/validation only. No `--hub-url` exists (nothing remote).

## D5: Exit-code convention

**Decision** (spec delegates this to planning):

| Code | Meaning | Trigger |
| --- | --- | --- |
| 0 | Success | Action completed; `query` turn reached `completed`; `ingest-resume` drained the queue |
| 1 | Operation failed | Triggered run/turn/task reached `failed`; conversation record unreadable; unexpected error |
| 2 | Usage error | Unknown command, missing/malformed required argument, empty prompt (Spectre validation — no action attempted) |
| 3 | Not found | Unknown task id / run id |
| 4 | State conflict | Lint run already active (in-process slot or `lint.pid` holder), unresolved remediation tasks, task not in required state, execution already started, task not in queue, conversation already active, query concurrency limit |
| 5 | Wait timeout | `query` timeout elapsed; turn interrupted (per FR-015 as clarified 2026-08-03) |
| 130 | Cancelled | Interrupt signal during a blocking wait (POSIX 128+SIGINT); turn interrupted before exit |

Spectre's default exit codes are overridden where needed (validation/unknown-command
mapped to 2 via `CommandApp` configuration; command `ExecuteAsync` return values carry
the rest).

## D6: `query` command semantics

**Decision**:

- Submit via `QueryRunCoordinator.SubmitTurnAsync(conversationId, prompt)` —
  `{prompt}` only per ADR-014; `Busy`/`ConversationAlreadyActive`/`RecordUnreadable`
  results map per D5.
- Wait in-process on the coordinator's turn state (`GetTurn(turnId)`), **streaming the
  accumulating answer** (`QueryTurnState.Answer` deltas) to the console while waiting —
  the CLI-side equivalent of the realtime channel's `answer_chunk` events.
- `--timeout <seconds>`, integer, default **300** (spec: 5 minutes). On expiry the CLI
  calls `QueryRunCoordinator.InterruptAsync(turnId)` — the same interrupt action as
  HTTP — persisting the partial answer, and exits 5 (clarification 2026-08-03).
- Ctrl-C (`Console.CancelKeyPress`) → same interrupt call, cancellation message,
  exit 130. Timeout and cancellation stay distinguishable by message and exit code
  (SC-006).
- `--conversation-id` optional; when omitted the CLI generates
  `{utcNow:yyyy-MM-dd}-conv-{Guid:N}` truncated to 40 chars — conforming to ADR-014's
  `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$` — and prints it in the header line.
- Success output (spec clarification 2026-08-02): header line
  `Query turn {turnId} in conversation {conversationId}: {state}` followed by the
  answer verbatim. When the answer was already streamed live, the header is printed
  first as a status line and the final block remains the authoritative stdout contract.
- The server-side liveness watchdog (60 s event silence → `failed`) runs in the CLI's
  own coordinator instance, bounding a hung agent independently of `--timeout`.

## D7: ASCII-art logo (FigletText)

**Decision**: `AnsiConsole.Write(new FigletText("Grimoire"))` is printed in exactly two
places: the **root help** (top of the custom help provider output) and the **web-host
startup banner**. Command execution output stays logo-free — FR-006 requires
script-parseable stdout. Per-command `--help` output also stays logo-free (compact).
Live status displays render to stderr so stdout keeps carrying only the result
contract.

## D8: Observability — existing signals now emitted in-process; no new signals

**Decision**: This feature declares **no new metrics, log events, or trace spans** —
but unlike a thin client, the CLI process now *runs* the instrumented coordinator code,
so the existing signals (`wiki.lint.runs_total`, `hub.lint.trigger`,
`wiki.remediation.tasks_authorized_total`/`…dismissed_total`/`…withdrawn_total`,
`RemediationLifecycleLogEvents`, `ingest.queue.resumed`, `query.turns_total`,
`hub.query.submit`, `QueryLifecycleLogEvents`, …) fire in the CLI process.

Two obligations follow (implementation tasks, not new signal rows):

1. **Telemetry bootstrap + flush**: the CLI path uses the same `AddHubTelemetry`
   registration the web host uses (it comes with the shared composition, D1) and MUST
   dispose the host/providers before exit so OTLP export flushes — otherwise the
   emitted telemetry of a short-lived process is silently lost. A deterministic test
   asserts a CLI-invoked flow still records its existing signals via the in-memory
   exporter (ADR-005 pattern).
2. **No new rows**: the justification for declaring no new signals is that every
   business transition the CLI can cause already has its declared, tested signal from
   the feature that introduced it; the CLI adds activation, not new state transitions.
   (017 precedent for a justified-N/A section, strengthened here by the flush test.)

## D9: Test approach

**Decision**: three layers, following existing repo idioms:

1. **Structural (Phase 0, Red/Green probed)** in `Grimoire.ArchTests`:
   Spectre.Console containment to `Grimoire.Hub.Cli*` + composition root (new C9);
   `Grimoire.Hub.Cli` added to the N1 cross-agent namespace map (+ convention doc
   mirror). No HTTP-containment change (the CLI makes no HTTP calls).
2. **In-process integration** in `Grimoire.IntegrationTests`: command classes executed
   against the real composed service graph with temp-dir SQLite and
   `FakeAgentProcessLauncher` (scripted/auto-play terminal states) — asserting stdout
   result lines, exit codes, state transitions, blocking behavior, `lint.pid`
   cross-process conflict (two harness instances), timeout-interrupt and
   cancellation-interrupt paths, SQLite dual-writer tolerance (D1b), telemetry flush
   (D8), and CLI↔HTTP parity (SC-005: same seeded state, one transition via the
   endpoint handler, one via the command — identical rows/records).
3. **Out-of-process** (extends `HubHelpUsageTests` pattern): spawned real
   `Grimoire.Hub.dll` asserting root help lists all 8 commands + path switches
   (SC-004), per-command help, unknown command → exit 2, and that command invocations
   never bind a port.

Ctrl-C OS-signal glue is thin and code-reviewed; the cancellation *path* (interrupt +
exit code) is tested via the command's cancellation token.

## D10: New structural boundary → ADR-019

**Decision**: This feature introduces a new structural boundary — a multi-command CLI
dispatch surface with a new parser dependency, a blocking in-process execution mode,
and a cross-process lint lock — that no accepted ADR covers (verified: only ADR-009
touches argument handling, and it governs configuration binding only; 017's plan
formally recorded that no ADR governs CLI parsing). **ADR-019 — Hub CLI Command
Surface** is drafted as part of this plan
(`docs/adr/ADR-019-hub-cli-command-surface.md`) and MUST reach Accepted before
`/speckit-tasks`. It fixes: the `Grimoire.Hub.Cli` namespace and its cross-agent
ownership; Spectre.Console containment (C9); the dispatch rule and ADR-009 coexistence
(D3/D4); the in-process blocking execution model incl. the remediation-transition
extraction (D1); the `lint.pid` lock (D1a) and SQLite hardening (D1b); the
timeout-interrupt contract (D6); telemetry flush (D8); and the exit-code convention
(D5).

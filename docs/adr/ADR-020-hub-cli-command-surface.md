---
status: accepted
---

# ADR-020: Hub CLI Command Surface — Framework, Dispatch, and In-Process Blocking Execution

> **Amended by [ADR-022](ADR-022-minimal-directory-configuration-surface.md)**: every
> command still accepts the ADR-009 path switches exactly as this ADR describes, but
> `PathSwitchCatalog.All` — the single source of truth this ADR already deferred to —
> is now capped at exactly three entries (`--data-dir`, `--agent-dir`, `--wiki-dir`)
> instead of sixteen. The Spectre.Console.Cli framework, dispatch, and blocking-execution
> model this ADR establishes are unchanged.

## Context and Problem Statement

Feature 018 (`specs/018-hub-cli-commands/spec.md`) grows the Hub binary's command
surface from one command (`submit-source`) to eight: `lint-run`,
`remediation-authorize`, `remediation-dismiss`, `remediation-withdraw`,
`ingest-retrigger`, `ingest-resume`, and a blocking `query`, alongside the existing
`submit-source`. GitHub issue #45's addendum comment (2026-08-02) requests retiring the
hand-rolled parsing (`args.Any(...)` help check, linear `ParseOption` scan, `args[0]`
string-compare dispatch, hand-written `BuildUsageText()`) in favor of a command-pattern
CLI library, naming Spectre.Console.Cli as the preferred fit. The spec's 2026-08-03
clarification fixes the execution model: commands must **not** call a running Hub's
HTTP endpoints — they use the agent functionality **directly in-process**, blocking
until completion, as a peer activation path to HTTP (which remains the asynchronous,
subscribe-style path).

No accepted ADR covers CLI argument parsing or command dispatch: ADR-009 governs only
the *configuration binding* of path switches (stock `AddCommandLine` +
`PathSwitchCatalog`), and feature 017's plan formally recorded "none govern CLI" when
it introduced `--help`. This feature crosses the boundary threshold 017 did not:

1. A multi-command dispatch structure with a **new NuGet parsing dependency** that must
   coexist with ADR-009's configuration-provider parsing over the same `args`.
2. A **second process class that executes coordinator flows** against the same data
   directory a running Hub may be serving — touching ADR-003's single-writer
   operational state and the one-run-at-a-time invariants that today live only in a
   running Hub's memory.
3. A **blocking execution mode** in a codebase whose dispatch path is architecturally
   non-blocking (ADR-008, `NonBlockingDispatchRuleTests`), plus timeout and Ctrl-C
   contracts that both map onto the existing interrupt action.

## Decision Drivers

- Spec clarification 2026-08-03: same flow as HTTP, in-process, blocking; a running
  Hub must not be required; the CLI may render status / stream run events; Hub
  complexity must not grow disproportionately (a future dedicated hub-CLI may absorb
  these commands).
- Spec FR-005/SC-005: CLI and HTTP entry points must drive the *same* logic with 100%
  behavioral parity.
- ADR-003: operational state is Hub-owned SQLite; a second writer needs deliberate
  handling once "no running Hub required" is a requirement.
- Fire-and-forget agent supervision (`TryStartNextAsync`, `SuperviseAsync`) must not
  be started by a process that exits before the supervised run ends — hence blocking.
- ADR-008 explicitly permits run-to-exit awaiting on the manual CLI path (the
  `submit-source`/`SubmissionService.RunToExitAsync` precedent).
- ADR-009: one composition point and one precedence order for path configuration;
  `PathSwitchCatalog` stays the single source of truth for path switches.
- 017's principle: every command/switch declared exactly once — no drift between
  parser, dispatcher, and help output.
- Constitution Principles I–IV: namespace containment with Red/Green-probed rules;
  hermetic deterministic tests; deterministic exit codes and clean stdout for
  unattended scripting.

## Considered Options

### Command execution model

- **O1: Thin HTTP client against a running Hub** — commands call the existing
  endpoints. Rejected by the 2026-08-03 clarification (webserver must not be
  required).
- **O2: In-process shared composition, blocking** — commands build the same service
  graph as the web host (without binding a port), call the same coordinator methods
  the endpoint handlers call, and await any spawned agent work to its terminal state.
- **O3: In-process, fire-and-forget** — commands trigger and exit immediately.
  Rejected: orphans supervised agent children; no outcome is ever recorded.

### CLI framework

- **F1: Spectre.Console.Cli** — command classes (`AsyncCommand<TSettings>`),
  declarative settings, generated per-command help, `FigletText` logo, status/progress
  rendering for the live-status requirement.
- **F2: System.CommandLine** — Microsoft-only, dependency-light, no console art or
  status display.
- **F3: Keep hand-rolled parsing** — extend `ParseOption`/`BuildUsageText`.

## Decision Outcome

**Chosen: O2 (in-process shared composition, blocking) + F1 (Spectre.Console.Cli
0.55.0)**, structured as follows.

### Namespace and ownership

- New namespace **`Grimoire.Hub.Cli`** hosts the command surface: the `HubCliCommands`
  catalog, per-command `AsyncCommand<TSettings>` classes and settings, the custom help
  provider, the console status renderer, and the exit-code mapping. It is registered
  as **cross-agent** in ADR-013's N1 Hub namespace-ownership map
  (`AgentArtifactNamingRuleTests` + `docs/conventions/agent-artifact-naming.md`); like
  `Grimoire.Hub.Realtime`, it may host agent-token types (`LintRunCommand`,
  `QueryCommand`, …) because commands are per-agent entries of shared infrastructure.

### Execution model

- Each command runs the Hub's **existing composition**: the same
  `WebApplicationBuilder` registrations, ADR-009 path resolution, SQLite
  initialization, restart reconciliation, and coordinator initialization as the web
  host — `builder.Build()` is invoked but `app.Run()` never is, so **no port is
  bound**. Commands resolve services from the built application's service provider
  (Spectre type-registrar over the same container — one composition point preserved).
- Each command calls the **same coordinator/service method its HTTP endpoint handler
  calls** and blocks until any agent work the flow spawns reaches a terminal state,
  optionally rendering live status (to stderr) from the same lifecycle state the
  realtime channel publishes. The blocking wait is state observation inside
  `Grimoire.Hub.Cli` — no synchronous process-wait enters any dispatch namespace, so
  ADR-008's `NonBlockingDispatchRuleTests` is unaffected.
- The three remediation transitions (authorize / dismiss / withdraw), today inline in
  `RemediationTaskEndpoints`, are extracted into a **`RemediationTaskTransitionService`**
  in `Grimoire.Hub.RemediationTasks`, called by both the endpoint handlers and the CLI
  commands — the single extraction this model requires; lint, ingest, and query
  already expose coordinator methods. ADR-018's allow-listed execution dispatch
  (`RemediationRunCoordinator.TryStartNextAsync` only) is unchanged.
- Fresh-process semantics equal restart semantics: the CLI bootstrap runs the same
  `RestartReconciler`, so paused-queue-after-restart rules (ADR-008/ADR-018) apply to
  a CLI invocation exactly as to a freshly started Hub.

### Concurrency with a running Hub

- **No global guard** (clarified decision): a CLI command may run while a Hub serves
  the same data directory; the CLI runs "analogous to the Hub". Two targeted
  mitigations replace a global lock:
  - **`lint.pid` exclusive lock**: `LintRunCoordinator.TriggerAsync` — the single code
    path both HTTP and CLI use — acquires an exclusive OS file lock on a `lint.pid`
    file for the duration of a lint run. The path is a new runtime location under the
    data directory, registered in `GrimoirePathOptions`/`ResolvedGrimoirePaths` per
    ADR-009. A holder-conflict maps to the existing `lint_run_active` outcome, making
    "run already active" detectable across processes in both directions. Precedent:
    `SharedFileWriteGuard`'s per-target exclusive locks (ADR-015).
  - **SQLite busy-tolerance**: `OperationalStateRepository` enables `busy_timeout` and
    WAL journal mode so concurrent Hub+CLI writers back off instead of failing with
    `SQLITE_BUSY`.
- In-memory-only conflict knowledge of a concurrently running Hub (e.g. its active
  query turn) remains invisible to the CLI process — an accepted consequence; durable
  guards (task states, records, `lint.pid`) still apply.

### Blocking `query`, timeout, and cancellation

- `query` submits via `QueryRunCoordinator.SubmitTurnAsync`, streams the accumulating
  answer while waiting, and blocks until the turn's terminal state.
- `--timeout` (default 300 s) expiry **interrupts the turn** via the same
  `InterruptAsync` action HTTP uses — persisting the partial answer — and exits with
  the timeout code; Ctrl-C triggers the same interrupt with the cancellation code. No
  agent work continues unsupervised after the CLI exits. (This supersedes the spec's
  earlier "leave running server-side" wording, amended 2026-08-03.)
- CLI-generated conversation ids conform to ADR-014's
  `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`.

### Parser containment and dispatch

- New containment rule **C9**: `Spectre.Console` / `Spectre.Console.Cli` types may be
  referenced only from `Grimoire.Hub.Cli` (and sub-namespaces) and the composition
  root (global-namespace `Program`). Enforced by a structural test with a Red/Green
  probe, like C1–C8.
- `Program.cs` gates once: if `args[0]` matches a `HubCliCommands` catalog name, or
  `--help`/`-h` appears anywhere (017 precedence), run `CommandApp.RunAsync(args)` and
  exit with its code; otherwise the web-host path runs unchanged.
- The root help is produced by a custom Spectre `IHelpProvider` rendering the
  registered commands plus a server-options section generated from
  `PathSwitchCatalog.All` — `BuildUsageText()` retires; the 017 parity test keeps
  passing from the same single source.
- **All commands accept the ADR-009 path switches** (they resolve the data directory
  in-process): a shared `HubPathSettings` settings base declares one option per
  `PathSwitchCatalog.All` entry, parity-tested 1:1; binding still flows through the
  same configuration composition as the web host, preserving the ADR-009 precedence
  chain. `submit-source` migrates its parsing onto the same `CommandApp`, keeping its
  execution and output unchanged.

### Exit codes and console contract

`0` success · `1` operation failed · `2` usage error · `3` not found · `4` state
conflict (incl. `lint.pid` holder conflict and query concurrency limit) · `5` wait
timeout (turn interrupted) · `130` cancelled by interrupt signal (turn interrupted).
Stdout carries exactly the result contract
(`specs/018-hub-cli-commands/contracts/cli-commands.md`); live status renders to
stderr; the `FigletText` logo appears only on root help and web-host startup.

### Telemetry

The CLI process executes the instrumented coordinator code, so the existing signals
fire in-process; the command path MUST dispose the built host before exit so OTLP
export flushes (a deterministic in-memory-exporter test guards this). No new signals
are declared — the CLI adds activation paths, not state transitions.

### Consequences

- Good: no running Hub required; 100% CLI↔HTTP logic parity via shared coordinators
  and the extracted transition service; blocking supervision means no orphaned agent
  children; cross-process "lint already active" detection actually improves on the
  status quo; one library covers parsing, help, status display, and logo; the command
  layer stays thin enough to migrate to a future dedicated hub-CLI.
- Bad: a concurrently running Hub and a CLI invocation share the operational store
  with only targeted (not global) coordination — dual agent runs outside lint remain
  possible and are accepted; commands that trigger agent work can run for minutes
  (inherent to blocking); a new NuGet dependency enters the Hub assembly (contained by
  C9; community-maintained 0.x, mitigated by confinement behind `Grimoire.Hub.Cli`).
- Neutral: HTTP remains the asynchronous/subscribe interaction style; the CLI is the
  blocking one. `submit-source` semantics unchanged.

### Structural enforcement (Principle III)

| Rule | Test |
| --- | --- |
| C9: Spectre.Console* only in `Grimoire.Hub.Cli*` + composition root | new `HubCliContainmentRuleTests` (Red/Green probed) |
| C5 (existing): non-adapter namespaces never reference concrete adapter types | existing rule, unchanged scope |
| N1: `Grimoire.Hub.Cli` in the cross-agent ownership map | amended `AgentArtifactNamingRuleTests` + convention-doc mirror |
| ADR-008: no process-wait in dispatch namespaces | existing `NonBlockingDispatchRuleTests`, unchanged (CLI waits are state observation in `Grimoire.Hub.Cli`) |
| ADR-009: help output ⊇ `PathSwitchCatalog.All`; `HubPathSettings` ⇔ catalog parity | extended `HubHelpUsageTests` + new parity test |
| ADR-018: execution dispatch allow-list | existing `RemediationExecutionDispatchRuleTests`, unchanged (the new transition service does not touch `IAgentProcessLauncher`) |

# Implementation Plan: Hub CLI Command Parity for Write Actions

**Branch**: `018-hub-cli-commands` | **Date**: 2026-08-02, revised 2026-08-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-hub-cli-commands/spec.md` (incl.
Clarifications 2026-08-03: in-process execution, blocking commands, no global
concurrency guard, timeout→interrupt), plus GitHub issue #45's addendum comment
(Spectre.Console.Cli, ASCII-art logo).

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Grow the Hub binary's command surface from one command to eight by adopting
**Spectre.Console.Cli** as the command parser/dispatcher (each command an
`AsyncCommand<TSettings>` class in a new cross-agent `Grimoire.Hub.Cli` namespace) and
executing every command **in-process through the same coordinators the HTTP endpoints
use** — the web host's own composition is built (never run), the command calls the same
coordinator/service method its endpoint handler calls, and blocks until any spawned
agent work reaches a terminal state, optionally streaming live status. HTTP remains
the asynchronous/subscribe path; the CLI is the blocking peer activation path
(Clarification 2026-08-03). The three inline remediation endpoint transitions are
extracted into a shared `RemediationTaskTransitionService` (the one refactor this model
needs). Cross-process "lint run already active" detection is added via an exclusive
`lint.pid` file lock inside `LintRunCoordinator.TriggerAsync` (both entry paths);
`OperationalStateRepository` gains SQLite busy-tolerance for the accepted
no-global-guard concurrency model. `query` streams the answer while waiting and
interrupts the turn on timeout or Ctrl-C via the existing interrupt action. 017's
`BuildUsageText()` retires in favor of a custom help provider rendering commands plus
the `PathSwitchCatalog` server options. All decisions are fixed in the revised
**ADR-019** (must be Accepted before `/speckit-tasks`).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), ASP.NET Core minimal hosting;
top-level statements in `Program.cs` retain the single dispatch gate.

**Primary Dependencies**: **Spectre.Console.Cli 0.55.0 (new)** — central package
management entry in `backend/Directory.Packages.props` + reference in
`Grimoire.Hub.csproj`. No other new packages.

**Storage**: No new stores. Commands operate on the existing SQLite operational state
and Markdown records **in-process via the existing repositories/coordinators**;
`OperationalStateRepository` gains `busy_timeout` + WAL hardening for the accepted
Hub-parallel operation (research D1b). New runtime location: `lint.pid` under the data
directory (ADR-009 registration).

**Testing**: xUnit in `backend/tests/Grimoire.IntegrationTests` (real composed service
graph with temp-dir SQLite + `FakeAgentProcessLauncher`; out-of-process
`HubHelpUsageTests` process-spawn pattern) and `backend/tests/Grimoire.ArchTests`
(new/amended structural rules, Red/Green probed). No live LLM calls anywhere.

**Target Platform**: Same as the Hub — server/dev-machine .NET runtime
(Linux/macOS/Windows), invoked as a CLI process against a Hub data directory. **No
running Hub instance required.**

**Project Type**: Web service with a CLI command surface in the same binary (existing
`Grimoire.Hub` composition root + new `Grimoire.Hub.Cli` namespace) — no new project.

**Performance Goals**: Commands that trigger agent work block for the work's real
duration (minutes are expected and acceptable — operator/cron usage); `query` default
wait bound 300 s (`--timeout`); status streaming keeps the operator informed during
long waits.

**Constraints**: `--help`/`-h` precedence over command execution (FR-011, 017
convention); command stdout carries only the result contract — status/logo never on
stdout during execution (FR-006); no synchronous process-wait added to any dispatch
namespace (ADR-008 `NonBlockingDispatchRuleTests` — CLI waits are state observation in
`Grimoire.Hub.Cli`); ADR-009 precedence chain and `PathSwitchCatalog` single source
preserved; no agent work may continue unsupervised after the CLI exits (timeout/Ctrl-C
→ interrupt, FR-015/FR-016 as clarified); Hub complexity kept low — thin command
layer, one extraction, migratable to a future dedicated hub-CLI.

**Scale/Scope**: 7 new commands + `submit-source` parsing migration; one new namespace
(`Grimoire.Hub.Cli`); one new NuGet dependency; one endpoint-logic extraction
(remediation transitions); `lint.pid` lock + SQLite hardening; 2 new/amended
architecture rules; one new ADR (ADR-019).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal/DDD)**: No new external system: commands consume the same
  in-process coordinators and the existing `IAgentProcessLauncher` port (agent
  spawning stays behind it); no outbound HTTP is introduced. Spectre.Console.Cli is an
  in-process parsing library, contained by new rule C9 to `Grimoire.Hub.Cli*` + the
  composition root; the `lint.pid` path is a persistence/local-filesystem concern
  (port-exempt per the constitution's persistence exemption) registered per ADR-009.
  No Domain Core code touched. **Pass** (gated on ADR-019 acceptance).
- **Principle II (Pragmatic Testing)**: Harness-only feature, tested deterministically
  and hermetically against real infrastructure: the real composed service graph, real
  temp-dir SQLite, real file locks, `FakeAgentProcessLauncher` at the existing port
  for spawned processes. No live LLM calls, no API keys, no mocked doubles of the code
  under test. **Pass** — see Test Strategy.
- **Principle III (ADR-Driven & Test-Enforced)**: All 18 existing ADRs read (see
  Architectural Constraints). New structural boundary → **ADR-019 drafted/revised as
  part of this plan** and MUST reach Accepted before `/speckit-tasks`. Phase 0 of
  `tasks.md` will write the new/amended structural rules (C9, N1 map entry) each with
  a deliberate-violation Red/Green probe. **Pass** (conditional on ADR-019
  acceptance — workflow step 4).
- **Principle IV (Behavioral & Observable Engineering)**: Observability section below
  declares no new signals with justification — the CLI adds activation paths, not
  state transitions; the existing declared signals now fire in the CLI process and a
  deterministic flush test proves they survive process exit (research D8). New
  structural rules get CI enforcement via the existing ArchTests project in the
  standard PR pipeline. No new infrastructure beyond the ADR-gated library and the
  ADR-009-registered `lint.pid` location. **Pass.**
- **Principle V (Agentic Core & Deterministic Harness)**: No agentic surface — the CLI
  transports operator intent, triggers the existing guarded agent pipeline, and prints
  agent output verbatim; no wiki-content judgment enters backend code. **Pass.**

No violations requiring Complexity Tracking justification.

**Post-Phase-1 re-check**: design artifacts (data-model.md, contracts/, revised
ADR-019) introduce no additional boundaries beyond those declared above — still
passing.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

All 18 ADRs read. Constraining this feature:

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-002 | Ingest Agent Execution Model | Agents remain child processes spawned per unit of work; the CLI triggers them only through the existing coordinators/launcher port — no second spawn path. |
| ADR-003 | Domain vs. Operational State Persistence | The CLI process becomes a deliberate second operational-state writer (clarified: no global guard). Requires SQLite busy-tolerance hardening (D1b) and per-operation locks where invariants demand (D1a); rows/records must remain byte-identical to HTTP-produced ones (SC-005). |
| ADR-004 | Credential Scoping | The CLI loads secrets only via the existing spawn path (`LocalSecretsLoader` → agent env); never prints or widens exposure. |
| ADR-005 | Observability Backend | Existing signals now fire in the CLI process: same in-memory-exporter test pattern applies; host disposal must flush OTLP before exit (D8). No new signal rows declared. |
| ADR-008 | Agent Event Channel & Run Supervision | Run-to-exit awaiting is explicitly permitted on the manual CLI path (the `submit-source` precedent this feature generalizes). `NonBlockingDispatchRuleTests` stays intact: CLI waits are state observation in `Grimoire.Hub.Cli`, not process-waits in dispatch namespaces. Fresh-process bootstrap runs `RestartReconciler` → paused-queue-after-restart semantics apply identically. |
| ADR-009 | Runtime Path Configuration | All commands accept the path switches (`HubPathSettings` ⇔ `PathSwitchCatalog` parity-tested); binding flows through the same configuration composition, preserving precedence; `lint.pid` is a new runtime location registered in `GrimoirePathOptions`/`ResolvedGrimoirePaths`; root help keeps listing every path switch. |
| ADR-010 | Hexagonal Ports & Adapter Namespaces | No new port needed (no new external system); new containment rule C9 confines Spectre.Console to `Grimoire.Hub.Cli*` + composition root; C5 unchanged. |
| ADR-011 | Query Runtime & Concurrency | Over-limit submission is rejected, never queued — surfaced as conflict (exit 4). Terminal states completed/failed/interrupted; interrupt reuses `InterruptAsync`; the liveness watchdog runs in the CLI's coordinator instance. |
| ADR-013 | Packaging & Agent-Artifact Naming | `Grimoire.Hub.Cli` added to the N1 Hub namespace-ownership map (cross-agent) and the convention doc's mirror; agent-token command names permitted there (Realtime precedent). |
| ADR-014 | Query Conversation Records | CLI submits `{prompt}` only; conversation ids (given or generated) must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`; the durable record append coincides with the terminal state the CLI waits for. |
| ADR-015 | Query Write Scope & Cross-Process Write Coordination | Precedent for the `lint.pid` exclusive-file-lock mechanism; wiki writes stay behind the guarded tool layer — the CLI never writes wiki content. |
| ADR-018 | Remediation Authorization & Execution | The three remediation commands map 1:1 to the human-permitted transitions; extraction into `RemediationTaskTransitionService` must preserve CAS semantics, publishes, metrics, and record appends; `Authorized→Executing` stays coordinator-only (`RemediationExecutionDispatchRuleTests` unchanged). |
| **ADR-019** | **Hub CLI Command Surface (drafted by this plan, revised 2026-08-03)** | Fixes framework (Spectre.Console.Cli 0.55.0), namespace & ownership, containment (C9), dispatch rule & ADR-009 coexistence, in-process blocking execution model incl. the remediation extraction, `lint.pid` lock, SQLite hardening, timeout/cancel→interrupt contract, telemetry flush, exit codes, logo placement. |

ADR-001/-006/-007/-012/-016/-017 read and confirmed not to constrain beyond the above:
no new language/runtime, no guarded-tool or instruction-surface change, no eval-runner
change; lint/log format guards apply to agent writes the CLI merely triggers.

**New ADR required?**: **Yes — drafted**: [docs/adr/ADR-019-hub-cli-command-surface.md](../../docs/adr/ADR-019-hub-cli-command-surface.md)
(status: proposed). Per the constitution's workflow step 4, it MUST reach **Accepted**
(review or explicit author sign-off) before `/speckit-tasks` is invoked.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. Every capability is a new *entry point* to
existing harness operations:

| Capability | Side | Where it lives |
| --- | --- | --- |
| Command parsing, validation, help, exit codes, status rendering | Harness | `Grimoire.Hub.Cli` (Spectre command classes) |
| In-process invocation of existing coordinators | Harness | `Grimoire.Hub.Cli` commands → `LintRunCoordinator`, `IngestRunCoordinator`, `QueryRunCoordinator`, `RemediationTaskTransitionService` |
| Remediation transition extraction | Harness | `Grimoire.Hub.RemediationTasks.RemediationTaskTransitionService` (moves existing handler logic, adds none) |
| `lint.pid` cross-process run lock | Harness | `Grimoire.Hub.LintDispatch.LintRunCoordinator` |
| `query` prompt transport & verbatim answer streaming/printing | Harness | `QueryCommand` — the answer's *content* remains Query-agent judgment under its existing instruction files; the CLI never edits it |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

All success criteria are deterministic harness guarantees (100% tier) — the spec
defines no agent-judgment thresholds, so no evaluation tests are required.

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| SC-001: all 7 commands runnable from terminal/script, no web UI, no hand-rolled HTTP | Deterministic guarantee | Hermetic integration tests: command classes executed against the real composed service graph (temp-dir SQLite, real file locks, `FakeAgentProcessLauncher`); one out-of-process spawn test per the `HubHelpUsageTests` pattern proving the built binary dispatches commands and never binds a port | `FakeAgentProcessLauncher` (existing port fake); spawned real `Grimoire.Hub.dll` | Seeded remediation/ingest rows; scripted agent runs | No running Hub, no HTTP anywhere in the test path — matches the clarified model |
| SC-002: 100% of usage-error / not-found / conflict invocations → specific message + non-zero exit | Deterministic guarantee | Hermetic integration tests asserting message and mapped exit code for every failure class of every command (matrix from [contracts/cli-commands.md](./contracts/cli-commands.md)); Spectre validation errors asserted out-of-process for missing args and unknown commands | Same harness | Seeded rows in each wrong state; unknown ids; missing args | The contract table is the test matrix; includes the cross-process `lint.pid` conflict (two harness instances) |
| SC-003: 100% of successful invocations → result line with id + state, exit 0 | Deterministic guarantee | Hermetic integration tests asserting the exact success line format per command, including blocking behavior (command returns only after scripted terminal state) | Same harness | Scripted happy-path agent runs | Output format frozen in the contract; status stream goes to stderr, stdout stays contract-only |
| SC-004: all 7 commands listed in `--help` with purpose + args | Deterministic guarantee | Extended `HubHelpUsageTests` (out-of-process): root help contains all 8 command names + descriptions + every `PathSwitchCatalog.All` switch; per-command `--help` contains its arguments; `HubPathSettings`⇔`PathSwitchCatalog` parity test | Spawned real binary; in-process parity assertion | Command catalog itself (single source of truth) | Replaces 017's `BuildUsageText` parity guarantee under the new help provider |
| SC-005: CLI-produced state change indistinguishable from HTTP-produced | Deterministic guarantee | Parity integration tests: perform each action once via the endpoint handler path and once via the command class against identically seeded harnesses; assert identical repository rows, record content, and coordinator responses (incl. the extracted `RemediationTaskTransitionService` serving both) | Same harness ×2 | Identical seeds per pair | Parity holds by construction (same methods); the test guards against future divergence |
| SC-006: timeout vs. cancelled distinguishable; turn interrupted in both cases, never left unsupervised | Deterministic guarantee | Hermetic integration tests: (a) scripted never-completing turn + short `--timeout` → timeout message, exit 5, `InterruptAsync` invoked, turn terminal `interrupted`, partial answer persisted; (b) cancellation token fired mid-wait → cancellation message, exit 130, same interrupt path; (c) scripted failing turn → failure reason, exit 1 | `FakeAgentProcessLauncher` (scripted chunk/terminal sequences) | Scripted agent event sequences incl. partial answers | Ctrl-C OS-signal glue is thin and code-reviewed; the cancellation *path* is what's tested |

Additional deterministic guarantees from the clarified model (not spec SCs but plan
obligations): SQLite dual-writer tolerance test (D1b) and telemetry flush test (D8),
both hermetic in `Grimoire.IntegrationTests`.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

**No new signals — N/A with justification** (017 precedent, strengthened):

- The CLI adds **activation paths, not state transitions**: every business transition a
  command can cause already has its declared, integration-tested signal from the
  feature that introduced it (`wiki.lint.runs_total`, `wiki.lint.triggers_rejected_total`,
  `hub.lint.trigger`; `wiki.remediation.tasks_authorized_total`/`…dismissed_total`/
  `…withdrawn_total`, `hub.remediation.authorize`, `RemediationLifecycleLogEvents`;
  `ingest.queue.resumed`; `query.turns_total`, `query.submissions_rejected_total`,
  `hub.query.submit`, `QueryLifecycleLogEvents`).
- Under the in-process model these signals **fire in the CLI process itself** (the
  instrumented coordinator code runs there). Two derived obligations are carried into
  `tasks.md` as implementation + test work (without declaring new rows):
  1. the command path bootstraps the same telemetry registration as the web host and
     disposes the built host before exit so OTLP export flushes;
  2. a deterministic in-memory-exporter test (ADR-005 pattern) asserts a CLI-invoked
     flow still records its existing signals end-to-end — proving flush, not new
     identities.
- The extracted `RemediationTaskTransitionService` moves the existing
  metric/log/publish calls verbatim; the SC-005 parity tests plus the existing
  remediation observability tests guard that no signal is lost in the move.

### Business Metrics (OpenTelemetry Counters / Gauges)

None — N/A per justification above.

### Structured Log Events

None — N/A per justification above.

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory fields.
3. CI task(s) ensuring those logging tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

None — N/A per justification above.

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) that create the span with declared parent/child linkage and required attributes.
2. Deterministic integration test task(s) validating span name, parent/child relationship, and correlation attributes.
3. CI task(s) ensuring those trace tests run in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/018-hub-cli-commands/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/
│   └── cli-commands.md  # Phase 1 output — full command grammar & exit-code contract
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/Grimoire.Hub/
├── Program.cs                          # Dispatch gate: catalog command or --help →
│                                       #   CommandApp; else unchanged web-host path
├── Cli/                                # NEW namespace Grimoire.Hub.Cli (cross-agent)
│   ├── HubCliCommands.cs               # Catalog: name/description/type — single source
│   ├── HubCliApp.cs                    # CommandApp composition over the built host's
│   │                                   #   services (type registrar), exit-code mapping
│   ├── HubPathSettings.cs              # Shared settings mirroring PathSwitchCatalog
│   ├── HubCliHelpProvider.cs           # Root help: FigletText + commands + path switches
│   ├── CliStatusRenderer.cs            # Live status/event stream to stderr
│   ├── LintRunCommand.cs
│   ├── RemediationAuthorizeCommand.cs
│   ├── RemediationDismissCommand.cs
│   ├── RemediationWithdrawCommand.cs
│   ├── IngestRetriggerCommand.cs
│   ├── IngestResumeCommand.cs
│   ├── QueryCommand.cs                 # Submit → stream → terminal; timeout/Ctrl-C →
│   │                                   #   InterruptAsync
│   └── SubmitSourceCommand.cs          # Migrated parsing; in-process execution as today
├── LintDispatch/LintRunCoordinator.cs  # AMEND: exclusive lint.pid lock in TriggerAsync
├── RemediationTasks/
│   ├── RemediationTaskEndpoints.cs     # AMEND: handlers delegate to the service
│   └── RemediationTaskTransitionService.cs  # NEW: extracted authorize/dismiss/withdraw
├── OperationalState/OperationalStateRepository.cs  # AMEND: busy_timeout + WAL
└── Runtime/Paths/                      # AMEND: lint.pid location in GrimoirePathOptions/
                                        #   ResolvedGrimoirePaths (+ PathSwitchCatalog if
                                        #   a switch is warranted)

backend/tests/Grimoire.ArchTests/
├── HubCliContainmentRuleTests.cs       # NEW: C9 Spectre containment (Red/Green probed)
└── AgentArtifactNamingRuleTests.cs     # AMEND: Grimoire.Hub.Cli → cross-agent map
                                        #   (+ docs/conventions/agent-artifact-naming.md)

backend/tests/Grimoire.IntegrationTests/
├── HubCliCommandTests.cs               # NEW: per-command success/failure matrix, blocking
├── HubCliQueryCommandTests.cs          # NEW: stream/timeout-interrupt/cancel-interrupt
├── HubCliParityTests.cs                # NEW: SC-005 CLI-vs-endpoint row/record parity
├── HubCliConcurrencyTests.cs           # NEW: lint.pid cross-instance conflict; SQLite
│                                       #   dual-writer tolerance; telemetry flush
└── HubHelpUsageTests.cs                # EXTEND: 8 commands in root help; per-command
                                        #   help; HubPathSettings⇔PathSwitchCatalog parity

backend/Directory.Packages.props        # + Spectre.Console.Cli 0.55.0
backend/src/Grimoire.Hub/Grimoire.Hub.csproj  # + PackageReference

docs/adr/ADR-019-hub-cli-command-surface.md   # Drafted by this plan (proposed → Accepted gate)
docs/conventions/agent-artifact-naming.md     # AMEND: Cli namespace entry (doc↔fixture mirror)
```

**Structure Decision**: No new project. The command surface lives in the existing
`Grimoire.Hub` binary under one new namespace (`Grimoire.Hub.Cli`), consistent with
ADR-013's namespace-ownership map; the only orchestration-code changes outside `Cli/`
are the remediation-transition extraction, the `lint.pid` lock, and the SQLite
hardening — each shared verbatim by the HTTP path, per the "same flow" clarification.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table not applicable.

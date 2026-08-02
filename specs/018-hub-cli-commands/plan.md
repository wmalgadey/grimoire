# Implementation Plan: Hub CLI Command Parity for Write Actions

**Branch**: `018-hub-cli-commands` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-hub-cli-commands/spec.md`, plus GitHub
issue #45's addendum comment (2026-08-02) requesting a command-pattern CLI framework
(Spectre.Console.Cli preferred) and an ASCII-art logo.

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Grow the Hub binary's command surface from one command to eight by adopting
**Spectre.Console.Cli** as the command parser/dispatcher (each command a
`AsyncCommand<TSettings>` class in a new cross-agent `Grimoire.Hub.Cli` namespace) and
executing the seven new commands as **thin HTTP clients against the running Hub's
existing endpoints** — the only model that preserves 100% CLI↔HTTP parity (SC-005),
respects ADR-003's single-writer operational state, and avoids orphaning
fire-and-forget agent supervision. The blocking `query` command submits a turn, polls
the existing turn-state endpoint until a terminal state (default timeout 300 s,
`--timeout` override), and calls the existing interrupt endpoint on Ctrl-C. HTTP access
goes through a new `IHubApiClient` port with a `HubHttpApiClient` adapter in
`Grimoire.Hub.Cli.Adapters.HubHttp`; hermetic tests run the same adapter against a
TestServer-hosted real Hub. 017's `BuildUsageText()` retires in favor of a custom help
provider that renders registered commands plus a `PathSwitchCatalog`-generated server
options section, keeping the single-source-of-truth discipline. All decisions are fixed
in the newly drafted **ADR-019** (must be Accepted before `/speckit-tasks`).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), ASP.NET Core minimal hosting;
top-level statements in `Program.cs` retain the single dispatch gate.

**Primary Dependencies**: **Spectre.Console.Cli 0.55.0 (new)** — central package
management entry in `backend/Directory.Packages.props` + reference in
`Grimoire.Hub.csproj`. No other new packages; remote commands use `System.Net.Http`
(BCL) via the `HubHttpApiClient` adapter.

**Storage**: None new. The CLI process touches neither SQLite nor the filesystem data
directories for the seven remote commands (ADR-003 single-writer preserved);
`submit-source` keeps its existing in-process storage access unchanged.

**Testing**: xUnit in `backend/tests/Grimoire.IntegrationTests` (TestServer harness
with real temp-dir SQLite + `FakeAgentProcessLauncher`, plus the out-of-process
`HubHelpUsageTests` process-spawn pattern) and `backend/tests/Grimoire.ArchTests`
(new/amended structural rules, Red/Green probed). No live LLM calls anywhere.

**Target Platform**: Same as the Hub — server/dev-machine .NET runtime
(Linux/macOS/Windows), invoked as a CLI process; remote commands additionally require a
reachable running Hub instance (`--hub-url`, default `http://localhost:5255`).

**Project Type**: Web service with a CLI command surface in the same binary (existing
`Grimoire.Hub` composition root + new `Grimoire.Hub.Cli` namespace) — no new project.

**Performance Goals**: `query` polls turn state at 1 s intervals; default wait bound
300 s. Other commands are single round-trips; no throughput targets apply.

**Constraints**: `--help`/`-h` precedence over command execution (FR-011, 017
convention); command stdout must stay script-parseable — one result line, logo never on
command output (FR-006); no synchronous wait added to any dispatch namespace
(ADR-008 `NonBlockingDispatchRuleTests`); no second writer on operational state
(ADR-003); ADR-009 precedence chain untouched on the web-host path; timeout must not
cancel the turn server-side, Ctrl-C must (FR-015/FR-016).

**Scale/Scope**: 7 new commands + `submit-source` parsing migration; one new namespace
(`Grimoire.Hub.Cli` + `.Adapters.HubHttp`); one new NuGet dependency; ~4 new/amended
architecture rules; one new ADR (ADR-019).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal/DDD)**: The running Hub's HTTP API becomes an external
  dependency of the CLI surface → consumed through the new **`IHubApiClient` port**
  owned by the consuming namespace `Grimoire.Hub.Cli`, with production adapter
  `HubHttpApiClient` confined to `Grimoire.Hub.Cli.Adapters.HubHttp` (ADR-010 C3
  amended; new C9 confines Spectre.Console). Port, adapter namespace, and containment
  rules are named in ADR-019 before implementation, as Principle I requires. No Domain
  Core code touched. **Pass** (gated on ADR-019 acceptance).
- **Principle II (Pragmatic Testing)**: Harness-only feature, tested deterministically
  and hermetically: the production HTTP adapter runs against a TestServer-hosted real
  Hub (real endpoints, real temp-dir SQLite, `FakeAgentProcessLauncher` for the spawned
  process boundary — an existing port fake). No live LLM calls, no API keys, no mocked
  doubles of the code under test. **Pass** — see Test Strategy.
- **Principle III (ADR-Driven & Test-Enforced)**: All 18 existing ADRs read (see
  Architectural Constraints). This feature introduces a new structural boundary →
  **ADR-019 drafted as part of this plan** and MUST reach Accepted before
  `/speckit-tasks`. Phase 0 of `tasks.md` will write the new/amended structural rules
  (C9, C3 amendment, N1 map entry) each with a deliberate-violation Red/Green probe.
  **Pass** (conditional on ADR-019 acceptance — workflow step 4).
- **Principle IV (Behavioral & Observable Engineering)**: Observability section below
  declares no new signals, with justification: every business signal for the triggered
  actions already exists and is already tested server-side; the CLI's own contract
  (stdout + exit code) is CI-gated by deterministic integration tests. New structural
  rules get CI enforcement via the existing ArchTests project in the standard PR
  pipeline. No new infrastructure (Spectre.Console.Cli is an in-process library; its
  adoption is ADR-gated via ADR-019 regardless). **Pass.**
- **Principle V (Agentic Core & Deterministic Harness)**: No agentic surface — the CLI
  transports operator intent and prints agent output verbatim; no wiki-content judgment
  enters backend code. `query` reuses the guarded agent pipeline end-to-end. **Pass.**

No violations requiring Complexity Tracking justification.

**Post-Phase-1 re-check**: design artifacts (data-model.md, contracts/, ADR-019)
introduce no additional boundaries beyond those declared above — still passing.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

All 18 ADRs read. Constraining this feature:

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-003 | Domain vs. Operational State Persistence | SQLite operational store is single-writer (the running Hub). Forces the remote-execution model: CLI commands MUST NOT open the state DB from a second process. |
| ADR-005 | Observability Backend | Any declared signal needs in-memory-exporter integration tests. This feature declares none (justified below); existing server-side signals continue to cover the triggered actions. |
| ADR-008 | Agent Event Channel & Run Supervision | `NonBlockingDispatchRuleTests` forbids synchronous waits in dispatch namespaces — `query`'s wait is client-side polling in `Grimoire.Hub.Cli`, leaving the rule intact. `ingest-resume`/`ingest-retrigger` map to this ADR's resume/re-trigger operations via their existing endpoints. |
| ADR-009 | Runtime Path Configuration | Web-host path parsing unchanged; `PathSwitchCatalog` stays single source of truth; root help must keep listing every path switch; `submit-source`'s Spectre settings are parity-tested against the catalog; remote commands add `--hub-url` (not a path switch). |
| ADR-010 | Hexagonal Ports & Adapter Namespaces | New port/adapter must follow context-nested layout: `IHubApiClient` in `Grimoire.Hub.Cli`, adapter in `Grimoire.Hub.Cli.Adapters.HubHttp`; rule C3 (outbound HTTP) amended, new C9 (Spectre containment); C5 continues to apply. |
| ADR-011 | Query Runtime & Concurrency | Over-limit submission is rejected (503), never queued — `query` surfaces it as a conflict (exit 4), no retry loop. Terminal states: completed/failed/interrupted. Interrupt reuses the existing action. |
| ADR-013 | Packaging & Agent-Artifact Naming | `Grimoire.Hub.Cli` must be added to the N1 Hub namespace-ownership map (cross-agent) and the convention doc's mirror; agent-token command names are permitted there (Realtime precedent). |
| ADR-014 | Query Conversation Records | CLI submits `{prompt}` only; `--conversation-id` (and generated ids) must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`; one-active-turn 409 surfaced as conflict; terminal state coincides with durable record append. |
| ADR-018 | Remediation Authorization & Execution | The three remediation commands map 1:1 to the human-permitted transitions (authorize/dismiss/withdraw); `Authorized→Executing` stays coordinator-only. Driving the existing endpoints keeps `RemediationExecutionDispatchRuleTests`'s allow-list untouched. |
| **ADR-019** | **Hub CLI Command Surface (drafted by this plan)** | Fixes framework (Spectre.Console.Cli 0.55.0), namespace & ownership, port/adapter & containment (C9, C3 amendment), dispatch rule & ADR-009 coexistence, remote execution model, `query` wait/cancellation semantics, exit codes, logo placement. |

ADR-001/-002/-004/-006/-007/-012/-015/-016/-017 read and confirmed not to constrain:
no new language/runtime, no agent spawning from command handlers (ADR-002/-018 enforced
by driving endpoints), no credential exposure (ADR-004: the CLI never reads the secrets
file for remote commands), no guarded-tool or instruction-surface change
(ADR-006/-007/-015/-016/-017), no eval-runner change (ADR-012).

**New ADR required?**: **Yes — drafted**: [docs/adr/ADR-019-hub-cli-command-surface.md](../../docs/adr/ADR-019-hub-cli-command-surface.md)
(status: proposed). Per the constitution's workflow step 4, it MUST reach **Accepted**
(review or explicit author sign-off) before `/speckit-tasks` is invoked.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. Every capability is a new *entry point* to
existing harness operations:

| Capability | Side | Where it lives |
| --- | --- | --- |
| Command parsing, validation, help, exit codes | Harness | `Grimoire.Hub.Cli` (Spectre command classes) |
| Remote invocation of existing endpoints | Harness | `Grimoire.Hub.Cli.Adapters.HubHttp.HubHttpApiClient` |
| `query` prompt transport & verbatim answer printing | Harness | `QueryCommand` — the answer's *content* remains Query-agent judgment under its existing instruction files; the CLI never edits it |
| Remediation authorize/dismiss/withdraw | Harness | Existing endpoints/state machine (ADR-018) — proposal content and execution judgment stay agent-side, unchanged |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

All success criteria are deterministic harness guarantees (100% tier) — the spec
defines no agent-judgment thresholds, so no evaluation tests are required.

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| SC-001: all 7 commands runnable from terminal/script, no web UI, no hand-rolled HTTP | Deterministic guarantee | Hermetic integration tests: command classes executed against the production `HubHttpApiClient` backed by a TestServer-hosted real Hub; plus one out-of-process spawn test per the `HubHelpUsageTests` pattern proving the built binary dispatches commands without starting the web host | TestServer (real endpoints), real temp-dir SQLite, `FakeAgentProcessLauncher`; spawned real `Grimoire.Hub.dll` | Seeded remediation/ingest task rows; scripted agent runs | Proves each command end-to-end without frontend or curl |
| SC-002: 100% of usage-error / not-found / conflict invocations → specific message + non-zero exit | Deterministic guarantee | Hermetic integration tests asserting stderr/stdout message and mapped exit code for every failure class of every command (matrix from [contracts/cli-commands.md](./contracts/cli-commands.md)); Spectre validation errors asserted out-of-process for missing args and unknown commands | Same harness | Seeded rows in each wrong state; unknown ids; missing args | One test row per failure class per command — the contract table is the test matrix |
| SC-003: 100% of successful invocations → result line with id + state, exit 0 | Deterministic guarantee | Hermetic integration tests asserting the exact success line format per command | Same harness | Seeded happy-path rows | Output format frozen in the contract |
| SC-004: all 7 commands listed in `--help` with purpose + args | Deterministic guarantee | Extended `HubHelpUsageTests` (out-of-process): root help contains all 8 command names + descriptions + every `PathSwitchCatalog.All` switch; per-command `--help` contains its arguments; `HubPathSettings`⇔`PathSwitchCatalog` parity test | Spawned real binary; in-process parity assertion | Command catalog itself (single source of truth — no separate fixture) | Replaces 017's `BuildUsageText` parity guarantee under the new help provider |
| SC-005: CLI-produced state change indistinguishable from HTTP-produced | Deterministic guarantee | Parity integration tests: perform each action once via CLI command class and once via direct `HttpClient` call against identical seeded harnesses; assert identical repository rows / coordinator responses | Same harness ×2 | Identical seeds per pair | Parity holds by construction (same endpoints); test guards against future divergence |
| SC-006: timeout vs. cancelled vs. other failures distinguishable, with correct server-side effect | Deterministic guarantee | Hermetic integration tests: (a) scripted never-completing turn + short `--timeout` → timeout message, exit 5, turn still running, no interrupt recorded; (b) cancellation token fired mid-wait → interrupt endpoint called, turn `interrupted`, exit 130; (c) scripted failing turn → failure reason, exit 1 | TestServer harness, `FakeAgentProcessLauncher` (`autoPlay: false` / scripted terminal states), fake-clock-free short timeouts | Scripted agent event sequences | Ctrl-C OS-signal glue is thin and code-reviewed; the cancellation *path* is what's tested |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

**No new signals — N/A with justification** (mirrors the accepted 017 precedent):

- The CLI process is a one-shot console client; its entire externally observable
  contract — stdout result lines and exit codes — is specified in
  [contracts/cli-commands.md](./contracts/cli-commands.md) and CI-gated by the
  deterministic tests above.
- Every business signal for the actions triggered already exists server-side and is
  already covered by in-memory-exporter tests from prior features:
  `wiki.lint.runs_total`, `wiki.lint.triggers_rejected_total`, `hub.lint.trigger`
  (lint); `wiki.remediation.tasks_authorized_total`/`…dismissed_total`/`…withdrawn_total`,
  `hub.remediation.authorize`, `RemediationLifecycleLogEvents` (remediation);
  `ingest.queue.resumed` (ingest); `query.turns_total`,
  `query.submissions_rejected_total`, `hub.query.submit`, `QueryLifecycleLogEvents`
  (query). Because the CLI drives the same endpoints, these fire identically for CLI
  and frontend traffic, and ASP.NET Core instrumentation records each CLI-issued
  request without new code.
- The CLI sends `User-Agent: grimoire-hub-cli/{version}`, keeping CLI-originated
  requests distinguishable in existing server-side traces.
- Emitting OTLP from the short-lived client would add exporter-flush plumbing for
  signals with no consumer; rejected as instrumentation theater.

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
│   ├── HubCliApp.cs                    # CommandApp composition, exit-code mapping
│   ├── HubApiSettings.cs               # Shared settings: --hub-url
│   ├── HubPathSettings.cs              # Shared settings mirroring PathSwitchCatalog
│   ├── HubCliHelpProvider.cs           # Root help: FigletText + commands + path switches
│   ├── IHubApiClient.cs                # Port + typed result records
│   ├── LintRunCommand.cs
│   ├── RemediationAuthorizeCommand.cs
│   ├── RemediationDismissCommand.cs
│   ├── RemediationWithdrawCommand.cs
│   ├── IngestRetriggerCommand.cs
│   ├── IngestResumeCommand.cs
│   ├── QueryCommand.cs                 # Submit → poll → print; timeout & Ctrl-C paths
│   ├── SubmitSourceCommand.cs          # Migrated parsing; in-process execution as today
│   └── Adapters/
│       └── HubHttp/
│           └── HubHttpApiClient.cs     # Only System.Net.Http consumer in the CLI surface
└── Runtime/Paths/PathSwitchCatalog.cs  # Unchanged (single source for path switches)

backend/tests/Grimoire.ArchTests/
├── HubCliContainmentRuleTests.cs       # NEW: C9 Spectre containment (Red/Green probed)
├── HexagonalPortsAdapterRuleTests.cs   # AMEND: C3 + Grimoire.Hub.Cli.Adapters.HubHttp
└── AgentArtifactNamingRuleTests.cs     # AMEND: Grimoire.Hub.Cli → cross-agent map
                                        #   (+ docs/conventions/agent-artifact-naming.md)

backend/tests/Grimoire.IntegrationTests/
├── HubCliCommandTests.cs               # NEW: per-command success/failure matrix (TestServer)
├── HubCliQueryCommandTests.cs          # NEW: wait/timeout/cancel/failure paths
├── HubCliParityTests.cs                # NEW: SC-005 CLI-vs-HTTP row parity
└── HubHelpUsageTests.cs                # EXTEND: 8 commands in root help; per-command help;
                                        #   HubPathSettings⇔PathSwitchCatalog parity

backend/Directory.Packages.props        # + Spectre.Console.Cli 0.55.0
backend/src/Grimoire.Hub/Grimoire.Hub.csproj  # + PackageReference

docs/adr/ADR-019-hub-cli-command-surface.md   # Drafted by this plan (proposed → Accepted gate)
docs/conventions/agent-artifact-naming.md     # AMEND: Cli namespace entry (doc↔fixture mirror)
```

**Structure Decision**: No new project. The command surface lives in the existing
`Grimoire.Hub` binary under one new namespace (`Grimoire.Hub.Cli` +
`.Adapters.HubHttp`), consistent with ADR-010's context-nested adapter layout and
ADR-013's namespace-ownership map; tests extend the two existing test projects.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table not applicable.

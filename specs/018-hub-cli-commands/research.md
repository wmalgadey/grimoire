# Research: Hub CLI Command Parity for Write Actions

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02

**Input**: [spec.md](./spec.md), GitHub issue #45 and its addendum comment
(2026-08-02, requesting a real CLI framework — Spectre.Console.Cli preferred — plus an
ASCII-art logo), all 18 ADRs in `docs/adr/`, and the current Hub code base.

## D1: Execution model — thin HTTP client against the running Hub

**Decision**: The seven new commands (`lint-run`, `remediation-authorize`,
`remediation-dismiss`, `remediation-withdraw`, `ingest-retrigger`, `ingest-resume`,
`query`) execute as **thin HTTP clients against a running Hub instance**, calling the
exact endpoints the frontend already uses. They do not bootstrap path resolution,
SQLite, or coordinators in their own process.

| Command | HTTP call(s) |
| --- | --- |
| `lint-run` | `POST /api/lint-runs/` |
| `remediation-authorize` | `POST /api/remediation-tasks/{taskId}/authorize` |
| `remediation-dismiss` | `POST /api/remediation-tasks/{taskId}/dismiss` |
| `remediation-withdraw` | `POST /api/remediation-tasks/{taskId}/withdraw-authorization` |
| `ingest-retrigger` | `POST /api/ingest-submissions/{taskId}/retrigger` |
| `ingest-resume` | `POST /api/ingest-queue/resume` |
| `query` | `POST /api/query-conversations/{conversationId}/turns`, then poll `GET /api/query-turns/{turnId}` until terminal; `POST /api/query-turns/{turnId}/interrupt` on Ctrl-C |

**Rationale**:

- **ADR-003 single-writer operational state**: the SQLite state store is owned by the
  running Hub. A second short-lived process opening the same file while the Hub serves
  would be a second writer, and `OperationalStateRepository` opens plain connections
  with no WAL/`busy_timeout` — concurrent writes risk `SQLITE_BUSY`.
- **Coordinator state is in-memory**: the lint single-run slot
  (`LintRunCoordinator._slot`), the query concurrency semaphore and active-turn map
  (`QueryRunCoordinator`), and the eager-dispatch behavior of
  `RemediationRunCoordinator`/`IngestRunCoordinator` live only in the running Hub
  process. A second in-process bootstrap could never observe "lint run already active"
  (US1-S2) or "conversation already active" (US4-S6) — SC-005 parity would be
  structurally unachievable in-process.
- **Orphaned children**: `ingest-resume`, `ingest-retrigger`, and
  `remediation-authorize` all trigger `TryStartNextAsync`, which spawns and supervises
  an agent child process fire-and-forget. A CLI process that exits after the call would
  orphan that child and lose its outcome.
- **FR-005 parity by construction**: driving the literal endpoint means zero duplicated
  logic. The remediation handlers keep their inline transition logic (no extraction
  refactor needed); ADR-018's allow-listed dispatch (`RemediationExecutionDispatchRuleTests`)
  and ADR-008's `NonBlockingDispatchRuleTests` are untouched, because the blocking wait
  in `query` is client-side polling, not a dispatch-path wait.

**New failure mode introduced**: Hub not reachable at the configured URL → the CLI
prints `Cannot reach the Grimoire Hub at {url}. Is the Hub running?` and exits with a
dedicated exit code (see D5). This is consistent with the spec: SC-001 excludes a
running web *UI*, not a running Hub — every conflict scenario in the spec presupposes
live Hub state.

**Alternatives considered**:

- *In-process bootstrap like `submit-source`*: rejected for the four reasons above.
  `submit-source` only works this way because it merely inserts a row and spawns before
  the Hub-side queue takes over.
- *Extract shared transition services + in-process execution*: still leaves the
  in-memory-state blindness, second-writer, and orphaned-child problems; adds a large
  refactor for negative value.
- *Hybrid (HTTP when Hub is up, in-process fallback)*: two code paths to test, and the
  fallback silently changes semantics (paused queues, no eager dispatch). Rejected.

## D2: CLI framework — Spectre.Console.Cli

**Decision**: Adopt **Spectre.Console.Cli 0.55.0** (latest stable on NuGet as of
2026-08-02) as the Hub's command parser/dispatcher. Each command is an
`AsyncCommand<TSettings>` class with declarative `[CommandArgument]`/`[CommandOption]`
settings; commands are registered once on a `CommandApp`, which generates per-command
`--help` — retiring `BuildUsageText()` while preserving 017's single-source-of-truth
principle (each switch declared exactly once, in its settings class).

**Rationale**: The issue's addendum comment (the motivating input for this plan)
explicitly requests a command-pattern CLI library and names Spectre.Console.Cli as the
preferred fit; it also brings `FigletText` for the requested logo (D7) at no extra
dependency cost. A hand-rolled `ParseOption` scan does not scale to 8 commands with
per-command validation — the premise of the comment, confirmed by the current
`Program.cs` structure.

**Alternatives considered**:

- *`System.CommandLine`*: viable and Microsoft-only, but the comment prefers Spectre;
  System.CommandLine's API has also been through repeated breaking redesigns, and it
  has no console-art support for D7.
- *Keep hand-rolled parsing*: rejected — 8 commands × validation × help would triple
  the ad-hoc code 017 just consolidated.

**Packaging**: new `PackageVersion` entry in `backend/Directory.Packages.props` +
`PackageReference` in `Grimoire.Hub.csproj`. Spectre.Console.Cli is a pure in-process
parsing library — not an "external system" in the ADR-010 sense (nothing for a hermetic
test to replace) — but its *containment* is a new structural rule and is fixed by
ADR-019 (see D10).

## D3: Dispatch rule and ADR-009 coexistence

**Decision**: `Program.cs` keeps a single early dispatch gate:

1. A static **`HubCliCommands` catalog** (name → description → command type) is the
   single source of truth for command names. It drives both the `CommandApp`
   registrations and the dispatch check.
2. If `args[0]` matches a catalog name, or `--help`/`-h` appears anywhere in `args`
   (017's precedence rule, FR-011), the process runs `CommandApp.RunAsync(args)` and
   exits with its return code. **The web host never starts.**
3. Otherwise the existing web-host path runs completely unchanged:
   `AddCommandLine(args, PathConfigurationSwitchMappingsFactory())`, ADR-009 precedence
   (CLI > env > appsettings > defaults), `PathSwitchCatalog` untouched.

**Root help**: a custom Spectre `IHelpProvider` renders the command list from the
`CommandApp` registrations and **appends a "Server options" section generated from
`PathSwitchCatalog.All`** — so the 017 parity test (usage text ⊇ every path switch)
keeps passing and path switches remain single-sourced. An unknown `args[0]` command
name falls into the CommandApp, whose unknown-command error satisfies the spec's
unknown-command edge case (usage error, non-zero exit).

**`submit-source` migration**: `submit-source` becomes `SubmitSourceCommand` on the
same `CommandApp` (settings: `--path` required, `--source-kind` optional), keeping its
in-process execution and exact output line `Submitted ingest task: {taskId}`. Its
settings class inherits a shared `HubPathSettings` base declaring the ADR-009 path
switches (parity-tested 1:1 against `PathSwitchCatalog.All`, so the two cannot drift),
because Spectre parses strictly and `submit-source --path x --base-dir y` must keep
working; the command then feeds the original `args` through the same
configuration-composition helper used today, preserving ADR-009 precedence exactly.
The seven remote commands do *not* take path switches — they need no local paths.

## D4: Hub URL resolution for remote commands

**Decision**: Every remote command accepts `--hub-url <url>` (shared
`HubApiSettings` base class). Default resolution order: `--hub-url` >
`GRIMOIRE_HUB_URL` environment variable > `http://localhost:5255`. The default matches
the Hub's `launchSettings.json` `applicationUrl` and the frontend's
`VITE_HUB_ORIGIN` fallback — the project's canonical local address.

## D5: Exit-code convention

**Decision** (spec delegates this to planning):

| Code | Meaning | Trigger |
| --- | --- | --- |
| 0 | Success | 2xx response; `query` turn reached `completed` |
| 1 | Operation failed | `query` turn terminal state `failed`; unexpected 5xx; `conversation_record_unreadable` (500) |
| 2 | Usage error | Unknown command, missing/malformed required argument, empty prompt (Spectre validation — no HTTP call made) |
| 3 | Not found | 404 (unknown task id / run id / turn id) |
| 4 | State conflict | 409 (`lint_run_active`, `unresolved_remediation_tasks`, `task_not_proposed`, `task_not_authorized`, `execution_already_started`, task not in queue, `conversation_already_active`) and 503 `query_concurrency_limit_reached` |
| 5 | Wait timeout | `query` timeout elapsed before terminal state (turn left running server-side) |
| 7 | Hub unreachable | Connection failure to `--hub-url` |
| 130 | Cancelled | Interrupt signal during `query` wait (POSIX 128+SIGINT convention), after the interrupt action was called |

Spectre's default exit codes are overridden where needed (validation/unknown-command
mapped to 2 via the `CommandApp` configuration; command `ExecuteAsync` return values
carry the rest).

## D6: `query` command semantics

**Decision**:

- Submit via `POST .../turns` with `{ "prompt": ... }` only (ADR-014: no prior turns).
- Poll `GET /api/query-turns/{turnId}` at a **1-second interval** until
  `state ∈ {completed, failed, interrupted}` or the timeout elapses.
- `--timeout <seconds>`, integer, default **300** (spec clarification: 5 minutes).
- `--conversation-id` optional; when omitted the CLI generates
  `{utcNow:yyyy-MM-dd}-conv-{Guid:N}` truncated to 40 chars — conforming to the
  ADR-014 regex `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$` and mirroring the Hub's existing id
  conventions (`...-query-...`, `...-lint-...`).
- Ctrl-C (`Console.CancelKeyPress`) → stop polling, `POST .../interrupt`, print a
  cancellation message, exit 130. Timeout → no interrupt call (per FR-015), exit 5.
- Success output (spec clarification): header line
  `Query turn {turnId} in conversation {conversationId}: {state}` followed by the
  `answer` field verbatim. The server-side liveness watchdog (60 s event silence →
  `failed`) already bounds a hung agent independently of the CLI timeout.

## D7: ASCII-art logo (FigletText)

**Decision**: `AnsiConsole.Write(new FigletText("Grimoire"))` is printed in exactly two
places: the **root help** (top of the custom help provider output) and the **web-host
startup banner**. Command execution output stays logo-free — FR-006 requires
script-parseable stdout, so a logo on command output would break `$(grimoire-hub
lint-run)`-style capture. Per-command `--help` output also stays logo-free (compact).

## D8: Observability — no new signals (justified)

**Decision**: This feature declares **no new metrics, log events, or trace spans**.

**Justification** (mirrors the accepted 017 precedent): the CLI process is a one-shot
console client whose entire contract is stdout + exit code, both deterministically
tested. Every business signal for the actions it triggers is already emitted — and
already integration-tested — on the server side of the existing endpoints:
`wiki.lint.runs_total` / `wiki.lint.triggers_rejected_total` / `hub.lint.trigger`,
`wiki.remediation.tasks_authorized_total` / `…dismissed_total` / `…withdrawn_total` /
`hub.remediation.authorize` + `RemediationLifecycleLogEvents`,
`ingest.queue.resumed` log event, `query.turns_total` / `query.submissions_rejected_total`
/ `hub.query.submit` + `QueryLifecycleLogEvents`. Because the CLI drives the same
endpoints over HTTP, ASP.NET Core instrumentation additionally records each CLI-issued
request server-side with no new code. Emitting OTLP telemetry from a short-lived
console client would require exporter flush-on-exit plumbing for signals nobody
consumes; the CLI sets a `User-Agent: grimoire-hub-cli/{version}` header so CLI-issued
requests remain distinguishable in existing server-side traces.

## D9: Test approach

**Decision**: three layers, following existing repo idioms:

1. **Structural (Phase 0, Red/Green probed)** in `Grimoire.ArchTests`: Spectre.Console
   containment to `Grimoire.Hub.Cli*` + composition root; outbound-HTTP containment
   rule (ADR-010 C3) amended to additionally allow `Grimoire.Hub.Cli.Adapters.HubHttp`;
   `Grimoire.Hub.Cli` added to the N1 cross-agent namespace map (+ convention doc
   mirror in `docs/conventions/agent-artifact-naming.md`).
2. **In-process integration** in `Grimoire.IntegrationTests`: the existing
   TestServer harness pattern (`QueryTurnSubmissionApiTests.BuildHostAsync`,
   `RemediationEndpointHostHarness`) hosts the real endpoints with real temp-dir SQLite
   and `FakeAgentProcessLauncher`; command classes execute against the production
   `HubHttpApiClient` backed by the TestServer's `HttpClient`. Asserts output lines,
   exit codes, state transitions, and CLI↔HTTP parity (SC-005) — hermetic, no live
   LLM, real infrastructure per Principle II.
3. **Out-of-process** (extends `HubHelpUsageTests` pattern): spawned real
   `Grimoire.Hub.dll` asserting root help lists all 8 commands + path switches
   (SC-004), unknown command → exit 2, remote command with unreachable Hub → exit 7,
   and no web host start on any command path.

Signal-handling (Ctrl-C) is covered by invoking the command's cancellation path
directly with a `CancellationToken` in-process (the `CancelKeyPress` hookup itself is
thin glue verified by code review, not worth a TTY-signal test harness).

## D10: New structural boundary → ADR-019

**Decision**: This feature introduces a new structural boundary — a multi-command CLI
dispatch surface with a new parser dependency and a new outbound-HTTP consumer — that
no accepted ADR covers (verified: only ADR-009 touches argument handling, and it
governs configuration binding only; 017's plan formally recorded that no ADR governs
CLI parsing). **ADR-019 — Hub CLI Command Surface** is drafted as part of this plan
(`docs/adr/ADR-019-hub-cli-command-surface.md`) and MUST reach Accepted before
`/speckit-tasks`. It fixes: the `Grimoire.Hub.Cli` namespace and its cross-agent
ownership; the `IHubApiClient` port and `Grimoire.Hub.Cli.Adapters.HubHttp` adapter
namespace with its containment rule; Spectre.Console containment; the dispatch rule and
ADR-009 coexistence (D3); the execution model (D1); and the exit-code convention (D5).

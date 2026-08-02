---
status: proposed
---

# ADR-019: Hub CLI Command Surface — Framework, Dispatch, and Remote Execution Model

## Context and Problem Statement

Feature 018 (`specs/018-hub-cli-commands/spec.md`) grows the Hub binary's command
surface from one command (`submit-source`) to eight: `lint-run`,
`remediation-authorize`, `remediation-dismiss`, `remediation-withdraw`,
`ingest-retrigger`, `ingest-resume`, and a blocking `query`, alongside the existing
`submit-source`. GitHub issue #45's addendum comment (2026-08-02) explicitly requests
retiring the hand-rolled parsing (`args.Any(...)` help check, linear `ParseOption`
scan, `args[0]` string-compare dispatch, hand-written `BuildUsageText()`) in favor of a
command-pattern CLI library, naming Spectre.Console.Cli as the preferred fit.

No accepted ADR covers CLI argument parsing or command dispatch: ADR-009 governs only
the *configuration binding* of path switches (stock `AddCommandLine` +
`PathSwitchCatalog`), and feature 017's plan formally recorded "none govern CLI" when
it introduced `--help`. This feature crosses the boundary threshold 017 did not,
in four ways:

1. A multi-command dispatch structure with a **new NuGet parsing dependency** that must
   coexist with ADR-009's configuration-provider parsing over the same `args`.
2. A **second entry point to operations that mutate live coordinator state**. The lint
   single-run slot, the query concurrency semaphore and active-turn map, and the
   ingest/remediation eager-dispatch loops are in-memory state of the running Hub
   process; ADR-003 makes the SQLite operational store single-writer by process.
3. A **blocking wait** (`query` waits for a turn's terminal state) in a codebase whose
   dispatch path is architecturally non-blocking (ADR-008,
   `NonBlockingDispatchRuleTests`), plus a Ctrl-C → interrupt cancellation contract.
4. A new **outbound-HTTP consumer** inside the Hub assembly, currently confined by
   ADR-010 rule C3 to `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch`.

## Decision Drivers

- Spec FR-005/SC-005: CLI and HTTP entry points must drive the *same* logic with 100%
  behavioral parity — including state-conflict detection ("lint run already active",
  "conversation already active") that only exists in the running Hub's memory.
- ADR-003: single-writer operational state; a second process must not open the running
  Hub's SQLite store for writes.
- Fire-and-forget agent supervision (`TryStartNextAsync` in the ingest and remediation
  coordinators) must not be started in a process that exits immediately afterward.
- ADR-009: one composition point and one precedence order for path configuration;
  `PathSwitchCatalog` stays the single source of truth for path switches.
- 017's principle: every command/switch is declared exactly once — name, description,
  and arguments must not be able to drift between parser, dispatcher, and help output.
- Constitution Principle I: new external-system dependencies are consumed through
  ports; infrastructure packages are namespace-contained with Red/Green-probed rules.
- Constitution Principle IV: unattended scriptability — deterministic exit codes,
  clean stdout.
- Solo-maintainer stack economy (ADR-001): prefer one dependency that solves both the
  parsing problem and the requested console identity (ASCII logo) over two.

## Considered Options

### Command execution model

- **O1: Thin HTTP client against the running Hub** — each command calls the existing
  endpoint; `query` polls the existing turn-state endpoint and calls the existing
  interrupt endpoint on cancellation.
- **O2: In-process bootstrap (the `submit-source` pattern)** — resolve paths, open
  SQLite, construct coordinators, execute directly.
- **O3: Hybrid** — HTTP when a Hub is reachable, in-process fallback otherwise.

### CLI framework

- **F1: Spectre.Console.Cli** — command classes (`AsyncCommand<TSettings>`),
  declarative settings, generated per-command help, `FigletText` logo included.
- **F2: System.CommandLine** — Microsoft-only, dependency-light, no console art.
- **F3: Keep hand-rolled parsing** — extend `ParseOption`/`BuildUsageText`.

## Decision Outcome

**Chosen: O1 (thin HTTP client) + F1 (Spectre.Console.Cli 0.55.0)**, structured as
follows. (`submit-source` keeps its in-process execution — it predates this ADR, only
inserts a row and spawns, and does not touch live coordinator state — but its *parsing*
migrates onto the same command framework.)

### Namespace and ownership

- New namespace **`Grimoire.Hub.Cli`** hosts the command surface: the
  `HubCliCommands` catalog, per-command `AsyncCommand<TSettings>` classes and settings,
  the custom help provider, and the exit-code mapping. It is registered as
  **cross-agent** in ADR-013's N1 Hub namespace-ownership map
  (`AgentArtifactNamingRuleTests` + `docs/conventions/agent-artifact-naming.md`); like
  `Grimoire.Hub.Realtime`, it may host agent-token types (`LintRunCommand`,
  `QueryCommand`, …) because commands are per-agent entries of shared infrastructure.

### Port and adapter (Principle I)

- Port **`IHubApiClient`**, declared in `Grimoire.Hub.Cli` (the consuming orchestration
  namespace), exposes one method per remote operation returning typed results that
  mirror the endpoints' success/conflict/not-found shapes.
- Production adapter **`HubHttpApiClient`** in
  **`Grimoire.Hub.Cli.Adapters.HubHttp`** — the only place in the CLI surface that
  touches `System.Net.Http`. It takes an injected `HttpClient`; hermetic tests inject a
  TestServer-backed `HttpClient` so the *same production adapter* runs against the real
  endpoints in-process (real infrastructure per Principle II — no mocked doubles of the
  thing under test).
- ADR-010 rule **C3 is amended**: outbound HTTP in the Hub assembly is permitted in
  exactly two namespaces — `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch` and
  `Grimoire.Hub.Cli.Adapters.HubHttp`.

### Parser containment

- New containment rule **C9**: `Spectre.Console` / `Spectre.Console.Cli` types may be
  referenced only from `Grimoire.Hub.Cli` (and sub-namespaces) and the composition
  root (global-namespace `Program`). Enforced by a structural test with a Red/Green
  probe, like C1–C8.

### Dispatch rule and ADR-009 coexistence

- `Program.cs` gates once, before `WebApplication.CreateBuilder`: if `args[0]` matches
  a `HubCliCommands` catalog name, or `--help`/`-h` appears anywhere (017 precedence,
  FR-011), run `CommandApp.RunAsync(args)` and exit with its code; the web host never
  starts. Otherwise the web-host path runs unchanged — `AddCommandLine` with
  `PathConfigurationSwitchMappingsFactory()`, ADR-009 precedence intact.
- The root help is produced by a custom Spectre `IHelpProvider` that renders the
  registered commands and appends a server-options section generated from
  `PathSwitchCatalog.All` — `BuildUsageText()` retires; the 017 parity test
  (help ⊇ every path switch) continues to pass from the same single source.
- `submit-source` parses via a `SubmitSourceCommand` whose settings inherit a shared
  `HubPathSettings` base declaring the ADR-009 switches (parity-tested 1:1 against
  `PathSwitchCatalog.All`); the command then composes configuration through the same
  factory as the web host, preserving the ADR-009 precedence chain. Remote commands
  take no path switches — only `--hub-url` (default: `GRIMOIRE_HUB_URL`, else
  `http://localhost:5255`, the project's canonical local Hub address).

### Blocking `query` and cancellation

- The wait lives entirely client-side: poll `GET /api/query-turns/{turnId}` (1 s
  interval) until a terminal state, bounded by `--timeout` (default 300 s). No
  synchronous wait is added to any dispatch namespace — `NonBlockingDispatchRuleTests`
  is unaffected. Timeout leaves the turn running (no interrupt); Ctrl-C calls the
  existing interrupt endpoint before exiting. Conversation ids generated by the CLI
  conform to ADR-014's `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`.

### Exit-code convention

`0` success · `1` operation failed (turn `failed`, unexpected 5xx) · `2` usage error ·
`3` not found (404) · `4` state conflict (409/503-busy) · `5` wait timeout ·
`7` Hub unreachable · `130` cancelled by interrupt signal. Stdout carries exactly the
result contract (`specs/018-hub-cli-commands/contracts/cli-commands.md`); the ASCII
logo (`FigletText`) appears only on root help and web-host startup, never on command
execution output.

### Consequences

- Good: 100% CLI↔HTTP parity by construction; no second SQLite writer; no orphaned
  agent children; remediation endpoint logic needs no extraction refactor; the
  non-blocking dispatch rule stays intact; one library covers parsing, help, and logo.
- Bad: remote commands require a running Hub — "Hub unreachable" becomes a documented
  failure mode (exit 7) rather than an impossible state; a new NuGet dependency enters
  the Hub assembly (contained by C9); Spectre.Console.Cli is community-maintained
  (0.x versioning), mitigated by confining it behind `Grimoire.Hub.Cli` so a future
  swap is namespace-local.
- Neutral: `submit-source` retains its in-process semantics; a future decision could
  migrate it to an endpoint + HTTP call, out of scope here.

### Structural enforcement (Principle III)

| Rule | Test |
| --- | --- |
| C9: Spectre.Console* only in `Grimoire.Hub.Cli*` + composition root | new `HubCliContainmentRuleTests` (Red/Green probed) |
| C3 amendment: outbound HTTP also allowed in `Grimoire.Hub.Cli.Adapters.HubHttp` | amended `HexagonalPortsAdapterRuleTests` (Red/Green probed) |
| C5 (existing): non-adapter namespaces never reference concrete adapter types — applies to `HubHttpApiClient` | existing rule, extended fixture |
| N1: `Grimoire.Hub.Cli` in the cross-agent ownership map | amended `AgentArtifactNamingRuleTests` + convention-doc mirror |
| ADR-009: help output ⊇ `PathSwitchCatalog.All`; `HubPathSettings` ⇔ catalog parity | extended `HubHelpUsageTests` + new parity test |

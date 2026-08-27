---
status: accepted
supersedes: ADR-020
---

# ADR-048: Hub CLI Command Framework — Spectre.Console.Cli

## Context and Problem Statement

The Hub binary exposes a multi-command CLI surface (`submit-source`, `lint-run`,
`remediation-authorize`, `remediation-dismiss`, `remediation-withdraw`, `ingest-retrigger`,
`ingest-resume`, `query`, plus the root default command that starts the web host). The
original surface grew out of hand-rolled parsing — an `args.Any(...)` help check, a linear
`ParseOption` scan, `args[0]` string-compare dispatch, and a hand-written `BuildUsageText()`
— which meant every command and switch had to be kept consistent across parser, dispatcher,
and help output by hand. Growing from one command to eight (feature 018, issue #45's
addendum) crossed the threshold where a command-pattern CLI library was warranted. Which
framework parses and dispatches the Hub's CLI, and where that framework's types are allowed
to live, is the single aspect this ADR decides. How the commands execute is ADR-049's
aspect; root/default-command routing and root help rendering are ADR-023's.

## Decision Drivers

- Every command and switch must be declared exactly once — no drift between parser,
  dispatcher, and help output (feature 017's principle, carried forward).
- The framework's parsing must coexist with `Microsoft.Extensions.Configuration`'s
  command-line provider over the same `args`, so the directory switches keep their
  established precedence chain.
- The CLI must render live status/progress (blocking commands report while they wait) and
  generated per-command help.
- A new NuGet parsing dependency enters the Hub assembly and must be contained so the rest
  of the Hub cannot grow a dependency on it (Constitution Principle I adapter-containment
  family).
- Minimal surface for a solo-operator project: one library should cover parsing, help,
  status display, and the console logo.

## Considered Options

1. **Spectre.Console.Cli** — command classes (`AsyncCommand<TSettings>`), declarative
   settings, generated per-command help, and Spectre.Console's status/progress rendering
   and `FigletText` logo for the live-status requirement.
2. **System.CommandLine** — Microsoft-owned and dependency-light, but no console art or
   status display; a second rendering library would still be needed.
3. **Keep hand-rolled parsing** — extend `ParseOption`/`BuildUsageText`; retains the
   drift-by-hand failure mode the change exists to remove.

## Decision Outcome

Chosen option: **Option 1 — Spectre.Console.Cli** (currently `Spectre.Console.Cli` 0.55.0
paired with `Spectre.Console` 0.57.2), because one library covers parsing, per-command
help, status rendering, and the logo, while its command/settings classes give the
single-declaration-point property the hand-rolled surface lacked.

- **`Grimoire.Hub.Cli` hosts the command surface**: the `HubCliCommands` catalog, the
  per-command `AsyncCommand<TSettings>` classes and their settings, the custom help
  provider, the console status renderer, and the exit-code mapping (`CliExitCode`). The
  namespace is registered as **Cross-agent** in ADR-013's N1 Hub namespace-ownership map;
  like `Grimoire.Hub.Realtime`, it may host agent-token type names (`LintRunCommand`,
  `QueryCommand`, …) because commands are per-agent entries of shared infrastructure.
- **One container, one composition point**: Spectre's type registrar resolves command
  dependencies from the Hub's own service container — the framework never becomes a second
  composition root (the execution model behind this is ADR-049's aspect).
- **Directory switches on every command**: a shared `HubPathSettings` settings base
  declares one `[CommandOption]` per `PathSwitchCatalog.All` entry, so every registered
  command accepts the directory switches; binding still flows through the same
  configuration composition as the web host, preserving the per-option precedence chain.
  The catalog's content and cap are owned by the directory-root decisions (ADR-041,
  ADR-024), not here.
- **Boundary Rule — C9, Spectre containment**: `Spectre.Console` / `Spectre.Console.Cli`
  types may be referenced only from `Grimoire.Hub.Cli` (and sub-namespaces) and the
  composition root. Enforced by the existing Red/Green-probed structural test
  `HubCliContainmentRuleTests` (Constitution Principle III).
- **Feature-Scoped Invariant — switch surface renders from the catalog**: the rendered
  `--help` output presents exactly the catalog's directory switches, each with description
  text. Enforced behaviorally, out-of-process, by `HubHelpUsageTests` — ADR-032 owns the
  decision that this invariant is proven by the rendered help, never by reflection over
  settings types.

### Consequences

- Good, because one framework covers parsing, generated help, status rendering, and the
  logo, and each command/switch is declared exactly once in a settings class.
- Good, because commands stay thin declarative classes over shared services, so the
  command layer could migrate to a future dedicated hub-CLI without rework.
- Bad, because a community-maintained 0.x NuGet dependency enters the Hub assembly;
  mitigated by C9 confining it behind `Grimoire.Hub.Cli` so a framework swap touches one
  namespace plus the composition root.
- Neutral, because the framework choice deliberately decides neither the routing shape
  (ADR-023's default command) nor the execution model (ADR-049) — those are separable
  decisions on top of any framework.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new command class and settings type on
  the same framework; a new switch or option added within the catalog governance ADR-041/
  ADR-024 define; changes to help text, status rendering, or the exit-code mapping's
  members; upgrading the Spectre package versions.
- **Invalidations (would require full supersession):** switching to a different CLI
  framework (e.g. System.CommandLine); returning to hand-rolled argument parsing or
  hand-written usage text; introducing a second CLI framework alongside Spectre; lifting
  C9 so Spectre types spread beyond `Grimoire.Hub.Cli`.

## More Information

Supersedes [ADR-020](ADR-020-hub-cli-command-surface.md) together with
[ADR-049](ADR-049-cli-in-process-blocking-execution.md) and
[ADR-050](ADR-050-cli-hub-concurrency-locking.md), folding in the amendments that touched
this aspect: the switch catalog's cap and later growth (originally ADR-022, now
[ADR-041](ADR-041-independent-directory-roots.md) and
[ADR-024](ADR-024-memory-directory-root.md)) and the behavioral enforcement of the help
surface ([ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md)).

Read alongside:
[ADR-023](ADR-023-hub-cli-default-command-and-root-help-routing.md) — `HubRootCommand` as
the Spectre default command, the one-line `Program.cs` pass-through, and generated root
help; [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) — the N1
namespace-ownership map `Grimoire.Hub.Cli` is registered in;
[ADR-049](ADR-049-cli-in-process-blocking-execution.md) — how commands built on this
framework execute. None of their decisions are restated or narrowed here.

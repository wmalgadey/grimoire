# Research: Explicit Path Configuration (005-content-root-config)

## R1 — Where path defaults live (single location)

**Decision**: All runtime-data path defaults are defined in exactly one place: a new
`GrimoirePathOptions` record (namespace `Grimoire.Hub.Runtime.Paths`) plus one
`Grimoire:Paths` section in `appsettings.json`. A single `GrimoirePathResolver` turns
options into the resolved, validated `ResolvedGrimoirePaths` used everywhere else.
`ContentRootPaths.Resolve(repoRoot, …)` and `RawStoragePaths.Resolve(repoRoot)` lose
their repo-root parameters and become projections of the resolved data directory;
`Program.cs` no longer assembles any path from string literals.

**Rationale**: The user's core complaint is that defaults are (a) inside project
folders and (b) scattered across `Program.cs`, `ContentRootPaths`, `RawStoragePaths`,
and hard-coded literals (`backend/data`, `.env`, `agents/ingest`). One options type +
one config section makes every current and future runtime location visible in one file
each for code and configuration.

**Alternatives considered**: Keeping per-component `Resolve` methods with individual
config keys (still scattered); a custom config file format (violates the "no new
configuration mechanism" assumption — standard .NET configuration already provides
CLI/env/JSON layering).

## R2 — Consolidated data directory layout and names

**Decision**: Default layout under the base directory:

Two homes beneath the base: the wiki in its own directory (independently
version-controllable), and one consolidated `data/` directory for all internal
runtime data.

| Location | Default | Kind |
| --- | --- | --- |
| Base directory | explicitly configured, else process working directory | — |
| Wiki content root | `<base>/wiki` | writable data (auto-create) |
| Data directory | `<base>/data` | — |
| Raw intake storage | `<data>/raw` (`originals/`, `sources/`) | writable data (auto-create) |
| Operational state | `<data>/state/operational-state.db` | writable data (auto-create dir) |
| Secrets file | `<data>/.env` | required input |
| Agent instructions | `<data>/agents/ingest` (`system-prompt.md`, `default-user-prompt.md`, `policy.json`) | required input |
| Agent worker | next to the Hub binaries (see R4) | required input |

The wiki sits deliberately OUTSIDE `data/` (plan revision 2026-07-18): it is domain
state a user may put under its own git repository and commit independently (ADR-003
portability), while `data/` holds internal runtime data the application owns.
ADR-003's illustrative `.grimoire/` example is superseded by the concrete,
configurable `data/` location; ADR-003's substance (git-friendly domain state vs.
git-ignored operational SQLite) is strengthened — `data/state/` and `data/raw/` are
git-ignored, the wiki directory is a clean, independently trackable tree. In the
checkout, today's `wiki/` directory already matches the default and does not move.

**Rationale**: Clarification session 2026-07-18 (consolidated internal data, working
directory default, manual one-time move) + plan-revision input: "the wiki dir and the
data dir should be separate, so the wiki could for instance be separately committed
to git."

**Alternatives considered**: Wiki inside `data/` (couples the user's knowledge base
to internal runtime files, making an independent wiki git repo awkward — rejected by
plan revision); `.grimoire/` hidden dir (hostile to wiki editing); keeping today's
scattered layout (explicitly rejected in clarification Q1).

## R3 — Removing repo-root discovery from both processes

**Decision**: Delete `FindRepoRoot` from `Grimoire.Hub/Program.cs` and
`Grimoire.IngestAgent/Program.cs` (both shell out to `git rev-parse --show-toplevel`
today). The Hub resolves everything via R1. The agent gains one new CLI argument,
`--wiki-root` (the content root), passed by `AgentProcessHost`:

- relative page paths in task artifacts/reports are computed against `--wiki-root`
  (satisfies FR-009);
- `PolicyLoader` resolves policy path prefixes against `--wiki-root` instead of a
  discovered repo root, and `agents/ingest/policy.json` prefixes are rewritten to be
  content-root-relative (`pages/`, `tasks/`, `index.md`, `log.md`).

`SubmissionService.ResolveSourcePath` resolves relative source paths against the
process working directory instead of the repo root.

**Rationale**: FR-002/FR-007. The policy-prefix change also fixes a real defect: the
current prod launch config points the content root at an external absolute path while
policy prefixes still resolve against the repo root, so guardrail prefixes and actual
write targets diverge. Content-root-relative prefixes make the policy file portable
across deployments.

**Alternatives considered**: Passing a separate `--policy-root`: rejected — every
guarded location (pages, tasks, index, log) lives under the content root, so a second
root adds a degree of freedom nothing needs.

## R4 — Locating the agent worker without a source tree

**Decision**: New config value `Grimoire:Paths:AgentWorker`. Interpretation by suffix:
`.csproj` → launch via `dotnet run --project` (dev convenience, preserves today's
behavior when explicitly configured); `.dll` → `dotnet <dll>`; otherwise → direct
executable. Default: `Grimoire.IngestAgent.dll` beside the Hub assembly
(`AppContext.BaseDirectory`) — the layout a production publish produces. The dev launch
configurations pass the `.csproj` (or built dll) path explicitly.

**Rationale**: The worker binary is part of the application, not runtime data, so its
default is install-relative rather than data-relative; FR-001 still makes it
overridable. Keeps ADR-002's child-process model untouched.

**Alternatives considered**: Putting the worker path under `data/` (wrong category —
it is code); always requiring explicit configuration (breaks zero-config dev start).

## R5 — Configuration channels and precedence

**Decision**: Use the standard ASP.NET Core configuration stack. Friendly CLI switches
map onto `Grimoire:Paths:*` keys via command-line switch mappings: `--base-dir`,
`--data-dir`, `--content-root`, `--raw-dir`, `--state-db`, `--secrets-file`,
`--instructions-dir`, `--agent-worker`. Environment variables use the standard
`Grimoire__Paths__*` form. Provider order gives exactly the mandated precedence:
command line > environment > `appsettings.json` > code defaults. The existing
hand-rolled `ParseOption(args, "--content-root")` is removed in favor of the
configuration system.

**Rationale**: FR-005 with zero new mechanism; `--content-root` keeps working (now
uniformly, in all channels).

**Alternatives considered**: Hand-parsing all switches (duplicates the framework);
named profiles / environment-keyed defaults (rejected in clarification Q3).

## R6 — Startup validation and reporting

**Decision**: `GrimoirePathResolver` validates after resolution, before the host
serves or dispatches anything: required inputs (secrets file, instructions dir with
its three files, agent worker) must exist with the right kind → otherwise throw a
startup exception whose message names the logical location, configured value, and
resolved path; writable data locations are created if absent. On success one
structured `paths_resolved` INFO event lists every resolved absolute path; each
auto-creation emits `paths_location_created`; each validation failure emits
`paths_validation_failed` ERROR before the process exits non-zero.

**Rationale**: FR-006, FR-008, SC-002, SC-005; fail-fast keeps misconfiguration out
of mid-run failures (user story 3).

**Alternatives considered**: Lazy per-use validation (surfaces errors mid-ingest —
exactly what the story forbids).

## R7 — Migration of the existing checkout

**Decision**: Documented one-time move (quickstart.md §Migration): `wiki/` stays
where it is (already matches the `<base>/wiki` default); `git mv agents data/agents`;
move `raw/` → `data/raw`, `.env` → `data/.env`,
`backend/data/operational-state.db` → `data/state/`; update `.gitignore`
(`backend/data/` → `data/state/`, add `data/raw/`); rewrite `policy.json` prefixes to
content-root-relative; update `.vscode/launch.json` (dev: cwd at the checkout root —
or explicit `--base-dir ${workspaceFolder}`; prod: explicit `--content-root` plus its
own base/data dir). No legacy-layout detection code ships.

**Rationale**: Clarification Q4; a single dev environment does not justify permanent
compatibility code.

**Alternatives considered**: Auto-detect/auto-migrate (rejected in clarification Q4).

## R8 — Structural enforcement rule (Principle III)

**Decision**: New arch test `RuntimePathsBoundaryRuleTests` in `Grimoire.ArchTests`
(Mono.Cecil IL scan, same idiom as `GuardedWriteBoundaryRuleTests`): in production
assemblies (`Grimoire.Hub`, `Grimoire.IngestAgent`, `Grimoire.Domain`), calls to
`System.IO.Directory::GetCurrentDirectory`, reads of
`System.Environment::CurrentDirectory`, and reads of
`System.AppContext::BaseDirectory` are permitted only inside
`Grimoire.Hub.Runtime.Paths`; and no production type may invoke a process named `git`
(IL string-literal scan for `rev-parse`/`--show-toplevel` as the tripwire). Phase 0
Red/Green probe: introduce a deliberately violating class, verify the rule fails,
delete it.

**Rationale**: Principle III requires every active ADR constraint to have an automated
structural test; ambient-directory APIs are the only IL-visible fingerprint of
"resolves paths outside the single composition point".

**Alternatives considered**: NetArchTest dependency rules (cannot see method-level
calls to static BCL members); convention-only review (constitution: conventions not
enforced by CI do not exist).

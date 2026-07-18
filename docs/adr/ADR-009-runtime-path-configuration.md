---
status: proposed
---

# ADR-009: Explicit Runtime Path Configuration and Consolidated Data Directory

## Context and Problem Statement

Every runtime location Grimoire uses — wiki content root, raw intake storage, the
SQLite operational store, the secrets file, agent instruction files, and the agent
worker binary — is currently derived from the root of the git checkout the process
happens to run in (`git rev-parse --show-toplevel`), using hard-coded project-layout
fragments scattered across `Program.cs`, `ContentRootPaths`, and `RawStoragePaths`.
A deployed installation has no git checkout and no source layout, so the application
cannot run outside a developer clone; defaults are invisible to operators; and the
prod launch configuration already exposes a divergence (external content root vs.
repo-root-resolved policy prefixes). How runtime paths are resolved is a cross-cutting
shape every process (Hub, current and future agents) inherits, so it is fixed by ADR
(feature 005-content-root-config).

## Decision Drivers

- The application must run in a directory with no repository structure and without
  version-control tooling installed (spec 005 FR-002).
- All defaults must be defined in one obvious place, and all internal runtime data
  must land under one consolidated directory — for current and future locations
  (clarification 2026-07-18 Q1).
- The wiki must stay separable from internal runtime data so it can be committed to
  its own git repository independently (plan revision 2026-07-18; ADR-003
  portability).
- The repo checkout must remain a fully valid base directory; any other base must work
  identically (clarification Q1/Q2).
- Prod and dev must differ only in configuration values, not code paths
  (clarification Q3).
- Constitution III/IV: the boundary needs automated structural enforcement and CI
  gating; no new configuration infrastructure.

## Considered Options

1. **Single path-options composition point + consolidated `data/` directory,
   standard configuration layering, no ambient discovery**
2. Keep repo-root discovery but allow overriding individual paths
3. OS-conventional app-data directories (XDG/AppData) as defaults
4. A dedicated required `--base-dir` with no defaults (fully explicit always)

## Decision Outcome

Chosen option: **Option 1.**

- **Single composition point.** One options record (`GrimoirePathOptions`, bound from
  the `Grimoire:Paths` configuration section) and one resolver
  (`GrimoirePathResolver`, namespace `Grimoire.Hub.Runtime.Paths`) define and resolve
  every runtime location. No other production code derives a root path or reads
  ambient process context (current directory, executable directory) for path purposes.
- **Two homes beneath the base.** Defaults: base = configured `BaseDir`, else the
  process working directory. The wiki content root lives in its own directory,
  `<base>/wiki`, deliberately outside the data directory so the user's knowledge base
  can be version-controlled and committed independently of application internals. All
  internal runtime data lives beneath `<base>/data` — `raw/`,
  `state/operational-state.db`, `.env`, `agents/ingest/`. The agent worker binary is
  application code, not data: its default resolves beside the Hub binaries. Future
  internal runtime locations MUST be added to this options type and default beneath
  the data directory.
- **Standard layering, one precedence.** Command line > environment >
  `appsettings.json` > code defaults, via the stock configuration providers with
  friendly switch mappings (`--base-dir`, `--content-root`, …). No profile mechanism.
- **No ambient discovery anywhere.** `FindRepoRoot` is deleted from Hub and agent;
  the agent receives an explicit `--wiki-root` (anchor for relative page paths and
  for safety-policy prefix resolution, making `policy.json` content-root-relative and
  portable). Relative source-path resolution uses the working directory.
- **Fail-fast validation.** Required inputs (secrets, instruction files, worker)
  missing ⇒ startup aborts naming the logical location, configured value, and
  resolved path; writable data locations are auto-created; every successful start
  reports all resolved locations.

This ADR supersedes ADR-003's illustrative `.grimoire/` runtime-directory naming;
ADR-003's substance (git-friendly domain state vs. git-ignored operational SQLite)
is strengthened — `data/state/` and `data/raw/` are git-ignored while the wiki is a
clean, independently trackable tree outside `data/`. ADR-002/004/006/007/008
contracts are amended only by the added `--wiki-root` argument and policy-prefix
anchor.

### Consequences

- Good, because the application becomes deployable to any directory, with operator-
  visible, centrally defined locations for all runtime data, now and for future
  features (new locations have exactly one place to go).
- Good, because one policy file is valid for every deployment (prefixes anchored at
  the content root), closing the existing prod divergence.
- Bad, because the existing checkout needs a documented one-time data move
  (`agents/`, `raw/`, `.env`, `backend/data/` → `data/...`; `wiki/` stays in place),
  and task artifacts change page paths from repo-relative to content-root-relative.
- Neutral: launch configurations must pass the agent-worker path when running the
  worker from source.

## Structural Enforcement (Constitution III)

`Grimoire.ArchTests/RuntimePathsBoundaryRuleTests` (Mono.Cecil IL scan, same idiom as
the ADR-006 guarded-write rule):

1. In production assemblies, calls to `Directory.GetCurrentDirectory`,
   `Environment.CurrentDirectory`, and `AppContext.BaseDirectory` are permitted only
   in namespace `Grimoire.Hub.Runtime.Paths`.
2. No production assembly contains IL string literals `rev-parse` or
   `--show-toplevel` (tripwire against reintroducing repo discovery).

The rule ships with a Red/Green probe (deliberate violation, verified failure,
removal) before feature code, and runs in the standard PR pipeline.

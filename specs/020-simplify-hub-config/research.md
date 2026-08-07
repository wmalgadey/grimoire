# Phase 0 Research: Simplify Hub CLI Configuration

**Feature**: `020-simplify-hub-config` | **Date**: 2026-08-07

All Technical Context unknowns are resolved below. Each decision records what was chosen,
why, and what was rejected. Findings marked **[ADR-022]** are load-bearing enough to be
fixed by ADR (`docs/adr/ADR-022-minimal-directory-configuration-surface.md`).

---

## R1 — How the three roots anchor and resolve **[ADR-022]**

**Decision**: Three independent roots, no shared base.

| Root | Anchor | Configured default | CLI switch | Env var |
| --- | --- | --- | --- | --- |
| Runtime data | process working directory | `.grimoire` | `--data-dir` | `Grimoire__Paths__DataDir` |
| Wiki | process working directory | `llm-wiki` | `--wiki-dir` | `Grimoire__Paths__WikiDir` |
| Agent | resolved `DataDir` | `agents` | `--agent-dir` | `Grimoire__Paths__AgentDir` |

Relative values resolve against the anchor; absolute values are used verbatim
(`GrimoirePathResolver.ResolveAgainst`, unchanged behavior).

**Rationale**: FR-009 fixes `.grimoire/` and `llm-wiki/` as cwd siblings and the agent
folder inside the data folder. Anchoring the agent folder at `DataDir` — not at cwd — is
what makes US3 work: relocating runtime data carries the agent folder with it (one
option), while the wiki, anchored independently at cwd, provably stays put (US3 AS2).

**Alternatives rejected**:

- *Keep `BaseDir` and derive all three.* Moving the base would drag the wiki along,
  contradicting US3 AS2; re-pinning the wiki would need a second flag anyway.
- *Anchor the agent folder at cwd too.* Then `--data-dir` alone would not relocate a full
  environment, defeating US3's "one option" promise.

## R2 — Removing the code-default tier **[ADR-022]**

**Decision**: `appsettings.json` is mandatory and is the only source of default paths.
After `builder.Configuration.GetSection("Grimoire:Paths").Bind(options)`, the resolver
validates that `DataDir`, `WikiDir` and `AgentDir` each carry a non-empty value and throws
a `GrimoirePathValidationException` naming `appsettings.json` and the missing keys
otherwise (FR-005, SC-006).

**Mechanism note**: the file is already copied to build output and already loaded — the
Hub pins `ContentRootPath = GrimoirePathResolver.ProcessBaseDirectory`
(`HubHostComposition.cs:60-66`), so `appsettings.json` loads from beside `Grimoire.Hub.dll`
regardless of the launching cwd. No new configuration infrastructure is needed (Principle
IV: no unapproved infrastructure).

**Rationale**: a code fallback is precisely what made 16 switches feel simultaneously
mandatory and invisible — the effective layout was readable nowhere. One versioned file
that must be present and complete makes it readable in one place, and makes a
misconfiguration loud instead of silently defaulted.

**Alternatives rejected**: keeping code defaults as a safety net (status quo; two sources
of truth, silent winner); making the file optional but authoritative when present (same
defect, harder to reason about).

## R3 — Which sub-paths survive, and where they anchor **[ADR-022]**

**Decision**: sub-paths remain `GrimoirePathOptions` fields with `appsettings.json` keys
but get **no** switches (FR-015).

| Sub-path | Anchor | Configured default |
| --- | --- | --- |
| `RawDir` | `DataDir` | `raw` |
| `StateDb` | `DataDir` | `state/operational-state.db` |
| `WriteLocksDir` | `DataDir` | `write-locks` |
| `TasksDir` | `WikiDir` | `tasks` |
| `ConversationsDir` | `WikiDir` | `conversations` |
| `FindingsDir` | `WikiDir` | `findings` |
| `RemediationTasksDir` | `WikiDir` | `remediation-tasks` |
| `SecretsFile` | working directory | `.env` |

`lint.pid` and `index.md` / `log.md` stay fixed filenames under an already-resolved
directory (no options field — the ADR-020 precedent), as do `raw/originals`, `raw/sources`
and the per-agent-type instruction subfolders.

**The consequential move**: `ConversationsDir`, `FindingsDir` and `RemediationTasksDir`
re-anchor from `DataDir`/`BaseDir` to `WikiDir` (FR-007, clarification 2026-08-06 — they
are agent output). This reverses their ADR-003/ADR-009 status as git-ignored internal
bookkeeping. Recorded as a deliberate consequence in ADR-022; `.gitignore` changes
accordingly.

## R4 — Agent workers: inside the agent directory, one launch mode, no runtime build **[ADR-022]**

**Decision** (author directives, 2026-08-07 — *(a) the hub must never run an agent build;
(b) the agent build must copy the assemblies into the agent directory, because the agent
assembly belongs to the hub's runtime and it should all be in one directory after build and
consumed from there*):

1. The three `--*-agent-worker` switches and their options fields are deleted. Each worker
   resolves as `<AgentDir>/<agent-id>/Grimoire.{Ingest,Query,Lint}Agent.dll`.
2. `AgentProcessHost` gets **one** launch mode — `dotnet <that path>`. The
   `.csproj` → `dotnet run --project` branch and the bare-executable branch are deleted
   from all five spawn sites (`AgentProcessHost.cs:181, 266, 353, 468, 581`).
3. A missing worker DLL fails at startup validation, not at dispatch:
   *"Grimoire.IngestAgent.dll not found in the agent directory. Build first: dotnet build
   backend/Grimoire.slnx"*.

### The finding that drives this

ADR-009 says the worker default "resolves beside the Hub binaries". **It never has.**
`Grimoire.Hub.csproj` references only `Grimoire.Domain`, and ADR-002's
`HubAgentDispatchBoundaryRuleTests` explicitly forbids a Hub→agent assembly reference — so
`Grimoire.IngestAgent.dll` is not in `backend/src/Grimoire.Hub/bin/Debug/net10.0/` and no
solution build ever puts it there (verified: that directory contains only `Grimoire.Hub.dll`
and `Grimoire.Domain.dll`).

That gap is *why* the `.csproj` launch branch exists: launch configurations pointed the
worker switch at a project file and the hub built the agent to substitute for an artifact
that was never delivered. It also means a hub started with default configuration could
never dispatch — so **SC-001 ("hub commands succeed with no flags") was unreachable** under
the previous revision of this plan, which kept `<ProcessBaseDirectory>` as the worker
anchor.

Meanwhile each agent's own build output already *is* a complete runnable directory —
`Grimoire.IngestAgent.dll` + `deps.json` + `runtimeconfig.json` + 30 dependency assemblies.
Delivering that directory to `<AgentDir>/<agent-id>/` closes the gap at its source.

### Why the hub must not build

`AgentProcessHost` branches on the configured worker path's extension — `.csproj` via
`dotnet run --project` (documented in the type as "dev convenience"), `.dll` via
`dotnet <dll>`, anything else directly. The first branch means a **running Hub can trigger
a NuGet restore and an MSBuild compile inside a request**: unbounded agent-start latency,
compile errors surfacing as agent-run failures, and `obj/`/NuGet lock contention with any
concurrent `dotnet test` or IDE build. ADR-008's supervision and heartbeat timings were
written assuming process *start*, not process *build*.

`Grimoire.EvalRunner` already demonstrates the target discipline
(`AgentProcessInvoker.ResolveAgentDllPath`): find the built DLL, or fail telling the
operator to build. The hub adopts that rather than inventing a second story.

**Unifying principle**: *the hub consumes build artifacts and never produces them* — true
for instruction files and worker binaries alike, and now true of one directory rather than
two.

**Alternatives rejected**:

- *Workers beside the hub binaries, instructions in the agent directory* (this plan's
  previous revision). Rejected on the finding above: nothing delivers the DLLs there, so
  the default configuration cannot dispatch.
- *Add `ProjectReference … ReferenceOutputAssembly="false"` from the Hub to each agent* so
  the DLLs land in the Hub's output. Makes the Hub's build depend on the agents (inverting
  ADR-002's dispatch-only relationship for build ordering), still needs a second mechanism
  for instruction files, and offers no redirect for a custom agent directory (FR-012).
- *Keep the `.csproj` branch behind an explicit opt-in flag.* Reintroduces a switch this
  feature exists to remove, for a behavior not worth offering.
- *Keep it but build once at hub startup rather than per dispatch.* The hub still invokes
  MSBuild and still needs a source tree; a deployed installation has neither — the exact
  problem ADR-009 was written to solve.

**Cost accepted**: developers editing an agent must build before the hub picks up the
change, instead of the next dispatch rebuilding implicitly. `dotnet build
backend/Grimoire.slnx` is already the documented first step for the eval runner, the
integration tests and CI.

**Consequences to verify in tasks**: any launch configuration, devcontainer setting, script
or documentation passing `--agent-worker` (or a `.csproj` worker path) must be updated —
they now fail with "unrecognized option". `.vscode/launch.json` currently launches
`Grimoire.Hub.dll` without worker switches, so it is likely unaffected; verify the
devcontainer and `scripts/`. Integration tests that construct `ResolvedGrimoirePaths` with
`AgentWorkerPath: "unused"` (≈8 files) follow the record's restructuring.

**Enforcement**: structural rule R4 (`NoRuntimeBuildInvocationRuleTests`) — no production
assembly carries build-invocation IL literals (`.csproj`, `--project`, `msbuild`,
`dotnet build`/`restore` arguments), with a documented allow-list for the eval runner's
*diagnostic message* strings, which mention those commands in prose without invoking them.
ADR-002's existing `HubAgentDispatchBoundaryRuleTests` is cited as newly load-bearing: it is
the rule that makes the agent directory the only possible home for the agent runtime.

## R5 — The whole agent runtime becomes build output, in one copy **[ADR-022]**

**Decision**: two composed mechanisms, one copy operation.

1. Instruction sources live in `backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/`
   and are declared as `Content` with `CopyToOutputDirectory`, so they land in the agent's
   own `$(OutDir)` under `Instructions/`, beside its assemblies.
2. `backend/Directory.Build.targets` gains a **`PublishAgentRuntime`** target,
   `AfterTargets="Build"`, condition `'$(GrimoireAgentId)' != ''`, which clears
   `$(GrimoireAgentDir)/$(GrimoireAgentId)/` and copies the agent's **entire build
   output** — `$(OutDir)**` — into it.

Result per agent type: `<AgentDir>/<agent-id>/` holds the worker DLL, `deps.json`,
`runtimeconfig.json`, every dependency assembly, and `Instructions/`. One directory, one
copy, launchable as-is.

`GrimoireAgentDir` defaults to the repo-relative `.grimoire/agents` and is the supported
redirect (FR-012): `dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=/srv/agents`.

**Why the whole `$(OutDir)`, not a selection**: `deps.json` enumerates every assembly the
app expects; copying a subset breaks resolution at launch. This is the exact failure the
eval runner's `AgentProcessInvoker` documents — *"a copy inside a test host's output
directory lacks assemblies the test host takes from the ASP.NET shared framework"*. Copying
the complete output to a fresh directory has no such problem: `deps.json`,
`runtimeconfig.json` and the assemblies stay consistent with each other.

**Why clear-then-copy**: an additive copy leaves stale assemblies and renamed instruction
files behind, so the directory would no longer state what the current build produces
(SC-008). The target deletes only `$(GrimoireAgentDir)/$(GrimoireAgentId)/` — a path it
wholly owns — never `GrimoireAgentDir` itself or anything beside it.

**Why an MSBuild property rather than a shell script**: FR-012 accepts "build script,
build-tool task, or build property". A property composes with every invocation path the
project already uses (`dotnet build`, `dotnet test`, IDE build, CI, devcontainer) without
a second entry point to keep in sync; a script would only cover the paths that call it.

**Why not `dotnet publish`**: semantically the closest fit ("a deployable directory"), but
it does not run as part of `dotnet build`, and chaining it from `AfterTargets="Build"` is
recursive and fragile. `dotnet build` already produces a runnable output directory for
these projects, so copying it is both simpler and equivalent.

**Alternatives rejected**:

- *Instruction files only, assemblies left beside the hub binaries.* This plan's previous
  revision — rejected on the R4 finding: nothing delivers the DLLs there.
- *Embedded resources written out by the hub at startup.* Makes the hub the author of
  instruction content — the exact Principle V violation this feature must avoid — and
  removes the operator's ability to read effective instructions on disk.
- *`CopyToOutputDirectory` alone.* Lands the files beside the agent binary but never in the
  configurable agent directory, and offers no redirect. Retained here only as step 1 of
  the composed mechanism above.
- *Keeping them as hand-maintained data files.* Nothing then keeps a relocated agent
  directory current across rebuilds (FR-012 unsatisfiable).

**Cost accepted**: each agent-type subfolder carries its own copy of the shared dependency
set (~30 files × 3 agents, a few MB) rather than one shared set. That duplication is what
makes each subfolder independently launchable with a correct `deps.json`, and per-agent
isolation matches ADR-013's per-profile capability model.

**Instruction-file authorship stays in agent sources**, so the Principle V boundary is
unchanged in substance: wiki behavior changes remain instruction-file changes — the files
simply have one authoritative home instead of two divergent ones (repo data dir vs. eval
runner hardcode).

**Side benefit**: because instruction files now flow through `$(OutDir)`, each agent's own
`bin/<config>/net10.0/` is itself a valid agent directory — useful for tests that need a
real agent runtime without redirecting the shared build property.

## R6 — Eval fixture and instruction resolution **[ADR-022]**

**Decision**:

- Recordings move `data/evals/recordings/` → `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/`
  (259 tracked files, `git mv`). `EvalPaths.DefaultRecordingsRoot` becomes
  `RecordingsRoot => Path.Combine(FixturesRoot, "recordings")`; `--recordings-root` is
  removed from `Grimoire.EvalRunner`'s parser and usage line (FR-016/SC-009).
  `recordings` becomes a reserved name under `Fixtures/` (scenario fixtures are
  `Fixtures/<scenario>/wiki`).
- `EvalPaths.{Agent,Query,Lint}InstructionsDir` resolve to
  `<repo-root>/backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions` — agent project
  **sources**, not build output and not the runtime agent directory (FR-017/FR-018/SC-010).
- `EvalPaths.LocalEnvPath` becomes `<repo-root>/.env` (FR-019/SC-011).

`EvalPaths.Discover` (walks up for `.git`/`.specify`) stays — the eval runner is a
repo-local development tool, explicitly outside the ADR-009 no-ambient-discovery rule,
which scopes itself to the deployable application.

**Consequence to verify in tasks**: `.github/workflows/eval.yml` uploads and commits
`data/evals/recordings/**`; both path globs and the CI comment referencing that directory
must move with the files.

## R7 — Secrets file at the project root **[ADR-022]**

**Decision**: `SecretsFile` defaults to `.env`, anchored at the **working directory**, not
at any of the three roots. `data/.env` moves to `<repo-root>/.env`, beside the existing
`.env-example`.

**Rationale**: FR-019 requires the location to be unaffected by the three directory
options, and the working directory is the only anchor that satisfies that while remaining
inside ADR-009's "no ambient discovery outside `Grimoire.Hub.Runtime.Paths`" rule —
`GrimoirePathResolver.CurrentWorkingDirectory` is the sanctioned read. The `.gitignore`
already covers a bare `.env` pattern, so the file stays git-ignored with no change.

**Amends ADR-019**: the devcontainer credential story is written against `<base>/data/.env`
and must be re-pointed at `<repo-root>/.env` (documentation + any bind mount).

## R8 — Capping the switch surface, not just trimming it **[ADR-022]**

**Decision**: `PathSwitchCatalog` stays the single source of truth (ADR-020's "declared
exactly once" principle) and is **structurally capped** at three entries by
`Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests` (rule R1), with the existing
`HubPathSettings` 1:1 parity assertion retained.

**Rationale**: the 16-switch drift happened *under* ADR-009's single-source-of-truth rule
— that rule prevents inconsistency, not growth. SC-002 states a count, so the count is
what must be enforced. Without a cap, the next feature that adds a runtime location
re-derives "add a switch" from the same reading of ADR-009.

## R9 — Naming: `ContentRoot` → `WikiDir`, and the bounded scope of the rename

**Decision**: rename the configuration surface and the resolved record —
`GrimoirePathOptions.ContentRoot` → `WikiDir`, config key `Grimoire:Paths:WikiDir`, switch
`--wiki-dir`, `ResolvedGrimoirePaths.ContentRoot` → `WikiDir`, log field `content_root` →
`wiki_dir`. **Deliberately out of scope**: the `ContentRootPaths` / `RawStoragePaths`
projection types and the agent's `--wiki-root` process argument.

**Rationale**: Principle I requires Ubiquitous Language in naming, and the spec's entity is
"Wiki Folder". The projection types are internal, consumed via DI in ~10 Hub files, and
renaming them buys no clarity at the operator boundary while widening an already large
change. The agent's `--wiki-root` argument already reads as "wiki" and is fixed by
ADR-002/007/011 contracts — changing it would break the agent CLI contract for cosmetics.

**Follow-up recorded, not scheduled**: renaming `ContentRootPaths` → `WikiPaths` is a
mechanical cleanup for a later change.

## R10 — Structural enforcement inventory

Three new Red/Green-probed rules in `Grimoire.ArchTests` (ADR-022 §Structural Enforcement):

- **R1** `DirectorySwitchSurfaceRuleTests` — catalog capped at exactly the three switches;
  `HubPathSettings` parity. Probe: add a fourth entry.
- **R2** `NoCodeLevelPathDefaultsRuleTests` — no production-assembly IL string literal
  equals `.grimoire` or `llm-wiki`. Probe: reintroduce a `DefaultDataDirName` constant.
- **R3** `InstructionAuthorshipBoundaryRuleTests` — no production type outside
  `Grimoire.Hub.Runtime.Paths` uses an instruction filename literal as a write target.
  Probe: a Hub type writing `system-prompt.md`.

Existing `RuntimePathsBoundaryRuleTests` (ADR-009) is unchanged and keeps guarding ambient
discovery.

## R11 — Testing approach for the mandatory-config and empty-agent-dir failures

**Decision**: hermetic integration tests in `Grimoire.IntegrationTests/PathConfiguration/`,
following the established idiom in that folder — build an `IConfigurationRoot` in a temp
directory, call `GrimoirePathResolver.Resolve`, assert on the returned record or the thrown
`GrimoirePathValidationException` (state-based, Chicago-school; no doubles needed because
the resolver's only collaborator is the filesystem, which Principle II says to exercise for
real).

For end-to-end no-flags startup (SC-001) the existing `HubHelpUsageTests` process-spawn
idiom applies: seed a temp directory satisfying every required input, launch the Hub binary
with cwd set there and no arguments, assert exit code and the `paths_resolved` report.

**No mocking framework, no new port.** The filesystem is a persistence/local-filesystem
concern, port-exempt under Principle I; introducing an `IFileSystem` to isolate the
resolver would violate both Principle I's persistence exemption and Principle II's
classicist rule.

## R12 — Data migration: none

**Decision**: no detection, no migration, no deprecation aliases (FR-014, clarification
2026-08-06). Removed switches produce the CLI parser's standard "unrecognized option"
error.

**In-repo consequence**: this repository's own working data (`data/`, `conversations/`,
`tasks/`, `remediation-tasks/`) is *developer* data, not operator data. Tasks will `git mv`
the git-tracked parts (instruction files → agent projects, recordings → test fixtures,
`data/.env` → `.env`) and leave the git-ignored parts (`data/state/`, `data/raw/`,
`data/write-locks/`, `data/findings/`) for manual relocation, exactly as an operator would.

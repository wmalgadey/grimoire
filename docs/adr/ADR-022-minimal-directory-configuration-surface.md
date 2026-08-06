---
status: accepted
---

# ADR-022: Minimal Directory Configuration Surface — Three Roots, Mandatory Configuration File, and Build-Distributed Agent Artifacts

## Context and Problem Statement

ADR-009 established one composition point (`GrimoirePathOptions` /
`GrimoirePathResolver`) and one precedence chain for every runtime location, and it
solved the problem it was written for: the application became deployable outside a git
checkout. What it did not bound was the *width* of the operator-facing surface. Its
"friendly switch mappings (`--base-dir`, `--content-root`, …)" clause has been read by
every subsequent feature as "each new runtime location gets its own switch", and the
surface has grown to **16 command-line path switches** (`PathSwitchCatalog.All`),
mirrored 1:1 into `HubPathSettings` for every CLI command (ADR-020). Eleven of the
sixteen are internal layout details (`--raw-dir`, `--state-db`, `--write-locks-dir`,
`--findings-dir`, three `--*-instructions-dir`, three `--*-agent-worker`,
`--remediation-tasks-dir`); an operator must read all sixteen to discover that only a
couple ever need changing.

Feature 020 (`specs/020-simplify-hub-config/spec.md`) requires the opposite shape: the
hub must run with **no flags and no environment variables at all**, and expose exactly
three directory options. Delivering that changes four load-bearing parts of ADR-009's
decision outcome, plus contracts owned by ADR-007, ADR-012, ADR-019 and ADR-020 — so it
must be fixed by ADR rather than decided inside the feature:

1. **Root shape.** ADR-009's `BaseDir` + "two homes beneath the base" (`<base>/wiki`,
   `<base>/data`) becomes three *independent* roots with no shared parent, so relocating
   runtime data cannot drag the wiki with it (spec US3 AS2).
2. **Where defaults live.** ADR-009 ends its precedence chain with "> code defaults".
   Spec FR-005 removes that terminal: `appsettings.json` is the *sole* source of default
   paths and its absence is a startup failure. A code default is exactly what makes a
   16-switch surface feel optional-but-invisible; removing it makes the layout readable
   in one versioned file.
3. **Who authors instruction files.** Today `data/agents/{ingest,query,lint}/` are
   hand-maintained, git-tracked files living *inside* the runtime data directory, and the
   eval runner hardcodes that same path. Spec FR-011/FR-012/FR-017 bind instruction files
   to the agent that consumes them: they become agent-project sources distributed by the
   agent build, which makes a relocated agent directory stay current across rebuilds and
   lets eval runs resolve instructions without any prior build or hub configuration.
4. **Where the secrets file lives.** ADR-009 put `.env` under the data directory and
   ADR-019 built the devcontainer credential story on `<base>/data/.env`. Spec FR-019
   moves it to the project root beside the `.env-example` it is copied from, and makes it
   independent of all three roots, so relocating runtime data never separates an operator
   from their credentials.
5. **Whether the hub may build.** `AgentProcessHost` today branches on the configured
   worker path's extension: a `.csproj` launches via `dotnet run --project` ("dev
   convenience", five call sites across Ingest/Query/Lint), a `.dll` via `dotnet <dll>`,
   anything else directly. The first branch means a *running* Hub can trigger a NuGet
   restore and an MSBuild compile **at dispatch time**, inside a request. That makes agent
   start latency unbounded, surfaces compile errors as agent-run failures, and contends on
   `obj/`/NuGet locks with any concurrent `dotnet test` or IDE build. The hub must consume
   build artifacts, never produce them.
6. **Where the agent runtime lives.** ADR-009 declared the worker's default "resolves
   beside the Hub binaries". **It never has.** `Grimoire.Hub.csproj` references only
   `Grimoire.Domain` — ADR-002's `HubAgentDispatchBoundaryRuleTests` explicitly forbids a
   Hub→agent assembly reference — so `Grimoire.IngestAgent.dll` is not in the Hub's output
   directory and never lands there by building the solution. That gap is precisely why the
   `.csproj` launch branch had to exist: every launch configuration pointed the worker
   switch at a project file, and the hub built the agent to make up for an artifact that
   was never delivered. A hub started with default configuration could not dispatch at
   all. Meanwhile each agent's own build output *is* already a complete runnable directory
   (worker DLL + `deps.json` + `runtimeconfig.json` + ~30 dependency assemblies). The
   agent runtime therefore belongs in the agent directory, delivered there by the agent
   build — which also satisfies spec FR-008's "instruction files **and runtime data** for
   every agent type" literally rather than partially.

## Decision Drivers

- Spec FR-001/FR-002/SC-001/SC-002: zero-configuration first run; exactly three
  command-line directory options, down from sixteen.
- Spec FR-004/FR-015: per-option precedence CLI > environment > configuration file; all
  further layout customization confined to the configuration file, never mirrored as
  switches. The switch surface must be *capped*, not merely trimmed once — otherwise it
  regrows exactly as it did after ADR-009.
- Spec FR-005/SC-006: no code-level fallback; a missing or incomplete configuration file
  is a named startup failure, not a silent default.
- Spec FR-011/FR-013/SC-007/SC-008: instruction files arrive from the agent build; an
  empty agent directory fails the start by name.
- Spec FR-016/FR-017/FR-018/SC-009/SC-010: eval recordings and eval instruction
  resolution are repo-anchored test concerns, independent of operator configuration.
- ADR-009's genuinely load-bearing parts must survive: one composition point, no ambient
  discovery outside `Grimoire.Hub.Runtime.Paths`, no `rev-parse` repo discovery,
  fail-fast validation naming the logical location, auto-creation of writable locations,
  and a startup report of every resolved location.
- Constitution Principles III/IV: each rule needs a structural test with a Red/Green
  probe and a CI gate; Principle V: the hub must not author instruction content.
- Author directives (2026-08-07): (a) the hub must not be able to run an agent build — the
  solution is built ahead of time and the hub only uses build artifacts; (b) the agent
  build must also copy the assemblies into the agent directory, because the agent assembly
  belongs to the hub's runtime and everything must sit in **one** directory after the build
  and be consumed from there.
- ADR-002's spawn model assumes a ready-to-execute agent binary; ADR-008's supervision and
  heartbeat timings assume process start, not process build.
- Pre-1.0, no external consumers: a breaking configuration change is allowed to be clean
  (spec clarification 2026-08-06 — removed switches simply stop being recognized).

## Considered Options

### Root shape

- **S1: Three independent roots** (`DataDir`, `AgentDir`, `WikiDir`), `AgentDir`
  defaulting inside `DataDir`, `WikiDir` a cwd-anchored sibling of `DataDir`.
- **S2: Keep `BaseDir` and derive all three from it.** Rejected: spec US3 AS2 requires
  relocating runtime data to leave the wiki where it is — under a shared base, moving the
  base moves everything, and re-pinning the wiki needs a second option anyway.
- **S3: One root, wiki nested inside it.** Rejected by clarification 2026-08-06 (separate
  siblings) and by ADR-009's still-valid rationale that the wiki must be independently
  version-controllable.

### Where defaults live

- **W1: `appsettings.json` is mandatory and is the only source of default paths**;
  binding produces a startup failure naming the file when a root is absent.
- **W2: Code defaults retained as a safety net, config file optional.** Rejected: it is
  the status quo, and it makes the effective layout unreadable — the operator cannot see
  the defaults anywhere.
- **W3: Code defaults retained, config file mandatory.** Rejected as the worst of both:
  two sources of truth with a silent winner.

### Agent-runtime distribution

- **I1: The agent build copies its entire output — assemblies, `deps.json`,
  `runtimeconfig.json` and instruction files — into `<AgentDir>/<agent-id>/`, redirectable
  via a build property.** One directory per agent type holds everything that agent needs;
  the hub launches and reads from there.
- **I2: Instruction files only; assemblies stay beside the hub binaries** (the previous
  revision of this ADR). Rejected once the Hub→agent reference ban was traced: nothing
  puts the agent DLLs in the Hub's output, so this leaves the hub unable to dispatch on a
  default configuration — the very defect that produced the `.csproj` launch branch.
- **I3: Instruction files remain hand-maintained under the data directory** (status quo).
  Rejected: FR-012 requires a relocated agent directory to stay current across rebuilds,
  which nothing keeps true when the files are loose data.
- **I4: Instruction files embedded as assembly resources, written out by the hub at
  startup.** Rejected: it makes the hub the author of instruction *content*, exactly the
  Principle V boundary this ADR must protect, and destroys the operator's ability to read
  the effective instructions on disk.
- **I5: Add a `ProjectReference … ReferenceOutputAssembly="false"` from the Hub to each
  agent so the DLLs land in the Hub's output.** Rejected: it makes the Hub's build depend
  on the agents (inverting ADR-002's dispatch-only relationship for build ordering), leaves
  instruction files needing a second mechanism anyway, and offers no redirect for a custom
  agent directory (FR-012).

### Capping the switch surface

- **P1: `PathSwitchCatalog` remains the single source of truth and is structurally capped
  at the three declared switches**, with `HubPathSettings` parity preserved.
- **P2: Convention plus review.** Rejected — Principle IV: conventions not enforced by
  CI do not exist, and the 16-switch drift is the proof.

### Agent process launch

- **L1: One launch mode — `dotnet <worker>.dll`.** The `.csproj`/`dotnet run --project`
  and bare-executable branches are deleted; a missing worker DLL is a startup failure
  telling the operator to build.
- **L2: Keep the `.csproj` branch behind an explicit opt-in flag.** Rejected: it
  reintroduces a switch this ADR exists to remove, and the failure mode (a build inside a
  request) is not one worth making available at all.
- **L3: Keep the branch but pre-build at hub startup instead of at dispatch.** Rejected:
  the hub still invokes MSBuild, still needs a source tree, and a deployed installation has
  neither — this is exactly the ADR-009 problem restated.

## Decision Outcome

**Chosen: S1 + W1 + I1 + P1 + L1.**

### Three roots, one composition point

`GrimoirePathOptions` keeps its role as the single composition input, but its fields
split into two tiers:

| Tier | Fields | Anchor | CLI switch |
| --- | --- | --- | --- |
| **Roots** | `DataDir` | process working directory | `--data-dir` |
| | `WikiDir` | process working directory | `--wiki-dir` |
| | `AgentDir` | `DataDir` | `--agent-dir` |
| **Sub-paths** | `RawDir`, `StateDb`, `WriteLocksDir` | `DataDir` | *none* |
| | `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir` | `WikiDir` | *none* |
| | `SecretsFile` | process working directory | *none* |

- `BaseDir` is **removed**. The two cwd-anchored roots replace it; nothing else anchors
  at a shared base.
- Agent worker executables are **no longer configurable** (the three `--*-agent-worker`
  switches and their options fields are removed). Each worker resolves inside the agent
  directory as `<AgentDir>/<agent-id>/Grimoire.{Ingest,Query,Lint}Agent.dll`. The agent
  directory is now the *whole* agent runtime — binaries and instructions — so `--agent-dir`
  is the single option that governs everything about agents, and
  `GrimoirePathResolver.ProcessBaseDirectory` is left with exactly one consumer (pinning
  the host's `ContentRootPath` so `appsettings.json` loads).
- The three per-agent instruction directories become **per-agent-type subfolders of the
  single agent directory**: `<AgentDir>/ingest`, `<AgentDir>/query`, `<AgentDir>/lint`,
  each holding that agent's assemblies at its root and its instruction documents under
  `Instructions/`. Neither the subfolder names nor the `Instructions/` grouping are
  configurable, in the configuration file or anywhere else — they are part of the agent
  build contract below.
- The secrets file anchors at the working directory (project root), **not** at any of the
  three roots (FR-019). Repointing any root leaves it where it is.
- Absolute configured values continue to override their anchor; relative values resolve
  against it. `Grimoire.Hub.Runtime.Paths` remains the only namespace permitted to read
  ambient process context (ADR-009 rule, unchanged).

Everything else in ADR-009 stands: one options record, one resolver, fail-fast validation
naming the logical location + configured value + resolved path, auto-creation of writable
data locations, and one `paths_resolved` startup report.

### The configuration file is mandatory and is the only source of defaults

- `backend/src/Grimoire.Hub/appsettings.json` is versioned in git, ships the values for
  all three roots and every sub-path, and is copied to the build output (it already is).
- After binding, the resolver **validates that each of the three roots carries a
  non-empty configured value** and fails with a `GrimoirePathValidationException` naming
  `appsettings.json` and the missing keys when one does not (spec FR-005/SC-006). There
  is no code constant holding a root's default value; the tripwire rule below enforces
  this.
- Precedence is evaluated **per option**, not as a group: CLI switch > environment
  variable (`Grimoire__Paths__*`) > configuration file. This is stock
  `Microsoft.Extensions.Configuration` layering and is unchanged in mechanism — only the
  terminal "code defaults" step disappears.
- Sub-path keys stay in the configuration file only. Adding a new runtime location means
  adding a `GrimoirePathOptions` field and an `appsettings.json` key — **never** a switch.

### The agent directory is the agent runtime, delivered by the agent build

- Each agent project owns its instruction surface as project sources:
  `backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/` — `system-prompt.md`,
  `policy.json`, and (Ingest only) `default-user-prompt.md`, matching the profile's
  `RequiredInstructionDocuments` (ADR-007's document set is unchanged; only its home
  moves). They are declared as `Content` with `CopyToOutputDirectory`, so they land in the
  agent's own `$(OutDir)` under `Instructions/` alongside its assemblies.
- `backend/Directory.Build.targets` defines a **`PublishAgentRuntime`** target that runs
  after `Build` for any project declaring `<GrimoireAgentId>`, and copies the agent's
  **entire build output** — `$(OutDir)**` — to `$(GrimoireAgentDir)/$(GrimoireAgentId)/`.
  One copy delivers the worker DLL, `deps.json`, `runtimeconfig.json`, every dependency
  assembly, and the instruction documents together, so the destination is a directory the
  agent can actually be launched from. (Copying a *selection* of assemblies would break
  `deps.json` resolution — the failure mode `Grimoire.EvalRunner`'s
  `AgentProcessInvoker` already documents.)
- The target **clears `$(GrimoireAgentDir)/$(GrimoireAgentId)/` before copying**, so a
  build leaves exactly the current artifact set with no stale assembly or renamed
  instruction file surviving (SC-008). It only ever deletes the agent-id subfolder it owns,
  never `GrimoireAgentDir` itself or anything beside it.
- Every build refreshes the destination, so hand edits in the output are overwritten by
  design — durable instruction changes are made in the agent's sources (Principle V:
  instruction changes stay instruction-file changes, they simply have one authoritative
  home now).
- `GrimoireAgentDir` defaults to the repository-relative `.grimoire/agents` and is the
  **supported redirect mechanism** (FR-012):
  `dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=/srv/grimoire/agents`.
- The hub **never writes** anything under the agent directory. It validates the directory,
  each agent-type subfolder, each required instruction document and each worker DLL, and
  fails naming `agent_dir` when the directory is missing or holds no agent runtime
  (FR-013/SC-007).
- **ADR-002's boundary is preserved and made clearer**: the Hub still holds no assembly
  reference to any agent. The agent directory is populated by each agent's *own* build, and
  the Hub only ever spawns a child process from it — dispatch-only, as ADR-002 requires.

### The hub consumes build artifacts and never produces them

This is the single principle behind both halves of the agent story — instruction files and
worker binaries alike are produced by `dotnet build` and only ever *read* by the hub.

- **One launch mode.** `AgentProcessHost` launches every agent as
  `dotnet <AgentDir>/<agent-id>/Grimoire.<Type>Agent.dll`. The `.csproj` →
  `dotnet run --project` branch and the bare-executable branch are **deleted** from all five
  spawn sites (Ingest submit, Ingest run-to-exit, Query, Lint, remediation). No production
  code path can invoke a build, a restore, or MSBuild.
- **Fail early, not mid-request.** A missing worker DLL is caught by the existing
  `RequiredInput` validation at startup — before serving or before a CLI command runs —
  with a reason that tells the operator what to do:
  `Grimoire.IngestAgent.dll not found in the agent directory. Build first: dotnet build
  backend/Grimoire.slnx`. This mirrors the wording `Grimoire.EvalRunner`'s
  `AgentProcessInvoker.ResolveAgentDllPath` has always used; the hub adopts the eval
  runner's already-correct discipline rather than inventing a second one.
- **Running from source is a build step, not a launch mode.** A developer runs
  `dotnet build backend/Grimoire.slnx` and starts the hub; there is no configuration that
  makes the hub compile an agent for them. This retires ADR-009's "launch configurations
  must pass the agent-worker path when running the worker from source" consequence.
- Rule R4 below makes the removal permanent: no production assembly may carry the IL
  string literals that constitute a build invocation.

### Eval fixtures and eval instruction resolution are repo-anchored

- Recordings move from `data/evals/recordings/` to
  `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/` (ADR-012's recording-store
  *location* is amended; its format, fingerprint semantics and staleness-gate role are
  untouched). `EvalPaths.RecordingsRoot` resolves there; the `--recordings-root` switch is
  removed from the eval runner CLI (FR-016/SC-009). `recordings` becomes a reserved
  directory name under `Fixtures/` — no eval scenario may be named `recordings`.
- `EvalPaths` resolves instructions from the **agent project sources**
  (`backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions`), not from the runtime
  agent directory and not from build output, so an eval run needs neither a prior agent
  build nor any hub configuration (FR-017/FR-018/SC-010).
- `EvalPaths.LocalEnvPath` becomes `<repo-root>/.env`, agreeing with the hub's
  project-root secrets file (FR-019/SC-011).

### Consequences

- Good: a first run needs nothing but a checkout and an agent build; the operator-facing
  surface is three switches whose effect is obvious, and the full effective layout is
  readable in one versioned file.
- Good: instruction files can no longer drift from the agent that loads them, and a
  relocated agent directory is refreshed by the same build that produces the worker.
- Good: **a default-configuration hub can actually dispatch.** Because the Hub's output
  never contained the agent DLLs, the previous default worker location resolved to a file
  that no build produces; the whole point of spec SC-001 ("hub commands succeed with no
  flags") was unreachable without this change. `--agent-dir` now governs the complete agent
  runtime — one option, one directory, binaries and instructions together — which is what
  spec FR-008 ("instruction files **and runtime data** for every agent type") asks for.
- Good: eval runs become independent of operator configuration and of build state,
  removing a class of "works on my machine" eval divergence.
- Good: agent start becomes a process spawn and nothing else. A running hub can no longer
  restore packages or compile inside a request, so agent start latency is bounded, compile
  errors can no longer masquerade as agent-run failures, and the hub no longer contends
  with a concurrent `dotnet test` or IDE build over `obj/` and NuGet locks. ADR-008's
  supervision and heartbeat timings regain the "process start" assumption they were
  written under.
- **Bad / deliberate**: conversations, findings, and remediation-task records move from
  the git-ignored data directory into the wiki directory (spec clarification 2026-08-06 —
  they are agent output). This reverses the ADR-003/ADR-009 placement of that bookkeeping
  as internal, git-ignored state: an operator who version-controls the wiki will now see
  it. Accepted as the clarified product decision; the wiki directory carries its own
  `.gitignore` if an operator wants them excluded.
- **Bad**: this is a breaking configuration change. Thirteen switches disappear and are
  rejected by the CLI parser's standard "unrecognized option" error, with no aliases and
  no deprecation window (pre-1.0, clarification 2026-08-06). No on-disk data is migrated;
  relocating a prior `data/`, `wiki/`, `tasks/`, `conversations/`, or `remediation-tasks/`
  layout is a manual operator step (FR-014).
- **Bad**: each agent-type subfolder carries its own copy of the shared dependency set
  (`Grimoire.Domain`, `Grimoire.AgentRuntime`, OpenTelemetry, the model SDK) — roughly 30
  files × 3 agents instead of one shared set. Accepted: it is a few megabytes, it is what
  makes each subfolder independently launchable with a correct `deps.json`, and per-agent
  isolation is the honest shape for processes whose capability sets differ by profile
  (ADR-013).
- **Bad**: rebuilding while the hub is running rewrites the directory the hub launches
  agents from. The target clears and recopies its agent-id subfolder, so an in-flight agent
  keeps its already-opened files on Unix but a rebuild may fail on locked files on Windows.
  Rebuild-then-restart is the supported sequence; this ADR does not add coordination
  between the build and a running hub.
- **Bad**: the `dotnet run --project` dev convenience disappears. A developer who edits an
  agent must now build before the hub picks the change up, instead of having the next
  dispatch rebuild it implicitly. Accepted deliberately: the implicit rebuild is precisely
  the behavior that makes a running hub's agent latency and failure modes unpredictable,
  and `dotnet build backend/Grimoire.slnx` is already the documented first step everywhere
  else (eval runner, integration tests, CI).
- **Bad**: ADR-019's devcontainer credential story is amended — the mounted/bind-mounted
  secrets path becomes `<repo-root>/.env` instead of `<base>/data/.env`. Its substance
  (no new credential mechanism, git-ignored file, `.env-example` as the declared variable
  surface) is unchanged.
- Neutral: `ResolvedGrimoirePaths.ContentRoot` is renamed `WikiDir` to match the spec's
  Ubiquitous Language. The `ContentRootPaths` / `RawStoragePaths` projection types and the
  agent's `--wiki-root` CLI argument keep their names; the agent-side contract
  (ADR-002/007/011) is untouched.
- Neutral: `HubPathSettings` shrinks from sixteen properties to three; ADR-020's
  "all commands accept the ADR-009 path switches" clause holds unchanged in substance —
  the catalog it mirrors is simply smaller.

## Superseded and amended decisions

| ADR | What changes |
| --- | --- |
| ADR-009 | `BaseDir` and the "two homes beneath the base" layout are replaced by three independent roots; "> code defaults" is removed from the precedence chain; per-location switches are replaced by three capped switches; the secrets file and agent instruction files move out of the data directory; the agent-worker path stops being configurable and its `.csproj`/executable launch modes are deleted, retiring the "launch configurations must pass the agent-worker path when running the worker from source" consequence. Composition point, no-ambient-discovery rule, fail-fast validation, auto-creation and the startup report are retained verbatim. |
| ADR-002 | The spawn model, the file-based child-process contract, and the per-spawn argument set are unchanged, and the no-assembly-reference rule is preserved (the agent directory is filled by each agent's own build, not by the Hub's). The launch *mechanism* narrows from three modes to one (`dotnet <AgentDir>/<agent-id>/Grimoire.<Type>Agent.dll`). |
| ADR-007 | Instruction documents are grouped under `Instructions/` inside each agent-type subfolder rather than sitting at its root, so they stay legible next to the agent's assemblies. Document names, fail-closed loading and SHA-256 traceability are unchanged. |
| ADR-007 | The instruction *document set* is unchanged. Their home moves from `data/agents/<agent>/` to agent-project `Instructions/` sources, distributed to `<AgentDir>/<agent>/` by the build. |
| ADR-012 | The recording store's *location* moves to `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/`; format, fingerprints, and the staleness merge gate are unchanged. The `--recordings-root` switch is removed. |
| ADR-019 | The secrets file the devcontainer exposes is `<repo-root>/.env`, not `<base>/data/.env`. |
| ADR-020 | `HubPathSettings` mirrors the three-entry catalog; the root help's "Server options" section shrinks accordingly. The parity requirement itself is unchanged. |

## Structural Enforcement (Constitution III)

Each rule ships with a Red/Green probe (deliberate violation → verified failure →
removal) before feature code, and runs in the standard PR pipeline.

| Rule | Statement | Test |
| --- | --- | --- |
| **R1** | `PathSwitchCatalog.All` contains exactly three entries — `--data-dir`, `--agent-dir`, `--wiki-dir` — and `HubPathSettings` declares exactly one `[CommandOption]` per entry. Adding a fourth path switch fails the build. | `Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests` (probe: add a fourth catalog entry) |
| **R2** | No production assembly contains an IL string literal equal to a configured root's default value (`.grimoire`, `llm-wiki`) — the defaults exist only in `appsettings.json`. Tripwire against reintroducing code-level path defaults, same idiom as ADR-009's `rev-parse` tripwire. | `Grimoire.ArchTests/NoCodeLevelPathDefaultsRuleTests` (probe: reintroduce a `DefaultDataDirName` constant) |
| **R3** | No production assembly outside `Grimoire.Hub.Runtime.Paths` references an agent-instruction filename literal (`system-prompt.md`, `default-user-prompt.md`, `policy.json`) as a write target — the hub composes instruction *paths*, never instruction *content* (Principle V). | `Grimoire.ArchTests/InstructionAuthorshipBoundaryRuleTests` (probe: a Hub type writing `system-prompt.md`) |
| **R4** | No production assembly contains the IL string literals that constitute a build invocation — `.csproj`, `--project`, `msbuild`, or `"build"`/`"restore"` as a `dotnet` argument. Tripwire against a process ever building instead of launching, same idiom as ADR-009's `rev-parse` tripwire. `Grimoire.EvalRunner`'s *diagnostic message* strings (`"Build first: dotnet build …"`, capture-command hints) are exempt by an explicit, documented allow-list of message-only literals. | `Grimoire.ArchTests/NoRuntimeBuildInvocationRuleTests` (probe: restore the `.csproj` → `dotnet run --project` branch in `AgentProcessHost`) |
| ADR-009 (existing) | Ambient process-context reads confined to `Grimoire.Hub.Runtime.Paths`; no `rev-parse` / `--show-toplevel` literals. | existing `RuntimePathsBoundaryRuleTests`, unchanged |
| ADR-010 C4 (existing) | Process spawning in the Hub confined to `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` and the MarkItDown adapter. | existing rule, unchanged scope — R4 narrows *what* that adapter may spawn |
| ADR-002 (existing, newly load-bearing) | `Grimoire.Hub` must not reference any agent assembly. This rule is what makes the agent directory the only possible home for the agent runtime; it is cited here so the reasoning is not rediscovered a third time. | existing `HubAgentDispatchBoundaryRuleTests`, unchanged |

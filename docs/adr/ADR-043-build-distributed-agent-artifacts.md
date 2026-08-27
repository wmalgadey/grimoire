---
status: accepted
supersedes: ADR-022
superseded_by: []
reason: null
---

# ADR-043: Build-Distributed Agent Artifacts and Single Launch Mode

## Context and Problem Statement

The Hub spawns each agent as a child process (ADR-036), which raises the question this ADR
decides: where does the agent runtime — worker binaries *and* instruction files — come
from, and how is it launched? Historically the two halves lived apart and neither was
delivered: instruction files were hand-maintained, git-tracked files inside the runtime
data directory, and the agent assemblies were nowhere the Hub could reach — the
Hub→agent assembly-reference ban (ADR-036's dispatch-only Boundary Rule) means building
the solution never places agent DLLs in the Hub's output. `AgentProcessHost` compensated
by branching on the configured worker path's extension, and its `.csproj` branch let a
*running* Hub trigger a NuGet restore and MSBuild compile at dispatch time — unbounded
agent start latency, compile errors surfacing as agent-run failures, and lock contention
with any concurrent build. A default-configuration Hub could not dispatch at all.

## Decision Drivers

- The Hub must consume build artifacts, never produce them: no production code path may
  invoke a build, a restore, or MSBuild (author directive 2026-08-07).
- Everything an agent needs — binaries and instructions — must sit in one directory after
  the build and be consumed from there, so a relocated agent directory stays current
  across rebuilds and instruction files cannot drift from the agent that loads them.
- Agent start must be a process spawn with bounded latency; ADR-038's supervision and
  heartbeat timings assume process start, not process build.
- The Hub must not author instruction *content* (Constitution Principle V) — it composes
  paths and reads what the build delivered.
- Eval runs must resolve instructions and fixtures without a prior agent build or any hub
  configuration, so evals cannot diverge on operator state.

## Considered Options

### Artifact distribution

1. **The agent build copies its entire output — assemblies, `deps.json`,
   `runtimeconfig.json`, and instruction files — into `<AgentDir>/<agent-id>/`,
   redirectable via a build property.**
2. **Instruction files only; assemblies stay beside the hub binaries.** Rejected: the
   Hub→agent reference ban means nothing puts agent DLLs in the Hub's output, leaving a
   default-configuration hub unable to dispatch — the very defect that produced the
   `.csproj` launch branch.
3. **Instruction files hand-maintained under the data directory** (the prior status quo).
   Rejected: nothing keeps a relocated agent directory current across rebuilds when the
   files are loose data.
4. **Instruction files embedded as assembly resources, written out by the hub at
   startup.** Rejected: it makes the hub the author of instruction content (Principle V)
   and destroys the operator's ability to read the effective instructions on disk.
5. **A `ProjectReference` with `ReferenceOutputAssembly="false"` from the Hub to each
   agent.** Rejected: it inverts the dispatch-only relationship for build ordering, still
   needs a second mechanism for instruction files, and offers no redirect for a custom
   agent directory.

### Launch mode

1. **One launch mode — `dotnet <worker>.dll`**; a missing worker DLL is a startup failure
   telling the operator to build.
2. **Keep the `.csproj` build-at-dispatch branch behind an opt-in flag.** Rejected: the
   failure mode (a build inside a request) is not worth making available at all.
3. **Keep the branch but pre-build at hub startup instead of at dispatch.** Rejected: the
   hub still invokes MSBuild and still needs a source tree, which a deployed installation
   does not have.

## Decision Outcome

Chosen option: **build-distributed per-agent directories with a single launch mode**
(distribution option 1 + launch option 1), because it is the only combination in which a
default-configuration hub can dispatch, artifacts and instructions cannot drift apart,
and no production process ever builds anything.

- **Each agent project owns its instruction files as sources**
  (`backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/`), declared as `Content`
  with `CopyToOutputDirectory` so they land in the agent's own build output under
  `Instructions/` alongside its assemblies. The instruction *surface* itself — document
  set, fail-closed loading, SHA-256 traceability — is owned by
  [ADR-007](ADR-007-agent-instruction-surface.md) and unchanged; this ADR fixes where the
  authoritative copy lives and how it reaches the agent: the build-output copy under the
  agent directory is what the agent loads at runtime. Hand edits in the output are
  overwritten by the next build by design — durable instruction changes are made in the
  agent's sources.
- **The build distributes the whole runtime.** `backend/Directory.Build.targets` defines
  a `PublishAgentRuntime` target that runs after `Build` for any project declaring
  `<GrimoireAgentId>` and copies the agent's entire build output (`$(OutDir)**`) to
  `$(GrimoireAgentDir)/$(GrimoireAgentId)/` — worker DLL, `deps.json`,
  `runtimeconfig.json`, every dependency assembly, and the instruction documents in one
  copy, so the destination is a directory the agent can actually be launched from. The
  target clears its own agent-id subfolder before copying (never `GrimoireAgentDir`
  itself), so a build leaves exactly the current artifact set with no stale file
  surviving. `GrimoireAgentDir` defaults to the repository-relative `.grimoire/agents`
  and is the supported redirect (`dotnet build backend/Grimoire.slnx
  -p:GrimoireAgentDir=…`). The per-agent-type subfolder names and the `Instructions/`
  grouping are part of this build contract and are not configurable anywhere.
- **One launch mode.** The Hub launches every agent as
  `dotnet <AgentDir>/<agent-id>/Grimoire.<Type>Agent.dll`. There is no `.csproj`/`dotnet
  run --project` branch and no bare-executable branch; no production code path can invoke
  a build or a restore. A missing worker DLL or empty agent directory is a fail-fast
  startup failure naming `agent_dir` and telling the operator to build first
  (`dotnet build backend/Grimoire.slnx`). Running from source is a build step, not a
  launch mode. The Hub never writes anything under the agent directory — it validates and
  reads.
- **Boundary Rule — no runtime build invocation** (formerly ADR-022's rule R4): no
  production assembly contains the IL string literals that constitute a build invocation
  (`.csproj`, `--project`, `msbuild`, or `build`/`restore` as a `dotnet` argument), with
  an explicit documented allow-list for message-only diagnostic literals (e.g. the
  eval runner's "Build first: dotnet build …" hint). Enforced by the Red/Green-probed
  structural test `Grimoire.ArchTests/NoRuntimeBuildInvocationRuleTests`.
- **Boundary Rule — instruction authorship stays outside the harness** (formerly
  ADR-022's rule R3): no production assembly outside `Grimoire.Hub.Runtime.Paths`
  references an agent-instruction filename literal (`system-prompt.md`,
  `default-user-prompt.md`, `policy.json`) as a write target — the hub composes
  instruction *paths*, never instruction *content* (Principle V). Enforced by the
  Red/Green-probed structural test
  `Grimoire.ArchTests/InstructionAuthorshipBoundaryRuleTests`.
- The build contract itself is verified behaviorally: `AgentDirBuildContractTests`
  (`Grimoire.IntegrationTests`) runs a real `dotnet build` with a redirected
  `GrimoireAgentDir` and asserts every agent type gets a complete, launchable,
  clear-then-copied runtime.
- **Eval fixtures and eval instruction resolution are repo-anchored.** Recordings live in
  test-project sources at `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/`
  (`recordings` is a reserved directory name under `Fixtures/`), and `EvalPaths` resolves
  instruction files from the agent *project sources*, not from build output or the
  runtime agent directory — an eval run needs neither a prior agent build nor any hub
  configuration. `EvalPaths.LocalEnvPath` is `<repo-root>/.env`, agreeing with the hub's
  working-directory-anchored secrets file. The recorded-replay format, fingerprint
  semantics, and staleness gate are owned by
  [ADR-012](ADR-012-eval-runner-recorded-replay.md) and unchanged.

### Consequences

- Good, because a default-configuration hub can actually dispatch: `--agent-dir` governs
  the complete agent runtime — one option, one directory, binaries and instructions
  together, delivered by the same build.
- Good, because agent start is a process spawn and nothing else — latency is bounded,
  compile errors cannot masquerade as agent-run failures, and the hub never contends with
  a concurrent build over `obj/` or NuGet locks.
- Good, because instruction files cannot drift from the agent that loads them, and a
  relocated agent directory is refreshed by the same build that produces the worker.
- Bad, because each agent-type subfolder carries its own copy of the shared dependency
  set (roughly 30 files per agent). Accepted: a few megabytes buys independently
  launchable directories with correct `deps.json` resolution, and per-agent isolation is
  the honest shape for processes whose capability sets differ by profile (ADR-013).
- Bad, because rebuilding while the hub is running rewrites the directory the hub
  launches agents from; rebuild-then-restart is the supported sequence, with no
  build/hub coordination.
- Bad, because the `dotnet run --project` dev convenience is gone — a developer who edits
  an agent builds before the hub picks the change up. Accepted deliberately: the implicit
  rebuild is precisely what made a running hub's agent latency and failure modes
  unpredictable.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent id distributed under the same
  pattern (a project declaring `<GrimoireAgentId>`, its output published to
  `<AgentDir>/<new-id>/`, launched in the same single mode); new files in an agent's
  build output; new message-only literals added to the documented build-invocation
  allow-list alongside their diagnostic.
- **Invalidations (would require full supersession):** any form of build-at-dispatch or
  build-at-startup in a production process; agent artifacts fetched or materialized at
  runtime instead of delivered by the build (downloads, embedded-resource extraction,
  hub-written files under the agent directory); a second launch mode (project files,
  bare executables, or anything beside `dotnet <worker>.dll`).

## More Information

Supersedes [ADR-022](ADR-022-minimal-directory-configuration-surface.md). ADR-022's other
two aspects are re-decided in [ADR-041](ADR-041-independent-directory-roots.md) (the
independent directory roots, including the agent root's switch) and
[ADR-042](ADR-042-mandatory-configuration-file.md) (the mandatory configuration file as
the sole source of defaults).

Read alongside: [ADR-036](ADR-036-agent-child-process-spawn-contract.md) — the spawn
contract that launches from the directory this ADR has the build populate, and the
dispatch-only Boundary Rule that makes the agent directory, not the Hub's output, the
only possible home of the agent runtime;
[ADR-007](ADR-007-agent-instruction-surface.md) — the instruction surface (document set,
fail-closed loading, SHA-256 traceability) whose files this ADR's build distributes;
[ADR-012](ADR-012-eval-runner-recorded-replay.md) — the recorded-replay model whose
fixtures and instruction resolution this ADR anchors in the repository;
[ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md) — agent packaging and
naming; [ADR-034](ADR-034-path-and-subprocess-containment-hardening.md) — the spawn-site
allowlist constraining who may launch the distributed artifacts. None of their decisions
are restated or narrowed here.

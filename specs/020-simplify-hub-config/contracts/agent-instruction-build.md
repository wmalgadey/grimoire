# Contract: Agent Build Output

**Feature**: `020-simplify-hub-config` | **Governs**: FR-008, FR-011, FR-012, FR-013,
FR-017 + the 2026-08-07 no-runtime-build directive | **Verified by**: SC-007, SC-008,
SC-010

Everything an agent needs at runtime — its worker binary, every dependency assembly, and
its instruction files — is produced by `dotnet build` into **one directory per agent type**
under the agent directory, and only ever **read** by the hub. The hub consumes build
artifacts and never produces them: it does not author instruction content (Constitution
Principle V) and it does not compile, restore, or invoke MSBuild (ADR-022 rule R4).

`Grimoire.Hub` holds no assembly reference to any agent (ADR-002,
`HubAgentDispatchBoundaryRuleTests`), so the agent directory — filled by each agent's own
build — is the only place the agent runtime can live.

---

## 1. Source layout

```text
backend/src/Grimoire.IngestAgent/Instructions/
├── system-prompt.md
├── default-user-prompt.md
└── policy.json

backend/src/Grimoire.QueryAgent/Instructions/
├── system-prompt.md
└── policy.json

backend/src/Grimoire.LintAgent/Instructions/
├── system-prompt.md
└── policy.json
```

The document set per agent is exactly its `AgentProfile.RequiredInstructionDocuments`
(ADR-007, unchanged) plus `policy.json` (ADR-006). These files are the **only**
authoritative copies; nothing else in the repository holds instruction content.

Each agent `.csproj` declares them as build content so they reach the agent's own output:

```xml
<Content Include="Instructions\**" CopyToOutputDirectory="PreserveNewest" />
```

## 2. Build properties

| Property | Declared in | Default | Purpose |
| --- | --- | --- | --- |
| `GrimoireAgentId` | each agent `.csproj` | — (`ingest` / `query` / `lint`) | Marks a project as an agent and names its output subfolder. |
| `GrimoireAgentDir` | `backend/Directory.Build.targets` | repo-relative `.grimoire/agents` | Destination root; the supported operator redirect (FR-012). |

## 3. Target

`PublishAgentRuntime`, `AfterTargets="Build"`, condition `'$(GrimoireAgentId)' != ''`:

```text
clear   $(GrimoireAgentDir)/$(GrimoireAgentId)/
copy    $(OutDir)**  →  $(GrimoireAgentDir)/$(GrimoireAgentId)/
```

- Copies the agent's **entire build output**, not a selection: the worker DLL,
  `deps.json`, `runtimeconfig.json`, every dependency assembly, and `Instructions/`.
  `deps.json` enumerates every assembly the app expects, so a partial copy fails at
  launch — the failure `Grimoire.EvalRunner`'s `AgentProcessInvoker` already documents.
- **Clears the agent-id subfolder first**, so the destination states exactly what the
  current build produced — no stale assembly, no renamed instruction file (FR-011/SC-008).
  It deletes only `$(GrimoireAgentDir)/$(GrimoireAgentId)/`, never `GrimoireAgentDir`
  itself or anything beside it.
- Creates the destination directory when absent.
- Runs on every invocation path that builds the project: `dotnet build`, `dotnet test`,
  `dotnet run`, IDE build, CI.
- **Not supported**: rebuilding while the hub is running. The target rewrites the directory
  the hub launches agents from; rebuild, then restart.

### Redirecting

```bash
dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=/srv/grimoire/agents
```

Point the hub at the same directory to complete the loop (spec US4):

```bash
dotnet run --project backend/src/Grimoire.Hub -- --agent-dir /srv/grimoire/agents
```

## 4. Runtime layout the hub expects

```text
<AgentDir>/
├── ingest/
│   ├── Grimoire.IngestAgent.dll                  ← worker
│   ├── Grimoire.IngestAgent.deps.json
│   ├── Grimoire.IngestAgent.runtimeconfig.json
│   ├── Grimoire.AgentRuntime.dll  Grimoire.Domain.dll  Anthropic.dll  OpenTelemetry.*.dll  …
│   └── Instructions/  system-prompt.md  default-user-prompt.md  policy.json
├── query/
│   ├── Grimoire.QueryAgent.dll  + deps  + assemblies
│   └── Instructions/  system-prompt.md  policy.json
└── lint/
    ├── Grimoire.LintAgent.dll  + deps  + assemblies
    └── Instructions/  system-prompt.md  policy.json
```

Agent-type subfolder names (`ingest`, `query`, `lint`), the worker filenames, and the
`Instructions/` grouping are fixed — not configurable in any tier (FR-008).

Each subfolder carries its own copy of the shared dependency set. That duplication is what
makes it independently launchable with a correct `deps.json`, and matches ADR-013's
per-profile capability model.

## 5. Hub validation

| Condition | Result |
| --- | --- |
| `<AgentDir>` missing | fail, `location=agent_dir`, reason `required directory does not exist` |
| `<AgentDir>` present but holds no agent runtime | fail, `location=agent_dir`, reason `agent directory contains no agent runtime` |
| An agent-type subfolder missing | fail, naming that subfolder |
| A worker DLL missing | fail, naming it + `Build first: dotnet build backend/Grimoire.slnx` |
| A required instruction document missing | fail, naming that logical location |

The hub never creates or writes anything under `<AgentDir>` (structural rule R3).

## 6. Hand edits

Editing anything under `<AgentDir>` works until the next build, which clears and recopies
the agent-id subfolder. This is intentional: durable instruction changes are made in the
agent's `Instructions/` sources, which are versioned with the agent and are what the eval
suite fingerprints.

## 7. Launch contract

| Agent | Worker artifact | Resolved location |
| --- | --- | --- |
| ingest | `Grimoire.IngestAgent.dll` | `<AgentDir>/ingest/Grimoire.IngestAgent.dll` |
| query | `Grimoire.QueryAgent.dll` | `<AgentDir>/query/Grimoire.QueryAgent.dll` |
| lint | `Grimoire.LintAgent.dll` | `<AgentDir>/lint/Grimoire.LintAgent.dll` |

**One launch mode.** Every agent spawn is:

```text
dotnet <AgentDir>/<agent-id>/Grimoire.<Type>Agent.dll --task-id … --wiki-root … [agent args]
```

The previous three-way branch on the configured path's extension is removed: `.csproj` →
`dotnet run --project` (which built the agent inside a request) and the bare-executable
mode no longer exist. The ADR-002 argument set passed to the child process is unchanged —
only the way the process is started narrows.

`<ProcessBaseDirectory>` no longer anchors any path. Its one remaining use is pinning the
host's `ContentRootPath` so `appsettings.json` loads from beside `Grimoire.Hub.dll`.

**No build at runtime.** No production code path invokes `dotnet build`, `dotnet run`,
`dotnet restore`, or MSBuild. Enforced by ADR-022 rule R4
(`NoRuntimeBuildInvocationRuleTests`), Red/Green probed by restoring the `.csproj` branch.

**Missing worker.** Caught at startup validation, before serving or before a CLI command
runs — never at dispatch:

```text
agent_worker_ingest: configured '(default)' resolved to '<AgentDir>/ingest/Grimoire.IngestAgent.dll' —
Grimoire.IngestAgent.dll not found in the agent directory.
Build first: dotnet build backend/Grimoire.slnx
```

This mirrors the wording `Grimoire.EvalRunner.Workspace.AgentProcessInvoker` already uses;
the hub adopts it rather than inventing a second story.

**Running from source** is therefore a build step, not a launch mode: build the solution,
then start the hub. There is no configuration that makes the hub compile an agent.

## 8. Eval runner resolution

`Grimoire.EvalRunner` resolves instructions from the **sources**
(`backend/src/Grimoire.*Agent/Instructions`), repo-anchored — never from `<AgentDir>` and
never from build output. An eval run therefore requires no agent build and is unaffected by
hub configuration (FR-017/FR-018/SC-010). ADR-012's instruction fingerprints are computed
over these same source files, so the staleness merge gate keeps working unchanged.

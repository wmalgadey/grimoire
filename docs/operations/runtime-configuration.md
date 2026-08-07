# Runtime Path Configuration

Operator-facing reference for where Grimoire reads and writes data, and how to configure
it. Full contract: [`specs/020-simplify-hub-config/contracts/directory-options.md`](../../specs/020-simplify-hub-config/contracts/directory-options.md).
Defaults and resolution rules: [`specs/020-simplify-hub-config/data-model.md`](../../specs/020-simplify-hub-config/data-model.md).
Worked examples: [`specs/020-simplify-hub-config/quickstart.md`](../../specs/020-simplify-hub-config/quickstart.md).
Architectural rationale: [ADR-022](../adr/ADR-022-minimal-directory-configuration-surface.md)
(supersedes [ADR-009](../adr/ADR-009-runtime-path-configuration.md)'s switch surface).

## The three roots

Every runtime location Grimoire uses is composed in one place
(`Grimoire.Hub.Runtime.Paths.GrimoirePathResolver`) beneath exactly three independently
configurable roots, none nested inside one another under any configuration:

- **The data directory** (`.grimoire` by default, cwd-anchored) — internal harness
  runtime state: raw intake storage, the operational-state database,
  write-coordination locks, and, by default, the agent directory. Never git-tracked.
- **The wiki directory** (`llm-wiki` by default, cwd-anchored, independent of the data
  directory) — the knowledge base an agent maintains (`index.md`, `log.md`, topical
  article subfolders) plus agent-produced results: tasks, conversations, findings, and
  remediation-task records. Deliberately kept independent of the data directory so it
  can be committed to its own git repository.
- **The agent directory** (`agents` by default, anchored under the resolved data
  directory) — the complete agent runtime (worker binaries, dependency assemblies, and
  instruction files) for every agent type, in per-agent-type subfolders. Produced and
  refreshed by the agent build (`backend/Directory.Build.targets`'s `PublishAgentRuntime`
  target) — the Hub never writes here, only reads.

Every other runtime location (raw intake, the state DB, write-locks, tasks,
conversations, findings, remediation tasks) is a fixed sub-path anchored under one of
these three roots — configurable only through `appsettings.json`, never via a CLI
switch (see "Sub-paths" below).

The secrets file (`.env`) is the one location anchored independently of all three
roots — always at the process working directory — so relocating runtime data, the
agent directory, or the wiki never separates an operator from their credentials.

## Configuration table

Precedence for every location: **command line > environment > `appsettings.json`**.
There is no fourth "code default" tier — `appsettings.json` is the sole source of
default values (ADR-022). Relative values always resolve against the documented
anchor below — never against a discovered repository or project root; the application
does not invoke `git` or any other version-control tooling at runtime.

| Location | CLI switch | Environment variable | Shipped default | Resolves against | Kind |
| --- | --- | --- | --- | --- | --- |
| Data directory | `--data-dir` | `Grimoire__Paths__DataDir` | `.grimoire` | process working directory | required input (must exist) |
| Wiki directory | `--wiki-dir` | `Grimoire__Paths__WikiDir` | `llm-wiki` | process working directory | writable (auto-created) |
| Agent directory | `--agent-dir` | `Grimoire__Paths__AgentDir` | `agents` | data directory | required input (must hold a complete agent runtime) |
| Secrets file | — | — | `.env` | process working directory | required input |

### Sub-paths (`appsettings.json`-only, no CLI switch)

| Location | Configuration key | Shipped default | Resolves against |
| --- | --- | --- | --- |
| Raw intake storage | `Grimoire:Paths:RawDir` | `raw` | data directory |
| Operational state DB | `Grimoire:Paths:StateDb` | `state/operational-state.db` | data directory |
| Write-coordination locks | `Grimoire:Paths:WriteLocksDir` | `write-locks` | data directory |
| Task artifacts | `Grimoire:Paths:TasksDir` | `tasks` | wiki directory |
| Conversation Records | `Grimoire:Paths:ConversationsDir` | `conversations` | wiki directory |
| Findings Reports | `Grimoire:Paths:FindingsDir` | `findings` | wiki directory |
| Remediation Task Records | `Grimoire:Paths:RemediationTasksDir` | `remediation-tasks` | wiki directory |

An operator who needs to relocate one of these internal locations without moving its
whole root edits `appsettings.json` directly — no CLI switch exists for any sub-path
(FR-015), and every other sub-path under the same root stays at its own default.

Required-input locations that are missing, or of the wrong kind (a file where a
directory is expected, or vice versa), abort startup immediately with a message naming
the location, the configured value, and the resolved path. A root missing from every
configuration tier (including `appsettings.json`) fails distinctly, naming the
configuration file and every missing key, before any location is touched. Writable-data
locations are created automatically. Every successful start logs the fully resolved
absolute path of every location (`paths_resolved`), so an operator can always confirm
where data actually lives.

## The agent directory is a build artifact

Unlike the data and wiki directories, the agent directory is never written by the Hub.
It is produced entirely by `dotnet build backend/Grimoire.slnx`, which — for every
agent project — copies that project's complete build output (worker DLL, dependency
assemblies, `deps.json`, `runtimeconfig.json`, and its `Instructions/` folder) into
`<AgentDir>/<agent-id>/`, clearing and replacing that subfolder on every build. To
redirect where the build delivers it:

```bash
dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=/srv/grimoire/agents
```

and point the Hub at the same location with `--agent-dir /srv/grimoire/agents` (or the
matching `Grimoire__Paths__AgentDir` environment variable / `appsettings.json` value).
An agent directory that is missing, empty, or missing one required file (a worker DLL
or an instruction document) fails startup naming exactly what is missing, including a
"Build first: `dotnet build backend/Grimoire.slnx`" hint when a worker binary is absent.

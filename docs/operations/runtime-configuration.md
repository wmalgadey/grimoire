# Runtime Path Configuration

Operator-facing reference for where Grimoire reads and writes data, and how to configure
it. Full contract: [`specs/022-memory-directory-root/contracts/directory-options.md`](../../specs/022-memory-directory-root/contracts/directory-options.md).
Defaults and resolution rules: [`specs/022-memory-directory-root/data-model.md`](../../specs/022-memory-directory-root/data-model.md).
Worked examples: [`specs/022-memory-directory-root/quickstart.md`](../../specs/022-memory-directory-root/quickstart.md).
Architectural rationale: [ADR-022](../adr/ADR-022-minimal-directory-configuration-surface.md),
amended by [ADR-024](../adr/ADR-024-memory-directory-root.md) (supersedes
[ADR-009](../adr/ADR-009-runtime-path-configuration.md)'s switch surface).

## The four roots

Every runtime location Grimoire uses is composed in one place
(`Grimoire.Hub.Runtime.Paths.GrimoirePathResolver`) beneath exactly four independently
configurable roots, none nested inside one another under any configuration:

- **The data directory** (`.grimoire` by default, cwd-anchored) — internal harness
  runtime state: raw intake storage, the operational-state database, and
  write-coordination locks. Never git-tracked.
- **The wiki directory** (`llm-wiki` by default, cwd-anchored, independent of the data
  directory) — the knowledge base an agent maintains: `index.md`, `log.md`, and topical
  article subfolders. Holds only wiki content — no agent process bookkeeping — so it can
  be committed to its own git repository without also carrying harness record-keeping.
- **The agent directory** (`.grimoire/agents` by default, cwd-anchored, independent of
  the data directory) — the complete agent runtime (worker binaries, dependency
  assemblies, and instruction files) for every agent type, in per-agent-type subfolders.
  Produced and refreshed by the agent build (`backend/Directory.Build.targets`'s
  `PublishAgentRuntime` target) — the Hub never writes here, only reads. Its default
  value happens to nest under the data directory's default, but relocating `--data-dir`
  does not move it — the two are resolved independently.
- **The memory directory** (`memory` by default, cwd-anchored, independent of the other
  three roots) — agent process bookkeeping: task artifacts, conversation records,
  findings reports, and remediation task records. Held apart from the wiki directory so
  an operator can back it up, retain it, or exclude it as one unit, independently of the
  wiki content itself.

Every other runtime location (raw intake, the state DB, write-locks, tasks,
conversations, findings, remediation tasks) is a fixed sub-path anchored under one of
these four roots — configurable only through `appsettings.json`, never via a CLI
switch (see "Sub-paths" below).

The secrets file (`.env`) is the one location anchored independently of all four
roots — always at the process working directory — so relocating runtime data, the
agent directory, the wiki, or the memory directory never separates an operator from
their credentials.

## Configuration table

Precedence for every location: **command line > environment > `appsettings.json`**.
There is no fourth "code default" tier — `appsettings.json` is the sole source of
default values (ADR-022). Relative values always resolve against the documented
anchor below — never against a discovered repository or project root; the application
does not invoke `git` or any other version-control tooling at runtime.

`Grimoire:Paths` is grouped by anchoring root in `appsettings.json` — each group's own
`Dir` key is a root, and every sibling key inside a group is a sub-path anchored at that
group's resolved `Dir`. Environment variables nest the same way with a second `__`
(e.g. `Grimoire__Paths__Memory__Dir`). CLI switch names are unaffected by the grouping.

| Location | CLI switch | Environment variable | Shipped default | Resolves against | Kind |
| --- | --- | --- | --- | --- | --- |
| Data directory | `--data-dir` | `Grimoire__Paths__Data__Dir` | `.grimoire` | process working directory | writable (auto-created) |
| Wiki directory | `--wiki-dir` | `Grimoire__Paths__Wiki__Dir` | `llm-wiki` | process working directory | writable (auto-created) |
| Agent directory | `--agent-dir` | `Grimoire__Paths__Agent__Dir` | `.grimoire/agents` | process working directory | required input (must hold a complete agent runtime) |
| Memory directory | `--memory-dir` | `Grimoire__Paths__Memory__Dir` | `memory` | process working directory | writable (auto-created) |
| Secrets file | — | `Grimoire__Paths__SecretsFile` | `.env` | process working directory | required input |

### Sub-paths (`appsettings.json`-only, no CLI switch)

| Location | Configuration key | Shipped default | Resolves against |
| --- | --- | --- | --- |
| Raw intake storage | `Grimoire:Paths:Data:RawDir` | `raw` | data directory |
| Operational state DB | `Grimoire:Paths:Data:StateDb` | `state/operational-state.db` | data directory |
| Write-coordination locks | `Grimoire:Paths:Data:WriteLocksDir` | `write-locks` | data directory |
| Task artifacts | `Grimoire:Paths:Memory:TasksDir` | `tasks` | memory directory |
| Conversation Records | `Grimoire:Paths:Memory:ConversationsDir` | `conversations` | memory directory |
| Findings Reports | `Grimoire:Paths:Memory:FindingsDir` | `findings` | memory directory |
| Remediation Task Records | `Grimoire:Paths:Memory:RemediationTasksDir` | `remediation-tasks` | memory directory |

An operator who needs to relocate one of these internal locations without moving its
whole root edits `appsettings.json` directly — no CLI switch exists for any sub-path
(ADR-024 rule M1), and every other sub-path under the same root stays at its own default.

### Superseded configuration keys

The four-root regrouping (022-memory-directory-root) renamed every configuration key
and environment variable — `Grimoire__Paths__DataDir` became
`Grimoire__Paths__Data__Dir`, and correspondingly for all eleven pre-existing keys. CLI
switch names are unchanged. An operator still exporting an old flat-form environment
variable or `appsettings.json` key gets the ordinary silent-ignore treatment
configuration systems give any unrecognized key: the old key simply does not work, and
the location quietly resolves to its default. The hub does not detect or reject
superseded keys — the project is pre-1.0 (alpha) with no external installations
carrying old key names, so there is nothing to guard against.

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

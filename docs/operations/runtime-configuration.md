# Runtime Path Configuration

Operator-facing reference for where Grimoire reads and writes data, and how to configure
it. Full contract: [`specs/005-content-root-config/contracts/path-configuration.md`](../../specs/005-content-root-config/contracts/path-configuration.md).
Defaults and resolution rules: [`specs/005-content-root-config/data-model.md`](../../specs/005-content-root-config/data-model.md).
Worked examples: [`specs/005-content-root-config/quickstart.md`](../../specs/005-content-root-config/quickstart.md).
Architectural rationale: [ADR-009](../adr/ADR-009-runtime-path-configuration.md).

## The two-home layout

Every runtime location Grimoire uses is composed in one place
(`Grimoire.Hub.Runtime.Paths.GrimoirePathResolver`) beneath a single base directory —
either an explicitly configured `--base-dir`, or the process working directory when none
is given. There are exactly two homes beneath that base:

- **The wiki content root** (`<base>/wiki` by default) — the knowledge base an agent
  maintains: pages, tasks, the catalog (`index.md`), and the ingest log (`log.md`).
  Deliberately kept outside the data directory so it can be committed to its own git
  repository, independently of the application's internal runtime data.
- **The consolidated data directory** (`<base>/data` by default) — every other piece of
  internal runtime data the application owns: raw intake storage, the operational-state
  database, the secrets file, and the agent instruction set.

The two never nest inside one another under any configuration.

## Configuration table

Precedence for every location: **command line > environment > `appsettings.json` >
code default**. Relative values always resolve against the documented anchor below —
never against a discovered repository or project root; the application does not invoke
`git` or any other version-control tooling at runtime.

| Location | CLI switch | Environment variable | Default | Resolves against | Kind |
| --- | --- | --- | --- | --- | --- |
| Base directory | `--base-dir` | `Grimoire__Paths__BaseDir` | process working directory | — | required input (must exist) |
| Wiki content root | `--content-root` | `Grimoire__Paths__ContentRoot` | `wiki` | base directory | writable (auto-created) |
| Data directory | `--data-dir` | `Grimoire__Paths__DataDir` | `data` | base directory | writable (auto-created) |
| Raw intake storage | `--raw-dir` | `Grimoire__Paths__RawDir` | `raw` | data directory | writable (auto-created) |
| Operational state DB | `--state-db` | `Grimoire__Paths__StateDb` | `state/operational-state.db` | data directory | writable (auto-created) |
| Secrets file | `--secrets-file` | `Grimoire__Paths__SecretsFile` | `.env` | data directory | required input |
| Agent instructions dir | `--instructions-dir` | `Grimoire__Paths__InstructionsDir` | `agents/ingest` | data directory | required input |
| Agent worker | `--agent-worker` | `Grimoire__Paths__AgentWorker` | `Grimoire.IngestAgent.dll` | application install directory | required input |

Required-input locations that are missing, or of the wrong kind (a file where a
directory is expected, or vice versa), abort startup immediately with a message naming
the location, the configured value, and the resolved path. Writable-data locations are
created automatically. Every successful start logs the fully resolved absolute path of
all eight locations (`paths_resolved`), so an operator can always confirm where data
actually lives.

## Migrating an existing checkout

A checkout created before this configuration model moved its instruction set and
secrets under the consolidated data directory once, manually — there is no automatic
migration:

```bash
mkdir -p data/state
# wiki/ stays where it is — it already matches the <base>/wiki default.
git mv agents data/agents
[ -d raw ] && mv raw data/raw || mkdir -p data/raw
mv .env data/.env
[ -f backend/data/operational-state.db ] && mv backend/data/operational-state.db data/state/
```

After moving `agents/` to `data/agents/`, the policy file's path prefixes and the
system prompt's path references both had to become content-root-relative (`pages/`,
`tasks/`, `index.md`, `log.md` — no `wiki/` prefix), since the agent now addresses
locations relative to the wiki root it is explicitly given (`--wiki-root`), not a
discovered repository root. `.gitignore` was updated (`backend/data/` →
`data/state/` + `data/raw/`), and `.vscode/launch.json` now sets `cwd` to the
workspace root and passes an absolute `--agent-worker` path — both required because
`dotnet run --project` changes the process's own working directory to the project's
directory, and the agent-worker default resolves against the install directory, not
the base.

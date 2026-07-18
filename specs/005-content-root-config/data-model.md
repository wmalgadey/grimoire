# Data Model: Explicit Path Configuration (005-content-root-config)

This feature introduces no persisted domain entities. Its "data model" is the in-memory
configuration model that every other component consumes.

## GrimoirePathOptions (configuration input)

Bound from configuration section `Grimoire:Paths`. All values are strings; empty/absent
means "use default". Relative values resolve per FR-003 (base directory, else process
working directory).

| Field | Config key | Default | Notes |
| --- | --- | --- | --- |
| `BaseDir` | `Grimoire:Paths:BaseDir` | process working directory | Root for every other relative default |
| `ContentRoot` | `Grimoire:Paths:ContentRoot` | `wiki` (under base) | Wiki content root — deliberately OUTSIDE the data directory so it can be committed to its own git repository |
| `DataDir` | `Grimoire:Paths:DataDir` | `data` (under base) | The consolidated internal runtime data directory |
| `RawDir` | `Grimoire:Paths:RawDir` | `raw` (under data) | Raw intake storage |
| `StateDb` | `Grimoire:Paths:StateDb` | `state/operational-state.db` (under data) | SQLite operational state (ADR-003) |
| `SecretsFile` | `Grimoire:Paths:SecretsFile` | `.env` (under data) | ADR-004 secrets file |
| `InstructionsDir` | `Grimoire:Paths:InstructionsDir` | `agents/ingest` (under data) | ADR-007 instruction surface |
| `AgentWorker` | `Grimoire:Paths:AgentWorker` | `Grimoire.IngestAgent.dll` beside Hub binaries | `.csproj` / `.dll` / executable (research R4) |

Resolution rule: an absolute value is taken as-is; a relative value resolves against
its documented anchor (`ContentRoot` and `DataDir` against base; the four internal
data locations against `DataDir`; `AgentWorker` against the application install
directory). `--content-root` therefore accepts an absolute external wiki exactly as
the prod launch config does today.

## ResolvedGrimoirePaths (validated output)

Produced once at startup by `GrimoirePathResolver`; the only path source injected into
DI. Replaces the repo-root parameters of today's `ContentRootPaths` /
`RawStoragePaths`.

| Field | Kind | Validation |
| --- | --- | --- |
| `BaseDir` | info | must exist (it is the resolution anchor) |
| `DataDir` | writable data | auto-create |
| `ContentRoot` (+ `PagesDir`, `TasksDir`, `IndexPath`, `LogPath`) | writable data | auto-create dirs |
| `RawOriginalsDir`, `RawSourcesDir` | writable data | auto-create |
| `StateDbPath` | writable data | auto-create parent dir |
| `SecretsFilePath` | required input | must exist as file, else startup failure |
| `SystemPromptPath`, `DefaultUserPromptPath`, `PolicyPath` | required input | must exist as files, else startup failure |
| `AgentWorkerPath` | required input | must exist as file, else startup failure |

Internal layout within each root (`pages/`, `tasks/`, `index.md`, `log.md`,
`originals/`, `sources/`, instruction file names) is system-owned and NOT configurable
(spec assumption "internal layout is owned by the system").

## PathLocation (validation/reporting vocabulary)

Each resolved entry carries, for the startup report and error messages:

- `name` — stable logical name (`base_dir`, `data_dir`, `content_root`, `raw_dir`,
  `state_db`, `secrets_file`, `instructions_dir`, `agent_worker`)
- `configuredValue` — the raw configured string (or `(default)`)
- `resolvedPath` — absolute path
- `kind` — `required-input` | `writable-data`
- `source` — `command-line` | `environment` | `config-file` | `default`

These names are the mandatory fields of the log events in `plan.md ## Observability`.

## State transitions

None persisted. Startup sequence: bind options → resolve → validate (fail fast) →
auto-create writable locations → emit `paths_resolved` → register in DI.

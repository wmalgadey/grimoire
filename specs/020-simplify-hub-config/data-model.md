# Phase 1 Data Model: Simplify Hub CLI Configuration

**Feature**: `020-simplify-hub-config` | **Date**: 2026-08-07

This feature has no persisted domain entities. Its "data model" is the **configuration
composition**: the bound options record, the resolved path record, and the validation
states between them. Types below live in `Grimoire.Hub.Runtime.Paths` unless stated.

---

## 1. `GrimoirePathOptions` — configuration input

Bound from the `Grimoire:Paths` section. Every value is a string that is either absolute
(used verbatim) or relative (resolved against the field's anchor).

### Root fields — required, CLI-exposed

| Field | Anchor | `appsettings.json` value | Switch | Required |
| --- | --- | --- | --- | --- |
| `DataDir` | working directory | `.grimoire` | `--data-dir` | **yes** |
| `WikiDir` | working directory | `llm-wiki` | `--wiki-dir` | **yes** |
| `AgentDir` | working directory | `.grimoire/agents` | `--agent-dir` | **yes** |

"Required" means: absent or whitespace after binding ⇒ startup fails naming
`appsettings.json` and the missing key (FR-005). There is no code constant holding these
values (enforced by rule R2).

### Sub-path fields — required, configuration-file only

| Field | Anchor | `appsettings.json` value |
| --- | --- | --- |
| `RawDir` | `DataDir` | `raw` |
| `StateDb` | `DataDir` | `state/operational-state.db` |
| `WriteLocksDir` | `DataDir` | `write-locks` |
| `TasksDir` | `WikiDir` | `tasks` |
| `ConversationsDir` | `WikiDir` | `conversations` |
| `FindingsDir` | `WikiDir` | `findings` |
| `RemediationTasksDir` | `WikiDir` | `remediation-tasks` |
| `SecretsFile` | working directory | `.env` |

No switch exists for any of these (FR-015, enforced by rule R1). `SecretsFile`'s anchor is
the working directory — **not** any root — so no directory option can move it (FR-019).

### Removed fields

`BaseDir`, `ContentRoot` (renamed `WikiDir`), `InstructionsDir`, `QueryInstructionsDir`,
`LintInstructionsDir`, `AgentWorker`, `QueryAgentWorker`, `LintAgentWorker`.

### Removed constants

All `Default*DirName` / `Default*RelativePath` path constants. Retained (filenames, not
configurable locations, not matched by rule R2): `DefaultLintPidFileName`,
`DefaultAgentWorkerFileName`, `DefaultQueryAgentWorkerFileName`,
`DefaultLintAgentWorkerFileName`.

---

## 2. Fixed, non-configurable locations

Derived inside `GrimoirePathResolver` from an already-resolved root — no options field, no
key, no switch (the `index.md` / `lint.pid` precedent).

| Location | Derivation |
| --- | --- |
| `<AgentDir>/ingest`, `/query`, `/lint` | agent-type subfolder (FR-008); name is the agent build contract |
| `<agent subfolder>/Grimoire.{Ingest,Query,Lint}Agent.dll` | agent worker — no longer configurable, always a built `.dll`, launched only as `dotnet <dll>` (R4) |
| `<agent subfolder>/Instructions/system-prompt.md` | required instruction document (all three agents) |
| `<agent subfolder>/Instructions/policy.json` | required instruction document (all three agents) |
| `<AgentDir>/ingest/Instructions/default-user-prompt.md` | required instruction document (Ingest only) |
| `<WikiDir>/index.md`, `<WikiDir>/log.md` | wiki catalog and log |
| `<RawDir>/originals`, `<RawDir>/sources` | raw intake subfolders |
| `<DataDir>/lint.pid` | ADR-020 cross-process lint lock |

`<ProcessBaseDirectory>` is no longer an anchor for any path. Its single remaining use is
pinning the host's `ContentRootPath` so `appsettings.json` loads from beside
`Grimoire.Hub.dll`.

---

## 3. `ResolvedGrimoirePaths` — composition output

Immutable record produced once at startup, registered as the single path source in DI.

**Renamed**: `ContentRoot` → `WikiDir`.
**Removed**: `BaseDir`.
**Restructured**: the six per-agent instruction/worker members collapse into per-agent
groups derived from `AgentDir`.

```text
ResolvedGrimoirePaths(
    DataDir, WikiDir, AgentDir,
    RawOriginalsDir, RawSourcesDir, StateDbPath, WriteLocksDir, LintPidPath,
    TasksDir, ConversationsDir, FindingsDir, RemediationTasksDir,
    IndexPath, LogPath,
    SecretsFilePath,
    Ingest: AgentRuntimePaths, Query: AgentRuntimePaths, Lint: AgentRuntimePaths,
    Locations: IReadOnlyList<PathLocation>)
```

`AgentRuntimePaths(Dir, WorkerPath, InstructionsDir, SystemPromptPath, PolicyPath,
DefaultUserPromptPath?)` groups everything one agent type needs — its subfolder, its worker
DLL, and its instruction surface. `DefaultUserPromptPath` is non-null for Ingest only.
Existing helper methods (`TaskArtifactPathFor`, `ConversationRecordPathFor`,
`FindingsReportPathFor`, `RemediationTaskRecordPathFor`) are unchanged.

```text
Ingest: AgentRuntimePaths(
    Dir              = <AgentDir>/ingest
    WorkerPath       = <AgentDir>/ingest/Grimoire.IngestAgent.dll
    InstructionsDir  = <AgentDir>/ingest/Instructions
    SystemPromptPath = <AgentDir>/ingest/Instructions/system-prompt.md
    …)
```

The three separate `*WorkerPath` members and the six separate instruction members of the
previous record collapse into these three groups — one per agent type.

---

## 4. `PathLocation` — reportable location

Unchanged shape: `(Name, ConfiguredValue, ResolvedPath, Kind, Source)`.

- `Kind` ∈ `RequiredInput` | `WritableData` — unchanged semantics.
- `Source` ∈ `command-line` | `environment` | `config-file` — the `default` value is
  **removed**: with no code defaults, every location traces to one of the three tiers
  (SC-005). A location whose source would be `default` is a configuration error caught by
  the missing-root validation.

### Location classification after this change

| Kind | Locations |
| --- | --- |
| `RequiredInput` (validated, never created) | `agent_dir`, the three agent subfolders, each agent's worker DLL, each required instruction document, `secrets_file` |
| `WritableData` (auto-created if missing) | `data_dir`, `wiki_dir`, `raw_dir` (+`originals`/`sources`), `state_db` directory, `write_locks_dir`, `tasks_dir`, `conversations_dir`, `findings_dir`, `remediation_tasks_dir` |

`data_dir` and `wiki_dir` are auto-created (FR-010); `agent_dir` is **not** — it is agent
build output and its absence is a failure (FR-013).

---

## 5. Resolution and validation state machine

```text
bind Grimoire:Paths
   │
   ├─▶ any of DataDir / WikiDir / AgentDir empty?
   │        └─▶ FAIL  paths_configuration_missing   (names appsettings.json + missing keys)
   │
   ├─▶ resolve roots (DataDir/WikiDir/AgentDir — all three independently cwd-anchored)
   ├─▶ resolve sub-paths against their roots; resolve SecretsFile against cwd
   │
   ├─▶ validate required inputs, fail-fast, before any write:
   │        agent_dir exists and is non-empty?      └─▶ FAIL paths_validation_failed
   │        each agent subfolder + required docs?   └─▶ FAIL paths_validation_failed
   │        secrets_file exists?                    └─▶ FAIL paths_validation_failed
   │        each worker DLL exists?                 └─▶ FAIL paths_validation_failed
   │                                                    reason: "…not found in its build
   │                                                    output. Build first: dotnet build
   │                                                    backend/Grimoire.slnx"
   │
   ├─▶ create writable data locations (idempotent)  ──▶ paths_location_created (per creation)
   │
   └─▶ emit paths_resolved (all locations + sources) ─▶ ResolvedGrimoirePaths
```

Failure semantics are unchanged from ADR-009: `GrimoirePathValidationException` carrying
`Location`, `ConfiguredValue`, `ResolvedPath`, `Reason`; the process exits non-zero before
serving or before any command runs.

`agent_dir` gains a distinct check beyond "directory exists": a directory present but
holding no agent runtime fails with reason `agent directory contains no agent runtime`
(FR-013/SC-007) — a plain existence check would let an empty directory through to a
per-document failure that names a file rather than the directory the operator must fix.
A missing worker DLL reports `Grimoire.<Type>Agent.dll not found in the agent directory.
Build first: dotnet build backend/Grimoire.slnx`.

---

## 6. Agent build contract (`GrimoireAgentId` / `GrimoireAgentDir`)

Not a runtime entity, but the producer of every `RequiredInput` location above.

| Property | Declared in | Value |
| --- | --- | --- |
| `GrimoireAgentId` | each agent `.csproj` | `ingest` \| `query` \| `lint` |
| `GrimoireAgentDir` | `backend/Directory.Build.targets` (overridable via `-p:`) | default: repo-relative `.grimoire/agents` |

Two composed mechanisms, one copy:

1. Each agent `.csproj` declares `Instructions\**` as `Content` with
   `CopyToOutputDirectory`, so instruction documents land in the agent's own `$(OutDir)`
   under `Instructions/`, beside its assemblies.
2. Target **`PublishAgentRuntime`** (`AfterTargets="Build"`, condition
   `'$(GrimoireAgentId)' != ''`) clears `$(GrimoireAgentDir)/$(GrimoireAgentId)/` and
   copies the agent's entire `$(OutDir)**` into it (FR-011/SC-008).

The whole output is copied — not a selection — because `deps.json` enumerates every
assembly the app expects; a partial copy fails at launch. Clear-then-copy guarantees the
directory states exactly what the current build produced, with no stale assembly or
renamed instruction file surviving. The target deletes only the agent-id subfolder it owns.

Per agent type, the delivered directory is:

```text
<AgentDir>/<agent-id>/
├── Grimoire.<Type>Agent.dll          ← worker, launched as `dotnet <this>`
├── Grimoire.<Type>Agent.deps.json
├── Grimoire.<Type>Agent.runtimeconfig.json
├── Grimoire.AgentRuntime.dll, Grimoire.Domain.dll, Anthropic.dll, OpenTelemetry.*.dll, …
└── Instructions/
    ├── system-prompt.md
    ├── policy.json
    └── default-user-prompt.md        ← Ingest only
```

---

## 7. Directory layout, before → after

```text
BEFORE (repo root)                        AFTER (repo root)
├── data/                                 ├── .grimoire/              (git-ignored)
│   ├── .env                              │   ├── agents/{ingest,query,lint}/   ← build output:
│   ├── agents/{ingest,query,lint}/       │   │      worker dll + deps + assemblies
│   ├── raw/  state/  write-locks/        │   │      + Instructions/
│   ├── findings/                         │   ├── raw/{originals,sources}/
│   └── evals/recordings/                 │   ├── state/operational-state.db
│                                         │   ├── write-locks/
│                                         │   └── lint.pid
├── wiki/                                 ├── llm-wiki/
├── tasks/                                │   ├── index.md  log.md  <pages>
├── conversations/                        │   ├── tasks/  conversations/
└── remediation-tasks/                    │   ├── findings/  remediation-tasks/
                                          ├── .env                    (git-ignored)
                                          ├── .env-example
                                          └── backend/
                                              ├── src/Grimoire.*Agent/Instructions/  ← sources
                                              └── tests/Grimoire.AgentEvals/Fixtures/recordings/
```

No migration is performed (FR-014). In this repository, the git-tracked parts move by
`git mv`; git-ignored working data is left for manual relocation.

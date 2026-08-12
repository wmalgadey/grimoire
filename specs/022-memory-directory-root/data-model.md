# Data Model: Independent Memory Directory Root

**Feature**: `022-memory-directory-root` | **Date**: 2026-08-11 |
**Plan**: [plan.md](./plan.md) | **Contracts**: [directory-options.md](./contracts/directory-options.md)

This feature adds no persisted entity, no database table and no file format. Its "data
model" is the shape of the path-configuration composition point and the anchoring
relationships between roots and sub-paths. Everything below describes types in
`Grimoire.Hub.Runtime.Paths`, plus the two spec entities that motivate them.

---

## 1. Spec entities

### Memory Folder *(new)*

The independently configurable root holding all agent process bookkeeping: task artifacts,
conversation records, lint findings reports and remediation task records.

| Property | Value |
| --- | --- |
| Anchor | Process working directory |
| Shipped default | `memory` |
| Nesting | Shares no parent with, and is never nested inside, `DataDir`, `WikiDir` or `AgentDir` — unless an operator explicitly configures it that way, which is accepted verbatim |
| Kind | `WritableData` — auto-created when absent |
| Lifecycle | Created on first resolve; never deleted, never migrated by the hub |

### Wiki Folder *(redefined)*

The independently configurable root holding **only** wiki content: `index.md`, `log.md`,
and topical article subfolders. It no longer holds agent process bookkeeping.

**Consequence beyond configuration**: the four bookkeeping folders leave the agents'
guarded root. `GuardedToolExecutor` is constructed with `repositoryRoot: WikiRoot`, so
after this change it can neither list nor write them, and the policies' `"."` catch-all no
longer covers them. The "these are not pages" contract stops being prompt-only.

---

## 2. `GrimoirePathOptions` — the configuration input

Bound from configuration section `Grimoire:Paths` in one call at one composition point.
The type is no longer a flat bag: it is a **graph of four anchor groups plus one ungrouped
property**, mirroring the JSON tree (research R8, ADR-024 C-A).

```text
GrimoirePathOptions                                    ← bound from "Grimoire:Paths"
├── Data   : DataPathOptions    = new()   ← group, anchored at working directory
│   ├── Dir            ".grimoire"                      ← the root itself
│   ├── RawDir         "raw"
│   ├── StateDb        "state/operational-state.db"
│   └── WriteLocksDir  "write-locks"
├── Wiki   : WikiPathOptions    = new()
│   └── Dir            "llm-wiki"
├── Agent  : AgentPathOptions   = new()
│   └── Dir            ".grimoire/agents"
├── Memory : MemoryPathOptions  = new()                 ← new group
│   ├── Dir                  "memory"
│   ├── TasksDir             "tasks"                    ← re-anchored from Wiki
│   ├── ConversationsDir     "conversations"            ← re-anchored from Wiki
│   ├── FindingsDir          "findings"                 ← re-anchored from Wiki
│   └── RemediationTasksDir  "remediation-tasks"        ← re-anchored from Wiki
└── SecretsFile ".env"                                  ← ungrouped, working directory
```

| Location | Group | Key | Anchor | Default | Switch | Change |
| --- | --- | --- | --- | --- | --- | --- |
| data root | `Data` | `Dir` | working dir | `.grimoire` | `--data-dir` | key renamed |
| raw intake | `Data` | `RawDir` | `Data.Dir` | `raw` | none | key renamed |
| state DB | `Data` | `StateDb` | `Data.Dir` | `state/operational-state.db` | none | key renamed |
| write locks | `Data` | `WriteLocksDir` | `Data.Dir` | `write-locks` | none | key renamed |
| wiki root | `Wiki` | `Dir` | working dir | `llm-wiki` | `--wiki-dir` | key renamed |
| agent root | `Agent` | `Dir` | working dir | `.grimoire/agents` | `--agent-dir` | key renamed |
| **memory root** | **`Memory`** | **`Dir`** | **working dir** | **`memory`** | **`--memory-dir`** | **new** |
| task artifacts | `Memory` | `TasksDir` | `Memory.Dir` | `tasks` | none | **re-anchored** |
| conversation records | `Memory` | `ConversationsDir` | `Memory.Dir` | `conversations` | none | **re-anchored** |
| findings reports | `Memory` | `FindingsDir` | `Memory.Dir` | `findings` | none | **re-anchored** |
| remediation records | `Memory` | `RemediationTasksDir` | `Memory.Dir` | `remediation-tasks` | none | **re-anchored** |
| secrets file | *(none)* | `SecretsFile` | working dir | `.env` | none | unchanged |

**Initializers vs. defaults.** Each *group* property is initialized (`= new()`) so an
absent JSON group binds to an empty group rather than null. Each *leaf* path property is
`string?` with no initializer — a code-level path default remains forbidden (ADR-022 R2,
ADR-024 M2). The distinction matters and is easy to misread: `= new()` is a null guard, not
a default.

**Shape rule (ADR-024 M4)**: exactly four group properties, each declaring a `Dir`, plus
exactly one ungrouped property (`SecretsFile`). No path-valued property may sit directly on
`GrimoirePathOptions` otherwise. This keeps the type and the JSON the same shape so the file
cannot drift back toward flatness one property at a time.

**Grouping invariant (ADR-024 M5)**: relocating a group's `Dir` moves every sub-path
declared in that group and nothing declared in another. The grouping is enforced, not
documentary.

**Validation rule (FR-006)**: after binding, each of the four groups' `Dir` must carry a
non-empty value. A missing one produces `GrimoirePathConfigurationMissingException` naming
`appsettings.json` and the **full key path** (`Grimoire:Paths:Memory:Dir`). Sub-paths are
*not* in this check — an empty sub-path value falls back to its anchor, the pre-existing
`ResolveAgainst` behavior, unchanged.

**Superseded keys are not detected.** An earlier draft of this feature added a rule here —
before the validation rule above, the resolver would probe the bound configuration for
eleven superseded flat keys and fail naming each one found together with its replacement.
That rule (FR-014) was withdrawn on 2026-08-11: the project is pre-1.0 (alpha) with no
external installations carrying old key names, so there is no superseded configuration to
detect. An operator still exporting a pre-regrouping key such as `Grimoire:Paths:DataDir`
or `Grimoire:Paths:MemoryDir` gets the ordinary silent-ignore treatment configuration
systems give any unrecognized key — the resolver never inspects the flat key names at all.

**Anchoring rule (unchanged mechanism)**: an absolute configured value is used verbatim; a
relative value combines with its anchor; an empty value resolves to the anchor itself.

---

## 3. `ResolvedGrimoirePaths` — the resolution output

Gains one member and **stays flat**, deliberately. The grouping in §2 exists to express an
anchoring relationship between *configured* values; once resolution has run, every location
is an independent absolute path and the relationship has already been consumed. Grouping
the output too would churn ~40 call sites (`paths.MemoryDir` → `paths.Memory.Dir`) for no
invariant — rule M5 constrains the input graph, not this record.

The four record-path helpers keep their signatures and their output shape exactly
(FR-010) — they simply compose against a directory that now sits elsewhere.

```text
ResolvedGrimoirePaths
├── DataDir                              (unchanged)
├── WikiDir                              (unchanged)
├── AgentDir                             (unchanged)
├── MemoryDir                            ← new
├── RawOriginalsDir, RawSourcesDir       (unchanged, under DataDir)
├── StateDbPath, WriteLocksDir           (unchanged, under DataDir)
├── LintPidPath                          (unchanged, under DataDir)
├── IndexPath, LogPath                   (unchanged, under WikiDir)
├── TasksDir                             ← now under MemoryDir
├── ConversationsDir                     ← now under MemoryDir
├── FindingsDir                          ← now under MemoryDir
├── RemediationTasksDir                  ← now under MemoryDir
├── SecretsFilePath                      (unchanged, at working directory)
├── Ingest / Query / Lint : AgentRuntimePaths   (unchanged, under AgentDir)
└── Locations : IReadOnlyList<PathLocation>     ← gains a memory_dir entry
```

Helpers, all unchanged in signature and in the filename they produce:

| Helper | Produces |
| --- | --- |
| `TaskArtifactPathFor(taskId)` | `<MemoryDir>/tasks/<taskId>.md` |
| `ConversationRecordPathFor(conversationId)` | `<MemoryDir>/conversations/<conversationId>.md` |
| `FindingsReportPathFor(runId)` | `<MemoryDir>/findings/<runId>.md` |
| `RemediationTaskRecordPathFor(taskId)` | `<MemoryDir>/remediation-tasks/<taskId>.md` |

---

## 4. `PathLocation` — the startup report entry

One new entry joins the existing eleven. Shape is unchanged:
`(Name, ConfiguredValue, ResolvedPath, Kind, Source)`.

| Name | Kind | Auto-created | Notes |
| --- | --- | --- | --- |
| `memory_dir` | `WritableData` | yes | New. Reported in `paths_resolved`, included in the `sources` field |
| `tasks_dir`, `conversations_dir`, `findings_dir`, `remediation_tasks_dir` | `WritableData` | yes | Names unchanged; resolved values now under `memory_dir` |
| `data_dir`, `wiki_dir`, `raw_dir`, `raw_originals_dir`, `raw_sources_dir`, `write_locks_dir` | `WritableData` | yes | unchanged |
| `agent_dir`, `secrets_file` | `RequiredInput` | no — startup fails if absent | unchanged |

`Source` ∈ `{command-line, environment, config-file}`, derived by reversing the
configuration providers. `memory_dir` participates identically.

---

## 5. Anchoring graph

**Before** — bookkeeping mixed into the wiki tree:

```text
<cwd>
├── .grimoire/          DataDir ──── raw/ · state/ · write-locks/
│   └── agents/         AgentDir ─── ingest/ · query/ · lint/
├── llm-wiki/           WikiDir ──── index.md · log.md · tech/ · concepts/
│                                  ├── tasks/                 ← bookkeeping
│                                  ├── conversations/         ← bookkeeping
│                                  ├── findings/              ← bookkeeping
│                                  └── remediation-tasks/     ← bookkeeping
└── .env                SecretsFile
```

**After** — four peer roots, one of them holding all bookkeeping:

```text
<cwd>
├── .grimoire/          DataDir ──── raw/ · state/ · write-locks/
│   └── agents/         AgentDir ─── ingest/ · query/ · lint/
├── llm-wiki/           WikiDir ──── index.md · log.md · tech/ · concepts/
├── memory/             MemoryDir ── tasks/ · conversations/ · findings/ · remediation-tasks/
└── .env                SecretsFile
```

**Independence invariant (FR-003/FR-004, SC-002)**: the four roots each anchor at the
working directory and at nothing else. The graph has no edge between any two roots, so
relocating one cannot move another. `AgentDir`'s default value happens to *spell* a path
under `DataDir`'s default (`.grimoire/agents`), which is a coincidence of values, not an
anchoring relationship — ADR-022 already spelled it out in full for exactly this reason.

---

## 6. Record formats — explicitly unchanged

FR-010 freezes the internal shape of all four record kinds. No field is added, removed or
renamed; no filename convention changes.

| Record | Format authority | Path |
| --- | --- | --- |
| Task artifact | ADR-002 / ADR-003 (markdown + frontmatter) | `<MemoryDir>/tasks/<taskId>.md` |
| Conversation record | ADR-014 (`grimoire-conversation/1`) | `<MemoryDir>/conversations/<conversationId>.md` |
| Findings report | ADR-016 / Lint feature | `<MemoryDir>/findings/<runId>.md` |
| Remediation task record | ADR-018 | `<MemoryDir>/remediation-tasks/<taskId>.md` |

---

## 7. State transitions

None. Path resolution is a pure function of configuration plus the working directory,
evaluated once per process start. The only state change is the side effect of
auto-creation:

```text
resolve → [MemoryDir absent on disk?] ─yes→ Directory.CreateDirectory
                                             + paths_location_created (INFO)
                                     └─no──→ (no-op)
```

Existing records under the previous `<WikiDir>/…` locations are **not** detected, read,
moved or deleted (FR-011/SC-007). They simply stop being resolved against.

---

## 8. Cross-references

- Configuration surface and precedence: [contracts/directory-options.md](./contracts/directory-options.md)
- Log event and span field contracts: [contracts/paths-observability.md](./contracts/paths-observability.md)
- Why the four sub-paths move together, and why the wikilink goes: [research.md](./research.md) R1, R3
- Structural rules M1/M2/M3: [ADR-024](../../docs/adr/ADR-024-memory-directory-root.md)

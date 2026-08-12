# Contract: Hub Directory Options (four roots)

**Feature**: `022-memory-directory-root` | **Governs**: FR-001, FR-002, FR-003, FR-004,
FR-005, FR-006, FR-007, FR-009, FR-010 | **Verified by**: SC-001, SC-002, SC-003, SC-004,
SC-005, SC-007

Supersedes [`specs/020-simplify-hub-config/contracts/directory-options.md`](../../020-simplify-hub-config/contracts/directory-options.md).
The hub's complete path-configuration surface. Anything not listed in §1 is not
configurable from the command line — by ADR-024 rule M1 it cannot become configurable
without amending that ADR.

---

## 1. Command-line switches

Exactly **four**, accepted by the web host and by every `Grimoire.Hub.Cli` command alike.

| Switch | Configuration key | Environment variable | Meaning |
| --- | --- | --- | --- |
| `--data-dir <PATH>` | `Grimoire:Paths:Data:Dir` | `Grimoire__Paths__Data__Dir` | Root for all harness runtime state (raw intake, state DB, write-locks). |
| `--agent-dir <PATH>` | `Grimoire:Paths:Agent:Dir` | `Grimoire__Paths__Agent__Dir` | Directory holding the complete agent runtime — worker binaries, dependency assemblies and instruction files — in per-agent-type subfolders. Produced by the agent build. |
| `--wiki-dir <PATH>` | `Grimoire:Paths:Wiki:Dir` | `Grimoire__Paths__Wiki__Dir` | Root for the wiki content itself — `index.md`, `log.md`, and topical article folders. |
| **`--memory-dir <PATH>`** | **`Grimoire:Paths:Memory:Dir`** | **`Grimoire__Paths__Memory__Dir`** | **Root for agent process bookkeeping — task artifacts, conversation records, lint findings reports, remediation task records.** |

Relative values resolve against the process working directory for all four switches — each
independently. Absolute values are used verbatim.

### Changed in this feature

- `--memory-dir` is new.
- **Every configuration key and environment variable is renamed** by the regrouping (§5):
  `Grimoire:Paths:DataDir` → `Grimoire:Paths:Data:Dir`, and correspondingly for all eleven
  keys. **Switch names are unchanged.**
- `--wiki-dir`'s description narrows. It previously read "Root for all agent-produced
  results (wiki pages, `index.md`, `log.md`, tasks, conversations, findings, remediation
  tasks)". The trailing four are no longer under it, and the description in both
  `PathSwitchCatalog` and `HubPathSettings` must be updated to match — they are the text an
  operator reads in `--help`.

### Migration: the key rename is detected, not silent

An unrecognized **switch** is a parser error. An unrecognized **configuration key** would
normally be ignored — so without a guard, an operator still exporting
`Grimoire__Paths__DataDir` would get the shipped default with no warning of any kind. The
hub therefore probes for every superseded key and **fails at startup** naming each one it
finds and its replacement (FR-014):

```text
appsettings.json / environment: superseded configuration key(s).
  Grimoire:Paths:DataDir  → Grimoire:Paths:Data:Dir
  Grimoire:Paths:TasksDir → Grimoire:Paths:Memory:TasksDir
```

This is **not** an alias and **not** a deprecation window — the old key does not work. It
is reported so the failure is loud rather than quiet. Operators who configure only through
CLI switches are unaffected; those using `appsettings.json` or environment variables are
told exactly what to change. The detection table is scoped to this one rename and is to be
deleted, not extended, if the layout changes again.

| Old environment variable | New |
| --- | --- |
| `Grimoire__Paths__DataDir` | `Grimoire__Paths__Data__Dir` |
| `Grimoire__Paths__WikiDir` | `Grimoire__Paths__Wiki__Dir` |
| `Grimoire__Paths__AgentDir` | `Grimoire__Paths__Agent__Dir` |
| `Grimoire__Paths__RawDir` | `Grimoire__Paths__Data__RawDir` |
| `Grimoire__Paths__StateDb` | `Grimoire__Paths__Data__StateDb` |
| `Grimoire__Paths__WriteLocksDir` | `Grimoire__Paths__Data__WriteLocksDir` |
| `Grimoire__Paths__TasksDir` | `Grimoire__Paths__Memory__TasksDir` |
| `Grimoire__Paths__ConversationsDir` | `Grimoire__Paths__Memory__ConversationsDir` |
| `Grimoire__Paths__FindingsDir` | `Grimoire__Paths__Memory__FindingsDir` |
| `Grimoire__Paths__RemediationTasksDir` | `Grimoire__Paths__Memory__RemediationTasksDir` |
| `Grimoire__Paths__SecretsFile` | `Grimoire__Paths__SecretsFile` *(unchanged — ungrouped)* |

### Removed switches

Unchanged from feature 020: `--base-dir`, `--content-root`, `--raw-dir`, `--state-db`,
`--secrets-file`, `--instructions-dir`, `--query-instructions-dir`,
`--lint-instructions-dir`, `--agent-worker`, `--query-agent-worker`,
`--lint-agent-worker`, `--conversations-dir`, `--write-locks-dir`, `--findings-dir`,
`--remediation-tasks-dir`.

Note in particular that `--conversations-dir`, `--findings-dir` and
`--remediation-tasks-dir` stay removed. This feature does **not** reintroduce per-record
switches; it introduces the single root that makes them unnecessary.

---

## 2. Precedence

Per option, evaluated independently — setting one option never requires setting another
(FR-005):

```text
command-line switch  >  environment variable  >  appsettings.json
```

There is no further tier. A root absent from all three is a startup failure (§4).

**Worked example** — `Grimoire__Paths__Memory__Dir=/env/memory` in the environment,
`--memory-dir /cli/memory` on the command line, `Wiki:Dir` = `llm-wiki` and `Memory:Dir` =
`memory` in the file, nothing else set:

| Location | Effective value | Source |
| --- | --- | --- |
| memory root | `/cli/memory` | `command-line` |
| task artifacts | `/cli/memory/tasks` | `config-file` (value `tasks`), anchored at the resolved memory root |
| wiki root | `<cwd>/llm-wiki` | `config-file` — unaffected by the memory root moving |
| data root | `<cwd>/.grimoire` | `config-file` — unaffected |

Note that precedence is evaluated per **key**, not per group: setting
`Grimoire__Paths__Memory__Dir` does not affect `Grimoire:Paths:Memory:TasksDir`, which
continues to come from the file. Grouping expresses anchoring, not co-resolution.

---

## 3. Anchoring

| Location | Anchor | Shipped default |
| --- | --- | --- |
| `DataDir` | process working directory | `.grimoire` |
| `WikiDir` | process working directory | `llm-wiki` |
| `AgentDir` | process working directory | `.grimoire/agents` |
| `MemoryDir` | process working directory | `memory` |
| `RawDir` / `StateDb` / `WriteLocksDir` | resolved `DataDir` | `raw` / `state/operational-state.db` / `write-locks` |
| `TasksDir` / `ConversationsDir` / `FindingsDir` / `RemediationTasksDir` | resolved **`MemoryDir`** | `tasks` / `conversations` / `findings` / `remediation-tasks` |
| `SecretsFile` | process working directory | `.env` |

**Independence (FR-003/FR-004, SC-002)**: no root anchors at another root. Relocating any
one of the four leaves the other three exactly where they were. Verified by a 4×4
relocation matrix.

**Explicit nesting is permitted.** An operator who sets `MemoryDir` to a path equal to or
inside `WikiDir`, `DataDir` or `AgentDir` gets exactly that, with no warning and no error.
The resolver anchors and validates; it does not second-guess a deliberate choice (spec
edge case).

---

## 4. Validation and startup behavior

| Condition | Behavior |
| --- | --- |
| `Grimoire:Paths:Memory:Dir` absent or whitespace in every tier | `GrimoirePathConfigurationMissingException`, message names `appsettings.json` and lists the full key path `Grimoire:Paths:Memory:Dir` among the missing keys. Metric `grimoire.hub.path_resolution_failures_total{reason="configuration_missing"}` incremented; `paths_configuration_missing` logged at ERROR. Startup aborts. |
| The entire `Grimoire:Paths:Memory` group absent | Identical behavior. The group property binds to an empty group rather than null (§5), so this reaches the same missing-key gate rather than throwing a `NullReferenceException`. Both cases are separately tested. |
| Resolved `MemoryDir` does not exist on disk | Created via `Directory.CreateDirectory`; `paths_location_created` logged at INFO with `location=memory_dir` (FR-007/SC-005). |
| Resolved `MemoryDir` path is occupied by a file | `GrimoirePathValidationException` naming `memory_dir`, its configured value and its resolved path. Same treatment as `data_dir`/`wiki_dir`. |
| The four sub-paths do not exist | Created, each with its own `paths_location_created` entry. Unchanged behavior at a new anchor. |
| Records exist under the previous `<WikiDir>/…` locations | Ignored entirely — not detected, read, moved or deleted (FR-011/SC-007). |

`MemoryDir` is `PathLocationKind.WritableData`, not `RequiredInput`: like the data and wiki
directories it is created on demand, unlike the agent directory which must already hold a
build artifact.

---

## 5. `appsettings.json` — grouped by anchoring root

`backend/src/Grimoire.Hub/appsettings.json` is the sole source of defaults (ADR-022 W1,
unchanged). The section is **grouped**, not flat: each group's `Dir` key is a root, and
every sibling key inside that group is a sub-path anchored at that group's resolved `Dir`.
The JSON tree is the anchoring graph, and rule M5 keeps it honest.

```jsonc
"Grimoire": {
  "Paths": {
    // Each group's "Dir" is a root, anchored at the process working directory.
    // Every sibling key inside a group anchors at that group's resolved "Dir".
    // The four roots are mutually independent — relocating one never moves another.
    "Data": {
      "Dir": ".grimoire",
      "RawDir": "raw",
      "StateDb": "state/operational-state.db",
      "WriteLocksDir": "write-locks"
    },
    // index.md and log.md are children of the wiki root but are not configurable.
    "Wiki": {
      "Dir": "llm-wiki"
    },
    // Per-agent-type subfolders and Instructions/ are fixed by the agent build
    // contract (ADR-022) and are likewise not configurable.
    "Agent": {
      "Dir": ".grimoire/agents"
    },
    // Agent process bookkeeping, held apart from the wiki content so an operator
    // can back it up, retain it, or exclude it as one unit.
    "Memory": {
      "Dir": "memory",
      "TasksDir": "tasks",
      "ConversationsDir": "conversations",
      "FindingsDir": "findings",
      "RemediationTasksDir": "remediation-tasks"
    },
    // Anchored at the process working directory and in no group by design:
    // relocating any root must never separate an operator from their credentials.
    "SecretsFile": ".env"
  }
}
```

`Wiki` and `Agent` carry only a `Dir` today and are still written as groups, so the four
roots read in parallel and a future sub-path under either is an added key rather than a
breaking shape change.

### Binding

`GrimoirePathOptions` mirrors this tree: four group properties (`Data`, `Wiki`, `Agent`,
`Memory`) of dedicated option types plus `SecretsFile`, bound in one call from one section
at one composition point (ADR-009's rule, preserved in substance). Each group property is
initialized (`= new()`) so an absent JSON group binds to an empty group rather than null.

**Those initializers are not path defaults.** Every leaf path property remains `string?`
with no initializer, so ADR-022 R2 and ADR-024 M2 stand: no production assembly may contain
`.grimoire` or `llm-wiki` as an IL string literal anywhere, nor `memory` within
`Grimoire.Hub.Runtime.Paths`.

### Grouping invariant

For each group, relocating that group's `Dir` moves every sub-path declared in the group
and nothing declared in another group. Asserted by
`Grimoire.IntegrationTests/PathConfiguration/PathGroupingInvariantTests` (ADR-024 rule M5),
driven by reflection over the options graph so a newly added sub-path is covered without
editing the test. Declaring a key in one group while anchoring it at another root in the
resolver fails the build.

---

## 6. Structural cap

`PathSwitchCatalog.All` contains exactly the four entries in §1, and `HubPathSettings`
declares exactly one `[CommandOption]` with a `[Description]` per entry — asserted by
`Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests` (ADR-024 M1). The rule remains an
exact named enumeration, not a count: adding a fifth switch, or renaming one, fails the
build.

Adding a new **sub-path** means adding a property to the relevant group option type and a
key inside that group in `appsettings.json` — never a switch (ADR-022, unchanged), and
never as a loose property on `GrimoirePathOptions` (ADR-024 rule M4). Adding a new **root**
means a new group and remains an ADR-level decision.

---

## 7. Startup report

Every successful start emits one `paths_resolved` event listing every resolved location.
`memory_dir` is a mandatory field (FR-008/SC-006). Full contract:
[paths-observability.md](./paths-observability.md).

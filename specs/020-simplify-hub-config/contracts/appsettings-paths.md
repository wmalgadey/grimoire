# Contract: `Grimoire:Paths` Configuration Section

**Feature**: `020-simplify-hub-config` | **Governs**: FR-005, FR-009, FR-015 |
**Verified by**: SC-006

`backend/src/Grimoire.Hub/appsettings.json` is **mandatory**, versioned in git, and is the
only place default paths exist. Every key below must be present and non-empty.

---

## Shipped content

```json
{
  "Grimoire": {
    "Paths": {
      "DataDir": ".grimoire",
      "WikiDir": "llm-wiki",
      "AgentDir": "agents",

      "RawDir": "raw",
      "StateDb": "state/operational-state.db",
      "WriteLocksDir": "write-locks",

      "TasksDir": "tasks",
      "ConversationsDir": "conversations",
      "FindingsDir": "findings",
      "RemediationTasksDir": "remediation-tasks",

      "SecretsFile": ".env"
    }
  }
}
```

## Key reference

| Key | Anchor | CLI switch | Notes |
| --- | --- | --- | --- |
| `DataDir` | working directory | `--data-dir` | Root — required |
| `WikiDir` | working directory | `--wiki-dir` | Root — required |
| `AgentDir` | resolved `DataDir` | `--agent-dir` | Root — required |
| `RawDir` | `DataDir` | — | `originals/` and `sources/` are fixed subfolders |
| `StateDb` | `DataDir` | — | file path; its directory is auto-created |
| `WriteLocksDir` | `DataDir` | — | |
| `TasksDir` | `WikiDir` | — | agent output |
| `ConversationsDir` | `WikiDir` | — | agent output |
| `FindingsDir` | `WikiDir` | — | agent output |
| `RemediationTasksDir` | `WikiDir` | — | agent output |
| `SecretsFile` | working directory | — | project root; unaffected by any root (FR-019) |

## Rules

1. **Mandatory.** Missing file, empty file, or any of `DataDir` / `WikiDir` / `AgentDir`
   absent or whitespace ⇒ startup fails naming the file and the missing keys. No code
   default exists to fall back to (enforced by structural rule R2).
2. **Relative or absolute.** A relative value resolves against the key's anchor; an
   absolute value is used verbatim. This is how the internal layout is customized —
   e.g. `"StateDb": "/mnt/fast-ssd/operational-state.db"` moves only the database
   (spec US5 AS1).
3. **No key ever becomes a switch.** Adding a runtime location means adding a field and a
   key here — never a command-line option (FR-015, rule R1).
4. **Environment override.** Every key is overridable as `Grimoire__Paths__<Key>`; the
   three roots are additionally overridable by their switch.
5. **`appsettings.Development.json` carries no `Grimoire:Paths` section** — development
   and production differ in configuration values only where an operator deliberately sets
   them, not by shipped divergence (ADR-009 driver, retained).

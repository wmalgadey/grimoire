# Contract: Hub Path Configuration Surface

The operator-facing contract for configuring runtime data locations. One precedence
order across all channels: **command line > environment > `appsettings.json` > code
defaults** (FR-005).

## Channels

| Logical location | CLI switch | Environment variable | appsettings key |
| --- | --- | --- | --- |
| Base directory | `--base-dir` | `Grimoire__Paths__BaseDir` | `Grimoire:Paths:BaseDir` |
| Data directory | `--data-dir` | `Grimoire__Paths__DataDir` | `Grimoire:Paths:DataDir` |
| Wiki content root | `--content-root` | `Grimoire__Paths__ContentRoot` | `Grimoire:Paths:ContentRoot` |
| Raw intake storage | `--raw-dir` | `Grimoire__Paths__RawDir` | `Grimoire:Paths:RawDir` |
| Operational state DB | `--state-db` | `Grimoire__Paths__StateDb` | `Grimoire:Paths:StateDb` |
| Secrets file | `--secrets-file` | `Grimoire__Paths__SecretsFile` | `Grimoire:Paths:SecretsFile` |
| Agent instructions dir | `--instructions-dir` | `Grimoire__Paths__InstructionsDir` | `Grimoire:Paths:InstructionsDir` |
| Agent worker | `--agent-worker` | `Grimoire__Paths__AgentWorker` | `Grimoire:Paths:AgentWorker` |

Defaults and resolution anchors: see `data-model.md` (single source:
`GrimoirePathOptions` + the `Grimoire:Paths` section of `appsettings.json`).

## Behavior guarantees

1. Absolute values are used as-is; relative values resolve against their documented
   anchor (never a discovered repository root). (FR-002, FR-003)
2. With no configuration at all, every location resolves beneath
   `<working-directory>/data`. (FR-004)
3. Missing/wrong-kind **required inputs** (secrets file, the three instruction files,
   agent worker) abort startup with exit code ≠ 0 and an error naming the logical
   location, configured value, and resolved path. (FR-006, SC-002)
4. Absent **writable data** locations are created; each creation is reported. (FR-006)
5. Every successful start logs all resolved absolute locations (`paths_resolved`).
   (FR-008, SC-005)
6. Compatibility: `--content-root` remains valid, including absolute external paths
   (current prod launch config keeps working with an added `--base-dir` or data-dir
   override for the remaining locations).

## Removed contract surface

- Repo-root discovery (`git rev-parse --show-toplevel`) in Hub and agent: gone; `git`
  is no longer a runtime dependency. (FR-002)
- Hand-parsed `--content-root` via `ParseOption`: replaced by the configuration
  system (same switch, all channels).

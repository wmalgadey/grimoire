# Contract: Hub Directory Options

**Feature**: `020-simplify-hub-config` | **Governs**: FR-002, FR-003, FR-004, FR-015 |
**Verified by**: SC-002, SC-005

The hub's complete path-configuration surface. Anything not listed here is not
configurable from the command line — by rule R1 it cannot become configurable without
amending ADR-022.

---

## 1. Command-line switches

Exactly three, accepted by the web host and by every `Grimoire.Hub.Cli` command alike.

| Switch | Configuration key | Environment variable | Meaning |
| --- | --- | --- | --- |
| `--data-dir <PATH>` | `Grimoire:Paths:DataDir` | `Grimoire__Paths__DataDir` | Root for all harness runtime state (raw intake, state DB, write-locks) and, by default, the agent directory. |
| `--agent-dir <PATH>` | `Grimoire:Paths:AgentDir` | `Grimoire__Paths__AgentDir` | Directory holding the complete agent runtime — worker binaries, dependency assemblies and instruction files — in per-agent-type subfolders. Produced by the agent build. |
| `--wiki-dir <PATH>` | `Grimoire:Paths:WikiDir` | `Grimoire__Paths__WikiDir` | Root for all agent-produced results (wiki pages, `index.md`, `log.md`, tasks, conversations, findings, remediation tasks). |

Relative values resolve against the option's anchor (`--data-dir` and `--wiki-dir`:
process working directory; `--agent-dir`: the resolved data directory). Absolute values
are used verbatim.

### Removed switches

`--base-dir`, `--content-root`, `--raw-dir`, `--state-db`, `--secrets-file`,
`--instructions-dir`, `--query-instructions-dir`, `--lint-instructions-dir`,
`--agent-worker`, `--query-agent-worker`, `--lint-agent-worker`, `--conversations-dir`,
`--write-locks-dir`, `--findings-dir`, `--remediation-tasks-dir`.

Supplying any of them produces the CLI parser's standard unrecognized-option error and a
usage exit code. No alias, no detection, no replacement guidance (clarification
2026-08-06).

---

## 2. Precedence

Per option, evaluated independently — setting one option never requires setting another
(FR-003):

```text
command-line switch  >  environment variable  >  appsettings.json
```

There is no fourth tier. A root absent from all three is a startup failure (§4).

**Worked example** — `Grimoire__Paths__DataDir=/env/data` in the environment,
`--data-dir /cli/data` on the command line, `"WikiDir": "llm-wiki"` in the file, nothing
else set:

| Option | Effective value | Source |
| --- | --- | --- |
| `DataDir` | `/cli/data` | `command-line` |
| `WikiDir` | `<cwd>/llm-wiki` | `config-file` |
| `AgentDir` | `/cli/data/agents` | `config-file` (value), anchored at the CLI-set root |

---

## 3. Help output

`--help` (root and per-command) lists exactly these three switches under
`Server options:`, generated from `PathSwitchCatalog.All` — the ADR-020 single-declaration
rule is unchanged, only the catalog is smaller.

---

## 4. Startup failure contract

| Condition | Exit | Log event | Message names |
| --- | --- | --- | --- |
| `appsettings.json` missing, empty, or missing any of the three roots | non-zero | `paths_configuration_missing` (ERROR) | the configuration file and every missing key |
| Agent directory missing, or present but holding no agent runtime | non-zero | `paths_validation_failed` (ERROR), `location=agent_dir` | the agent directory, its configured value, its resolved path |
| A required instruction document or the secrets file is missing | non-zero | `paths_validation_failed` (ERROR) | the logical location, configured value, resolved path |
| An agent worker DLL is missing from the agent directory | non-zero | `paths_validation_failed` (ERROR) | the worker, its resolved path, and `Build first: dotnet build backend/Grimoire.slnx` |

No condition falls back to a code default (FR-005/SC-006), and no failure is deferred to
dispatch time — the hub never builds an agent to recover from a missing artifact
(see [`agent-instruction-build.md`](agent-instruction-build.md) §7).

---

## 5. Auto-creation contract

The data directory and the wiki directory — and every writable sub-location beneath them —
are created when absent (FR-010). The agent directory is never created: it is build output
(FR-013).

An explicitly configured wiki directory equal to, or nested inside, the data directory is
accepted without error (spec Edge Cases) — the sibling relationship is a default, not a
constraint.

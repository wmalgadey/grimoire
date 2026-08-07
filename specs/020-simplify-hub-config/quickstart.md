# Quickstart & Validation: Simplify Hub CLI Configuration

**Feature**: `020-simplify-hub-config` | **Date**: 2026-08-07

Runnable scenarios that prove the feature end to end. Each maps to a user story and the
success criteria it verifies. Details live in the contracts — this file is the run guide.

- Directory surface → [`contracts/directory-options.md`](contracts/directory-options.md)
- Configuration keys → [`contracts/appsettings-paths.md`](contracts/appsettings-paths.md)
- Build output → [`contracts/agent-instruction-build.md`](contracts/agent-instruction-build.md)
- Resolution and validation states → [`data-model.md`](data-model.md)

---

## Prerequisites

- .NET 10 SDK (`backend/Directory.Build.props`)
- A checkout — nothing else. No environment variables, no hand-written configuration file.
- Provider credentials only for scenarios that actually run an agent: copy `.env-example`
  to `.env` **at the repository root** (not under any configured directory).

```bash
cp .env-example .env       # then fill in the API key
dotnet build backend/Grimoire.slnx
```

**Build first — always.** The hub never builds anything. The agent build delivers each
agent's *complete runtime* — worker DLL, dependency assemblies and instruction files — into
`.grimoire/agents/<agent-id>/` (`PublishAgentRuntime`), and the hub launches and reads from
there. Skipping the build means the hub refuses to start, naming what is missing.

```bash
ls .grimoire/agents/ingest
# Grimoire.IngestAgent.dll  Grimoire.IngestAgent.deps.json  Grimoire.IngestAgent.runtimeconfig.json
# Grimoire.AgentRuntime.dll  Grimoire.Domain.dll  Anthropic.dll  OpenTelemetry.*.dll  …  Instructions/

ls .grimoire/agents/ingest/Instructions
# system-prompt.md  default-user-prompt.md  policy.json
```

---

## Scenario 1 — Run with no configuration at all (US1 · SC-001)

```bash
dotnet run --project backend/src/Grimoire.Hub
```

**Expected**: the hub starts with no flags and no environment variables. The
`paths_resolved` startup line reports every location with `source=config-file`, and
`.grimoire/` and `llm-wiki/` are created if absent:

```text
Runtime paths resolved. data_dir=<cwd>/.grimoire wiki_dir=<cwd>/llm-wiki
agent_dir=<cwd>/.grimoire/agents secrets_file=<cwd>/.env sources=data_dir=config-file, ...
```

Same for any CLI command — no flags required:

```bash
dotnet run --project backend/src/Grimoire.Hub -- lint-run
```

**Also check** the surface is three switches wide (SC-002):

```bash
dotnet run --project backend/src/Grimoire.Hub -- --help
# "Server options:" lists exactly --data-dir, --agent-dir, --wiki-dir
```

## Scenario 2 — Point the wiki somewhere else (US2 · SC-004)

```bash
dotnet run --project backend/src/Grimoire.Hub -- \
  submit-source --path ./some-source.md --wiki-dir /tmp/my-wiki
```

**Expected**: `/tmp/my-wiki/` receives the pages, `index.md`, `log.md`, `tasks/`,
`conversations/`, `findings/` and `remediation-tasks/`. `.grimoire/` still holds `raw/`,
`state/` and `write-locks/`, and the agent directory is untouched — one option, nothing
else to set.

## Scenario 3 — Relocate the runtime data folder (US3 · SC-003)

```bash
dotnet run --project backend/src/Grimoire.Hub -- submit-source --path ./some-source.md --data-dir /tmp/env-a
```

**Expected**: `/tmp/env-a/` holds `raw/`, `state/operational-state.db` and `write-locks/`
— but the wiki lands at `<cwd>/llm-wiki` and the agent directory stays at its own default
`<cwd>/.grimoire/agents`, neither one moving into `/tmp/env-a` (US3 AS2 — relocating
`DataDir` moves only what is actually anchored on it; `WikiDir` and `AgentDir` are
independent cwd-anchored roots of their own). The secrets file is still read from
`<cwd>/.env` (SC-011).

## Scenario 4 — Custom agent folder fed by the build (US4 · SC-008)

```bash
dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=/tmp/my-agents
ls /tmp/my-agents/ingest                  # worker dll + deps + assemblies + Instructions/
ls /tmp/my-agents/ingest/Instructions     # system-prompt.md  default-user-prompt.md  policy.json

dotnet run --project backend/src/Grimoire.Hub -- lint-run --agent-dir /tmp/my-agents
```

**Expected**: the whole agent runtime — binaries and instructions — lives in
`/tmp/my-agents`, and the hub launches agents from there. Edit an agent's
`Instructions/system-prompt.md` source, rebuild with the same property, and the change
appears in `/tmp/my-agents/<agent>/Instructions/system-prompt.md` (US4 AS3).

**Also check the build clears stale artifacts** (SC-008):

```bash
touch /tmp/my-agents/ingest/Instructions/stale-leftover.md
dotnet build backend/Grimoire.slnx -p:GrimoireAgentDir=/tmp/my-agents
ls /tmp/my-agents/ingest/Instructions/stale-leftover.md   # gone
```

**Failure path (US4 AS2 · SC-007)**:

```bash
mkdir -p /tmp/empty-agents
dotnet run --project backend/src/Grimoire.Hub -- --agent-dir /tmp/empty-agents
# fails, naming agent_dir and /tmp/empty-agents
```

## Scenario 4b — The hub never builds (2026-08-07 directive)

```bash
mv .grimoire/agents/ingest/Grimoire.IngestAgent.dll /tmp/
dotnet run --project backend/src/Grimoire.Hub --no-build
```

**Expected**: the hub fails at **startup**, not at first dispatch, naming the missing
worker and telling you what to do:

```text
Grimoire.IngestAgent.dll not found in the agent directory.
Build first: dotnet build backend/Grimoire.slnx
```

It does **not** compile the agent to recover. Rebuild to continue:

```bash
dotnet build backend/Grimoire.slnx
```

**Also check**: no path configuration can point the hub at a `.csproj`. There is no
`--agent-worker` switch, and the launch mode is `dotnet <dll>` only — verified by
`Grimoire.ArchTests/NoRuntimeBuildInvocationRuleTests`.

## Scenario 5 — Internal layout via the configuration file (US5)

Edit `backend/src/Grimoire.Hub/appsettings.json`:

```json
"StateDb": "/mnt/fast-ssd/operational-state.db"
```

```bash
dotnet build backend/Grimoire.slnx && dotnet run --project backend/src/Grimoire.Hub
```

**Expected**: only the database moves; `raw/`, `write-locks/` and everything else stay
under `.grimoire/`. No new command-line switch exists or is needed (FR-015).

**Precedence check (US5 AS2 · SC-005)**:

```bash
Grimoire__Paths__DataDir=/tmp/from-env \
  dotnet run --project backend/src/Grimoire.Hub -- --data-dir /tmp/from-cli
# paths_resolved reports data_dir=/tmp/from-cli, source=command-line
```

Drop the switch and the environment value wins (`source=environment`); drop both and the
file wins (`source=config-file`).

## Scenario 6 — Missing configuration fails loudly (SC-006)

```bash
cp backend/src/Grimoire.Hub/bin/Debug/net10.0/appsettings.json /tmp/appsettings.bak
echo '{}' > backend/src/Grimoire.Hub/bin/Debug/net10.0/appsettings.json
dotnet run --project backend/src/Grimoire.Hub --no-build
# fails naming appsettings.json and the missing keys; no silent default
cp /tmp/appsettings.bak backend/src/Grimoire.Hub/bin/Debug/net10.0/appsettings.json
```

## Scenario 7 — Eval runs ignore hub configuration (SC-009 · SC-010)

```bash
dotnet run --project backend/src/Grimoire.EvalRunner -- status
dotnet run --project backend/src/Grimoire.EvalRunner -- replay --scenario adversarial-source
```

**Expected**: recordings resolve from
`backend/tests/Grimoire.AgentEvals/Fixtures/recordings/` and instructions from the agent
projects' `Instructions/` sources. `--recordings-root` is rejected as unrecognized. Results
are identical with the three hub directory options set to arbitrary locations, and no agent
build is required.

---

## Automated verification

```bash
./scripts/test-fast.sh                                        # arch + domain tiers (rules R1–R4)
dotnet test backend/tests/Grimoire.IntegrationTests            # path resolution, precedence, failures
dotnet test backend/tests/Grimoire.AgentEvals                  # replay tier against relocated fixtures
```

The full mapping from success criterion to test lives in
[`plan.md`](plan.md) `## Test Strategy`.

---

## Migrating an existing checkout

No migration is performed by the hub (FR-014). For a checkout that predates this change:

```bash
mv data/state data/raw data/write-locks .grimoire/     # git-ignored working state
mv wiki/* llm-wiki/ 2>/dev/null || true
mv tasks conversations remediation-tasks llm-wiki/
mv data/findings llm-wiki/findings
mv data/.env .env
rmdir data 2>/dev/null || true
```

Nothing detects the old layout; skipping this simply starts from empty directories.

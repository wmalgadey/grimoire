# Quickstart: Explicit Path Configuration (005-content-root-config)

Validation scenarios proving the feature end-to-end. Contracts:
[path-configuration](contracts/path-configuration.md),
[agent-launch](contracts/agent-launch.md); defaults: [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK, repo checkout, `dotnet build backend/Grimoire.sln` succeeds.
- One-time migration (below) applied to the checkout.

## One-time migration of the existing checkout (R7)

From the checkout root:

```bash
mkdir -p data/state
# wiki/ stays where it is — it already matches the <base>/wiki default and remains
# independently committable (its own git repo if desired).
git mv agents data/agents
[ -d raw ] && mv raw data/raw || mkdir -p data/raw
mv .env data/.env
[ -f backend/data/operational-state.db ] && mv backend/data/operational-state.db data/state/
```

Then: update `.gitignore` (`backend/data/` → `data/state/`, add `data/raw/`), rewrite
`data/agents/ingest/policy.json` prefixes to content-root-relative
(`pages/`, `tasks/`, `index.md`, `log.md`; bump `version`), and update
`.vscode/launch.json` per the scenarios below.

## Scenario 1 — Zero-config start from the checkout (User Story 2, SC-003)

```bash
cd /Volumes/Daten/grimoire
dotnet run --project backend/src/Grimoire.Hub -- --agent-worker backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj
```

Expected: startup succeeds; the `paths_resolved` log line shows the content root at
`/Volumes/Daten/grimoire/wiki` and every other location under
`/Volumes/Daten/grimoire/data/...`; submitting a small text source produces artifacts
only under `wiki/tasks`, `wiki/pages`, `data/raw`, `data/state`.
(Only the agent-worker value is passed because the checkout runs the worker from
source; a published install needs no arguments at all.)

## Scenario 2 — Production-style start outside any checkout (User Story 1, SC-001)

```bash
PUB=/tmp/grimoire-prod
dotnet publish backend/src/Grimoire.Hub -o $PUB/app
dotnet publish backend/src/Grimoire.IngestAgent -o $PUB/app
mkdir -p $PUB/base && cd $PUB/base            # no .git, no project folders anywhere
cp -R /Volumes/Daten/grimoire/data/agents ./data/agents   # required inputs
cp /Volumes/Daten/grimoire/data/.env ./data/.env
dotnet $PUB/app/Grimoire.Hub.dll --content-root /Volumes/Daten/parainoid/llm-wiki
```

Expected: startup succeeds without git installed/available; content root is the
external wiki (the default `$PUB/base/wiki` is not used or created); all other
locations auto-resolve/auto-create under `$PUB/base/data`;
an end-to-end ingest writes pages into the external wiki and raw/state under
`$PUB/base/data`. Task-artifact page paths are content-root-relative (`pages/...`).

## Scenario 3 — Misconfiguration fails fast (User Story 3, SC-002)

```bash
cd $(mktemp -d)
dotnet $PUB/app/Grimoire.Hub.dll --instructions-dir /nonexistent/agents/ingest
```

Expected: exit code ≠ 0 before serving any request; stderr/log contains
`paths_validation_failed` naming `instructions_dir`, the configured value
`/nonexistent/agents/ingest`, and the reason. Same check for `--secrets-file` and
`--agent-worker` pointing nowhere.

## Scenario 4 — Precedence and overrides (FR-005)

```bash
Grimoire__Paths__ContentRoot=/tmp/env-wiki dotnet $PUB/app/Grimoire.Hub.dll --content-root /tmp/cli-wiki
```

Expected: `paths_resolved` shows content root `/tmp/cli-wiki` with source
`command-line` (CLI beats environment); dropping the CLI switch yields `/tmp/env-wiki`
with source `environment`; dropping both falls back to `appsettings.json`/default.

## Scenario 5 — No repo discovery (SC-004, arch rule R8)

```bash
dotnet test backend/tests/Grimoire.ArchTests --filter RuntimePathsBoundary
grep -rn "rev-parse" backend/src && echo "FAIL: discovery still present" || echo OK
```

Expected: arch tests pass; no production source references `rev-parse` /
`FindRepoRoot`; integration test asserts the agent receives `--wiki-root` on every
dispatch.

## Launch configurations (one codebase, config-only split)

- **Dev** (`Hub (dev: litellm proxy + NVIDIA)`): `cwd` = `${workspaceFolder}`; args:
  `--agent-worker ${workspaceFolder}/backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj`.
  The wiki defaults to `${workspaceFolder}/wiki`; everything else beneath
  `${workspaceFolder}/data`.
- **Prod** (`Hub (prod: external wiki + Anthropic)`): `cwd` = `${workspaceFolder}`
  (or any base), args: `--content-root /Volumes/Daten/parainoid/llm-wiki` plus
  `--agent-worker …` as above. Only configuration values differ between the two —
  no code-level profile logic (clarification Q3).

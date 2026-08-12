# Quickstart: Validating the Independent Memory Directory Root

**Feature**: `022-memory-directory-root` | **Plan**: [plan.md](./plan.md) |
**Contracts**: [directory-options.md](./contracts/directory-options.md),
[paths-observability.md](./contracts/paths-observability.md)

Runnable scenarios that prove the feature end to end. Each maps to the success criteria it
verifies. Field names, defaults and precedence rules are in the contracts — this document
does not restate them.

---

## Prerequisites

```bash
# From the repository root.
dotnet build backend/Grimoire.slnx
```

The agent directory must hold a built agent runtime before the hub will start
(ADR-022 — `agent_dir` is a `RequiredInput`); the build above produces it. A `.env` at the
repository root is required for any scenario that dispatches an agent; the
configuration-only scenarios (1, 2, 3, 5) do not need one beyond its presence.

Work in a scratch directory so the repo's own `llm-wiki/` and `memory/` are untouched:

```bash
export WORK=$(mktemp -d) && cd "$WORK"
export HUB="dotnet run --project /path/to/grimoire/backend/src/Grimoire.Hub --"
```

---

## Scenario 1 — Zero configuration creates the memory root (US3, SC-005, SC-006)

```bash
cd "$WORK" && ls -a          # empty
$HUB --help                  # any command that composes paths will do
ls -a
```

**Expected**: `memory/` exists alongside `.grimoire/` and `llm-wiki/`, created
automatically. `--help` lists `--memory-dir` in the **Server options** section with the
other three roots.

Then check the startup report:

```bash
$HUB lint status 2>&1 | grep paths_resolved
```

**Expected**: one `paths_resolved` line at Information level containing
`memory_dir=<WORK>/memory` alongside `data_dir`, `wiki_dir` and `agent_dir`, and a
`sources=` list containing `memory_dir=config-file`.

---

## Scenario 2 — Relocating the memory root moves only bookkeeping (US1 AS1/AS2, SC-001)

```bash
cd "$WORK"
$HUB submit --memory-dir /tmp/gm-memory --source ./some-source.md
```

**Expected**:

- `/tmp/gm-memory/tasks/<task_id>.md` exists — the task artifact.
- `<WORK>/llm-wiki/` contains the created/updated pages, `index.md` and `log.md`, and
  **no** `tasks/`, `conversations/`, `findings/` or `remediation-tasks/` folder.
- `<WORK>/memory/` was not used for this run.

Repeat for the other three record kinds to cover all of SC-001:

```bash
$HUB query --memory-dir /tmp/gm-memory "what does the wiki cover?"   # → conversations/
$HUB lint run --memory-dir /tmp/gm-memory                            # → findings/
                                                                     #   remediation-tasks/
```

---

## Scenario 3 — The four roots are mutually independent (US2, SC-002)

Relocate each root alone and confirm the other three stay put. Read the resolved values
straight out of the startup report rather than inferring them from disk:

```bash
cd "$WORK"
for FLAG in "--wiki-dir /tmp/gw" "--data-dir /tmp/gd" "--agent-dir /tmp/ga" "--memory-dir /tmp/gm"; do
  echo "== $FLAG"
  $HUB lint status $FLAG 2>&1 | grep -o '\(data\|wiki\|agent\|memory\)_dir=[^ ]*'
done
```

**Expected**: in each row exactly one of the four resolved paths differs from its default;
the other three read `<WORK>/.grimoire`, `<WORK>/llm-wiki`, `<WORK>/.grimoire/agents`,
`<WORK>/memory` respectively. In particular, `--wiki-dir /tmp/gw` leaves
`memory_dir=<WORK>/memory` (US2 AS1) and `--memory-dir /tmp/gm` leaves
`wiki_dir=<WORK>/llm-wiki` (US2 AS2). `--data-dir` and `--agent-dir` leave `memory_dir`
untouched (US2 AS3).

---

## Scenario 4 — Precedence (SC-003)

Note the **nested** environment-variable name — the configuration keys are grouped by
anchoring root, so it is `Grimoire__Paths__Memory__Dir`, not `Grimoire__Paths__MemoryDir`.

```bash
cd "$WORK"
Grimoire__Paths__Memory__Dir=/tmp/from-env $HUB lint status 2>&1 | grep -o 'memory_dir=[^ ]*'
# → memory_dir=/tmp/from-env

Grimoire__Paths__Memory__Dir=/tmp/from-env $HUB lint status --memory-dir /tmp/from-cli 2>&1 \
  | grep -o 'memory_dir=[^ ]*'
# → memory_dir=/tmp/from-cli
```

**Expected**: command line beats environment beats configuration file, evaluated for this
option independently of the other three.

**Negative check — the old flat name must fail loudly** (SC-010, FR-014):

```bash
Grimoire__Paths__MemoryDir=/tmp/old-name $HUB lint status; echo "exit=$?"
```

**Expected**: startup aborts naming the superseded key and its replacement
(`Grimoire:Paths:MemoryDir → Grimoire:Paths:Memory:Dir`), with a
`paths_configuration_superseded` event at Error and one
`grimoire.hub.path_resolution_failures_total{reason="configuration_superseded"}`
increment. It must **not** silently resolve to `<WORK>/memory` — that silent fallback is
exactly what FR-014 exists to prevent.

Repeat for the three pre-existing roots, whose keys this feature also renames
(`Grimoire__Paths__DataDir`, `Grimoire__Paths__WikiDir`, `Grimoire__Paths__AgentDir`), and
for the seven sub-path keys. The automated equivalent covers all eleven:

```bash
dotnet test backend/tests/Grimoire.IntegrationTests \
  --filter "FullyQualifiedName~SupersededConfigurationKeyTests"
```

---

## Scenario 5 — A missing configuration key fails by name (SC-004)

Two variants, because the grouped shape makes them distinct code paths:

```bash
cd "$WORK"
# (a) Copy the hub's appsettings.json, delete Grimoire:Paths:Memory:Dir but keep the
#     rest of the Memory group, and point the host at it.
# (b) Same, but delete the entire Grimoire:Paths:Memory group.
```

**Expected, both variants**: startup aborts with
`GrimoirePathConfigurationMissingException`; the message names `appsettings.json` and lists
the full key path `Grimoire:Paths:Memory:Dir`. A `paths_configuration_missing` event is
logged at Error with `missing_keys` containing that same full path, and
`grimoire.hub.path_resolution_failures_total{reason="configuration_missing"}` is
incremented once. No directory is created.

Variant (b) specifically must **not** produce a `NullReferenceException` — the group
property's `= new()` initializer is what routes an absent group into the same named
failure.

The automated equivalent is the more reliable check here, since it asserts the exception
type and the metric rather than only stderr text:

```bash
dotnet test backend/tests/Grimoire.IntegrationTests \
  --filter "FullyQualifiedName~PathConfiguration.StartupValidation"
```

---

## Scenario 6 — Pre-existing records are left alone (SC-007, FR-011)

```bash
cd "$WORK"
mkdir -p llm-wiki/tasks llm-wiki/conversations
echo "legacy" > llm-wiki/tasks/old-task.md
echo "legacy" > llm-wiki/conversations/old-conv.md
$HUB lint status >/dev/null 2>&1
find llm-wiki -name '*.md' -path '*/tasks/*' -o -name '*.md' -path '*/conversations/*'
find memory -type f
```

**Expected**: both legacy files are still at their original paths, byte-identical, and
`memory/` contains none of them. The hub neither detected nor migrated them — relocating
them is a manual operator step.

---

## Scenario 7 — Instruction files no longer describe the folders as wiki-reachable (SC-008)

```bash
cd /path/to/grimoire
grep -nE 'tasks/|conversations/|findings/|remediation-tasks/|\[\[tasks/' \
  backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/system-prompt.md
```

**Expected**: no matches. Specifically gone: the "Skip the reserved harness folders" step
in all three prompts, the four `harness-owned` lines and the trailing paragraph in the
Ingest prompt's Wiki Folder Structure diagram, and both `Task: [[tasks/<task_id>.md]]`
citations in its `log.md` template — the last replaced by a bare `Task: <task_id>`.

Confirm the agent side still round-trips correctly, which is what the bare id preserves:

```bash
cd "$WORK" && $HUB submit --source ./some-source.md
grep -c 'harness backstop' llm-wiki/log.md
```

**Expected**: `0` on a successful run. The agent's own paragraph names the task id, so
`WikiLogAppender` finds it and skips appending a duplicate harness entry. A `1` here means
the task reference was dropped from the prompt rather than de-linked — see
[research.md R3](./research.md).

---

## Scenario 8 — The configuration file reads as the directory layout (author directive, R8)

```bash
cd /path/to/grimoire
sed -n '/"Paths"/,/^    }/p' backend/src/Grimoire.Hub/appsettings.json
```

**Expected**: four groups — `Data`, `Wiki`, `Agent`, `Memory` — each opening with a `Dir`
key, with every sub-path nested inside the group whose root it resolves against, and
`SecretsFile` sitting outside all four. Reading the file top to bottom should tell you the
whole layout without opening `GrimoirePathResolver`.

The check that this stays true is not visual:

```bash
dotnet test backend/tests/Grimoire.IntegrationTests \
  --filter "FullyQualifiedName~PathGroupingInvariantTests"
```

**Expected**: green. This is ADR-024 rule M5 — for each group, relocating that group's
`Dir` moves every sub-path declared in it and nothing declared elsewhere. Declaring a key
under `Memory` while anchoring it at `dataDir` in the resolver fails here, which is what
makes the grouping load-bearing rather than decorative.

---

## Test suites

```bash
# Fast tier — includes the three structural rules (ADR-024 M1/M2/M3).
./scripts/test-fast.sh

# Integration tier — the path-configuration contract suite.
dotnet test backend/tests/Grimoire.IntegrationTests \
  --filter "FullyQualifiedName~PathConfiguration"

# Full integration tier.
dotnet test backend/tests/Grimoire.IntegrationTests

# Evals. NOTE: this is a PR gate — CI runs the project unfiltered, not --filter Tier=Fast.
dotnet test backend/tests/Grimoire.AgentEvals
```

---

## Re-capturing eval recordings

**Required before merge.** The FR-012 prompt edits change all three `system-prompt.md`
files, which invalidates the `system_prompt` fingerprint in all 22 scenario manifests and
the per-turn `system_prompt_sha256` in all 230 sample files. `dotnet test
backend/tests/Grimoire.AgentEvals` will fail with `TrustStatus.Stale` until they are
re-captured. There is no bless/accept verb — a manifest cannot be refreshed without fresh
samples.

**Preferred route** — the CI workflow, which needs no personal API key:

1. Trigger `.github/workflows/eval.yml` via `workflow_dispatch`. It captures every scenario
   through LiteLLM → NVIDIA NIM using `secrets.NVIDIA_NIM_API_KEY`.
2. Download the uploaded `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/**`
   artifact.
3. Commit the refreshed recordings.
4. Re-run `dotnet test backend/tests/Grimoire.AgentEvals` and confirm every scenario reports
   `Trusted` and still meets its score threshold.

**Local fallback** — requires a live provider:

```bash
# Either ANTHROPIC_AUTH_TOKEN, or the GRIMOIRE_EVAL_PROVIDER_{BASE_URL,MODEL,API_KEY}
# triple — setting both is a configuration error (exit 2).
dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario <scenario-id>
```

Step 4 is the substantive one: it is the only evidence that removing the reserved-folder
guidance did not change agent behavior. `convention-adherence` and
`log-paragraph-specificity` are the two Ingest scenarios most exposed to the task-reference
change.

# scripts/mutation-tests/

The measurement harness behind the 2026-08-23 test-suite audit (issues #180 and #181).
Nothing here is a gate; every script answers a question and writes CSV a human reads.

Output goes to `$MEASURE_OUT`, default `artifacts/` at the repo root — a local working
directory, gitignored like `docs/reports/mutation/`.

## Suite analysis — run in this order

| Script | Question it answers | Reads | Writes |
|---|---|---|---|
| `parse_trx.py` | how long does each individual test take? | `backend/tests/*/TestResults/*.trx` | `raw-tests.csv` |
| `classify.py` | which infrastructure does each test actually touch? | `raw-tests.csv` + test sources | `tests-classified.csv` |
| `build_inventory.py` | which tier does each test belong in, and why? | `tests-classified.csv` | `test-inventory.csv` |
| `cochange.py [limit]` | is any test file coupled to one production class? | `git log` (generated on first run) | `cochange-small-commits.csv` |

`parse_trx.py` needs a `dotnet test` run to have happened first — TRX files are written to
each test project's `TestResults/` and are gitignored.

`cochange.py` restricts to commits touching at most `limit` backend files (default 15);
the large feature commits otherwise generate noise pairs that flatten every concentration
score. The 2026-08-23 run found a maximum concentration of 0.30 — no test file in this
repository mirrors a single production class.

## Mutation score — `run-guardrails-stryker.sh`

Scores `Grimoire.AgentRuntime/Guardrails/**` against `Grimoire.IntegrationTests`, scoped
through `stryker-guardrails-config.json`. Roughly 24 minutes on 4 cores; reports into
`docs/reports/mutation/agent-runtime-guardrails/` by default.

The scoping is done in a **config file**, not with CLI `--mutate` globs, and the reason is
worth keeping: Stryker.NET injects every mutant into the compiled assembly first and applies
the file filter afterwards, at test-selection time. A run therefore reports ~1323 "mutants
created" for all of `Grimoire.AgentRuntime` and may warn about a compile error in a file
outside the filter — neither means the scoping failed. The filter's effect appears later as
`Removed by mutate filter`. A run interrupted before that line tells you nothing.

`scripts/mutation-test.sh` remains the general entry point for every other mutation target;
this script exists because the guardrail surface needed a scoped run with a documented
result (#181: 2.35 %, or 0.35 % excluding message-string mutants).

## One caveat on `AgentDirBuildContractTests`

Running the full integration suite executes
`PathConfiguration.AgentDirBuildContractTests.RebuildingAfterInstructionEdit_…`, which
appends a marker to the **tracked** file
`backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` and restores it in a
`finally`. Interrupt the run and the marker stays. Check `git status` after an aborted
measurement.

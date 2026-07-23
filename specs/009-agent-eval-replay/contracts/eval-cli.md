# Contract: Eval Command CLI (`Grimoire.EvalRunner`)

**Feature**: 009-agent-eval-replay

Invocation: `dotnet run --project backend/src/Grimoire.EvalRunner -- <subcommand> [options]`

## Subcommands

### `capture`

Runs live evaluation and (re)writes recordings.

| Option | Required | Default | Meaning |
|--------|----------|---------|---------|
| `--scenario <id>` | no | all scenarios | Restrict to one scenario (repeatable) |
| `--samples <n>` | no | scenario default (10, clamp 1–20) | Samples per scenario |
| `--recordings-root <path>` | no | `data/evals/recordings` | Recording store root |
| `--summary <path>` | no | stdout | Write the run summary (format-eval-summary compatible) |

Behavior contract:
- Provider selection reuses 007's env-var contract (`ANTHROPIC_AUTH_TOKEN` xor complete
  `GRIMOIRE_EVAL_PROVIDER_*`); neither → exit 2 with the named-options message; both →
  exit 2 with the conflict message (no silent pick). No implicit defaults.
- Per sample: isolated workspace (fixture + instruction copies under OS temp), real
  `Grimoire.IngestAgent` spawned via its production CLI (ADR-002) with
  `GRIMOIRE_MODEL_CAPTURE_PATH` set; 120 s per-model-call bound enforced; judge verdicts
  captured for judge-scored scenarios.
- On scenario success or failure, recordings for a captured scenario are replaced
  wholesale; partially captured scenarios are not committed to the store.
- Scores are reported against unchanged spec thresholds; exit 0 = all captured scenarios
  meet thresholds, exit 1 = threshold failure, exit 2 = configuration/connectivity error
  (distinct from judgment failure).
- Credential material never appears in output, recordings, or summaries.

### `replay`

Replays recordings and scores them — the same code path the replay tests use.

| Option | Required | Default | Meaning |
|--------|----------|---------|---------|
| `--scenario <id>` | no | all | Restrict scenario(s) |
| `--recordings-root <path>` | no | `data/evals/recordings` | Recording store root |
| `--summary <path>` | no | stdout | Summary output |

Behavior contract:
- Requires no provider configuration; makes zero network calls.
- Per sample: isolated workspace, real `Grimoire.IngestAgent` spawned with
  `GRIMOIRE_MODEL_REPLAY_PATH` pointing at the sample recording.
- Trust status per sample: `trusted` | `stale` | `missing` | `mismatch`; every result
  names provenance (model, captured-at, recording path).
- Exit 0 = all trusted and thresholds met; exit 1 = threshold failure on trusted
  replays; exit 3 = any stale/missing/mismatch (actionable message names the
  `capture` invocation to run).

### `status`

Pure staleness/provenance report; touches no workspaces, spawns no agent.

- Lists every scenario with manifest provenance and computed trust status.
- Exit 0 = all current; exit 3 = any stale/missing (same messages as `replay`).

## Global rules

- All subcommands emit the observability signals declared in `plan.md ## Observability`.
- Machine-readable summary format is the existing TRX/markdown pipeline consumed by
  `scripts/ci/format-eval-summary` (007 FR-007 contract preserved for `eval.yml`).
- The runner never modifies `data/agents/` or repo wiki content; it writes only to the
  recordings root, temp workspaces, and the summary path.

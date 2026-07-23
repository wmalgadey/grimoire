# Quickstart: Recorded-Replay Agent Evaluations

**Feature**: 009-agent-eval-replay

Validation scenarios proving the feature end-to-end. Contracts:
[eval-cli.md](./contracts/eval-cli.md), [recording-format.md](./contracts/recording-format.md).

## Prerequisites

- .NET 10 SDK; repo cloned; `dotnet build backend/Grimoire.slnx` succeeds.
- For capture only: a provider per 007 — either `ANTHROPIC_AUTH_TOKEN` or the complete
  `GRIMOIRE_EVAL_PROVIDER_*` triple (NIM proxy: `scripts/nim/run-litellm-proxy.sh`).
- Replay needs no configuration at all.

## 1. Replay at zero cost (US1 / SC-001..SC-004)

```bash
env -u ANTHROPIC_AUTH_TOKEN -u GRIMOIRE_EVAL_PROVIDER_API_KEY \
  dotnet run --project backend/src/Grimoire.EvalRunner -- replay
```

Expected: all scenarios replay from `data/evals/recordings/`, zero network calls,
per-sample results show trust status `trusted` + provenance (model, captured-at);
exit 0; wall clock < 5 min. Running it twice yields identical summaries (SC-004).

Same tier as tests (what PR CI runs):

```bash
dotnet test backend/tests/Grimoire.AgentEvals
```

Expected: replay eval tests execute — **0 skipped** (SC-008).

## 2. Capture / refresh recordings (US2)

```bash
dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario update-over-duplicate --samples 3
```

Expected: live run against the configured provider; `data/evals/recordings/update-over-duplicate/`
rewritten (manifest + samples) with model, timestamp, fingerprints; summary scores vs.
unchanged thresholds; no credential material in any written file (`grep` the directory
for the key value → no hits). With both providers configured simultaneously: exit 2,
conflict named. With neither: exit 2, options named.

## 3. Staleness gate (US3 / SC-005, SC-009, FR-016)

```bash
echo "- test drift" >> data/agents/ingest/system-prompt.md
dotnet run --project backend/src/Grimoire.EvalRunner -- status
```

Expected: every scenario reports `stale` with changed fingerprint `system_prompt` and
the exact `capture` command to refresh; exit 3. `dotnet test backend/tests/Grimoire.AgentEvals`
now fails with the same staleness message (this failing in PR CI is the FR-016 merge
gate). Revert the edit → `status` exits 0, tests green.

## 4. Mismatch & missing outcomes (FR-009/FR-010)

- Delete one `sample-NN.json` → replay reports `missing` for that sample, names the
  capture command, exit 3.
- Edit a byte inside a remaining sample file → `mismatch` (manifest hash), exit 3 —
  never a judgment failure.

## 5. CI

- `ci.yml` backend job: replay eval tests run hermetically, no provider secret, zero
  skips asserted.
- `eval.yml` (`workflow_dispatch`): runs `capture` with the repository secret, uploads
  recordings + summary as artifacts, comments the summary on an associated PR (007
  contract preserved). Trigger once manually and verify artifacts contain refreshed
  recordings ready to commit.

## Observability spot-check

Run replay with the console/in-memory exporter (ADR-005 dev setup) and verify
`eval.replay_run` spans, `eval_replay_result` events (task_id-correlated), and
`grimoire.eval.replay_results_total{trust_status="trusted"}` increments; capture runs
emit `eval.capture_run` / `eval_recording_captured` / `grimoire.eval.recordings_captured_total`.

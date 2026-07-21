# Quickstart: Validating Agent Evals on the Affordable Provider

Proves User Stories 1–3 (spec.md) end-to-end. See `contracts/eval-provider-env-vars.md` for
the full variable contract and `data-model.md` for the gate's resolution rules.

## Prerequisites

- `.NET 10` SDK installed (matches `backend/Grimoire.slnx`).
- `data/.env` populated with `NVIDIA_API_KEY` (see `.env-example`).
- No `ANTHROPIC_AUTH_TOKEN` set in your shell (to prove User Story 1's "no subscription"
  path — unset it if it's exported from a prior session).

## Scenario 1 — Local run without an Anthropic subscription (User Story 1)

1. Start the LiteLLM proxy: `scripts/nim/run-litellm-proxy.sh` (leave running; it listens on
   `http://localhost:4000`, exposing `model_name: nvidia-model` per
   `scripts/nim/litellm_config.yaml`).
2. In a second shell, from the repo root:
   ```bash
   export GRIMOIRE_EVAL=1
   export GRIMOIRE_EVAL_PROVIDER_BASE_URL=http://localhost:4000
   export GRIMOIRE_EVAL_PROVIDER_MODEL=nvidia-model
   export GRIMOIRE_EVAL_PROVIDER_API_KEY="$NVIDIA_API_KEY"
   dotnet test backend/tests/Grimoire.AgentEvals
   ```
3. **Expected**: all eval tests execute (none report `Skipped`); each completed run's task
   artifact names `nvidia-model` in its `Model` field (SC-002).

## Scenario 2 — Gate skip and fail-fast behavior (FR-003, FR-012)

1. Unset every eval-related variable and run `dotnet test backend/tests/Grimoire.AgentEvals`.
   **Expected**: every eval test reports `Skipped`, with a reason naming both the Anthropic
   and affordable-provider options.
2. Set `ANTHROPIC_AUTH_TOKEN` **and** all three `GRIMOIRE_EVAL_PROVIDER_*` variables at once,
   then run again. **Expected**: the run fails fast with a configuration-conflict error — it
   does not skip and does not silently pick a provider.

## Scenario 3 — Connectivity/timeout failure is not misreported (FR-004, FR-013)

1. Set the affordable-provider variables with `GRIMOIRE_EVAL_PROVIDER_BASE_URL` pointing at
   an address nothing is listening on (e.g. `http://localhost:1`).
2. Run one eval test. **Expected**: it fails with an actionable connectivity error — not a
   skip, not an agent-judgment failure message.

## Scenario 4 — On-demand CI run (User Story 2)

1. In the GitHub UI (or `gh workflow run eval.yml -f pr_number=<PR#>`), trigger `eval.yml`
   manually against a branch with an open PR.
2. **Expected**: the run completes without any Anthropic secret configured anywhere in CI;
   a summary comment appears on the named PR with per-test pass/fail and scores; the run's
   artifacts include the eval transcripts.
3. Repeat via `gh workflow run eval.yml` **without** `pr_number`. **Expected**: no PR
   comment is posted; the job summary and artifacts still contain the full results.

## Scenario 5 — Provider transparency and Anthropic path unchanged (User Story 3)

1. Run the suite once against the affordable provider (Scenario 1) and once with only
   `ANTHROPIC_AUTH_TOKEN` set (no affordable variables). **Expected**: both runs' task
   artifacts each name the model that actually served them (`nvidia-model` vs. the Anthropic
   model), and the Anthropic-only run behaves exactly as it did before this feature
   (FR-011).

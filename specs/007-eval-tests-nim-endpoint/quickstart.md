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

   **Manual validation (T017, executed 2026-07-21)**: ran `CatalogDiscoverabilityEvals` with
   `GRIMOIRE_EVAL_SAMPLES=1` against the real LiteLLM proxy from Scenario 1. The eval
   executed (did not skip); the task artifact recorded `model: "nvidia-model"` even though
   the sample itself failed — one agent turn exceeded the 120s bound
   (`failure_reason: "Model call exceeded the 120s timeout."`), correctly caught by
   `TimeoutEnforcingModelClient` (FR-013) after the agent's `list_files` calls were denied
   (pre-existing policy path behavior, unrelated to provider choice) and a subsequent turn
   stalled. This proves the full harness path end-to-end (gate → provider wiring → real
   network call → timeout enforcement → sanitized failure recording → correct `Model`
   field) but is a single sample, not a threshold measurement — SC-006's ≥90% pass-rate
   claim (T036) requires a full `GRIMOIRE_EVAL_SAMPLES`-sized run per eval class, which is
   a slower and more expensive live run left to that task. No harness defect was found; no
   code change was made as a result of this run.

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

### One-time setup: repository secret (T022, manual — requires repo admin access)

`eval.yml` reads the affordable-provider credential from the `NVIDIA_NIM_API_KEY`
repository secret (never committed, never echoed — contracts/eval-workflow-dispatch.md).
Set it once, from the same value as `NVIDIA_API_KEY` in your local `data/.env`:

```bash
gh secret set NVIDIA_NIM_API_KEY --body "$NVIDIA_API_KEY"
```

or via GitHub UI: **Settings → Secrets and variables → Actions → New repository secret**.
This step is intentionally not automated by any Spec Kit command — it writes to shared
repository infrastructure and requires a maintainer to authorize it explicitly.

### Running the workflow

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

   **Automated validation (T026)**: `EvalProviderTransparencyTests` in
   `backend/tests/Grimoire.AgentEvals/` proves this hermetically for both providers (each
   run's `TaskArtifactDocument.Model` matches its configured model; the Anthropic-only path
   leaves `GRIMOIRE_INGEST_BASE_URL`/`GRIMOIRE_INGEST_MODEL` unmutated, per FR-011) without
   needing live credentials. A live side-by-side run against both a real Anthropic
   subscription and the real NIM endpoint was not additionally executed here — Scenario 1
   already proved the affordable path live (see above); running the Anthropic path live too
   is a real-credential, real-cost manual step left to whoever owns the Anthropic
   subscription.

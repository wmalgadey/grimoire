# Contract: `eval.yml` On-Demand CI Workflow

## Trigger

`workflow_dispatch` only — never `pull_request`, never scheduled (spec Assumptions: manual
triggering is the only new CI trigger in scope for this feature).

## Inputs

| Input | Required | Default | Purpose |
|-------|----------|---------|---------|
| `pr_number` | No | (none) | When set, results are posted as a comment on this PR (FR-007). When omitted, results publish as artifacts only — no comment is attempted (clarified no-PR fallback). |
| `sample_count` | No | Suite default (10, existing `EvalGate.ResolveSampleCount`) | Forwarded to `GRIMOIRE_EVAL_SAMPLES`; clamped 1–20 as today (FR-010, unchanged). |

## Required repository secret

| Secret | Purpose |
|--------|---------|
| `NVIDIA_NIM_API_KEY` | The affordable-provider credential; mapped into the job's `GRIMOIRE_EVAL_PROVIDER_API_KEY` (and, by the LiteLLM proxy process itself, into `NVIDIA_NIM_API_KEY` as `litellm_config.yaml` already expects). Read exclusively from the repository secret store (FR-005) — never committed, never echoed. |

## Job outline (informational — implementation detail is finalized in tasks.md)

1. Checkout, setup .NET 10 (mirrors `ci.yml`'s `backend` job).
2. Start `scripts/nim/run-litellm-proxy.sh` in the background; poll until it accepts
   connections on `:4000` before proceeding (fail the job early, with a clear message, if it
   never comes up — this is a connectivity failure, not silently skipped, per the same
   principle as FR-004).
3. Set `GRIMOIRE_EVAL=1`, `GRIMOIRE_EVAL_PROVIDER_BASE_URL=http://localhost:4000`,
   `GRIMOIRE_EVAL_PROVIDER_MODEL=nvidia-model`,
   `GRIMOIRE_EVAL_PROVIDER_API_KEY=${{ secrets.NVIDIA_NIM_API_KEY }}`, and
   `GRIMOIRE_EVAL_SAMPLES` (if `sample_count` was provided).
4. Run `dotnet test backend/tests/Grimoire.AgentEvals` with a results logger producing a
   machine-readable output file.
5. Always: upload the results file and eval transcripts as workflow artifacts (FR-007).
6. If `pr_number` was provided: post a summary comment to that PR via `gh pr comment`
   (`GITHUB_TOKEN`, `pull-requests: write` permission — no new secret). If omitted: skip
   commenting entirely (no attempt, no error).
7. Also always: write the same summary to `$GITHUB_STEP_SUMMARY` (job summary), so results
   are readable directly from the Actions run regardless of PR association.

## Isolation from the PR pipeline (SC-005)

`eval.yml` is a separate workflow file from `ci.yml`. `ci.yml` is not modified by this
feature: it gains no new job, no new secret reference, and no new required check. This is a
structural fact verifiable by inspecting `ci.yml`'s diff for this feature (expected: no
changes), not a runtime test.

## Secret-leakage guarantee (FR-008/SC-004)

- **Console logs**: GitHub Actions automatically masks any log output matching a registered
  secret's value once it is referenced via `secrets.NVIDIA_NIM_API_KEY` in the workflow —
  this is a platform guarantee, not something this feature implements.
- **Artifacts/transcripts**: covered by the harness-side sanitization test in plan.md's
  `## Test Strategy` (the credential must not appear in `TaskArtifactDocument.FailureReason`,
  `Narrative`, or recorded transcripts even on an auth-rejection error).

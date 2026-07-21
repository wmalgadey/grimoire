# Contract: Eval Provider Environment Variables

Consumed only by `backend/tests/Grimoire.AgentEvals` (via `EvalProviderResolver` /
`EvalGate`). Not read by any production (`src/`) code.

## Variables

| Variable | Required for | Notes |
|----------|---------------|-------|
| `GRIMOIRE_EVAL` | Enabling evals at all (existing, unchanged) | Must be `"1"`. |
| `ANTHROPIC_AUTH_TOKEN` | Anthropic path (existing, unchanged) | Same variable already used by the production ingest path (ADR-004). |
| `GRIMOIRE_EVAL_PROVIDER_BASE_URL` | Affordable path | e.g. `http://localhost:4000` for the local LiteLLM proxy. |
| `GRIMOIRE_EVAL_PROVIDER_MODEL` | Affordable path | Must match a `model_name` in `scripts/nim/litellm_config.yaml` (`nvidia-model` by default). |
| `GRIMOIRE_EVAL_PROVIDER_API_KEY` | Affordable path | Forwarded internally as `ANTHROPIC_AUTH_TOKEN` when constructing `AnthropicModelClient`; never logged verbatim (see sanitization test, plan.md `## Test Strategy`). |

## Resolution contract (`EvalGate` / `EvalProviderResolver`)

Given the five variables above, exactly one of three outcomes results — see
`data-model.md#EvalGateOutcome` for the full decision table:

| `ANTHROPIC_AUTH_TOKEN` set? | All 3 `GRIMOIRE_EVAL_PROVIDER_*` set? | Outcome |
|---|---|---|
| No | No | `Skipped` — reason names both options |
| Yes | No | `Enabled` — provider = Anthropic |
| No | Yes | `Enabled` — provider = Affordable |
| Yes | Yes | `ConfigurationError` — fails fast, names the conflict |
| No | Partial (1 or 2 of 3) | `Skipped` — a partial affordable config does not count as present |
| Yes | Partial (1 or 2 of 3) | `Enabled` — provider = Anthropic (partial affordable config is ignored, not a conflict) |

## Backward compatibility

A developer/CI run that sets only `GRIMOIRE_EVAL=1` + `ANTHROPIC_AUTH_TOKEN` (today's only
supported configuration) is unaffected — behaves exactly as before (FR-011).

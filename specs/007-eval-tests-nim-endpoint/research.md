# Phase 0 Research: Agent Eval Tests on Affordable Model Providers

## D1 — Reuse the existing `IModelClient` port; no new external-system boundary

**Decision**: This feature introduces no new port, adapter, or ADR. `AnthropicModelClient`
(`backend/src/Grimoire.IngestAgent/AgentCore/Adapters/Anthropic/AnthropicModelClient.cs`)
already:

- reads an optional `GRIMOIRE_INGEST_BASE_URL` and points the Anthropic .NET SDK client at
  it instead of `api.anthropic.com`;
- reads an optional `GRIMOIRE_INGEST_MODEL` (default `claude-opus-4-8`) and sends it as the
  `model` field on every request;
- reads its credential implicitly from `ANTHROPIC_AUTH_TOKEN` via the SDK's default
  `AnthropicClient()` constructor (confirmed by `AgentProcessHost.cs`, which sets exactly
  that variable in the spawned agent's environment before it constructs this same class).

Any endpoint that speaks the Anthropic Messages API protocol at `POST {base_url}/v1/messages`
is therefore already a supported provider for both the production ingest path and the eval
harness, with zero code changes to `AnthropicModelClient` — this is precisely what FR-002
requires ("documented configuration only — no code changes") and what ADR-010 already
certifies as the port/adapter for `IModelClient`.

**Rationale**: Constitution Principle I requires new external-system dependencies to go
through a port with an ADR naming it. There is no new external system here — the affordable
provider is consumed through the identical port and identical production adapter already
governed by ADR-010. Introducing a second adapter or a new ADR would be Big-Design-Up-Front
for a boundary that doesn't structurally change.

**Alternatives considered**:
- *New `IModelClient` implementation for NIM/LiteLLM* — rejected; would duplicate
  `AnthropicModelClient` for no behavioral difference, since the LiteLLM proxy speaks the
  same wire protocol once pointed at via `GRIMOIRE_INGEST_BASE_URL`.
- *New port `IAffordableModelClient`* — rejected; violates "one port per external system"
  (Principle I) since it's the same external-system *shape* (an Anthropic-Messages-API
  endpoint), only a different network address and credential.

## D2 — Eval-only configuration surface, distinct from the production env vars

**Decision**: Add three new environment variables, read only inside the
`Grimoire.AgentEvals` test project:

- `GRIMOIRE_EVAL_PROVIDER_BASE_URL`
- `GRIMOIRE_EVAL_PROVIDER_MODEL`
- `GRIMOIRE_EVAL_PROVIDER_API_KEY`

A new `EvalProviderResolver` (in `AgentEvalSupport.cs`) reads these plus the existing
`GRIMOIRE_EVAL` / `ANTHROPIC_AUTH_TOKEN`, and produces an `EvalGateOutcome` (see
`data-model.md`). When the resolved provider is "affordable," the resolver sets the process
environment's `GRIMOIRE_INGEST_BASE_URL`, `GRIMOIRE_INGEST_MODEL`, and `ANTHROPIC_AUTH_TOKEN`
from the `GRIMOIRE_EVAL_PROVIDER_*` values immediately before `AnthropicModelClient` is
constructed, so the existing production adapter needs no awareness of the eval-specific
variable names.

**Rationale**: FR-012 requires detecting "an Anthropic credential AND a complete
affordable-provider configuration both present" as a conflict. That distinction is only
possible if the two configurations are named differently — reusing `ANTHROPIC_AUTH_TOKEN`
for both would make the two states indistinguishable. Scoping the new variables to the test
project (rather than teaching `AnthropicModelClient` a second credential source) keeps the
production adapter's default-credential path byte-for-byte unchanged, satisfying FR-011
without an added constructor overload or branch in production code.

**Alternatives considered**:
- *Single provider-selector env var* (e.g. `GRIMOIRE_EVAL_PROVIDER=anthropic|affordable`)
  — considered during clarification; rejected by the answered clarification itself, which
  resolved ambiguous-both-configured as a hard error rather than adding a selector/override
  mechanism.
- *Extend `AnthropicModelClient` with an explicit-credential constructor overload* —
  viable and ADR-010-compliant (same adapter namespace), but rejected in favor of the
  env-var-translation approach because it keeps this feature 100% test-harness-scoped, with
  no diff to any production `src/` file.

## D3 — Supported local proxy: LiteLLM (`scripts/nim/run-litellm-proxy.sh`), not the other NIM scripts

**Decision**: The supported affordable-provider setup is
`scripts/nim/run-litellm-proxy.sh` + `scripts/nim/litellm_config.yaml`, which starts a
LiteLLM proxy on `http://localhost:4000` exposing `model_name: nvidia-model` (backed by
`nvidia_nim/minimaxai/minimax-m3`). This matches the feature's `Input` description and the
spec's `Assumptions` section verbatim ("LiteLLM-style proxy... NVIDIA NIM endpoint as the
concrete default").

**Risk flagged for an early implementation task**: `scripts/nim` also contains two other,
unrelated proxy mechanisms — `claude-nim.sh` (sets `ANTHROPIC_BASE_URL` directly to NVIDIA's
own endpoint, bypassing LiteLLM entirely, for running the Claude Code CLI itself against
NIM) and `run-claude-nim-proxy.sh` (a third, `bun`-based proxy on port 3456). The committed
`.env-example` currently has commented-out example lines referencing port **3456** (the bun
proxy), not port 4000 (the LiteLLM proxy) — a leftover from earlier exploration, predating
the spec's clarified decision. This feature corrects `.env-example` to document the LiteLLM
proxy flow (port 4000, model `nvidia-model`) as part of satisfying FR-002's "documented
configuration" requirement. Whether the Anthropic .NET SDK's request format is accepted
as-is by the LiteLLM proxy's `/v1/messages` route (LiteLLM's built-in Anthropic-format
passthrough) needs a one-time live smoke test — this is called out explicitly as the first
implementation task so a protocol mismatch surfaces immediately rather than as a confusing
eval failure later.

**Alternatives considered**:
- *Document all three scripts as equally supported* — rejected; the spec's already-answered
  Assumptions section designates exactly one supported mechanism, and offering three
  increases support burden for a solo-developer project (Principle II proportionality).

**Risk resolution (T002, executed 2026-07-21)**: Installed `litellm[proxy]` via
`uv tool install 'litellm[proxy]' --with python-dotenv` (the `pip install uv` line in
`run-litellm-proxy.sh` fails on machines without `pip` on `PATH` — `uv` itself is
sufficient and already present; no script change needed since the line is a no-op once
`uv` is installed). Started the proxy (`litellm --config litellm_config.yaml --port 4000`
with `NVIDIA_NIM_API_KEY` set from `data/.env`'s `NVIDIA_API_KEY`) and confirmed
`GET /health/liveliness` returns `200` within ~8s of startup. Sent
`POST http://localhost:4000/v1/messages` with `model: "nvidia-model"` and a minimal
Anthropic-Messages-API-shaped body (`x-api-key`, `anthropic-version: 2023-06-01` headers) —
the proxy returned a well-formed Anthropic Messages API response (`type: "message"`,
`role: "assistant"`, `content: [{"type": "text", ...}]`, `stop_reason: "end_turn"`,
`usage.input_tokens`/`output_tokens`). **No protocol mismatch and no config adjustment
required** — `scripts/nim/litellm_config.yaml` works as committed against the real NVIDIA
NIM backend.

## D4 — Timeout enforcement via a decorator on the port, not inside `AnthropicModelClient`

**Decision**: Add `TimeoutEnforcingModelClient : IModelClient`, a decorator (same pattern as
the existing `RecordingModelClient` in `AgentEvalSupport.cs`) that wraps any `IModelClient`
and races `NextTurnAsync` against a configurable timeout (120s in production eval usage;
injectable in tests for fast execution). On expiry it throws a distinct exception type
(e.g. `ModelCallTimeoutException`) so callers can tell a timeout apart from an
agent-judgment failure, consistent with FR-004's existing distinction requirement.

**Rationale**: FR-013 is an eval-suite-specific reliability requirement, not a production
ingest requirement (the spec's scope is entirely evals). Implementing it as a decorator
keeps `AnthropicModelClient` and the production ingest path completely untouched (FR-011),
reuses the existing ADR-010 port abstraction instead of inventing a new mechanism, and is
trivially unit-testable with a `FakeModelClient` that never completes.

**Alternatives considered**:
- *Set the HTTP client timeout inside `AnthropicModelClient`* — rejected; would change
  production ingest behavior (no such requirement exists there) and reach into the
  production adapter for an eval-only concern.
- *Wrap the whole `AgentLoop.RunAsync` call with one timeout* — rejected; FR-013 bounds a
  *single provider call*, not the whole multi-turn run, and the existing `AgentLoop` already
  has its own turn-count cap (`AgentLoopCapException`) for total-run bounding.

## D5 — CI publish target: repository secret name, workflow trigger shape, PR-comment mechanism

**Decision**:
- Repository secret: `NVIDIA_NIM_API_KEY` — reuses the exact name `litellm_config.yaml`
  already expects the proxy process to read (`api_key: os.environ/NVIDIA_NIM_API_KEY`),
  minimizing new naming surface.
- Workflow trigger: `.github/workflows/eval.yml`, `on: workflow_dispatch` only, with an
  optional `pr_number` input. When provided, the workflow posts a summary comment to that
  PR (via the built-in `GITHUB_TOKEN`, `pull-requests: write` permission, no new secret).
  When omitted, results publish only as workflow artifacts (per the clarified no-PR
  fallback) — matching FR-007 exactly.
- The workflow starts the LiteLLM proxy as a background step, waits for it to accept
  connections, sets `GRIMOIRE_EVAL=1` plus the three `GRIMOIRE_EVAL_PROVIDER_*` variables
  (base URL = `http://localhost:4000`, model = `nvidia-model`, key = the secret), runs
  `dotnet test backend/tests/Grimoire.AgentEvals` with a logger producing a machine-readable
  results file, converts that into the job summary / PR comment body, and always uploads the
  results file plus eval transcripts as workflow artifacts.

**Rationale**: Keeping the workflow entirely separate from `ci.yml` and trigger-gated to
`workflow_dispatch` is what makes SC-005 ("0 new secrets, 0 new jobs in the PR-triggered
pipeline") true by construction rather than by a test that has to check it. GitHub Actions'
automatic secret-value masking in logs (any string equal to a registered secret's value is
redacted from console output automatically) covers FR-008 for the log surface for free once
the secret is referenced via `secrets.NVIDIA_NIM_API_KEY`; it does **not** cover file-based
artifact contents, which is why the sanitization test in `## Test Strategy` (plan.md) is
still required for the transcript/error-message path.

**Alternatives considered**:
- *Trigger via `pull_request` with a label* — rejected; spec's Assumptions explicitly scope
  this feature to manual, on-demand triggering only ("the only new CI trigger in scope").
  scheduled or automatic per-PR runs are explicitly out of scope.
- *`peter-evans/create-or-update-comment` or similar third-party action for the PR comment*
  — deferred to the implementation task; `gh pr comment` (GitHub CLI, preinstalled on
  `ubuntu-latest` runners) achieves the same result with zero new external Action
  dependency, preferred for a solo-developer project's maintenance surface.

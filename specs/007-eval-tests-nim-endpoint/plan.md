# Implementation Plan: Agent Eval Tests on Affordable Model Providers

**Branch**: `007-eval-tests-nim-endpoint` | **Date**: 2026-07-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-eval-tests-nim-endpoint/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Make the agent-behavior eval suite (`Grimoire.AgentEvals`) runnable without an Anthropic
subscription by accepting an affordable-provider configuration (NVIDIA NIM via the
project's existing LiteLLM proxy) as an alternative to `ANTHROPIC_AUTH_TOKEN`, and add an
on-demand GitHub Actions workflow that runs the suite against that provider using a
repository secret. The production model-provider abstraction (`IModelClient` /
`AnthropicModelClient`, ADR-010) already supports a configurable base URL and model via
`GRIMOIRE_INGEST_BASE_URL` / `GRIMOIRE_INGEST_MODEL`; this feature does not introduce a new
external-system port. It adds eval-only gating/precedence logic, a call-timeout decorator,
and CI orchestration — all confined to the test harness and CI configuration.

## Technical Context

**Language/Version**: C# / .NET 10 (existing `backend/Grimoire.slnx`)

**Primary Dependencies**: Anthropic .NET SDK (`Anthropic` NuGet, already referenced by
`Grimoire.IngestAgent`), xUnit (existing eval test host), GitHub Actions (existing CI
platform), the project's LiteLLM proxy scripts (`scripts/nim/run-litellm-proxy.sh`,
`scripts/nim/litellm_config.yaml`)

**Storage**: N/A — reuses the existing eval transcript files under the OS temp directory
and `TaskArtifactStore`; no new persistence.

**Testing**: xUnit, run via `dotnet test backend/tests/Grimoire.AgentEvals` (existing
project, currently excluded from `ci.yml`'s PR pipeline and unaffected by this feature)

**Target Platform**: Linux GitHub Actions runner (new on-demand workflow) and developer
workstations (macOS/Linux/Windows, existing local eval flow)

**Project Type**: Existing hexagonal backend + web frontend repo; this feature touches
only the backend test project (`Grimoire.AgentEvals`), a new CI workflow file, and
developer-facing configuration documentation (`.env-example`).

**Performance Goals**: N/A (test-harness feature; no product-facing latency/throughput
target)

**Constraints**: A single provider call MUST fail after 120s (FR-013); the standard PR
pipeline's runtime and required secrets MUST be unchanged (FR-006, SC-005); the provider
API key MUST never appear in logs/transcripts/artifacts (FR-008, SC-004).

**Scale/Scope**: 6 existing eval test classes; sample count remains configurable,
default 10, clamped 1–20 (`EvalGate.ResolveSampleCount`, unchanged).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal boundaries)**: PASS. No new external-system dependency is
  introduced. `IModelClient` (port, owned by `Grimoire.IngestAgent.AgentCore`) and its
  sole production adapter `AnthropicModelClient`
  (`Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`, ADR-010) already accept a
  configurable base URL and model via environment variables and are reused unchanged.
  The new eval-only resolution/timeout logic lives entirely inside the
  `Grimoire.AgentEvals` test project, which is not a production adapter namespace and is
  not subject to C1–C5 containment (those rules govern production assemblies).
- **Principle II (Pragmatic testing)**: PASS. Provider gating, precedence, conflict
  detection, and timeout enforcement are harness contracts — tested hermetically with
  injected environment variables and a fake/slow model client, no live provider calls
  required. Agent-judgment thresholds (SC-006) remain evaluation-tier and unchanged.
- **Principle III (ADR-driven)**: PASS — see Architectural Constraints below. No new ADR
  required; existing ADR-010 already covers the only external-system port touched.
- **Principle IV (Observability)**: Addressed below in `## Observability`.
- **Principle V (Agentic core / harness split)**: PASS — see Agentic Boundary below; this
  feature has no agentic surface.

No violations to justify; `## Complexity Tracking` is not needed.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-010 | Hexagonal Ports and Adapter Namespaces for External Systems | `IModelClient` is the only port this feature touches; its production adapter `AnthropicModelClient` (`Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`) is reused unmodified in its zero-argument/default-credential path (FR-011). The new `GRIMOIRE_EVAL_PROVIDER_*` variables and the timeout decorator are consumed only inside `Grimoire.AgentEvals` (a test project), so C1–C5 production containment is untouched. No new port or adapter namespace is introduced. |
| ADR-004 | Credential Scoping for the LLM API Key | The affordable-provider credential (`GRIMOIRE_EVAL_PROVIDER_API_KEY`) follows the same least-privilege pattern: it is read only by the eval test host process and, for CI, injected only into that job's environment from the repository secret store — never into the Hub process or any other agent's environment. |
| ADR-005 | Observability Backend (Local and CI) | New log events, metric, and span (see `## Observability`) follow the established pattern: OpenTelemetry .NET SDK, verified in CI via in-memory exporter assertions rather than a live collector. |
| ADR-009 | Explicit Runtime Path Configuration | Not touched — this feature adds no new runtime path; eval transcripts continue to use the existing OS-temp-directory convention (`AgentEvalSupport.AgentEvalRunner`, unchanged). |

**New ADR required?**: No. No new external system, port, or cross-cutting structural
boundary is introduced — this feature reuses ADR-010's existing `IModelClient` port and
its existing configuration surface, confined to harness (test project) and CI
configuration changes.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

No agentic surface — harness-only feature. This feature changes *where* and *under what
gate* the existing agent loop runs (provider selection, CI orchestration); it makes no
change to wiki-content judgment (update-vs-create, categorization, tagging, etc.), which
continues to live entirely in `data/agents/ingest/system-prompt.md` and is exercised
identically regardless of which provider serves the request.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001: 100% of eval tests execute (none skip) once the affordable provider is configured | Deterministic guarantee | Hermetic unit test of the gate/resolver | None — pure env-var injection | Env-var permutation table: neither / Anthropic-only / affordable-only / both / partial-affordable | Covers FR-001, FR-002, FR-003 |
| FR-003 / FR-012: gate skips when neither configured; fails fast when both an Anthropic credential and a *complete* affordable config are present | Deterministic guarantee | Hermetic unit test | None | Same env-var permutation table as above, incl. partial affordable config (1 or 2 of 3 vars set) | "Complete" means all three of endpoint/model/key explicit — partial config must NOT count as present |
| FR-004: unreachable endpoint / rejected credential fails (not skip, not misreported as agent-judgment failure) | Deterministic guarantee | Hermetic integration test against a local loopback listener that refuses/rejects connections | Local `HttpListener` or refused-port double standing in for the affordable endpoint | N/A | Asserts failure reason names connectivity/auth, distinct from an agent-judgment failure path |
| FR-013: a single provider call exceeding 120s fails the sample without hanging the run | Deterministic guarantee | Hermetic unit test of the timeout-enforcing `IModelClient` decorator | `FakeModelClient` whose `NextTurnAsync` never completes; test uses an injected short timeout (not the real 120s) to keep the test fast | N/A | Decorator wraps `IModelClient` (ADR-010 port pattern), so production `AnthropicModelClient` is untouched |
| FR-008 / SC-004: provider API key never appears in output, logs, transcripts, or CI artifacts | Deterministic guarantee | Hermetic unit test of the error-sanitization path + CI workflow review | A rejected-auth double whose exception message embeds the configured fake key | N/A | Mirrors the existing `SanitizeErrorText` pattern in `Program.cs`, extended to the new eval credential; CI log masking is additionally guaranteed by GitHub Actions' automatic secret masking when the workflow references `secrets.NVIDIA_NIM_API_KEY` |
| FR-009 / SC-002: every eval run record names the model that produced it | Deterministic guarantee | Existing hermetic assertion, extended | `RecordingModelClient` (existing) | Existing eval fixtures | `TaskArtifactDocument.Model` is already populated from `IModelClient.ModelId` (`AgentEvalSupport.cs:198`); test asserts this holds when the affordable provider's model is configured |
| SC-005: PR pipeline's runtime/required secrets unchanged (0 new secrets, 0 new jobs in `ci.yml`) | Deterministic guarantee | Structural fact, enforced by construction | N/A | N/A | The new workflow lives in a separate file (`eval.yml`) triggered only by `workflow_dispatch`; `ci.yml` is not modified by this feature |
| FR-005/FR-007: on-demand CI workflow runs against the affordable provider and publishes results (PR comment when associated, artifacts always; no comment when no PR) | Mixed | (a) Deterministic: summary-generation logic as a hermetic unit test; (b) Non-hermetic: one manual `workflow_dispatch` run during quickstart validation | (a) sample eval-results JSON fixture; (b) real NIM endpoint via CI secret | (a) fixture results with mixed pass/fail; (b) N/A | (a) covers FR-007's publish-format branching (PR comment vs. artifacts-only) without a live run; (b) proves the workflow actually completes end-to-end |
| SC-006: agent-judgment thresholds evaluated unchanged on the affordable provider | Agent-judgment threshold | Evaluation run (existing `EvalFact`-gated tests, unmodified thresholds) | Real or recorded LLM output via the configured NIM endpoint | Existing eval fixtures (`backend/tests/Grimoire.AgentEvals/Fixtures/`) | No provider-conditional threshold branch is introduced anywhere in the 6 existing eval classes |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `grimoire.eval.gate_resolutions_total` | Counter | Incremented once per eval-suite invocation when the provider gate resolves (enabled/skipped/error) | `provider=anthropic\|affordable\|none`, `outcome=enabled\|skipped\|configuration_error` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| `eval_provider_resolved` | INFO | The eval gate finishes resolving which provider (if any) will serve the run | `provider`, `outcome`, `model` (null if skipped/error), `reason` (skip/error message, null if enabled) |
| `eval_sample_timeout` | WARN | A single provider call exceeds the 120s bound (FR-013) | `eval_name`, `provider`, `model`, `timeout_seconds` |

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation tasks emitting each event with the exact name and mandatory fields
   above.
2. Deterministic integration tests validating event name, level, and mandatory fields for
   each trigger (env-var permutations for `eval_provider_resolved`; a fake slow model
   client for `eval_sample_timeout`).
3. CI enforcement tasks ensuring these logging tests run in the standard PR pipeline
   (`backend` job in `ci.yml`, which already runs `Grimoire.IntegrationTests` — the new
   tests are added there, not to `Grimoire.AgentEvals`, so they run without requiring the
   provider secret).

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `eval.gate_resolution` | root (no active parent in the eval test host) | `provider`, `outcome`, `model` |

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task creating the span with the declared attributes at gate-resolution
   time.
2. Deterministic integration test validating the span name and attributes via an
   in-memory OTel exporter (ADR-005 pattern), for each provider/outcome permutation.
3. CI enforcement task ensuring this trace test runs in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/007-eval-tests-nim-endpoint/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   └── Grimoire.IngestAgent/
│       └── AgentCore/Adapters/Anthropic/
│           └── AnthropicModelClient.cs        # Unchanged (reused as-is, ADR-010)
└── tests/
    ├── Grimoire.AgentEvals/
    │   ├── AgentEvalSupport.cs                # EvalGate extended; new EvalProviderResolver,
    │   │                                       # EvalGateOutcome, TimeoutEnforcingModelClient
    │   └── EvalProviderResolverTests.cs        # New — hermetic gate/precedence/conflict tests
    └── Grimoire.IntegrationTests/
        └── EvalObservabilityTests.cs           # New — hermetic log/metric/span assertions
                                                  # (runs in the PR pipeline; no provider secret)

.github/
└── workflows/
    ├── ci.yml            # Unchanged
    └── eval.yml          # New — on-demand (workflow_dispatch) affordable-provider eval run

.env-example              # Updated — corrected LiteLLM-proxy-based affordable-provider example
```

**Structure Decision**: Single existing repo structure (backend `Grimoire.slnx` +
frontend), unchanged. This feature adds no new project; it extends the existing
`Grimoire.AgentEvals` test project, adds one CI workflow file, and adds one new test file
to the already-PR-gated `Grimoire.IntegrationTests` project for the observability
contract (so PR CI verifies instrumentation without needing the provider secret,
consistent with FR-006/SC-005).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table intentionally omitted.

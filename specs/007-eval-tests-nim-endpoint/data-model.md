# Phase 1 Data Model: Agent Eval Tests on Affordable Model Providers

This feature adds no persisted storage or database entities. The "data model" here is the
in-memory configuration/outcome shape the eval harness resolves once per test run, plus the
extension to the existing eval run record. All types below live in
`backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` unless noted.

## ProviderKind (enum)

The two selectable providers plus the absence of either.

| Value | Meaning |
|-------|---------|
| `None` | Neither an Anthropic credential nor a complete affordable-provider configuration is present. |
| `Anthropic` | `ANTHROPIC_AUTH_TOKEN` is set (existing path, unchanged). |
| `Affordable` | All three of `GRIMOIRE_EVAL_PROVIDER_BASE_URL`, `GRIMOIRE_EVAL_PROVIDER_MODEL`, `GRIMOIRE_EVAL_PROVIDER_API_KEY` are set. |

## ProviderConfiguration

The resolved, validated configuration for one eval run. Exactly one active configuration
per run (spec Key Entities).

| Field | Type | Notes |
|-------|------|-------|
| `Kind` | `ProviderKind` | Which provider is active. |
| `BaseUrl` | `string?` | Null for `Anthropic` (uses the SDK default) or `None`. |
| `Model` | `string?` | Null for `None`. |
| `HasCredential` | `bool` | Always true when `Kind != None` (credential presence is part of "complete"). |

**Identity/uniqueness rule**: A configuration is "affordable-complete" only when all three
of `BaseUrl`, `Model`, and the credential are non-empty — a partially set affordable
configuration (e.g. only the API key) does **not** count as present for gating purposes
(FR-003) and does **not** participate in the both-configured conflict check (FR-012): it is
simply absent, same as none of the three being set.

## EvalGateOutcome

The result of resolving `ProviderConfiguration` against the two possible failure states.
Replaces the current binary `EvalGate.IsEnabled` boolean with a three-way outcome while
keeping `EvalFactAttribute` behavior-compatible (skip vs. run).

| Field | Type | Notes |
|-------|------|-------|
| `Status` | `Enabled \| Skipped \| ConfigurationError` | `Enabled` → the eval runs against `Configuration`. `Skipped` → `EvalFactAttribute.Skip = Reason` (existing xUnit skip mechanism). `ConfigurationError` → the test run fails loudly rather than skipping (FR-012 — this is a fail-fast configuration error, not a silent skip). |
| `Configuration` | `ProviderConfiguration` | The resolved configuration when `Status == Enabled`; `Kind = None` otherwise. |
| `Reason` | `string?` | Human-readable skip reason (names both supported options, FR-003) or the configuration-conflict message (FR-012, names the conflict). Null when `Enabled`. |

**Resolution rules** (pure function of environment variables, no I/O — hermetically
testable):

1. `anthropicPresent` = `ANTHROPIC_AUTH_TOKEN` non-empty.
2. `affordableComplete` = all three `GRIMOIRE_EVAL_PROVIDER_*` non-empty.
3. `anthropicPresent && affordableComplete` → `ConfigurationError`, naming the conflict.
4. `anthropicPresent` (only) → `Enabled`, `Kind = Anthropic`.
5. `affordableComplete` (only) → `Enabled`, `Kind = Affordable`.
6. Neither → `Skipped`, reason names both options (existing `EvalGate.SkipReason` pattern,
   extended).

## TimeoutEnforcingModelClient (decorator)

Implements the existing `IModelClient` port (`Grimoire.IngestAgent.AgentCore`). Not a new
port — a decorator over any `IModelClient`, per ADR-010's existing pattern (mirrors
`RecordingModelClient`, already in `AgentEvalSupport.cs`).

| Field | Type | Notes |
|-------|------|-------|
| `_inner` | `IModelClient` | The wrapped client (`AnthropicModelClient` in production eval usage). |
| `_timeout` | `TimeSpan` | 120s (FR-013) in eval usage; injectable per-test for fast unit tests. |

`NextTurnAsync` races `_inner.NextTurnAsync` against `_timeout`; on expiry, throws
`ModelCallTimeoutException` (new, minimal exception type carrying the elapsed timeout) so
the eval harness can record it distinctly from an agent-judgment failure or a connectivity
failure, per FR-004's existing distinction requirement.

## Eval run record (extension, not a new entity)

`TaskArtifactDocument` (existing, `backend/src/Grimoire.IngestAgent/TaskArtifact/`) already
has a `Model` field populated from `IModelClient.ModelId` (`AgentEvalSupport.cs:198` /
`Program.cs:240`). No schema change — this feature only guarantees the field is populated
correctly when `ProviderConfiguration.Kind == Affordable` (FR-009/SC-002), verified by an
existing-pattern assertion, not a new field.

## CI eval workflow run (new — GitHub Actions concept, not a C# type)

| Field | Source | Notes |
|-------|--------|-------|
| `pr_number` | `workflow_dispatch` input, optional | Determines PR-comment vs. artifacts-only publishing (FR-007, clarified no-PR fallback). |
| `sample_count` | `workflow_dispatch` input, optional | Forwarded to `GRIMOIRE_EVAL_SAMPLES` (existing `EvalGate.ResolveSampleCount`, unchanged, clamp 1–20). |
| provider secret | `secrets.NVIDIA_NIM_API_KEY` | Mapped to `GRIMOIRE_EVAL_PROVIDER_API_KEY` in the job environment; never logged (D5, research.md). |
| results artifact | Workflow artifact, always uploaded | Eval results file + transcripts (FR-007). |
| PR comment | `gh pr comment`, only when `pr_number` is set | Per-test pass/fail + scores summary (FR-007). |

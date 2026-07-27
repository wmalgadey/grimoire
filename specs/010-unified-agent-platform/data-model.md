# Data Model: Unified Agent Platform & Naming Convention

**Feature**: `010-unified-agent-platform` | **Date**: 2026-07-27

This feature is a pure restructuring: it introduces **no persisted data, no database
schema, no artifact format, and no API payload**. All existing persisted shapes (task
artifacts, query-run artifacts, NDJSON run events, operational-state rows, eval
recordings) are frozen (FR-008) and remain specified by their owning features
(specs 002/004/008/009). The "entities" of this feature are code-level design
concepts. They are modeled here so `/speckit-tasks` has a stable vocabulary.

## Agent Profile

The per-agent declaration that fully distinguishes one agent from another (spec Key
Entities; ADR-013). One instance per host assembly, constructed in that host's
composition root. In-memory only — never serialized.

| Field | Type | Description / validation |
|---|---|---|
| `AgentName` | string | Ubiquitous-language identity: `ingest`, `query` (later `lint`). Non-empty; matches the naming-convention token for the agent. |
| `ServiceName` | string | Frozen OTel resource service name: `Grimoire.IngestAgent` / `Grimoire.QueryAgent`. MUST equal today's value (FR-008). |
| `ActivitySourceName` / `MeterName` | string | Frozen OTel source/meter identities (today: same as `ServiceName`). |
| `RunSpanName` | string | Frozen root span name: `ingest_agent.run` / `query_agent.run`. |
| `CorrelationAttribute` | string | Frozen correlation attribute name: `task_id` / `turn_id`. |
| `ToolRegistry` | `ToolRegistry` | The agent's complete tool set (`IngestToolRegistry`: `list_files`, `read_file`, `write_file`; `QueryToolRegistry`: `list_files`, `read_file`). FR-004: effective capabilities == this declaration; no code path may register tools outside it. |
| `RequiredInstructionDocuments` | set | Which ADR-007 documents the host requires (Ingest: system prompt + default user prompt; Query: system prompt only). Load remains fail-closed. |
| `ModelEnvVarNames` | record | Per-agent model/base-url env-var names for `ModelClientFactory` (ADR-004 scoping preserved; Ingest: defaults, Query: `GRIMOIRE_QUERY_MODEL`/`GRIMOIRE_QUERY_BASE_URL`). |

**Invariants**: all identity fields are frozen constants asserted by the existing
observability/guardrail tests; a profile never contains agent-conditional platform
behavior (that would violate FR-002).

## Agent Platform (`Grimoire.AgentRuntime`)

The single shared implementation of all cross-agent machinery; has no knowledge of
any specific agent's intent. Relationship: each host assembly depends on the
platform; the platform depends on no host (enforced by normal project references +
existing dependency-direction rules).

| Component (namespace) | Responsibility | Status |
|---|---|---|
| `Core` (AgentLoop, IModelClient + Anthropic/Replay adapters) | Model interaction loop, model port | existing, unchanged |
| `Guardrails` (GuardedToolExecutor, ToolRegistry, WriteJournal) | Guarded tool enforcement at invocation time | existing, unchanged |
| `RunEvents` (RunEventEmitter) | NDJSON event emission incl. `answer_chunk` | existing, unchanged |
| `Instructions` (SystemPromptLoader, PolicyLoader) | Fail-closed instruction/policy loading + SHA-256 | existing, unchanged |
| `Telemetry` (AgentTelemetryBootstrap, AgentTracing) | OTel provider bootstrap + tracing scaffold, parameterized by profile identities | **new** (consolidates the duplicated 68+63 / 28+27-line scaffolds) |
| `Composition` (ModelClientFactory, ErrorSanitizer, AgentArgumentReader) | ADR-012 adapter selection (once), credential-safe error text, CLI scaffold | **new** (consolidates duplicated `CreateModelClient`/`SanitizeErrorText`/`ParseArgs`) |
| `Host` (AgentProfile, AgentHost) | Startup/shutdown template: load instructions (fail-closed) → `started` → heartbeat → loop → terminal event; per-intent behavior only via profile + hooks | **new** |

**State transitions** (unchanged semantics, now owned by `AgentHost`): the run event
sequence `started → heartbeat* → activity*/answer_chunk* → completed | failed` is the
ADR-008/ADR-011 contract, byte-identical before/after.

## Intent Handler (per-agent hook set)

The host-side counterpart of the profile: the code that differs because the *intent*
differs, not because the platform was copied.

| Agent | Intent-specific handling (stays in host, unchanged behavior) |
|---|---|
| Ingest | Task-artifact lifecycle (`TaskArtifact/`), ingest log appending (`IngestLog/`), source reading (`Source/`), rollback + all-denied failure handling, user-prompt resolution |
| Query | Stdin conversation input + harness-owned message scaffold (prior turns → conversation), no artifact writes (Hub-written per ADR-011) |

## Naming Convention Document

`docs/conventions/agent-artifact-naming.md` — versioned project documentation (not
domain/operational state, ADR-003 untouched).

| Section | Content | Validation |
|---|---|---|
| Rule | Agent-specific code artifacts carry their agent's name; unprefixed = genuinely cross-agent (≥2 agents or platform/harness) | Mirrored by arch rule N1; Red/Green probe proves the check is live (FR-007) |
| Rationale | Ownership legibility ahead of agent three | — |
| Cross-agent definition + exemption list | Every unprefixed artifact in scope either satisfies the definition or appears here with justification | Exemption list is mirrored in the N1 test fixture; drift between doc and fixture fails the test |
| Old→new rename map | Complete FR-006 mapping (provisional inventory: research.md R5) | Enables mechanical rebase of parallel branches (spec edge case) |

## Structural Rules (new enforcement entities)

| Rule | Assertion | Probe |
|---|---|---|
| N1 | Single-agent-referencing types in shared assemblies carry the agent token; Hub namespace-ownership map holds | Deliberately misnamed type → red → removed |
| D1 | OTel provider-construction APIs in `Grimoire.*Agent` assemblies only via `Grimoire.AgentRuntime.Telemetry` | Private bootstrap re-added in a host → red → removed |
| D2 | Hosts never construct model-client adapters directly; only `ModelClientFactory` | Direct `AnthropicModelClient` construction in a host → red → removed |

No other entities exist for this feature.

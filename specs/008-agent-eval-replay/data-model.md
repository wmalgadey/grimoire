# Data Model: Recorded-Replay Agent Evaluations

**Feature**: 008-agent-eval-replay | **Date**: 2026-07-23

## ScenarioDefinition

One evaluated agent-behavior scenario (harness-owned; consumed by capture, replay, and
status). Defined in code in `Grimoire.EvalRunner/Scenarios`.

| Field | Type | Notes |
|-------|------|-------|
| `ScenarioId` | string | Stable id, e.g. `update-over-duplicate` (SC-006), `convention-adherence` (SC-007), `catalog-discoverability` (SC-008), `instruction-change-adoption` (SC-009), `adversarial-source` (SC-010), `steering-adoption` (004 SC-007) |
| `FixtureName` | string | Wiki fixture directory (existing `Fixtures/` content) |
| `SourceContent` | string | Pasted-text source fed to the agent |
| `UserPrompt` / `SteerPairs` | string? / list | Steering scenarios carry (source, steer) pairs |
| `SystemPromptMutation` | optional | Instruction-change scenario's appended requirement |
| `SampleCount` | int | Default 10, clamp 1–20 (unchanged from 007) |
| `Threshold` | double + kind | e.g. ≥ 0.90 rate; adversarial additionally carries the 100% no-out-of-scope-write guarantee |
| `Scorer` | binding | Deterministic scorer id, or judge-scored (steering) |

**Validation**: `ScenarioId` unique; every scenario names an existing fixture.

## Recording (per sample)

Captured artifact of one genuine agent run (FR-004). File: `sample-NN.json`.

| Field | Type | Notes |
|-------|------|-------|
| `Sample` | int | 1-based index |
| `TaskId` | string | Task id of the captured run (correlates artifact/transcript/spans) |
| `Turns` | RecordedTurn[] | Ordered; extends 007's `RecordedEvalTurn`: turn number, system-prompt SHA-256, conversation message hashes (role + content SHA-256 each), tool-name list, stop reason, tool-use requests (id, name, input JSON), assistant text, token counts |
| `JudgeVerdicts` | JudgeVerdict[]? | Judge-scored scenarios only: judge prompt SHA-256, verdict, rationale |
| `Outcome` | object | Captured run's scored outcome (status, per-check booleans) for cross-checking replay determinism |

**Validation**: turns non-empty; no credential material (scanned at write, FR-011);
hand-edits detectable via manifest content hash over each sample file.

## RecordingManifest (per scenario)

File: `data/evals/recordings/<scenario>/manifest.json`. Establishes provenance
(FR-006) and the staleness basis (FR-008).

| Field | Type | Notes |
|-------|------|-------|
| `ScenarioId` | string | Matches `ScenarioDefinition.ScenarioId` |
| `CapturedAt` | ISO-8601 UTC | Capture timestamp |
| `Model` | string | Model identity that produced the recordings (from `IModelClient.ModelId`) |
| `ProviderKind` | enum | `anthropic` \| `affordable` |
| `Fingerprints` | map | SHA-256 per kind: `system_prompt`, `default_user_prompt`, `policy`, `fixture`, `scenario_definition`, `judge_prompt` (optional) — see research R4 |
| `Samples` | entry[] | Per sample: file name, content SHA-256, task id |

**State transitions**: a scenario's recording set is replaced wholesale by a new capture
run (`absent → current → superseded(deleted)`); no mixed generations.

## TrustStatus (per replayed sample / scenario)

Computed at replay or `status` time; never persisted as authority (always derived).

| Value | Meaning | Surfaced as |
|-------|---------|-------------|
| `trusted` | All fingerprints match; replay completed; sample content hash matches manifest | Passing replay result |
| `stale` | ≥ 1 fingerprint differs from current workspace inputs | Failing replay test naming changed fingerprint kinds + refresh command (FR-008, gate for FR-016) |
| `missing` | No recording / manifest for the scenario or sample | Failing with capture instruction (FR-009) |
| `mismatch` | Conversation diverged from recording at turn *k* (R2), or sample hash ≠ manifest hash (tamper/hand-edit) | Failing as infrastructure error, never a judgment score (FR-010, FR-004) |

## ReplayResult (per sample)

| Field | Type | Notes |
|-------|------|-------|
| `ScenarioId`, `Sample`, `TaskId` | — | Correlation (task_id shared with spans/logs) |
| `TrustStatus` | enum | Above |
| `Provenance` | object | Model, `CapturedAt`, recording path (FR-012, SC-003) |
| `Score` | object? | Scorer outcome; only meaningful when `trusted` |

Aggregated per scenario against `Threshold` → suite outcome consumed by the replay
tests and the CLI summary (reusing `scripts/ci/format-eval-summary`).

## ProviderConfiguration / EvalGateOutcome (moved, unchanged)

007's resolver entities relocate from `Grimoire.AgentEvals` to
`Grimoire.EvalRunner` with identical semantics (env-var contract
`specs/007-eval-tests-nim-endpoint/contracts/eval-provider-env-vars.md` stays valid);
used only by `capture`. Replay uses no provider configuration by design.

## Relationships

```text
ScenarioDefinition 1 ── 1 RecordingManifest ── * Recording (sample-NN.json)
Recording 1 ── 1 ReplayResult (per replay execution)
ReplayResult * ── 1 scenario aggregate → threshold verdict
Fingerprints: manifest ←→ current workspace inputs → TrustStatus
```

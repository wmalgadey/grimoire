# Implementation Plan: Recorded-Replay Agent Evaluations

**Branch**: `009-agent-eval-replay` (git: `claude/speckit-agent-eval-alternatives-yhik4p`) | **Date**: 2026-07-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-agent-eval-replay/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Replace the always-live agent-behavior eval suite with a two-tier setup:

1. **`Grimoire.EvalRunner`** — a new standalone console command (the "remote control")
   that owns all live evaluation: it prepares an isolated per-run workspace, spawns the
   **real `Grimoire.IngestAgent` executable** through its production CLI entry point
   (ADR-002 contract), runs the eval scenarios against a real provider (007's provider
   resolution reused), scores them, and **captures every model interaction as a
   versioned recording** with provenance metadata (model, timestamp, instruction/scenario
   fingerprints).
2. **Replay tier** — the `Grimoire.AgentEvals` test project loses all `[EvalFact]`-gated
   live tests (no more skipped tests in the standard suite) and instead gains
   always-running, hermetic **replay eval tests**: they spawn the same real
   `Grimoire.IngestAgent` executable with a replay model-client adapter that serves the
   versioned recordings, then apply the unchanged scoring and thresholds. Staleness
   (instruction/scenario fingerprint drift) fails the replay test in the standard PR
   pipeline — which *is* the FR-016 merge gate for instruction changes.

Record/replay is implemented at the existing `IModelClient` port (ADR-010): a
`ReplayModelClient` adapter and a turn-capture decorator, both selected exclusively in
the `Grimoire.IngestAgent` composition root via environment configuration. A new ADR-011
fixes this structural boundary (new process, new adapter namespace, recording store,
containment rules).

## Technical Context

**Language/Version**: C# / .NET 10 (existing `backend/Grimoire.slnx`)

**Primary Dependencies**: xUnit (replay test host), Anthropic .NET SDK (unchanged, only
inside `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`), GitHub Actions (existing CI),
LiteLLM/NIM proxy scripts (`scripts/nim/`, reused for live capture runs). No new external
package is introduced.

**Storage**: Recordings as JSON files versioned in the repository under
`data/evals/recordings/<scenario>/` (manifest + per-sample turn files) — local-filesystem
persistence, port-exempt per Principle I but subject to adapter containment. Transcripts
and isolated workspaces stay under the OS temp directory.

**Testing**: xUnit via `dotnet test`. Replay eval tests + existing hermetic harness tests
run in the standard PR pipeline (`ci.yml` backend job). Live runs happen only via
`dotnet run --project backend/src/Grimoire.EvalRunner` (locally or via `eval.yml`).

**Target Platform**: Developer workstations (macOS/Linux/Windows) and Linux GitHub
Actions runners.

**Project Type**: Existing hexagonal backend + web frontend repo; this feature adds one
backend console project (`Grimoire.EvalRunner`), one adapter namespace in
`Grimoire.IngestAgent`, restructures `Grimoire.AgentEvals`, and reworks `eval.yml`.

**Performance Goals**: Full replay of the eval suite completes in < 5 minutes on a
standard developer machine (SC-002); replay adds no network calls (SC-001).

**Constraints**: Replay MUST be deterministic (SC-004) and credential-free (FR-002);
recordings MUST NOT contain credential material (FR-011); standard test suite MUST report
zero skipped eval tests (SC-008); live capture reuses 007's provider gate, timeout bound
(120s per call, enforced at process level by the runner), and secret-hygiene rules.

**Scale/Scope**: 6 eval scenario families (SC-006–SC-010 + steering), default 10 samples
each (clamped 1–20) → ≈ 60 recorded samples, each a multi-turn agent run plus, for the
steering scenario, recorded LLM-judge verdicts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Hexagonal boundaries)**: PASS with new ADR. Record/replay is consumed
  through the existing `IModelClient` port; the new `ReplayModelClient` production-grade
  adapter and `TurnCaptureModelClient` decorator live in the new
  `Grimoire.IngestAgent.AgentCore.Adapters.Replay` namespace, constructed only by the
  `Grimoire.IngestAgent` composition root (`Program.cs`) from environment configuration.
  `Grimoire.EvalRunner` consumes the agent exclusively as a spawned process (ADR-002
  pattern) and MUST NOT reference the Anthropic SDK or concrete adapter types — enforced
  by new arch rules (ADR-011, C6/C7). The recording store is local-filesystem persistence:
  port-exempt, containment-bound.
- **Principle II (Pragmatic testing)**: PASS — this feature is the direct implementation
  of Principle II's "real **or recorded** LLM output" clause. Replay tests are hermetic
  and deterministic yet still verify genuine agent behavior (recordings are captured
  model output, FR-004); agent-judgment thresholds stay evaluation-tier and unchanged
  (FR-014). Harness contracts (recording I/O, fingerprints, staleness, replay mismatch,
  CLI gating) are tested hermetically. The replay tests spawn the real agent executable —
  integration-first, no mocked repositories.
- **Principle III (ADR-driven)**: PASS — all ADRs in `docs/adr/` were read; constraints
  listed below. A new structural boundary is introduced → **ADR-011 drafted** as part of
  this plan (`docs/adr/ADR-011-eval-runner-recorded-replay.md`); it MUST reach Accepted
  before `/speckit-tasks`. Structural rules get Red/Green probes (Phase 0 of tasks).
- **Principle IV (Observability)**: Addressed in `## Observability`; every row derives
  implementation, deterministic test, and CI-enforcement tasks.
- **Principle V (Agentic core / harness split)**: PASS — harness-only feature. Scoring
  remains verification of agent output against spec thresholds, not a reimplementation of
  agent judgment: recordings are genuine model output, and the steering scenario's
  judgment scoring stays an LLM-judge verdict (captured live, replayed verbatim — never
  rewritten as deterministic string matching). Instruction files remain the only place
  wiki behavior is defined.

No violations to justify; `## Complexity Tracking` is not needed.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-002 | Ingest Agent Execution Model | The eval command MUST drive the agent the same way the Hub does: spawn the standalone `Grimoire.IngestAgent` executable per sample with CLI arguments + scoped environment, file-based results (task artifact, wiki tree, exit code). This satisfies FR-015 ("real production entry points") without new IPC. |
| ADR-004 | Credential Scoping for the LLM API Key | Live capture runs receive the provider credential only in the spawned agent's scoped environment (and the runner's judge client); replay runs receive no credential at all. Recordings and runner output MUST stay credential-free (FR-011), extending 007's sanitization pattern. |
| ADR-005 | Observability Backend (Local and CI) | New metrics/log events/spans (below) use the OpenTelemetry .NET SDK and are verified via in-memory exporter assertions in the PR pipeline — no live collector. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | Untouched: record/replay wraps only the model-client port; the guarded tool executor and policy enforcement run identically live and replayed (which is what makes replayed runs faithful). |
| ADR-007 | Agent Instruction Surface | The instruction fingerprint set is exactly ADR-007's surface: `system-prompt.md`, `default-user-prompt.md`, plus `policy.json` (ADR-006) and the scenario definition/fixture. These SHA-256 hashes are the staleness basis (FR-006/FR-008). |
| ADR-008 | Agent Event Channel, Run Supervision | The runner consumes the agent's exit code and task artifact; stdout NDJSON events remain available but are not a new contract surface. No change. |
| ADR-009 | Explicit Runtime Path Configuration | Recordings live under the consolidated `data/` directory (`data/evals/recordings/`); all agent paths are passed explicitly per run into the isolated workspace, per ADR-009's explicit-path rule. |
| ADR-010 | Hexagonal Ports and Adapter Namespaces | Record/replay MUST attach at the `IModelClient` port; new adapter namespace `Grimoire.IngestAgent.AgentCore.Adapters.Replay`; construction only in the composition root. `Grimoire.EvalRunner` MUST NOT reference the Anthropic SDK or any concrete `.Adapters.` type. ADR-010's table is extended by ADR-011 ("new boundaries via ADR"). |
| **ADR-011 (new, drafted)** | Standalone Eval Runner and Recorded-Replay at the Model Port | Fixes: `Grimoire.EvalRunner` as a new standalone process; `Adapters.Replay` namespace; recording store location/format; env-based composition-root selection (`GRIMOIRE_MODEL_CAPTURE_PATH` / `GRIMOIRE_MODEL_REPLAY_PATH`); containment rules C6 (replay adapter isolation) and C7 (EvalRunner references no LLM SDK / concrete adapters); replay-tests-as-merge-gate. |

**New ADR required?**: **Yes — drafted**: `docs/adr/ADR-011-eval-runner-recorded-replay.md`
(status: proposed). It MUST be moved to Accepted (review or explicit author sign-off)
before `/speckit-tasks` is invoked.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|----------------|
| Wiki-content judgment being evaluated (update-vs-create, conventions, discoverability, injection resistance, steering) | Agentic core | `data/agents/ingest/system-prompt.md` + `default-user-prompt.md` (unchanged by this feature) |
| Steering-adoption judgment scoring | Agentic core (LLM judge) | Judge prompt invoked by `Grimoire.EvalRunner` during capture; verdicts recorded and replayed verbatim — never reimplemented deterministically |
| Scenario definitions, thresholds, deterministic scorers (frontmatter/index/denial checks over produced files) | Harness | `Grimoire.EvalRunner/Scenarios`, `Grimoire.EvalRunner/Scoring` |
| Workspace isolation, agent process invocation, provider gating, timeout | Harness | `Grimoire.EvalRunner/Workspace`, reusing 007 resolver semantics |
| Turn capture + replay at the model port | Harness | `Grimoire.IngestAgent.AgentCore.Adapters.Replay`, wired in `Program.cs` (composition root) |
| Recording store, provenance fingerprints, staleness detection | Harness | `Grimoire.EvalRunner/Recording` + replay tests in `Grimoire.AgentEvals` |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001: 100% of replay runs make zero provider calls with zero provider config | Deterministic guarantee | Hermetic integration test: replay run with all provider env vars unset + arch rule (no LLM SDK reference in replay path) | Replay adapter serving committed recordings | Versioned recordings under `data/evals/recordings/` | Replay spawns the real agent executable; absence of credentials proves no live call is possible |
| SC-002: full replay suite < 5 min | Deterministic guarantee | CI timing assertion on the replay test job + quickstart measurement | None | Full recording set | Soft-gate: budget documented in quickstart; CI job timeout set accordingly |
| SC-003: 100% of replay results name recording provenance + trust status | Deterministic guarantee | Hermetic integration test over replay result records | None | Sample recording with known manifest | Asserts model, capture timestamp, fingerprints, trust status present |
| SC-004: repeated replays produce identical outcomes | Deterministic guarantee | Hermetic test running the same replay twice and diffing scored results | None | Any committed scenario recording | Also guarded by design: replay adapter is a pure function of the recording |
| SC-005: 100% of affected recordings flagged stale; 0 stale trusted passes | Deterministic guarantee | Hermetic tests: mutate an instruction file copy → replay reports stale (test fails with staleness message, not judgment failure) | None | Recording + deliberately drifted instruction fixture | This same mechanism running in `ci.yml` implements FR-016/SC-009 |
| SC-006: routine verification incurs zero provider spend | Deterministic guarantee | Structural fact: `ci.yml` has no provider secret; live path exists only in `Grimoire.EvalRunner` + `eval.yml` | N/A | N/A | Verified by workflow review task + arch rule C7 |
| SC-007: agent-judgment thresholds unchanged | Agent-judgment threshold | Evaluation: capture runs via the eval command score against the unchanged thresholds; replay re-applies the same scorers | Real provider (NIM/Anthropic) during capture only | Existing eval fixtures + new recordings | Thresholds live once, in scenario definitions consumed by both tiers |
| SC-008: zero skipped eval tests in the standard suite | Deterministic guarantee | Test-suite fact asserted in CI: `dotnet test` summary contains 0 skipped in the backend job | None | N/A | `[EvalFact]`/`GRIMOIRE_EVAL` gating is deleted; replay tests run unconditionally |
| SC-009: 100% of merged instruction changes carry fresh eval evidence | Deterministic guarantee | Same staleness tests as SC-005 running as required checks in the PR pipeline | None | N/A | A PR touching instructions without refreshed recordings goes red; refresh procedure in quickstart |
| FR-009: missing/unusable recording → actionable, distinct outcome | Deterministic guarantee | Hermetic test with deleted/corrupted recording | None | Truncated + absent recording fixtures | Message names the capture command |
| FR-010: harness/recording divergence → replay-mismatch outcome | Deterministic guarantee | Hermetic test replaying a recording whose captured conversation diverges from what the agent sends | None | Recording with mutated conversation history | Distinct from stale and from judgment failure |
| FR-011: no credential material in recordings/output | Deterministic guarantee | Hermetic test: capture with a fake key in env → scan recording + runner output | Fake provider double | N/A | Extends 007 sanitization tests |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `grimoire.eval.recordings_captured_total` | Counter | Incremented per captured sample recording during a live capture run | `scenario`, `provider=anthropic\|affordable` |
| `grimoire.eval.replay_results_total` | Counter | Incremented per replayed sample with its trust outcome | `scenario`, `trust_status=trusted\|stale\|missing\|mismatch` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `eval_recording_captured` | INFO | A sample recording is written by the eval command | `task_id`, `scenario`, `sample`, `model`, `recording_path` |
| `eval_replay_result` | INFO | A replayed sample finishes scoring | `task_id`, `scenario`, `sample`, `trust_status`, `model`, `captured_at` |
| `eval_recording_stale` | WARN | Fingerprint drift detected for a recording at replay/staleness check | `scenario`, `changed_fingerprints`, `recording_path` |

**Derivation rule (MANDATORY)**: Every row in **Structured Log Events** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory
   fields (in `Grimoire.IntegrationTests`, PR pipeline, no provider secret).
3. CI task(s) ensuring those logging tests run in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `eval.capture_run` | root (eval command) | `task_id`, `scenario`, `provider`, `model` |
| `eval.replay_run` | root (replay test host) | `task_id`, `scenario`, `trust_status`, `recording_id` |

**Derivation rule (MANDATORY)**: Every row in **Distributed Trace Spans** MUST map to
concrete work in `tasks.md` covering all three categories:

1. Implementation task(s) creating each span with declared parentage and attributes,
   with logs/metrics emitted inside the active span context and correlatable via
   `task_id`.
2. Deterministic integration test task(s) validating span name, linkage, and correlation
   attributes via in-memory exporter (ADR-005).
3. CI task(s) ensuring those trace tests run in the standard PR pipeline.

The existing `eval.gate_resolution` span and `eval_provider_resolved` /
`eval_sample_timeout` events (007) move with the provider resolver into
`Grimoire.EvalRunner` unchanged in name and fields; their existing tests in
`Grimoire.IntegrationTests` are re-pointed, not redefined.

## Project Structure

### Documentation (this feature)

```text
specs/009-agent-eval-replay/
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
│   ├── Grimoire.EvalRunner/                    # NEW — standalone eval command ("remote control")
│   │   ├── Program.cs                          # CLI: capture | replay | status (contracts/eval-cli.md)
│   │   ├── Scenarios/                          # Scenario definitions: fixture ref, source content,
│   │   │                                       #   sample count, thresholds, scorer binding
│   │   ├── Scoring/                            # Deterministic scorers (moved from AgentEvals asserts)
│   │   │                                       #   + LLM-judge invocation (capture) / verdict replay
│   │   ├── Recording/                          # Recording store, manifest, fingerprint computation,
│   │   │                                       #   staleness check (filesystem persistence, port-exempt)
│   │   └── Workspace/                          # Isolated per-run workspace + IngestAgent process
│   │                                           #   invocation (ADR-002 contract), provider env scoping
│   └── Grimoire.IngestAgent/
│       ├── Program.cs                          # Composition root: selects Anthropic vs Replay adapter,
│       │                                       #   optional TurnCapture decorator, from env (ADR-011)
│       └── AgentCore/Adapters/Replay/          # NEW — ReplayModelClient, TurnCaptureModelClient,
│                                               #   recording turn (de)serialization
├── tests/
│   ├── Grimoire.AgentEvals/                    # REPURPOSED — always-running replay eval tests
│   │   │                                       #   (spawn real agent w/ replay adapter; no [EvalFact],
│   │   │                                       #   no GRIMOIRE_EVAL gate, zero skips) + existing
│   │   │                                       #   hermetic harness tests that remain (mismatch,
│   │   │                                       #   staleness, provenance, determinism)
│   │   └── (EvalFact/live-gating code deleted; provider resolver moves to EvalRunner)
│   ├── Grimoire.ArchTests/                     # + C6/C7 rules and Red/Green probes (ADR-011)
│   └── Grimoire.IntegrationTests/              # EvalObservabilityTests re-pointed + new event/span tests
data/
└── evals/
    └── recordings/
        └── <scenario>/
            ├── manifest.json                   # Provenance + fingerprints + sample index
            └── sample-NN.json                  # Captured turns (+ judge verdicts where applicable)
.github/workflows/
├── ci.yml                                      # Backend job now also runs replay eval tests (hermetic);
│                                               #   asserts zero skipped tests
└── eval.yml                                    # REWORKED — workflow_dispatch invokes the eval command
                                                #   (capture/live), uploads recordings + summary
scripts/ci/format-eval-summary                  # Reused for the command's summary output
.env-example                                    # + GRIMOIRE_MODEL_CAPTURE_PATH / _REPLAY_PATH docs
```

**Structure Decision**: One new console project (`Grimoire.EvalRunner`) in the existing
solution — justified because the eval command is a distinct process with its own
lifecycle (FR-013/FR-015) and must be referenceable by the replay test project for shared
scenario/scoring/recording code without a test project leaking into production. No other
new assemblies; record/replay lands as a namespace inside `Grimoire.IngestAgent` per
ADR-010's no-extra-assemblies stance.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table intentionally omitted.

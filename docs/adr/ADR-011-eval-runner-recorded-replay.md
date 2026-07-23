---
status: accepted
---

# ADR-011: Standalone Eval Runner and Recorded-Replay at the Model Port

## Context and Problem Statement

Feature 009 replaces the always-live agent-behavior eval suite with a recorded-replay
tier (Constitution Principle II explicitly permits evaluation "against real or recorded
LLM output"). This requires three structural elements no existing ADR covers: a
standalone eval command that drives the real agent from outside in isolated workspaces
(replacing the test harness's in-process rebuild of the agent loop), record/replay
capability at the model boundary, and a versioned recording store whose fingerprints
gate instruction-file merges. ADR-010 requires new boundaries to be fixed via ADR before
implementation.

## Decision Drivers

- Constitution Principle I: external systems via ports; adapters in designated
  namespaces; composition root constructs adapters; enforcement via structural tests
  with Red/Green probes (Principle III).
- Principle II: replay tests must be hermetic and deterministic while still verifying
  genuine agent behavior; harness contracts tested without live LLM calls.
- Spec 009 FR-015: evaluation must execute the real application through its production
  entry points — no duplicated orchestration wiring.
- Spec 009 FR-016 / SC-008: no skipped eval tests in the standard suite; staleness must
  block instruction-change merges.
- ADR-002: agents are standalone executables spawned per task with a file-based
  contract — the natural outside-in seam.

## Considered Options

1. **Standalone `Grimoire.EvalRunner` console app + record/replay adapters at the
   `IModelClient` port, selected by the `Grimoire.IngestAgent` composition root via
   environment configuration; recordings versioned under `data/evals/recordings/`.**
2. Keep evals inside the xUnit test project with an in-process harness and add replay
   there (status quo mechanics + snapshots).
3. HTTP-level record/replay proxy between the agent and the provider endpoint.
4. Separate eval assembly per concern (runner, recorder, scenarios) with
   compile-time-enforced boundaries.

## Decision Outcome

Chosen option: **Option 1.**

- **New process**: `backend/src/Grimoire.EvalRunner` — subcommands `capture`, `replay`,
  `status` (contract: `specs/009-agent-eval-replay/contracts/eval-cli.md`). It drives
  `Grimoire.IngestAgent` exclusively as a spawned child process per sample (ADR-002
  contract) in an isolated per-run workspace. 007's provider resolution, timeout, and
  sanitization move here; all `[EvalFact]`/`GRIMOIRE_EVAL` gating is deleted from the
  test suite.
- **Port extension (ADR-010 table)**: two new implementations of the existing
  `IModelClient` port, both in the new adapter namespace
  `Grimoire.IngestAgent.AgentCore.Adapters.Replay`:
  - `ReplayModelClient` — serves recorded turns, verifies request fingerprints per turn,
    fails fast on divergence (`replay_mismatch`).
  - `TurnCaptureModelClient` — decorator over the live adapter, writes the turn stream.
  Selection happens only in the `Grimoire.IngestAgent` composition root (`Program.cs`)
  from `GRIMOIRE_MODEL_REPLAY_PATH` / `GRIMOIRE_MODEL_CAPTURE_PATH`; both set is a
  fail-fast configuration error; neither preserves production behavior unchanged.
- **Recording store**: JSON manifest + per-sample files versioned under
  `data/evals/recordings/<scenario>/` (format:
  `specs/009-agent-eval-replay/contracts/recording-format.md`). Local-filesystem
  persistence — port-exempt per Principle I, containment-bound. Manifest fingerprints
  (instruction surface per ADR-007, policy per ADR-006, fixture, scenario definition,
  judge prompt) are the staleness authority; replay tests failing on staleness in the
  standard PR pipeline are the merge gate for instruction changes.

### Containment rules (extend ADR-010; enforced in `Grimoire.ArchTests`, each with a Red/Green probe)

- **C6**: The `Anthropic` SDK remains confined to
  `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic` (C2 unchanged); the `Replay`
  adapter namespace references no LLM SDK package, and no namespace outside `.Adapters.`
  references `ReplayModelClient`/`TurnCaptureModelClient` (C5 extension to the new
  concrete types).
- **C7**: `Grimoire.EvalRunner` references no LLM SDK package and no concrete
  `.Adapters.` type from any assembly; its only paths to the model are the spawned
  agent process and, for judge calls during capture, the `IModelClient` port with the
  adapter constructed in its own composition root.
- **C8**: process spawning in `Grimoire.EvalRunner` is confined to its
  `Workspace` namespace (mirror of ADR-010 C4 for the new process).

### Consequences

- Good: live and replayed runs execute byte-identical orchestration (loop, guardrails,
  prompts, artifacts) — replay verifies the shipped agent, not a copy; the harness
  duplication in the old eval support code is deleted.
- Good: the standard suite has zero skipped tests; PR CI gets free, deterministic
  agent-behavior regression coverage plus a self-enforcing instruction-change gate.
- Good: no new external system, no new package; one new console project and one adapter
  namespace, matching ADR-010's no-extra-assemblies default (Option 4 rejected as BDUF).
- Bad: recordings are repo-versioned binary-ish assets (~60 JSON files) that must be
  refreshed when fingerprinted inputs change; accepted — the refresh cost is the
  explicit, visible replacement for silent per-run provider spend.
- Bad: strict per-turn matching makes replay sensitive to intentional harness prompt
  changes (they surface as `mismatch`/`stale` requiring re-capture); accepted — that
  sensitivity is exactly the drift signal the feature exists to provide.
- Neutral: Option 3 (wire-level proxy) remains possible later for black-box evaluation
  of a deployed system; explicitly out of scope per spec clarification.

## Verification

- `Grimoire.ArchTests` gains C6–C8 with Red/Green probes (Phase 0 of feature 009
  tasks); ADR-010's C1–C5 remain in force and their tests keep passing.
- Hermetic contract tests cover replay matching, staleness, provenance, determinism,
  credential hygiene; the standard PR pipeline runs them plus the replay eval tests.
- This ADR MUST be Accepted before `/speckit-tasks` for feature 009 (Constitution
  Principle III).

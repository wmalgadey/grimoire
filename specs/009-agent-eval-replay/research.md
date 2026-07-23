# Research: Recorded-Replay Agent Evaluations

**Feature**: 009-agent-eval-replay | **Date**: 2026-07-23

All Technical Context unknowns are resolved below. Each entry: Decision / Rationale /
Alternatives considered.

## R1. Where record/replay attaches

**Decision**: At the existing `IModelClient` port, inside `Grimoire.IngestAgent`:
a `ReplayModelClient` adapter (serves recorded turns) and a `TurnCaptureModelClient`
decorator (wraps the Anthropic adapter, writes turns to a capture file). Both live in
`Grimoire.IngestAgent.AgentCore.Adapters.Replay` and are selected exclusively by the
composition root (`Program.cs`) from environment configuration:
`GRIMOIRE_MODEL_REPLAY_PATH` (replay) / `GRIMOIRE_MODEL_CAPTURE_PATH` (capture);
both set → configuration error, fail fast (mirrors 007's FR-012 precedent).

**Rationale**: FR-015 demands the real application through production entry points —
so capture and replay must happen *inside* the agent process, not in a test harness
rebuild. The port already exists (ADR-010); a decorator/adapter at that seam changes no
orchestration code, keeps the guarded tool loop, prompt loading, artifact writing, and
policy enforcement bit-identical between live and replayed runs, and is exactly the
"production adapter and test fake implement the same port" pattern the constitution
mandates. The prior eval harness's in-process rebuild (`AgentEvalRunner` duplicating
`Program.cs` wiring) is retired — that duplication was the root of "we test a copy".

**Alternatives considered**:
- *HTTP-level recording proxy between agent and provider*: records the wire protocol
  instead of the port contract — brittle against SDK/protocol changes, adds a runtime
  dependency, and hides the seam from the architecture tests.
- *Keep the in-process `AgentEvalRunner`*: rejected by clarification Q2 (real app,
  isolated workspace) and by the duplication smell it already caused.

## R2. Replay matching semantics (basis for FR-010 mismatch detection)

**Decision**: Sequential replay with fingerprint guard. A recording stores the ordered
turns of one sample. `ReplayModelClient` serves turn *k*'s recorded response for the
*k*-th call and verifies, per call, that the requested conversation matches the recorded
one via a canonical hash (system-prompt hash + per-message role/content hashes + tool
name list). First divergence → the adapter fails the run with a `replay_mismatch` error
naming the turn and the differing component; the replay test surfaces it as
infrastructure failure, never as a judgment score.

**Rationale**: Strict matching is what makes a green replay meaningful ("the harness
still conducts the same conversation"); hashing avoids storing/verifying huge payload
diffs; failing on first divergence keeps the FR-010 outcome sharp and actionable.

**Alternatives considered**: fuzzy/positional-only matching (silently masks harness
drift — the exact failure mode FR-010 exists to expose); full-payload equality diffs
(same power as hashes but bloats failure output; hash + turn index is enough to locate
drift, and the recorded turn content is available for inspection).

## R3. Recording store: location, format, granularity

**Decision**: JSON files versioned in the repository under
`data/evals/recordings/<scenario>/`:
- `manifest.json` — scenario id, capture timestamp, model identity, provider kind,
  fingerprints (see R4), sample index with per-sample status.
- `sample-NN.json` — ordered captured turns (extending 007's `RecordedEvalTurn` shape:
  conversation hashes, stop reason, tool uses, assistant text, token counts) plus, for
  judge-scored scenarios, the recorded judge verdicts.

Superseding capture runs replace a scenario's directory wholesale (spec edge case:
no mixed generations within a scenario).

**Rationale**: FR-007 requires versioned, reviewable recordings; `data/` is the
consolidated data directory (ADR-009) and already hosts the instruction files the
fingerprints refer to. JSON keeps diffs reviewable in PRs. Directory-per-scenario with
wholesale replacement keeps growth bounded and staleness atomic per scenario.

**Alternatives considered**: OS temp / CI artifacts only (violates FR-007 — contributors
and CI must replay identical state); one monolithic recordings file (unreviewable diffs,
cross-scenario staleness coupling); storing under `backend/tests/` (recordings are
consumed by both the EvalRunner CLI and tests; they are data, not test code).

## R4. Staleness fingerprint set

**Decision**: SHA-256 over, per scenario:
1. the agent instruction surface (ADR-007): `system-prompt.md`, `default-user-prompt.md`,
2. the guardrail policy `policy.json` (ADR-006),
3. the scenario fixture tree (wiki fixture files),
4. the scenario definition (source content, steer/user prompt, sample count, thresholds,
   scorer identity),
5. the judge prompt template (judge-scored scenarios only).

Stored in `manifest.json`; recomputed at replay/staleness check; any mismatch → every
recording of that scenario is stale (FR-008), reported with the changed fingerprint kind.

**Rationale**: These are exactly the inputs whose change invalidates what a recording
proves. Model identity is provenance, not a staleness input — switching the *configured*
model doesn't invalidate old recordings retroactively; it warrants a re-capture decision,
which the manifest's recorded model makes visible.

**Alternatives considered**: hashing the whole `data/agents/` tree (over-invalidates on
unrelated agent additions); git-commit-based staleness (breaks for uncommitted local
edits, the primary developer loop).

## R5. Execution vehicle & suite hygiene

**Decision**: New console project `backend/src/Grimoire.EvalRunner` with subcommands
`capture` (live run + record + score), `replay` (replay + score, same code path the test
tier uses), and `status` (staleness report). All `[EvalFact]`/`GRIMOIRE_EVAL` gating and
the `AgentEvalRunner` in-process harness are deleted from `Grimoire.AgentEvals`; the
test project keeps only always-running hermetic tests: per-scenario replay eval tests
plus harness-contract tests (mismatch, staleness, provenance, determinism, credential
hygiene). 007's `EvalProviderResolver`, timeout enforcement (now process-level, 120s per
model call → per-sample budget), and sanitization move into `Grimoire.EvalRunner`.

**Rationale**: Clarification Q1 (hybrid) and SC-008 (zero skips). A `src/` console
project lets the replay tests reference scenario/scoring/recording code without a
production→test reference, and matches ADR-002's one-process-per-lifecycle pattern.

**Alternatives considered**: keeping evals as filtered xUnit categories (still shows
skip/filter machinery, no operational "remote control", rejected in Q1); a dotnet-tool
package (needless packaging overhead for an in-repo tool).

## R6. LLM-judge handling under replay

**Decision**: During `capture`, the runner invokes the judge (via the same provider
configuration) and records each verdict (prompt hash, verdict, rationale) into the
sample recording. During `replay`, scorers consume the recorded verdicts; the judge is
never re-invoked. The judge prompt template joins the fingerprint set (R4), so editing
it stales the affected scenario.

**Rationale**: Keeps replay fully offline while honoring Principle V — the judgment
stays an LLM verdict, only its execution moment moves to capture time. Re-running a
judge at replay would reintroduce cost and non-determinism.

**Alternatives considered**: deterministic re-scoring of steering adoption (explicitly
forbidden — reimplements agent judgment); live judge at replay (breaks SC-001/SC-004).

## R7. FR-016 merge gate mechanics

**Decision**: The staleness check runs as ordinary hermetic tests inside the standard PR
pipeline (`ci.yml` backend job, which gains the `Grimoire.AgentEvals` replay tests). A PR
that changes any fingerprinted input without committing refreshed recordings turns those
tests red with an actionable message naming the affected scenarios and the
`Grimoire.EvalRunner capture` invocation that refreshes them. No new CI job, no new
secret, no bespoke bot: the merge gate is the test suite.

**Rationale**: SC-009 needs a blocking, always-on gate; the replay tests already fail on
staleness (FR-008), so reusing them keeps the gate hermetic and free. `eval.yml` (reworked
to invoke the eval command) remains the on-demand path to produce fresh recordings in CI
when a local capture isn't possible — its 007 contract (PR comment + artifacts,
secret-store key, dispatch-only) carries over.

**Alternatives considered**: a separate "instruction PR" workflow with path filters
(second mechanism to maintain, drifts from the fingerprint truth); GitHub branch
protection rules alone (not expressible per-fingerprint, not testable in-repo).

## R8. Suite runtime budget (SC-002)

**Decision**: Replay spawns one agent process per recorded sample (~60), each completing
in single-digit seconds without network. Budget: < 5 minutes wall clock; CI job timeout
10 minutes as guard. If process-spawn overhead threatens the budget, samples within a
scenario may run concurrently (workspaces are already isolated per sample) — noted as an
optimization lever for tasks, not a required design element.

**Rationale**: Keeps SC-002 verifiable without premature parallelization.

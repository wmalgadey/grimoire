---

description: "Task list for feature implementation"
---

# Tasks: Recorded-Replay Agent Evaluations

**Input**: Design documents from `/specs/008-agent-eval-replay/`

**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Every user story includes the deterministic tests that verify its behavior hermetically (no live LLM calls, Constitution Principle II). The only non-hermetic task is the one-time initial recording capture (T031), which is the explicit, paid bootstrap this feature exists to make rare.

**Logging Contract (MANDATORY)**: `eval_recording_captured` → impl T029, test T038, CI T041; `eval_replay_result` → impl T022, test T038, CI T041; `eval_recording_stale` → impl T034, test T038, CI T041.

**Trace Contract (MANDATORY)**: `eval.capture_run` → impl T029, test T039, CI T041; `eval.replay_run` → impl T022, test T039, CI T041.

**Organization**: Tasks are grouped by user story (US1 replay, US2 capture, US3 staleness; priorities from spec.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Every task includes exact file path(s)

## Path Conventions

Existing hexagonal backend repo (`backend/Grimoire.slnx`). This feature touches:
- `backend/src/Grimoire.EvalRunner/` — NEW standalone eval command
- `backend/src/Grimoire.IngestAgent/` — composition root + new `AgentCore/Adapters/Replay/` namespace
- `backend/tests/Grimoire.ArchTests/` — ADR-011 rules C6–C8
- `backend/tests/Grimoire.AgentEvals/` — repurposed: always-running replay tests (live gating deleted)
- `backend/tests/Grimoire.IntegrationTests/` — adapter contract + observability tests (PR pipeline)
- `data/evals/recordings/` — NEW versioned recording store
- `.github/workflows/ci.yml`, `.github/workflows/eval.yml`, `.env-example`

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove, before any feature code exists, that the ADR-011 boundaries are guarded: the replay adapter namespace stays SDK-free and port-shaped, and the eval runner can never bypass the spawned-process/port seam.

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [ ] T001 Write and verify ADR-011 structural rules in `backend/tests/Grimoire.ArchTests/EvalRunnerReplayBoundaryTests.cs`. Precondition inside this task (no feature code): create the empty console-project shell `backend/src/Grimoire.EvalRunner/Grimoire.EvalRunner.csproj` + minimal `Program.cs` stub and add it to `backend/Grimoire.slnx`, so the rules have an assembly to load. Rules:
  - **C6**: no namespace in `Grimoire.IngestAgent.AgentCore.Adapters.Replay` references the `Anthropic` SDK namespace, and no namespace outside an `.Adapters.` segment references the concrete types `ReplayModelClient`/`TurnCaptureModelClient` (extends ADR-010 C2/C5).
  - **C7**: the `Grimoire.EvalRunner` assembly references no LLM SDK namespace and no concrete `.Adapters.` type from any assembly.
  - **C8**: `System.Diagnostics.Process` usage in `Grimoire.EvalRunner` is confined to the `Grimoire.EvalRunner.Workspace` namespace (mirror of ADR-010 C4).

  **Red/Green probe** (required, per rule):
  1. Write C6–C8.
  2. Probe C6 with a temporary `Anthropic` reference in a stub file under `AgentCore/Adapters/Replay/`; probe C7 with a temporary `new Anthropic.AnthropicClient()` line in the EvalRunner stub; probe C8 with a temporary `Process.Start` call in `Program.cs` outside `Workspace/`.
  3. Run `dotnet test backend/tests/Grimoire.ArchTests` after each probe — each MUST fail naming the rule.
  4. Delete the probe lines; run again — MUST pass (C6/C7/C8 vacuously green on the empty scaffold).
  5. Document probe results in the commit message.

**Checkpoint**: ADR-011 boundaries are guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project wiring and documented configuration surface for the new env contract.

- [ ] T002 Finish `backend/src/Grimoire.EvalRunner/Grimoire.EvalRunner.csproj` project setup: net10.0 console, reference `Grimoire.IngestAgent` (for the `IModelClient` port + replay schema types only — C7 keeps concrete adapters out), OpenTelemetry packages per `Directory.Packages.props`, nullable enabled per `backend/Directory.Build.props`; verify `dotnet build backend/Grimoire.slnx` passes with the arch rules from T001
- [ ] T003 [P] Document the new environment contract in `.env-example`: commented `GRIMOIRE_MODEL_REPLAY_PATH` / `GRIMOIRE_MODEL_CAPTURE_PATH` entries with a warning that both-set is a fail-fast configuration error and that production leaves both unset (contracts/recording-format.md)

**Checkpoint**: Solution builds with the guarded scaffold; user-facing configuration is documented.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The record/replay seam inside the agent, the recording store, and the shared runner primitives every story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 [P] Implement the recording schema types (`RecordedSample`, `RecordedTurn`, `JudgeVerdict`, JSON (de)serialization with `schema_version`) in `backend/src/Grimoire.IngestAgent/AgentCore/Adapters/Replay/RecordingSchema.cs` per `contracts/recording-format.md` (responses verbatim; conversation requests as SHA-256 hashes)
- [ ] T005 Implement `TurnCaptureModelClient : IModelClient` (decorator: forwards to inner client, appends each turn — request hashes + verbatim response — to the file named by its capture path) in `backend/src/Grimoire.IngestAgent/AgentCore/Adapters/Replay/TurnCaptureModelClient.cs` (depends on T004)
- [ ] T006 Implement `ReplayModelClient : IModelClient` (serves turn *k*'s recorded response for call *k*; verifies system-prompt hash, per-message conversation hashes, and tool-name list per call; first divergence throws `ReplayMismatchException` naming turn + differing component; exhausted recording likewise) in `backend/src/Grimoire.IngestAgent/AgentCore/Adapters/Replay/ReplayModelClient.cs` per research.md R2 (depends on T004)
- [ ] T007 Wire composition-root selection in `backend/src/Grimoire.IngestAgent/Program.cs`: `GRIMOIRE_MODEL_REPLAY_PATH` → construct `ReplayModelClient` (no credential read); `GRIMOIRE_MODEL_CAPTURE_PATH` → wrap `AnthropicModelClient` in `TurnCaptureModelClient`; both set → exit non-zero with named conflict before any provider call; neither → unchanged production path (depends on T005, T006)
- [ ] T008 Hermetic adapter contract tests in `backend/tests/Grimoire.IntegrationTests/ReplayAdapterTests.cs`: replay serves recorded turns in order; mismatch on mutated conversation (turn index + component named); exhausted-recording failure; capture writes a schema-valid turn stream; spawned `Grimoire.IngestAgent` with both env vars set exits non-zero with the conflict message and with `GRIMOIRE_MODEL_REPLAY_PATH` set completes a run with all provider env vars unset (depends on T007)
- [ ] T009 [P] Move 007's provider primitives (`ProviderKind`, `ProviderConfiguration`, `EvalGateOutcome`, `EvalProviderResolver`, `SanitizeErrorText`) from `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` into `backend/src/Grimoire.EvalRunner/Providers/EvalProviderResolver.cs` unchanged in env-var semantics (007 contract stays valid); re-point `backend/tests/Grimoire.IntegrationTests/EvalObservabilityTests.cs` and `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs` imports (tests move content later in T020)
- [ ] T010 [P] Implement the recording store in `backend/src/Grimoire.EvalRunner/Recording/RecordingStore.cs`: read/write `manifest.json` + `sample-NN.json` under a recordings root (default `data/evals/recordings/`), per-sample content SHA-256 in the manifest, atomic wholesale scenario replacement (temp dir + swap) per `contracts/recording-format.md`
- [ ] T011 [P] Implement fingerprint computation in `backend/src/Grimoire.EvalRunner/Recording/Fingerprints.cs`: SHA-256 over `system-prompt.md`, `default-user-prompt.md`, `policy.json`, canonical fixture-tree hash, scenario-definition serialization, judge prompt (research.md R4); returns a named map matching the manifest schema
- [ ] T012 Implement workspace + agent invocation in `backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs` and `Workspace/AgentProcessInvoker.cs`: per-sample isolated workspace under OS temp (fixture copy + `data/agents/ingest` copy + optional system-prompt mutation), spawn the real `Grimoire.IngestAgent` executable with the ADR-002 CLI arguments and a scoped environment (replay/capture path, provider credentials only for capture), per-sample process timeout budget (120 s × expected turns), collect exit code + task artifact + produced files (depends on T002)
- [ ] T013 [P] Port the six scenario definitions into `backend/src/Grimoire.EvalRunner/Scenarios/ScenarioDefinitions.cs` per `data-model.md#ScenarioDefinition`: `update-over-duplicate`, `convention-adherence`, `catalog-discoverability`, `instruction-change-adoption` (incl. the system-prompt mutation), `adversarial-source` (incl. the 100% no-out-of-scope-write guarantee), `steering-adoption` (steer pairs, judge-scored) — source contents, fixtures, sample counts, and thresholds copied unchanged from the existing eval classes in `backend/tests/Grimoire.AgentEvals/`
- [ ] T014 [P] Move the deterministic scorers into `backend/src/Grimoire.EvalRunner/Scoring/DeterministicScorers.cs`: existing-page-update check, page-count bound, frontmatter conventions, index discoverability, out-of-scope-write detection, `reviewed: false` adoption — extracted verbatim from the assertion logic of the six eval classes (fixtures stay in `backend/tests/Grimoire.AgentEvals/Fixtures/`, path passed in)
- [ ] T015 Implement CLI shell in `backend/src/Grimoire.EvalRunner/Program.cs`: subcommand parsing for `capture|replay|status` with the options and exit-code contract from `contracts/eval-cli.md` (0 pass / 1 threshold failure / 2 configuration-or-connectivity / 3 stale-missing-mismatch), summary output compatible with `scripts/ci/format-eval-summary` (depends on T009–T014)

**Checkpoint**: Foundation ready — the agent can record and replay through its production entry point, and the runner has store, fingerprints, workspaces, scenarios, and scorers. User story implementation can now begin.

---

## Phase 3: User Story 1 — Verify agent behavior via replay at zero provider cost (Priority: P1) 🎯 MVP

**Goal**: `replay` runs every scenario from versioned recordings — real agent process, zero provider calls, zero credentials, deterministic, < 5 min — and the standard test suite runs replay evals with zero skips.

**Independent Test**: With all provider env vars unset, `dotnet run --project backend/src/Grimoire.EvalRunner -- replay` executes all scenarios from committed recordings and scores them; `dotnet test backend/tests/Grimoire.AgentEvals` reports 0 skipped (quickstart §1). Until T031 commits genuine recordings, verification uses the synthetic contract fixtures from T024 (documented dependency, see Dependencies).

### Implementation for User Story 1

- [ ] T016 [US1] Implement the `replay` subcommand pipeline in `backend/src/Grimoire.EvalRunner/Replay/ReplayPipeline.cs`: per sample — trust check (manifest sample hash), workspace, spawn agent with `GRIMOIRE_MODEL_REPLAY_PATH`, score via `Scoring/`, aggregate per-scenario against thresholds; requires no provider configuration by construction (depends on Phase 2)
- [ ] T017 [P] [US1] Implement `ReplayResult` + trust-status derivation (`trusted|stale|missing|mismatch`) with provenance (model, captured-at, recording path) on every result, and the FR-009 missing-recording message naming the exact `capture` invocation, in `backend/src/Grimoire.EvalRunner/Replay/ReplayResult.cs` per `data-model.md` (staleness inputs arrive in US3; until T032, trust check covers hash/missing/mismatch)
- [ ] T018 [US1] Delete the live-eval machinery from `backend/tests/Grimoire.AgentEvals/`: remove `EvalFactAttribute`, `GRIMOIRE_EVAL` gating, `AgentEvalRunner`, `RecordingModelClient`, `TimeoutEnforcingModelClient` (+ its tests, absorbed by runner-side coverage), and the six `*Evals.cs` classes whose scenarios/scorers now live in `Grimoire.EvalRunner` (T013/T014); keep `Fixtures/`, keep `EvalProviderResolverTests.cs`/`EvalProviderTransparencyTests.cs`/`EvalConnectivityTests.cs` content pending T020/T030 (depends on T013, T014, T016)
- [ ] T019 [US1] Add always-running per-scenario replay eval tests in `backend/tests/Grimoire.AgentEvals/ReplayEvalTests.cs`: one `[Fact]` per scenario invoking the shared replay pipeline against `data/evals/recordings/`, asserting trusted status and threshold satisfaction; failure messages distinguish threshold vs. trust failures (depends on T016, T017; goes green with T031's committed recordings)
- [ ] T020 [US1] Repurpose the remaining hermetic harness tests in `backend/tests/Grimoire.AgentEvals/`: keep resolver permutation tests (now against `Grimoire.EvalRunner.Providers`), fold provider-transparency assertions into replay provenance tests, retire the live-connectivity test into a `capture`-path unit test of the runner's connectivity error mapping (exit 2, FR from 007 preserved) (depends on T009, T018)
- [ ] T021 [P] [US1] Hermetic replay contract tests in `backend/tests/Grimoire.AgentEvals/ReplayContractTests.cs` using synthetic recordings (fixture recordings built by test setup — harness mechanics, not agent behavior): determinism (same recording twice → identical results, SC-004), zero-config execution (all provider vars unset, SC-001), missing recording (FR-009), tampered sample hash → `mismatch` (FR-010), provenance fields present (SC-003) (depends on T016, T017)
- [ ] T022 [US1] Emit replay observability in `backend/src/Grimoire.EvalRunner/EvalRunnerTelemetry.cs` + `Replay/ReplayPipeline.cs`: `eval.replay_run` root span (`task_id`, `scenario`, `trust_status`, `recording_id`), `eval_replay_result` INFO event (`task_id`, `scenario`, `sample`, `trust_status`, `model`, `captured_at`) inside the span context, `grimoire.eval.replay_results_total{scenario,trust_status}` counter (plan.md ## Observability; deterministic tests in T038/T039)
- [ ] T023 [US1] Add `Grimoire.AgentEvals` to the standard PR pipeline in `.github/workflows/ci.yml` (Deterministic Backend Gates job): run `dotnet test backend/tests/Grimoire.AgentEvals` with no provider secret, assert the test summary reports **0 skipped** (SC-008), set a 10-minute job timeout for the replay budget (SC-002 guard) (depends on T019, T021)
- [ ] T024 [P] [US1] Commit the synthetic contract-test recording fixtures under `backend/tests/Grimoire.AgentEvals/Fixtures/replay-contract/` (clearly named as harness-mechanics fixtures, never read by `ReplayEvalTests`) so T021 runs in CI before genuine recordings exist (depends on T021)

**Checkpoint**: Replay tier fully functional and CI-gated; per-scenario tests await genuine recordings (T031).

---

## Phase 4: User Story 2 — Capture and refresh recordings from genuine live runs (Priority: P2)

**Goal**: `capture` runs live evaluation through the real agent, scores it, and (re)writes provenance-carrying recordings; `eval.yml` offers the on-demand CI path.

**Independent Test**: With the NIM proxy configured (007 setup), `capture --scenario update-over-duplicate --samples 3` rewrites `data/evals/recordings/update-over-duplicate/` with manifest provenance and threshold-scored summary; no credential material anywhere in the store (quickstart §2).

### Implementation for User Story 2

- [ ] T025 [US2] Implement the `capture` subcommand pipeline in `backend/src/Grimoire.EvalRunner/Capture/CapturePipeline.cs`: provider gate via `EvalProviderResolver` (neither/both → exit 2 with 007's messages), per sample — workspace, spawn agent with `GRIMOIRE_MODEL_CAPTURE_PATH` + scoped provider env (ADR-004: credential only in the child process env), collect turn stream, score live outcome, assemble `RecordedSample`; on scenario completion write manifest (model from task artifact `Model` field, capture timestamp, fingerprints via T011) and swap wholesale; partial scenarios are not committed (depends on Phase 2)
- [ ] T026 [US2] Implement judge capture for `steering-adoption` in `backend/src/Grimoire.EvalRunner/Scoring/JudgeScoring.cs`: build the judge client via the `IModelClient` port constructed in the EvalRunner composition root (C7: no concrete adapter reference — construction via the same env-shim pattern the resolved configuration defines), record per-sample `JudgeVerdict` (judge prompt SHA-256, verdict, rationale) into the recording; replay path consumes recorded verdicts without invoking any client (depends on T025, T016)
- [ ] T027 [US2] Enforce capture-time credential hygiene (FR-011): extend `SanitizeErrorText` usage to all runner output paths and add a write-time scan in `backend/src/Grimoire.EvalRunner/Recording/RecordingStore.cs` rejecting any recording containing the configured credential value; hermetic test with a fake key in `backend/tests/Grimoire.AgentEvals/CaptureHygieneTests.cs` (depends on T025)
- [ ] T028 [US2] Per-call timeout enforcement for capture: bound each model call at 120 s via the per-sample process budget in `Workspace/AgentProcessInvoker.cs` and map a timeout kill to the 007-compatible timeout failure (distinct from judgment failure), emitting the existing `eval_sample_timeout` WARN event fields from `Grimoire.EvalRunner` (depends on T012, T025)
- [ ] T029 [US2] Emit capture observability in `backend/src/Grimoire.EvalRunner/EvalRunnerTelemetry.cs` + `Capture/CapturePipeline.cs`: `eval.capture_run` root span (`task_id`, `scenario`, `provider`, `model`), `eval_recording_captured` INFO event (`task_id`, `scenario`, `sample`, `model`, `recording_path`) in span context, `grimoire.eval.recordings_captured_total{scenario,provider}` counter; keep 007's `eval_provider_resolved` event + `eval.gate_resolution` span emitting from the moved resolver (depends on T025, T009)
- [ ] T030 [US2] Rework `.github/workflows/eval.yml`: replace the `dotnet test` eval invocation with `dotnet run --project backend/src/Grimoire.EvalRunner -- capture --summary …`, keep `workflow_dispatch`-only trigger, repository-secret provider key, PR comment via `scripts/ci/format-eval-summary` when a PR is associated, and upload the refreshed `data/evals/recordings/` + summary as artifacts ready to commit (007 FR-005/FR-007 contract preserved) (depends on T025, T029)
- [ ] T031 [US2] Bootstrap capture (non-hermetic, one-time): run `capture` for all six scenarios against the configured affordable provider, review the summary against thresholds, and commit the genuine recording set to `data/evals/recordings/` — this turns T019's per-scenario replay tests green in CI (depends on T025–T028; unblocks full US1 verification)

**Checkpoint**: Live capture works end-to-end locally and in CI; genuine recordings are versioned; replay tier is now fully green.

---

## Phase 5: User Story 3 — Trust replay results through staleness detection (Priority: P3)

**Goal**: Fingerprint drift flags affected recordings as stale everywhere (replay, tests, `status`), never counting as trusted passes — making PR CI the FR-016 merge gate.

**Independent Test**: Append a line to `data/agents/ingest/system-prompt.md` → `status` exits 3 naming `system_prompt` drift + refresh command; `dotnet test backend/tests/Grimoire.AgentEvals` fails with the staleness message; revert → green (quickstart §3).

### Implementation for User Story 3

- [ ] T032 [US3] Implement staleness evaluation in `backend/src/Grimoire.EvalRunner/Recording/StalenessCheck.cs`: recompute fingerprints (T011) against the current workspace inputs, diff against each scenario manifest, produce per-scenario `TrustStatus.Stale` with the changed fingerprint kinds; wire into `ReplayPipeline` trust derivation (T017) so stale samples never score as trusted (FR-008) (depends on T011, T017)
- [ ] T033 [US3] Implement the `status` subcommand in `backend/src/Grimoire.EvalRunner/Program.cs` + `Recording/StalenessCheck.cs`: per-scenario provenance + trust report, no workspaces or agent spawns, exit 0 all-current / exit 3 any stale-or-missing with the exact `capture` refresh invocation per affected scenario (contracts/eval-cli.md) (depends on T032)
- [ ] T034 [US3] Emit `eval_recording_stale` WARN event (`scenario`, `changed_fingerprints`, `recording_path`) from `StalenessCheck` within the active span context, and count stale outcomes via `grimoire.eval.replay_results_total{trust_status="stale"}` (plan.md ## Observability) (depends on T032)
- [ ] T035 [US3] Hermetic staleness tests in `backend/tests/Grimoire.AgentEvals/StalenessTests.cs`: instruction-file drift → affected scenarios stale with `system_prompt` named (SC-005); scenario-definition drift → only that scenario stale (partial staleness, spec edge case); stale result is a failing test whose message names the refresh command and is distinct from judgment/threshold failure (FR-016 gate mechanics); judge-prompt drift stales only judge-scored scenarios (depends on T032, T033, T019)

**Checkpoint**: All three stories functional — staleness gates instruction changes in the standard PR pipeline.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Constitution-mandated final-phase verification (observability, evaluation thresholds) and DoD closure.

- [ ] T036 [P] Observability implementation completeness pass: confirm every signal from `plan.md ## Observability` is emitted within span context with `task_id` correlation across `Capture/CapturePipeline.cs`, `Replay/ReplayPipeline.cs`, `Recording/StalenessCheck.cs`, `EvalRunnerTelemetry.cs` (MANDATORY — Constitution Principle IV)
- [ ] T037 Migrate & extend `backend/tests/Grimoire.IntegrationTests/EvalObservabilityTests.cs`: keep 007's `eval_provider_resolved`/`eval_sample_timeout`/`eval.gate_resolution` assertions against the moved resolver (T009), verifying names, levels, and fields unchanged (MANDATORY — Constitution Principle IV)
- [ ] T038 Deterministic logging-contract tests in `backend/tests/Grimoire.IntegrationTests/EvalRunnerObservabilityTests.cs`: in-memory exporter assertions for `eval_recording_captured` (INFO: task_id, scenario, sample, model, recording_path — captured via a fake-provider run), `eval_replay_result` (INFO: task_id, scenario, sample, trust_status, model, captured_at), `eval_recording_stale` (WARN: scenario, changed_fingerprints, recording_path) — event name, level, every mandatory field per trigger (MANDATORY — Constitution Principle IV)
- [ ] T039 Deterministic trace-contract tests in `backend/tests/Grimoire.IntegrationTests/EvalRunnerObservabilityTests.cs`: `eval.capture_run` (root; task_id, scenario, provider, model) and `eval.replay_run` (root; task_id, scenario, trust_status, recording_id) span names, root parentage, and task_id correlation with the log events and metric increments (`grimoire.eval.recordings_captured_total`, `grimoire.eval.replay_results_total`) (MANDATORY — Constitution Principle IV)
- [ ] T040 Agent-behavior evaluation verification: confirm every agent-judgment success criterion holds at its spec-defined threshold on the recorded evidence — T031's capture summary (live scores) and the green per-scenario replay tests (recorded scores) for SC-006/007/008/009/010 + steering (SC-007/004) — and record the results in `specs/008-agent-eval-replay/tasks.md` completion notes (MANDATORY — Constitution Principles II & V)
- [ ] T041 CI enforcement: verify `.github/workflows/ci.yml` (Deterministic Backend Gates) runs `Grimoire.IntegrationTests` (T037–T039 logging + trace tests) and `Grimoire.AgentEvals` (replay + staleness tests) in the standard PR pipeline with no provider secret and 0 skipped tests (MANDATORY — Constitution Principle IV; completes the logging/trace contract CI category)
- [ ] T042 [P] Run full `quickstart.md` validation (§1–§5 incl. SC-002 < 5 min timing measurement and the observability spot-check); fix any deviation or update quickstart with corrected commands
- [ ] T043 [P] Documentation closure: update `specs/007-eval-tests-nim-endpoint/`-referencing docs where behavior moved (`.env-example` cross-check, `scripts/ci/format-eval-summary` usage note for the runner summary), and verify `docs/adr/ADR-011-eval-runner-recorded-replay.md` Verification section matches the implemented rule names/files

**Checkpoint**: Definition of Done satisfiable — all ADRs Accepted with live structural tests, observability contracts implemented+tested+CI-enforced, evaluation thresholds verified on recorded evidence, zero skipped tests.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (T001)**: First, always — guards ADR-011 before feature code.
- **Phase 1 (T002–T003)**: After T001.
- **Phase 2 (T004–T015)**: After Phase 1 — blocks all stories.
- **US1 (T016–T024)**: After Phase 2.
- **US2 (T025–T031)**: After Phase 2; independent of US1 code except T026 reuses the replay pipeline's scoring path (T016).
- **US3 (T032–T035)**: After Phase 2; T032 hooks into T017's trust derivation, T035 exercises T019/T033.
- **Polish (T036–T043)**: After all stories.

### Cross-story reality (documented deviation)

T019's per-scenario replay tests can only go green once T031 (US2) commits genuine
recordings — FR-004 forbids synthetic stand-ins for scenario recordings. US1 therefore
verifies its machinery via synthetic **contract** fixtures (T021/T024, harness mechanics
only) and reaches full green at the T031 checkpoint. This is the one intentional
US1→US2 dependency; it mirrors the spec's own edge case ("first trusted result requires
one live recording run").

### Parallel Opportunities

- Phase 2: T004 ∥ T009 ∥ T010 ∥ T011 ∥ T013 ∥ T014 (different files); T005/T006 after T004; T012 after T002; T015 last.
- US1: T017 ∥ T021 ∥ T024 after T016; T022 alongside T018–T020.
- US2: T026–T029 largely sequential on T025; T030 ∥ T031 after T025–T029.
- Polish: T036 ∥ T042 ∥ T043; T037–T039 sequential in the same test file; T040/T041 last.

## Implementation Strategy

**MVP = US1 + T031 bootstrap**: Phase 0→2, then US1 (replay machinery + CI wiring +
contract tests), then jump to T025+T031 (minimal capture path + one genuine recording
set) to light up the per-scenario replay tests — that already delivers the headline
value: free, deterministic agent-behavior verification on every PR. Full US2 (judge,
eval.yml, hygiene) and US3 (staleness gate) follow incrementally; each checkpoint leaves
CI green and previous stories intact.

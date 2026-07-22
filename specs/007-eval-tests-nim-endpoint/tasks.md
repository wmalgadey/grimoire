---

description: "Task list for feature implementation"
---

# Tasks: Agent Eval Tests on Affordable Model Providers

**Input**: Design documents from `/specs/007-eval-tests-nim-endpoint/`

**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Every user story below includes the deterministic tests needed to verify its independent behavior hermetically (no live LLM calls, per Constitution Principle II), plus the one non-hermetic validation run that actually proves the story end-to-end against the real affordable provider.

**Logging Contract (MANDATORY)**: covered by T027–T028 (`eval_provider_resolved`) and T029–T030 (`eval_sample_timeout`), CI-enforced by T035.

**Trace Contract (MANDATORY)**: covered by T033–T034 (`eval.gate_resolution`), CI-enforced by T035.

**Organization**: Tasks are grouped by user story (US1/US2/US3, priorities from spec.md) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Every task includes exact file path(s)

## Path Conventions

This is the existing hexagonal backend+frontend repo (`backend/Grimoire.slnx`). This
feature touches only:
- `backend/tests/Grimoire.ArchTests/` — one new structural rule
- `backend/tests/Grimoire.AgentEvals/` — gate/resolver/decorator + its own tests
- `backend/tests/Grimoire.IntegrationTests/` — observability tests (PR-pipeline-gated,
  no provider secret needed)
- `.github/workflows/eval.yml` — new, on-demand only
- `scripts/ci/` — new eval-results summary formatting script + fixture test
- `.env-example` — corrected affordable-provider example

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Prove, before any feature code exists, that the eval harness cannot silently
bypass the ADR-010 `IModelClient` port by reaching for the Anthropic SDK directly — the
architectural premise this whole feature relies on (no new port needed, per research.md
D1/D2).

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [X] T001 Write and verify a new NetArchTest rule in `backend/tests/Grimoire.ArchTests/HexagonalPortsAdapterRuleTests.cs` (or a new adjacent file) asserting the `Grimoire.AgentEvals` test assembly has **no** dependency on the `Anthropic` SDK namespace (extends ADR-010's C2 containment, currently scoped only to `Grimoire.IngestAgent`, to the eval harness assembly that this feature's design depends on staying port-only)

  **Red/Green probe** (required):
  1. Write the rule.
  2. Temporarily add a deliberately violating line to `AgentEvalSupport.cs` (e.g. `var _ = new Anthropic.AnthropicClient();`).
  3. Run `dotnet test backend/tests/Grimoire.ArchTests` — it MUST fail, naming the violation.
  4. Delete the violating line.
  5. Run the test again — it MUST pass.
  6. Document the probe result in the commit message.

**Checkpoint**: The eval-harness/port boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: De-risk the one external assumption every user story depends on, and correct
the misleading existing documentation before adding new documented configuration on top of
it.

- [X] T002 Smoke-test `scripts/nim/run-litellm-proxy.sh` + `scripts/nim/litellm_config.yaml`: start the proxy, point the Anthropic .NET SDK (or a `curl POST http://localhost:4000/v1/messages`) at it with `model: "nvidia-model"`, and confirm it returns a valid Anthropic-Messages-API-shaped response. Record the outcome (and any required config adjustment) as an update to `specs/007-eval-tests-nim-endpoint/research.md` (D3 risk resolution)
- [X] T003 [P] Update `.env-example`: remove the stale `claude-nim`/port-3456 commented block (superseded, per research.md D3) and add a corrected commented example for `GRIMOIRE_EVAL_PROVIDER_BASE_URL=http://localhost:4000`, `GRIMOIRE_EVAL_PROVIDER_MODEL=nvidia-model`, `GRIMOIRE_EVAL_PROVIDER_API_KEY="$NVIDIA_API_KEY"` matching the LiteLLM proxy flow

**Checkpoint**: The affordable-provider proxy is confirmed reachable and documented; user story implementation can now begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The provider-resolution, timeout-enforcement, and gate-wiring primitives every
user story depends on (US1's local run, US2's CI run, and US3's transparency guarantee all
exercise the same resolved configuration and the same model client).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 [P] Add `ProviderKind` enum, `ProviderConfiguration` record, and `EvalGateOutcome` record to `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` per `data-model.md`
- [X] T005 Implement `EvalProviderResolver.Resolve()` as a pure function of environment variables → `EvalGateOutcome`, per the resolution table in `contracts/eval-provider-env-vars.md`, in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` (depends on T004)
- [X] T006 [P] Implement `TimeoutEnforcingModelClient : IModelClient` (decorator, injectable timeout, default 120s) and `ModelCallTimeoutException` in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs`
- [X] T007 Replace `EvalGate.IsEnabled`'s boolean check with `EvalProviderResolver`-backed resolution in `EvalFactAttribute`: `Enabled` → run, `Skipped` → `Skip = Reason` (existing xUnit mechanism), `ConfigurationError` → throw so the run fails loudly rather than skipping (FR-012), in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` (depends on T005)
- [X] T008 Wire the resolved `ProviderConfiguration` and `TimeoutEnforcingModelClient` into `AgentEvalRunner.RunAsync`: when `Kind == Affordable`, set `GRIMOIRE_INGEST_BASE_URL`/`GRIMOIRE_INGEST_MODEL`/`ANTHROPIC_AUTH_TOKEN` from the resolved config immediately before constructing `AnthropicModelClient`, then wrap it in `TimeoutEnforcingModelClient` before handing it to `RecordingModelClient`/`AgentLoop`, in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` (depends on T005, T006, T007)
- [X] T009 [P] Extend the credential-redaction path in `AgentEvalRunner`'s failure handling to also scrub the configured `GRIMOIRE_EVAL_PROVIDER_API_KEY` value from `FailureReason`/`Narrative` (mirrors `SanitizeErrorText` in `backend/src/Grimoire.IngestAgent/Program.cs:353`), in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs` (depends on T008)

**Checkpoint**: Foundation ready — gate resolution, timeout enforcement, and provider wiring are all functional. User story implementation can now begin.

---

## Phase 3: User Story 1 - Run agent evals locally without an Anthropic subscription (Priority: P1) 🎯 MVP

**Goal**: A developer with no Anthropic credential configures the affordable-provider
endpoint and key and runs the full eval suite locally; all evals execute and score against
existing thresholds.

**Independent Test**: Unset all Anthropic credentials, configure only the affordable
provider (endpoint + model + key), run `dotnet test backend/tests/Grimoire.AgentEvals`, and
observe every eval test executes (none skip) and produces a scored result.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before the Phase 2 implementation is complete**

- [X] T010 [P] [US1] Test: neither Anthropic nor affordable configured → `Skipped`, reason names both options (FR-003) in `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs`
- [X] T011 [P] [US1] Test: affordable-only, all 3 vars set → `Enabled`, `Kind = Affordable` in `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs`
- [X] T012 [P] [US1] Test: partial affordable config (1 or 2 of the 3 vars set) → `Skipped`, not counted as present (data-model.md identity rule) in `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs`
- [X] T013 [P] [US1] Test: `ANTHROPIC_AUTH_TOKEN` set AND all 3 affordable vars set → `ConfigurationError` naming the conflict, not a silent pick (FR-012) in `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs`
- [X] T014 [P] [US1] Test: affordable endpoint unreachable (refused local port) → eval sample fails with an actionable connectivity error — not a skip, not misreported as an agent-judgment failure (FR-004) in `backend/tests/Grimoire.AgentEvals/EvalConnectivityTests.cs`
- [X] T015 [P] [US1] Test: `TimeoutEnforcingModelClient` wrapping a `FakeModelClient` whose `NextTurnAsync` never completes, with a short injected timeout, throws `ModelCallTimeoutException` (FR-013) in `backend/tests/Grimoire.AgentEvals/TimeoutEnforcingModelClientTests.cs`
- [X] T016 [P] [US1] Test: a rejected-auth double whose exception message embeds the configured affordable-provider key produces a `FailureReason`/`Narrative` with the key redacted (FR-008 harness half) in `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs`

### Implementation for User Story 1

- [X] T017 [US1] Run `quickstart.md` Scenarios 1–3 against the local LiteLLM proxy started in T002; fix any gap the manual run surfaces (depends on T002, T008–T016)

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - Run agent evals on demand in CI (Priority: P2)

**Goal**: A maintainer manually triggers the eval suite in GitHub Actions against the
affordable provider, using a repository secret, with no Anthropic configuration anywhere in
CI, and gets readable published results.

**Independent Test**: Manually trigger the `eval.yml` workflow with the provider key stored
as a repository secret; verify the run completes and publishes results without any
Anthropic credential configured anywhere in CI.

### Tests for User Story 2

- [X] T018 [P] [US2] Fixture-based test: given a sample eval-results file with mixed pass/fail scores, the summary formatter produces the expected per-test pass/fail + scores markdown (both the PR-comment form and the job-summary form) in `scripts/ci/format-eval-summary.test.sh` (or equivalent, exercising `scripts/ci/format-eval-summary`)

### Implementation for User Story 2

- [X] T019 [US2] Implement `scripts/ci/format-eval-summary` (script), converting the `dotnet test` results file into the markdown summary consumed by T018's test (depends on T018)
- [X] T020 [US2] Create `.github/workflows/eval.yml`: `workflow_dispatch` trigger with optional `pr_number` and `sample_count` inputs; start the LiteLLM proxy and poll until ready (fail fast with a clear message if it never comes up); set `GRIMOIRE_EVAL=1` and `GRIMOIRE_EVAL_PROVIDER_*` from `secrets.NVIDIA_NIM_API_KEY`/fixed base URL/model; run `dotnet test backend/tests/Grimoire.AgentEvals` with a results logger; always upload the results file and transcripts as artifacts; write the formatted summary (T019) to `$GITHUB_STEP_SUMMARY` (depends on T019, T008)
- [X] T021 [US2] Add a PR-comment step to `.github/workflows/eval.yml` using `gh pr comment` against `pr_number`, executed only when that input is provided; when omitted, no comment is attempted — artifacts and job summary remain the only output (FR-007, no-PR fallback) (depends on T020)
- [X] T022 [US2] Add the `NVIDIA_NIM_API_KEY` secret to the repository's secret store (manual GitHub repository-settings step); document the one-time setup in `specs/007-eval-tests-nim-endpoint/quickstart.md` (confirmed done manually by the repo owner, 2026-07-22)
- [ ] T023 [US2] Run `quickstart.md` Scenario 4 end-to-end: trigger `eval.yml` once with `pr_number` set against a real open PR, and once without it; confirm the comment/no-comment behavior and that artifacts are always present (depends on T020, T021, T022)

**Checkpoint**: User Stories 1 AND 2 both work independently.

---

## Phase 5: User Story 3 - Keep provider choice flexible and transparent (Priority: P3)

**Goal**: Every eval run's recorded output names the model that actually produced it,
regardless of provider, and the existing Anthropic path is unaffected.

**Independent Test**: Run the same eval with two different configured models (Anthropic and
affordable) and verify each run's recorded output names the model that was actually used.

### Tests for User Story 3

- [X] T024 [P] [US3] Test: with the affordable provider configured, the completed run's `TaskArtifactDocument.Model` equals the configured `GRIMOIRE_EVAL_PROVIDER_MODEL` value (SC-002/FR-009) in `backend/tests/Grimoire.AgentEvals/EvalProviderResolverTests.cs`
- [X] T025 [P] [US3] Test: running the same eval once with only `ANTHROPIC_AUTH_TOKEN` set and once with only the affordable provider set produces two artifacts, each naming its own model, with the Anthropic-only run's `Model`/behavior identical to pre-feature output (FR-011) in `backend/tests/Grimoire.AgentEvals/EvalProviderTransparencyTests.cs`

### Implementation for User Story 3

- [X] T026 [US3] Run `quickstart.md` Scenario 5 manually; confirm both artifacts name their respective models (depends on T008, T024, T025)

**Checkpoint**: All three user stories are independently functional.

---

## Final Phase: Observability & Polish (MANDATORY — Constitution Principle IV)

**Purpose**: Emit and verify the business metric, structured log events, and trace span
declared in `plan.md ## Observability`, and run the remaining cross-cutting/evaluation
gates. Per Constitution Principle III, these tasks require the Phase 2–5 implementation to
already exist and therefore belong here, not in Phase 0.

- [X] T027 Implement the `eval_provider_resolved` (INFO) structured log event — fields `provider`, `outcome`, `model`, `reason` — emitted once `EvalProviderResolver.Resolve()` finishes, in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs`
- [X] T028 [P] Deterministic integration test validating `eval_provider_resolved`'s event name, level, and all four mandatory fields across the enabled/skipped/configuration-error permutations, in `backend/tests/Grimoire.IntegrationTests/EvalObservabilityTests.cs`
- [X] T029 Implement the `eval_sample_timeout` (WARN) structured log event — fields `eval_name`, `provider`, `model`, `timeout_seconds` — emitted when `TimeoutEnforcingModelClient` throws `ModelCallTimeoutException`, in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs`
- [X] T030 [P] Deterministic integration test validating `eval_sample_timeout`'s event name, level, and mandatory fields, in `backend/tests/Grimoire.IntegrationTests/EvalObservabilityTests.cs`
- [X] T031 Implement the `grimoire.eval.gate_resolutions_total` counter (labels `provider`, `outcome`), incremented alongside T027, in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs`
- [X] T032 [P] Deterministic integration test validating the counter increments correctly for each provider/outcome label combination, in `backend/tests/Grimoire.IntegrationTests/EvalObservabilityTests.cs`
- [X] T033 Implement the `eval.gate_resolution` trace span (root, attributes `provider`/`outcome`/`model`) around gate resolution, in `backend/tests/Grimoire.AgentEvals/AgentEvalSupport.cs`
- [X] T034 [P] Deterministic integration test validating the span name and attributes via an in-memory OTel exporter (ADR-005 pattern), in `backend/tests/Grimoire.IntegrationTests/EvalObservabilityTests.cs`
- [X] T035 CI enforcement: confirm T028/T030/T032/T034 run in the standard PR pipeline — they land in `Grimoire.IntegrationTests`, which `.github/workflows/ci.yml`'s `backend` job already runs unconditionally; verify no change to `ci.yml` is needed and no provider secret is required (SC-005)
- [ ] T036 Agent-behavior evaluation tests (MANDATORY — Constitution Principles II & V): run all 6 existing `EvalFact`-gated eval classes in `backend/tests/Grimoire.AgentEvals/` against the configured affordable provider and confirm every threshold in the underlying feature specs still passes, unchanged (SC-006) — no new code, a verification run

  **Attempted 2026-07-22 (reduced to `GRIMOIRE_EVAL_SAMPLES=3` per cost/time agreement).**
  The run surfaced and led to fixing two real harness bugs (see the
  "Fix eval-harness provider env-var self-pollution and judge-client wiring" commit):
  `AgentEvalRunner.RunAsync` mutated the real process's `ANTHROPIC_AUTH_TOKEN`
  permanently, so the second sample (or second eval class) in the same process
  self-triggered a `ConfigurationError`; and `SteeringAdoptionEvals`'s separate
  LLM-judge client never received the affordable-provider wiring at all.

  With both fixed, a re-run completed with **no harness errors** (no
  `ConfigurationError`, no auth failures) — confirming the gate/wiring/timeout
  mechanics work correctly end-to-end. However, most SC-006/007/008/009/010 samples
  still failed. Root cause, confirmed via transcript/task-artifact inspection: a
  separate, pre-existing bug in `SafetyPolicy.PrefixMatches`
  (`backend/src/Grimoire.Domain/Guardrails/SafetyPolicy.cs`) — a directory-prefix rule
  (e.g. `"pages/"`) never matched the bare directory path itself (e.g.
  `list_files(path: "pages")`, which canonicalizes without the trailing separator the
  rule requires), so every such `list_files` call was denied with `reason: "no_rule"`
  regardless of provider. This applies identically to Anthropic and the affordable
  provider — not a regression from this feature — but it was blocking real
  measurement of SC-006 on the affordable provider, so it was fixed (see the "Fix
  SafetyPolicy directory-prefix matching" commit) at the user's direction.

  A 1-sample sanity check after the fix confirmed the `"pages"` denial is gone (only
  the harmless, undocumented `list_files(".")` attempt remains denied — the system
  prompt never instructs listing the wiki root itself).

  **Full run completed 2026-07-23 (`GRIMOIRE_EVAL_SAMPLES=5`, all 6 classes, 37 live
  samples total, ~1h56m).** Results: SC-006 0/5, SC-007 Convention 0/5, SC-007 Steering
  1/10, SC-008 0/5, SC-009 1/5, SC-010 0/5 — none met their spec threshold. Root cause
  is now conclusive and singular: **34 of the 37 samples** failed with
  `failure_reason: "Model call exceeded the 120s timeout."` (`TimeoutEnforcingModelClient`,
  FR-013), confirmed via task-artifact inspection across every class. The handful of
  samples that *did* complete within the timeout passed their check correctly (e.g.
  `sc009-5`, `sc007-steer-2`, `sc007-steer-5`) — proving the harness path (gate →
  provider wiring → real network call → guardrail evaluation → completion → correct
  `Model` field) works end-to-end. There is no remaining harness or guardrail defect;
  this is a genuine performance characteristic of the configured NVIDIA NIM model
  (`minimax-m3` via the LiteLLM proxy) under this workload — it too often cannot
  complete a single agent turn within FR-013's 120s bound. SC-006 as measured on this
  specific affordable-provider configuration does **not** currently pass — that is a
  model/infrastructure capability finding, not a code defect, and is outside what this
  feature (provider selection) can fix by construction. Options for whoever owns this
  finding: try a faster NIM model/config, or accept the affordable path as
  developer-convenience-only (unblocking local iteration without an Anthropic
  subscription) rather than a like-for-like substitute for Anthropic's judgment
  thresholds.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural Boundary)**: No dependencies — run first, blocks everything else.
- **Phase 1 (Setup)**: Depends on Phase 0.
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Phase 2 completion.
  - US1 (P1) has no dependency on US2/US3.
  - US2 (P2) depends on Phase 2's `AgentEvalRunner` wiring (T008) but not on US1's tests.
  - US3 (P3) depends on Phase 2's `AgentEvalRunner` wiring (T008) but not on US1/US2.
  - All three can proceed in parallel once Phase 2 is done; priority order (P1 → P2 → P3) is recommended for solo/incremental delivery.
- **Final Phase (Observability & Polish)**: Depends on Phase 2 at minimum (needs `EvalProviderResolver`/`TimeoutEnforcingModelClient` to exist); T036 additionally benefits from US1/US2 being done so a real affordable-provider run is available to verify thresholds against.

### Within Each User Story

- Tests (T010–T016, T018, T024–T025) MUST be written and FAIL before the Phase 2 implementation they exercise is considered complete.
- Story validation task (T017, T023, T026) comes last in each story, after that story's tests pass.

### Parallel Opportunities

- T004 and T006 (Phase 2) can run in parallel (different concerns, same file — coordinate on merge).
- T009 is parallel-flagged but depends on T008 landing first.
- All of T010–T016 (US1 tests) can be written in parallel.
- T018 (US2 test) and T024–T025 (US3 tests) can proceed in parallel with the US1 test phase once Phase 2 is done.
- T028, T030, T032, T034 (Final Phase tests) can run in parallel once their respective implementation task lands.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together once Phase 2 is complete:
Task: "Neither configured → Skipped, reason names both options — EvalProviderResolverTests.cs"
Task: "Affordable-only complete → Enabled/Affordable — EvalProviderResolverTests.cs"
Task: "Partial affordable config → Skipped, not counted present — EvalProviderResolverTests.cs"
Task: "Both configured → ConfigurationError naming conflict — EvalProviderResolverTests.cs"
Task: "Unreachable affordable endpoint → connectivity error, not skip — EvalConnectivityTests.cs"
Task: "TimeoutEnforcingModelClient exceeds timeout → ModelCallTimeoutException — TimeoutEnforcingModelClientTests.cs"
Task: "Rejected-auth double → key redacted from FailureReason/Narrative — EvalProviderResolverTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0: Structural Boundary Enforcement.
2. Complete Phase 1: Setup (proxy smoke test + corrected `.env-example`).
3. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
4. Complete Phase 3: User Story 1.
5. **STOP and VALIDATE**: run `quickstart.md` Scenarios 1–3.
6. This is a usable MVP: any developer can run the full eval suite locally without an Anthropic subscription.

### Incremental Delivery

1. Phase 0 + Phase 1 + Phase 2 → Foundation ready.
2. Add User Story 1 → validate independently (MVP!).
3. Add User Story 2 → validate independently (on-demand CI eval runs).
4. Add User Story 3 → validate independently (provider transparency, Anthropic path unchanged).
5. Final Phase → observability instrumentation + agent-judgment threshold verification, gating the Definition of Done.

---

## Notes

- [P] tasks = different files (or independent test cases in the same file), no dependencies on each other.
- [Story] label maps task to specific user story for traceability.
- Verify tests fail before the implementation they exercise is considered done.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- No task in this feature touches `AnthropicModelClient.cs` or any other production `src/` file under `Grimoire.IngestAgent` — everything is scoped to test projects, CI configuration, and documentation, consistent with `plan.md`'s "no new ADR required" finding.

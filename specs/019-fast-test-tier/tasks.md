---

description: "Task list for feature implementation"
---

# Tasks: Fast Developer Feedback Tier for the Backend Test Suite

**Input**: Design documents from `/specs/019-fast-test-tier/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md,
contracts/test-tier-commands.md, contracts/deterministic-wait-rule.md,
docs/adr/ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md (Accepted)

**Tests**: This feature's subject *is* the test suite, so most tasks are themselves
test-infrastructure changes verified by running the suites they touch — there is no
separate "write tests first, then implement" split for most stories the way a
feature with production endpoints would have. Where a genuine test-before-code
ordering applies (the Phase 0 structural rule; PollAsync's own timeout behavior), it
is called out explicitly. No agent-judgment success criteria exist (spec.md's
Success Criteria preamble: "All outcomes of this feature are deterministic
harness/tooling guarantees") — confirmed against `plan.md`'s Agentic Boundary
section ("No agentic surface — harness-only feature") — so no evaluation-threshold
tests are generated.

**Logging Contract**: N/A — `plan.md ## Observability` has zero Structured Log
Events rows (justified: no production business logic, no runtime component).

**Trace Contract**: N/A — `plan.md ## Observability` has zero Distributed Trace
Spans rows (same justification).

**Organization**: Tasks are grouped by user story per `spec.md`'s five stories:
US1 (P1, fast tier), US2 (P2, integration-suite speedup), US3 (P3, opt-in slow-tier
docs/command), US4 (P3, eval concurrency), US5 (P4, future-test guidance). Phases 1
("Setup") and 2 ("Foundational") of the standard template are intentionally omitted:
this feature adds no new project and has no infrastructure that blocks every story
equally — the one shared artifact multiple stories touch (the AgentEvals trait
scheme) is introduced by US1 and extended in place by US3/US4, tracked explicitly in
Dependencies below.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify the ADR-021 structural rule before any feature code is
written. Enforces FR-003/FR-004/FR-005/FR-010 and SC-004/SC-007 —
`contracts/deterministic-wait-rule.md` is this task's exact spec.

**⚠️ NON-NEGOTIABLE**: No feature implementation (T002+) may begin until this phase
is complete and confirmed RED/GREEN per the probe below.

- [X] T001 Write `DeterministicTierNoFixedWaitRuleTests` in
      `backend/tests/Grimoire.ArchTests/DeterministicTierNoFixedWaitRuleTests.cs`.
      Using the same Mono.Cecil IL-scan idiom as
      `RuntimePathsBoundaryRuleTests.cs`, scan the compiled assemblies of
      `Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, `Grimoire.IntegrationTests`,
      and `Grimoire.AgentEvals` for any `call`/`callvirt` IL instruction targeting
      `System.Threading.Tasks.Task::Delay` or `System.Threading.Thread::Sleep`. A
      call site is **not** a violation when either holds: (a) the containing method
      is `Grimoire.IntegrationTests.TestSupport.PollAsync` itself, or (b) the
      containing method or its declaring type carries a Mono.Cecil-detected
      `[Trait("TimingDependent", "true")]` custom attribute. Every other call site is
      a reported violation named by assembly, type, and method (same
      violation-reporting shape as `RuntimePathsBoundaryRuleTests`).

**Red/Green probe** (required — confirms the rule actually detects violations):
1. Add a scratch test method containing an un-exempted `Task.Delay(1000)` call to a
   test class in one of the four scanned assemblies.
2. Run the rule; confirm it FAILS and names the exact call site (assembly, type,
   method).
3. Remove the scratch method. Do not merge probe code.
4. Run the rule again against the *current, unmodified* codebase; record its result
   (see Note below — this is expected to still report violations at this point,
   which is correct, not a probe failure).

**Note (expected initial state, not a defect)**: `Grimoire.IntegrationTests`
currently has 53 `Task.Delay` call sites (~49 already-bounded ad hoc poll loops plus
~4 genuinely fixed unconditional waits — see `research.md` R4) and none of them yet
route through `PollAsync` or carry `[Trait("TimingDependent", "true")]`, because
neither exists yet. Immediately after T001 lands, this rule is therefore expected to
report those 53 pre-existing call sites as violations — this is the correct,
honest state of a brownfield retrofit, not a Phase 0 failure. T011 (US2) is the task
that drives this rule to zero violations by consolidating/triaging every call site.
Do not skip, `[Fact(Skip=...)]`, or weaken this rule to hide the interim red state;
CI on this feature branch is expected to stay red on the `Grimoire.ArchTests` step
until T011 completes.

**Definition of Done**:
- [X] Rule written and committed
- [X] Red/Green probe completed (commit message documents the probe result)
- [X] Rule's current (interim) violation count against `Grimoire.IntegrationTests`
      recorded, to be driven to zero by T011 (53 pre-existing violations, matching
      research.md's 53 Task.Delay/Thread.Sleep count — see commit e2f4546)

**Checkpoint**: Structural rule is guarded and provably able to detect violations.
Feature code may now begin.

---

## Phase 3: User Story 1 - Fast inner-loop feedback while developing (Priority: P1) 🎯 MVP

**Goal**: A single documented command (`scripts/test-fast.sh`) runs
`Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, and the `Tier=Fast`-filtered
hermetic subset of `Grimoire.AgentEvals`, executes in low single-digit seconds, and
never executes a replay-eval test (FR-001, FR-002, FR-014; SC-001, SC-002).

**Independent Test**: Run `time ./scripts/test-fast.sh` on a clean, built checkout;
confirm exit 0, no replay-eval class name in the output, and a low single-digit-second
test-execution wall clock. Repeat with `data/evals/recordings/` absent and no
`ANTHROPIC_AUTH_TOKEN` set — confirm it still completes successfully.

- [X] T002 [US1] In `backend/tests/Grimoire.AgentEvals/`, add
      `[Trait("Tier", "Fast")]` to all nine hermetic harness-mechanics test classes
      per `research.md` R1: `ReplayContractTests`, `CaptureHygieneTests`,
      `StalenessTests`, `EvalProviderResolverTests`, `EvalCredentialRedactionTests`
      (nested in `EvalProviderResolverTests.cs`), `LintDeterministicScorersTests`,
      `RemediationReVerificationScorerTests`, `LocalEnvFileTests`,
      `TimeoutEnforcingModelClientTests`. Additionally, for `ReplayContractTests` and
      `CaptureHygieneTests` only, remove their existing
      `[Collection("EvalRunnerProcessTests")]` attribute entirely (they do not spawn
      agent processes or mutate process-wide state, so they can rejoin xUnit's
      default parallel execution). Leave `StalenessTests`'s and
      `EvalCredentialRedactionTests`'s `[Collection("EvalRunnerProcessTests")]`
      attribute untouched for now — T018 (US4) redirects it to a new dedicated
      collection. `EvalProviderResolverTests`, `LintDeterministicScorersTests`,
      `RemediationReVerificationScorerTests`, `LocalEnvFileTests`, and
      `TimeoutEnforcingModelClientTests` carry no `[Collection]` attribute today and
      need none added — they only gain the trait.
- [X] T003 [P] [US1] Create `scripts/test-fast.sh`: a `set -eo pipefail` shell script
      that runs, in order,
      `dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release`,
      `dotnet test backend/tests/Grimoire.ArchTests --configuration Release`, and
      `dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=Fast"`,
      stopping at the first failing invocation so the developer sees which tier
      failed (per `contracts/test-tier-commands.md`). Make it executable
      (`chmod +x`).
- [X] T004 [US1] Add a new `Grimoire.ArchTests` rule,
      `AgentEvalsTierMembershipRuleTests.cs`, that reflects/Mono.Cecil-inspects the
      compiled `Grimoire.AgentEvals` assembly and asserts: the nine classes named in
      T002 carry `[Trait("Tier", "Fast")]`, and none of the five replay-eval classes
      (`IngestReplayEvalTests`, `LintReplayEvalTests`, `QueryReplayEvalTests`,
      `LintRemediationProposalRelevanceEvalTests`,
      `RemediationReVerificationEvalTests`) carry `Tier=Fast`. This operationalizes
      SC-002/FR-014 as a permanent regression guard rather than a one-time manual
      check. (Scope note: this task does not yet assert the five replay classes
      carry `Tier=SlowEval` — that trait is added in T014/US3; asserting it here
      would fail until US3 lands.)
- [X] T005 [US1] Run the `quickstart.md` US1 walkthrough: `time ./scripts/test-fast.sh`
      — confirm exit 0, console output shows `Grimoire.Domain.UnitTests`,
      `Grimoire.ArchTests`, and a `Tier=Fast`-filtered `Grimoire.AgentEvals` run with
      no replay-eval class name present (SC-002), and record the reported
      test-execution wall clock against the SC-001 target (≤ 5s, excluding the prior
      `dotnet build`). Repeat in an environment/container with `data/evals/recordings/`
      absent and no `ANTHROPIC_AUTH_TOKEN` set — confirm the script still completes
      successfully (SC-002, FR-002).
- [X] T006 [US1] Deliberately violate a domain invariant in
      `backend/src/Grimoire.Domain` (temporarily), rerun `./scripts/test-fast.sh`,
      and confirm the failure is reported without the developer needing to run
      `Grimoire.IntegrationTests` or the `Tier=SlowEval` suite first (Acceptance
      Scenario 3). Revert the deliberate violation afterward.

**Checkpoint**: The fast tier is fully functional and independently testable/deliverable
as the MVP — a developer gets sub-5-second feedback today, even before US2–US5 land.

---

## Phase 4: User Story 2 - Integration tests finish without paying fixed real-time waits (Priority: P2)

**Goal**: `Grimoire.IntegrationTests` runs at least 30% faster than the 61s baseline
via parallel collection execution, every genuinely fixed unconditional wait is
converted to condition-based waiting or explicitly marked timing-dependent, and
zero test coverage is lost (FR-003, FR-004, FR-005, FR-011; SC-003, SC-004).

**Independent Test**: Audit the suite for fixed unconditional real-time waits used
only to out-wait an async operation (target: zero remaining) and compare wall-clock
runtime against the recorded 61s baseline on the same environment.

- [X] T007 [US2] Create
      `backend/tests/Grimoire.IntegrationTests/TestSupport/PollAsync.cs`: a shared
      helper, e.g. `PollAsync(Func<Task<bool>> condition, TimeSpan timeout, string
      onTimeoutMessage, TimeSpan? pollInterval = null)`, that polls `condition` on a
      bounded interval (default ~20–25ms, matching the suite's existing ad hoc
      pattern) until it returns `true` or `timeout` elapses, and calls
      `Assert.Fail(onTimeoutMessage)` with a clear diagnostic on expiry (FR-004).
      This method is the rule's one allow-listed call site (`contracts/deterministic-wait-rule.md`).
- [X] T008 [US2] Audit and triage every `Task.Delay`/`Thread.Sleep` call site in
      `backend/tests/Grimoire.IntegrationTests` (53 occurrences found by
      `research.md` R4/R5 audit; re-grep to confirm current count before starting).
      For each: (a) if it is a fixed unconditional wait used only to out-wait an
      async operation with an observable completion signal (audit candidates per
      `research.md`: `IngestTaskRecordWatcherTests.cs` lines ~50/64/78's
      `Task.Delay(TimeSpan.FromSeconds(...))`, `QueryInterruptionTests.cs` line
      ~134's `Task.Delay(500)`, and any other true fixed wait the re-audit finds —
      confirm the final count against the ~4 the research audit estimated) —
      convert it to `PollAsync` polling that observable signal; (b) if the test's own
      subject is genuinely time-based (e.g. asserting a debounce window itself
      elapses, or a documented self-restart delay actually occurs) — keep the wait,
      add `[Trait("TimingDependent", "true")]` to the method or class with a
      one-line rationale comment (FR-005); (c) every remaining ad hoc poll-loop tick
      call (the ~49 already-bounded `while`-loop polling pattern) — consolidate onto
      `PollAsync` with no behavior change (removes duplication, gives T001's rule its
      one sanctioned call site). Do not touch `Fakes/FakeAgentProcess.cs` or
      `Fakes/FakeModelClient.cs`'s `Task.Delay` calls if they simulate production
      timing behavior of the fake itself rather than a test out-waiting an operation
      — evaluate each against the same (a)/(b) split and tag `TimingDependent`
      accordingly if kept.
- [X] T009 [P] [US2] Flip `parallelizeTestCollections` from `false` to `true` in
      `backend/tests/Grimoire.IntegrationTests/xunit.runner.json`. Confirm
      `IngestAgentObservabilityCollection.cs`'s
      `[CollectionDefinition(..., DisableParallelization = true)]` is left unchanged
      and remains the sole serialization boundary (FR-011).
- [X] T010 [P] [US2] Remove the unused
      `<PackageReference Include="Testcontainers" />` line from
      `backend/tests/Grimoire.IntegrationTests/Grimoire.IntegrationTests.csproj` and
      the corresponding `<PackageVersion Include="Testcontainers" Version="4.13.0" />`
      entry from `backend/Directory.Packages.props` (confirm no other test project
      references `Testcontainers` before removing the version entry) (FR-015).
- [X] T011 [US2] Run `DeterministicTierNoFixedWaitRuleTests` (T001) against the
      post-triage `Grimoire.IntegrationTests` assembly; confirm zero violations
      remain (SC-004, SC-007) — this is the point where T001's rule goes fully green
      against the real codebase for the first time.
- [X] T012 [US2] Run `time dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release`;
      confirm all tests pass, executed test count ≥ 583, and wall clock is ≥ 30%
      below the recorded 61s baseline on the same/comparable environment (SC-003).
- [X] T013 [P] [US2] Add or confirm a test that exercises Acceptance Scenario 4:
      temporarily point a `PollAsync`-awaited condition at one that never becomes
      true and confirm the test fails with the bounded-timeout diagnostic message
      rather than hanging.

**Checkpoint**: `Grimoire.IntegrationTests` is measurably faster with zero coverage
loss and zero fixed unconditional waits outside `TimingDependent`-marked tests —
independently testable and deliverable without US3/US4/US5.

---

## Phase 5: User Story 3 - Agent-evaluation suite as an explicit, opt-in slow tier (Priority: P3)

**Goal**: The three tiers are documented (purpose, duration class, command) in
CONTRIBUTING.md, the stale `GRIMOIRE_EVAL=1` claim and its dead code are removed, and
`dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"` runs exactly
the five genuine replay-eval classes (FR-006, FR-007; SC-005).

**Independent Test**: Follow the documentation to run each tier: the default
developer workflow (`./scripts/test-fast.sh`) completes without executing any
evaluation test, and the documented eval-tier command executes exactly the
replay-eval suite.

- [ ] T014 [US3] Add `[Trait("Tier", "SlowEval")]` to the five genuine replay-eval
      classes in `backend/tests/Grimoire.AgentEvals/`: `IngestReplayEvalTests`,
      `LintReplayEvalTests`, `QueryReplayEvalTests`,
      `LintRemediationProposalRelevanceEvalTests`, `RemediationReVerificationEvalTests`.
      **Note**: `contracts/test-tier-commands.md`'s trait-vocabulary section describes
      these classes as "untagged" for the *default/unfiltered* project run, but the
      same contract's documented opt-in command
      (`dotnet test ... --filter "Tier=SlowEval"`) requires an actual
      `Tier=SlowEval` trait value to match anything under xUnit's filter semantics —
      an untagged class does not match `Tier=SlowEval`. This task resolves that by
      explicitly tagging the five classes, so the documented command in
      `contracts/test-tier-commands.md` and `quickstart.md` actually selects them;
      the "untagged" language is read as applying only to the whole-project
      unfiltered run's default behavior, not the filtered command's mechanics.
- [ ] T015 [US3] Rewrite CONTRIBUTING.md's "Building and testing" section: remove the
      paragraph "Agent-behavior (evaluation) tests that call a real LLM provider are
      gated behind `GRIMOIRE_EVAL=1` and are not part of the default hermetic test
      run"; add a new `## Test Tiers` subsection documenting all three tiers — Fast
      (`scripts/test-fast.sh`, purpose, "fast" duration class), Integration
      (`dotnet test backend/tests/Grimoire.IntegrationTests`, "moderate"), and
      SlowEval (`dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"`,
      "slow, opt-in") — each with its purpose, contents, duration class, and exact
      command, per `contracts/test-tier-commands.md` (FR-007, SC-005, Acceptance
      Scenario 1).
- [ ] T016 [P] [US3] Delete `backend/tests/Grimoire.AgentEvals/EvalFactAttribute.cs`
      (defines the dead `[EvalFact]`/`EvalGate` code — confirmed zero `[EvalFact]`
      usages and zero `EvalGate.*` call sites anywhere in the suite; superseded by
      ADR-012's recorded-replay tier) (FR-007/research.md R8 cleanup).
- [ ] T017 [US3] Verify: `dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"`
      executes exactly the five classes named in T014 and no others; re-run
      `./scripts/test-fast.sh` (US1) and confirm it still executes zero
      evaluation tests after T014's tagging (re-confirms SC-002/FR-006 hold after
      this phase's change). Extend `AgentEvalsTierMembershipRuleTests.cs` (T004)
      with an assertion that exactly these five classes carry `Tier=SlowEval`.

**Checkpoint**: All three tiers are documented and independently runnable by a
single command each; the stale doc/dead-code contradiction (FR-007) is resolved.

---

## Phase 6: User Story 4 - Agent-evaluation suite runs faster without changing what it verifies (Priority: P3)

**Goal**: Replay-eval scenarios execute with bounded concurrency (both at the
collection level and inside each scenario's per-sample loop) and per-sample
workspace setup is parallelized, cutting `Grimoire.AgentEvals` wall clock by ≥ 50%
versus the ~190s baseline with byte-identical sample counts, scores, and thresholds
versus a sequential run (FR-012, FR-013; SC-008).

**Independent Test**: Run the agent-evaluation tier on the reference environment and
compare wall-clock runtime against the ~190s baseline while verifying executed
sample count, scorer results, and thresholds are identical to a sequential run.

- [ ] T018 [US4] In `backend/tests/Grimoire.AgentEvals/SyntheticRecordings.cs`,
      remove the single
      `[CollectionDefinition("EvalRunnerProcessTests", DisableParallelization = true)]`
      / `EvalRunnerProcessTestsCollection` marker class and replace it with two:
      (a) `[CollectionDefinition("EvalRunnerEnvMutatingTests", DisableParallelization = true)]`,
      applied via `[Collection("EvalRunnerEnvMutatingTests")]` to `StalenessTests`
      (`StalenessTests.cs`) and `EvalCredentialRedactionTests` (nested in
      `EvalProviderResolverTests.cs` — the class that actually mutates real process
      environment variables via `Environment.SetEnvironmentVariable`; the outer
      `EvalProviderResolverTests` class uses an injectable `Env()` func and needs no
      collection); (b) `[CollectionDefinition("EvalRunnerReplayScenarios")]` (leave
      `DisableParallelization` at its default `false`), applied via
      `[Collection("EvalRunnerReplayScenarios")]` to the five replay classes tagged
      in T014, replacing their old `[Collection("EvalRunnerProcessTests")]`
      reference.
- [ ] T019 [US4] In `backend/src/Grimoire.EvalRunner/Replay/ReplayPipeline.cs`'s
      `RunScenarioAsync` (the sequential `for` loop over `manifest.Samples`, lines
      ~61–67, calling `ReplaySampleAsync` one sample at a time), change it to bounded-
      concurrent execution (e.g. `Parallel.ForEachAsync` capped at
      `Environment.ProcessorCount`, or an equivalent semaphore-gated fan-out),
      preserving each sample's position in the returned `results` list and full
      per-sample isolation (own `EvalWorkspace`, own recording, own
      `AgentProcessInvoker` call) (FR-012).
- [ ] T020 [P] [US4] In `backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs`'s
      `CopyDirectory` (invoked twice from `Create` — wiki fixture, agent
      instructions), parallelize the per-file copy (e.g. `Parallel.ForEachAsync` over
      `Directory.GetFiles(sourceDir)`) instead of the sequential `foreach`; recurse
      into subdirectories the same way. The source directory is shared and read-only
      across samples, so this introduces no new isolation risk (research.md R7)
      (FR-013).
- [ ] T021 [US4] Run `time dotnet test backend/tests/Grimoire.AgentEvals --configuration Release`;
      confirm wall clock is ≥ 50% below the recorded ~190s baseline and zero tests
      are skipped (`ci.yml`'s `Skipped:\s+0,` check). Then re-run forced-sequential
      (`-- xunit.parallelizeAssembly=false xunit.parallelizeTestCollections=false`,
      or a pre-change comparison) and diff executed sample count, scorer results,
      and pass/fail thresholds against the concurrent run — confirm they are
      identical (SC-008; FR-012 Acceptance Scenario 2/3).
- [ ] T022 [US4] Confirm each replay sample still uses its own isolated
      `EvalWorkspace` under concurrency: inspect
      `Path.GetTempPath()/grimoire-eval-runner/` during a concurrent run and confirm
      one directory per sample, never shared (quickstart.md US4 step 4).

**Checkpoint**: The opt-in slow tier is materially faster with unchanged replay
semantics — independently testable and deliverable without US5.

---

## Phase 7: User Story 5 - Future tests follow the tiering and traceability rules (Priority: P4)

**Goal**: Written guidance in CONTRIBUTING.md tells contributors which tier a new
test belongs in, states the TDD-against-expected-behavior rule, and states the
edge-case traceability rule; the automated rule (T001) is demonstrated to reject a
reintroduced fixed wait (FR-009; SC-007 re-demonstration, Acceptance Scenario 2).

**Independent Test**: Verify the guidance exists where contributors are directed,
states the tier-placement/deterministic-waiting/edge-case-traceability rules, and
verify a fresh attempt to reintroduce a fixed wait is rejected by
`dotnet test backend/tests/Grimoire.ArchTests`.

- [ ] T023 [US5] Extend CONTRIBUTING.md's `## Test Tiers` section (added in T015)
      with guidance stating: new tests are written TDD-style against expected system
      behavior; which tier a new test belongs in (by what it verifies, not which
      project it lives in — FR-014); and that edge-case coverage is added only when
      traceable to a concrete user-facing scenario (a spec, functional requirement,
      or user story) (FR-009, Acceptance Scenario 1).
- [ ] T024 [US5] Re-demonstrate the Phase 0 rule against the now-fully-triaged
      codebase: add a scratch test with a deliberate un-exempted `Task.Delay` call to
      a deterministic-tier test project, run
      `dotnet test backend/tests/Grimoire.ArchTests`, confirm
      `DeterministicTierNoFixedWaitRuleTests` (T001) fails naming the exact call
      site, then remove the scratch test and confirm the rule passes again
      (Acceptance Scenario 2; quickstart.md US5 step 2). This is the feature's final
      confirmation that SC-007's "100% of attempts... are rejected" holds once the
      codebase itself carries zero pre-existing violations (unlike T001's initial
      probe, which ran against a codebase still full of pre-triage waits).

**Checkpoint**: All five user stories are complete; the feature's guarantees are
both documented and machine-enforced going forward.

---

## Phase N: Polish & Cross-Cutting Concerns

**Purpose**: Final-phase completeness audit (Constitution Principle III) and
full-suite parity validation. This feature has no Structured Log Events or
Distributed Trace Spans rows and no agent-judgment success criteria, so those
completeness-audit sub-items are short-circuited below rather than omitted
silently.

- [ ] T025 Full four-suite merge-gate parity run: execute
      `dotnet test backend/tests/Grimoire.ArchTests`,
      `dotnet test backend/tests/Grimoire.Domain.UnitTests`,
      `dotnet test backend/tests/Grimoire.IntegrationTests`, and
      `dotnet test backend/tests/Grimoire.AgentEvals` (all `--configuration Release`,
      unfiltered); sum the executed test counts and confirm the total is ≥ 796
      (SC-006); diff `.github/workflows/ci.yml` and confirm all four steps still
      gate the PR job unchanged in shape (FR-008 — no suite removed from the merge
      gate).
- [ ] T026 Observability completeness audit (MANDATORY — Constitution Principle
      III/IV): confirm `plan.md ## Observability` has zero rows (Business Metrics,
      Structured Log Events, Distributed Trace Spans all "None — N/A per
      justification") and that no task above introduced an unaudited signal —
      direct diff review of `scripts/test-fast.sh`, all `*.cs` trait/collection
      changes, `ReplayPipeline.cs`, and `EvalWorkspace.cs` confirms no `ILogger`,
      metric, or trace-span call was added. File a new task only if a gap is found;
      none expected.
- [ ] T027 Agent-behavior evaluation completeness audit (MANDATORY only for
      features with agentic behavior — Constitution Principles II & V): confirm
      `spec.md`'s Success Criteria preamble and `plan.md`'s Agentic Boundary section
      both state this feature has no agentic surface and defines no agent-judgment
      success criteria — including SC-008, whose only judgment-adjacent aspect
      (replay scores) is verified by T021's exact-match diff against the unchanged
      sequential baseline, not a new sampled threshold. No evaluation tests
      required; audited, no gap found.
- [ ] T028 Final FR/SC completeness audit (MANDATORY — Constitution Principle III):
      cross-reference every functional requirement and success criterion against
      its implementing task, filing any gap as a new task before declaring the DoD
      met:
      - FR-001/FR-002 → T003, T005 · FR-003/FR-004 → T007, T008, T013 · FR-005 → T008
      - FR-006 → T003/T005 (exclusion), T017 · FR-007 → T015, T016 · FR-008 → T025
      - FR-009 → T023 · FR-010 → T001, T011, T024 · FR-011 → T007–T009, T012
      - FR-012 → T018, T019, T021, T022 · FR-013 → T020, T021 · FR-014 → T002, T004, T014
      - FR-015 → T010
      - SC-001 → T005 · SC-002 → T004, T005, T017 · SC-003 → T012 · SC-004 → T011
      - SC-005 → T015, T029 · SC-006 → T025 · SC-007 → T011, T024 · SC-008 → T021, T022
      Confirm every row above has a passing (or, for T029, upcoming) task and file
      any gap as a new task before the DoD is declared met.
- [ ] T029 Run `quickstart.md` validation end-to-end (all five user-story sections
      plus the "Full-suite / merge-gate parity check" section) and record the
      outcome of each.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (T001)**: No dependencies — can start immediately. BLOCKS all of
  T002–T024 (Constitution Principle III: structural rule before feature code).
- **US1 (Phase 3, T002–T006)**: Depends only on T001. Independent of US2–US5.
- **US2 (Phase 4, T007–T013)**: Depends only on T001. Independent of US1/US3/US4/US5
  (different files: `Grimoire.IntegrationTests` vs `Grimoire.AgentEvals`).
- **US3 (Phase 5, T014–T017)**: Depends on T001. T017's structural-rule extension
  depends on T004 (US1) existing. T015 depends on T003 existing only insofar as it
  documents `scripts/test-fast.sh`'s already-created command (soft ordering, not a
  hard block).
- **US4 (Phase 6, T018–T022)**: Depends on T001. T018 touches the same five replay
  classes' attributes that T014 (US3) already tagged with `Tier=SlowEval` — do T014
  before T018 to avoid a merge conflict on the same lines (not a logical
  dependency, a file-overlap sequencing note). T019/T020 touch different files
  (`ReplayPipeline.cs`, `EvalWorkspace.cs`) and have no dependency on T018 or each
  other — mark `[P]`.
- **US5 (Phase 7, T023–T024)**: T023 depends on T015 (same CONTRIBUTING.md section,
  sequential edit). T024 depends on T001 and is best run after T011 (US2) so the
  rule's RED state is attributable solely to the injected scratch test, not
  lingering pre-existing violations — do not run T024 before T011 completes.
- **Polish (Phase N)**: Depends on all of US1–US5 being complete.

### Parallel Opportunities

- T003 (script) can be written in parallel with T002 (trait tagging) — different
  files.
- T009 (xunit.runner.json flip) and T010 (Testcontainers removal) can run in
  parallel with each other and with T007/T008 — different files within
  `Grimoire.IntegrationTests`.
- T013 can run in parallel with T009/T010 once T007 (PollAsync) exists.
- T016 (delete EvalFactAttribute.cs) can run in parallel with T014/T015 — different
  file.
- T019 and T020 can run in parallel — different files (`ReplayPipeline.cs` vs
  `EvalWorkspace.cs`).

---

## Parallel Example: User Story 1

```bash
# T002 and T003 touch different files — launch together:
Task: "Tag nine hermetic Grimoire.AgentEvals classes [Trait(\"Tier\",\"Fast\")], drop [Collection] on ReplayContractTests/CaptureHygieneTests"
Task: "Create scripts/test-fast.sh"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0 (T001) — rule written, Red/Green probed.
2. Complete Phase 3 (T002–T006) — fast tier command exists, documented, verified
   against SC-001/SC-002.
3. **STOP and VALIDATE**: `./scripts/test-fast.sh` gives sub-5-second feedback with
   zero eval prerequisites. This alone delivers the feature's core value.

### Incremental Delivery

1. Phase 0 → Phase 3 (US1, MVP) → validate independently.
2. Add Phase 4 (US2) → validate independently (SC-003/SC-004, rule fully green).
3. Add Phase 5 (US3) → validate independently (tier docs, SlowEval filter exact).
4. Add Phase 6 (US4) → validate independently (SC-008 speed + parity).
5. Add Phase 7 (US5) → validate independently (guidance + rule re-demonstration).
6. Phase N — completeness audits + full quickstart validation → DoD met.

---

## Notes

- Commit after each task or logical group; do not batch unrelated files into one
  commit (this feature already touches many small, independently reviewable
  surfaces).
- T001's rule stays RED against `Grimoire.IntegrationTests` from Phase 0 until T011
  (US2) — expected, documented, not a regression to chase down early.
- Avoid: deleting or weakening any existing test to hit a runtime target (FR-008,
  spec Assumptions — the audit found no untraceable edge-case bloat, so pruning is
  explicitly out of scope).
- Avoid: introducing a new environment-variable gate (e.g. `GRIMOIRE_FAST_TESTS=1`)
  — `research.md` R1 explicitly rejected this in favor of the `Tier` trait.

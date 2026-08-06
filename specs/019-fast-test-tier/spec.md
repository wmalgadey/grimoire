# Feature Specification: Fast Developer Feedback Tier for the Backend Test Suite

**Feature Branch**: `019-fast-test-tier`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "Fast developer feedback tier for the backend test suite (from GitHub issue #44, https://github.com/wmalgadey/grimoire/issues/44). The backend test suite currently takes over 300 seconds to run locally, which makes it useless as a developer feedback loop. Grimoire.AgentEvals alone accounts for ~64% of runtime, and CI's PR gate runs all four suites in the same sequence a developer would run locally, so there is no fast inner loop. Investigation showed the slowness is not caused by edge-case bloat (IntegrationTests are already traceable to concrete FR/user-story slices) but by (a) fixed real-time waits (Task.Delay/Thread.Sleep, 146 occurrences) in Grimoire.IntegrationTests instead of deterministic condition-based synchronization, and (b) no fast/slow test-tier separation. Goals: a developer-facing fast tier (Domain.UnitTests, ArchTests, and ideally the non-timing-dependent parts of IntegrationTests) completes in low single-digit seconds and can be run independently of AgentEvals; fixed delays in IntegrationTests are replaced with deterministic condition-based waiting wherever they exist only to out-wait an async operation; AgentEvals is documented/runnable as an explicitly slower, separate opt-in tier so its cost is not paid on every default test run; and going forward new tests are written TDD-style against expected system behavior, with edge-case coverage added only when traceable to a concrete user-facing scenario (spec/FR/user story)."

**Source**: [GitHub issue #44 — Backend test suite exceeds 300s locally — needs a fast developer feedback tier](https://github.com/wmalgadey/grimoire/issues/44)

## Clarifications

### Session 2026-08-03

- Q: How should User Story 2 / SC-003 be corrected, given a suite audit found only ~4 true fixed unconditional waits (~0.5 s combined) in the integration suite — not the 146 assumed by issue #44 (that count matched already-condition-based wait constructs)? → A: Keep the −30% runtime target (SC-003) but broaden the permitted levers to any deterministic runtime reduction — test parallelization, poll-interval tuning, shared host/fixture reuse — with fixed-wait replacement as one tactic among several.
- Q: AgentEvals' ~190 s runtime is mechanical (≈235 sequential agent child-process spawns with parallelization disabled, fresh fixture copy per sample), not LLM-bound — is reducing it in scope? → A: In scope: add a user story and requirements to parallelize replay-scenario execution and reduce per-sample setup cost, keeping replay semantics, sample counts, scorers, and thresholds unchanged.
- Q: ~45 of AgentEvals' ~71 facts are hermetic harness-mechanics tests (scorers, replay contracts, staleness, env-file parsing), not evals — re-tier them out of the slow opt-in tier? → A: Yes: the slow opt-in tier contains only the actual replay-eval scenarios; hermetic harness-mechanics tests run with the deterministic tiers (joining the fast tier if fast enough). Tier membership reflects what a test verifies, not which project it lives in.
- Q: Should the spec allow pruning tests ("only useful tests"), given the audit found no untraceable edge-case bloat? → A: No — FR-008 stays strict: no test deletion or weakening in this feature. Usefulness is preserved by re-tiering and the FR-009 traceability rule; slowness stems from mechanics, not bloat.
- Q: Existing docs are already wrong (CONTRIBUTING.md describes an unused `GRIMOIRE_EVAL=1` gate; an unused Testcontainers package reference lingers) — fix as part of this feature? → A: Yes, both: the FR-007 tier documentation replaces the stale contributor-doc claims, and the dead Testcontainers package reference is removed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast inner-loop feedback while developing (Priority: P1)

A developer working on domain logic, architecture rules, or harness behavior wants to
verify their change locally. They run a single, documented "fast tier" command that
executes the quick, deterministic test suites — without the agent-evaluation suite —
and get a pass/fail answer within seconds, so they can stay in a tight
edit-test-edit loop.

**Why this priority**: This is the core value of the feature. Today the only
documented path runs everything (≈ 300 s), which means developers either wait five
minutes per iteration or stop running tests locally at all. A fast tier restores the
feedback loop that makes test-driven work possible.

**Independent Test**: Can be fully tested by running the documented fast-tier command
on a clean checkout and measuring wall-clock test execution time and which suites
were executed. Delivers value even if no other story is implemented, because
developers immediately gain a sub-minute verification path.

**Acceptance Scenarios**:

1. **Given** a built backend workspace, **When** the developer runs the documented
   fast-tier command, **Then** the domain unit tests and architecture tests execute
   and complete, and no agent-evaluation test is executed.
2. **Given** a built backend workspace, **When** the developer runs the fast-tier
   command, **Then** test execution completes in low single-digit seconds
   (see SC-001), excluding compilation.
3. **Given** a developer who has made a change that breaks a domain rule,
   **When** they run the fast-tier command, **Then** the failure is reported by the
   fast tier without the developer having to wait for slower tiers.

---

### User Story 2 - Integration tests finish without paying fixed real-time waits (Priority: P2)

A developer runs the integration test suite locally. Tests that previously slept for
a fixed real-time duration merely to out-wait an asynchronous operation now wait
deterministically on the actual condition, and the suite as a whole runs measurably
faster with no loss of coverage — using any deterministic speed lever available
(parallel test execution, tighter poll intervals, shared fixture/host reuse), not
only wait replacement.

**Why this priority**: A suite audit (clarification session 2026-08-03) found only
~4 true fixed unconditional waits (~0.5 s combined); most wait-shaped code consists
of poll intervals inside already-bounded condition loops. The dominant runtime
driver is sequential execution of tests that each start real infrastructure.
Shrinking the largest deterministic suite makes it a candidate for inclusion in the
fast inner loop. It is P2 because the fast tier (P1) already delivers value from the
two already-fast suites even before the integration suite is sped up.

**Independent Test**: Can be tested independently by auditing the integration test
suite for fixed unconditional real-time waits used only to out-wait asynchronous
operations (target: zero remaining), and by comparing suite wall-clock runtime
against the recorded baseline on the same environment.

**Acceptance Scenarios**:

1. **Given** an integration test that previously used a fixed delay to out-wait an
   asynchronous operation, **When** the awaited condition completes early, **Then**
   the test proceeds immediately instead of sleeping for the full fixed duration.
2. **Given** the full integration test suite, **When** it is run after the change on
   the same environment as the recorded baseline, **Then** total suite runtime is
   measurably lower than the baseline (see SC-003) and the number of executed tests
   has not decreased.
3. **Given** a test that verifies genuine timing behavior (where elapsed real time is
   itself the behavior under test), **When** the suite is audited, **Then** that test
   is explicitly identified as timing-dependent and is exempt from the fixed-delay
   replacement.
4. **Given** a condition-based wait whose condition never becomes true, **When** the
   test runs, **Then** it fails with a clear timeout diagnosis rather than hanging
   indefinitely.

---

### User Story 3 - Agent-evaluation suite as an explicit, opt-in slow tier (Priority: P3)

A developer who has changed agent instructions or evaluation scenarios deliberately
opts into running the agent-evaluation suite, knowing it is the slow tier. Developers
who have not touched agent behavior are never forced to pay its cost as part of the
default local test run.

**Why this priority**: The agent-evaluation suite accounts for roughly two thirds of
total local runtime and is constitutionally mandated to exist (Principle II) — it
cannot be removed or made hermetic. The only lever is making its cost explicit and
opt-in. It is P3 because P1 already excludes it from the fast tier; this story adds
the documented, intentional way to run it.

**Independent Test**: Can be tested by following the documentation to run each tier:
the default developer workflow completes without executing any evaluation test, and a
separate documented command executes exactly the evaluation suite.

**Acceptance Scenarios**:

1. **Given** the project documentation, **When** a developer looks up how to run
   tests locally, **Then** the tiers are described with their purpose and expected
   duration, and the evaluation suite is clearly marked as the slow, opt-in tier.
2. **Given** a developer following the documented default local workflow, **When**
   they run it, **Then** no agent-evaluation test executes.
3. **Given** a developer who wants to verify agent behavior, **When** they run the
   documented evaluation-tier command, **Then** the agent-evaluation suite executes
   independently of the other tiers.

---

### User Story 4 - Agent-evaluation suite runs faster without changing what it verifies (Priority: P3)

A developer who opts into the agent-evaluation tier gets its verdict in a fraction of
today's ~190 s, because replay scenarios execute concurrently and per-sample setup is
cheaper — while every sample still replays the same recordings through the real agent
executable, scored by the same scorers against the same thresholds.

**Why this priority**: Investigation (clarification session 2026-08-03) showed the
eval suite's cost is mechanical — sequential child-process spawns (≈235 per run) with
test parallelization explicitly disabled and a fresh fixture-workspace copy per
sample — not an inherent cost of agent evaluation. Cutting it lowers the price of the
opt-in tier (User Story 3) and of every merge-gate run, without touching what the
evals verify. It shares P3 with the opt-in story because both shape the slow tier;
it depends on neither P1 nor P2.

**Independent Test**: Can be tested by running the agent-evaluation tier on the
reference environment and comparing wall-clock runtime against the recorded ~190 s
baseline, while verifying the executed sample count, scorer results, and thresholds
are identical to a sequential run.

**Acceptance Scenarios**:

1. **Given** the agent-evaluation suite, **When** it runs after the change, **Then**
   replay scenarios execute concurrently where sample isolation permits, and total
   wall-clock runtime is reduced (see SC-008) versus the recorded baseline.
2. **Given** any replay sample, **When** it executes under concurrency, **Then** it
   uses its own isolated workspace and recordings, and its score is identical to the
   score produced by a sequential run.
3. **Given** the full evaluation suite, **When** it completes, **Then** the number of
   executed samples, the scorers applied, and the pass thresholds are unchanged from
   the baseline, and no test is skipped.

---

### User Story 5 - Future tests follow the tiering and traceability rules (Priority: P4)

A contributor adding new backend tests finds written guidance that tells them:
write tests TDD-style against expected system behavior; place them in the correct
tier; and add edge-case coverage only when it is traceable to a concrete user-facing
scenario (a spec, functional requirement, or user story).

**Why this priority**: This preserves the value of the other three stories over time.
Without a stated rule, new fixed delays and untraceable edge-case tests would erode
the fast tier again. It is P4 because it protects future state rather than improving
current runtime.

**Independent Test**: Can be tested by verifying the guidance exists in the location
contributors are directed to, states the tier placement rules, the
deterministic-waiting rule, and the edge-case traceability rule, and by verifying an
automated check rejects reintroduction of forbidden fixed waits in the deterministic
tiers.

**Acceptance Scenarios**:

1. **Given** a contributor preparing to add a backend test, **When** they consult the
   project's testing guidance, **Then** it states which tier the new test belongs in
   and that edge-case tests must reference the concrete user-facing scenario they
   cover.
2. **Given** a change that reintroduces a fixed unconditional real-time wait (used
   only to out-wait an asynchronous operation) into a deterministic-tier test,
   **When** the standard verification pipeline runs, **Then** the violation is
   reported and the change is rejected.

---

### Edge Cases

- What happens when a condition-based wait's condition is never met? The test must
  fail with a bounded timeout and a diagnostic message, not hang the suite.
- How are tests handled whose subject is genuinely time-based (e.g., verifying that
  something happens only after a configured interval)? They are explicitly exempted
  and documented as timing-dependent; they are not silently left in the fast path.
- What happens when the evaluation suite has stale recordings (the 7 pre-existing
  failures noted in issue #44)? Out of scope for this feature; the tiering must not
  mask or "fix" them by exclusion — they remain visible whenever the evaluation tier
  runs.
- What happens on a slower or faster machine than the baseline environment? Runtime
  targets are anchored to the same reference environment as the issue #44 baseline
  measurements; relative improvements (percentage, suite composition) hold anywhere.
- What happens if replacing a fixed delay exposes a real race condition in the system
  under test? The race is surfaced as a genuine finding, not papered over by
  restoring the sleep; the affected test may keep a documented, justified wait only
  if the behavior itself is timing-dependent.
- What happens if concurrent replay execution changes an evaluation score? Replay is
  deterministic replay of committed recordings, so any score divergence under
  concurrency indicates cross-sample interference — a defect to fix before the
  concurrent mode ships, never an accepted variance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST provide a single documented fast-tier command that
  runs the fast deterministic suites (at minimum the domain unit tests and the
  architecture tests) and excludes the agent-evaluation suite.
- **FR-002**: The fast tier MUST be runnable independently of the agent-evaluation
  suite — executing it MUST NOT require evaluation recordings, live model access, or
  any evaluation-suite prerequisite.
- **FR-003**: Every fixed unconditional real-time wait in the integration test suite
  that exists only to out-wait an asynchronous operation MUST be replaced with
  deterministic condition-based waiting that completes as soon as the awaited
  condition holds.
- **FR-004**: Every condition-based wait MUST have a bounded timeout and MUST fail
  with a clear diagnostic when the timeout is reached.
- **FR-005**: Tests whose verified behavior is inherently time-based MAY retain
  real-time waits, but each such test MUST be explicitly identified as
  timing-dependent so it is distinguishable from a forbidden fixed delay.
- **FR-006**: The agent-evaluation suite MUST be runnable as a separate, explicitly
  documented tier, and the documented default local developer test workflow MUST NOT
  execute it.
- **FR-007**: Project documentation MUST describe the test tiers: what each tier
  contains, its purpose, its expected duration class (fast / moderate / slow), and
  the command to run it. This documentation MUST replace the stale claims in
  existing contributor documentation (notably the obsolete `GRIMOIRE_EVAL=1` gating
  description); no contradictory description of how to run tests may remain.
- **FR-008**: The restructuring MUST NOT reduce existing verification coverage: no
  test may be deleted or weakened to achieve runtime targets, and every suite that
  gates merges today MUST continue to gate merges.
- **FR-009**: Written testing guidance MUST state that new tests are written against
  expected system behavior (TDD-style), placed in the correct tier, and that
  edge-case coverage is added only when traceable to a concrete user-facing scenario
  (spec, functional requirement, or user story).
- **FR-010**: The rule forbidding fixed unconditional real-time waits (per FR-003)
  in deterministic-tier tests MUST be enforced by an automated check in the standard
  verification pipeline, so reintroduction is rejected rather than relying on
  reviewer vigilance.
- **FR-011**: The integration-suite runtime reduction (SC-003) MAY be achieved
  through any deterministic mechanism — parallel test execution, reduced poll
  intervals in condition-based waits, shared fixture or host reuse, and fixed-wait
  replacement — provided test determinism, isolation, and coverage (FR-008) are
  preserved.
- **FR-012**: Replay scenarios in the agent-evaluation suite MUST be able to execute
  concurrently, with every sample retaining full isolation (its own workspace,
  fixture copy, and recordings). Concurrency MUST NOT change replay semantics: the
  executed sample count, the scorers applied, the resulting scores, and the pass
  thresholds MUST be identical to a sequential run.
- **FR-013**: Per-sample setup overhead in the agent-evaluation suite (workspace and
  fixture preparation) MUST be reduced where this does not weaken sample isolation;
  each sample still replays its recordings through the real agent executable.
- **FR-014**: Tier membership MUST reflect what a test verifies, not which project
  file it lives in: hermetic harness-mechanics tests currently housed in the
  agent-evaluation project (scorer logic, replay contracts, staleness checks,
  configuration parsing) MUST run with the deterministic tiers, and the slow opt-in
  tier MUST contain only the replay-eval scenarios that exercise agent judgment.
- **FR-015**: Dependencies declared by test projects but referenced by no test code
  (currently the unused Testcontainers package reference in the integration test
  project) MUST be removed as part of the restructuring.

### Key Entities

- **Test tier**: A named grouping of tests with a defined purpose, duration class,
  run command, and inclusion rule (fast deterministic tier; integration tier; opt-in
  agent-evaluation tier). Membership follows what a test verifies, not project
  boundaries (per FR-014): hermetic harness-mechanics tests belong to a
  deterministic tier even when housed in the evaluation project.
- **Timing-dependent test**: A test explicitly marked as verifying real elapsed-time
  behavior, exempt from the fixed-delay ban.
- **Baseline measurement**: The recorded issue #44 runtimes (per suite, on the
  reference environment) against which improvement is judged.

## Success Criteria *(mandatory)*

All outcomes of this feature are deterministic harness/tooling guarantees; the
feature changes no agent behavior and therefore defines no agent-judgment evaluation
thresholds. The agent-evaluation suite is re-tiered and made mechanically faster
(concurrent replay, cheaper per-sample setup), but its replay semantics — sample
counts, scorers, scores, and thresholds — are never altered.

### Measurable Outcomes

- **SC-001**: The fast tier's test execution (excluding compilation) completes in at
  most 5 seconds on the reference environment used for the issue #44 baseline.
- **SC-002**: 100% of fast-tier runs execute zero agent-evaluation tests, and the
  fast tier runs to completion on a machine with no evaluation prerequisites
  available.
- **SC-003**: Integration test suite wall-clock runtime on the reference environment
  is reduced by at least 30% versus the recorded baseline (61 s), with the number of
  executed tests not lower than the baseline count (583). Any deterministic lever
  (per FR-011) counts toward this target.
- **SC-004**: An audit of the integration test suite finds zero fixed unconditional
  real-time waits that exist only to out-wait an asynchronous operation; 100% of
  retained real-time waits are explicitly identified as timing-dependent.
- **SC-005**: A developer following only the project documentation can run each tier
  with a single command per tier, and the documented default local workflow finishes
  without executing the agent-evaluation suite.
- **SC-006**: The full suite (all tiers together) executes at least the same number
  of tests as the issue #44 baseline (796 across the four suites), and every suite
  that gated merges before the change still gates merges after it.
- **SC-007**: 100% of attempts to reintroduce a forbidden fixed unconditional wait
  into a deterministic-tier test are rejected by the standard verification pipeline.
- **SC-008**: Agent-evaluation suite wall-clock runtime on the reference environment
  is reduced by at least 50% versus the recorded baseline (~190 s), while the
  executed sample count, scorer results, and pass thresholds are identical to the
  sequential baseline and zero tests are skipped.

## Assumptions

- A suite audit (clarification session 2026-08-03) found all existing tests
  purposeful and traceable; suite runtime problems stem from execution mechanics
  (sequential real-host and child-process execution), not from edge-case bloat.
  Test pruning is therefore explicitly not a lever of this feature (FR-008).
- The merge gate continues to run all tiers, including the agent-evaluation suite;
  this feature adds a fast local inner loop and explicit tiering, it does not remove
  any suite from the merge gate (per FR-008). Re-sequencing or parallelizing the
  merge-gate pipeline for speed is welcome but not required by this spec.
- "Low single-digit seconds" applies to test execution of the fast tier after the
  code is built; compilation time is out of scope for the runtime targets.
- The 7 pre-existing agent-evaluation failures caused by stale recordings (noted in
  issue #44) are out of scope; refreshing recordings is a separate activity.
- Runtime targets (SC-001, SC-003) are anchored to the same reference environment on
  which the issue #44 baseline was measured; on other hardware the relative
  improvement and tier composition are the binding expectations.
- Whether the non-timing-dependent parts of the integration suite are folded into
  the developer-facing fast tier is a desirable outcome ("ideally", per issue #44)
  but not a hard requirement: the fast tier MUST at minimum contain the domain unit
  tests and architecture tests, and the integration suite MUST become faster
  (SC-003) whether or not it joins the fast tier.
- Agent-judgment verification remains exclusively in the agent-evaluation suite as
  mandated by the constitution; nothing in this feature converts evaluation-style
  coverage into deterministic tests or vice versa.

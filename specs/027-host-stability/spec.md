# Feature Specification: Host Stability Guarantee for Agent Runs

**Feature Branch**: `027-host-stability`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Host stability guarantee for agent runs. Constitution v1.12.0 (Principle V, \"Host stability guarantee\") requires: regardless of what a task or an instruction file says — including malformed or adversarial content — the harness MUST ensure the agent process cannot destabilize the host: unbounded CPU, memory, disk, or subprocess consumption, or any action outside the guarded tool boundary and credential scope already in force. This guarantee holds independently of instruction-file content and must be proven by hermetic tests exercising real resource pressure (never by agent-behavior evaluation, since it must hold even when the agent is actively misbehaving). Today this is a known gap (recorded in the constitution's Sync Impact Report for v1.12.0): agent child processes are spawned with no CPU/memory/disk quota and no wall-clock ceiling on the dispatch path; a spawned agent may itself spawn arbitrary subprocesses (the existing tree-kill is reactive only, tied to the liveness window); the markdown converter child process has a timeout but no memory cap and its stdout is buffered unbounded in Hub memory; URL fetching downloads without any size limit; guarded writes have no per-write or per-run content-size cap, so disk growth within policy scope is unbounded. Already bounded today (keep, not in scope to change): agent turn cap, context cap, and spend cap in the agent loop; the converter wall-clock timeout; the liveness-window supervision. The feature: the operator can rely on the host surviving any single agent run. Every resource vector an agent run can consume (CPU time, resident memory, disk writes, downloaded/converted content size, number of child processes, total run wall-clock) is bounded by an operator-configurable limit with a safe default; hitting a limit terminates or denies the offending operation deterministically, is recorded with a reason (like guardrail denials are today), surfaces to the operator through the Hub's observability (metrics/log events/spans per Principle IV, visible on a user-facing surface per the operator loop), and never corrupts durable state (task artifacts and records reflect the terminated run's true state). An instruction file or task input must have no way to raise or disable these limits. All success criteria for this feature are deterministic harness guarantees (100%) — there is no agent-judgment criterion in scope, so no eval suite; verification is hermetic tests exercising real resource pressure per the constitution."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A runaway agent run cannot take down the host (Priority: P1)

The operator runs Grimoire unattended on a machine that also does other work (or is
small — a self-hosted server). An agent run goes wrong: the model loops, an instruction
file is malformed, or ingested content is adversarial, and the run starts consuming CPU,
memory, or wall-clock time without bound. The harness ends the run at its configured
resource ceiling, records why, and the host — and every other Grimoire run — continues
unaffected. The operator finds a terminated run with a clear reason, not a frozen or
crashed machine.

**Why this priority**: This is the constitutional guarantee itself (Principle V, Host
stability guarantee). Every other story refines how limits are configured or observed;
without this one, an unattended deployment is unsafe and the constitution's mandate is
unmet. It is also the minimum viable slice: fixed safe defaults with no configurability
would already deliver it.

**Independent Test**: Can be fully tested by launching a run whose (scripted, misbehaving)
agent process consumes CPU, memory, wall-clock, or spawns subprocesses without bound, and
observing that the harness terminates the run at the ceiling while the host and a
concurrently running well-behaved run stay healthy.

**Acceptance Scenarios**:

1. **Given** a run whose agent process exceeds its memory ceiling, **When** the ceiling is
   crossed, **Then** the run is terminated (the agent process and all of its
   descendants), the run reaches a terminal failure state naming the exceeded limit, and
   no other running task is affected.
2. **Given** a run whose agent process exceeds its total wall-clock ceiling — even while
   still emitting liveness signals, **When** the ceiling is crossed, **Then** the run is
   terminated with the exceeded limit recorded as the reason.
3. **Given** a run whose agent process spawns more child processes than the configured
   subprocess ceiling, **When** the ceiling is crossed, **Then** the excess spawning is
   stopped, the run is terminated, and every process the run created is gone afterward.
4. **Given** a misbehaving run that is terminated at any ceiling, **When** the operator
   inspects the run afterward, **Then** its task artifact and harness records exist, are
   well-formed, and state the run's true terminal outcome (which limit, when) — never a
   record that claims success or an artifact left half-written by the kill.
5. **Given** an instruction file or task input that attempts to name, raise, or disable a
   resource limit, **When** the run executes, **Then** the limits in force are unchanged
   — nothing an agent or a task author writes can influence them.

---

### User Story 2 - Oversized external content is refused, not swallowed (Priority: P2)

The operator submits a source for ingest that turns out to be enormous — a huge download
behind a URL, or a document whose conversion output explodes in size. Instead of the
harness consuming unbounded memory or disk holding it, the fetch or conversion stops at
the configured size ceiling and the task fails with a clear "content too large" outcome
the operator can see and act on (split the source, raise the limit deliberately).

**Why this priority**: These vectors (fetch size, conversion output size, per-write and
per-run write volume) destabilize the host through the harness's own process rather than
the agent's, so they are not covered by terminating the agent process. They complete the
"every vector bounded" guarantee but are less acute than P1's runaway-process case.

**Independent Test**: Can be fully tested by submitting sources whose download or
conversion output exceeds the configured ceilings and asserting the task fails with the
size-limit reason while memory and disk usage stay bounded.

**Acceptance Scenarios**:

1. **Given** a URL source whose content exceeds the download size ceiling, **When** it is
   fetched, **Then** the transfer stops at the ceiling (it does not download fully and
   discard afterward), the task reaches a failure state naming the size limit, and the
   host's memory use stays bounded throughout.
2. **Given** a document whose conversion output exceeds the conversion size ceiling,
   **When** it is converted, **Then** conversion stops at the ceiling and the task fails
   naming the size limit, without the oversized output ever being held fully in memory.
3. **Given** an agent that attempts a single guarded write larger than the per-write size
   ceiling, **When** the write is invoked, **Then** it is denied at the tool boundary
   with a recorded reason and the run continues with its remaining allowed actions —
   exactly as guardrail denials behave today.
4. **Given** an agent whose accumulated guarded writes exceed the per-run write-volume
   ceiling, **When** the ceiling is crossed, **Then** further writes are denied with a
   recorded reason; already-completed writes remain valid and recorded.

---

### User Story 3 - The operator configures the limits and sees every limit event (Priority: P3)

The operator tunes resource ceilings to their machine — a larger server can afford more —
through the same configuration surface as the rest of the system, with every limit having
a safe default so a fresh deployment is protected without any tuning. When any limit
fires, the operator can see it: which run, which limit, its configured value, and what the
harness did, on a surface they actually look at.

**Why this priority**: Configurability and visibility make the guarantee operable, but
the guarantee itself (P1/P2, on safe defaults) protects the host even if this story ships
last.

**Independent Test**: Can be fully tested by starting the system with custom limit values
and asserting they are in force; starting with none and asserting the defaults are in
force; and firing any limit and asserting the operator-visible signals appear.

**Acceptance Scenarios**:

1. **Given** no limit configuration, **When** the system starts, **Then** every resource
   limit is active at its documented safe default.
2. **Given** an operator-configured value for a limit, **When** the system starts,
   **Then** that value is in force for subsequent runs, and the configuration surface is
   the operator's (host configuration) — never the agent's instruction files or the task
   request.
3. **Given** any limit event (termination or denial), **When** it occurs, **Then** the
   operator can see the run, the limit, its configured value, and the action taken
   through the Hub's observability signals on a user-facing surface, correlated to the
   run's identity.
4. **Given** an invalid limit configuration (negative, zero where zero is meaningless,
   non-numeric), **When** the system starts, **Then** startup fails with a message naming
   the offending setting — it does not silently fall back.

---

### Edge Cases

- A run is terminated at a ceiling at the exact moment it is writing its task artifact:
  the artifact must still end up well-formed and truthful (terminal state recorded by the
  harness, partial agent narrative preserved or absent — never a corrupt file).
- The agent process ignores the first termination request: the harness must escalate so
  that the process tree is gone within a bounded grace period regardless.
- Two limits are crossed near-simultaneously (e.g. memory and wall-clock): one reason is
  recorded deterministically; the run must not double-terminate or race itself into an
  inconsistent record.
- A subprocess spawned by the agent outlives its parent's termination attempt: cleanup
  must cover the whole tree, including processes that re-parented.
- The converter or fetch ceiling fires on content that is exactly at the boundary: at-limit
  content succeeds; only content strictly beyond the limit is refused (off-by-one is
  observable and tested).
- Limit events during concurrent runs: each event is attributed to the correct run; one
  run's termination never cancels or delays an unrelated run.
- The host itself is under external memory pressure unrelated to Grimoire: the harness's
  own supervision must keep functioning (its bookkeeping is not the thing that dies
  first).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The harness MUST bound every resource vector a single agent run can consume
  on the host: processor time, resident memory, total run wall-clock time, number of
  live child processes in the run's process tree, volume of disk written through guarded
  tools (per single write and cumulative per run), downloaded content size per fetch, and
  conversion output size per document.
- **FR-002**: Every limit MUST have a safe default that is active with no operator
  configuration, and every limit MUST be operator-configurable through the host's own
  configuration surface (the same tiers as other operator settings).
- **FR-003**: Limits MUST NOT be readable as an instruction-file or task-input concern:
  no content an agent reads, a task supplies, or an instruction file states can raise,
  lower, disable, or otherwise influence a limit in force. (Lowering is also excluded to
  keep the boundary absolute: limits belong to the operator, full stop.)
- **FR-004**: When a process-scoped ceiling (processor time, memory, wall-clock,
  subprocess count) is crossed, the harness MUST terminate the run: the agent process and
  every descendant process are stopped within a bounded grace period, the run reaches a
  terminal failure state, and the recorded outcome names the exceeded limit and its
  configured value.
- **FR-005**: When an operation-scoped ceiling (per-write size, per-run write volume,
  fetch size, conversion output size) is crossed, the harness MUST deny or stop that
  operation deterministically with a recorded reason; where the run can meaningfully
  continue (guarded-write denials), it continues with allowed actions, mirroring today's
  guardrail-denial behavior.
- **FR-006**: Oversized external content MUST be stopped at the ceiling as it streams in
  — the harness MUST NOT first buffer or download the full content and discard it
  afterward; host memory use for fetching and converting MUST stay bounded regardless of
  source size.
- **FR-007**: A run terminated or denied by any limit MUST leave durable state truthful
  and well-formed: task artifacts and harness records reflect the run's actual terminal
  outcome, completed writes remain valid, and no artifact or record is left corrupt.
- **FR-008**: Every limit event MUST be observable by the operator: emitted as business
  metrics, structured log events, and trace attributes correlated to the run's identity,
  and consumable on at least one user-facing surface (Principle V operator loop).
- **FR-009**: Limit enforcement MUST be independent of agent cooperation: it holds when
  the agent process is unresponsive, actively spawning, or ignoring termination —
  escalating as needed until the process tree is gone.
- **FR-010**: Invalid limit configuration MUST fail startup with a message naming the
  offending setting; the system MUST NOT start with silently substituted values.
- **FR-011**: Existing bounds stay as they are and out of scope to change: the agent
  loop's turn, context, and spend caps; the converter's wall-clock timeout; and
  liveness-window supervision. This feature adds the missing resource ceilings around
  them; where an existing mechanism already terminates a run, the new limits compose with
  it rather than replacing it.
- **FR-012**: The guarantee MUST be verified by hermetic tests that exercise real
  resource pressure (a genuinely memory-hungry, CPU-hungry, subprocess-spawning, or
  oversized-content workload) against the real enforcement path — never by agent-behavior
  evaluation and never by simulating the pressure away (Principle V; Principle II harness
  contracts).

### Key Entities

- **Resource limit policy**: the operator-owned set of ceilings in force for a run —
  each with a vector (CPU time, memory, wall-clock, subprocess count, per-write size,
  per-run write volume, fetch size, conversion output size), a configured value or safe
  default, and the scope it applies to (process-scoped vs. operation-scoped).
- **Limit event**: the record that a specific run crossed a specific ceiling — carries
  the run identity, the limit, its configured value, the action taken (terminated /
  denied / stopped), and when it happened. Appears in the run's durable records and in
  the observability signals.

## Success Criteria *(mandatory)*

All criteria below are deterministic harness guarantees (100%) per Principle II's
success-criteria split. There is no agent-judgment success criterion in this feature —
the guarantee must hold precisely when agent judgment has failed — so no criterion
carries a high-stakes/lower-stakes classification and no eval suite is in scope.

### Measurable Outcomes

- **SC-001**: 100% of runs whose process tree crosses a process-scoped ceiling (memory,
  processor time, wall-clock, subprocess count) under hermetic resource pressure reach a
  terminal failure state naming the exceeded limit, with the entire process tree gone
  within the bounded grace period.
- **SC-002**: 100% of oversized fetches and conversions stop at the configured ceiling
  with the task failing on the named size limit, while the harness's own memory use
  remains bounded (verified under real oversized inputs).
- **SC-003**: 100% of guarded writes beyond the per-write or per-run ceiling are denied
  with a recorded reason; 100% of previously completed writes remain valid afterward.
- **SC-004**: 100% of limit-terminated runs leave well-formed, truthful durable records:
  the task artifact and harness records name the limit outcome, and no artifact is
  corrupt or claims success.
- **SC-005**: 100% of limit events are visible through the Hub's observability signals
  with run correlation, and 0 limit values can be altered from instruction files or task
  input (attempts have no effect, verified adversarially).
- **SC-006**: With zero operator configuration, 100% of the limits are active at their
  documented safe defaults; with invalid configuration, startup fails naming the setting
  in 100% of cases.
- **SC-007**: During any single misbehaving run under hermetic resource pressure,
  concurrently running well-behaved work completes unaffected in 100% of test scenarios
  (no cross-run termination, delay beyond the host's normal scheduling, or record
  corruption).

## Assumptions

- "Host stability" is scoped to what a single Grimoire deployment can control: bounding
  its own runs' consumption. Protecting the host against other software, or against an
  operator deliberately configuring absurdly high limits, is out of scope.
- Safe defaults are chosen for a modest self-hosted machine and documented with the
  configuration surface; picking their concrete values is planning/implementation work,
  not spec work — the spec requires only that they exist, are safe for unattended
  operation, and are documented.
- The subprocess ceiling counts live processes in the run's tree at any moment, not
  cumulative spawns over the run's lifetime.
- Operation-scoped denials reuse the existing guardrail-denial recording shape (reasoned
  denial, run continues) rather than inventing a parallel mechanism; process-scoped
  terminations reuse the existing terminal-failure shape runs already have. This is a
  consistency assumption about operator experience, not an implementation prescription.
- Observability for limit events follows Principle IV as usual (metrics, structured log
  events, trace correlation); naming the exact signals is plan work (`plan.md ##
  Observability`), including the user-facing surface where the operator sees them.
- The constitution's host-stability rule also names "any action outside the guarded tool
  boundary and credential scope already in force" — those boundaries exist today
  (guarded tools, credential scoping) and are out of scope here except that this feature
  must not weaken them.

## Out of Scope

- Changing the agent loop's existing turn/context/spend caps or the liveness-window
  supervision (FR-011).
- Containerizing or otherwise re-architecting how agent processes are hosted; this
  feature bounds the current execution model. (A future ADR may still choose
  containerization; nothing here precludes it.)
- Multi-run/global resource budgeting across concurrent runs (per-run ceilings only).
- Network egress restrictions beyond download size (destination allow-listing is the
  credential/fetch boundary's concern, unchanged).

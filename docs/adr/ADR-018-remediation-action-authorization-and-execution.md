---
status: accepted
---

# ADR-018: Human-Authorized Remediation Action Execution

## Context and Problem Statement

Feature 015 (`specs/015-lint-board-parity/spec.md`) turns lint from a passive report
into actionable work items: the Lint agent proposes one **Remediation Action Task** per
actionable finding, a human reviews each on the shared task board, and only explicitly
authorized tasks are ever executed by the agent (FR-007..FR-018). This introduces a
cross-cutting concern no existing ADR covers: a **human-authorization gate between
agent proposal and agent execution**, with a persisted task state machine, sequential
one-at-a-time execution in authorization order, withdrawal semantics, execution-time
re-verification, and a task-scoped human⇄agent message history.

The safety property at stake is SC-005: **100% of wiki-modifying remediation actions
correspond to a task a human explicitly authorized** — a deterministic harness
guarantee, not an agent-judgment threshold. Per Constitution Principle II's
success-criteria split, this must be structurally enforced, not delegated to agent
self-restraint. Per Principle V, however, the *content* of each proposal, the decision
whether a finding is actionable, and the execution-time judgment whether a proposal
still applies remain agent judgment and must not leak into backend code.

Existing ADRs supply all the building blocks — spawned agent processes (ADR-002),
SQLite operational state with restart reconciliation (ADR-003), the guarded tool
boundary (ADR-006), per-run user-prompt override (ADR-007), the NDJSON event channel
with liveness supervision (ADR-008), FIFO single-slot dispatch (`IngestRunCoordinator`
precedent), frontmatter-only write scope (ADR-016), and append-only Markdown records
(ADR-014) — but none defines how they compose into an authorization-gated execution
pipeline.

## Decision Drivers

- SC-005/FR-008: no execution without prior explicit authorization — must be a
  structural harness guarantee (Principle II: 100% deterministic tier).
- FR-016: authorization is withdrawable until execution starts; the withdrawal/start
  race must resolve deterministically (spec Edge Cases).
- FR-017: authorized tasks execute sequentially, one at a time, in authorization
  order; waiting tasks are visibly distinct from the executing one.
- FR-018: execution-time re-verification is agent judgment (Principle V) — the harness
  must transport the "no longer applicable" outcome, never compute it.
- FR-012/FR-014: task-scoped message history persists past terminal outcomes.
- Principle I: new boundaries via ADR; reuse existing ports/adapters where they exist;
  persistence stores stay concrete classes under namespace containment.
- Minimal surface: compose existing mechanisms (coordinator shapes, event vocabulary,
  record formats) rather than inventing parallel concepts.

## Considered Options

1. **Authorization as dispatch precondition in a new `RemediationRunCoordinator`
   (FIFO-queue shape), proposals carried on the Lint terminal event, records
   ADR-014-shaped**: the harness never spawns an execution process except by dequeuing
   a task whose persisted state is `Authorized`; the coordinator mirrors
   `IngestRunCoordinator`'s persisted FIFO + single slot; the Lint agent's terminal
   `completed` event gains a structured `proposedActions` list; each task gets an
   append-only Markdown record (Conversation-Record-shaped) for attached context and
   message history.
2. Authorization checked *inside* the agent loop (a guardrail rule consulting task
   state at each write attempt), with execution processes spawned eagerly on proposal.
3. A standing remediation daemon/worker that watches the task table and self-schedules,
   instead of Hub-dispatched per-task processes.
4. Reuse the ingest queue itself (enqueue authorized remediation tasks as a new
   `QueuedIngestRun` kind) instead of a separate coordinator.

## Decision Outcome

Chosen option: **Option 1.**

### RemediationActionTask state machine (harness-owned, persisted)

States: `Proposed → Authorized → Executing → {Completed | Failed | NotApplicable}`,
plus `Proposed → Dismissed` (human, terminal, no agent involvement) and
`Authorized → Proposed` (withdrawal, FR-016). Transitions are valid only along these
edges; every transition attempt outside them is rejected (idempotent,
first-transition-wins for terminal states, mirroring `LintRunState.TryTransitionTo`).

- Rows live in the existing SQLite operational-state store
  (`Grimoire.Hub.OperationalState`, ADR-003), keyed by task id, carrying: originating
  lint run id, proposal description (verbatim agent text — the harness never edits
  it), state, authorization timestamp (defines FIFO order), and terminal
  outcome/reason.
- **Withdrawal race (FR-016, spec Edge Cases)**: `Executing` is entered inside the
  coordinator's slot lock *before* the process is spawned; withdrawal is a compare-
  and-swap `Authorized → Proposed` on the persisted row. Whichever transition commits
  first wins; the loser is rejected and surfaced to the caller (the board shows the
  actual outcome). Once `Executing`, the run goes to a terminal state only.
- Restart reconciliation (ADR-003): a task found `Executing` with no live process on
  Hub startup is failed by `RestartReconciler` exactly as running ingest tasks are;
  `Authorized` tasks survive a restart still authorized but the execution queue
  starts paused, consistent with the ingest queue's restart rule.

### Authorization gate = dispatch precondition (SC-005 enforcement)

The **only** code path that spawns a remediation-execution agent process is
`RemediationRunCoordinator.TryStartNextAsync`, which dequeues exclusively tasks in
state `Authorized` (ordered by authorization timestamp) and transitions them to
`Executing` under the slot lock. There is no other spawn site; the execution agent
binary is never invoked with a task id whose row is not `Executing`. This makes SC-005
a structural property: an unauthorized execution would require a code path that does
not exist.

- Structural enforcement (Principle III): a `Grimoire.ArchTests` rule proves the
  remediation namespace reaches `IAgentProcessLauncher` only from
  `RemediationRunCoordinator` (allow-listed-caller shape, mirroring the existing
  guarded-write boundary rules), verified with a Red/Green probe. Hermetic
  integration tests additionally drive every non-`Authorized` state through the
  coordinator and assert no launch occurs.

### Sequential execution (FR-017): FIFO-queue coordinator shape

`RemediationRunCoordinator` copies `IngestRunCoordinator`'s persisted-FIFO +
`SemaphoreSlim(1,1)`-slot shape (not `LintRunCoordinator`'s reject-immediately shape,
which remains the lint-run trigger's own semantics per FR-004): authorized tasks queue
in authorization order, `TryStartNextAsync` advances the queue on every terminal
transition, queue position is exposed so the board can distinguish waiting from
executing. Supervision reuses the ADR-008 NDJSON event channel and liveness-window
watchdog unchanged.

### Proposals ride the Lint terminal event (ADR-008 extension)

The Lint agent's terminal `completed` event gains an optional `proposedActions` field
(list of `{title, description, targetPath?}` — agent-authored text, harness-opaque),
alongside the existing `deniedActions`/`createdPages` precedents. The tolerant parser
already ignores unknown fields, so the change is backward compatible. The Hub
materializes one `Proposed` task row per entry *before* marking the lint run
completed — satisfying FR-007's "completed means all proposals are on the board".
The harness never filters, merges, or rewrites proposals (Principle V); an empty or
absent list simply creates no tasks.

### Execution and message turns: Lint agent binary, new invocation modes (ADR-002/ADR-016)

- **Execution mode**: the Hub spawns `Grimoire.LintAgent` with a remediation-execution
  request (task id, proposal text, human-attached context as the ADR-007 user-prompt
  override, same policy file). The agent first **re-verifies** the proposal against
  current wiki content (FR-018, agent judgment); if moot/stale it reports a
  `notApplicable` outcome with reason on its terminal event and makes no write;
  otherwise it applies the change through the unchanged guarded tool boundary
  (ADR-006), under the unchanged `frontmatter-only` policy (ADR-016) and write
  coordination (ADR-015). A proposal whose fix would exceed frontmatter scope fails
  at the guard with the recorded denial reason — no new write mode is introduced.
- **Message-turn mode** (FR-012): a bounded, read-only single exchange (Query-turn
  shape per ADR-011): the agent receives the task's context (finding, proposal,
  attached info, prior messages from the task record) and returns its response on the
  terminal event; the Hub appends both sides to the task record.
- The ADR-008 event vocabulary gains one optional terminal field for the execution
  mode's outcome (`notApplicable` + reason); everything else is reused verbatim.

### Task records: ADR-014 shape, one level down (FR-011/FR-012/FR-014)

Each remediation task gets one durable, append-only Markdown record at
`RemediationTaskRecordPathFor(taskId)` under a new `RemediationTasksDir` (registered in
`GrimoirePathOptions`/`ResolvedGrimoirePaths` per ADR-009): YAML frontmatter
(task id, run id, state bookkeeping) + human-readable sections per attached-context
entry and message exchange, written by a concrete `RemediationTaskRecordStore`
(persistence exemption, namespace-containment-tested per ADR-010). The record is the
source of prior-message context for message turns — context and audit trail cannot
diverge (ADR-014's rationale, inherited). Records outlive terminal outcomes (FR-014).

### Rejected options

- **Option 2** (in-loop authorization check): leaves an execution process running
  before authorization is verified, making SC-005 depend on a runtime check racing
  live agent turns instead of a structurally absent code path; also spawns processes
  for tasks that may never be authorized (wasted runs, larger attack surface).
- **Option 3** (standing daemon): introduces a new execution model contradicting
  ADR-002's per-unit-of-work child-process decision, plus new lifecycle/liveness
  machinery ADR-008 already solves for Hub-dispatched processes.
- **Option 4** (reuse ingest queue): entangles two domains' lifecycles — pausing or
  draining ingest would silently affect remediation and vice versa; FR-015 requires
  ingest behavior to remain exactly unchanged, which a shared queue puts at risk.

### Consequences

- Good, because SC-005 becomes structurally true (no spawn path outside the
  authorization-gated coordinator) and arch-testable with the established
  Red/Green-probe discipline.
- Good, because every mechanism is a reuse of an Accepted decision — coordinator
  shape (ingest), event channel (ADR-008), write scope (ADR-016), record format
  (ADR-014), paths (ADR-009) — keeping the genuinely new surface small: one state
  machine, one coordinator, one store, one event field.
- Good, because the withdrawal race has a single deterministic arbiter (the persisted
  row's compare-and-swap under the slot lock), directly satisfying the spec's edge
  case.
- Bad, because the Lint agent binary now has three invocation modes (lint run,
  execution, message turn), increasing its CLI/request surface; accepted because a
  fourth standalone agent process would duplicate instruction-loading and guardrail
  wiring for no boundary gain.
- Neutral, because remediation writes stay frontmatter-only for now; proposals whose
  fixes need body changes surface as guard-denied failures with reasons. Broadening
  the write scope is a deliberate future decision requiring its own ADR.

## More Information

Detailed rationale: `specs/015-lint-board-parity/research.md`. Contracts:
`specs/015-lint-board-parity/contracts/`. Per Constitution Principle III this ADR
reached **Accepted** before `/speckit-tasks` ran for feature 015 (accepted 2026-08-01
as part of the feature-015 workflow run; author sign-off).

**Implementation note (T042/US5, added during Phase 8 polish):** the "Authorization
gate = dispatch precondition" section above, as drafted, anticipated exactly one
allow-listed caller of `IAgentProcessLauncher` inside `Grimoire.Hub.RemediationTasks`
— `RemediationRunCoordinator`. Implementing FR-012 (task messaging) added a second,
independently allow-listed caller, `RemediationMessageTurnCoordinator`, which spawns
the message-turn invocation mode (a bounded, read-only Q&A exchange via a distinct
`IAgentProcessLauncher` overload and request type, deny-by-default write policy). This
does not weaken SC-005: a message turn never transitions `RemediationActionTask`'s
execution state machine and performs no wiki write, so it carries none of the
"execution without authorization" risk the gate exists to prevent — it is a second,
narrowly-scoped door, not a hole in the original one. The `Grimoire.ArchTests` rule
(`RemediationExecutionDispatchRuleTests`) was extended accordingly to an
allow-listed-caller *set* of exactly these two types, with the rationale documented
in the rule's own doc comment.

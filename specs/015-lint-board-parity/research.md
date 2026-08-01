# Research: Unified Task Board for Lint and Agentic Remediation

**Feature**: 015-lint-board-parity | **Date**: 2026-08-01

All Technical Context unknowns were resolved by codebase/ADR investigation; no external
research was required. Each decision below records what was chosen, why, and what was
considered.

## R1: Board realtime transport for lint and remediation lifecycle

**Decision**: Two new per-domain SignalR hub+publisher pairs —
`LintLifecycleHub`/`LintLifecyclePublisher` and
`RemediationLifecycleHub`/`RemediationLifecyclePublisher` — mirroring the existing
`IngestLifecycleHub`/`IngestLifecyclePublisher` and `QueryLifecycleHub` precedent
(`backend/src/Grimoire.Hub/Realtime/`). The lint page's current 1-second polling
(`frontend/src/routes/lint/+page.svelte` → `GET /api/lint-runs/{runId}`) is superseded
on the board by push updates; the REST endpoints remain for initial state recovery
(spec edge case: reconnect must recover correct state).

**Rationale**: ADR-001 fixes SignalR as the realtime transport; the board already
consumes `taskLifecycleChanged`/`runActivityChanged` broadcasts via
`createBoardLifecycleStream` (`ingestLifecycleClient.ts`). SC-001/SC-002 require
ingest-parity latency without reload — polling cannot deliver parity with push.
Per-domain hubs keep `IngestLifecycleHub` untouched (FR-015/SC-008: ingest behavior
must not change).

**Alternatives considered**: (a) one shared "board hub" multiplexing all domains —
rejected: requires touching the working ingest hub and client, highest regression risk
for SC-008; (b) keep polling and add lint polling to the board — rejected: fails
SC-002's no-reload parity intent and contradicts the established push precedent.

## R2: Dispatch shape for sequential remediation execution (FR-017)

**Decision**: New `RemediationRunCoordinator` copying `IngestRunCoordinator`'s
persisted-FIFO-queue + `SemaphoreSlim(1,1)` single-slot shape
(`backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs`), ordered by
authorization timestamp. Lint-run triggering itself keeps `LintRunCoordinator`'s
reject-immediately shape unchanged, extended only by the FR-004 precondition (no
unresolved remediation tasks).

**Rationale**: FR-017 requires queueing in authorization order with visible
waiting-vs-executing distinction — exactly what the ingest coordinator already provides
(persisted `QueuedIngestRun` rows, `GetQueuePositionsAsync`, `TryStartNextAsync`
advancing on every terminal transition, restart-pause semantics per ADR-003). The two
existing coordinator shapes (FIFO-queue vs. reject-immediately) were compared; FR-017's
semantics match the FIFO shape, FR-004's match reject-immediately.

**Alternatives considered**: reusing the ingest queue itself for remediation tasks —
rejected (ADR-018): entangles two domains' pause/drain lifecycles and risks FR-015.

## R3: How proposals travel from the Lint agent to the board (FR-007)

**Decision**: The Lint agent's terminal `completed` NDJSON event gains an optional
`proposedActions` field (agent-authored `{title, description, targetPath?}` entries).
The Hub materializes one `Proposed` task row per entry inside
`LintRunCoordinator.FinishRunAsync` *before* the run's terminal transition is
published, so "completed" already implies all cards exist (FR-007).

**Rationale**: ADR-008's event channel already carries structured terminal metadata
(`deniedActions`, `createdPages`, `systemPromptSha256` — see
`Grimoire.Hub.AgentDispatch.AgentRunEvent`); `AgentRunEventParser` is tolerant of
unknown fields, so the extension is backward compatible. Principle V: proposal content
stays agent-authored, harness-opaque — the Hub never filters or rewrites it.

**Alternatives considered**: (a) agent writes a proposals file the Hub parses —
rejected: a second result channel beside the event channel, more failure modes, and the
Findings Report store writes exactly once at terminal transition (no append path);
(b) Hub derives tasks from the narrative by parsing headings — rejected outright:
deterministic backend derivation of wiki-content judgment violates Principle V and
FR-007's explicit "not derived by fixed backend rules".

## R4: Enforcing "no execution without authorization" (FR-008/SC-005)

**Decision**: Authorization is a **dispatch precondition**, not a runtime check: the
only spawn site for remediation execution is `RemediationRunCoordinator.
TryStartNextAsync`, which dequeues exclusively `Authorized` rows and transitions them
to `Executing` under the slot lock before launching. Enforced by a `Grimoire.ArchTests`
allow-listed-caller rule (remediation namespace reaches `IAgentProcessLauncher` only
from the coordinator) with a Red/Green probe, plus hermetic integration tests driving
every non-`Authorized` state.

**Rationale**: Makes SC-005 structurally true — an unauthorized execution would require
a nonexistent code path — rather than dependent on a check racing live agent turns.
Details and rejected in-loop/daemon alternatives: ADR-018.

## R5: Withdrawal race resolution (FR-016, spec edge case)

**Decision**: `Authorized → Executing` (coordinator, under slot lock) and
`Authorized → Proposed` (withdrawal endpoint) are both compare-and-swap transitions on
the persisted row; first commit wins, the loser is rejected and the caller sees the
actual state. Once `Executing`, only terminal states are reachable.

**Rationale**: A single persisted arbiter gives the deterministic resolution the spec
edge case demands, and the board can always show which side won. Mirrors the
first-transition-wins idempotence already used by `LintRunState.TryTransitionTo` and
ingest's `FinishRunAsync`.

## R6: Task messaging and attached context (FR-011/FR-012/FR-014)

**Decision**: One append-only Markdown **Remediation Task Record** per task
(ADR-014's Conversation Record shape, one level down), at
`RemediationTaskRecordPathFor(taskId)` under a new `RemediationTasksDir` (ADR-009
registration). Attached context and each human⇄agent exchange are appended sections;
the record is the source of prior-message context for message turns. Message turns are
bounded single exchanges reusing the Query-turn invocation shape (ADR-011).

**Rationale**: ADR-014 chose record-as-context precisely so audit trail and agent
context cannot diverge — the same property FR-014 needs (history visible after
terminal outcome). Human-attached context reaches the execution run as the ADR-007
per-run user-prompt override, an existing mechanism.

**Alternatives considered**: storing messages in SQLite operational state — rejected:
message history is durable domain-adjacent content (survives, is user-facing,
git-trackable), matching ADR-003's split where SQLite holds only operational
coordination state.

## R7: Write scope of remediation execution

**Decision**: Reuse ADR-016's `WriteMode.FrontmatterOnly` policy unchanged (same
`data/agents/lint/policy.json`, same `SharedFileWriteGuard` checks, ADR-017's
`log.md`/`index.md` shape rules unchanged). No new write mode. Proposals are not
pre-filtered by scope; an over-scope fix fails at the guard with a recorded denial
reason, surfaced as the task's failure reason (FR-005/SC-007).

**Rationale**: Broadening lint-driven write scope is a separately-reasoned safety
decision (ADR-016 exists because of it) and is out of feature scope; FR-007 imposes no
proposal-scope restriction, so filtering proposals in the backend would itself be a
Principle V violation.

## R8: Agent process identity for execution and message turns

**Decision**: Reuse the `Grimoire.LintAgent` binary with additional invocation modes
(lint run / remediation execution / message turn), spawned per unit of work via the
existing `IAgentProcessLauncher` port (ADR-002/ADR-010).

**Rationale**: Keeps ADR-002's one-process-per-domain intent; a fourth standalone agent
would duplicate instruction-loading, policy, and guardrail wiring with no boundary
gain. The mode is CLI/request surface, not a new architectural boundary (ADR-018
records the accepted trade-off).

## R9: Board projection for mixed entry kinds (FR-001/FR-006/FR-015)

**Decision**: Extend the board response to carry lint-run and remediation-task entries
as additional, explicitly-typed entry kinds alongside the existing ingest
`KanbanBoardProjection` rows; the frontend renders distinct card components
(`LintRunCard`/`RemediationTaskCard` or a kind-discriminated `TaskCard`) for FR-006
distinguishability. Existing ingest rows, columns, and the `SubmissionForm` are
untouched.

**Rationale**: FR-015/SC-008 make "don't touch ingest" the hard constraint; additive
typed entries keep the existing projection store and its tests unchanged while giving
the board one composite response plus three lifecycle streams (R1).

## R10: Evaluation strategy for the two agent-judgment criteria

**Decision**: Two eval suites in `Grimoire.AgentEvals` via the ADR-012 recorded-replay
harness: (a) proposal relevance — sampled lint runs over wiki fixtures with seeded
findings, scored against a human-adjudicated golden set, threshold ≥ 90% (SC-006);
(b) re-verification correctness — fixtures pairing a recorded proposal with wiki
states that changed vs. didn't change after proposal, asserting the agent chooses
apply vs. not-applicable correctly, threshold ≥ 90% (FR-018).

**Rationale**: Constitution Principle II requires agent-judgment criteria to be
evaluation-thresholded, never reimplemented deterministically; ADR-012's replay
mechanism is the established, CI-safe way to run them.

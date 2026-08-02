# Quickstart: Unified Task Board for Lint and Agentic Remediation

Validation scenarios proving the feature end-to-end. Contracts:
[contracts/remediation-task-api.md](./contracts/remediation-task-api.md),
[contracts/lint-board-api.md](./contracts/lint-board-api.md),
[contracts/remediation-lifecycle-events.md](./contracts/remediation-lifecycle-events.md).
Entity shapes and the task state machine: [data-model.md](./data-model.md).

## Prerequisites

- .NET 10 SDK; Node.js + npm for the frontend.
- Hub running: `dotnet run --project backend/src/Grimoire.Hub`; frontend dev
  server: `npm run dev` in `frontend/` (board at the root route `/`).
- A wiki fixture with seeded, actionable lint defects — reuse the 013 fixture
  (pages missing tags/confidence, an orphan page, a contradiction) so a live
  run reliably yields at least two proposed remediation actions.
- `ANTHROPIC_API_KEY` for live agent runs and evals (hermetic tests need none).
- Optional for the observability check: local Aspire Dashboard (ADR-005),
  `docker run --rm -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard`.

## Run the test suites (hermetic, primary verification)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests            # RemediationTasks containment + authorization-dispatch rule, Red/Green probed
dotnet test tests/Grimoire.Domain.UnitTests --filter FullyQualifiedName~RemediationActionTask
                                                # state-machine invariants (valid edges only, CAS semantics)
dotnet test tests/Grimoire.IntegrationTests     # trigger precondition, coordinator FIFO, endpoints, board response,
                                                # hub broadcasts, withdrawal race, restart reconciliation, observability contracts
cd ../frontend
npm test                                        # board card kinds, trigger control, reason-message mapping, lifecycle stream clients
```

Expected: all pass; no backend test requires an API key or network (fake
`IAgentProcessLauncher` + fake clock), no frontend test requires the Hub
(mocked API clients).

Agent-judgment evals (recorded-replay per ADR-012; live re-record needs the
API key):

```bash
cd backend
dotnet test tests/Grimoire.AgentEvals           # proposal relevance ≥ 90% (SC-006) + re-verification correctness ≥ 90% (FR-018)
```

## Scenario 1 — See lint status on the board, live (US1 / SC-001, SC-002)

1. Open the board (`/`) with no lint run ever triggered: it shows "no lint
   activity" plus the trigger control, alongside existing ingest cards.
2. Trigger a lint run **from the `/lint` page** (not the board), keep the
   board visible.

Expected: the run appears on the board as a running lint-run card without a
reload — a run triggered elsewhere shows up exactly as a board-triggered one
(spec edge case); on completion the card flips to completed; force a failure
(e.g. kill the agent process) and the card shows failed **with the reason**,
consistent with ingest failure display (FR-005).

## Scenario 2 — Trigger from the board, and blocked triggers explain why (US2 / FR-004, SC-004)

1. From the board, with no active run and no unresolved remediation tasks,
   click the lint trigger: accepted (202), card appears running — one action,
   no navigation (SC-003).
2. While it runs, trigger again.
3. After a run that produced proposals (Scenario 3), leave one task
   unresolved and trigger again.

Expected: step 2 shows the human-readable "run already active" message
(`reason: lint_run_active`); step 3 shows the "unresolved remediation tasks"
message (`reason: unresolved_remediation_tasks`) with links to the blocking
cards; neither ever silently does nothing.

## Scenario 3 — Proposals appear as cards when the run completes (US3 / FR-006, FR-007)

1. Trigger a lint run against the seeded fixture; watch the board.

Expected: the moment the run's card shows completed, one remediation task card
per agent-proposed action is already on the board (completed implies
assessment done — never a completed run with cards trickling in afterwards);
each card shows the agent's verbatim title, is visually distinct from ingest
cards and from the lint-run card (FR-006), and is decidable independently. A
run with nothing actionable completes with zero task cards.

## Scenario 4 — Authorize, sequential execution, outcomes; dismiss; withdraw (US4 / FR-008..FR-010, FR-013, FR-016..FR-018, SC-005, SC-007)

1. From Scenario 3, authorize two task cards in quick succession; dismiss a
   third.
2. While the second authorized task is still waiting, withdraw its
   authorization.
3. Re-authorize it; before it executes, edit the target page so the proposal
   no longer applies, then let execution proceed.
4. Try to withdraw a task the instant it starts executing.

Expected: only one task executes at a time, in authorization order — the
waiting card is visibly distinct (queue position) from the executing one
(FR-017); the executing card shows live progress and then completed (FR-013);
the dismissed card resolves with **no** agent action or wiki change; the
withdrawal in step 2 returns the card to proposed; step 3 ends
`not_applicable` **with the agent's reason** on the card and no wiki write
(FR-018); step 4 resolves deterministically — either withdrawal wins (card
back to proposed) or execution wins (409 `execution_already_started`, card
runs to a terminal outcome) — and the board shows which (spec edge case).
Throughout: no wiki-modifying change ever happens for a task that was not
explicitly authorized (SC-005), and every failed/not-applicable card shows a
clear reason (SC-007).

## Scenario 5 — Attach context and message the agent about a task (US5 / FR-011, FR-012, FR-014)

1. On a proposed task card, attach an instruction (e.g. "use the index.md tag
   taxonomy").
2. Send the agent a message asking about the proposal; wait for the reply.
3. Authorize the task; after it completes, reopen the card.

Expected: the attached context is visible on the task and demonstrably reaches
the agent (the executed change follows the instruction); the agent's reply
appears in the same task's message thread; after the terminal outcome the full
message history and attached context remain readable (FR-014). Attaching
context or messaging a non-proposed task is rejected with the actual state
named, never silently.

## Ingest regression (FR-015 / SC-008)

Run the existing ingest integration suite and frontend board tests unmodified:

```bash
cd backend && dotnet test tests/Grimoire.IntegrationTests --filter FullyQualifiedName~Ingest
cd ../frontend && npm test
```

Expected: all pass with no fixture or assertion changes; on the live board,
ingest submission, cards, live updates, and detail views behave exactly as
before.

## Structural guarantee (SC-005)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests --filter FullyQualifiedName~RemediationExecutionDispatch
```

Expected: passes — the remediation namespace reaches `IAgentProcessLauncher`
only from `RemediationRunCoordinator` (the sole, authorization-gated spawn
site, ADR-018); the Red/Green probe commit documents the rule going red on a
scratch violation and back to green.

## Observability check

With the Aspire Dashboard running (ADR-005): a completed run-plus-remediation
cycle shows `hub.lint.propose_remediation_tasks` (child of
`hub.lint.run_supervision`), then `hub.remediation.authorize`,
`hub.remediation.execution_dispatch` → `hub.remediation.run_supervision` →
`hub.remediation.re_verify`, and `hub.remediation.message_turn` for Scenario 5
— correlated by `task_id`. The log stream carries
`hub.lint.remediation_task_proposed`, `hub.remediation.task_authorized` /
`task_dismissed` / `authorization_withdrawn`, `execution_started` /
`execution_completed` (with `outcome` and `reason`), and `message_recorded`.
Counters `wiki.lint.remediation_tasks_proposed_total`,
`wiki.remediation.tasks_authorized_total` / `tasks_dismissed_total` /
`tasks_withdrawn_total`, and `wiki.remediation.tasks_executed_total{outcome=…}`
increment per Scenario 4's actions; `wiki.remediation.queue_depth` rises to 1
while the second task waits and returns to 0.

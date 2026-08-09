# Feature Specification: Unified Task Board for Lint and Agentic Remediation

**Feature Branch**: `015-lint-board-parity`

**Created**: 2026-07-31

**Status**: Implemented — completed 2026-08-01

**Input**: User description: "use <https://github.com/wmalgadey/grimoire/issues/40>" — GitHub issue #40, "Lint and ingest both create tasks — both should be triggerable/visible on the task board." Scope was subsequently expanded during clarification: lint findings become agent-proposed, individually actionable task cards on the board, authorized by a human before the agent executes them.

## Clarifications

### Session 2026-08-01

- Q: Can a user cancel a remediation action task after authorizing it? → A: Yes, until execution starts — authorization can be withdrawn while the task is still waiting; once the agent begins executing, it runs to completion or failure.
- Q: When multiple remediation action tasks are authorized, how does the agent execute them? → A: Sequentially, one at a time — authorized tasks queue up, and later ones see earlier ones' wiki changes.
- Q: Is the agent's findings assessment part of the lint run itself, or a separate phase after the run completes? → A: Part of the run — the lint run only shows completed once assessment is done and every proposed remediation task card is on the board.
- Q: What happens when an authorized remediation action no longer applies because the underlying wiki content changed after it was proposed? → A: The agent re-verifies at execution time — if the proposal is moot or stale, it resolves the task with a clear "no longer applicable" outcome instead of applying blindly.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See lint activity on the shared task board (Priority: P1)

As someone using the wiki harness, when I look at the task board, I want to see whether a lint run is in progress, completed, or failed, so that I don't have to leave the board and check a separate page to know what the harness is doing.

**Why this priority**: This is the core visibility gap the issue describes: the board is the single place users look for harness activity, but lint activity is currently invisible there. Without this, nothing else in this feature has value.

**Independent Test**: Trigger a lint run (by any currently-available means) and confirm its status appears on the task board and updates as the run progresses to completion or failure — without navigating to the dedicated lint page.

**Acceptance Scenarios**:

1. **Given** no lint run has ever been triggered, **When** a user opens the task board, **Then** the board shows that no lint activity is in progress and offers a way to start one.
2. **Given** a lint run is in progress, **When** a user opens or is already viewing the task board, **Then** the board shows the lint run as currently running, distinguishable from ingest tasks.
3. **Given** a lint run that was in progress finishes successfully, **When** the user is viewing the task board, **Then** the board reflects the completed state without the user manually reloading the page.
4. **Given** a lint run that was in progress fails, **When** the user is viewing the task board, **Then** the board shows the failed state along with the reason for the failure.

---

### User Story 2 - Trigger a lint run from the task board (Priority: P2)

As someone using the wiki harness, when I'm looking at the task board, I want to start a lint run right there, so that I have one place to both see and start harness work instead of switching to a separate page.

**Why this priority**: Visibility (User Story 1) delivers value on its own; triggering is the second half of parity with ingest, which already lets users submit new work directly from the board.

**Independent Test**: From the task board, without navigating elsewhere, start a lint run and confirm it is accepted and begins appearing as in-progress on the board.

**Acceptance Scenarios**:

1. **Given** no lint run is currently active and no remediation action task from a prior run is still unresolved, **When** a user starts a lint run from the task board, **Then** the request is accepted and the board shows the run as in progress.
2. **Given** a lint run is currently active, **When** a user attempts to start another lint run from the task board, **Then** the board clearly communicates that a run is already active and does not silently fail or start a conflicting run.

---

### User Story 3 - Review agent-proposed remediation actions from a lint run (Priority: P3)

As someone using the wiki harness, when a lint run finds something worth fixing, I want the agent to propose each fix as its own task on the board, so that I can see exactly what the agent thinks needs doing, one item at a time, instead of reading through one long report.

**Why this priority**: This turns lint from a passive report into actionable work items, which is the main new value of this feature beyond basic visibility. It depends on User Stories 1–2 (a lint run must be visible and triggerable first) but is independently verifiable once a run produces findings.

**Independent Test**: Trigger a lint run that surfaces at least one issue worth fixing, and confirm that by the time the run shows as completed, a separate task card appears on the board for each proposed remediation action, distinct from the lint run's own card.

**Acceptance Scenarios**:

1. **Given** a lint run identifies one or more issues worth fixing, **When** the run reaches its completed state, **Then** one task card per proposed remediation action is already on the board, each describing what the agent proposes to do — findings assessment is part of the run, so completed means all proposals have been surfaced.
2. **Given** a lint run completes and finds nothing worth acting on, **When** the user views the board, **Then** the lint run shows as completed and no remediation action task cards are created.
3. **Given** several remediation action task cards exist from the same lint run, **When** the user views the board, **Then** each card can be reviewed and decided on independently of the others.

---

### User Story 4 - Authorize a proposed remediation action (Priority: P4)

As someone using the wiki harness, when I agree with a remediation action the agent has proposed, I want to explicitly authorize it, so that the agent only ever changes the wiki with my sign-off, never on its own initiative.

**Why this priority**: This is what turns a proposal into applied work, and it's the feature's key safety property (no unsupervised wiki writes). It depends on User Story 3 producing proposed actions to authorize.

**Independent Test**: Given a proposed remediation action task card, authorize it (e.g., by moving it to a "ready" state) and confirm the agent subsequently carries out the action and the card's status reflects the outcome.

**Acceptance Scenarios**:

1. **Given** a proposed remediation action task card, **When** a user authorizes it, **Then** the agent begins carrying out the action and the board reflects it as in progress.
2. **Given** an authorized remediation action that the agent successfully carries out, **When** the user views the board, **Then** the card shows as completed.
3. **Given** an authorized remediation action the agent cannot carry out, **When** the user views the board, **Then** the card shows as failed along with the reason.
4. **Given** a proposed remediation action task card the user does not agree with, **When** the user dismisses it instead of authorizing it, **Then** the agent never acts on it and the card is resolved without a wiki change.
5. **Given** the agent has not yet had any proposed action authorized, **Then** it MUST NOT make any wiki-modifying change on its own.

---

### User Story 5 - Give the agent more context on a specific proposed action (Priority: P5)

As someone using the wiki harness, when a proposed remediation action is missing context or I want to steer it before authorizing, I want to add information to it or ask the agent about it, so that what eventually gets authorized and applied is actually correct.

**Why this priority**: This is a refinement on top of User Stories 3–4 — the workflow already functions (propose, authorize/dismiss) without it, but it materially improves the quality of what gets authorized.

**Independent Test**: Given a proposed remediation action task card, attach additional information to it and separately send the agent a message about it, then confirm both the attached information and the agent's response are visible in the context of that task.

**Acceptance Scenarios**:

1. **Given** a proposed remediation action task card, **When** a user attaches additional information or instructions to it, **Then** that information is visibly associated with the task and available to the agent before the user authorizes it.
2. **Given** a proposed remediation action task card, **When** a user sends the agent a message about it, **Then** the agent's response is visible to the user in the context of that same task.
3. **Given** a remediation action task that has already been authorized or completed, **When** a user views its message history, **Then** prior messages remain visible for reference.

---

### Edge Cases

- What happens if a user tries to trigger a lint run from the board at the exact moment an existing run finishes? The system must resolve this without leaving the user uncertain whether their request was accepted.
- What happens if the user's connection drops and reconnects while a lint run or a remediation action is in progress? The board must recover the correct current state rather than showing stale information indefinitely.
- What happens if a lint run was triggered from the existing dedicated lint page (if that page continues to exist) while the user is viewing the task board? The board must reflect that run, and any resulting remediation action tasks, just as if it had been triggered from the board itself.
- What happens when the board is opened for the first time after this feature ships and a lint run happened to be active before the change? The board must show its current state correctly, not treat it as never having started.
- What happens if two proposed remediation actions from the same lint run touch overlapping wiki content? Each must still be reviewable and decidable independently; the system must not silently merge or drop either one. If both are authorized, sequential execution (FR-017) plus execution-time re-verification (FR-018) resolves the overlap: the later task is re-checked against the content as the earlier one left it.
- What happens if a user tries to start a new lint run while a prior run's remediation action tasks are still proposed, in progress, or otherwise unresolved? The request must be blocked with a clear explanation, not silently ignored (see User Story 2).
- What happens if a user withdraws authorization at the exact moment the agent begins executing the task? The system must resolve this deterministically — either the withdrawal wins (task returns to proposed, no wiki change) or the execution wins (task runs to a terminal outcome) — and the board must clearly show which happened.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The task board MUST display the current status of lint activity (not started / in progress / completed / failed) alongside ingest tasks.
- **FR-002**: The task board MUST allow a user to start a lint run directly from the board, without navigating to a separate page.
- **FR-003**: The task board MUST update the displayed status of lint runs and remediation action tasks as they change, without requiring the user to manually reload the page.
- **FR-004**: The system MUST block starting a new lint run while a lint run is already active, or while any remediation action task from a prior run has not reached a terminal outcome (completed, failed, no longer applicable, or dismissed) — and MUST clearly tell the user why, rather than silently ignoring the request.
- **FR-005**: When a lint run or a remediation action task fails, the board MUST show the failure reason to the user, consistent with how ingest task failures are shown.
- **FR-006**: A lint run entry and its remediation action task entries MUST be visually distinguishable from ingest task entries and from each other, so a user can tell at a glance what kind of activity a card represents.
- **FR-007**: When a lint run identifies issues worth acting on, the agent itself MUST assess the findings and propose one remediation action task per identified action, each surfaced as its own board entry; this judgment MUST be exercised by the agent, not derived by fixed backend rules. Findings assessment is part of the lint run: the run MUST NOT show as completed until assessment is done and every resulting remediation action task is on the board.
- **FR-008**: The agent MUST NOT make any wiki-modifying change as part of a remediation action unless a human has explicitly authorized that specific action task.
- **FR-009**: The board MUST let a human authorize a proposed remediation action task, after which the agent carries it out.
- **FR-010**: The board MUST let a human dismiss a proposed remediation action task instead of authorizing it, resolving it without the agent acting on it.
- **FR-011**: The board MUST let a human attach additional information or instructions to a proposed remediation action task before authorizing it, and that information MUST be available to the agent.
- **FR-012**: The board MUST let a human send the agent a message about a specific proposed remediation action task and see the agent's response in the context of that same task.
- **FR-013**: Once a remediation action task is authorized, the board MUST show its progress (in progress / completed / failed) the same way other harness activity is shown.
- **FR-014**: Message history and attached information on a remediation action task MUST remain visible after the task reaches a terminal outcome, for later reference.
- **FR-015**: The task board MUST remain fully functional for ingest tasks exactly as it is today; adding lint activity and remediation action tasks MUST NOT change existing ingest task behavior.
- **FR-016**: The board MUST let a human withdraw authorization from a remediation action task while it is still waiting for the agent to begin executing it, returning it to the proposed state; once execution has started, the task runs to its terminal outcome and cannot be cancelled.
- **FR-017**: Authorized remediation action tasks MUST be executed sequentially, one at a time, in the order they were authorized; a task waiting behind another MUST be visibly distinguishable on the board from one the agent is actively executing.
- **FR-018**: Before applying an authorized remediation action, the agent MUST re-verify that the proposal still applies to the current wiki content; if it has become moot or stale, the agent MUST resolve the task with a clear "no longer applicable" outcome — visible on the board with the reason — instead of applying the change blindly. This re-verification judgment is exercised by the agent, not by fixed backend rules.

### Key Entities

- **Task Board Entry**: A card on the shared task board representing one unit of harness activity — an ingest task, a lint run, or a remediation action task — showing its current state, when it last changed, and a way to see more detail.
- **Lint Run**: An execution of the wiki health check, including the agent's assessment of its findings. Has a status that moves through in-progress to a terminal outcome (completed or failed), a start time, and, on failure, a failure reason. The run reaches completed only after the findings assessment is done and any resulting remediation action tasks have been created on the board.
- **Remediation Action Task**: A single, agent-proposed fix arising from a lint run's findings. Has a description of what it proposes to do, a state that moves from proposed, through optional human-added context and agent discussion, to authorized, and then to a terminal outcome (completed, failed, no longer applicable, or dismissed without ever being authorized). An authorized task may be returned to proposed (authorization withdrawn) at any point before the agent begins executing it; once execution starts, only completed, failed, or no longer applicable remain reachable. "No longer applicable" means the agent re-verified the proposal at execution time and found the underlying content had changed such that the action is moot; it involves no wiki change.
- **Task Message**: A single exchange between a human and the agent, scoped to one remediation action task, forming that task's message history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of lint runs, however triggered, are visible on the task board within the same time it takes an ingest task's status change to appear there today.
- **SC-002**: 100% of lint run and remediation action task state changes are reflected on the task board without the user needing to manually reload the page.
- **SC-003**: A user can go from viewing the task board to having started a lint run in a single action, with no more navigation steps than starting an ingest submission requires today.
- **SC-004**: 100% of lint-trigger attempts blocked because a run or a prior run's remediation action task is still active clearly tell the user why, instead of appearing to do nothing.
- **SC-005**: 100% of wiki-modifying remediation actions the agent carries out correspond to a remediation action task a human explicitly authorized — none are applied without prior authorization.
- **SC-006**: ≥ 90% of sampled agent-proposed remediation action tasks are judged by a reviewing user to be a relevant, actionable response to the finding that produced them (not irrelevant, duplicate, or nonsensical).
- **SC-007**: 100% of remediation action tasks that fail to apply show a clear reason to the user.
- **SC-008**: 100% of existing ingest task board behavior (display, live updates, triggering) continues to work unchanged after this feature ships.

## Assumptions

- The existing dedicated lint page may continue to exist alongside this feature (e.g., for a detailed findings report view); this spec requires only that lint visibility, triggering, and the remediation action workflow also be available wherever the task board is shown. Whether the dedicated page is removed, kept, or changed is not decided here.
- The task board today is shown in exactly one place in the product (the main board view); "wherever the board is shown" refers to that view and any future views that render the same shared board.
- The task board has a single shared view for all users (no per-user filtering); this feature does not introduce per-user visibility scoping for lint activity or remediation action tasks.
- "Authorize" is described in user-facing terms (e.g., moving a card to a "ready" state); the exact interaction mechanism (drag-and-drop vs. a button vs. something else) is a design/implementation decision, not fixed by this spec.
- A remediation action task's terminal "dismissed" outcome does not itself change the wiki and does not require the agent's involvement to resolve.
- Findings that don't warrant a concrete action (purely informational observations, if any) do not require a remediation action task to be created; only findings the agent judges actionable produce one.

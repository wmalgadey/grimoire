# Feature Specification: Task Visibility & Recovery Improvements

**Feature Branch**: `023-task-ui-improvements`

**Created**: 2026-08-13

**Status**: Draft

**Input**: User description: "die anzeige in den tasks soll verbessert werden

1. im detail des tasks wird die source als pfad angegeben. hier soll ein link angezeigt werden, mit dem ich die original source im browser anzeigen kann
2. der task im ui hat nur eine uid. ich möchte sehen, wofür der task steht, anstatt einer uid
3. die Status-Werte in denen der Tasks war sollen als "Pfad" im Task-Detail sichtbar sein, damit wir erkennen können, wo der Task gestoppt wurde im Fehlerfall.
4. Ich habe einen zweiten Tasks gescheduled, dieser wurde nach 60 Sekunden beendet (Ingest failed: Agent run showed no liveness for 60 seconds and was terminated.). Ein Task soll nicht gestoppt werden, wenn überhaupt soll er neu "aktiviert" werden
5. im board steht im task der status, das ist unnötig, der Status steht ja bereits im spaltentitel.
6. ich möchte einen failed task erneut starten können"

## Clarifications

### Session 2026-08-13

- Q: Should the liveness-interruption and reactivation events appear only inside a task's status-history list, or should they also become new columns/stages on the board itself? → A: History-list entries only — no new board columns; the board keeps its existing status set.
- Q: When the system automatically retries a liveness-interrupted task, should each retry attempt happen immediately after the timeout is detected, or after an increasing wait (backoff) before the next attempt? → A: Increasing backoff between attempts.
- Q: Should these task-visibility and recovery improvements also apply to remediation tasks, or are they scoped to ingest tasks only? → A: Ingest tasks only; remediation tasks are out of scope for this feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See status history to diagnose a failure (Priority: P1)

As someone reviewing a task, I want to see every status the task passed through, in order, so that when a task fails I can immediately tell at which stage it stopped instead of only seeing the final "failed" state.

**Why this priority**: Without this, diagnosing why and where a task stopped requires guesswork or digging through server logs. This is the direct trigger for the reported liveness-timeout incident and is foundational for making failures actionable.

**Independent Test**: Open the detail view of any task (in progress, completed, or failed) and confirm the ordered sequence of statuses it has held is visible, with the point of failure clearly identifiable for failed tasks.

**Acceptance Scenarios**:

1. **Given** a task that moved through several statuses before failing, **When** the user opens the task detail view, **Then** the full ordered sequence of statuses is displayed, ending at the failing status.
2. **Given** a task that is still running, **When** the user opens the task detail view, **Then** the statuses reached so far are displayed, with the current status clearly distinguished as still in progress.
3. **Given** a task that completed successfully, **When** the user opens the task detail view, **Then** its full status sequence is displayed ending at the completed status.

---

### User Story 2 - Recover from an unresponsive agent run without losing the task (Priority: P1)

As someone who scheduled a task, I want an agent run that stops responding (no liveness signal) to be recovered rather than treated as a dead end, so that a transient stall doesn't force me to discover the failure and manually resubmit the work from scratch.

**Why this priority**: This is the exact failure the user hit ("Ingest failed: Agent run showed no liveness for 60 seconds and was terminated."). Today it is a silent, permanent dead end — the task is marked failed with no path back to progress, which directly wastes scheduled work.

**Independent Test**: Schedule a task whose agent run stops sending liveness signals; confirm the task is not left in a silently abandoned dead end, and that a clear recovery step is available and visible in its status history.

**Acceptance Scenarios**:

1. **Given** a task whose agent run has not shown liveness within the configured window, **When** the system detects this, **Then** the task's status history records the liveness interruption as a distinct, visible event, and the system automatically attempts to reactivate the task rather than jumping straight to a final "failed" state.
2. **Given** a task that was automatically reactivated after a liveness interruption, **When** the user views the task detail, **Then** both the interruption and the reactivation appear in the status history.
3. **Given** a task that exhausts its bounded number of automatic reactivation attempts, **When** the user views the task, **Then** it is clearly marked as finally failed and remains eligible for the manual restart described in User Story 5.

---

### User Story 3 - Identify a task at a glance (Priority: P2)

As someone scanning a list of tasks, I want to see what a task represents rather than only its unique identifier, so I can recognize the task I'm looking for without opening each one.

**Why this priority**: A bare UID forces the user to open every task to figure out what it is, which is a constant friction point across the board and detail views, though it does not block diagnosing or recovering from failures the way Stories 1 and 2 do.

**Independent Test**: Look at a task in the board view and in the task detail view and confirm a human-readable label describing what the task is for is shown, with the UID still available for reference but no longer the only identifying label.

**Acceptance Scenarios**:

1. **Given** a task in the board view, **When** the user looks at its card, **Then** a human-readable label is shown instead of, or clearly alongside, the raw UID.
2. **Given** a task in the detail view, **When** the user opens it, **Then** the same human-readable label is shown prominently, with the UID still discoverable for cases where the exact identifier is needed.

---

### User Story 4 - View the original source in the browser (Priority: P2)

As someone reviewing a task, I want to click a link in the task detail to open the original source content in my browser, so I don't have to manually locate and open a filesystem path myself.

**Why this priority**: This removes a manual, error-prone step (locating a path outside the tool) but is a convenience improvement rather than something blocking diagnosis or recovery.

**Independent Test**: Open the detail view of a task whose source is a web URL and confirm a working link opens it in a new browser view; open the detail view of a task whose source is a local file and confirm an equivalent working link is offered.

**Acceptance Scenarios**:

1. **Given** a task whose source is a web URL, **When** the user views the task detail, **Then** a clickable link opens that URL in the browser.
2. **Given** a task whose source is a local file, **When** the user views the task detail, **Then** a clickable link opens a browser-viewable rendering of that file's original content.
3. **Given** a task whose source reference cannot be resolved (e.g., the underlying file or URL is no longer available), **When** the user views the task detail, **Then** the system clearly indicates the source is unavailable rather than presenting a broken or misleading link.

---

### User Story 5 - Restart a failed task (Priority: P2)

As someone whose task has failed, I want to restart it directly from the UI, so I don't need command-line access to recover from a failure.

**Why this priority**: Directly requested recovery capability; important, but of lower urgency than making failures diagnosable (Story 1) and preventing avoidable failures in the first place (Story 2).

**Independent Test**: Find a task in the failed status and confirm a restart action is available; trigger it and confirm the task re-enters an active status and its prior status history (including the failure) remains visible.

**Acceptance Scenarios**:

1. **Given** a task with a failed status, **When** the user views its detail, **Then** a restart action is available.
2. **Given** the user triggers the restart action, **When** the task resumes, **Then** the task moves out of the failed status into an active status and the earlier failure remains visible in its status history.
3. **Given** a task that is not in a failed status, **When** the user views its detail, **Then** no restart action is offered.

---

### User Story 6 - Decluttered board cards (Priority: P3)

As someone scanning the board, I don't want to see a redundant status label on every task card, since the column the card sits in already tells me the status.

**Why this priority**: Purely cosmetic decluttering with no effect on functionality or diagnosability.

**Independent Test**: View the board and confirm task cards no longer show a separate status label, while the column headers still communicate status.

**Acceptance Scenarios**:

1. **Given** the board view, **When** the user looks at any task card, **Then** no status label is shown on the card itself.
2. **Given** the board view, **When** the user looks at a column, **Then** the column heading still clearly identifies the status of the tasks it contains.

---

### Edge Cases

- What happens when a task's source reference points to content that has since been moved, deleted, or is otherwise unreachable? (See User Story 4, Scenario 3 — must show "unavailable", not a broken link.)
- What happens when a task has no meaningful information available to derive a human-readable label from? The system MUST fall back to a clearly labeled default rather than showing an empty or confusing name.
- What happens when a user restarts a task that is concurrently being restarted or is no longer in a failed status (e.g., two browser tabs)? The system MUST prevent duplicate restarts of the same task and MUST reflect the task's true current status back to the user.
- What happens if a liveness-interrupted task fails to recover repeatedly? It MUST eventually settle into a clearly visible failed status (see User Story 2, Scenario 3) rather than looping indefinitely with no visible end state.
- What happens when a task's status history becomes very long (many transitions)? The full history MUST remain accessible in the task detail view.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display, in the task detail view, a clickable link that opens the task's original source content in the user's browser, for both web-URL sources and local-file sources. For local-file sources, the link MUST open a view of the file's content served by the system itself (not a direct local-filesystem reference), so the link works regardless of whether the user's browser runs on the same machine as the system.
- **FR-002**: System MUST indicate clearly when a task's source reference cannot be resolved, instead of showing a non-functional or misleading link.
- **FR-003**: System MUST display a human-readable label describing what each task represents, in every UI location where the task is currently identified only by its raw unique identifier (board cards and task detail). The label MUST be derived from a title extracted from the task's source content (e.g., a document heading or page title). When no such title can be extracted, the system MUST fall back to a clearly labeled default derived from the source reference (e.g., its filename or URL) rather than showing an empty or confusing label.
- **FR-004**: System MUST continue to make the task's raw unique identifier available (e.g., in the detail view or URL) for cases where the exact identifier is needed, even once the human-readable label is shown.
- **FR-005**: System MUST record every status a task passes through, in chronological order, as a persisted status history rather than only the current status.
- **FR-006**: System MUST display a task's full ordered status history in its task detail view, so the status at which a failed task stopped is identifiable.
- **FR-007**: When an agent run shows no liveness within the configured window, system MUST NOT leave the task in a silently abandoned, unrecoverable state; the interruption MUST be recorded as a distinct entry in the task's status history (visible only in the task detail view's history, not as a new board column) rather than presented only as an opaque final failure.
- **FR-008**: When a task is interrupted by a liveness timeout, system MUST automatically attempt to reactivate it, up to a bounded number of attempts, before treating the task as immediately and permanently failed. Successive automatic reactivation attempts MUST be spaced apart with an increasing wait (backoff) rather than being retried back-to-back. Only after the bounded number of automatic reactivation attempts is exhausted MUST the task be marked as finally failed (at which point it becomes eligible for the manual restart in FR-010).
- **FR-009**: Board view MUST NOT display a separate status label on individual task cards; the task's status MUST remain identifiable solely from its column placement.
- **FR-010**: System MUST allow a user to manually restart a task that is in a failed status, from the UI, without requiring command-line access.
- **FR-011**: System MUST NOT offer the restart action for tasks that are not currently in a failed status.
- **FR-012**: System MUST prevent a failed task from being restarted more than once concurrently (e.g., via duplicate submissions), and MUST reflect the task's true current status to the user afterward.
- **FR-013**: When a task is restarted, system MUST preserve its prior status history (including the earlier failure) alongside the statuses reached after the restart.

### Key Entities

- **Task**: A unit of scheduled or in-progress work (e.g., an ingest). Now carries a human-readable label in addition to its unique identifier, a source reference, and a full status history rather than only a current status.
- **Status Transition**: A single recorded entry in a task's status history — the status held and when the task entered it. Ordered entries together form the "path" the task took, including any liveness interruption and reactivation entries.
- **Source Reference**: A pointer to a task's originating content, either a web URL or a local file location, from which a browser-viewable link is derived.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of task detail views for tasks with a resolvable source reference show a working link that opens the original source content in the browser.
- **SC-002**: 100% of task detail views for tasks with an unresolvable source reference show a clear "unavailable" indication instead of a broken link.
- **SC-003**: 100% of tasks shown on the board or in the detail view display a human-readable label; the raw UID is no longer the only identifying text shown to the user.
- **SC-004**: 100% of task detail views display the task's complete ordered status history.
- **SC-005**: 100% of liveness-timeout interruptions are recorded as a distinct, visible status-history entry rather than an unexplained jump to a final failed state.
- **SC-006**: 100% of board task cards show no separate status label, while the task's status remains 100% identifiable from column placement alone.
- **SC-007**: A user can restart any failed task in a single UI action, without CLI access, and the task moves out of the failed status while its prior failure remains visible in its history.
- **SC-008**: 100% of restart attempts on a task that is not in a failed status are rejected, and 100% of concurrent duplicate restart attempts on the same task result in only one restart taking effect.

## Assumptions

- This feature is scoped to the ingest task type described in the report (the one exposing a source reference and the liveness-timeout failure). Remediation tasks are explicitly out of scope for this feature, even where they carry a similar status model; extending these improvements to remediation tasks would be a follow-up feature.
- Restarting a task re-uses the existing task record and identifier, appending to its status history, rather than creating a separate new task.
- The board's status/column set (received, converting, queued, running, completed, failed) is unchanged by this feature. Liveness-interruption and reactivation are recorded as status-history entries visible in the task detail view only; they do not become new board columns or otherwise alter the board's status model.
- Viewing the original source via the new link is read-only; this feature does not add editing capability for source content.
- No new user roles or permission levels are introduced; restart and source-viewing are available to whoever can already view the task today.
- The exact number of automatic reactivation attempts before a liveness-interrupted task is marked finally failed, and the exact backoff durations between attempts, are operational tuning values, not scope decisions; a small fixed bound on attempts (e.g., low single digits) with a growing wait between them is assumed and MAY be adjusted during planning without requiring a spec change.
- Content-extracted titles used for the human-readable task label reuse whatever title/heading is already produced as part of processing the source content; this feature does not introduce a new content-analysis capability solely for labeling.

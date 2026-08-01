# Data Model: Unified Task Board for Lint and Agentic Remediation

Entities from spec.md `## Key Entities`, refined with the decisions in `research.md`
(R1–R10) and ADR-018 (normative for the state machine and the authorization gate).
This feature adds one new persisted entity (`RemediationActionTask`), one new durable
file (the Remediation Task Record), one event-vocabulary extension (`proposedActions`),
and extends two existing shapes (Lint Run, board projection). Ingest entities are
untouched (FR-015/SC-008).

## RemediationActionTask *(operational entity, Hub-managed, persisted)*

One agent-proposed fix arising from a lint run's findings assessment (FR-007), owned by
the new `Grimoire.Hub.RemediationTasks` namespace (ADR-010 containment). The proposal
content is agent-authored and harness-opaque: the Hub never filters, merges, or
rewrites it (Principle V, research.md R3).

### Identity

| Field | Format | Notes |
|---|---|---|
| `task_id` | `$"{proposedAt:yyyy-MM-dd}-remediation-{Guid.NewGuid():N}"[..44]` | Hub-generated at materialization, mirroring the existing id shapes: `{date}-ingest-{guid}` (`backend/src/Grimoire.Hub/IngestSubmission/SubmissionService.cs`) and `{date}-lint-{guid}"[..40]` (`backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs`). The `remediation` infix makes the kind readable at a glance in logs, spans, and file names (FR-006's spirit applied to identifiers); truncation keeps ids uniform-length like lint's. |

### Fields (SQLite operational-state row)

Persisted in a new `remediation_tasks` table in the existing SQLite operational-state
store (`backend/src/Grimoire.Hub/OperationalState/OperationalStateRepository.cs`,
ADR-003), sibling to `operational_task_state` / `ingest_queue` / `hub_flags`:

| Column | Type | Notes |
|---|---|---|
| `task_id` | TEXT PRIMARY KEY | Identity above |
| `run_id` | TEXT NOT NULL | Originating Lint Run (`{date}-lint-{...}`) |
| `title` | TEXT NOT NULL | Agent-authored, verbatim from the `proposedActions` entry — never edited by the harness |
| `description` | TEXT NOT NULL | Agent-authored, verbatim |
| `target_path` | TEXT NULL | Optional agent-suggested wiki path; opaque hint, never validated or enforced by the Hub (scope enforcement stays at the write guard, ADR-016) |
| `state` | TEXT NOT NULL | `proposed \| authorized \| executing \| completed \| failed \| not_applicable \| dismissed` |
| `proposed_at` | TEXT NOT NULL (ISO-8601 `"O"`) | Materialization time |
| `authorized_at` | TEXT NULL (ISO-8601 `"O"`) | Set on `Proposed → Authorized`, cleared on withdrawal; **defines FIFO execution order** (FR-017, ADR-018) |
| `outcome_reason` | TEXT NULL | Mandatory for `failed` and `not_applicable` (FR-005/FR-018/SC-007); null otherwise |
| `updated_at` | TEXT NOT NULL (ISO-8601 `"O"`) | Last state change, drives board "when it last changed" |

Timestamps stored as `"O"` round-trip strings, matching the existing repository's
`accepted_at`/`updated_at` convention.

### State machine (ADR-018, normative)

```text
Proposed ──authorize──► Authorized ──dispatch──► Executing ──► Completed
   │  ▲                     │                        ├────────► Failed
   │  └────withdraw─────────┘                        └────────► NotApplicable
   └───dismiss───► Dismissed
```

Terminal states: `Completed`, `Failed`, `NotApplicable`, `Dismissed`. Every transition
attempt outside the edges below is rejected; terminal transitions are idempotent,
first-transition-wins — the same discipline as `LintRunState.TryTransitionTo`
(`backend/src/Grimoire.Hub/LintDispatch/LintRunState.cs`) and ingest's
`FinishRunAsync` slot check (`backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs`).

| From | To | Trigger | Semantics |
|---|---|---|---|
| *(none)* | `Proposed` | `LintRunCoordinator.FinishRunAsync` materializes one row per `proposedActions` entry, **before** the lint run's terminal transition is published (FR-007, research.md R3) | Creation, not a transition; an absent/empty `proposedActions` list creates no rows |
| `Proposed` | `Authorized` | Human — authorize endpoint (`RemediationTaskEndpoints`) (FR-009) | Stamps `authorized_at`; task enters the FIFO queue |
| `Proposed` | `Dismissed` | Human — dismiss endpoint (FR-010) | Terminal; no agent involvement, no wiki change (spec Assumptions) |
| `Authorized` | `Proposed` | Human — withdraw endpoint (FR-016) | Compare-and-swap; clears `authorized_at` (re-authorizing later gets a fresh queue position). Valid **only** while the task is still waiting — see race below |
| `Authorized` | `Executing` | `RemediationRunCoordinator.TryStartNextAsync`, under the slot lock, **before** the process is spawned (ADR-018) | Compare-and-swap; the **only** code path that spawns a remediation-execution process (FR-008/SC-005) |
| `Executing` | `Completed` | Agent terminal `completed` event | First-terminal-wins |
| `Executing` | `Failed` | Agent terminal `failed` event, **or** liveness-window expiry (ADR-008 watchdog is the sole failure authority), **or** process spawn failure | `outcome_reason` mandatory; a guard-denied over-scope write surfaces here with the recorded denial reason (research.md R7) |
| `Executing` | `NotApplicable` | Agent terminal `completed` event carrying the `notApplicable` outcome + reason (FR-018, ADR-018 event-vocabulary extension) | Agent re-verified at execution time and judged the proposal moot/stale; `outcome_reason` mandatory; involves no wiki change |

**Withdrawal race (FR-016, spec Edge Cases, research.md R5)**: `Authorized → Executing`
and `Authorized → Proposed` are both compare-and-swap transitions on the persisted row;
the first commit wins, the loser's request is rejected, and the caller sees the actual
resulting state — the board always shows which side won. `Executing` is entered inside
the coordinator's slot lock *before* the process is spawned, so a withdrawal can never
land on a task whose execution has started. Once `Executing`, only terminal states are
reachable (no cancellation, per clarification 2026-08-01).

**Authorization gate (FR-008/SC-005)**: execution dispatch is a *precondition*, not a
runtime check — `TryStartNextAsync` dequeues exclusively `Authorized` rows, ordered by
`authorized_at`, mirroring `IngestRunCoordinator.TryStartNextAsync`'s persisted-FIFO +
`SemaphoreSlim(1,1)` single-slot shape (research.md R2/R4). An unauthorized execution
would require a code path that does not exist; a `Grimoire.ArchTests` allow-listed-caller
rule with Red/Green probe enforces this structurally (ADR-018).

### Restart reconciliation (ADR-003/ADR-018)

On Hub startup, `RestartReconciler` (`backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs`)
treats remediation rows the same way it treats ingest tasks:

- A task found `Executing` with no live process is transitioned to `Failed` with a
  "Hub restarted while task was executing" reason (surfaced on the board per FR-005).
- `Authorized` tasks survive the restart still authorized, but the remediation execution
  queue starts **paused** until explicitly resumed — the same rule as
  `IngestRunCoordinator.InitializeAsync`'s `queue_paused` flag; the remediation queue
  uses its own flag so the two domains' pause lifecycles stay independent (research.md
  R2, FR-015).
- `Proposed` and terminal rows need no reconciliation.

## Lint Run *(extended, not new)*

The Lint Run entity from `specs/013-lint-agent/data-model.md` and its in-memory shape
`LintRunState` (`backend/src/Grimoire.Hub/LintDispatch/LintRunState.cs`:
`Running → Completed | Failed`, first-terminal-transition-wins) are **unchanged in
shape**. Two behavioral extensions:

1. **Proposal materialization gates completion (FR-007)**: findings assessment is part
   of the run. `LintRunCoordinator.FinishRunAsync` now parses `proposedActions` off the
   terminal event and materializes one `Proposed` task row per entry **before**
   `TryTransitionTo(Completed)` is published — so "completed" on the board already
   implies every proposed card exists (research.md R3). A run that proposes nothing
   completes with zero tasks (valid, spec US3 scenario 2).
2. **Trigger precondition (FR-004)**: `LintRunCoordinator` keeps its reject-immediately
   shape (no queue), extended with one additional precondition — a new run is rejected
   (409-shaped, with an explanatory reason per SC-004) while **either** a lint run is
   active **or** any `RemediationActionTask` from a prior run has not reached a terminal
   state (`Completed`, `Failed`, `NotApplicable`, or `Dismissed`). "Unresolved" =
   `Proposed`, `Authorized`, or `Executing`.

### Relationships

- 1 Lint Run : 0..N RemediationActionTasks (created atomically before the run's
  terminal transition; each task carries its originating `run_id`).
- 1 Lint Run : 1 Findings Report (unchanged from 013).
- 1 RemediationActionTask : 1 Remediation Task Record (below).

## Proposed Action *(event-level shape, ADR-008 extension)*

The Lint agent's terminal `completed` Agent Run Event
(`backend/src/Grimoire.Hub/AgentDispatch/AgentRunEvent.cs`) gains one optional field,
alongside the existing `deniedActions`/`createdPages` precedents:

| Field | Type | Notes |
|---|---|---|
| `proposedActions` | list of `{ title, description, targetPath? }` \| absent | One entry per remediation action the agent judges actionable. All three fields are agent-authored free text, harness-opaque; `targetPath` is an optional hint. Absent or empty ⇒ no tasks created. |

**Backward compatibility**: `AgentRunEventParser` is a tolerant NDJSON parser — unknown
fields never fail a run, and a missing `proposedActions` deserializes to `null`. Old
event streams (recorded fixtures, replays) parse unchanged; the extension is purely
additive (research.md R3, ADR-018).

The remediation-execution mode's terminal event additionally gains an optional outcome
field carrying `notApplicable` + reason (ADR-018) — consumed only by the
`Executing → NotApplicable` transition above. Everything else in the started /
heartbeat / activity / completed / failed vocabulary is reused verbatim.

## Remediation Task Record *(file, Hub-written, one per task)*

The durable, append-only record of one task's attached context and message history —
ADR-014's Conversation Record shape (`specs/011-query-conversations/data-model.md`)
applied one level down: one record per remediation task instead of per conversation.
Stored at `RemediationTaskRecordPathFor(taskId)` = `<RemediationTasksDir>/<taskId>.md`,
with `RemediationTasksDir` registered in `GrimoirePathOptions` /
`ResolvedGrimoirePaths` following the exact `ConversationsDir` /
`ConversationRecordPathFor` pattern
(`backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs`, ADR-009); outside
`wiki/`, git-ignored per ADR-003's domain/operational split. Written by the concrete
`RemediationTaskRecordStore` (persistence exemption, namespace-containment-tested).

Created at task materialization; earlier bytes are never modified. The record is the
source of prior-message context for message turns, so audit trail and agent context
cannot diverge (ADR-014 rationale, inherited via research.md R6). It survives terminal
outcomes: history remains readable after `Completed`/`Failed`/`NotApplicable`/`Dismissed`
(FR-014).

### Task-level facts (YAML frontmatter, written once at creation)

| Field | Type | Notes |
|---|---|---|
| `task_id` | string | Matches the SQLite row; names the file |
| `run_id` | string | Originating lint run |
| `proposed_at` | timestamp (ISO-8601) | |
| `record_format` | string | `grimoire-remediation-task/1` — parser/version handshake, mirroring `grimoire-conversation/1` |

The live state authority is the SQLite row (above) — the record's frontmatter carries
identity bookkeeping only, keeping the file strictly append-only.

### Appended entries

Each entry = one machine-readable bookkeeping comment + one human-readable section,
mirroring the Recorded Turn shape (`<!-- grimoire:turn ... -->` in ADR-014):

| Entry kind | Bookkeeping comment | Bookkeeping fields | Human-readable body |
|---|---|---|---|
| Proposal (exactly one, at creation) | `<!-- grimoire:proposal ... -->` | `title_chars`, `description_chars`, `target_path` (nullable) | The verbatim agent-authored title + description |
| Attached context (0..N, FR-011) | `<!-- grimoire:context ... -->` | `attached_at`, `text_chars` | The human-attached information/instructions, verbatim |
| Message exchange (0..N, FR-012) | `<!-- grimoire:message ... -->` | `sender` (`human` \| `agent`), `timestamp`, `text_chars` | One side of a human⇄agent exchange (a full exchange = one `human` entry + one `agent` entry) |
| Outcome (exactly one, at terminal transition) | `<!-- grimoire:outcome ... -->` | `state` (terminal), `reason` (nullable except `failed`/`not_applicable`), `completed_at` | Optional agent-authored summary, verbatim |

`*_chars` fields carry exact UTF-16 content lengths for injection-proof parsing —
the same mechanism as `prompt_chars`/`answer_chars` in the Conversation Record
(`specs/011-query-conversations/data-model.md`, research.md R2 there).

## Task Message *(one human⇄agent exchange, record-only)*

A single exchange between a human and the agent, scoped to one remediation action task
(spec Key Entities). Lives **only** in the Remediation Task Record — not in SQLite
(research.md R6: message history is durable, user-facing, domain-adjacent content,
matching ADR-003's split where SQLite holds only operational coordination state).

| Field | Type | Notes |
|---|---|---|
| `sender` | `human` \| `agent` | |
| `timestamp` | timestamp (ISO-8601) | |
| `text` | string | Verbatim; agent responses come off the message-turn terminal event |

A message turn is a bounded, read-only single exchange reusing the Query-turn
invocation shape (ADR-011): the agent receives the task's context (finding, proposal,
attached info, prior messages from the record) and returns its response on the terminal
event; the Hub appends both sides to the record (`hub.remediation.message_recorded`).

## Board Entry *(extended read model)*

The board response gains lint-run and remediation-task entries as additional,
**explicitly-typed entry kinds** alongside the existing ingest rows — the ingest
`KanbanBoardProjection` record
(`backend/src/Grimoire.Hub/IngestSubmission/KanbanBoardProjection.cs`:
`TaskId, Column, Title, Subtitle, UpdatedAt, FailureReason, TaskLink`) and its store
are untouched (FR-015/SC-008, research.md R9).

| Field | Type | Notes |
|---|---|---|
| `entryKind` | `ingest` \| `lint_run` \| `remediation_task` | Discriminator; drives distinct card rendering (FR-006) |
| `id` | string | `task_id` (ingest, remediation) or `run_id` (lint) |
| `state` | string | Kind-specific state, mapped below |
| `title` / `subtitle` | string / string? | Remediation: the agent-authored proposal title; subtitle links the originating run |
| `updatedAt` | timestamp | "When it last changed" (spec Key Entities) |
| `failureReason` | string? | Non-null for `failed` (all kinds) and for `not_applicable` (the agent's staleness reason, FR-018) — surfaced the same way ingest failures are (FR-005) |
| `queuePosition` | int? | Remediation only: 1-based FIFO position while `Authorized`-waiting (from the coordinator's `GetQueuePositionsAsync` analog); null otherwise |
| `detailLink` | string | Ingest: task artifact; remediation: task detail (record view); lint: findings report |

### State → board mapping

| Entry kind + state | Board presentation | Notes |
|---|---|---|
| lint: *(no run ever / none active)* | No run card; board offers the trigger control | US1 scenario 1 |
| lint: `Running` | In-progress, lint-styled | Distinguishable from ingest (FR-006) |
| lint: `Completed` | Done | Implies all proposed task cards already exist (FR-007) |
| lint: `Failed` | Failed + `failureReason` | FR-005 |
| remediation: `Proposed` | Needs-review card: authorize / dismiss / attach context / message actions | FR-009..FR-012 |
| remediation: `Authorized` (waiting) | In-progress column, **waiting** visual + `queuePosition` | Visibly distinct from executing (FR-017) |
| remediation: `Executing` | In-progress, active (live activity via `RemediationLifecycleHub`) | FR-013 |
| remediation: `Completed` | Done | |
| remediation: `Failed` | Failed + `failureReason` | FR-005/SC-007 |
| remediation: `NotApplicable` | Resolved-without-change + agent's reason | FR-018 |
| remediation: `Dismissed` | Resolved-without-change, human-dismissed | FR-010 |

Live updates arrive over the two new per-domain hubs (`LintLifecycleHub`,
`RemediationLifecycleHub`, research.md R1); the composite board REST response provides
initial-state recovery after reconnect (spec Edge Cases).

## Validation rules

| Rule | Enforced where | Requirement |
|---|---|---|
| Execution dispatch only from `Authorized`, only via `RemediationRunCoordinator.TryStartNextAsync` | Structural (ArchTests allow-listed caller) + state machine | FR-008/SC-005 |
| Withdrawal valid only from `Authorized`, and only until the `Executing` CAS commits | State machine CAS, persisted-row arbiter | FR-016 |
| Attach-context and message turns valid only while `Proposed` (context must be settled before authorization freezes what execution will see); reading history is always valid, including after terminal states | Endpoints | FR-011/FR-012/FR-014 |
| `outcome_reason` mandatory for `failed` and `not_applicable` | State machine | FR-005/FR-018/SC-007 |
| At most one task `Executing` at any time; order = `authorized_at` ascending | Coordinator slot lock + FIFO | FR-017 |
| New lint run rejected while a run is active or any task is unresolved, with reason | `LintRunCoordinator` precondition | FR-004/SC-004 |
| Proposal fields stored and displayed verbatim — never filtered, merged, or rewritten by the harness | Materialization code path (no transformation exists) | FR-007, Principle V |
| One `Proposed` row per `proposedActions` entry, all rows exist before the run's terminal publish | `FinishRunAsync` ordering | FR-007 |

## State transitions (end-to-end)

```text
POST /api/lint-runs (from board or /lint page — same endpoint)
  → LintRunCoordinator.TryStartAsync()
      active run OR unresolved remediation task → 409 + reason (FR-004)
      accepted → spawn Grimoire.LintAgent (lint mode) … terminal event
          → FinishRunAsync:
              parse proposedActions → create N Proposed rows + N task records
              → LintRunState.TryTransitionTo(terminal)   (first-transition-wins)
              → publish lint lifecycle (board shows completed, cards already present)

Per task:            [attach context / message turns while Proposed]
  Proposed ─dismiss─► Dismissed (terminal)
  Proposed ─authorize─► Authorized (authorized_at = now, FIFO)
  Authorized ─withdraw─► Proposed          ┐ CAS on the persisted row:
  Authorized ─TryStartNextAsync─► Executing ┘ first commit wins (R5)
      → spawn Grimoire.LintAgent (remediation-execution mode)
          → agent re-verifies proposal against current wiki (FR-018)
              stale → terminal notApplicable + reason  → NotApplicable
              valid → guarded frontmatter-only write (ADR-006/015/016)
                  → completed → Completed
                  → failed / guard-denied / liveness expiry → Failed + reason
      → terminal transition → TryStartNextAsync() advances the queue (FR-017)
```

## Retired / superseded entities

None. This feature adds entities and extends existing shapes additively; no entity
from features 002–014 is retired or changed in stored shape. The lint page's 1-second
polling is superseded *on the board* by push updates, but the REST endpoints it uses
remain for initial-state recovery (research.md R1).

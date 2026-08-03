# Data Model: Hub CLI Command Parity for Write Actions

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02, revised 2026-08-03

This feature introduces **no new persisted entities and no new states**. All persisted
data (remediation task rows, ingest queue rows, lint run state, query turns,
conversation records) is owned by existing features and mutated exclusively through
the same coordinators/services the HTTP endpoints use, invoked in-process (see
[research.md](./research.md) D1). New artifacts are CLI-side in-memory models, one
extracted service, and one lock file.

## New in-memory models (namespace `Grimoire.Hub.Cli`)

### HubCliCommand (catalog entry)

Single source of truth for the command surface (FR-010): drives `CommandApp`
registration, the `Program.cs` dispatch check, and (via Spectre's help) the root help
listing.

| Field | Type | Notes |
| --- | --- | --- |
| `Name` | string | Literal command name (`lint-run`, `remediation-authorize`, `remediation-dismiss`, `remediation-withdraw`, `ingest-retrigger`, `ingest-resume`, `query`, `submit-source`) |
| `Description` | string | One-line purpose shown in root help |
| `CommandType` | Type | The Spectre `AsyncCommand<TSettings>` class |

**Validation**: catalog names are unique; a parity test asserts help output lists every
catalog entry.

### Settings hierarchy (Spectre `CommandSettings`)

All commands resolve the data directory in-process, so **every settings class inherits
`HubPathSettings`** (one `[CommandOption]` per `PathSwitchCatalog.All` entry,
parity-tested 1:1 — research D4); binding flows through the same configuration
composition as the web host, preserving ADR-009 precedence.

| Settings class | Additional options/arguments | Used by |
| --- | --- | --- |
| `HubPathSettings` (base) | the ADR-009 path switches | all commands |
| `LintRunSettings` | — | `lint-run` |
| `RemediationTaskSettings` | `--task-id <id>` (required, non-empty) | the three remediation commands |
| `IngestRetriggerSettings` | `--task-id <id>` (required, non-empty) | `ingest-retrigger` |
| `IngestResumeSettings` | — | `ingest-resume` |
| `QuerySettings` | `<prompt>` (required argument, non-empty after trim, ≤ 8000 chars); `--conversation-id <id>` (optional, must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`); `--timeout <seconds>` (optional positive integer, default 300) | `query` |
| `SubmitSourceSettings` | `--path <path>` (required), `--source-kind <kind>` (optional, default `file`) | `submit-source` |

**Validation rules** (enforced by Spectre validation *before* any action — FR-009,
FR-018): missing/empty required values → usage error, exit 2, no side effect.

### CliExitCode

The exit-code convention (research D5), fixed by ADR-020:

`Success=0`, `OperationFailed=1`, `UsageError=2`, `NotFound=3`, `StateConflict=4`,
`WaitTimeout=5` (turn interrupted), `Cancelled=130` (turn interrupted).

### Conversation id generation

When `--conversation-id` is omitted: `{utcNow:yyyy-MM-dd}-conv-{Guid:N}` truncated to
40 chars — conforms to ADR-014's regex and the Hub's existing id conventions; printed
in the result header line so follow-up turns can target it.

## New/changed harness components (outside `Cli/`)

### RemediationTaskTransitionService (`Grimoire.Hub.RemediationTasks`) — NEW

Extraction of the three inline endpoint-handler transition flows, called by **both**
the endpoint handlers and the CLI commands (FR-005). Moves existing logic verbatim —
CAS transition, lifecycle publish, metrics, log events, record append (dismiss), eager
`TryStartNextAsync` (authorize) — and adds none. Result shape mirrors the handlers'
outcomes: `Ok(taskId, newState, authorizedAt?)` | `NotFound` |
`Conflict(reason, currentState)` with the existing reasons `task_not_proposed`,
`task_not_authorized`, `execution_already_started`.

### `lint.pid` lock file — NEW runtime location

| Aspect | Value |
| --- | --- |
| Path | under the data directory; registered in `GrimoirePathOptions`/`ResolvedGrimoirePaths` (ADR-009) |
| Semantics | exclusive OS file lock held by `LintRunCoordinator.TriggerAsync` for the duration of a lint run — both entry paths (HTTP and CLI) |
| Conflict mapping | holder conflict → existing `Busy`/`lint_run_active` outcome (cross-process US1-S2 detection) |
| Precedent | `SharedFileWriteGuard` per-target exclusive locks (ADR-015) |

### OperationalStateRepository hardening — AMEND

`busy_timeout` + WAL journal mode on connections, tolerating the clarified
no-global-guard concurrency between a running Hub and a CLI invocation (research D1b).
No schema change.

## Referenced existing entities (unchanged)

| Entity | Owner | States relevant to CLI messages |
| --- | --- | --- |
| Lint Run | `Grimoire.Hub.LintDispatch.LintRunState` | running/terminal; trigger conflicts: `lint_run_active` (in-process slot **or** `lint.pid` holder), `unresolved_remediation_tasks` |
| Remediation Task | `Grimoire.Hub.OperationalState` rows + `RemediationTaskStates` | `proposed → authorized → executing → completed/failed/not_applicable`; `proposed → dismissed`; `authorized → proposed` (withdraw); transitions per ADR-018, CAS-guarded |
| Ingest Task / Queue | `Grimoire.Hub.IngestDispatch` | queued (retriggerable) vs. running/completed/not-found; queue resume idempotent; paused-after-restart flag durable |
| Query Turn | `Grimoire.Hub.QueryDispatch.QueryTurnState` | `running → completed/failed/interrupted` (terminal); answer accumulated in-process and streamed by the CLI |
| Conversation | ADR-014 Conversation Record | one active turn per conversation (durable record + in-process guard); record appended at terminal transition |

## State transitions introduced

None. Every transition the CLI can cause is an existing transition executed by the
same coordinator/service code the HTTP path uses; the CLI only maps the outcome to a
message and exit code. (The `lint.pid` lock adds cross-process *detection* of an
existing conflict, not a new state.)

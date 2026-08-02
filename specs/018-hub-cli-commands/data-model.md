# Data Model: Hub CLI Command Parity for Write Actions

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02

This feature introduces **no new persisted entities and no new states**. All persisted
data (remediation task rows, ingest queue rows, lint run state, query turns,
conversation records) is owned by existing features and mutated exclusively through
the existing endpoints (see [research.md](./research.md) D1). What this feature adds
are in-memory CLI-side models.

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

| Settings class | Options/arguments | Used by |
| --- | --- | --- |
| `HubApiSettings` (base) | `--hub-url <url>` (optional; default: `GRIMOIRE_HUB_URL` env var, else `http://localhost:5255`) | all seven remote commands |
| `RemediationTaskSettings : HubApiSettings` | `--task-id <id>` (required, non-empty) | the three remediation commands |
| `IngestRetriggerSettings : HubApiSettings` | `--task-id <id>` (required, non-empty) | `ingest-retrigger` |
| `QuerySettings : HubApiSettings` | `<prompt>` (required argument, non-empty after trim, ≤ 8000 chars); `--conversation-id <id>` (optional, must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`); `--timeout <seconds>` (optional positive integer, default 300) | `query` |
| `HubPathSettings` (base) | one `[CommandOption]` per `PathSwitchCatalog.All` entry (parity-tested 1:1) | `submit-source` |
| `SubmitSourceSettings : HubPathSettings` | `--path <path>` (required), `--source-kind <kind>` (optional, default `file`) | `submit-source` |

**Validation rules** (enforced by Spectre validation *before* any HTTP call — FR-009,
FR-018): missing/empty required values → usage error, exit 2, no side effect.

### IHubApiClient port results

Typed result records mirroring the endpoints' response shapes (see
[contracts/cli-commands.md](./contracts/cli-commands.md) for the full mapping):

- `LintRunResult`: `Accepted(runId, status, triggeredAt)` | `Busy` |
  `Blocked(unresolvedTaskIds)`
- `RemediationTransitionResult`: `Ok(taskId, newState, authorizedAt?)` |
  `NotFound` | `Conflict(reason, currentState)`
- `IngestRetriggerResult`: `Ok(taskId)` | `NotFound` | `NotQueued(column)`
- `IngestResumeResult`: `Ok(queuedTasks)`
- `QuerySubmitResult`: `Accepted(turnId, conversationId)` | `Busy` |
  `ConversationActive` | `RecordUnreadable(reason)`
- `QueryTurnSnapshot`: `(turnId, conversationId, state, answer, failureReason)` —
  polled view of `GET /api/query-turns/{turnId}`
- `HubUnreachable(url, error)` — transport-level failure, common to all

### CliExitCode

The exit-code convention (research D5), fixed by ADR-019:

`Success=0`, `OperationFailed=1`, `UsageError=2`, `NotFound=3`, `StateConflict=4`,
`WaitTimeout=5`, `HubUnreachable=7`, `Cancelled=130`.

### Conversation id generation

When `--conversation-id` is omitted: `{utcNow:yyyy-MM-dd}-conv-{Guid:N}` truncated to
40 chars — conforms to ADR-014's regex and the Hub's existing id conventions; printed
in the result header line so follow-up turns can target it.

## Referenced existing entities (unchanged)

| Entity | Owner | States relevant to CLI messages |
| --- | --- | --- |
| Lint Run | `Grimoire.Hub.LintDispatch.LintRunState` | running/terminal; trigger conflicts: `lint_run_active`, `unresolved_remediation_tasks` |
| Remediation Task | `Grimoire.Hub.OperationalState` rows + `RemediationTaskStates` | `proposed → authorized → executing → completed/failed/not_applicable`; `proposed → dismissed`; `authorized → proposed` (withdraw); transitions per ADR-018, CAS-guarded |
| Ingest Task / Queue | `Grimoire.Hub.IngestDispatch` | queued (retriggerable) vs. running/completed/not-found; queue resume idempotent |
| Query Turn | `Grimoire.Hub.QueryDispatch.QueryTurnState` | `running → completed/failed/interrupted` (terminal); answer accumulated server-side |
| Conversation | ADR-014 Conversation Record | one active turn per conversation (409 guard); record appended at terminal transition |

## State transitions introduced

None. Every transition the CLI can cause is an existing transition triggered through
its existing endpoint; the CLI only maps the outcome to a message and exit code.

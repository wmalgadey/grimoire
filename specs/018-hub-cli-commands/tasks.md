# Tasks: Hub CLI Command Parity for Write Actions

**Input**: Design documents from `/specs/018-hub-cli-commands/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cli-commands.md — all present. ADR-020 is Accepted.

**Tests**: All success criteria are deterministic harness guarantees (100% tier); the spec defines no agent-judgment thresholds, so no evaluation tests are required. `plan.md ## Observability` declares **no new metrics, log events, or trace spans** (justified N/A) — the logging/trace contract derivation rules therefore produce no per-row tasks; the two derived obligations (telemetry flush implementation + flush test, D8) and the final completeness audit are tasked explicitly below.

**Organization**: Tasks are grouped by user story so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 (lint-run), US2 (remediation lifecycle), US3 (ingest recovery), US4 (query)

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Guard the ADR-020 boundaries before any feature code exists.

**⚠️ NON-NEGOTIABLE**: No feature implementation may begin until Phase 0 is complete.

- [x] T001 Write structural rule C9 (Spectre.Console containment) in `backend/tests/Grimoire.ArchTests/HubCliContainmentRuleTests.cs`: types referencing `Spectre.Console`/`Spectre.Console.Cli` MUST reside in namespace `Grimoire.Hub.Cli*` or the composition root (`Program.cs` dispatch gate). Red/Green probe: add a deliberately violating class in a non-Cli Hub namespace referencing Spectre, verify the test fails, delete it, verify the test passes. Commit message documents the probe result.
- [x] T002 Amend rule N1 (Hub namespace-ownership map) in `backend/tests/Grimoire.ArchTests/AgentArtifactNamingRuleTests.cs`: add `Grimoire.Hub.Cli` as a cross-agent namespace, and mirror the entry in `docs/conventions/agent-artifact-naming.md` (doc↔fixture mirror). Red/Green probe: temporarily remove the new map entry (or add a probe type violating the amended rule), verify failure, restore, verify pass.

**Checkpoint**: Both boundaries guarded and probed. Feature code may now begin.

---

## Phase 1: Setup

**Purpose**: Bring in the single new dependency.

- [x] T003 Add `Spectre.Console.Cli` 0.55.0: `PackageVersion` entry in `backend/Directory.Packages.props` and `PackageReference` in `backend/src/Grimoire.Hub/Grimoire.Hub.csproj`. Solution builds; C9 (T001) passes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The command framework, dispatch gate, shared settings, help, and dual-writer hardening every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Create `CliExitCode` in `backend/src/Grimoire.Hub/Cli/CliExitCode.cs` per data-model.md: `Success=0`, `OperationFailed=1`, `UsageError=2`, `NotFound=3`, `StateConflict=4`, `WaitTimeout=5`, `Cancelled=130`.
- [x] T005 [P] Create `HubPathSettings` (base `CommandSettings`) in `backend/src/Grimoire.Hub/Cli/HubPathSettings.cs`: one `[CommandOption]` per `PathSwitchCatalog.All` entry; Spectre performs syntax only — actual binding flows through the existing configuration composition (`AddCommandLine` + ADR-009 switch mappings), preserving CLI > env > appsettings > defaults precedence.
- [x] T006 [P] Create `CliStatusRenderer` in `backend/src/Grimoire.Hub/Cli/CliStatusRenderer.cs`: live status/event stream rendering to **stderr** only (stdout carries only the result contract, FR-006).
- [x] T007 Create `HubCliCommands` catalog in `backend/src/Grimoire.Hub/Cli/HubCliCommands.cs`: single source of truth (name, description, command type) for all 8 commands (`lint-run`, `remediation-authorize`, `remediation-dismiss`, `remediation-withdraw`, `ingest-retrigger`, `ingest-resume`, `query`, `submit-source`); unique-name validation; drives CommandApp registration and the Program.cs dispatch check (FR-010).
- [x] T008 Create `HubCliApp` in `backend/src/Grimoire.Hub/Cli/HubCliApp.cs`: builds the Hub's existing web-host composition (`builder.Build()`, never `app.Run()` — no port bound), exposes its services to Spectre via a type registrar, runs `RestartReconciler` bootstrap identically to the web host, maps Spectre validation/unknown-command errors to exit 2, and **disposes the built host before exit so OTLP telemetry flushes** (research D8, obligation 1).
- [x] T009 Create `HubCliHelpProvider` in `backend/src/Grimoire.Hub/Cli/HubCliHelpProvider.cs`: root help renders `FigletText("Grimoire")` logo + usage line + `Commands:` section from the CommandApp registrations + `Server options:` section generated from `PathSwitchCatalog.All`; per-command `--help` stays logo-free (research D3/D7).
- [x] T010 Migrate `submit-source` to `SubmitSourceCommand` in `backend/src/Grimoire.Hub/Cli/SubmitSourceCommand.cs` (`--path` required, `--source-kind` optional default `file`): identical in-process run-to-exit execution and exact output `Submitted ingest task: {taskId}`; existing submit-source integration tests pass unchanged.
- [x] T011 Rewire the dispatch gate in `backend/src/Grimoire.Hub/Program.cs`: if `args[0]` matches a catalog name or `--help`/`-h` appears anywhere (FR-011), run `CommandApp.RunAsync(args)` and exit with its code; otherwise the web-host path runs unchanged (ADR-009 precedence, `PathSwitchCatalog` untouched). Retire `BuildUsageText()`.
- [x] T012 [P] Harden `backend/src/Grimoire.Hub/OperationalState/OperationalStateRepository.cs`: enable `busy_timeout` + WAL journal mode on connections (research D1b); no schema change.
- [x] T013 [P] Add `HubPathSettings` ⇔ `PathSwitchCatalog` 1:1 parity test (in-process) in `backend/tests/Grimoire.IntegrationTests/HubHelpUsageTests.cs` so the two sources can never drift (research D4).
- [x] T014 [P] Add SQLite dual-writer tolerance test in `backend/tests/Grimoire.IntegrationTests/HubCliConcurrencyTests.cs`: two concurrent writers against one temp-dir operational-state database back off via `busy_timeout` instead of failing with `SQLITE_BUSY` (research D1b).

**Checkpoint**: `Grimoire.Hub <command>` dispatches through Spectre against the real composition; help/dispatch/exit-code plumbing works; foundation ready — user stories can start (in parallel if staffed).

---

## Phase 3: User Story 1 — Trigger a lint run from a script or terminal (Priority: P1) 🎯 MVP

**Goal**: `lint-run` starts a lint run in-process, supervises it to its terminal state, and reports conflicts (already active — including cross-process; blocked by unresolved remediation tasks) with specific messages and exit codes.

**Independent Test**: Invoke the command against a seeded Hub data directory with no active lint run: a run starts, the result line prints, exit 0 — without any other command in this feature existing.

### Implementation for User Story 1

- [X] T015 [US1] Register the `lint.pid` runtime location in `GrimoirePathOptions`/`ResolvedGrimoirePaths` under `backend/src/Grimoire.Hub/Runtime/Paths/` per ADR-009 (under the data directory; no new switch unless warranted — mirror the existing runtime-location pattern).
- [X] T016 [US1] Add the exclusive `lint.pid` OS file lock to `LintRunCoordinator.TriggerAsync` in `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` (research D1a): acquired for the duration of a run on **both** entry paths; holder conflict maps to the existing `Busy`/`lint_run_active` outcome; the in-memory slot remains the in-process fast path. Precedent: `SharedFileWriteGuard` (ADR-015).
- [X] T017 [US1] Create `LintRunSettings` + `LintRunCommand` in `backend/src/Grimoire.Hub/Cli/LintRunCommand.cs`: trigger via `LintRunCoordinator.TriggerAsync()`, print run id at start (status), supervise to terminal state, outputs and exit codes exactly per contracts/cli-commands.md (completed 0, failed 1, already-active 4, blocked-by-unresolved 4).
- [X] T018 [P] [US1] Add `lint-run` integration tests in `backend/tests/Grimoire.IntegrationTests/HubCliCommandTests.cs`: full contract matrix (success line + exit 0; failed reason + exit 1; already-active message + exit 4; unresolved-tasks message with count and ids + exit 4), blocking behavior (command returns only after the scripted terminal state), stdout/stderr separation. Real composed service graph, temp-dir SQLite, `FakeAgentProcessLauncher`.
- [X] T019 [P] [US1] Add cross-process `lint.pid` conflict test in `backend/tests/Grimoire.IntegrationTests/HubCliConcurrencyTests.cs`: two harness instances over the same data directory — while one holds the lint run, the other's trigger returns the already-active conflict (US1 acceptance scenario 2, cross-process).
- [X] T020 [P] [US1] Add lint parity test in `backend/tests/Grimoire.IntegrationTests/HubCliParityTests.cs`: trigger once via the HTTP endpoint handler path and once via `LintRunCommand` against identically seeded harnesses; assert identical repository rows, record content, and coordinator responses (SC-005).

**Checkpoint**: US1 fully functional and independently testable — the parse → invoke coordinator → print → exit-code pattern is established.

---

## Phase 4: User Story 2 — Manage a remediation task's authorization lifecycle (Priority: P1)

**Goal**: `remediation-authorize`, `remediation-dismiss`, `remediation-withdraw` drive the existing human-permitted transitions through a shared service, with the full not-found/conflict/usage error matrix.

**Independent Test**: Seed a remediation task in `proposed` state and run each of the three commands against it in isolation; observe the correct transition, printed result, and exit status — independent of the other commands.

### Implementation for User Story 2

- [X] T021 [US2] Extract `RemediationTaskTransitionService` in `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskTransitionService.cs`: move the three inline endpoint-handler transition flows **verbatim** — CAS transition, lifecycle publish, metrics, log events, record append (dismiss), eager `TryStartNextAsync` (authorize) — adding nothing. Result shape: `Ok(taskId, newState, authorizedAt?)` | `NotFound` | `Conflict(reason, currentState)` with existing reasons `task_not_proposed`, `task_not_authorized`, `execution_already_started`. `Authorized→Executing` stays coordinator-only (`RemediationExecutionDispatchRuleTests` unchanged, ADR-018).
- [X] T022 [US2] Delegate the three handlers in `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskEndpoints.cs` to the service; all existing remediation endpoint + observability integration tests pass unchanged (proves no signal lost in the move).
- [X] T023 [US2] Create `RemediationTaskSettings` (`--task-id` required, non-empty) + `RemediationAuthorizeCommand` in `backend/src/Grimoire.Hub/Cli/RemediationAuthorizeCommand.cs`: transition via the service; when the eager dispatch starts executing, supervise the execution to its terminal state (completed/not-applicable → 0, failed → 1); when the remediation queue is paused (fresh-process/restart semantics, ADR-018), exit after the transition — identical to the HTTP flow. Outputs per contract.
- [X] T024 [P] [US2] Create `RemediationDismissCommand` in `backend/src/Grimoire.Hub/Cli/RemediationDismissCommand.cs`: immediate transition, outputs/exit codes per contract.
- [X] T025 [P] [US2] Create `RemediationWithdrawCommand` in `backend/src/Grimoire.Hub/Cli/RemediationWithdrawCommand.cs`: immediate transition, incl. the `execution_already_started` lost-race conflict message, outputs/exit codes per contract.
- [X] T026 [US2] Add remediation command integration tests in `backend/tests/Grimoire.IntegrationTests/HubCliCommandTests.cs`: full contract matrix for all three commands (success 0 with authorization timestamp for authorize; missing `--task-id` usage error 2 with no store contact; unknown id not-found 3; each wrong-state conflict 4 with its specific message; authorize+scripted-execution paths 0/1).
- [X] T027 [US2] Add remediation parity tests in `backend/tests/Grimoire.IntegrationTests/HubCliParityTests.cs`: each of the three transitions once via the endpoint handler and once via the command against identically seeded harnesses — identical rows, record content, and responses, both paths through the extracted service (SC-005).

**Checkpoint**: US1 and US2 both work independently; the extraction serves both entry paths.

---

## Phase 5: User Story 3 — Recover the ingest queue from the command line (Priority: P2)

**Goal**: `ingest-retrigger` re-arms one queued task and supervises its processing; `ingest-resume` resumes the queue idempotently and supervises the drain.

**Independent Test**: Seed a queued ingest task and run the retrigger command against it; separately run the resume command against a queue — correct outcome and exit status for each, independent of the other commands.

### Implementation for User Story 3

- [X] T028 [P] [US3] Create `IngestRetriggerSettings` (`--task-id` required, non-empty) + `IngestRetriggerCommand` in `backend/src/Grimoire.Hub/Cli/IngestRetriggerCommand.cs`: `IngestRunCoordinator.RetriggerAsync(taskId)`, supervise the triggered processing to terminal state; outputs/exit codes per contract (processed 0, failed 1, usage 2, not found 3, not-in-queue 4 with column).
- [X] T029 [P] [US3] Create `IngestResumeSettings` + `IngestResumeCommand` in `backend/src/Grimoire.Hub/Cli/IngestResumeCommand.cs`: `IngestRunCoordinator.ResumeAsync()`, print queued count up front, supervise until the queue drains, print processed/failed counts; idempotent, exit 0 even when individual tasks failed (per-task outcomes listed on stderr).
- [X] T030 [US3] Add ingest command integration tests in `backend/tests/Grimoire.IntegrationTests/HubCliCommandTests.cs`: retrigger matrix (re-armed + processed 0, failed 1, missing arg 2, unknown id 3, not-queued 4) and resume (idempotent 0 in any queue state, drained counts, blocking until drain).
- [X] T031 [US3] Add ingest parity tests in `backend/tests/Grimoire.IntegrationTests/HubCliParityTests.cs`: retrigger and resume once via endpoint handler, once via command — identical rows/records/responses (SC-005).

**Checkpoint**: US1–US3 independently functional.

---

## Phase 6: User Story 4 — Ask the wiki a question from the command line (Priority: P2)

**Goal**: `query` submits a turn in-process, streams the accumulating answer to stderr, blocks until the terminal state, and maps timeout (interrupt, exit 5) and Ctrl-C (interrupt, exit 130) distinguishably (SC-006).

**Independent Test**: Run the command with a prompt — with and without `--conversation-id` — against a seeded data directory; a turn is submitted, the CLI waits for the terminal state, and the answer (or failure/timeout reason) prints with the correct exit status, independent of the other commands.

### Implementation for User Story 4

- [X] T032 [US4] Create `QuerySettings` in `backend/src/Grimoire.Hub/Cli/QueryCommand.cs` (same file as T033): `<prompt>` required argument (non-empty after trim, ≤ 8000 chars), `--conversation-id` optional (must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`, ADR-014), `--timeout` optional positive integer default 300 — all validated by Spectre before any action (FR-009/FR-018, exit 2, no side effect); conversation-id generation when omitted: `{utcNow:yyyy-MM-dd}-conv-{Guid:N}` truncated to 40 chars.
- [X] T033 [US4] Create `QueryCommand` in `backend/src/Grimoire.Hub/Cli/QueryCommand.cs`: submit via `QueryRunCoordinator.SubmitTurnAsync(conversationId, prompt)` (`{prompt}` only, ADR-014); map `ConcurrencyLimitReached`→4, `ConversationAlreadyActive`→4, `RecordUnreadable`→1; wait on the coordinator's turn state streaming `Answer` deltas to stderr; on terminal `completed` print header `Query turn {turnId} in conversation {conversationId}: completed` + answer verbatim to stdout, exit 0; terminal `failed` → reason, exit 1; `--timeout` expiry → `QueryRunCoordinator.InterruptAsync(turnId)`, timeout message, exit 5; cancellation (Ctrl-C via `Console.CancelKeyPress`/cancellation token) → same interrupt, distinct cancellation message, exit 130. No agent work continues unsupervised after exit (FR-015/FR-016).
- [X] T034 [US4] Add query integration tests in `backend/tests/Grimoire.IntegrationTests/HubCliQueryCommandTests.cs`: (a) completed turn with generated and with supplied conversation id — header + verbatim multi-line answer on stdout, exit 0; (b) scripted failing turn → failure reason, exit 1; (c) usage errors (missing/empty prompt, malformed conversation id, non-positive timeout) → exit 2, no turn submitted; (d) conflicts (concurrency limit 4, conversation already active 4, record unreadable 1); (e) scripted never-completing turn + short `--timeout` → timeout message, exit 5, `InterruptAsync` invoked, turn terminal `interrupted`, partial answer persisted; (f) cancellation token fired mid-wait → cancellation message, exit 130, same interrupt path; timeout/cancel/failure/conflict messages all mutually distinct (SC-006).
- [X] T035 [US4] Add query parity test in `backend/tests/Grimoire.IntegrationTests/HubCliParityTests.cs`: turn submission via endpoint handler vs. `QueryCommand` against identically seeded harnesses — identical turn rows and conversation-record appends at the terminal transition (SC-005, ADR-014).

**Checkpoint**: All four user stories independently functional.

---

## Phase 7: Polish, Help Contract & Completeness Audit

**Purpose**: Cross-cutting guarantees (help surface, out-of-process behavior, telemetry flush) and the mandatory DoD audit.

- [X] T036 Extend `backend/tests/Grimoire.IntegrationTests/HubHelpUsageTests.cs` (out-of-process, spawned real `Grimoire.Hub.dll`): root `--help` shows the FigletText logo, all 8 command names with descriptions, and every `PathSwitchCatalog.All` switch (SC-004, 017 parity preserved); per-command `--help` shows that command's arguments logo-free; `--help` alongside a command prints help and runs nothing (FR-011); unknown command name → usage error naming it, exit 2; a command invocation never binds a port (SC-001).
- [X] T037 [P] Add telemetry flush test in `backend/tests/Grimoire.IntegrationTests/HubCliConcurrencyTests.cs` (research D8, obligation 2): a CLI-invoked flow (e.g. `remediation-dismiss`) still records its existing signals end-to-end via the in-memory exporter (ADR-005 pattern) — proving host disposal flushes before process exit, not new signal identities.
- [X] T038 Observability completeness audit (MANDATORY — Constitution Principles III/IV): confirm `plan.md ## Observability` declares no new metrics/log-events/trace-spans with valid justification; cross-reference the two derived obligations — telemetry bootstrap + disposal (T008) and flush test (T037) — and the no-signal-lost guarantee for the remediation extraction (T022 existing observability tests + T027 parity) against their implementing tasks and passing tests; confirm no agent-judgment success criteria exist in spec.md (no evaluation tests due); file any gap found as a new task before declaring the DoD met.
- [X] T039 CI enforcement (MANDATORY — Constitution Principle IV): verify the new/amended `Grimoire.ArchTests` rules (T001, T002) and all new `Grimoire.IntegrationTests` classes run in the standard PR pipeline with no filtering that excludes them; the retired `BuildUsageText` leaves no dead code or stale references.
- [X] T040 Run quickstart.md validation: execute the documented command walkthrough against a scratch data directory; fix any drift between docs and behavior.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** → blocks everything (constitutional gate).
- **Phase 1** (T003) → blocks Phase 2.
- **Phase 2** → blocks all user stories. Within it: T004–T006 parallel; T007 → T008 → T009 (help needs the app, app needs the catalog); T010, T011 after T007–T009; T012–T014 parallel to the command-framework chain.
- **Phases 3–6 (user stories)**: independent of each other once Phase 2 is done; priority order US1 → US2 → US3 → US4 when executed sequentially. Within US1: T015 → T016 → T017; T018–T020 after T017. Within US2: T021 → T022 → T023; T024/T025 parallel after T021; T026/T027 after commands exist. Within US3: T028/T029 parallel; T030/T031 after. Within US4: T032 → T033 → T034/T035.
- **Phase 7**: T036 after all commands exist; T037 after T008 + any command; T038–T040 last.

### Parallel Opportunities

- T001 ∥ T002 (different rule files)
- T004 ∥ T005 ∥ T006; T012 ∥ T013 ∥ T014
- After Phase 2: US1, US2, US3, US4 phases can proceed in parallel (different command files; shared test files `HubCliCommandTests.cs`/`HubCliParityTests.cs` require coordination — merge per story in priority order)
- T018 ∥ T019 ∥ T020; T024 ∥ T025; T028 ∥ T029; T036 ∥ T037

## Implementation Strategy

**MVP = Phase 0 + 1 + 2 + Phase 3 (US1)**: the lint-run command proves the entire pattern — Spectre dispatch, in-process composition, blocking supervision, contract outputs, cross-process lock — end to end. Each subsequent story adds value without touching the previous ones; the shared integration-test files grow per story. Stop and validate at every checkpoint.

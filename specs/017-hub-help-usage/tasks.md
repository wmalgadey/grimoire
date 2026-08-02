---

description: "Task list for feature implementation"
---

# Tasks: Hub --help Usage Output

**Input**: Design documents from `/specs/017-hub-help-usage/`

**Prerequisites**: plan.md, spec.md, research.md, quickstart.md

**Tests**: Deterministic tests are required for both success criteria (SC-001, SC-002);
SC-003 is a manual quickstart read-through per `plan.md ## Test Strategy` (not
automatable). No agent-judgment success criteria exist (harness-only feature), so no
evaluation tests are required.

**Logging Contract**: N/A — `plan.md ## Observability` has no Structured Log Events rows.

**Trace Contract**: N/A — `plan.md ## Observability` has no Distributed Trace Spans rows.

**Organization**: This feature has a single user story (US1, P1) — there is no P2/P3
and no Setup/Foundational infrastructure beyond the existing `Grimoire.Hub` composition
root, so Phases 1–2 of the standard template are intentionally omitted.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1)

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify the structural/parity test before any feature code is
written. Enforces the ADR-009 constraint from `plan.md § Architectural Constraints &
ADRs`: the usage text must never be able to drift from
`PathConfigurationSwitchMappingsFactory()`'s switch list.

**⚠️ NON-NEGOTIABLE**: No feature implementation (T003) can begin until this phase is
complete and confirmed RED.

- [x] T001 Write a parity test in `backend/tests/Grimoire.IntegrationTests/HubHelpUsageTests.cs`
      that spawns the built Hub executable with `--help`, captures stdout, and asserts
      the output contains every key from `PathConfigurationSwitchMappingsFactory()`
      (reflectively invoked or duplicated verbatim from `Program.cs` — see note below)
      plus the literal strings `submit-source`, `--path`, and `--source-kind`.

**Note on accessing `PathConfigurationSwitchMappingsFactory()` from the test project**:
it is currently a `static` method local to `Program.cs`'s top-level statements. Since
`Grimoire.Hub` uses top-level statements, the test cannot call it directly across the
process boundary (the test spawns a separate OS process — see T002). Instead, assert
against the same literal switch list documented in `plan.md`/`spec.md` FR-002, which
the implementation task (T003) must keep in lockstep with the factory. This keeps the
test hermetic (string assertion on captured stdout, no cross-process reflection) while
still catching drift the moment either list changes without the other.

**Red/Green probe** (required — confirms the test actually catches a missing/incomplete
implementation, standing in for the constitution's "deliberately bad file" probe since
this is TDD against not-yet-written feature code rather than an existing structural
rule):
1. Run T001's test against the current `Program.cs` (no `--help` handling exists yet).
2. Confirm it FAILS — either the process hangs past the test's timeout (the host starts
   normally since `--help` is an unrecognized switch today) or stdout lacks the expected
   switch list. Record which failure mode was observed.
3. Proceed to T003 to make it pass. Do not delete or weaken this test afterward.

**Definition of Done**:
- [x] Test written and committed
- [x] Red/Green probe completed and result recorded (RED: unhandled `GrimoirePathValidationException`
      — "secrets_file: required file does not exist" — SIGABRT/exit 134, since `--help`
      wasn't recognized yet and path resolution ran against this worktree's missing
      `data/.env`)
- [x] Test passes (GREEN) once T003 is implemented, with no other passing tests broken
      (588/588 `Grimoire.IntegrationTests` pass)

**Checkpoint**: Structural/parity guard is in place. Feature code may now begin.

---

## Phase 3: User Story 1 - Discover available startup options (Priority: P1) 🎯 MVP

**Goal**: Running the Hub with `--help`/`-h` prints a usage message listing
`submit-source` and every ADR-009 path switch, then exits with code 0 without starting
the web server.

**Independent Test**: Run `dotnet run --project src/Grimoire.Hub/ -- --help` from
`backend/` and confirm a usage message appears, the process exits quickly with code 0,
and no `Now listening on:` line is printed (see `quickstart.md`).

### Tests for User Story 1

> **NOTE: Write these tests FIRST (alongside T001), ensure they FAIL before T003**

- [x] T002 [US1] In `backend/tests/Grimoire.IntegrationTests/HubHelpUsageTests.cs`, add
      process-spawn tests (via `ProcessStartInfo`/`Process.Start`, following the
      existing pattern in `ReplayAdapterTests.cs`/`CrossProcessFileLockTests.cs`) for:
      - bare `--help` exits 0 within a short timeout (e.g. 5s) with no
        `Now listening on:` in stdout (FR-001, FR-003, SC-001)
      - bare `-h` behaves identically to `--help` (FR-001)
      - `--help --base-dir <nonexistent-tmp-dir>` still exits 0 with usage printed —
        proves `--help` wins and no path resolution is attempted against the bogus
        value (FR-004)
      - `submit-source --help` exits 0 with usage printed and does not attempt a
        submission (FR-004, edge case from spec.md)
      - a bare invocation with no arguments is unaffected by this feature (spec.md edge
        case) — out of scope to fully verify (would require a working `data/` setup),
        so assert only that this case is unaffected by the help-detection code path
        (e.g. via a code-level assertion in T003, not a new process-spawn test)

### Implementation for User Story 1

- [x] T003 [US1] In `backend/src/Grimoire.Hub/Program.cs`, add a help check as the
      first statement (before `WebApplication.CreateBuilder(args)`): if `--help` or
      `-h` (case-insensitive) appears anywhere in `args`, print a plain-text usage
      message to `Console.Out` and `return` immediately with implicit exit code 0.
      Build the message from `PathConfigurationSwitchMappingsFactory()`'s keys (one
      line per switch, each paired with a short description) plus a `submit-source`
      section documenting `--path` and `--source-kind`. No other startup code (path
      resolution, secrets loading, `OperationalStateRepository` init,
      `WebApplication.CreateBuilder`) may run before this check or after taking the
      help branch (FR-001–FR-005).
- [x] T004 [US1] Run T001 and T002 against the T003 implementation; confirm both are
      GREEN and no other `Grimoire.IntegrationTests` test regresses.

**Checkpoint**: User Story 1 (the feature's only story) is fully functional and
independently testable — this is also the feature's MVP and final scope.

---

## Phase N: Polish & Cross-Cutting Concerns

**Purpose**: Final-phase completeness audit (Constitution Principle III) and manual
validation. This feature has no Structured Log Events or Distributed Trace Spans rows
and no agent-judgment success criteria, so those completeness-audit sub-items are
short-circuited below rather than omitted silently.

- [x] T005 Observability completeness audit (MANDATORY — Constitution Principle III/IV):
      confirm `plan.md ## Observability` has zero rows (Business Metrics, Structured
      Log Events, Distributed Trace Spans all "None — N/A per justification") and that
      no implementation task in this file introduced an unaudited signal. No gap
      expected; file a new task only if T003's implementation is found to log/emit
      anything. **Audited**: T003's diff (`Program.cs`) contains no `ILogger`,
      metric, or trace-span calls — confirmed by direct diff review. No gap found.
- [x] T006 Agent-behavior evaluation completeness audit (MANDATORY only for features
      with agentic behavior — Constitution Principles II & V): confirm `spec.md` has no
      agent-judgment success criteria (all of SC-001/SC-002 are deterministic
      guarantees, SC-003 is a manual UX check) — N/A, no evaluation tests required.
      **Audited**: confirmed, no agentic surface touched.
- [x] T007 Run `quickstart.md` validation end-to-end by hand (all five scenarios:
      `--help`, `-h`, `--help` combined with other args, `submit-source --help`, and a
      confirmation that no-`--help` startup is unchanged), including the SC-003 timed
      read-through, and record the outcome.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** (T001): No dependencies — can start immediately. BLOCKS T003.
- **Phase 3 tests** (T002): No dependencies on T001 completion to *write*, but both
  T001 and T002 MUST be confirmed failing before T003 starts (same file, so write them
  together, sequentially, in one pass).
- **Phase 3 implementation** (T003): Depends on T001 and T002 existing and failing.
- **T004**: Depends on T003.
- **Polish (Phase N)**: Depends on T004 (all tests green).

### Within This Feature

- T001 and T002 land in the same new file (`HubHelpUsageTests.cs`) — write
  sequentially, not in parallel, to avoid merge conflicts on the same file.
- T003 is a single-file change (`Program.cs`) with no internal parallelism.
- No [P] markers apply: every task in this feature touches one of two files
  (`HubHelpUsageTests.cs` or `Program.cs`), so nothing is truly independent.

---

## Implementation Strategy

### MVP First (and only) Scope

1. Complete Phase 0 (T001) — RED.
2. Complete Phase 3 tests (T002) — RED.
3. Complete Phase 3 implementation (T003) — turns T001+T002 GREEN.
4. Complete T004 — confirm GREEN, no regressions.
5. Complete Polish (T005–T007) — audits + manual quickstart validation.
6. Done — this feature has one user story; there is no incremental follow-up phase.

---

## Notes

- Commit after each task or logical group.
- Verify T001 and T002 fail before implementing T003 (TDD, per constitution Principle III
  ordering: structural test verified RED → feature code → GREEN).
- Avoid: adding options beyond what `Program.cs` already wires up today (spec.md
  Assumptions — this feature only adds discoverability, not new switches).

---

## Phase 4: Convergence

- [x] T008 Remove the pluralized `Commands:` heading in `BuildUsageText()`
      (`backend/src/Grimoire.Hub/Program.cs`) for the single `submit-source` entry —
      fold it into the `Usage:` block or relabel singular — since the Hub exposes
      exactly one CLI command today and the current framing implies more exist per
      FR-005 (partial)

---

## Phase 5: Convergence

- [x] T009 Merge `PathConfigurationSwitchMappingsFactory()` and
      `PathConfigurationSwitchDescriptions()` (`backend/src/Grimoire.Hub/Program.cs:236-280`)
      into a single collection (e.g. a list of `(Switch, ConfigKey, Description)` records)
      that is the sole source of truth for the ADR-009 switch vocabulary — derive both
      `AddCommandLine`'s switch-mapping dictionary and `BuildUsageText()`'s output from
      it, and remove the now-unnecessary runtime fail-fast throw in `BuildUsageText()`
      since drift becomes structurally impossible rather than merely caught at runtime
      per research.md "single source of truth" decision (partial)

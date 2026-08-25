# Tasks: Host Stability Guarantee for Agent Runs

**Input**: Design documents from `/specs/027-host-stability/`
**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md)

**Tests**: All success criteria in this feature are deterministic harness guarantees (Constitution Principle II's success-criteria split) — there is no agent-judgment criterion, so no evaluation-style tests apply. Every task below that touches a Boundary Rule or Feature-Scoped Invariant carries its own hermetic test per Constitution Principle II/III.

**Logging Contract**: N/A — plan.md's Observability section introduces no new log event name; the three new `reason` label values on the existing denial event are exercised by the tests in Phase 3 (US1). See the Final Phase completeness audit.

**Trace Contract**: N/A — plan.md's Observability section introduces no new trace span; the existing `*_agent.tool_call` span's `reason` tag gains the same three new values, exercised by the same Phase 3 tests.

**Organization**: Tasks are grouped by user story (spec.md P1/P2) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Every task cites the `FR-###`/`SC-###` identifier(s) it implements or verifies

## Path Conventions

Single existing backend solution (`backend/Grimoire.sln`) — no new project. All paths are
repo-relative from `/home/user/grimoire`.

---

## Phase 0: Structural Boundary Enforcement (MANDATORY — Constitution Principle III)

**Purpose**: Write and verify the structural boundary test for this feature's two
Boundary Rules (ADR-034 R1/R2) before any feature code is written. Per Constitution
Principle III, R3/R4 are Feature-Scoped Invariants and are deliberately **not** here —
see Phase 4 (US2).

**⚠️ NON-NEGOTIABLE**: No feature implementation can begin until Phase 0 is complete.

- [ ] T001 Write `SpawnSiteRegistryRuleTests` in `backend/tests/Grimoire.ArchTests/SpawnSiteRegistryRuleTests.cs`, modeled on the existing `NonBlockingDispatchRuleTests` idiom and reusing `ArchScan.cs`'s shared IL-walk helpers (`FindConstructions`, `FindCalls`). Two facts against the compiled `Grimoire.Hub.dll`:
  - **R1** (FR-004, SC-002): every `newobj System.Diagnostics.Process::.ctor` / `System.Diagnostics.ProcessStartInfo::.ctor` and every `call System.Diagnostics.Process::Start` instruction has its enclosing outermost type in the allowlist `{Grimoire.Hub.AgentDispatch.Adapters.AgentProcess.AgentProcessHost, Grimoire.Hub.IngestSubmission.Adapters.MarkItDown.MarkItDownConverter}`.
  - **R2** (FR-003, SC-003): neither allowlisted type ever calls the `ProcessStartInfo.Arguments` (string) property setter — only `ArgumentList.Add` — anywhere in its `Process`-construction code.

  **Red/Green probe** (required, per Constitution Principle III):
  1. Temporarily add a violating call site — e.g. `Process.Start(new ProcessStartInfo("sh", "-c \"" + x + "\""))` — inside an unrelated `Grimoire.Hub` class (a scratch method in `IngestSubmissionEndpoints` works, per quickstart.md).
  2. Run the new test — it MUST fail, naming the new, unlisted call site (proves R1's detection).
  3. Revert step 1; instead add `.Arguments = "..."` inside `AgentProcessHost`'s `StartProcess` — the test MUST fail (proves R2's detection).
  4. Remove both probes; re-run — the test is green again.
  5. Document the probe result (both R1 and R2 confirmed detected) in the commit message.

**Definition of Done**:
- [ ] R1 and R2 each written and committed
- [ ] Red/Green probe completed for both rules (commit message documents the result)
- [ ] Test passes in CI with no violations (both probe sites removed)

**Checkpoint**: Structural boundary is guarded. Feature code may now begin.

---

## Phase 1: Setup

Not applicable — no new project, package, or dependency (Mono.Cecil is already a
`Grimoire.ArchTests` dependency; `backend/Grimoire.sln` is the existing solution). Skip
directly to Phase 3.

## Phase 2: Foundational

Not applicable — User Story 1 and User Story 2 touch disjoint files with no shared new
infrastructure between them; neither blocks the other beyond Phase 0. Skip directly to
Phase 3.

---

## Phase 3: User Story 1 - Adversarial paths cannot escape the guarded write boundary (Priority: P1) 🎯 MVP

**Goal**: Every guarded write, read, and delete stays inside its policy-designated root
even under adversarial path input (percent-encoding, Unicode-normalization tricks, a
null byte, a chained symlink, or a symlink swapped in after validation).

**Independent Test**: Submit each adversarial path variant to the guarded write, read,
and delete tools against a real filesystem and assert denial plus an untouched target
outside the root (spec.md's own Independent Test for this story).

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation (classicist TDD, Constitution Principle II).**

- [ ] T002 [US1] Extend `backend/tests/Grimoire.IntegrationTests/PathTraversalTests.cs` with adversarial-variant cases against a real temp-directory filesystem and real `File.CreateSymbolicLink` — no doubles:
  - A percent-encoded traversal string (e.g. `%2e%2e%2Foutside`) submitted as a guarded-write `path` — asserts the write is denied (or lands harmlessly inside the root as a literal filename) and nothing outside the root is touched.
  - A Unicode-normalization-confusable traversal string (e.g. a fullwidth solidus variant of `../`) — same assertion.
  - A path containing an embedded NUL byte followed by an out-of-root suffix — asserts denial with reason `malformed_path` and, critically, that the guarded tool call returns a normal `is_error` result rather than an unhandled exception propagating out of the run.
  - A chained/nested symlink (symlink A → symlink B → a target outside the root) — asserts denial (reason `symlink_loop` if the chain exceeds 40 hops, or ordinary `traversal`/`out_of_scope` otherwise).
  - A post-validation symlink swap: start a write to a path that resolves inside the root at validation time, swap the symlink to point outside the root before the mutating write executes, assert the write is denied with reason `revalidation_failed` — and that the swapped-in destination file is untouched.
  - A combined-technique variant: percent-encoded traversal through a symlinked intermediate directory (spec.md Edge Cases).

  Every new case asserts denial **and** that the out-of-root file's content is byte-identical before/after (proves containment, not just an error code). FR-001, FR-002, FR-006, SC-001.

### Implementation for User Story 1

- [ ] T003 [US1] Harden `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` so T002 passes (research.md D1–D3):
  - **D1**: catch the `ArgumentException` .NET's `Path` APIs throw for an embedded NUL character inside `Canonicalize`/`ResolvePhysicalPathInRepository`, and surface it as a normal policy denial with reason `malformed_path` instead of letting it propagate unhandled.
  - **D2**: make `ResolvePhysicalPathInRepository`'s per-segment symlink walk recursive — after resolving one segment's reparse point, recurse the same resolution walk on the reconstructed path instead of `break`-ing out, so a target reached through further symlinks is fully resolved. Cap recursion at 40 hops (Linux's conventional `MAXSYMLINKS`); exceeding it denies with reason `symlink_loop`.
  - **D3**: in `ExecuteWriteFileAsync`/`WriteFileAtomicallyAsync` and `ExecuteDeleteFileAsync`, immediately before the mutating `File.Move`/`File.Delete` call, recompute the canonical physical path and compare it ordinally to the path already validated against policy; a mismatch denies with reason `revalidation_failed` before the mutating call executes.

  FR-001, FR-002, SC-001.

**Checkpoint**: User Story 1 is fully functional and independently testable — every adversarial path variant is denied, and the constitutional guarantee (Principle V, Host stability) holds for the path-containment half.

---

## Phase 4: User Story 2 - Every process the harness spawns is a known, non-injectable one (Priority: P2)

**Goal**: Every process-spawn call site is enumerable and closed (already delivered by
Phase 0's `SpawnSiteRegistryRuleTests` — R1/R2 are Boundary Rules and were required to
land in Phase 0, not here), and every filename-derived value that selects a converter or
code path is validated against a fixed allowlist before use (R4, this phase's own work).

**Independent Test**: A structural test enumerating every process-spawn call site,
Red/Green-probed (already satisfied by T001); an unlisted-extension submission is
rejected by the ingest validator before any conversion or storage code runs (this
phase's test, T004).

### Implementation for User Story 2

- [ ] T004 [US2] Add `backend/tests/Grimoire.IntegrationTests/IngestSubmissionValidatorAllowlistTests.cs` (new file): a hermetic classicist integration test submitting a filename with an unlisted extension (e.g. `.exe`, `.sh`) through `IngestSubmissionValidator`'s real validation entry point (`backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionValidator.cs`), asserting rejection before any conversion or storage code runs. No production code change — `IngestSubmissionValidator`'s existing fixed `HashSet<string>` allowlists (`_markdownExtensions`, `_officeExtensions`, the literal `.pdf` check) already implement R4; this test pins it with a dedicated, intent-named case rather than relying on incidental coverage from `IngestSubmissionApiTests`/`IngestConvertStepTests`. FR-005, SC-004.

**Checkpoint**: User Stories 1 AND 2 both work independently. The corrected Host
stability guarantee (Principle V) is fully covered: path containment (US1) and
subprocess containment (Phase 0 + US2).

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: The mandatory completeness audit (Constitution Principle III) that gates
the DoD, plus quickstart validation.

- [ ] T005 Completeness audit (MANDATORY — Constitution Principle III): cross-reference plan.md's Test Strategy table — confirm SC-001 (T002/T003), SC-002/SC-003 (T001), SC-004 (T004) each have a passing implementing test; cross-reference plan.md's Observability section — confirm all three new denial-reason label values (`malformed_path`, `symlink_loop`, `revalidation_failed`) are actually reached by a T002 test case (no new metric/log-event/span name is introduced, so no new logging/trace CI-enforcement task is required per plan.md's derivation-rule check); confirm this feature has zero agent-judgment success criteria (Constitution Principle II) — no eval suite is required, no user-reported-correction-loop entry is needed either. File any gap found as a new task before declaring the DoD met.
- [ ] T006 Run `specs/027-host-stability/quickstart.md` validation end-to-end: the path-containment filter, the spawn-site-registry filter (including a fresh Red/Green probe run), the extension-allowlist filter, and the full `dotnet test backend/Grimoire.sln` suite all pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Structural Boundary Enforcement)**: No dependencies — starts immediately. BLOCKS all feature code (Constitution Principle III, "No feature implementation can begin until Phase 0 is complete").
- **Phase 1 (Setup) / Phase 2 (Foundational)**: Not applicable to this feature (see above).
- **User Stories (Phase 3, Phase 4)**: Both depend only on Phase 0 completion. They touch disjoint files (`GuardedToolExecutor.cs`/`PathTraversalTests.cs` for US1; a new `IngestSubmissionValidatorAllowlistTests.cs` for US2) and have no dependency on each other.
- **Polish (Phase 5)**: Depends on Phase 3 and Phase 4 both being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Can start immediately after Phase 0. No dependency on US2.
- **User Story 2 (P2)**: Can start immediately after Phase 0. Its spawn-site acceptance scenarios (spec.md AS1–AS3) are already satisfied by Phase 0/T001; this phase's only remaining work (T004) has no dependency on US1.

### Within Each User Story

- US1: T002 (tests) MUST be written and observed failing before T003 (implementation) — classicist TDD, same file dependency (`PathTraversalTests.cs` asserts against `GuardedToolExecutor.cs`).
- US2: T004 is a single self-contained task (test against existing, unchanged production code — there is nothing to sequence it against).

### Parallel Opportunities

- Phase 0 (T001) has no parallel sibling — one task.
- Once Phase 0 is green, **US1 (T002→T003) and US2 (T004) can proceed fully in parallel** — disjoint files, no shared state, no ordering dependency between the two stories.
- Within US1, T002 and T003 are NOT marked `[P]` — same-file dependency chain (test written first, red; implementation makes it green).

---

## Parallel Example: Phase 0 complete, then both stories at once

```bash
# After T001 is green (Phase 0 checkpoint):
Task: "[US1] Extend PathTraversalTests.cs with adversarial path variants (T002), then harden GuardedToolExecutor.cs (T003)"
Task: "[US2] Add IngestSubmissionValidatorAllowlistTests.cs (T004)"
# Both tracks touch entirely disjoint files and can run concurrently.
```

---

## Implementation Strategy

### Delivery shape (stated per `CLAUDE.md`'s stacked-PR convention, before implementation begins)

**Single PR** — not a stack, despite this `tasks.md` having three phase groups beyond
Phase 0 (US1, US2, Polish), which is `CLAUDE.md`'s mechanical default trigger for
stacking. Justification: the total surface is four files (one production file hardened,
three test files — one new ArchTest, one extended IntegrationTest, one new
IntegrationTest), all in direct service of one cohesive constitutional guarantee (Host
stability, Principle V). Phase 0's structural test is a prerequisite gate, not an
independently shippable product increment, and US1/US2 are both small hardening
changes to the same guarantee rather than separable value increments a reviewer would
want to see land independently — splitting this into three PRs would produce review
overhead disproportionate to the diff. This is exactly the "one small enough that a
stack is ceremony" carve-out `CLAUDE.md` names as the alternative to the default.

### MVP First (User Story 1 Only)

1. Complete Phase 0 (structural boundary enforcement — R1/R2, Red/Green probed).
2. Complete Phase 3 (User Story 1 — path containment hardening).
3. **STOP and VALIDATE**: run `PathTraversalTests` and confirm every adversarial variant
   is denied with an untouched out-of-root target.
4. This alone satisfies the constitutional guarantee's higher-priority half (P1).

### Incremental Delivery

1. Phase 0 → structural boundary guarded.
2. Add User Story 1 → validate independently → this is the MVP (the guarantee itself, P1).
3. Add User Story 2 → validate independently → subprocess containment structurally pinned (P2).
4. Phase 5 (Polish) → completeness audit → DoD met, single PR ready for review.

---

description: "Task list for: Rename ContentRootPaths to an Ingest-Specific Type"
---

# Tasks: Rename ContentRootPaths to an Ingest-Specific Type

**Input**: Design documents from `/specs/021-ingest-content-paths/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

**Tests**: This feature adds no new tests — it is a behavior-preserving rename/de-duplication (spec FR-009). The existing hermetic integration and architecture tests are the verification surface; tasks update their references in place rather than adding new coverage.

**Logging/Trace Contract**: N/A — plan.md `## Observability` declares no metric, log event, or trace span rows for this feature (no new or changed observability signals).

**Organization**: Tasks are grouped by user story (spec.md US1 = rename, US2 = de-duplicate) to enable independent implementation and testing of each.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- File paths are exact and repo-relative

## Path Conventions

Single backend .NET solution. All paths are relative to the repository root, under `backend/src/Grimoire.Hub/` (production) and `backend/tests/Grimoire.IntegrationTests/` / `backend/tests/Grimoire.ArchTests/` (verification), per plan.md `## Project Structure`.

---

## Phase 0: Structural Boundary Gate (Constitution Principle III)

**Purpose**: Confirm the structural rule this feature must conform to is live before any code changes.

Per plan.md `## Architectural Constraints & ADRs` and `## Constitution Check`: this feature introduces **no new** structural rule — it makes an existing type conform to the naming rule ADR-013 already established, enforced by `AgentArtifactNamingRuleTests` (`HubNamespaces_MustFollowTheOwnershipMap`, `ExemptionFixture_MustMirror_TheConventionDocument`), which already received its Red/Green probe in feature 010/013. Re-running a fresh Red/Green probe on an already-proven rule would not test anything new; instead this phase confirms the existing guard is live and green immediately before the rename begins, so a subsequent failure is attributable to this feature's change, not a pre-existing gap.

- [X] T001 Run `dotnet test --filter "FullyQualifiedName~AgentArtifactNamingRuleTests"` from `backend/` against `backend/tests/Grimoire.ArchTests/AgentArtifactNamingRuleTests.cs` and confirm both `HubNamespaces_MustFollowTheOwnershipMap` and `ExemptionFixture_MustMirror_TheConventionDocument` pass on the pre-change codebase. Record the pass as the baseline this feature must not regress. No file changes in this task.

**Checkpoint**: Existing N1 guard confirmed live. Feature code may now begin.

---

## Phase 1: Setup

No setup tasks — this feature touches only existing projects (`Grimoire.Hub`, `Grimoire.IntegrationTests`, `Grimoire.ArchTests`) already built and configured; nothing new to initialize (plan.md `## Technical Context`: no new dependency, project, or framework).

---

## Phase 2: Foundational

No foundational/blocking-prerequisite phase. User Story 1 (the rename) is itself the prerequisite User Story 2 (field removal) builds on — spec.md frames this explicitly ("Story 1 alone already delivers the naming benefit even if this one were skipped... Story 2... depends on nothing else being renamed first" — practically, sequencing 1 → 2 avoids editing the same identifiers twice). No separate infrastructure phase is needed beyond that ordering.

---

## Phase 3: User Story 1 - A type's name tells its owner on sight (Priority: P1) 🎯 MVP

**Goal**: Rename `ContentRootPaths` (and its file) to `IngestContentPaths`, updating every production and test reference, with zero field or behavior change.

**Independent Test**: Repository-wide search for `ContentRootPaths` returns zero matches in `.cs` files; solution builds; N1 architecture test passes with no exemption-list change (spec SC-001, SC-005).

**Note on scope**: `backend/src/Grimoire.Hub/Runtime/Paths/ResolvedGrimoirePaths.cs` line 39's doc comment ("Replaces the repo-root parameters of the former `ContentRootPaths`...") is **intentionally NOT changed** — spec.md Edge Cases: it describes pre-ADR-022 history and remains accurate regardless of the current name.

### Implementation for User Story 1

- [X] T002 [P] [US1] Rename `backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs` to `backend/src/Grimoire.Hub/ContentRoot/IngestContentPaths.cs`; rename the record `ContentRootPaths` → `IngestContentPaths` and its `FromResolved` factory's declared return type. Namespace (`Grimoire.Hub.ContentRoot`), all 8 fields, and factory body stay unchanged in this task — field removal is Phase 4 (US2).
- [X] T003 [P] [US1] `backend/src/Grimoire.Hub/HubHostComposition.cs` — update the `ContentRootPaths.FromResolved(resolvedPaths)` call (~line 104) and the `sp.GetRequiredService<ContentRootPaths>()` DI lookup (~line 128) to `IngestContentPaths`.
- [X] T004 [P] [US1] `backend/src/Grimoire.Hub/IngestSubmission/SubmissionService.cs` — update `SubmitAsync`'s `ContentRootPaths contentPaths` parameter (~line 24) to `IngestContentPaths contentPaths`.
- [X] T005 [P] [US1] `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionPipeline.cs` — update the `ContentRootPaths` field and constructor parameter (~lines 45, 55) to `IngestContentPaths`.
- [X] T006 [P] [US1] `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs` — update all four `ContentRootPaths` handler-parameter occurrences (~lines 209, 243, 269, 373) to `IngestContentPaths`.
- [X] T007 [P] [US1] `backend/src/Grimoire.Hub/IngestSubmission/BoardEndpoints.cs` — update the `ContentRootPaths contentPaths` handler parameter (~line 31) to `IngestContentPaths`.
- [X] T008 [P] [US1] `backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs` — update the `_contentPaths` field type and constructor parameter (~lines 38, 52) from `ContentRootPaths` to `IngestContentPaths`.
- [X] T009 [P] [US1] `backend/src/Grimoire.Hub/Cli/IngestResumeCommand.cs` — update the `_contentPaths` field type and both constructor overloads' parameter type (~lines 40, 49, 56) from `ContentRootPaths` to `IngestContentPaths`.
- [X] T010 [P] [US1] `backend/src/Grimoire.Hub/Cli/IngestRetriggerCommand.cs` — update the `_contentPaths` field type and both constructor overloads' parameter type (~lines 59, 68, 75) from `ContentRootPaths` to `IngestContentPaths`.
- [X] T011 [P] [US1] `backend/src/Grimoire.Hub/Cli/SubmitSourceCommand.cs` — update the `_contentPaths` field type and constructor parameter (~lines 44, 46) from `ContentRootPaths` to `IngestContentPaths`.
- [X] T012 [P] [US1] `backend/tests/Grimoire.IntegrationTests/Fakes/IngestSubmissionPipelineFixture.cs` — update the `ContentPaths` property type and the optional `contentPaths` constructor parameter type (~lines 25, 56) from `ContentRootPaths` to `IngestContentPaths`. The record literal at ~lines 80-88 keeps all 8 fields for now (field removal is T018 in Phase 4).
- [X] T013 [P] [US1] `backend/tests/Grimoire.IntegrationTests/HubCliCommandTests.cs` — update the `ContentPaths` property's declared type (~line 865) from `ContentRootPaths` to `IngestContentPaths`.
- [X] T014 [P] [US1] `backend/tests/Grimoire.IntegrationTests/BoardCompositeResponseTests.cs` — update the `ContentRootPaths.FromResolved(paths)` call (~line 366) to `IngestContentPaths`.
- [X] T015 [P] [US1] `backend/tests/Grimoire.IntegrationTests/PathConfiguration/IngestDispatchPathArgumentsTests.cs` — update the `ContentRootPaths.FromResolved(resolvedPaths)` call (~line 31) to `IngestContentPaths`.
- [X] T016 [P] [US1] `backend/tests/Grimoire.IntegrationTests/PathConfiguration/IngestRepoLessStartupTests.cs` — update the `ContentRootPaths.FromResolved(resolvedPaths)` call (~line 44) to `IngestContentPaths`.
- [X] T017 [US1] Verification checkpoint (depends on T002-T016): run `grep -rn "ContentRootPaths" backend --include="*.cs"` from repo root and confirm zero output (spec SC-001); run `dotnet build` from `backend/` and confirm it succeeds; run `dotnet test --filter "FullyQualifiedName~AgentArtifactNamingRuleTests"` from `backend/` and confirm it still passes with no exemption-list change (spec SC-005).

**Checkpoint**: User Story 1 is fully functional and independently testable — the type is renamed everywhere, nothing else changed.

---

## Phase 4: User Story 2 - Instruction-file paths have exactly one source (Priority: P2)

**Goal**: Remove `SystemPromptPath`, `DefaultUserPromptPath`, `PolicyPath` from `IngestContentPaths`; every caller that read them now reads `ResolvedGrimoirePaths.Ingest` directly instead.

**Independent Test**: `IngestContentPaths` declares exactly 5 fields; solution builds; full test suite passes with resolved path values unchanged (spec SC-002, SC-003, SC-004, SC-006).

**Design note** (from research.md R2 / plan.md Test Strategy): `ResolvedGrimoirePaths` is already registered as a DI singleton in `HubHostComposition.cs` (`builder.Services.AddSingleton(resolvedPaths)`), and CLI commands resolve from the same container via `HubCliTypeRegistrar` — so every production call site below gains its new `ResolvedGrimoirePaths` dependency by DI/constructor injection with no new registration wiring required. This mirrors the pattern `LintRunCoordinator` already uses (`_paths.Lint.SystemPromptPath`).

### Implementation for User Story 2

- [X] T018 [US2] `backend/src/Grimoire.Hub/ContentRoot/IngestContentPaths.cs` — remove the `SystemPromptPath`, `DefaultUserPromptPath`, `PolicyPath` fields from the record declaration and from the `FromResolved` factory body. Retain `Root`, `TasksDir`, `IndexPath`, `LogPath`, `WriteLocksDir` unchanged.
- [X] T019 [US2] `backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs` — add a `ResolvedGrimoirePaths resolvedPaths` constructor parameter, store it in a new `_resolvedPaths` field (constructor ~line 47); replace the `_contentPaths.SystemPromptPath` / `.DefaultUserPromptPath` / `.PolicyPath` reads in the `IngestAgentRequest` construction (~lines 211-213) with `_resolvedPaths.Ingest.SystemPromptPath` / `.DefaultUserPromptPath` / `.PolicyPath`.
- [X] T020 [US2] `backend/src/Grimoire.Hub/IngestSubmission/SubmissionService.cs` — add a `ResolvedGrimoirePaths resolvedPaths` parameter to `SubmitAsync` (~line 24); replace the `contentPaths.SystemPromptPath` / `.DefaultUserPromptPath` / `.PolicyPath` reads in the `IngestAgentRequest` construction (~lines 57-59) with `resolvedPaths.Ingest.SystemPromptPath` / `.DefaultUserPromptPath` / `.PolicyPath`.
- [X] T021 [US2] `backend/src/Grimoire.Hub/Cli/SubmitSourceCommand.cs` — add a `ResolvedGrimoirePaths resolvedPaths` constructor parameter (~line 46), store it, and pass it as the new argument to `_submissionService.SubmitAsync(...)` (~line 63) added by T020.
- [X] T022 [US2] `backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs` — in `GetDefaultsAsync` (~line 209), replace the `IngestContentPaths contentPaths` parameter with `ResolvedGrimoirePaths resolvedPaths` and replace the three `contentPaths.DefaultUserPromptPath` reads (~lines 211, 215, 219, 224) with `resolvedPaths.Ingest.DefaultUserPromptPath`. The other three handlers in this file that take `IngestContentPaths` (~lines 243, 269, 373) are unaffected — they only use the five retained fields.
- [X] T023 [US2] `backend/tests/Grimoire.IntegrationTests/Fakes/IngestSubmissionPipelineFixture.cs` — restructure construction: add an optional `ResolvedGrimoirePaths? resolvedPaths = null` constructor parameter. When provided, use it directly as `ResolvedPaths` (do not reconstruct it from `ContentPaths`) and seed `resolvedPaths.Ingest.SystemPromptPath` / `.DefaultUserPromptPath` on disk if missing (mirroring the existing seeding block at ~lines 65-75, keyed off the passed-in resolved paths instead of `ContentPaths`). When not provided, build the ad-hoc instruction paths first (as today, ~lines 76-96), assemble `ResolvedPaths` from them directly, then derive `ContentPaths = IngestContentPaths.FromResolved(ResolvedPaths)` — replacing today's hand-built record literal that sets the three removed fields. Pass `ResolvedPaths` as the new argument to the internally-constructed `IngestRunCoordinator` (matching T019's new parameter).
- [X] T024 [P] [US2] `backend/tests/Grimoire.IntegrationTests/PathConfiguration/IngestDispatchPathArgumentsTests.cs` — pass `resolvedPaths: resolvedPaths` (already in local scope, ~line 30) into the `IngestSubmissionPipelineFixture` constructor call (~line 36).
- [X] T025 [P] [US2] `backend/tests/Grimoire.IntegrationTests/PathConfiguration/IngestRepoLessStartupTests.cs` — pass `resolvedPaths: resolvedPaths` (already in local scope, ~line 33) into the `IngestSubmissionPipelineFixture` constructor call (~line 48).
- [X] T026 [US2] `backend/tests/Grimoire.IntegrationTests/BoardCompositeResponseTests.cs` — add `paths` (the already-in-scope `ResolvedGrimoirePaths`, ~line 359) as a new constructor argument to the inline `IngestRunCoordinator` construction (~lines 396-402), matching T019's new parameter.
- [X] T027 [US2] `backend/tests/Grimoire.IntegrationTests/IngestSubmissionPromptApiTests.cs` — replace `fixture.ContentPaths.DefaultUserPromptPath` (~line 47) with `fixture.ResolvedPaths.Ingest.DefaultUserPromptPath`; add `services.AddSingleton(fixture.ResolvedPaths);` to `BuildHostAsync`'s DI registrations (~line 129 area) so the `GetDefaultsAsync` endpoint's new `ResolvedGrimoirePaths` parameter (T022) resolves in this test's host.
- [X] T028 [US2] Verification checkpoint (depends on T018-T027): run `dotnet build` from `backend/` and confirm success; run `dotnet test` from `backend/` and confirm the full suite passes (spec SC-004); confirm `IngestContentPaths` declares exactly 5 fields with none of the three removed names present anywhere on the type (spec SC-002, SC-003); run `dotnet test --filter "FullyQualifiedName~IngestDispatchPathArgumentsTests|FullyQualifiedName~CustomAgentDirEndToEndTests"` and confirm resolved path values in assertions are unchanged (spec SC-006).

**Checkpoint**: User Stories 1 AND 2 both work — the type is renamed and carries only the wiki-root/write-lock fields; instruction-file paths have exactly one source everywhere.

---

## Phase 5: Polish & Completeness Audit

- [X] T029 Completeness audit (MANDATORY — Constitution Principle III/IV): plan.md `## Observability` declares zero metric, log-event, or trace-span rows, and spec.md declares zero agent-judgment success criteria for this feature — confirmed by re-reading both documents against the final diff. No gap exists to file as a follow-up task. Also re-run the full `quickstart.md` validation sequence (all 6 steps) end-to-end and confirm spec.md Success Criteria SC-001 through SC-006 all pass, closing the Definition of Done for this feature.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** (structural gate): No dependencies — run first.
- **Phase 1/2** (Setup/Foundational): Empty — nothing blocks User Story 1.
- **Phase 3 (US1)**: Depends on Phase 0 only. T002-T016 are mutually parallel (16 distinct files); T017 depends on all of them.
- **Phase 4 (US2)**: Depends on Phase 3 being complete (every call site must already compile against `IngestContentPaths` before its fields are removed). T018 first (removes the fields the rest react to); T019-T022 depend on T018 and touch distinct files (parallel-safe against each other once T018 lands, though listed sequentially above since T021 depends on T020's new `SubmitAsync` signature); T023 depends on T018; T024-T026 depend on T023 (fixture's new parameter must exist first); T027 depends on T022 (endpoint's new parameter). T028 depends on all of Phase 4.
- **Phase 5** (Polish): Depends on Phase 4 completion.

### Parallel Opportunities

- T002-T016 (Phase 3, all `[P]`) — 15 independent files, safe to assign to different agents simultaneously; the phase is only "done" once all are applied together and T017 passes.
- T024, T025 (Phase 4, `[P]`) — two independent test-file edits, both depending only on T023.
- T019, T020, T022 touch different production files but should land together with T018 in the same review pass since they share the same underlying field-removal; T021 strictly depends on T020's new method signature.

---

## Parallel Example: User Story 1

```text
# Launch together once Phase 0 (T001) is confirmed:
T002 backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs → IngestContentPaths.cs
T003 backend/src/Grimoire.Hub/HubHostComposition.cs
T004 backend/src/Grimoire.Hub/IngestSubmission/SubmissionService.cs
T005 backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionPipeline.cs
T006 backend/src/Grimoire.Hub/IngestSubmission/IngestSubmissionEndpoints.cs
T007 backend/src/Grimoire.Hub/IngestSubmission/BoardEndpoints.cs
T008 backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs
T009 backend/src/Grimoire.Hub/Cli/IngestResumeCommand.cs
T010 backend/src/Grimoire.Hub/Cli/IngestRetriggerCommand.cs
T011 backend/src/Grimoire.Hub/Cli/SubmitSourceCommand.cs
T012 backend/tests/Grimoire.IntegrationTests/Fakes/IngestSubmissionPipelineFixture.cs
T013 backend/tests/Grimoire.IntegrationTests/HubCliCommandTests.cs
T014 backend/tests/Grimoire.IntegrationTests/BoardCompositeResponseTests.cs
T015 backend/tests/Grimoire.IntegrationTests/PathConfiguration/IngestDispatchPathArgumentsTests.cs
T016 backend/tests/Grimoire.IntegrationTests/PathConfiguration/IngestRepoLessStartupTests.cs
# Then T017 verifies the whole set together.
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 0 (baseline gate).
2. Complete Phase 3 (User Story 1 — the rename).
3. **STOP and VALIDATE**: T017's grep/build/N1-test checks all pass.
4. This alone already delivers spec.md's primary naming benefit and is safe to ship independently — spec.md explicitly designed US1 to stand alone.

### Incremental Delivery

1. Phase 0 → guard confirmed live.
2. Phase 3 (US1) → validate independently → mergeable on its own.
3. Phase 4 (US2) → validate independently → mergeable on its own.
4. Phase 5 → completeness audit closes the Definition of Done.

### Single-PR Delivery (recommended for this feature)

Given the small, tightly-coupled scope (27 implementation/verification tasks across 16 files touched twice each at most), landing Phases 0/3/4/5 together in one PR — as the source issue #56 itself frames this as "a standalone mechanical-cleanup PR" — is reasonable and avoids an intermediate state where `IngestContentPaths` still carries fields nothing needs. Splitting into two PRs (after T017, after T028) is equally valid if preferred; both are independently testable per spec.md.

---

## Notes

- [P] tasks = different files, no dependencies among the marked set.
- [Story] label maps each task to spec.md's US1 or US2 for traceability.
- No test-writing tasks precede implementation (unlike the template's TDD example): this feature modifies existing passing tests' *references*, it does not add new behavior to test-first. The verification checkpoints (T017, T028) are where correctness is proven.
- Line numbers in task descriptions are `~` (approximate, as of spec-time inspection) — treat them as a locator, not a guarantee; re-read each file before editing.
- Commit after each phase (or after each checkpoint task) rather than after every individual file task, to keep the build green at each commit per the constitution's zero-behavior-change requirement.

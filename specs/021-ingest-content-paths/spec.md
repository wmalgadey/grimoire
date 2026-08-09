# Feature Specification: Rename ContentRootPaths to an Ingest-Specific Type

**Feature Branch**: `claude/grimoire-issue-56-h4s1se` (spec directory `021-ingest-content-paths`)

**Created**: 2026-08-07

**Status**: Implemented (merged 2026-08-08, PR #57 / 86cdb74)

**Input**: User description: "resolve https://github.com/wmalgadey/grimoire/issues/56 — Rename ContentRootPaths to an Ingest-specific name and drop its duplicated instruction-path fields. Follow-up from PR #55 review (ADR-022 / 020-simplify-hub-config). ContentRootPaths (backend/src/Grimoire.Hub/ContentRoot/ContentRootPaths.cs) is Ingest-only in practice — consumed by SubmissionService, IngestSubmissionPipeline, IngestRunCoordinator, and the ingest CLI commands (SubmitSourceCommand, IngestRetriggerCommand, IngestResumeCommand) — but its name doesn't say so, unlike LintRunCoordinator/QueryRunCoordinator which already follow the per-agent naming convention (docs/conventions/agent-artifact-naming.md, rule N1). Two changes: (1) drop the duplicated instruction-path fields SystemPromptPath/DefaultUserPromptPath/PolicyPath, which re-copy what ResolvedGrimoirePaths.Ingest already carries; (2) rename the type to something Ingest-specific (e.g. IngestContentPaths)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A type's name tells its owner on sight (Priority: P1)

A maintainer browsing `Grimoire.Hub.ContentRoot`, reading a dependency-injection registration, or following a compiler error sees the projection type that carries wiki-root and write-lock paths. Today that type is named `ContentRootPaths`, giving no hint that — in practice — only Ingest-owned code (`SubmissionService`, `IngestSubmissionPipeline`, `IngestRunCoordinator`, and the ingest CLI commands) ever consumes it. Its sibling coordinators, `LintRunCoordinator` and `QueryRunCoordinator`, already carry their owning agent's name; this type should too.

**Why this priority**: This is the naming gap the issue exists to close, and it is the change a reviewer or future contributor benefits from directly — reading the type name is enough to know its owner, without tracing every call site. It brings the type in line with the Ubiquitous Language the constitution already requires elsewhere in the codebase (rule N1).

**Independent Test**: Can be fully tested by renaming the type (and its source file) to carry the "Ingest" token, updating every reference across production code and tests, and confirming the solution builds with zero remaining references to the old name.

**Acceptance Scenarios**:

1. **Given** the renamed type, **When** a developer searches the C# codebase for the identifier `ContentRootPaths`, **Then** no production or test code references it — only the new Ingest-specific name is found.
2. **Given** the renamed type still lives in the cross-agent `Grimoire.Hub.ContentRoot` namespace, **When** the existing per-agent naming architecture rule (N1) runs, **Then** it passes without requiring any addition to the rule's exemption list.

---

### User Story 2 - Instruction-file paths have exactly one source (Priority: P2)

A developer working on Ingest needs the system-prompt, default-user-prompt, or policy file paths. Today those three values exist in two places at once: copied onto the wiki-root projection type, and on `ResolvedGrimoirePaths.Ingest` (the single-composition-point record introduced by ADR-022). Both copies are always populated from the same resolution, but carrying the same three values on two types is redundant surface a reader has to reconcile, and the copy hides that `ResolvedGrimoirePaths.Ingest` was always the authoritative source. After this change, every consumer reads those three paths directly from `ResolvedGrimoirePaths.Ingest`, and the wiki-root projection type no longer declares them at all.

**Why this priority**: This removes the redundant fields the issue calls out, but it depends on nothing else being renamed first — it can land independently of Story 1, and unlike Story 1 it changes what the type's shape guarantees (fewer fields), which is why it sits second: Story 1 alone already delivers the naming benefit even if this one were skipped.

**Independent Test**: Can be fully tested by removing the `SystemPromptPath`, `DefaultUserPromptPath`, and `PolicyPath` fields from the projection type, updating every call site that previously read them from the projection to instead read `ResolvedGrimoirePaths.Ingest.SystemPromptPath` / `.DefaultUserPromptPath` / `.PolicyPath`, and confirming the solution builds and the full test suite passes.

**Acceptance Scenarios**:

1. **Given** the updated projection type, **When** a developer inspects its declared fields, **Then** only `Root`, `TasksDir`, `IndexPath`, `LogPath`, and `WriteLocksDir` remain — the three instruction-path fields are gone.
2. **Given** a call site that previously read an instruction-file path from the projection type, **When** that call site is exercised (e.g. dispatching an ingest run, validating a submitted document exists), **Then** it resolves the same path value as before, now sourced from `ResolvedGrimoirePaths.Ingest`.

---

### Edge Cases

- What happens to historical prose that names the old type — `specs/020-simplify-hub-config/research.md` (decision R9), past PR review comments, and this issue itself? These describe a point-in-time decision and are not rewritten; only live code, live tests, and documentation that states current, binding naming rules (e.g. `docs/conventions/agent-artifact-naming.md`) are updated if they reference the current type name.
- What happens to doc comments that name the old type? Two are expected to survive and are exempt from SC-001: the comment on `ResolvedGrimoirePaths` describing it as replacing "the former `ContentRootPaths`" (pre-ADR-022 history, accurate regardless of the current name), and the comment the renamed type carries recording its own rename ("Renamed from `ContentRootPaths` (021-ingest-content-paths)"), which is the in-code provenance a reader hitting the new name needs. Both describe history; neither is a code reference.
- What happens to `RawStoragePaths`, named alongside `ContentRootPaths` in research.md R9 as a similarly out-of-scope projection type? It is unaffected — this issue scopes the rename to `ContentRootPaths` only.
- What happens to generated/derived artifacts that embed the old type name (e.g. `docs/code-complexity-analysis.json`)? They are regenerated by their producing tool on its next run, never hand-edited, and are out of scope for SC-001 — which scopes to `backend/**/*.cs` only.
- What happens to a call site that needs the wiki-root fields (`Root`, `TasksDir`, `IndexPath`, `LogPath`, `WriteLocksDir`) and previously got them from the same projection instance it also used for instruction paths? It keeps reading the wiki-root fields from the renamed projection type and additionally takes `ResolvedGrimoirePaths` (or its `Ingest` member) as a separate input for the instruction paths, since both are already available together wherever the projection is constructed today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST rename the `ContentRootPaths` record type, and its source file, to a name carrying the "Ingest" token (e.g. `IngestContentPaths`), consistent with rule N1 of `docs/conventions/agent-artifact-naming.md`.
- **FR-002**: The renamed type MUST remain declared in the `Grimoire.Hub.ContentRoot` namespace; the rename MUST change only the type's name, not its namespace or file location convention.
- **FR-003**: The renamed type MUST remove the `SystemPromptPath`, `DefaultUserPromptPath`, and `PolicyPath` fields, since their values are already available, unduplicated, via `ResolvedGrimoirePaths.Ingest`.
- **FR-004**: The renamed type's `FromResolved` factory MUST continue to project `Root`, `TasksDir`, `IndexPath`, `LogPath`, and `WriteLocksDir` from `ResolvedGrimoirePaths` unchanged, and MUST NOT populate the removed fields.
- **FR-005**: Every production call site that previously read `SystemPromptPath`, `DefaultUserPromptPath`, or `PolicyPath` from the projection type — exactly three: `SubmissionService`, `IngestRunCoordinator`, and `IngestSubmissionEndpoints`'s `GetDefaultsAsync` handler — MUST be updated to read the same value from `ResolvedGrimoirePaths.Ingest` instead. `SubmitSourceCommand` MUST additionally accept `ResolvedGrimoirePaths` solely to forward it to `SubmissionService.SubmitAsync`'s widened signature; it reads none of the three values itself.
- **FR-006**: Every production reference to the old type name — including dependency-injection registration and any constructor, field, or parameter typed as `ContentRootPaths` — MUST be updated to reference the renamed type. This covers the call sites that consume only the five retained fields and are therefore untouched by FR-005: `IngestSubmissionPipeline`, `BoardEndpoints`, `IngestRetriggerCommand`, `IngestResumeCommand`, and `IngestSubmissionEndpoints`'s three non-`/defaults` handlers.
- **FR-007**: Every test that constructs, injects, or asserts against the projection type or its removed fields (including `IngestSubmissionPipelineFixture`, `HubCliCommandTests`, `BoardCompositeResponseTests`, and the `PathConfiguration` tests that exercise it) MUST be updated to use the renamed type and to read instruction-file paths from `ResolvedGrimoirePaths.Ingest`.
- **FR-008**: The solution MUST build, and the full backend test suite MUST pass, with zero remaining *code* references to the identifier `ContentRootPaths` — declarations, type-typed parameters and fields, DI registrations and lookups, and factory calls — or to the removed field names on the renamed type, anywhere in production or test C# source. Doc-comment mentions of the former name are exempt (see Edge Cases).
- **FR-009**: This change MUST NOT alter any observable behavior — resolved path values, CLI output, HTTP responses, log fields, or trace attributes MUST be identical before and after the rename; only identifiers and field membership change.
- **FR-010**: Any `ResolvedGrimoirePaths` (or other DI-resolved service) parameter added to a minimal-API endpoint handler MUST carry an explicit `[FromServices]` attribute. Without it, ASP.NET Core's `RequestDelegateFactory` infers `[FromBody]` at route-matcher-build time when it cannot confirm the type's DI-service status, which throws eagerly for every host that maps the endpoint group — not only for requests that reach the handler.

### Key Entities

- **IngestContentPaths** (renamed from `ContentRootPaths`): The Ingest-owned projection of wiki-root and write-lock locations — `Root`, `TasksDir`, `IndexPath`, `LogPath`, `WriteLocksDir` — derived from `ResolvedGrimoirePaths`. No longer duplicates instruction-file paths.
- **ResolvedGrimoirePaths.Ingest** (`AgentRuntimePaths`, pre-existing, introduced by ADR-022): The single-composition-point source for Ingest's instruction-file paths (`SystemPromptPath`, `DefaultUserPromptPath`, `PolicyPath`). Becomes the only path every consumer uses for these three values.

## Success Criteria *(mandatory)*

<!--
  All criteria below are deterministic harness guarantees (Constitution Principle II).
  This is a pure internal rename/de-duplication with no user-facing or agent-judgment
  behavior change, so no evaluation-threshold criteria apply.
-->

### Measurable Outcomes

- **SC-001**: 100% of production and test C# source files contain zero *code* occurrences of the identifier `ContentRootPaths`; the two surviving occurrences are doc comments (`ResolvedGrimoirePaths.cs`, `IngestContentPaths.cs`) exempted by Edge Cases and excluded by the verification command in `quickstart.md` §1.
- **SC-002**: 100% of production and test call sites that previously read `SystemPromptPath`, `DefaultUserPromptPath`, or `PolicyPath` from the projection type now read the same values from `ResolvedGrimoirePaths.Ingest`, with the renamed type declaring none of those three fields.
- **SC-003**: The renamed type's declared field count is exactly 5 (`Root`, `TasksDir`, `IndexPath`, `LogPath`, `WriteLocksDir`), down from 8.
- **SC-004**: 100% of the pre-existing backend test suite (architecture, unit, integration) passes after the change, with no test outcome altered beyond mechanical reference updates.
- **SC-005**: The N1 agent-artifact-naming architecture test passes with 0 additions to its doc-mirrored exemption list.
- **SC-006**: 100% of resolved path values observable at existing call sites (CLI output, HTTP responses, dispatched process arguments) are byte-identical before and after the change.

## Assumptions

- The new type name is `IngestContentPaths`, the example the source issue itself proposes, and the file is renamed to match (`ContentRootPaths.cs` → `IngestContentPaths.cs`) per existing repository convention of one public type per matching file name.
- The renamed type stays in the `Grimoire.Hub.ContentRoot` namespace. That namespace is already listed as cross-agent in the ownership map and, per the map's own rule, cross-agent namespaces may host per-agent-named types of shared infrastructure (mirroring `Realtime`'s `IngestLifecycleHub` and `Cli`'s `LintRunCommand`) — so no namespace relocation or exemption-list change is required.
- `RawStoragePaths` — named alongside `ContentRootPaths` in `specs/020-simplify-hub-config/research.md` decision R9 as a similarly deferred projection type — is out of scope. The source issue scopes this change to `ContentRootPaths` only.
- This is a mechanical, behavior-preserving refactor: no new structural boundary, external-system dependency, or cross-cutting concern is introduced, so no new ADR is required beyond the existing ADR-013 (naming convention) and ADR-022 (single-composition-point paths) it already conforms to.
- No agent-judgment behavior is touched, so Constitution Principle V's agentic-core boundary is unaffected and every success criterion above is a deterministic guarantee (Principle II) rather than an evaluation threshold.
- No new observability signals (metrics, log events, trace spans) are introduced or removed by this change; existing ones are unaffected because resolved path values do not change.

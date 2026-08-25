# Quickstart: Validating the Host Stability Guarantee

**Feature**: `specs/027-host-stability/spec.md` | **Date**: 2026-08-25

This feature is entirely backend/harness-side (no agentic surface, no frontend change).
Validation is running the described test suites against real infrastructure (a real
filesystem, real symlinks, the real compiled `Grimoire.Hub.dll`) — no live LLM calls, no
API key, per Constitution Principle II.

## Prerequisites

- .NET SDK matching `backend/global.json`.
- Repository built once so `Grimoire.Hub.dll`/`Grimoire.*Agent.dll` exist for the
  ArchTests project's assembly scan (`dotnet build backend/Grimoire.sln`).
- Linux or another POSIX filesystem with symlink support (`File.CreateSymbolicLink`) —
  the existing `PathTraversalTests` already assume this; no new prerequisite.

## Validate path containment (User Story 1, FR-001/002)

```bash
cd backend
dotnet test tests/Grimoire.IntegrationTests \
  --filter "FullyQualifiedName~PathTraversalTests|FullyQualifiedName~AdversarialPathContainmentTests"
```

**Expected outcome**: every adversarial variant (plain traversal, absolute override,
symlink escape, chained/nested symlink, null-byte, post-validation symlink swap) is
denied; the file outside the root is provably untouched (its content is unchanged); no
unhandled exception propagates out of the guarded tool call (the null-byte case in
particular must return a normal `is_error` tool result, not crash the run).

## Validate the spawn-site registry (User Story 2, FR-003/004/005)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests --filter "FullyQualifiedName~SpawnSiteRegistry"
```

**Expected outcome (Green)**: the test passes against the current codebase — exactly
`AgentProcessHost` and `MarkItDownConverter` construct a `Process`, and neither uses the
shell-parsed `Arguments` string property.

**Red/Green probe** (Phase 0 task, per Constitution Principle III — done once during
implementation, not part of ordinary CI):

1. Temporarily add a `Process.Start(new ProcessStartInfo("sh", "-c \"" + someInput +
   "\""))` call inside an unrelated Hub class (e.g. a scratch method in
   `IngestSubmissionEndpoints`).
2. Re-run the filter above — the test MUST fail, naming the new, unlisted call site.
3. Delete the scratch call site; re-run — the test is Green again.

## Validate the extension allowlist (FR-005)

```bash
cd backend
dotnet test tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~IngestSubmissionValidator"
```

**Expected outcome**: a submission whose filename carries an unlisted extension (e.g.
`.exe`, `.sh`) is rejected by validation before any conversion or storage code runs.

## Full suite

```bash
cd backend
dotnet test Grimoire.sln
```

All existing tests remain green — this feature hardens existing mechanisms and adds
tests; it does not change any externally observable API, CLI surface, or wiki-content
behavior. There is no manual/UI validation step: the feature has no frontend surface and
no agentic behavior to exercise interactively (Constitution Principle V: harness-only).

# Quickstart: Validate the ContentRootPaths → IngestContentPaths Rename

Run from the repository root, after implementation is complete.

## Prerequisites

- .NET 10.0 SDK on PATH (matches `backend/Directory.Build.props`).
- No other setup — this feature adds no new dependency, service, or fixture.

## 1. Confirm no dangling references to the old name (SC-001)

```bash
grep -rn "ContentRootPaths" backend --include="*.cs" | grep -v '^\s*[^:]*:[0-9]*:\s*///'
```

Expected: no output. Any match is a missed reference to fix before proceeding. The
unfiltered grep returns two doc-comment lines by design — `ResolvedGrimoirePaths.cs`
(pre-ADR-022 history) and `IngestContentPaths.cs` (the renamed type's own provenance
note), both exempted by spec.md Edge Cases; the `///` filter drops them.

## 2. Confirm the removed fields are gone from the renamed type (SC-002, SC-003)

```bash
grep -n "SystemPromptPath\|DefaultUserPromptPath\|PolicyPath" \
  backend/src/Grimoire.Hub/ContentRoot/IngestContentPaths.cs | grep -v '^[0-9]*:\s*///'
```

Expected: no output — the renamed type declares only `Root`, `TasksDir`, `IndexPath`,
`LogPath`, `WriteLocksDir`. As in §1, the `///` filter drops the type's own doc comment,
which names the three removed fields while pointing readers at
`ResolvedGrimoirePaths.Ingest` — that pointer is the reason the comment exists and is
exempt per spec.md Edge Cases.

## 3. Build the solution

```bash
cd backend && dotnet build
```

Expected: builds clean. A compile error naming a removed field or the old type is the
fast-fail signal that a call site was missed (this is the real proof behind SC-001/SC-002
— the grep above is a convenience pre-check, not a substitute).

## 4. Run the full backend test suite (SC-004)

```bash
cd backend && dotnet test
```

Expected: 100% of previously-passing tests still pass. No new tests are added by this
feature; the bar is zero regressions.

## 5. Run the N1 naming architecture test specifically (SC-005)

```bash
cd backend && dotnet test --filter "FullyQualifiedName~AgentArtifactNamingRuleTests"
```

Expected: passes, including `HubNamespaces_MustFollowTheOwnershipMap` and
`ExemptionFixture_MustMirror_TheConventionDocument` — confirming the rename required no
change to the ownership map or the exemption list.

## 6. Spot-check resolved path values are unchanged (SC-006)

```bash
cd backend && dotnet test --filter "FullyQualifiedName~IngestDispatchPathArgumentsTests|FullyQualifiedName~CustomAgentDirEndToEndTests"
```

Expected: passes with existing assertions on exact resolved path strings unmodified in
their expected values — only their source reference (`IngestContentPaths` /
`ResolvedGrimoirePaths.Ingest`) changed.

## Done

All six steps passing is the complete Definition of Done for this feature — see
`spec.md`'s Success Criteria for the mapping and `plan.md`'s Test Strategy for how each
step was chosen.

# Quickstart: Grimoire Project Skeleton Validation

**Feature**: `001-grimoire-scaffold` | **Phase**: 1 | **Date**: 2026-06-23

This guide validates that the skeleton is correctly implemented end-to-end. It covers prerequisites, build commands, architecture test execution, and CI verification.

---

## Prerequisites

| Requirement | Version | Check |
|-------------|---------|-------|
| .NET SDK | 9.0+ | `dotnet --version` |
| Node.js | 20+ | `node --version` |
| npm | 10+ | `npm --version` |
| Git | any | `git --version` |

---

## Scenario 1 — Backend Compiles (US1-AC1, SC-001)

```bash
cd src/backend
dotnet build
```

**Expected**: Output ends with `Build succeeded. 0 Error(s) 0 Warning(s)`. All three projects (`Grimoire.Api`, `Grimoire.Core`, `Grimoire.ArchTests`) compile.

---

## Scenario 2 — Frontend Bundles (US1-AC2, SC-002)

```bash
cd src/frontend
npm install
npm run build
```

**Expected**: Output ends with `✓ built in ...ms` with no TypeScript or Svelte errors. `dist/` directory is created.

---

## Scenario 3 — Architecture Tests Pass (US1-AC3, US2-AC1–AC4, SC-003, SC-004)

```bash
cd src/backend
dotnet test Grimoire.ArchTests --logger "console;verbosity=normal"
```

**Expected**:
- All tests pass (exit code 0)
- Test run completes in < 30 seconds
- Output shows passing tests for each ADR constraint:
  - `Core_HasNoDependencyOnInfrastructure` — ADR-005, ADR-007
  - `ChannelImplementations_MustImplementIChannel` — ADR-004
  - `AgentWorkers_MustImplementIAgentWorker` — ADR-002
  - `BackendApi_TargetsNet9` — ADR-001
  - `Core_DefinesIChannelInCorrectNamespace` — ADR-004
  - `Core_DefinesIAgentWorkerInCorrectNamespace` — ADR-002

---

## Scenario 4 — Interfaces Meet Skeleton Contract (SC-007)

Verify `IChannel` and `IAgentWorker` comply with the 3-method-signature limit:

```bash
grep -c "Task\|string " src/backend/Grimoire.Core/Channels/IChannel.cs
grep -c "Task\|string " src/backend/Grimoire.Core/Agents/IAgentWorker.cs
```

**Expected**: Each interface has at most 3 members (1 property + 2 methods). See [contracts/IChannel.cs](contracts/IChannel.cs) and [contracts/IAgentWorker.cs](contracts/IAgentWorker.cs) for the reference definitions.

---

## Scenario 5 — CI Workflows Trigger Correctly (US3-AC1–AC3)

Push a commit that touches only `src/backend/`:

```bash
git add src/backend/
git commit -m "test: verify CI path filter"
git push origin 001-grimoire-scaffold
```

**Expected**:
- GitHub Actions `backend.yml` workflow triggers
- GitHub Actions `frontend.yml` workflow does NOT trigger (path filter)
- Both `dotnet build` and `dotnet test Grimoire.ArchTests` pass in the backend workflow

To verify the frontend workflow path filter, push a frontend-only change:

```bash
git add src/frontend/
git commit -m "test: verify frontend CI path filter"
git push origin 001-grimoire-scaffold
```

**Expected**: Only `frontend.yml` triggers.

---

## Scenario 6 — Host Startup Logging (Observability)

```bash
cd src/backend
dotnet run --project Grimoire.Api
```

**Expected log output** (structured JSON or plain text depending on environment):
```
info: Program[0] grimoire.host.started environment=Development version=0.1.0-skeleton
```

Stop with `Ctrl+C`:

**Expected**:
```
info: Program[0] grimoire.host.stopped environment=Development
```

---

## Structural Spot-Check

Verify directory layout matches ADR-005:

```bash
ls src/                  # backend  frontend  agents
ls src/backend/          # Grimoire.sln  Directory.Build.props  Grimoire.Api  Grimoire.Core  Grimoire.ArchTests
ls src/agents/           # .gitkeep
ls .github/workflows/    # backend.yml  frontend.yml
ls docs/adr/             # adr-001.md ... adr-007.md  README.md
```

---

## Definition of Done Checklist

- [ ] `dotnet build src/backend` — 0 errors, 0 warnings (SC-001)
- [ ] `npm run build` in `src/frontend` — 0 errors, 0 warnings (SC-002)
- [ ] `dotnet test Grimoire.ArchTests` — all pass, < 30s (SC-003, SC-004)
- [ ] Backend CI workflow completes < 3 min; frontend < 2 min (SC-005)
- [ ] No files in wrong directories (SC-006)
- [ ] `IChannel` and `IAgentWorker` ≤ 3 method signatures each (SC-007)
- [ ] `grimoire.host.started` and `grimoire.host.stopped` log events emitted (Observability)
- [ ] All ADRs 001–007 have a corresponding passing architecture test (Constitution Principle III)

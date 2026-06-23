# Quickstart: Validation & Testing Guide

**Phase**: 1 (Design)  
**Date**: 2026-06-23

This document provides the validation steps to verify that the domain-driven refactoring has been successfully completed.

## Prerequisites

- .NET 9 SDK installed
- Project cloned and dependencies restored
- All existing tests passing before refactoring begins (baseline)

## Validation Steps

### Step 1: Verify Directory Structure

**Command**: Inspect the project directory structure

```bash
ls -la src/backend/Grimoire.Api/
```

**Expected Output**:
```
Agents/
Hubs/
Channels/
Shared/
Grimoire.Api.csproj
```

**Why**: Verifies that the domain-based folder structure has been created.

---

### Step 2: Verify Namespace Organization

**Command**: Grep for namespace declarations

```bash
grep -r "^namespace Grimoire.Api\." src/backend/Grimoire.Api --include="*.cs" | head -30
```

**Expected Output**:
```
Grimoire.Api.Agents.Endpoints
Grimoire.Api.Agents.Handlers
Grimoire.Api.Agents.Services
Grimoire.Api.Hubs.Endpoints
Grimoire.Api.Channels.Services
Grimoire.Api.Shared.Middleware
Grimoire.Api.Shared.Observability
Grimoire.Api.Shared.Persistence
...
```

**Validation Criteria**:
- Minimum 80% of namespaces follow `Grimoire.Api.{Domain}.*` pattern
- All exceptions documented for non-compliant namespaces

**Why**: Ensures namespace consistency reflects domain organization.

---

### Step 3: Run Architecture Tests

**Command**: Execute the architecture test

```bash
cd src/backend/Grimoire.Api
dotnet test Grimoire.Api.Tests --filter "TestClass=ArchitectureTests"
```

**Expected Output**:
```
Test Run Successful.
Total tests: 3 passed, 0 failed
```

**Architecture Test Validates**:
- ✅ No circular dependencies between Agents, Hubs, Channels
- ✅ All types in expected namespaces
- ✅ Cross-domain communication via interfaces only

**Why**: Automated enforcement ensures architectural constraints are not violated.

---

### Step 4: Run All Unit Tests

**Command**: Execute unit test suite

```bash
cd src/backend/Grimoire.Api
dotnet test Grimoire.Api.Tests --filter "Category=Unit"
```

**Expected Result**: All tests pass

**Validation Criteria**:
- Zero test failures
- Zero test logic modifications (tests unchanged from refactoring)
- All domain-specific unit tests (Agents/Unit/*, Hubs/Unit/*, Channels/Unit/*) passing

**Why**: Confirms that business logic is preserved; refactoring is purely structural.

---

### Step 5: Run All Integration Tests

**Command**: Execute integration test suite

```bash
cd src/backend/Grimoire.Api
dotnet test Grimoire.Api.Tests --filter "Category=Integration"
```

**Expected Result**: All tests pass

**Validation Criteria**:
- Zero test failures
- Cross-domain integration scenarios pass (Hubs ↔ Agents, Hubs ↔ Channels)
- API endpoints respond as before (zero breaking changes)

**Why**: Ensures end-to-end functionality is preserved across domain boundaries.

---

### Step 6: Verify API Contracts Unchanged

**Command**: Run application and check endpoint behavior

```bash
cd src/backend/Grimoire.Api
dotnet run
```

**Manual Test** (in another terminal):

```bash
# Example: Test an agent endpoint
curl -X GET http://localhost:5000/api/agents/status

# Expected Response: Same as before refactoring (status 200, same JSON structure)
```

**Validation Criteria**:
- HTTP endpoints respond to same URLs
- Request/response payloads unchanged
- Status codes unchanged (no 404s for previously working endpoints)

**Why**: Zero breaking changes to external clients; API contracts preserved.

---

### Step 7: Verify No Code Duplication in Shared

**Command**: Check for duplicated infrastructure code

```bash
# Look for multiple Middleware classes across domains
find src/backend/Grimoire.Api/*/Middleware -name "*.cs" 2>/dev/null | wc -l

# Look for multiple Observability classes across domains
find src/backend/Grimoire.Api/*/Observability -name "*.cs" 2>/dev/null | wc -l

# Look for multiple Persistence classes across domains
find src/backend/Grimoire.Api/*/Persistence -name "*.cs" 2>/dev/null | wc -l
```

**Expected Output**: 0 (all infrastructure in Shared/)

**Why**: Ensures shared infrastructure is truly centralized, preventing duplication.

---

### Step 8: Verify Cross-Domain Communication Uses Interfaces

**Command**: Check for direct class references between domains

```bash
# Search for cross-domain direct references (e.g., Agents importing from Hubs directly)
grep -r "using Grimoire.Api.Agents\." src/backend/Grimoire.Api/Hubs --include="*.cs"
grep -r "using Grimoire.Api.Channels\." src/backend/Grimoire.Api/Hubs --include="*.cs"
grep -r "using Grimoire.Api.Hubs\." src/backend/Grimoire.Api/Agents --include="*.cs"
```

**Expected Output**: Empty (no cross-domain imports except via interfaces)

**Why**: Confirms architectural constraint: no direct class references between domains.

---

### Step 9: Full Build Validation

**Command**: Build the entire project

```bash
cd src/backend
dotnet build -c Release
```

**Expected Output**: Build successful, zero warnings (related to refactoring)

**Validation Criteria**:
- ✅ No compilation errors
- ✅ No new warnings introduced by namespace changes

**Why**: Final confirmation that all namespace updates and project references are correct.

---

### Step 10: Commit & Branch Check

**Command**: Verify git status after refactoring

```bash
git status
```

**Expected**: All changes related to reorganization (file moves, namespace updates)

**Command**: Run CI/CD pipeline

```bash
# Trigger your CI/CD (example: GitHub Actions)
git push
```

**Expected**: All CI/CD gates pass:
- ✅ Architecture tests pass
- ✅ Unit tests pass
- ✅ Integration tests pass
- ✅ Build succeeds
- ✅ Linting passes

---

## Success Criteria Summary

| Criterion | Status | How to Verify |
|-----------|--------|---------------|
| Domain folders exist | ✅ | `ls -la Agents/ Hubs/ Channels/ Shared/` |
| Namespace compliance | ✅ | grep `Grimoire.Api.{Domain}.*` (80%+) |
| No circular dependencies | ✅ | Architecture test passes |
| All unit tests pass | ✅ | `dotnet test --filter "Category=Unit"` |
| All integration tests pass | ✅ | `dotnet test --filter "Category=Integration"` |
| API contracts unchanged | ✅ | Endpoints respond identically |
| No shared infrastructure duplication | ✅ | Middleware/Observability/Persistence in Shared/ only |
| Cross-domain via interfaces only | ✅ | No direct domain-to-domain imports |
| Build succeeds | ✅ | `dotnet build -c Release` |
| CI/CD gates all pass | ✅ | All automated checks green |

---

## Troubleshooting

### Issue: Architecture Test Fails with "Circular Dependency"

**Cause**: Two domains import from each other

**Resolution**: Review cross-domain imports; ensure one domain only consumes interface from the other (via `Grimoire.Core.*` interfaces, not concrete classes)

### Issue: Tests Fail After Namespace Updates

**Cause**: Test project namespaces not updated or test files not reorganized

**Resolution**: Verify test project structure mirrors source structure; update test namespaces to `Grimoire.Api.Tests.{Category}.{Domain}.*`

### Issue: Endpoints Return 404 After Refactoring

**Cause**: Endpoint registration not updated to reflect new namespace organization

**Resolution**: Verify endpoint registration in `Program.cs` uses correct namespaces after refactoring

### Issue: Build Fails with "Cannot find namespace"

**Cause**: Namespace declarations or using statements not fully updated

**Resolution**: Run find-and-replace verification; check for stale imports from old layer-based namespaces

---

## Success Declaration

Once all steps above are complete and passing, the refactoring is **DONE**:

- ✅ Code organization reflects business domains
- ✅ All tests pass without modifications
- ✅ No breaking changes to API contracts
- ✅ Architecture constraints enforced and validated
- ✅ CI/CD gates all green

Ready for merge to main branch.

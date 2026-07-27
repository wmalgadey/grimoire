# Quickstart: Validating the Unified Agent Platform & Naming Convention

**Feature**: `010-unified-agent-platform` | **Date**: 2026-07-27

This feature is invisible when it works (US3). Validation therefore consists of
proving (1) the structural rules bite, (2) nothing observable changed, and (3) the
convention document exists and matches reality.

## Prerequisites

- .NET 10 SDK; repository checked out on `010-unified-agent-platform`.
- No API key and no network access are required — every check below is hermetic
  (replay evals use the versioned recordings under `data/evals/recordings/`).

## 1. Full pre-existing suite passes (SC-003)

```bash
cd backend
dotnet build
dotnet test    # runs Grimoire.ArchTests, Grimoire.IntegrationTests,
               # Grimoire.Domain.UnitTests, Grimoire.AgentEvals (incl. replay evals)
```

**Expected**: everything green, zero skipped tests (ADR-012 zero-skip gate), and the
replay eval suites (`IngestReplayEvalTests`, `QueryReplayEvalTests`) pass against the
**unchanged** recordings — a `stale`/`mismatch` failure here means the consolidation
altered a fingerprinted input (prompt scaffold, policy, scenario id) and is a defect
in the change, not an occasion to re-capture (research.md R7).

**No-weakening check**: `git diff main -- backend/tests` must show only renames,
namespace/using updates, and the new N1/D1/D2 rules — no assertion edits (FR-009).

## 2. Structural rules are live (SC-001, SC-002, SC-004)

```bash
cd backend
dotnet test tests/Grimoire.ArchTests
```

**Expected**: N1 (agent-artifact naming), D1 (telemetry-bootstrap containment), D2
(model-adapter composition containment), the renamed
`IngestAgentGuardedWriteBoundaryRuleTests`, `QueryAgentGuardedWriteBoundaryRuleTests`,
and all pre-existing rules (C1–C5 etc.) pass.

**Red/Green probes** (performed once during implementation, documented in tasks.md
commit history; repeatable manually):

- N1: add `public class MisnamedEvalTests` referencing only `Grimoire.IngestAgent`
  to `Grimoire.IntegrationTests` → `dotnet test tests/Grimoire.ArchTests` fails N1 →
  delete the class → green.
- D1: paste a private `Sdk.CreateTracerProviderBuilder()` bootstrap into
  `Grimoire.QueryAgent` → D1 fails → remove → green.
- D2: construct `new AnthropicModelClient(...)` directly in
  `Grimoire.IngestAgent/Program.cs` (bypassing `ModelClientFactory`) → D2 fails →
  remove → green.

## 3. Nothing observable changed (SC-003/SC-004, FR-008)

Run one ingest and one query end-to-end with the replay adapter (no credentials):

```bash
# Ingest agent, replayed recording (paths per your local base dir; ADR-009):
GRIMOIRE_MODEL_REPLAY_PATH=data/evals/recordings/<ingest-scenario>/<sample> \
  dotnet run --project backend/src/Grimoire.IngestAgent -- <existing CLI args>

# Query agent, replayed recording, conversation JSON on stdin:
GRIMOIRE_MODEL_REPLAY_PATH=data/evals/recordings/<query-scenario>/<sample> \
  dotnet run --project backend/src/Grimoire.QueryAgent -- <existing CLI args>
```

**Expected**: NDJSON events on stdout (`started`/`heartbeat`/…/`completed`), task
artifact / behavior identical in shape to a pre-consolidation run; exit codes
unchanged. The hermetic integration suite in step 1 asserts the same shapes
automatically — this manual run is optional corroboration. Observability identities
are verified by the existing in-memory-exporter tests (plan.md ## Observability);
optionally inspect spans/metrics locally via the Aspire Dashboard (ADR-005).

## 4. Convention document exists and matches reality (SC-002, FR-005/FR-006)

```bash
cat docs/conventions/agent-artifact-naming.md
```

**Expected**: the rule, rationale, cross-agent definition, exemption list, and the
complete old→new rename map (headline: `ReplayEvalTests` → `IngestReplayEvalTests`).
Spot-check: `ls backend/tests/Grimoire.AgentEvals/` shows `IngestReplayEvalTests.cs`
next to `QueryReplayEvalTests.cs`; `grep -r "class ReplayEvalTests" backend/` is
empty. N1's exemption fixture and the document's exemption list are identical (the
test fails on drift).

## 5. Gate reminders

- ADR-013 must be **Accepted** (project-owner sign-off) before `/speckit-tasks`.
- SC-005 (zero duplicated platform code for the Lint agent) is measured when
  feature 013 lands — its plan must cite the `AgentProfile`/`AgentHost` seam and
  rules D1/D2.

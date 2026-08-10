# Quickstart: Validating Wiki Structure Truth

**Feature**: 022-align-wiki-structure | **Date**: 2026-08-10

Runnable checks that prove the feature works end to end. See
[contracts/](contracts/) for the normative definitions and [data-model.md](data-model.md) for
field shapes.

## Prerequisites

- .NET 10 SDK
- A built solution: `dotnet build backend/Grimoire.slnx`
- For the evaluation scenarios only: recorded replay fixtures (no provider credentials needed)

## 1. The reported defect, reproduced and fixed

This is the scenario from the original report: a content root holding only the four harness
surfaces, with no catalog, log, or articles.

```bash
WIKI=$(mktemp -d)
mkdir -p "$WIKI"/{tasks,conversations,findings,remediation-tasks}
```

Point a query run at it and ask what the wiki covers.

**Before**: the agent reports "the wiki is currently empty — there is no `index.md`, no
`pages/` directory, and no `log.md` file", having enumerated a folder that cannot exist.

**Expected after**: the answer reports that no articles exist yet and describes what was
actually found. It contains no reference to a `pages/` directory, and does not attribute the
emptiness to a missing folder. (SC-007)

## 2. Articles land where they belong

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~ArticlePlacement"
```

Asserts a scripted ingest run writes its article to `<content-root>/<category>/<slug>.md` with
no wrapper segment, and that the catalog line it appends links to a path that resolves to an
existing file. (SC-002)

## 3. A fresh root bootstraps itself

```bash
WIKI=$(mktemp -d)   # completely empty — no index.md, no log.md
```

Run an ingest. **Expected**: both files exist and are populated afterwards, with no operator
setup step. ADR-017's guard already permits first-write creation; this proves the instruction
change carries it through. (SC-013)

## 4. Harness surfaces are denied by default

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~HarnessSurfaceRead"
```

With nothing configured, a scripted `list_files("tasks")` and `read_file("conversations/x.md")`
are both denied with reason `harness_surface_not_granted`, each recorded as a
`DeniedActionRecord`, and the run still reaches a terminal state. (SC-010)

Then grant one surface and re-run:

```bash
Grimoire__HarnessSurfaceReads__Findings=true dotnet run --project backend/src/Grimoire.Hub
```

**Expected**: `findings/` reads succeed; `tasks/`, `conversations/` and `remediation-tasks/`
reads are still denied and recorded; and the run's record carries
`granted_harness_surfaces: ["findings"]`. (SC-011)

## 5. Remediation still works with everything denied

Dispatch a remediation message turn under the default (all-denied) configuration.

**Expected**: it succeeds. The proposal description and attached context reach the agent as
Hub-injected CLI arguments, not guarded reads — so denying `remediation-tasks/` does not affect
it. This is the check that catches a naive implementation which enforces the scope Hub-side.

## 6. The structural rules bite

```bash
dotnet test backend/tests/Grimoire.ArchTests
```

Covers four things:

- No instruction file, doc, or comment reintroduces the retired path concept (SC-001)
- No identifier, metric name, or persisted field uses the retired term (SC-014)
- Accepted decision records documenting the retirement pass unmodified (SC-004)
- `docs/conventions/wiki-content-root.md` and the test fixture have not drifted (SC-005)

**Manual Red/Green ceremony** (Constitution Principle III), to be recorded in `tasks.md`:

```bash
# introduce a deliberate violation
sed -i '' 's/list_files(".")/list_files("pages\/")/' \
  backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md
dotnet test backend/tests/Grimoire.ArchTests   # MUST fail, naming that file
git checkout backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md
dotnet test backend/tests/Grimoire.ArchTests   # MUST pass
```

The permanent probe `[Fact]`s cover the same ground without mutating the repo, by feeding
synthetic scan targets.

## 7. Renamed signals report identical values

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~RenameInvariance"
```

Runs an identical scripted ingest against the paired fixture and asserts the renamed metric
reports the same count under its new name as the old one did, with the same labels. Signals are
obtained through `AddHubTelemetry`'s `configureTracing` hook — the production composition root,
under the real sampler — not a test-only provider. (SC-015)

## 8. Legacy conversation records still parse

Place a conversation record carrying the old `created_pages:` key in the conversations
directory and load its context.

**Expected**: it parses, the unknown key is ignored, and the fail-closed
`conversation_record_unreadable` path does **not** fire. This is the one place the clean break
could surface as a runtime failure.

## 9. Evaluations

```bash
dotnet test backend/tests/Grimoire.AgentEvals
```

Must report `Skipped: 0`. Scenarios and thresholds:

| Scenario | Threshold | Criterion |
|----------|-----------|-----------|
| Populated wiki, "what does the wiki cover?" | ≥ 95% name a real category and article; ≤ 2% assert emptiness | SC-006 |
| Empty content root (scenario 1 above) | ≥ 90% report no articles without referring to `pages/` | SC-007 |
| Wiki with articles + populated harness surfaces, one granted | ≥ 95% cite no harness record as a wiki source | SC-008, SC-012 |
| Ingest over a new source | ≥ 95% place the article in a non-reserved category | SC-009 |

**Before running**: all ingest, query and lint recordings must be re-captured. This feature
changes every fingerprinted input (three system prompts), so the entire corpus goes stale at
once and the zero-skip gate fails by design until it is refreshed.

## 10. Full pipeline

```bash
dotnet build backend/Grimoire.slnx --configuration Release
dotnet format backend/Grimoire.slnx --verify-no-changes
dotnet test backend/tests/Grimoire.ArchTests --configuration Release --no-build
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release --no-build
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release --no-build
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --no-build
```

All four test projects already run in the standard PR pipeline, so the logging, trace and
structural contract tests inherit CI enforcement by placement — no new pipeline step.

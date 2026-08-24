# Phase 1 Data Model: Lint at Scale

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

No new persistence store, table, or file format is introduced. This feature extends two
existing structures — one transient (in-process, per-run), one persisted (the Findings
Report file spec 013 already defines).

## `ConsideredPaths` (new, transient, in-process)

Lives on `GuardedToolExecutor`
(`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`), alongside the
existing `TouchedPaths`/`CreatedPaths`/`DeletedPaths`/`WikiContentWrites` accumulators
(lines 148-183). Not persisted on its own — it exists only for the duration of one run and is
read once, at run completion, to compute `WikiCoverage` (below).

| Field | Type | Description |
|---|---|---|
| `ConsideredPaths` | `IReadOnlySet<string>` (canonical paths) | Every distinct page path named in a successful `read_file` result (any mode: full, ranged, `frontmatter_only`), a `batch` member's read result, or a `search_files` match. Populated on the same success path as the existing write accumulators — no new dispatch branch. |

**Invariant**: a path denied by policy is never added (mirrors how `TouchedPaths` only
records allowed writes). A path that only appears in a `list_files` result is never added —
listing a filename is not "considering" a page (per research.md R2).

## `WikiCoverage` (new, computed once per run, persisted)

A new value carried on the run's terminal event and, from there, onto the persisted
`FindingsReport`. Computed by the harness from `ConsideredPaths` plus a page-count snapshot
taken at run start (a filesystem enumeration, the same traversal `list_files` already
performs) — never self-reported by the agent (FR-004).

| Field | Type | Description |
|---|---|---|
| `PagesTotal` | `int` | Count of markdown pages in the wiki content root when the run started. |
| `PagesConsidered` | `int` | `\|ConsideredPaths\|` at run completion. |
| `Status` | `Complete` \| `Partial` | `Complete` iff `PagesConsidered == PagesTotal`; `Partial` otherwise. |

**Explicitly not `FindingsReport.Partial`**: that existing field
(`backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs`) means "this run
failed/crashed mid-analysis" (`LintRunCoordinator.cs`: `var partial = status ==
LintRunStatus.Failed;`) — a run-outcome axis. `WikiCoverage.Status` is a distinct,
orthogonal axis: a run can complete successfully (`Partial == false`) while its
`WikiCoverage.Status` is `Partial` (it succeeded, but by design or budget did not touch every
page), and — in principle — a crashed run could still report whatever partial coverage it
had accumulated before failing. The two fields are never conflated or derived from one
another.

## `FindingsReport` (existing, spec 013 — gains one field)

Current shape (`FindingsReportFormat.cs`, confirmed by research):

```csharp
public sealed record FindingsReport(
    string RunId, DateTimeOffset TriggeredAt, DateTimeOffset CompletedAt,
    string OutcomeState, string? FailureReason, bool Partial,
    string? InstructionFilePath, string? InstructionFileSha256,
    IReadOnlyList<FindingsDeniedAction> DeniedActions,
    int InboundLinksRefreshed, string Narrative);
```

New shape (additive — existing readers unaffected by the new field's presence, per
constitution's "no half-finished implementations" and existing format-versioning practice
for `grimoire-findings/1`; a format-version bump is an implementation-phase decision, not a
data-model one):

```csharp
public sealed record FindingsReport(
    string RunId, DateTimeOffset TriggeredAt, DateTimeOffset CompletedAt,
    string OutcomeState, string? FailureReason, bool Partial,
    string? InstructionFilePath, string? InstructionFileSha256,
    IReadOnlyList<FindingsDeniedAction> DeniedActions,
    int InboundLinksRefreshed, WikiCoverage WikiCoverage, string Narrative);
```

## Transport path (unchanged pipeline shape, new payload)

```
GuardedToolExecutor.ConsideredPaths (per-run, in-process)
  → LintIntentHandler.ExecuteAsync computes WikiCoverage at run completion
      (backend/src/Grimoire.LintAgent/Program.cs, alongside existing narrative build)
  → RunCompletionMetadata gains a WikiCoverage field
      (backend/src/Grimoire.LintAgent/RunEvents/RunEventEmitter.cs:13-38)
  → RunEventEmitter.BuildTerminalPayload serializes it onto the NDJSON `completed` event
      (RunEventEmitter.cs:139-174)
  → LintRunCoordinator parses the terminal event and threads WikiCoverage into
      PersistFindingsReportAsync
      (backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs:291-296, 427-469)
  → FindingsReportFormat.Build writes it into the bookkeeping block of the persisted
      Findings Report file
      (backend/src/Grimoire.Hub/LintFindings/FindingsReportFormat.cs:51-96)
```

This is the same path `DeniedActions` and `InboundLinksRefreshed` already travel — no new
process boundary, no new port, no new file format family.

## Eval fixture additions (`LintAtScaleFixture` and `lint-seeded-defects`)

Two new planted-defect shapes, additive to the existing generator
(`backend/tests/Grimoire.EvalRunner/.../LintAtScaleFixture.cs` and its seeded-defects
counterpart):

| Fixture addition | Used by | Description |
|---|---|---|
| Contradiction pair | SC-004 | Two pages asserting mutually exclusive facts about the same subject. |
| Duplicate-content pair | SC-004 | Two pages whose bodies substantially restate the same content under different titles. |
| Stale inbound-link-count page | SC-005 | A page whose recorded inbound-link count in frontmatter no longer matches the actual inbound-link graph in the fixture. |

None of these change the fixture's deterministic-generation contract (still generated at
build time, git-ignored, LCG-seeded) — they extend what the generator plants, not how it
generates.

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

**Optional** planted-defect shapes (research.md R5, spec.md SC-004/SC-005 — Constitution
v1.12.0 lower-stakes tiering): at most one per criterion, added only if the optional
recorded-replay checks (tasks.md T025/T026) are kept, additive to the existing generator
(`backend/tests/Grimoire.EvalRunner/.../LintAtScaleFixture.cs` and its seeded-defects
counterpart):

| Fixture addition | Used by | Description |
|---|---|---|
| Contradiction pair **or** duplicate-content pair (pick one, not both) | SC-004 | Either two pages asserting mutually exclusive facts about the same subject, or two pages whose bodies substantially restate the same content under different titles — one shape, whichever is simpler to seed. |
| Stale inbound-link-count page | SC-005 | A page whose recorded inbound-link count in frontmatter no longer matches the actual inbound-link graph in the fixture. |

None of these change the fixture's deterministic-generation contract (still generated at
build time, git-ignored, LCG-seeded) — they extend what the generator plants, not how it
generates.

---

## Write-side: the `write_file` prepend mode (ADR-035)

No new persisted entity. This is a **call-shape** addition to an existing tool, not a data
model change — documented here because it changes what `content` means for one call shape,
and because it must not be confused with the pre-existing, unrelated policy-level
`Grimoire.Domain.Guardrails.WriteMode` enum (research.md R7).

### `write_file` call shape (existing tool, widened schema)

| Field | Type | Before | After |
|---|---|---|---|
| `path` | `string` | Required. Unchanged. | Required. Unchanged. |
| `content` | `string` | Required. Always the full proposed file content. | Required. Full proposed content when `mode` is omitted/`"replace"` (unchanged, default); **the entry alone** when `mode: "prepend"`. |
| `mode` | `string` enum | Did not exist. | New, optional. `"replace"` (default) \| `"prepend"`. Omitting it is byte-identical to today's behavior — no existing caller's call shape changes. |

### Two distinct "mode" concepts — not to be conflated

| | `Grimoire.Domain.Guardrails.WriteMode` (existing) | `write_file`'s new `mode` parameter |
|---|---|---|
| Scope | Policy-rule-scoped: which class of edit a *path* is allowed to receive (`SafetyPolicy`/`WriteRule`) | Call-scoped: how *this one call*'s `content` should be interpreted |
| Set by | The agent's declared policy file, evaluated by `SafetyPolicy.Evaluate` | The agent, per tool call, in its JSON arguments |
| Visible to the model? | No — the agent never sees or chooses this | Yes — an explicit, schema-visible parameter, same shape as ranged `read_file`'s `offset`/`limit` |
| Values | `ReadWrite`, `CreateOnly`, `FrontmatterOnly` | `"replace"`, `"prepend"` |
| Changed by this feature? | **No** — zero new members, zero new branches on `WriteRule.Mode` | **Yes** — this is the entire write-side deliverable |

`log.md`'s `WriteRule.Mode` stays `ReadWrite` (per ADR-031's full-authority grant) — an
agent *may* still send a full-content `mode: "replace"` write to `log.md` (expensive, but
still correct and still subject to ADR-028's prepend-ordering check); `mode: "prepend"` is
a cheaper alternative path to the same structural guarantee, not a policy restriction.

### Prepend-mode write assembly (transient, in-process, per-call)

No new persisted type. At dispatch time (`GuardedToolExecutor.ExecuteWriteFileAsync` →
`SharedFileWriteGuard.EvaluateWriteAsync`), a `mode: "prepend"` call:

1. Acquires the same per-target `CrossProcessFileLock` every write already acquires.
2. Reads `log.md`'s current content fresh from disk, inside the lock — not from any prior
   `OnReadFile` baseline, and performs no compare-and-swap check (research.md R8: there is
   no staleness scenario for a prepend to be stale *against*).
3. Assembles `proposedContent = entry + currentContent` in-memory — never persisted in this
   intermediate form, only the final concatenation is committed.
4. Runs the *same* `ValidateLogEntryFormat` heading/paragraph checks ADR-017/ADR-028 already
   define, retargeted to validate `entry` directly (ADR-035 R3) rather than a `head`
   subtracted from a whole-file `proposedContent`.
5. Commits via the same atomic temp-file + `File.Move` path every write uses
   (`GuardedToolExecutor.WriteFileAtomicallyAsync`), then re-baselines via `OnWriteCommitted`
   exactly as today.

### Transport path

```
Agent's write_file call: {path: "log.md", mode: "prepend", content: "<entry only>"}
  → GuardedToolExecutor.ExecuteWriteFileAsync forwards `mode` to
      SharedFileWriteGuard.EvaluateWriteAsync
  → SharedFileWriteGuard reads current log.md content under the lock (no baseline),
      assembles entry + current, validates the entry directly (ADR-035 R3)
  → GuardedToolExecutor.WriteFileAtomicallyAsync commits atomically (unchanged commit path)
  → GuardedToolExecutor.TouchedPaths / ActivityLogWritten updated exactly as any other
      successful log.md write (WikiLogCoverageObserver unaffected — research.md R10)
```

No new file, no new format, no new process boundary — the same `log.md` file, in the same
`grimoire-log/*` shape (whatever it's called today), reached by a cheaper call.

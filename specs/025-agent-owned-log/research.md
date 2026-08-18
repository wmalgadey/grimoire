# Phase 0 Research: Agent-Owned, Newest-First Wiki Activity Log

**Feature**: 025-agent-owned-log | **Date**: 2026-08-17

This document resolves every unknown surfaced by the Technical Context in `plan.md`. All
findings are grounded in the current codebase; file references are the state on branch
`025-agent-owned-log` at planning time.

---

## R1 — Where the append-only rule is actually enforced, and what inverting it costs

**Decision**: Invert the check in place, inside
`SharedFileWriteGuard.ValidateLogEntryFormat` — swap the prefix comparison for a suffix
comparison, take the *head* instead of the *tail*, and rename the denial reason
`log_entry_not_appended` → `log_entry_not_prepended`. Nothing else in the guard moves.

**Rationale**: The rule lives in exactly one place
([SharedFileWriteGuard.cs:263-291](backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs#L263-L291)):

```csharp
if (!proposedContent.StartsWith(currentContent, StringComparison.Ordinal))
    return "log_entry_not_appended";
var tail = proposedContent[currentContent.Length..];
```

becoming

```csharp
if (!proposedContent.EndsWith(currentContent, StringComparison.Ordinal))
    return "log_entry_not_prepended";
var head = proposedContent[..^currentContent.Length];
```

with the existing heading-then-paragraph scan applied to `head` unchanged. Three properties
fall out for free and were verified against the current code path:

- **Missing/empty log (FR-010)**: `currentContent` is `string.Empty` when the file does not
  exist ([SharedFileWriteGuard.cs:217](backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs#L217));
  `EndsWith("")` is true and `head` is the whole proposed content, so the first write
  creates the file — the same base case `StartsWith("")` provided before.
- **Whitespace/heading-only prepend (Edge Cases)**: the head is scanned for a conforming
  heading and then for a following non-blank line *within the head*; a head of `"\n\n"`
  fails the heading check, and a head of heading-only fails the paragraph check. Symmetric
  to today's tail behaviour, no extra code.
- **Concurrency (FR-011)**: the compare-and-swap check runs strictly before this one
  ([SharedFileWriteGuard.cs:176-188](backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs#L176-L188))
  and is direction-agnostic — it hashes the whole file. Concurrent prepends are detected
  exactly as concurrent appends were.

**Alternatives considered**:
- Drop the structural check and rely on the instruction files — rejected: SC-001/SC-004 are
  stated as 100% guarantees, and Constitution Principle II forbids downgrading a stated
  guarantee to an evaluation sample. ADR-017 rejected this same option for this same file.
- A `prepend_log_entry` structured tool — rejected: the harness would assemble the file, a
  larger Principle V risk than validating an agent-composed write; ADR-017 rejected the
  structured-tool shape for the identical reason.
- Store append-only and reverse on read — rejected: nothing reads `log.md`
  programmatically; the operator opens the file. It would also introduce the parsed
  representation the spec explicitly excludes.

**Blast radius** (all call sites of the renamed reason, verified by grep):
`GuardedToolExecutor.cs:295` (the recoverable-denial set), `DeniedActionRecord.cs:21-23`
(doc comment), `SharedFileWriteGuard.cs:26` (doc comment),
`LogEntryFormatEnforcementTests.cs:104`, `QueryWriteConflictRejectionAdr017MetricsTests.cs:20,51`.

---

## R2 — Every harness component that writes the activity log

**Decision**: Three writers exist; all three are removed. No fourth writer exists.

**Rationale**: An exhaustive search for filesystem writes against the resolved log path
found exactly:

1. **`WikiLogAppender.EnsureLogEntryAsync` / `AppendAsync`**
   ([WikiLogAppender.cs](backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogAppender.cs)) —
   called from `Grimoire.IngestAgent.Program` on the success path
   ([Program.cs:320](backend/src/Grimoire.IngestAgent/Program.cs#L320), `forceAppend: false`)
   and the failure path ([Program.cs:415](backend/src/Grimoire.IngestAgent/Program.cs#L415),
   `forceAppend: true`), and from `Grimoire.QueryAgent.Program` on both paths
   ([Program.cs:200](backend/src/Grimoire.QueryAgent/Program.cs#L200),
   [Program.cs:236](backend/src/Grimoire.QueryAgent/Program.cs#L236)), each gated on
   `CreatedPaths.Count > 0`. This is FR-002's named target.
2. **`RestartReconciler.AppendReconciliationLogAsync`**
   ([RestartReconciler.cs:126](backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs#L126),
   called from line 77) — a Hub-side writer the spec's Problem Statement does not name but
   FR-001 unambiguously covers ("no harness component MAY author, append, or otherwise
   write content into it"). Its own doc comment already records that it writes outside the
   guarded boundary and that ADR-017's check therefore never runs against it. **This is a
   finding of planning, not of the spec**, and it is in scope: leaving it would make SC-002
   ("100% of runs produce zero harness-authored activity-log content") false for every
   crash-reconciled task.
3. Test fixtures (`IngestSubmissionPipelineFixture`, `BoardCompositeResponseTests`) seed an
   *empty* `log.md`. These are test setup, not production harness authorship, and are
   unaffected.

The Hub never creates `log.md` outside the reconciler —
`GrimoirePathResolver.CreateDirectoryIfMissing` creates directories only, never the file
([GrimoirePathResolver.cs:154-168](backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs#L154-L168)) —
so "the first agent write creates the file" holds without further change.

**Alternatives considered**: routing the backstop through the guarded boundary instead of
deleting it — rejected, the content would still be harness-authored; the guardrail would
launder the violation rather than remove it.

---

## R3 — How to enforce "no harness authorship" structurally

**Decision**: Remove `"Grimoire.AgentRuntime.WikiLog"` from `_allowedNamespacePrefixes` in
all three existing `*AgentGuardedWriteBoundaryRuleTests`, and re-run the Red/Green probe.
Cover the Hub half behaviourally instead.

**Rationale**: The enforcement mechanism already exists and is exactly the right shape. Each
of `IngestAgentGuardedWriteBoundaryRuleTests`, `QueryAgentGuardedWriteBoundaryRuleTests`
([QueryAgentGuardedWriteBoundaryRuleTests.cs:38-48](backend/tests/Grimoire.ArchTests/QueryAgentGuardedWriteBoundaryRuleTests.cs#L38-L48)),
and `LintAgentGuardedWriteBoundaryRuleTests` scans agent + `Grimoire.AgentRuntime` IL for
`System.IO.File::Write*`/`Append*`/`StreamWriter::.ctor` call sites and fails any found
outside an allow-list. `Grimoire.AgentRuntime.WikiLog` is on that allow-list *solely* to
permit the backstop's `File.AppendAllTextAsync`. Deleting the backstop and deleting the
exemption is a single, self-reinforcing change: the rule becomes strictly stronger, and any
future reintroduction of a harness log write in an agent assembly fails the build.

This is a **Boundary Rule** under Constitution Principle III — it is a dependency-direction
rule ("who may reach the filesystem") whose truth does not depend on this feature's surface
shape, which is precisely the category for which a permanent IL-level test is
low-maintenance. Per Phase 0 discipline it gets a fresh Red/Green probe after the allow-list
entry is removed, because the tightened rule is a different rule from the one probed in
feature 014.

The Hub cannot use the same shape: `Grimoire.Hub` writes task artifacts, operational state,
conversation records, and findings by design, so an assembly-wide "no filesystem writes"
rule is meaningless there. Its half is a **Feature-Scoped Invariant** verified the way
Principle III requires — a classicist, state-based integration test that runs the real
`RestartReconciler` against a temp content root whose `log.md` has known bytes, and asserts
those bytes are unchanged while the task artifact and status history *are* updated.

**Alternatives considered**:
- A new reflection test asserting "`WikiLogAppender` does not exist" — rejected: it asserts
  a type's absence rather than a dependency direction, and would need deleting the first
  time anyone adds an unrelated type to that namespace.
- An IL rule scoped to "no write whose argument is the log path" — rejected: not decidable
  statically, and the behavioural test answers the real question directly.

---

## R4 — Deriving FR-012a's signal without judging wiki content

**Decision**: Derive it from `GuardedToolExecutor`'s own allowed-write record. Add two
mechanical, journal-derived properties — the run's allowed wiki-content writes (touched
paths minus the canonical activity-log path) and whether the activity log is among the
touched paths — and evaluate them once at run end in a new, write-free
`WikiLogCoverageObserver`.

**Rationale**: FR-012a explicitly requires the determination be made "from the harness's own
record of which guarded writes it allowed, never from judging the meaning of wiki content".
`GuardedToolExecutor` already maintains `TouchedPaths` (every path successfully written this
run) and already canonicalizes the log path in its constructor
([GuardedToolExecutor.cs:101-105](backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs#L101-L105)),
so both properties are set arithmetic over data the harness owns — no file is read, no
content is inspected, no judgment is made about what any write meant. `CreatedPaths` is
deliberately *not* the source: it is create-only writes only
([GuardedToolExecutor.cs:114-122](backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs#L114-L122)),
which would miss an index-only or page-update run, both of which the spec counts as wiki
changes.

The observer replaces `WikiLogAppender` in the same `Grimoire.AgentRuntime.WikiLog`
namespace, keeping the existing cross-agent construction pattern (the caller supplies its
own frozen `ActivitySource`/`Meter`, per ADR-005/ADR-013 — a shared `AgentRuntime`
component cannot own a static telemetry identity). It performs no I/O at all, which is what
lets R3's tightened allow-list hold.

**Alternatives considered**:
- Reading `log.md` and searching for the correlation id, as the backstop did
  ([WikiLogAppender.cs:62-74](backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogAppender.cs#L62-L74)) —
  rejected: it inspects wiki content to decide an operational fact, and it is wrong for the
  case at hand (an entry can mention the id without the run having written it).
- Computing the fact in the Hub from the task artifact after the run — rejected: no live
  signal, and it duplicates a fact the agent process already holds.

---

## R5 — Verifying the three agent-judgment criteria (SC-005/006/007)

**Decision**: Three new eval scenarios on the existing `Grimoire.EvalRunner` recorded-replay
infrastructure (ADR-012), all at threshold 0.90, all deterministically scored over the
sample's resulting workspace — no new judge.

| SC | Scenario id | Agent | Fixture | Deterministic scorer asserts |
| --- | --- | --- | --- | --- |
| SC-005 | `log-newest-first-placement` | Ingest | `empty-topic` + pre-seeded `log.md` | Exactly one new `## [` heading was added; the pre-existing content is an unchanged suffix; the new entry has a non-blank paragraph. |
| SC-006 | `log-changes-only` | Query | `empty-topic` + pre-seeded `log.md` | A routine lookup turn that writes no page leaves `log.md` byte-for-byte unchanged. |
| SC-007 | `log-no-day-grouping` | Ingest | `log-same-day-entry` | Heading count grew by exactly one; the pre-existing dated entry and its section are byte-unchanged (not extended with a bullet or a second paragraph). |

**Rationale**: SC-005's "accurately describing what actually changed" half is *already*
covered by the existing `log-paragraph-specificity` judge scenario
([IngestScenarioDefinitions.cs:187](backend/src/Grimoire.EvalRunner/Scenarios/IngestScenarioDefinitions.cs#L187),
[LogParagraphSpecificityScorer.cs](backend/src/Grimoire.EvalRunner/Scoring/LogParagraphSpecificityScorer.cs)),
which needs no change — its judge prompt asks about the paragraph's accuracy and is
ordering-agnostic. What is new in SC-005 is *placement and cardinality*, which is
mechanically checkable over the resulting file and therefore needs no second judge. The same
holds for SC-006 and SC-007. Keeping these deterministic (while still evaluation-tier, at a
0.90 threshold over sampled runs) matches the existing `catalog-discoverability` precedent
of a deterministic scorer inside the evaluation tier — this is *not* the Principle V
violation of reimplementing judgment, because the judgment being sampled is the agent's, and
the scorer only measures its output.

**The same-day fixture, and its refresh caveat.** `EvalWorkspace` copies a static fixture
directory per sample ([EvalWorkspace.cs:56-77](backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs#L56-L77)),
and replayed model turns are frozen at capture time, so a fixture cannot compute "today". The
`log-same-day-entry` fixture therefore hard-codes the pre-seeded entry's date to the date of
the scenario's capture run — genuinely the same calendar day as the recorded agent output,
and byte-identical at every replay. Cost: re-recording this scenario requires re-seeding the
fixture's date in the same change. This must be written into the fixture's own README so the
next person to re-record does not silently weaken the scenario into the generic case.

**Alternatives considered**:
- A judge for placement/cardinality — rejected: the property is mechanical; an LLM judge
  would add cost and nondeterminism for nothing.
- Deriving the fixture date at capture and persisting a workspace snapshot into the
  recording — rejected: a substantially larger change to the recording format for a property
  the hard-coded date already delivers.
- Folding SC-005's placement check into the existing `log-paragraph-specificity` scenario —
  rejected: that scenario's `StableSerialization` fingerprint gates its recordings; changing
  its scorer invalidates existing recordings for a criterion that is cleanly separable.

---

## R6 — Instruction-file scope

**Decision**: Edit `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` and
`backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md` only. Leave the Lint
instruction file and both `default-user-prompt.md` files unchanged.

**Rationale**: `.grimoire/agents/*/Instructions/` is gitignored and build-populated from
`backend/src/Grimoire.*Agent/Instructions/` (ADR-022); the sources under `backend/src` are
the versioned files. The passages requiring change, located by grep:

- Ingest — `system-prompt.md:44-45` ("append one entry to `log.md` … why it must go at the
  end of the file, not the top"), `:141` (tree comment "the append-only activity log"),
  `:315-347` (the whole **Ingest Log (log.md) Upkeep** section: the append-only paragraph,
  the "so the harness backstop can tell your entry already covers this run" clause, and the
  closing "For a failed run the harness appends its own minimal fallback entry" paragraph).
  New content must state newest-first placement, one complete entry per action with its own
  date heading regardless of existing same-date entries, and the changes-only criterion.
- Query — `system-prompt.md:100` (write-scope item 3), `:191-203` (the append-only shape
  paragraph), `:222` (the `write_conflict_stale_read` recovery bullet, whose "merged into
  the current content" advice must describe prepending). The `Recovering from a write error`
  section should also name `log_entry_not_prepended`, since that reason is in
  `GuardedToolExecutor`'s recoverable set.
- Lint — unchanged, per FR-013's explicit scoping and the 2026-08-17 clarification. Verified:
  Lint reads `log.md` (`system-prompt.md:26`, `:159`, `:168`) and is repeatedly told it is
  read-only (`:198`, `:215`, `:349`); no passage states or depends on an ordering.

No deterministic test may assert any of this wording (Constitution Principle V) — only that
the files load byte-exact and their hashes are recorded, which existing tests already cover.

---

## R7 — Delivery shape

**Decision**: Single pull request; do **not** invoke the `stacked-pr` skill.

**Rationale**: The CLAUDE.md convention defaults to a stack when `tasks.md` has more than
two phase groups beyond Phase 0, but the phases here cannot be reviewed independently. The
prepend inversion (US1) and the backstop removal (US2) are load-bearing for each other in
both directions: the guard's tests assert denial reasons the backstop's removal changes, and
removing the backstop while the guard still enforces append-only leaves the file in a state
no writer can extend correctly. Shipping US1 alone would also leave the tightened arch-test
allow-list red. The whole feature is one inversion, one deletion, one new observability
signal, and two instruction files — small enough that a stack would be ceremony. This
decision is recorded here and restated in `tasks.md`'s Implementation Strategy section, per
the CLAUDE.md rule that the choice is made out loud and that "single PR" must be said
explicitly rather than described as a stack nobody builds.

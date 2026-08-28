# Implementation Plan: Lint at Scale

**Branch**: `028-lint-at-scale` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-lint-at-scale/spec.md`

## Summary

This feature now covers two related but mechanically distinct scaling failures on Lint's
own path, merged from issues #108 and #201 at the user's explicit direction:

1. **Read-side (US1/US2/US4)**: Lint's wiki health check must complete over a wiki that no
   longer fits in one context window (633 pages / ~400k tokens today), and an operator must
   be able to tell a complete pass from a partial one. ADR-030's guarded retrieval tools and
   the frontmatter-first/search-first prompt rewrite (PR #179, spec 026) already deliver the
   reading strategy; this feature adds the harness-computed `WikiCoverage` signal
   (unchanged from the original plan) and validates the scale claim via a cheap
   budget-relation eval, not a large corpus (research.md R1-R5).
2. **Write-side (US3, new)**: `log.md`'s only write primitive costs O(file size) per entry,
   and this has already exceeded the agent's output-token budget in production —
   deterministic write failures today, on Ingest, and on the exact mechanism Lint's own
   instructions also depend on. This feature adds a `write_file` prepend mode, available to
   Ingest, Query, and Lint alike, dropping the cost to O(entry size) (research.md R6-R13).
   A second, independent write-side decision (Clarifications, 2026-08-25/27; research.md
   R15): `log.md`'s format/ordering checks (prepend order, heading pattern, paragraph
   presence) move from a hard deny to a monitored signal, for both write modes — content
   and structural shape are the agent's judgment (Constitution Principle V), not a harness
   denial. This reverses pre-existing, shipped behavior from spec 025/ADR-028, not just
   something newly introduced.

Direction B (harness-side sharding, read-side) remains explicitly not adopted (research.md
R1). This feature introduces **no new ADR**. An ADR was drafted for the write-side fix
(originally "ADR-035", renumbered ADR-051 after a numbering collision with `main`'s own
new ADR-035) and reached Accepted status, but was retracted before merge: the PR author's
review (research.md R13) established that it fails Constitution Principle III's existing
"one genuine system boundary or one technology choice" test — an optional call-shape
parameter added to an already-existing tool, defaulting to current behavior and granting
no capability the tool's existing contract did not already permit, changes neither. The
write-side fix is covered by the guarded tool boundary ADR-006 already established; its
two rules (schema addition, no-baseline dispatch) are Feature-Scoped Invariants in this
plan's Architectural Constraints section, not ADR material.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`) for the backend; no frontend surface.

**Primary Dependencies**: No new external dependency. Reuses `Grimoire.AgentRuntime`
(`GuardedToolExecutor`, `AgentLoop`, `SharedFileWriteGuard`), `Grimoire.Domain.Guardrails`
(`SafetyPolicy`, `WriteMode` — the existing policy-level enum, untouched),
`Grimoire.LintAgent`/`Grimoire.IngestAgent`/`Grimoire.QueryAgent` (their `ToolRegistry`
declarations, all three referencing the same shared `WriteFileDefinition`),
`Grimoire.Hub.LintDispatch`/`Grimoire.Hub.LintFindings`, `Grimoire.EvalRunner`/
`Grimoire.AgentEvals` (ADR-012).

**Storage**: Markdown files on the real filesystem under the content root (unchanged). The
persisted Findings Report file (`grimoire-findings/1`) gains one additive `WikiCoverage`
field; `log.md` itself gains no new format, only a cheaper way to write the same format.

**Testing**: xUnit. `Grimoire.IntegrationTests` (classicist state-based tests against real
`GuardedToolExecutor`/`SharedFileWriteGuard`/`LintRunCoordinator`, real temp-directory
content roots, and — for the read-side hermetic tests — a hand-rolled fake `IModelClient`
port double); `Grimoire.AgentEvals` recorded-replay (ADR-012) for the one read-side eval
addition and any optional lower-stakes checks kept for SC-004/SC-005. No `Grimoire.ArchTests`
addition: the write-side's two rules are Feature-Scoped Invariants (Constitution Principle
III), verified by the same classicist `Grimoire.IntegrationTests` suite, not a Phase 0
structural/reflection test.

**Target Platform**: Linux container (Docker) in production; Windows/macOS for development.

**Performance Goals**: A Lint run over the current production wiki size completes within
the existing `AgentLoop` caps (`DefaultContextTokenCap = 200_000`,
`DefaultSpendTokenCap = 1_000_000`). A single `log.md` prepend write costs output tokens
proportional to the entry's own size (typically a few hundred tokens), independent of
`log.md`'s total size — including at the ~35k-token size already observed failing in
production.

**Constraints**: No new external system, no new port. The read-side adds no new tool (six
Lint tools unchanged). The write-side widens one existing tool's schema
(`write_file` gains an optional `mode` parameter) rather than adding a seventh tool or a
new tool name — a Feature-Scoped Invariant under the already-Accepted ADR-006, not a
change requiring its own ADR (it decides neither a new system boundary nor a new
technology choice, Constitution Principle III). Hermetic
harness tests require no live LLM calls or API keys; only the evaluation tier does, gated
in CI (ADR-012).

**Scale/Scope**: Read-side validated as a budget-to-content-size *relation* (SC-003), not
by literal page count (research.md R3). Write-side validated at the exact production size
already observed failing (~128KB / ~35k tokens) and unboundedly beyond it, since the fix
removes the size dependency entirely rather than raising a ceiling (SC-007).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Verdict |
|---|---|---|
| I — Domain architecture & hexagonal boundaries | No new external system, no new port. `ConsideredPaths` (read-side) is an in-process accumulator on `GuardedToolExecutor`, same shape as its existing write-tracking lists. The write-side prepend path reads/writes the same local filesystem through the same guard — no new adapter, no infrastructure package relocation. | PASS |
| II — Pragmatic testing | Read-side: hermetic fake-`IModelClient` tests for SC-001/002 (no model calls); one recorded-replay eval variant for SC-003; SC-004/005 lower-stakes per Constitution v1.12.0, correction-loop primary. Write-side: SC-007/008 remain deterministic harness guarantees (cost proportionality; concurrency-safety via lock-serialization), tested with classicist state-based integration tests against the real guard, real lock, real temp-directory filesystem — no mocking framework, no double beyond the one sanctioned `IModelClient` port fake already in use for the read side. SC-009 (format/ordering deviation visibility) is newly classified lower-stakes agent-judgment per Constitution v1.12.0, satisfied by the correction loop reading the new structured log event/metric (FR-016) — not a mandatory eval suite, matching SC-004/005's treatment. | PASS |
| III — ADR-driven & test-enforced | All ADRs in `docs/adr/` re-read for this merge, including two independent upstream ADR events that landed on `main` while this feature was in flight: Constitution v2.0.0 (single-aspect ADRs, partial `Amends`/`Amended by` retired for new work) and, separately, a project-wide restructuring pass that retroactively split several old multi-aspect ADRs — including superseding ADR-028 wholesale with a new **ADR-035** (agent-exclusive activity-log authorship) and deprecating ADR-017 entirely (its format-enforcement content reclassified as feature-scoped, owned by a contract document, not an ADR). This feature's own ADR was first renumbered **ADR-035 → ADR-051** to clear the resulting number collision, then **retracted entirely** after the PR author's direct review pushback: Principle III's existing "one genuine system boundary or one technology choice" test (`Single-aspect ADRs; no feature content`) already answers what this feature had been drafting a new ADR for — an optional call-shape parameter on an already-existing tool, defaulting to current behavior, changes neither (research.md R13). `write_file`'s prepend mode instead operates inside the guarded tool boundary ADR-006 already decided; its two rules (schema addition, no-baseline dispatch) are **Feature-Scoped Invariants**, covered by classicist tests in their normal implementation phase, never a Phase 0 structural test. ADR-035 (agent-exclusive authorship) is confirmed unaffected — this feature changes only how cheaply an already-authored entry is committed, never who authors it. | PASS |
| IV — Behavioral & observable | Read-side Observability section unchanged from the pre-merge plan (coverage metrics/log event/span). Write-side now introduces one new business metric and one new structured log event (FR-016/SC-009, Clarifications 2026-08-27): a write that deviates from `log.md`'s expected format/ordering shape is committed as submitted and recorded via `wiki.log.format_deviation_total`/`wiki.log.format_deviation`, extending the existing `WikiLogMetrics`/`WikiLogEvents` components (`Grimoire.AgentRuntime.WikiLog`) rather than adding a new telemetry surface. The pre-existing `guardrails.format_validate` span's `outcome`/`reason` tags are retargeted from "allowed/denied" to "conforming/deviated", since the check no longer gates the write. | PASS |
| V — Agentic core & deterministic harness | Read-side: coverage signal is behavior-agnostic (records *whether* a page was read, not judgment quality). Write-side: FR-014 requires the same boundary — the harness gains a cheaper way to *commit* an agent-authored entry, never authorship of the entry. What to log, what an entry says, and what counts as a Lint finding all remain agent judgment under instruction files (three files updated: Ingest, Query, Lint — FR-015). FR-011/FR-016 extend this further (Clarifications 2026-08-27): *whether an entry's shape conforms* to the format contract is also agent judgment, not a harness-enforced denial — the harness's write-side guardrail job is capability (which tool/mode/path) and concurrency safety (FR-012), never content shape. | PASS |

**Post-design re-check**: unchanged after Phase 1 — data-model.md and contracts/
introduce no new boundary, no new dependency, and no relocation of judgment into backend
code beyond what ADR-006's existing guarded tool boundary already authorizes.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| [ADR-006](../../docs/adr/ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Governs the write-side (US3) directly: every `write_file` dispatch — replace or prepend — already passes through `GuardedToolExecutor`/the deny-by-default policy and its write journal. No new tool is added; unknown-tool rejection is unaffected since `write_file`'s tool *name* is unchanged. The `mode` schema addition and its no-baseline dispatch mechanism operate inside this already-Accepted boundary — neither a new system boundary nor a new technology choice (Constitution Principle III) — so they are this feature's two Feature-Scoped Invariants (below), not a new ADR's Boundary Rules. |
| [ADR-035](../../docs/adr/ADR-035-agent-exclusive-activity-log-authorship.md) | Agent-Exclusive Authorship of the Wiki Activity Log | Decides *who* may author `log.md` content (the agents, exclusively) — confirmed unaffected. This feature changes only how cheaply an already-authored entry is committed, never authorship of it. |
| [ADR-011](../../docs/adr/ADR-011-query-agent-shared-runtime-and-concurrency-model.md)'s successors (ADR-044–ADR-047) | Shared Agent Runtime Library; Token-Level Answer Streaming; Query Dispatch; Query Realtime Delivery | Confirmed unaffected (research.md R10): all three per-agent registries already declare the identical shared `WriteFileDefinition`; widening its schema needs no registry-scope decision, unlike ADR-030 R6's deliberate Lint-only scoping of genuinely new tools. |
| [ADR-030](../../docs/adr/ADR-030-guarded-retrieval-tool-surface.md) | Guarded Retrieval Tools — Search, Ranged Read, and Read-Only Batch | Read-side only (`search_files`, ranged `read_file`, `batch`); confirmed to not constrain `write_file`/`WriteMode` at all (research.md R10). |
| [ADR-031](../../docs/adr/ADR-031-lint-full-wiki-write-scope.md) | Lint Holds Full Authority Over Wiki Content, in Both Modes | Governs Lint's write scope (unaffected) — `log.md` is already in Lint's `ReadWrite` scope, so Lint reaching the new `mode: "prepend"` capability needs no policy change. |
| [ADR-012](../../docs/adr/ADR-012-eval-runner-recorded-replay.md) | Standalone Eval Runner and Recorded-Replay at the Model Port | Governs the one read-side eval scenario variant (SC-003) and any optional SC-004/SC-005 checks kept — recorded-replay, `[Trait("Tier","SlowEval")]`, no new eval mechanism. |
| [ADR-033](../../docs/adr/ADR-033-sloweval-replay-class-set-reduction.md) | SlowEval Replay Class Set Reduced by the Lower-Stakes Eval Removal | This feature adds no new SlowEval replay-eval class — all read-side eval work lands inside the existing `LintReplayEvalTests` class. ADR-033's four-class enumeration is unaffected. |

**New ADR required?**: **No.** An ADR was drafted for the write-side fix (originally
"ADR-035", renumbered ADR-051) and reached Accepted status, but was retracted before merge
following the PR author's direct review pushback (research.md R13): adding an optional
`mode` parameter to the already-existing `write_file` tool — defaulting to current
behavior, granting no capability the tool's contract did not already permit — decides
neither a new system boundary nor a new technology choice, so it fails Constitution
Principle III's existing "Single-aspect ADRs; no feature content" test. It is feature
content, covered by the guarded tool boundary ADR-006 already decided, and verified as
two Feature-Scoped Invariants (below) rather than a Boundary Rule. The read-side portion
of this feature independently still introduces no new structural boundary of its own
(unchanged from the original, pre-merge analysis).

### Feature-Scoped Invariants (write-side, US3)

Per Constitution Principle III, these are the write-side's own rules — not durable
dependency-direction rules, so they get no Phase 0 structural/reflection test. Each is
covered by a classicist, state-based integration test in `Grimoire.IntegrationTests`
exercising the real `SharedFileWriteGuard`/`GuardedToolExecutor` against a real
temp-directory filesystem.

| # | Rule | Verification |
|---|------|---------------|
| FSI-1 | `ToolRegistry.WriteFileDefinition`'s JSON schema gains an optional `mode` string property (`enum: ["replace", "prepend"]`, default `"replace"`), staying `additionalProperties: false`-compatible across all three per-agent registries (`LintToolRegistry`, `IngestToolRegistry`, `QueryToolRegistry`). Omitting `mode` is unchanged, existing behavior. | Integration test: an unlisted schema field is still rejected; a call omitting `mode` behaves byte-identically to today; a call with `mode: "prepend"` is accepted by all three registries. |
| FSI-2 | A `mode: "prepend"` write acquires the existing per-target `CrossProcessFileLock`, reads `log.md`'s current content fresh from disk under the lock (no prior `OnReadFile` baseline, no compare-and-swap check), and commits `entry + currentContent` atomically via the existing temp-file + `File.Move` path. | Integration test: a prepend write succeeds with no preceding read in the same run; two concurrent prepend writers are serialized by the lock and both entries land, in lock-acquisition order, with none lost. |
| FSI-3 | `log.md` writes are never denied for failing the activity-log format contract's structural shape (prepend order, heading pattern, paragraph presence), on either write mode — `EvaluateExistingTargetChecksAsync`'s log-format branch (`SharedFileWriteGuard.cs:230-244`) no longer returns a value that short-circuits the write. A write that deviates commits exactly as submitted and records the deviation via `wiki.log.format_deviation`/`wiki.log.format_deviation_total` (FR-011/FR-016). The compare-and-swap check (`write_conflict_stale_read`, ADR-015) and the `FrontmatterOnly` checks are unaffected — this invariant is scoped to the log-format branch only. | Integration test: a write with a malformed heading, missing paragraph, or wrong prepend order — on `mode: "replace"` and `mode: "prepend"` alike — commits unchanged and is never denied; a separate test asserts the deviation is captured (event + metric) with the correct reason code(s). |

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|---|---|---|
| Deciding which pages need a full read vs. frontmatter-only vs. skip | Agentic core | `agents/lint/system-prompt.md` ("Choosing how to read" — already updated by PR #179) |
| Judging what counts as a finding (contradiction, duplicate, stale link, etc.) | Agentic core | `agents/lint/system-prompt.md`, unchanged by this feature |
| Deciding what to log and what an entry says | Agentic core | `agents/ingest/system-prompt.md`, `agents/query/system-prompt.md`, `agents/lint/system-prompt.md` — unchanged judgment; only the mechanics of committing the entry change (FR-014) |
| Recording which pages a read-shaped tool call actually touched (`ConsideredPaths`) | Harness | `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor` |
| Computing `WikiCoverage` from harness-observed facts | Harness | `Grimoire.LintAgent` (`LintIntentHandler`/`RunEventEmitter`), `Grimoire.Hub.LintDispatch.LintRunCoordinator` |
| Persisting `WikiCoverage` onto the Findings Report | Harness | `Grimoire.Hub.LintFindings.FindingsReportFormat` |
| Concatenating a prepend-mode entry with current `log.md` content and committing atomically | Harness | `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor`, `SharedFileWriteGuard` (Feature-Scoped Invariant FSI-2, above) |
| Observing (never denying) whether a `log.md` write's shape conforms to the format contract | Harness | `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard` (Feature-Scoped Invariant FSI-3, above) — the check's *content* (what shape is expected) is feature-scoped, `contracts/log-prepend-write.md`; that a deviation is only ever recorded, never denied, is FSI-3 |
| Judging what a log entry says and whether its shape matters enough to fix | Agentic core | `agents/ingest/system-prompt.md`, `agents/query/system-prompt.md`, `agents/lint/system-prompt.md` — the correction-loop surface (Observability below) is where an operator, not the harness, decides whether a deviation needs a prompt fix |

No wiki-content judgment moves into backend code by this feature, on either the read or
write side. The harness gains only the ability to observe/report (read side) and to commit
more cheaply (write side) — never to decide.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|---|---|---|---|---|---|
| SC-001 (run completes at production-equivalent scale) | Deterministic guarantee | Integration test: a small ad hoc temp-dir content root plus a hand-rolled fake `IModelClient` scripted past a simulated budget; asserts no `AgentLoopCapException` | Hand-rolled fake `IModelClient` (existing port) | Small ad hoc content root; no eval fixture, no live/recorded LLM calls | Proves cap-enforcement mechanics only — relies on spec 026's `lint-at-scale-survey`/SC-011 (unchanged, still in CI) as the standing evidence the real agent's real reading stays bounded |
| SC-002 (100% of runs carry a coverage report) | Deterministic guarantee | Integration test asserting `FindingsReport.WikiCoverage` at the record level, both complete-pass and forced-partial-pass | Real filesystem, real coordinator, same fake `IModelClient` | Same small ad hoc content root as SC-001 | No new parser (contracts/coverage-signal.md) |
| SC-003 (scale headroom as a relation) | Deterministic measurement of a real agent-driven outcome | One additional `lint-at-scale-survey` scenario variant (tighter `ContextBudgetTokens`, same existing fixture), recorded-replay, comparing reading-volume growth for super-linearity | Recorded model responses (ADR-012) | Existing `LintAtScaleFixture`, one new budget value — no new pages | The one read-side eval addition this feature needs |
| SC-004 (cross-page findings survive narrowing) | Lower-stakes agent-judgment (Constitution v1.12.0) | Correction loop primary (Observability below); one optional recorded-replay case MAY exist | Recorded model responses, only if kept | At most one addition to the existing fixture | Absence of the optional check does not fail the DoD |
| SC-005 (inbound-link accuracy holds) | Lower-stakes agent-judgment (Constitution v1.12.0) | Same treatment as SC-004 | Recorded model responses, only if kept | At most one addition to the existing fixture | "Holds steady," not "improves" (FR-006) |
| SC-006 (token-efficiency gain not regressed) | Deterministic measurement of an agent-driven outcome | Comparison against `specs/026-guarded-tool-surface/baseline.md` | Recorded model responses (ADR-012) | Existing `lint-at-scale-survey` recordings | Observational check |
| SC-007 (prepend write cost proportional to entry, not file, size) | Deterministic guarantee | Integration test: real temp-dir `log.md` seeded to ~128KB (matching the production failure), a `write_file` call with `mode: "prepend"` and a small entry; asserts success and that no full-file content was required in the call | Real filesystem, real `SharedFileWriteGuard`, real lock — no doubles needed (no model call in the write path itself) | A seeded large `log.md` fixture built inline by the test (not a shared eval fixture) | Directly reproduces issue #201's production failure size and proves it now succeeds |
| SC-008 (concurrency safety holds under prepend) | Deterministic guarantee | Integration test: two concurrent writers (any mix of `mode: "replace"`/`mode: "prepend"`) both land, in lock-acquisition order, with no loss | Real filesystem, real lock, two concurrent tasks/threads for the race test | Small seeded `log.md` fixtures | Per research.md R8, the concurrent case proves *absence* of a corruption/loss scenario by construction, not a `write_conflict_stale_read`-style denial — the test asserts both entries present and correctly ordered, not a denial |
| SC-009 (format/ordering deviation is visible, not denied) | Lower-stakes agent-judgment (Constitution v1.12.0) | Integration test: a malformed heading/missing paragraph/wrong-order write, on either mode, commits unchanged and emits `wiki.log.format_deviation` (event) and increments `wiki.log.format_deviation_total` (metric) with the correct reason code | Real filesystem, real guard — no doubles needed | Small seeded `log.md` fixtures, reused from SC-008 | Correction-loop primary (Observability below); this reclassifies what SC-008 asserted as a 100% denial guarantee before Clarifications 2026-08-27 |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `wiki.lint.coverage_ratio` | Histogram | `pages_considered / pages_total` for one completed Lint run (0.0–1.0) | `agent=lint` |
| `wiki.lint.coverage_runs_total` | Counter | Completed Lint runs, by coverage status. **Corrected during implementation (Layer 2) from the originally planned name `wiki.lint.runs_total`**: that name is already an existing Hub-side metric (`HubMetrics.cs`, labeled `outcome`, one increment per terminal run regardless of coverage) — reusing it here for a second, differently-labeled agent-side counter would double-count runs under one metric name. Renamed instead of reused; no behavior this feature depends on changed. | `agent=lint`, `coverage_status=complete\|partial` |
| `wiki.log.format_deviation_total` | Counter | A `log.md` write committed despite deviating from the activity-log format contract's expected shape (FR-011/FR-016, Clarifications 2026-08-27) — extends the existing `Grimoire.AgentRuntime.WikiLog.WikiLogMetrics` component, mirroring its `wiki.log.unlogged_change_total` pattern | `agent=ingest\|query\|lint`, `mode=replace\|prepend`, `reason` (one of the existing denial-reason codes, now repurposed as deviation codes: `log_entry_not_prepended`, `log_entry_malformed_heading`, `log_entry_missing_paragraph`) |

**Write-side (US3): one new metric, above.** Reversing the pre-clarify plan's "no new
metric" position (Clarifications 2026-08-27): FR-016 now requires this. The pre-existing
`guardrails.format_validate` span still fires but its `outcome` tag is retargeted from
"allowed/denied" to "conforming/deviated," since the check no longer gates the write —
this metric is the durable, aggregable counterpart a dashboard can chart, which a span
attribute alone cannot provide.

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `lint.run.coverage_computed` | INFO | Once per completed Lint run, when `WikiCoverage` is computed (Hub side, alongside `PersistFindingsReportAsync`) | `run_id`, `pages_total`, `pages_considered`, `coverage_status` |
| `wiki.log.format_deviation` | WARN | Once per `log.md` write whose content deviates from the activity-log format contract's expected shape, immediately after the (non-denying) format check runs — extends `Grimoire.AgentRuntime.WikiLog.WikiLogEvents`, mirroring its `wiki.log.change_not_logged` idiom (FR-011/FR-016) | `agent`, `mode`, `path`, `reason` (comma-joined if more than one check deviated) |

**Derivation rule (MANDATORY)**: `tasks.md` MUST include, for each of these two rows: (1) an
implementation task emitting the event with a stable event name and these
mandatory fields; (2) a deterministic integration test validating event name, level, and every
mandatory field for this trigger; (3) a CI task ensuring this test runs in the standard PR
pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `lint_agent.run` (existing span — attributes added, no new span) | root | adds `coverage.pages_total`, `coverage.pages_considered`, `coverage.status` alongside its existing attributes |

**Derivation rule (MANDATORY)**: `tasks.md` MUST include: (1) an implementation task adding
these attributes to the existing `lint_agent.run` span at run completion; (2) a deterministic
integration test validating the span carries these attributes (against the real
`LintAgentTracing` composition root, per Principle IV — not a test-only `ActivitySource`); (3)
a CI task ensuring this test runs in the standard PR pipeline.

### Correction-loop observability surface (Constitution v1.12.0, Principle V)

Two lower-stakes agent-judgment criteria rely primarily on the user-reported correction
loop, each with its own named surface per the constitution's "Human in the loop" bullet:

- **SC-004 and SC-005**: the **persisted Findings Report file itself**
  (`grimoire-findings/1`) — an operator reads a run's narrative body directly to judge
  whether cross-page findings and inbound-link counts look right, and, per
  `WikiCoverage`, now also sees whether that judgment was made over the whole wiki or a
  partial pass. If the operator notices a miss, the fix is an edit to
  `agents/lint/system-prompt.md`, verified on the next run.
- **SC-009**: the `wiki.log.format_deviation` structured log event and
  `wiki.log.format_deviation_total` metric (Observability above) — an operator watching
  the OTel dashboard or logs sees which agent, mode, and reason a `log.md` write deviated
  under, without reading `log.md` by hand. If the pattern recurs, the fix is an edit to
  the relevant agent's `system-prompt.md` (Ingest/Query/Lint), verified on the next run.

No new user-facing surface is introduced for either — both route through infrastructure
(Findings Report file, OTel pipeline) this project already has.

## Project Structure

### Documentation (this feature)

```text
specs/028-lint-at-scale/
├── plan.md              # This file
├── research.md           # Phase 0 output (R1-R5 read-side, R6-R13 write-side/ADR history)
├── data-model.md          # Phase 1 output
├── quickstart.md          # Phase 1 output
├── contracts/
│   ├── coverage-signal.md      # Phase 1 output (read-side)
│   └── log-prepend-write.md    # Phase 1 output (write-side)
└── tasks.md               # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.AgentRuntime/
│   │   ├── Guardrails/
│   │   │   ├── GuardedToolExecutor.cs              # + ConsideredPaths accumulator; ExecuteWriteFileAsync forwards `mode`
│   │   │   ├── ToolRegistry.cs                     # WriteFileDefinition schema gains optional `mode` property
│   │   │   └── Coordination/
│   │   │       └── SharedFileWriteGuard.cs         # + prepend-mode dispatch (no baseline/CAS), entry-direct ValidateLogEntryFormat overload; log-format branch of EvaluateExistingTargetChecksAsync no longer denies (FSI-3)
│   │   └── WikiLog/
│   │       ├── WikiLogMetrics.cs                   # + RecordFormatDeviation (wiki.log.format_deviation_total)
│   │       └── WikiLogEvents.cs                    # + LogFormatDeviation (wiki.log.format_deviation)
│   ├── Grimoire.LintAgent/
│   │   ├── Program.cs                        # LintIntentHandler computes WikiCoverage at completion
│   │   ├── Instructions/system-prompt.md     # "Reconciling index.md and log.md" step updated to use mode: "prepend"
│   │   ├── RunEvents/RunEventEmitter.cs      # RunCompletionMetadata + terminal-event payload gain WikiCoverage
│   │   ├── LintAgentMetrics.cs               # + wiki.lint.coverage_ratio, wiki.lint.coverage_runs_total
│   │   ├── LintAgentTracing.cs               # + coverage.* attributes on lint_agent.run
│   │   └── LintAgentInstrumentation.cs       # wires the above
│   ├── Grimoire.IngestAgent/
│   │   └── Instructions/system-prompt.md     # log.md Upkeep section updated to use mode: "prepend"
│   ├── Grimoire.QueryAgent/
│   │   └── Instructions/system-prompt.md     # log.md section updated to use mode: "prepend"
│   └── Grimoire.Hub/
│       ├── LintDispatch/LintRunCoordinator.cs        # threads WikiCoverage into PersistFindingsReportAsync; emits lint.run.coverage_computed
│       └── LintFindings/FindingsReportFormat.cs      # FindingsReport gains WikiCoverage field
└── tests/
    ├── Grimoire.IntegrationTests/
    │   ├── (read-side) ConsideredPaths/WikiCoverage/log-event/span tests, hermetic fake-IModelClient tests for SC-001/002
    │   └── (write-side) SharedFileWriteGuardPrependTests.cs — new, mirroring the existing FrontmatterOnly test file's shape (covers FSI-1/FSI-2/FSI-3, classicist, no Grimoire.ArchTests addition) — asserts both the no-denial behavior and that `WikiLogEvents.LogFormatDeviation`/`WikiLogMetrics.RecordFormatDeviation` fire with the correct reason (SC-009)
    ├── Grimoire.EvalRunner/Scenarios/LintScenarioDefinitions.cs    # lint-at-scale-survey gains a parameterized budget variant
    └── Grimoire.AgentEvals/LintReplayEvalTests.cs                  # extended assertions; optional SC-004/SC-005 scenarios if kept
```

**Structure Decision**: No new project. This feature touches
`Grimoire.AgentRuntime`/`Grimoire.Domain` (guarded execution, both read and write paths),
`Grimoire.LintAgent`/`Grimoire.IngestAgent`/`Grimoire.QueryAgent` (agent process harnesses
and instruction files), and `Grimoire.Hub` (persistence/coordination), plus their existing
test projects. No new `Grimoire.ArchTests` file: FSI-1/FSI-2 are Feature-Scoped Invariants,
covered by the classicist `Grimoire.IntegrationTests` addition above, not a Phase 0
structural test. No frontend change.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — table intentionally omitted.

# Implementation Plan: The Guarded Tool and Policy Surface Lint Needs

**Branch**: `026-guarded-tool-surface-02-plan` | **Date**: 2026-08-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/026-guarded-tool-surface/spec.md`

## Summary

Lint cannot survey a wiki of the size it exists to maintain: `read_file` returns whole files,
there is no search, and 633 pages / ~400k tokens exceeds the 200k context guard. Separately,
its single `frontmatter-only` policy — shared by the survey run and remediation execution —
denies an authorized fix that needs a body edit.

Both are addressed at the same layer. The tool surface grows from three tools to six
(`search_files`, ranged `read_file`, read-only `batch`, `delete_file`), each shaped like the
shell tool it replaces so the agent's existing fluency transfers. The write scope becomes one
scope for both modes, covering the whole content root including `index.md` and `log.md`, with
deletion as a separately granted capability so no other agent inherits it. Git history is the
recovery path for destructive change; the harness adds no confirmation step.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: `System.Text.RegularExpressions` (`RegexOptions.NonBacktracking`) —
already in the BCL, no package added. No new external dependency.

**Storage**: Markdown files on the real filesystem under the content root; SQLite for
operational state (untouched by this feature).

**Testing**: xUnit. `Grimoire.ArchTests` (NetArchTest) for Phase 0 boundary rules;
`Grimoire.IntegrationTests` against the real filesystem in per-test temp directories;
`Grimoire.AgentEvals` recorded-replay for agent-judgment criteria.

**Target Platform**: Linux container (Docker); Windows and macOS for development.

**Project Type**: Web application — .NET backend (`backend/`) plus a frontend (`frontend/`)
untouched by this feature.

**Performance Goals**: A search over ~633 pages / ~4 MB completes well inside its 2 s budget;
a Lint survey run's total content read stays under the 200k context guard.

**Constraints**: No new external system, no new port, no shell or process-execution capability
for the agent. Hermetic harness tests, no live LLM calls or API keys.

**Scale/Scope**: The reference wiki is 633 pages / ~400k tokens; bounds are set against that.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Verdict |
|---|---|---|
| I — Domain architecture & hexagonal boundaries | No new external system. Search, ranged read and delete are filesystem operations, covered by the **persistence/local-filesystem exemption** — introducing a port to mock them would violate Principle II. Adapter containment unchanged; no infrastructure package moves. | PASS |
| II — Pragmatic testing | Integration tests against the real filesystem in temp dirs. Doubles limited to the existing `IModelClient` port fake. No mocking framework. Success criteria split: SC-001..SC-010 deterministic; SC-011 and SC-013 evaluation thresholds. SC-012/SC-014 are cut as gates — see "Eval scope" below. | PASS |
| III — ADR-driven & test-enforced | ADR-030 and ADR-031 drafted and Accepted before `/speckit-tasks`, with bidirectional links on ADR-006/011/016/017/018 and an `index.md` update. Boundary Rules vs Feature-Scoped Invariants classified in each ADR. | PASS |
| IV — Behavioral & observable | Observability section below enumerates metrics, log events and spans; each derives implementation + deterministic test + CI tasks. Contract tests must run against the production composition root, not a test-only provider. | PASS |
| V — Agentic core & deterministic harness | The harness gains capability and keeps deciding permission only. **Removing `frontmatter-only` moves judgment in the correct direction**: a mechanical limit on what kind of edit is permissible was the harness deciding wiki content by proxy. What to search for, what to edit, whether to delete, and whether the index needs reconciling are all agent judgment. | PASS |

**Post-design re-check**: unchanged — Phase 1 introduced no new boundary, no new dependency,
and no relocation of judgment into backend code.

### Watch item, recorded rather than waived

ADR-031 removes a deterministic guarantee (spec 013 SC-002) and replaces it with sampled
agent-judgment coverage (SC-013). This is permitted — Principle II forbids attaching a 100%
deterministic guarantee to an agent-judgment outcome, which is exactly what the old criterion
did — but it is a genuine reduction in absolute assurance, accepted deliberately with git
history as the compensating control. It is called out here so review sees it stated, not
discovered.

## Architectural Constraints & ADRs

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| [ADR-006](../../docs/adr/ADR-006-agent-tool-loop-guarded-boundary.md) | Agent Tool-Use Loop and Guarded Tool Boundary | Every new capability dispatches through `GuardedToolExecutor` against the deny-by-default policy, at invocation time. The write journal must cover deletion. Amended by ADR-030/031. |
| [ADR-010](../../docs/adr/ADR-010-hexagonal-ports-adapter-namespaces.md) | Hexagonal Ports and Adapter Namespaces | No new external-system port: filesystem work falls under the persistence exemption. Adapter containment unchanged. |
| [ADR-011](../../docs/adr/ADR-011-query-agent-shared-runtime-and-concurrency-model.md) | Shared Agent Runtime and Concurrency Model | New tools live in the shared `ToolRegistry` but are declared only by `LintToolRegistry`; R3/R11 unknown-tool rejection keeps them unreachable for Ingest/Query. Amended by ADR-030. |
| [ADR-015](../../docs/adr/ADR-015-query-write-scope-and-wiki-write-coordination.md) | Query Write Scope and Cross-Process Wiki Write Coordination | Reused unchanged. The lock and compare-and-swap mechanism gains no second implementation; FR-010's partial-read rule exists precisely to protect its baseline. |
| [ADR-016](../../docs/adr/ADR-016-lint-write-scope-frontmatter-only-enforcement.md) | Lint Write Scope — Frontmatter-Only | **Superseded by ADR-031.** Its `FrontmatterOnly` mode is retained in the policy model; its decision is not. |
| [ADR-017](../../docs/adr/ADR-017-log-and-catalog-entry-format-enforcement.md) | Format Enforcement for `log.md` and `index.md` | Now binds Lint's writes to those files too. Format rules unchanged. Amended by ADR-031. |
| [ADR-018](../../docs/adr/ADR-018-remediation-action-authorization-and-execution.md) | Human-Authorized Remediation Action Execution | State machine unchanged; authorization no longer confers write authority. Amended by ADR-031. |
| [ADR-021](../../docs/adr/ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md) | Test Tier Taxonomy and Deterministic-Wait Convention | New tests declare a tier and use deterministic waits — no wall-clock sleeps, no timing budgets as assertions (cf. #156). |
| [ADR-028](../../docs/adr/ADR-028-agent-owned-activity-log-prepend-ordering.md) | Agent-Owned Activity Log — Prepend Ordering | Lint's new access to `log.md` is bound by the prepend-only ordering rule. |
| [ADR-030](../../docs/adr/ADR-030-guarded-retrieval-tool-surface.md) | Guarded Retrieval Tools | **New.** Defines search/ranged-read/batch semantics, the non-backtracking regex bound, the read-scope rule for results, and the four default values. |
| [ADR-031](../../docs/adr/ADR-031-lint-full-wiki-write-scope.md) | Lint Full Wiki Write Scope | **New.** One scope for both modes, reserved files included, deletion as a separate deny-by-default capability, deletions journaled. |

**New ADR required?**: Yes — ADR-030 and ADR-031 drafted, reviewed and **Accepted** in this
change set, before `/speckit-tasks` is invoked.

### Boundary Rules requiring Phase 0 structural tests

Per Principle III, only Dependency & Layering Boundary Rules get Phase 0 reflection/IL tests:

1. **ADR-031 R3** — agent-side code performs no wiki *deletion* outside the guarded tool
   layer. Extends the existing Principle V guarded-write architecture test to deletion APIs
   (`File.Delete`, `Directory.Delete`). Red/Green probe required.
2. **ADR-030 R2** — the regex engine used by search is constructed with
   `RegexOptions.NonBacktracking` and a match timeout; no other `Regex` construction path is
   reachable from the search implementation. Red/Green probe required.
3. **ADR-030 R1/R4 + ADR-031 R1** — search, batch and delete reach the filesystem only via
   `GuardedToolExecutor`; no orchestration namespace constructs them directly, and no code
   branches on run mode when deciding write scope. Red/Green probe required.

Everything else these ADRs enumerate is a **Feature-Scoped Invariant** and is covered by
classicist behavioral tests in its own implementation phase, never by a reflection test.

## Agentic Boundary (Constitution Principle V)

| Capability | Side | Where it lives |
|---|---|---|
| Which pages to search for, and with what pattern | Agentic core | `agents/lint/system-prompt.md` |
| Whether a body edit is the right fix; what to write | Agentic core | `agents/lint/system-prompt.md` |
| Whether to delete a page rather than supersede it | Agentic core | `agents/lint/system-prompt.md` |
| Whether the index needs reconciling after a delete | Agentic core | `agents/lint/system-prompt.md` |
| What to leave to the user as a remediation task | Agentic core | `agents/lint/system-prompt.md` |
| Whether a requested path is in read/write/delete scope | Harness | `Grimoire.Domain/Guardrails/SafetyPolicy.cs` |
| Executing search within the read scope, capped and bounded | Harness | `Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` |
| Rejecting a batch containing a write | Harness | `Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs` |
| Journaling a deletion for rollback | Harness | `Grimoire.AgentRuntime/Guardrails/WriteJournal.cs` |
| Not setting the CAS baseline from a partial read | Harness | `GuardedToolExecutor` + `SharedFileWriteGuard` |
| Tool definitions offered per agent | Harness | `Grimoire.AgentRuntime/Guardrails/ToolRegistry.cs`, `Grimoire.LintAgent/LintToolRegistry.cs` |

## Test Strategy

| Success criterion | Category | Primary test type | Doubles / external deps | Fixtures | Notes |
|---|---|---|---|---|---|
| SC-001 search never surfaces an out-of-scope path | Deterministic | Hermetic integration | Real filesystem in temp dir | Wiki fixture with an excluded path containing the pattern | The match exists on disk and must be absent from results |
| SC-002 denials recorded, run continues | Deterministic | Hermetic integration | Real filesystem; `IModelClient` fake scripts the calls | Denied search root, denied batch member, denied write | State-based: assert recorded denials and that the run completed |
| SC-003 caps and time bound observable | Deterministic | Hermetic integration | Real filesystem | Fixture exceeding 200 matches | Assert truncation is signalled, not inferred |
| SC-004 mode never changes the decision | Deterministic | Hermetic integration | Real filesystem | Same path + content through survey and execution coordinators | The anti-regression test for ADR-031 R1 |
| SC-005 out-of-root writes denied | Deterministic | Hermetic integration | Real filesystem | Traversal and absolute-path attempts | Reuses existing canonicalization coverage |
| SC-005a deleted pages restored on failure | Deterministic | Hermetic integration | Real filesystem; fake model forces a late failure | Page set with a deletion mid-run | Asserts file content after rollback |
| SC-006 no task blocked by a frontmatter limit | Deterministic | Hermetic integration | Real filesystem | Authorized body-edit remediation | End-to-end through the real coordinator |
| SC-007 batch with a write rejected wholesale | Deterministic | Hermetic integration | Real filesystem | Batch mixing reads and one write | Assert *no* member executed |
| SC-008 partial read cannot set the write baseline | Deterministic | Hermetic integration | Real filesystem, real `SharedFileWriteGuard` | Ranged read then whole-file write | The ADR-015 protection test |
| SC-009 policy identity recorded | Deterministic | Hermetic integration | Real filesystem | Any run | Assert version + hash in the task artifact |
| SC-010 unparseable policy fails before any write | Deterministic | Hermetic integration | Real filesystem | Malformed policy file | Assert wiki unchanged |
| SC-005b Lint writes to index/log held to format rules | Deterministic | Hermetic integration | Real filesystem | Malformed catalog entry from Lint | Reuses ADR-017 enforcement |
| SC-011 survey stays under the context guard | Agent judgment | Recorded-replay evaluation, threshold ≥ 90% | Recorded LLM responses (ADR-012) | `lint-at-scale` — the existing `lint-seeded-defects` wiki plus generated filler pages | Scorer sums content tokens read per run |
| SC-013 authorized body edits address the proposal | Agent judgment | Recorded-replay evaluation, threshold ≥ 90% | Recorded responses | Reuses `lint-seeded-defects` | Adjudicated scorer; **this is what replaces ADR-016's structural guarantee** |
| SC-012 searches are scoped, not unbounded sweeps | *Not evaluated* | — | — | — | **Cut.** Measures means, not ends: SC-011 already tests the outcome. A criterion that pins *how* the agent reaches it ossifies agent behaviour for no added assurance |
| SC-014 median content tokens read drops ≥ 50% | *Not evaluated* | One-off measurement, recorded in the PR | — | Same fixture before/after | **Cut as a gate.** `wiki.read.invocations_total{shape}` answers this directly; a recurring eval adds recording cost without adding information the metric lacks |

### Eval scope — two scenarios, no bespoke corpus

This feature adds **two** eval scenarios, not four, and **no hand-authored fixture**. The
reasoning is recorded here because the first draft of this plan got it wrong in a way worth
naming: it proposed four criteria and a "≥600-page sampled wiki", which would have been roughly
70× the largest fixture in the repo (existing fixtures hold 1–9 markdown files) and taken Lint
from 5 eval scenarios to 9.

- **Fixture**: `lint-at-scale` extends the existing `lint-seeded-defects` wiki with filler pages
  **generated at fixture-build time**, and the eval configuration lowers the context budget so
  the fixture exceeds it. The property under test is "narrows instead of reading everything",
  which reproduces wherever wiki > budget — the absolute page count is irrelevant to it. No
  corpus is authored or committed.
- **SC-012 and SC-014 are cut** for the reasons in the table above. SC-014 survives as a
  one-off before/after number recorded in the implementation PR, not as a CI gate.
- **SC-013 is not negotiable down**: it is the only remaining assurance behind ADR-016's
  superseded structural guarantee. If eval scope needs to shrink further, SC-011 goes first.

A standing rule capping eval-suite growth is out of scope here and belongs to #136 — Principle
II currently *mandates* percentage thresholds for agent-judgment criteria, so changing that
shape is a constitution amendment, not a per-feature choice.

**Composition-root rule (Principle IV).** Every observability contract test obtains its
signals from the production telemetry registration, sampler and exporter pipeline — never a
test-only `ActivitySource` or always-on sampler. Feature 003 shipped green trace tests while
the Hub exported nothing; that failure mode is what this rule exists to prevent.

## Observability

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|---|---|---|---|
| `wiki.search.invocations_total` | Counter | One per `search_files` call | `agent`, `outcome=completed\|truncated\|timed_out\|denied\|pattern_rejected` |
| `wiki.search.matches_returned` | Histogram | Matches returned per search | `agent` |
| `wiki.search.files_scanned` | Histogram | Files opened per search | `agent` |
| `wiki.read.invocations_total` | Counter | Reads by shape, to show ranged reads displacing whole-page reads | `agent`, `shape=full\|range\|frontmatter` |
| `wiki.batch.invocations_total` | Counter | One per `batch` call | `agent`, `outcome=completed\|rejected_write\|rejected_size` |
| `wiki.page.deletions_total` | Counter | Pages deleted through the guarded boundary | `agent`, `outcome=applied\|rolled_back` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|---|---|---|---|
| `wiki.search.truncated` | WARN | Result cap reached | `task_id`, `run_id`, `agent`, `pattern_length`, `cap`, `turn` |
| `wiki.search.timed_out` | WARN | Time budget exhausted mid-scan | `task_id`, `run_id`, `agent`, `budget_ms`, `files_scanned`, `turn` |
| `wiki.search.pattern_rejected` | WARN | Pattern unsupported or over the size bound | `task_id`, `run_id`, `agent`, `reason`, `pattern_length`, `turn` |
| `wiki.batch.rejected` | WARN | Batch contained a write/delete/nested batch, or exceeded max size | `task_id`, `run_id`, `agent`, `reason`, `call_count`, `turn` |
| `wiki.page.deleted` | INFO | A deletion is applied through the guarded boundary | `task_id`, `run_id`, `agent`, `path`, `turn` |
| `wiki.page.delete_rolled_back` | WARN | A journaled deletion is restored during rollback | `task_id`, `run_id`, `agent`, `path`, `turn` |

Existing `lint.tool.denied` continues to carry every policy-scope denial; no new event
duplicates it.

**Derivation rule**: each row above maps in `tasks.md` to (1) an implementation task fixing the
event name and mandatory fields, (2) a deterministic integration test validating name, level
and every mandatory field, and (3) confirmation that those tests run in the standard PR
pipeline (`Deterministic Backend Gates`).

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|---|---|---|
| `guardrails.search_scan` | `lint_agent.tool_call` | `task_id`, `pattern_length`, `path_prefix`, `files_scanned`, `matches`, `truncated`, `outcome` |
| `guardrails.batch` | `lint_agent.model_turn` | `task_id`, `call_count`, `denied_count`, `outcome` |
| `guardrails.delete_file` | `lint_agent.tool_call` | `task_id`, `path`, `journaled`, `outcome` |

Existing `lint_agent.tool_call` remains the per-call span for every tool including the new
ones; the spans above are the child scopes for work that is substantial enough to time
separately. `task_id` is the correlation attribute shared with logs and metrics.

**Derivation rule**: each row maps in `tasks.md` to (1) an implementation task creating the
span with the declared parent and attributes, (2) a deterministic integration test validating
span name, parent/child linkage and correlation attributes, and (3) confirmation those tests
run in the standard PR pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/026-guarded-tool-surface/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── guarded-tool-surface.md
│   └── lint-policy.md
├── checklists/
│   └── requirements.md
└── spec.md
```

### Source Code (repository root)

```text
backend/src/
├── Grimoire.Domain/
│   └── Guardrails/
│       └── SafetyPolicy.cs            # + delete scope, deny-by-default
├── Grimoire.AgentRuntime/
│   └── Guardrails/
│       ├── ToolRegistry.cs            # + search_files, batch, delete_file, read range params
│       ├── GuardedToolExecutor.cs     # + search / ranged read / batch / delete dispatch
│       ├── WriteJournal.cs            # + journaled deletion
│       ├── IToolCallInstrumentation.cs# + search/batch/delete signals
│       └── Coordination/
│           └── SharedFileWriteGuard.cs# unchanged mechanism; partial reads must not reach it
└── Grimoire.LintAgent/
    ├── LintToolRegistry.cs            # declares the new tools
    └── Instructions/
        ├── policy.json                # one scope: read-write + delete on the content root
        └── system-prompt.md           # agent-side judgment for the new capabilities

backend/tests/
├── Grimoire.ArchTests/                # Phase 0 boundary rules (3, each Red/Green probed)
├── Grimoire.IntegrationTests/         # SC-001..SC-010, SC-005a/b
└── Grimoire.AgentEvals/               # SC-011, SC-013 (two scenarios; generated fixture)
```

**Structure Decision**: the existing backend layout is unchanged. No new project, assembly or
namespace is introduced — the feature extends three existing guardrail files, one domain
policy type, and the Lint agent's two instruction artifacts. `frontend/` is untouched.

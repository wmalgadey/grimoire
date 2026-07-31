# Implementation Plan: Wiki Storage Layout & Shared Log/Catalog Format

**Branch**: `014-wiki-storage-restructure` | **Date**: 2026-07-30 | **Spec**: `specs/014-wiki-storage-restructure/spec.md`

**Input**: Feature specification from `/specs/014-wiki-storage-restructure/spec.md`

## Summary

Remove the `pages/` wrapper folder so articles live directly under
`<content-root>/<category>/<article>.md`, alongside `index.md` and `log.md` (User
Story 1); relocate the tasks and conversations directories to be true siblings of the
content root, anchored at the same base directory it is (User Story 2); and unify
every agent's `log.md` append into one `[DATE] TYPE | SUMMARY` heading-plus-paragraph
shape and `index.md`'s catalog line into one link-description-status shape (User
Stories 3–4). The layout change is a mechanical repointing of the single ADR-009
composition point (`GrimoirePathResolver`) and its ~30 call sites — no new anchor
concept, no migration (the wiki starts empty). The format change generalizes the
existing Ingest-only `IngestLogAppender` backstop into a shared
`Grimoire.AgentRuntime` component (`WikiLogAppender`) used by all three agent
processes, and — because the spec states heading/entry *shape* conformance as 100%
guarantees (SC-003/SC-004/SC-006), not agent-judgment thresholds — adds a new
structural check at the guarded write boundary that denies malformed `log.md`/
`index.md` writes, drafted here as
`docs/adr/ADR-017-log-and-catalog-entry-format-enforcement.md` (**proposed**, extends
ADR-006/ADR-015/ADR-016, supersedes nothing). Full rationale: `research.md`.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / Svelte 5 (frontend) —
unchanged existing stack (ADR-001).

**Primary Dependencies**: Existing `Grimoire.AgentRuntime`/ASP.NET Core stack. No new
packages, no new external system, no new port. `WikiLogAppender` and the ADR-017
format checks are pure string/regex operations over content already resident in
memory.

**Storage**: No new runtime location. Existing locations are reconfigured: `TasksDir`
becomes an independently-configurable `GrimoirePathOptions` field (new — today
hardcoded inside `ContentRoot`) anchored at `baseDir`; `ConversationsDir`'s anchor
moves from `dataDir` to `baseDir`; `PagesDir` is removed (its value collapses into
`ContentRoot`). `DataDir` and everything under it (raw intake, operational state,
secrets, agent instructions/policy, write-locks, findings) is unchanged (FR-005).

**Testing**: xUnit `Grimoire.IntegrationTests` (path resolution, guardrail policy
matching, log/catalog format enforcement, backstop behavior) with a real filesystem
for deterministic guarantees; `Grimoire.Domain.UnitTests` for `PolicyLoader`'s new `.`
prefix normalization and `SharedFileWriteGuard`'s new format checks in isolation;
`Grimoire.ArchTests` (existing Mono.Cecil IL scan idiom) re-run unchanged to confirm
the guarded-tool boundary itself is untouched by the policy-value changes;
`Grimoire.EvalRunner` (ADR-012 recorded replay) for the agent-judgment thresholds
(SC-005, SC-007): existing ingest/query scenarios re-recorded against the new layout,
plus new scoring assertions for paragraph/description specificity.

**Target Platform**: Same as existing Hub — cross-platform .NET processes, local dev
and CI; SvelteKit frontend (no frontend change — no UI surfaces a raw filesystem path).

**Project Type**: Web application (existing `backend/` + `frontend/` split). No new
frontend route; this feature is backend-path-configuration and agent-instruction
content only.

**Performance Goals**: None new — same read/write volume as today, just different
paths and one additional cheap regex check per `log.md`/`index.md` write.

**Constraints**: FR-006 — no migration mechanism may be written (the content root
starts empty; there is no prior-layout content to preserve). FR-011 — `log.md` must be
structurally append-only, not just conventionally so (ADR-017). FR-013 — the same
catalog shape check applies regardless of which agent type writes it (Lint excepted —
it never writes `index.md`).

**Scale/Scope**: One-time layout/format change touching the path composition point,
three `data/agents/*/policy.json` files, three `system-prompt.md` files, the
Ingest-only backstop (generalized to Ingest and Query; Lint excluded — no
log-write scope), and roughly 30 call sites across `Grimoire.Hub`,
`Grimoire.IngestAgent`, `Grimoire.QueryAgent`, `Grimoire.LintAgent`,
`Grimoire.EvalRunner`, and their test projects that currently reference
`PagesDir`/the old `ConversationsDir` anchor.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Domain Architecture, Strategic DDD & Hexagonal Boundaries)**: PASS.
  No new external system, no new port. `WikiLogAppender` moves from
  `Grimoire.IngestAgent.IngestLog` into `Grimoire.AgentRuntime` (a namespace already
  shared by all three agent processes — Composition/Core/Guardrails/Host/
  Instructions/RunEvents/Telemetry), which is a relocation within existing
  containment, not a new boundary. The format-validation check (ADR-017) lives inside
  `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard`, already
  confined and containment-tested — no new namespace required.
- **Principle II (Pragmatic Testing Strategy)**: PASS. Success-criteria split is
  clean: SC-001–SC-004 and SC-006 (layout placement, log append-only/heading shape,
  catalog new-entry shape) are deterministic harness guarantees at 100%, hermetically
  tested; SC-005 and SC-007 (paragraph/description specificity) are agent-judgment
  thresholds verified by `Grimoire.EvalRunner`. This plan explicitly corrects an
  initial research-phase misreading (see `research.md` R6) where catalog-shape
  conformance was first assumed to need no enforcement — SC-006's literal "100%"
  wording required revisiting that, per this exact principle.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: PASS. All ADRs in
  `docs/adr/` (001–016) were read; see the table below. This plan introduces one new
  structural boundary (guarded-write-boundary format/shape validation, a check
  dimension no existing `WriteMode` expresses), so a new ADR was drafted:
  `ADR-017-log-and-catalog-entry-format-enforcement.md`, extending (not superseding)
  ADR-006/ADR-015/ADR-016. It was moved to **Accepted** by the ADR Review step within
  this same planning session (Governance: "Before tasks: Drafted ADRs must reach
  Accepted status"). The first task in `tasks.md` must be the structural boundary
  test(s) for the path-composition change (Principle I/ADR-009), followed by
  ADR-017's own Red/Green-probed format-validation tests.
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new infrastructure.
  Full `## Observability` section below, generalizing the existing Ingest-only
  backstop signal into a shared one and adding no new external dependency.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS. Every judgment about
  *what* a log paragraph or catalog description says stays in
  `data/agents/{ingest,query,lint}/system-prompt.md` — no backend rule generates or
  scores that content beyond the separate evaluation harness. Backend gains only
  harness capability: path composition, the format-*shape* check (never
  content-*quality*), and the backstop's mechanical fallback text. See
  `## Agentic Boundary` below.

Re-check after Phase 1 design: no change — no unjustified violations, Complexity
Tracking not needed. ADR-017 is Accepted; no open gate remains.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*
(All of ADR-001 through ADR-016 were read in full.)

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | Stays on the existing .NET/SvelteKit stack; no new language, runtime, or package. |
| ADR-002 | Ingest agent execution model | The internal `--pages-dir`→`--content-root` CLI rename and `WikiLogAppender`'s relocation stay within the existing spawned-child-process/CLI-args contract each agent process already uses. |
| ADR-003 | Domain vs. operational state persistence | Unaffected — `ConversationsDir`'s anchor moves, but it remains outside `wiki/` and git, alongside every other operational-bookkeeping location; `TasksDir` becomes a sibling of the content root, matching the same split (task artifacts are operational, not wiki content). |
| ADR-004 | Credential scoping | Untouched. |
| ADR-005 | Observability backend | The generalized `wiki.log.backstop_appended` signal and its new `type` label export via the existing OTel SDK/Aspire setup unchanged. |
| ADR-006 | Agent tool-use loop & guarded tool boundary | The single guarded chokepoint (`GuardedToolExecutor`) is unchanged in shape; only `policy.json` `pathPrefix` values change (R3/R4), and ADR-017 adds one more check composed at the same boundary. |
| ADR-007 | Agent instruction surface | `data/agents/{ingest,query,lint}/system-prompt.md` are the sanctioned location for the log-heading and catalog-entry *convention* text (R5/R6); no new instruction file, no new document type. |
| ADR-008 | Agent event channel, run supervision, run queue | Unaffected — dispatch/liveness mechanics for all three agent types are unchanged by a path/format feature. |
| ADR-009 | Runtime path configuration | The core constraint this feature operates inside: all path changes go through `GrimoirePathResolver`'s single composition point — no ambient discovery, no path constructed anywhere else. `TasksDir` joins the set of independently-configurable, auto-created, source-tracked locations this ADR already establishes for `ConversationsDir`/`WriteLocksDir`/`FindingsDir`. |
| ADR-010 | Hexagonal ports & adapter namespaces | Persistence exemption applies (local filesystem, no port); `WikiLogAppender`'s move into `Grimoire.AgentRuntime` and the format check's placement inside `SharedFileWriteGuard` both stay within existing adapter/guardrails containment — no new containment rule needed. |
| ADR-011 | Query agent shared runtime & concurrency model | Unaffected — no change to Query's dispatch/concurrency shape. |
| ADR-012 | Eval runner & recorded replay | Existing ingest/query scenarios are re-recorded against the new default paths; new scoring assertions for log-paragraph/catalog-description specificity (SC-005/SC-007) are added under the existing recorded-replay mechanism. |
| ADR-013 | Unified agent platform packaging | Unaffected — no new agent, no change to the `AgentProfile` shape. |
| ADR-014 | Query conversation records | `ConversationsDir`'s relocation moves *where* Conversation Records live, not their format — `ConversationRecordFormat`/sentinel-safety discipline is unchanged. |
| ADR-015 | Query write scope & wiki write coordination | The cross-process lock, CAS check, and `read-write`/`create-only`/`frontmatter-only` modes are reused entirely unchanged — only the `pathPrefix` values they're evaluated against change (R3/R4); ADR-017's format check composes on top, never replacing this layer. |
| ADR-016 | Lint write scope & frontmatter-only enforcement | Reused unchanged; Lint's `pathPrefix` moves from `pages/` to `.` (R3), still `frontmatter-only`, still never touching `index.md`/`log.md` (no write rule for either). |
| ADR-017 | Log and catalog entry format enforcement (**new, this plan, accepted**) | Fixes the guarded-write-boundary shape check for `log.md`/`index.md`; extends ADR-006/ADR-015/ADR-016, supersedes nothing. |

**New ADR required?**: Yes —
`docs/adr/ADR-017-log-and-catalog-entry-format-enforcement.md`, drafted as part of
this plan and moved to **Accepted** by the ADR Review step within this session.

### Considered path-anchoring precedent

`ConversationsDir` already had an independent `GrimoirePathOptions` field before this
feature (added in `011-query-conversations`); only its *anchor* argument in
`GrimoirePathResolver.Resolve` changes, from `dataDir` to `baseDir` — a one-line
change plus a doc-comment update, not a new option. `TasksDir` has no such field today
(it is a hardcoded `Path.Combine(contentRoot, "tasks")` with no source-tracking entry)
— this plan brings it up to the same pattern every other independently-relocatable
location already follows (`ConversationsDir`/`WriteLocksDir`/`FindingsDir`: optional
override field, `Default*DirName` const, `BuildLocation` source-tracking entry,
`CreateDirectoryIfMissing` auto-create call), rather than inventing a special case for
tasks alone.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|-----------------|
| What a log entry's `SUMMARY` and paragraph actually say; what an article's category folder is named | Agentic core | `data/agents/{ingest,query,lint}/system-prompt.md` |
| What a catalog entry's description and source-status marker actually say, in the configured content language | Agentic core | `data/agents/{ingest,query}/system-prompt.md` |
| Whether a `log.md`/`index.md` write's *shape* (heading pattern, append-only, catalog-line pattern) is well-formed | Harness | `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard` (ADR-017) |
| Path composition for the content root, tasks, conversations, and every other runtime location | Harness | `Grimoire.Hub.Runtime.Paths.GrimoirePathResolver` (ADR-009) |
| The backstop's own factual (harness-generated, non-narrative) log text when an agent omits its entry | Harness | `Grimoire.AgentRuntime` `WikiLogAppender` (generalized from `Grimoire.IngestAgent.IngestLog.IngestLogAppender`) |
| Guardrail read/write scope per agent type (`pathPrefix`/`mode` values) | Harness (policy data) — *which* prefixes/modes are harness config, but *why* an agent needs a given scope traces back to its instruction file's role | `data/agents/{ingest,query,lint}/policy.json` |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (100% of articles stored directly under a topical subfolder, zero wrapper segments) | Deterministic guarantee | Hermetic integration test | Real filesystem, temp base dir | Fresh content-root fixture, scripted article-creation write | Extends existing `PathConfiguration/*Tests.cs` idiom with the flattened layout |
| SC-002 (100% of tasks/conversations under a content-root sibling, zero nested instances) | Deterministic guarantee | Hermetic integration test | Real filesystem, temp base dir | Scripted task + conversation creation | New assertions in `PathConfiguration/DefaultLayoutTests.cs`-style fixture |
| SC-003 (100% of `log.md` entries — any agent or the backstop — start with a correctly formatted heading) | Deterministic guarantee | Hermetic integration test with TDD Red/Green probe (tests written and confirmed RED before ADR-017's `SharedFileWriteGuard` check lands) in `Grimoire.IntegrationTests` | `FakeAgentProcess`, temp wiki root | Well-formed append fixture (allow), non-append write (deny), malformed heading (deny), heading-with-no-paragraph (deny) | New `LogEntryFormatEnforcementTests`, mirrors `PolicyLoaderFrontmatterOnlyModeTests`' idiom |
| SC-004 (100% of `log.md` entries locatable by heading-pattern search) | Deterministic guarantee | Hermetic integration test | Real filesystem | Multi-entry `log.md` fixture (agent-written + backstop-written mixed) | Regex-search assertion over a fixture built from SC-003's same allowed writes — `LogEntryFormatEnforcementTests` (T063) |
| SC-005 (≥90% of sampled agent-written log paragraphs specifically/accurately describe the change) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥90% | Recorded/replayed `IModelClient` (ADR-012) | Existing ingest/query scenarios re-recorded against the new format instruction | New scorer in `Grimoire.EvalRunner/Scoring` checking paragraph specificity against the task's actual diff, not a generic restatement |
| SC-006 (100% of newly added `index.md` catalog entries follow the link-description-status shape) | Deterministic guarantee | Hermetic integration test with TDD Red/Green probe (tests written and confirmed RED before ADR-017's catalog check lands) in `Grimoire.IntegrationTests` | Real filesystem | Well-formed new entry (allow), malformed new entry (deny), edit to unrelated existing line (allow, untouched) | New `CatalogEntryFormatEnforcementTests` |
| SC-007 (≥90% of sampled catalog descriptions specifically/accurately describe the article) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥90% | Recorded/replayed `IModelClient` | Reuses SC-005's re-recorded scenarios | New scorer checking description specificity against the article's actual content |

Supporting deterministic tests (not SC-numbered but contract-bearing, feeding
`tasks.md`): `GrimoirePathResolver`/`GrimoirePathOptions` unit and integration tests
for the `TasksDir`/`ConversationsDir` anchor changes and `PagesDir` removal;
`PolicyLoader` unit tests for the new `.` prefix normalization and the
exact-match-before-catch-all ordering guarantee (index.md/log.md never fall through
to the catch-all's mode); `WikiLogAppender` unit tests for the generalized
type-parameterized backstop, covering all three agent types.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `wiki.log.backstop_appended_total` | Counter | Backstop log entries appended (generalizes the existing Ingest-only signal) | `type=ingest\|query` (`lint` reserved — emitted only if Lint gains log-write scope) |
| `wiki.write_conflict.rejections_total` | Counter | *Existing* (ADR-015) — label enumeration extended with ADR-017's four new denial reasons; no new metric | `reason=...\|log_entry_not_appended\|log_entry_malformed_heading\|log_entry_missing_paragraph\|catalog_entry_malformed` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `wiki.log.backstop_appended` | WARN | The backstop appends a `log.md` entry because the agent omitted its own (or the run failed) | `type`, `task_id_or_run_id`, `outcome` |
| `wiki.write_conflict.rejected` | INFO | *Existing* (ADR-015) — reused unchanged for ADR-017's four new denial reasons | `path`, `reason`, `agent_type` |

**Note**: `ingest.log.backstop_appended` (`IngestAgentLogEvents.cs:14`) is retired,
replaced by the shared `wiki.log.backstop_appended` event emitted from
`Grimoire.AgentRuntime`'s `WikiLogAppender`, now carrying a `type` field distinguishing
which agent's run triggered it — per the constitution's preference for one signal per
concept rather than per-agent duplicates (the same rationale `013-lint-agent`'s plan
applied to write-conflict rejections).

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md` work —
implementation with stable event name and mandatory fields, deterministic integration
tests validating event name/level/mandatory fields, and CI enforcement in the standard
PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `wiki_log.backstop_append` | `{ingest,query}_agent.run` (existing per-process root span; `lint` reserved — emitted only if Lint gains log-write scope) | `type`, `task_id_or_run_id`, `outcome` |
| `guardrails.format_validate` | `{ingest,query,lint}_agent.tool_call` (existing) | `path`, `target=log\|index`, `outcome=allowed\|denied`, `reason` |

**Derivation rule (MANDATORY)**: maps to concrete `tasks.md` work — implementation
(span creation with declared parent/child + attributes), deterministic integration
tests (name/linkage/correlation, in-memory exporter per ADR-005), and CI enforcement.
`ingest_agent.backstop_log`/`ingest_agent.append_log` (`IngestLogAppender.cs:51,73`)
are retired, replaced by the shared `wiki_log.backstop_append` span emitted from all
three agent processes. No new lock-acquisition span — `guardrails.acquire_write_lock`
(feature 012) is unchanged and still a sibling of the new
`guardrails.format_validate` span under the same tool-call parent.

## Project Structure

### Documentation (this feature)

```text
specs/014-wiki-storage-restructure/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── log-and-catalog-entry-format.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/Grimoire.Hub/Runtime/Paths/
├── GrimoirePathOptions.cs                # CHANGED: + TasksDir/DefaultTasksDirName; ConversationsDir doc comment updated
├── GrimoirePathResolver.cs               # CHANGED: pagesDir removed; tasksDir/conversationsDir anchored at baseDir; new BuildLocation/CreateDirectoryIfMissing entries for tasksDir
└── ResolvedGrimoirePaths.cs              # CHANGED: PagesDir removed; TasksDir promoted alongside other resolved locations

backend/src/Grimoire.Hub/
├── ContentRoot/ContentRootPaths.cs       # CHANGED: PagesDir reference removed
├── IngestDispatch/IngestRunCoordinator.cs        # CHANGED: PagesDir -> ContentRoot
├── IngestSubmission/SubmissionService.cs         # CHANGED: PagesDir -> ContentRoot
├── QueryDispatch/{QueryRunCoordinator.cs,QueryAgentRequest.cs}  # CHANGED: PagesDir -> ContentRoot
├── IngestDispatch/IngestAgentRequest.cs          # CHANGED: PagesDir -> ContentRoot
└── AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs      # CHANGED: --pages-dir -> --content-root

backend/src/Grimoire.Domain/Guardrails/
└── (no shape change — WriteMode enum unchanged; ADR-017's check is new logic, not a new mode)

backend/src/Grimoire.AgentRuntime/
├── Instructions/PolicyLoader.cs          # CHANGED: "." prefix normalization case
├── Guardrails/Coordination/SharedFileWriteGuard.cs  # CHANGED: ADR-017 format-validation step for log.md/index.md targets
├── Guardrails/DeniedActionRecord.cs      # CHANGED: 4 new reason strings (doc comment only)
└── WikiLog/                              # NEW namespace (moved + generalized from Grimoire.IngestAgent.IngestLog)
    ├── WikiLogAppender.cs                # generalized from IngestLogAppender.cs, parameterized by agent type
    ├── WikiLogEvents.cs                  # wiki.log.backstop_appended (generalized from IngestAgentLogEvents' backstop event)
    └── WikiLogMetrics.cs                 # wiki.log.backstop_appended_total

backend/src/Grimoire.IngestAgent/
├── IngestLog/                            # REMOVED (moved to Grimoire.AgentRuntime.WikiLog)
├── IngestCliOptions.cs                   # CHANGED: --pages-dir -> --content-root
└── Program.cs                            # CHANGED: uses Grimoire.AgentRuntime.WikiLog.WikiLogAppender

backend/src/Grimoire.QueryAgent/
├── QueryCliOptions.cs                    # CHANGED: --pages-dir -> --content-root
└── Program.cs                            # CHANGED: wires WikiLogAppender (new — Query had no backstop before)

backend/src/Grimoire.LintAgent/
├── LintCliOptions.cs                     # CHANGED: --pages-dir -> --content-root
└── Program.cs                            # CHANGED: --pages-dir -> --content-root argument parsing only. WikiLogAppender is deliberately NOT wired: Lint's policy.json grants no log.md write rule, so no log entry is ever owed and no backstop can apply. Wire it if/when Lint's write scope grows.

data/agents/{ingest,query,lint}/
├── policy.json                           # CHANGED: pages/ -> . (R3), tasks/ removed from ingest (R4)
└── system-prompt.md                      # CHANGED: log heading format (R5) and, for ingest/query, catalog entry format (R6)

backend/tests/
├── Grimoire.ArchTests/                   # + PagesWrapperRetirementBoundaryRuleTests (ADR-009 retirement probe)
├── Grimoire.Domain.UnitTests/            # + PolicyLoader "." normalization, SharedFileWriteGuard format-check unit tests
└── Grimoire.IntegrationTests/
    ├── PathConfiguration/                # CHANGED: ~6 files updated for the new default layout
    ├── IngestTaskRecordWatcherTests.cs   # CHANGED: TasksDir fixture updated
    └── LogEntryFormatEnforcementTests.cs, CatalogEntryFormatEnforcementTests.cs  # NEW

backend/src/Grimoire.EvalRunner/
├── Workspace/EvalWorkspace.cs            # CHANGED: PagesDir removed, mirrors the flattened layout
├── Scoring/{DeterministicScorers.cs,LintDeterministicScorers.cs}  # CHANGED: pages/ references updated
└── Scoring/                              # + log-paragraph and catalog-description specificity scorers (SC-005/SC-007)

backend/tests/Grimoire.AgentEvals/Fixtures/lint-seeded-defects/wiki/  # CHANGED: fixture layout flattened
```

**Structure Decision**: Existing `backend/` + `frontend/` split, unchanged; no new
project, no new frontend route. One new shared namespace
(`Grimoire.AgentRuntime.WikiLog`, replacing the Ingest-only `Grimoire.IngestAgent.IngestLog`)
following the established "shared runtime capability lives in `Grimoire.AgentRuntime`"
containment pattern every prior feature already uses for cross-agent-type mechanics
(Guardrails, Instructions, Host). No new port, no new persistence adapter — every
change here is either a path-composition value, a policy-data value, an
instruction-file convention, or a mechanical check inside an already-containment-tested
guardrails component.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — not applicable.

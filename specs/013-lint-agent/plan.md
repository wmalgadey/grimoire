# Implementation Plan: Lint Agent — Wiki Health Check

**Branch**: `013-lint-agent` | **Date**: 2026-07-30 | **Spec**: `specs/013-lint-agent/spec.md`

**Input**: Feature specification from `/specs/013-lint-agent/spec.md`

## Summary

Add the third wiki agent: Lint reads the whole wiki, judges its health under its own
instruction file, and produces a persistent Findings Report grouped by category
(content quality, metadata hygiene, structure); its only write action is refreshing
inbound-link counts in existing pages' frontmatter. It is a new `AgentProfile` on the
platform consolidated by feature 010 (ADR-013) — the practical proof of "adding an
agent requires only a profile" — dispatched with the same immediate-rejection,
single-active-run discipline `Grimoire.QueryAgent` already uses (limit fixed at 1,
not Ingest's queued-FIFO shape, since a second trigger while one run is active must be
rejected immediately, not queued for later). Its narrow write scope reuses ADR-015's
cross-process write-coordination mechanism unchanged, adding one new structural
guarantee no existing mode expresses — "frontmatter may change, body must not" —
via a new third write-scope mode drafted here as
`docs/adr/ADR-016-lint-write-scope-frontmatter-only-enforcement.md` (**proposed**,
extends ADR-015, supersedes nothing). Full rationale: `research.md`.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / Svelte 5 (frontend) —
unchanged existing stack (ADR-001).

**Primary Dependencies**: Existing `Grimoire.AgentRuntime`/ASP.NET Core stack. No new
packages. No new external system, no new port (Constitution I persistence exemption —
the Findings Report is local-filesystem persistence, like the Conversation Record).

**Storage**: One new runtime location, `ResolvedGrimoirePaths.FindingsDir`
(`GrimoirePathOptions.DefaultFindingsDirName = "findings"`, resolved beneath `DataDir`,
ADR-009 composition point), holding one Findings Report file per Lint Run at
`FindingsReportPathFor(runId)`. Outside `wiki/` and git (ADR-003) — operational
bookkeeping, not domain content. `data/agents/lint/policy.json` (new file): read
`pages/`, `index.md`, `log.md`; write `pages/` at `mode: "frontmatter-only"` (ADR-016)
— no create/delete capability at all (no create-only rule, no delete tool exists in
the platform regardless).

**Testing**: xUnit `Grimoire.IntegrationTests` with `FakeAgentProcess`/`FakeModelClient`
and a real filesystem for the deterministic guarantees (SC-001–SC-004); `Grimoire.ArchTests`
(Mono.Cecil IL scan, existing idiom) for Lint's write-boundary rule, both with Red/Green
probes. `Grimoire.EvalRunner` (ADR-012 recorded replay) for the agent-judgment
thresholds (SC-005–SC-008): new scenarios seeding wiki fixtures with known defects
(contradiction, orphan, missing tags, stale low-confidence page).

**Target Platform**: Same as existing Hub — cross-platform .NET processes, local dev
and CI; SvelteKit frontend.

**Project Type**: Web application (existing `backend/` + `frontend/` split). New
frontend route (`/lint`) — Lint has no submission form (a bare trigger) and no
per-run task board (at most one run ever active), so it does not reuse Ingest's
Kanban UI or Query's conversation UI.

**Performance Goals**: None new. A Lint Run reads the whole wiki once; no latency
budget is stated by the spec beyond "the report is complete, ordered by category and
severity — nothing silently truncated" (no truncation, not a speed target).

**Constraints**: FR-003 exactly one active Lint Run, immediate rejection (not queued)
on a second trigger; FR-010/FR-011 (ADR-016) the write scope is structurally
frontmatter-only and immune to injected wiki content; FR-014 concurrent-write
integrity reuses ADR-015 unchanged.

**Scale/Scope**: Single lint run at a time, system-wide (not per-conversation like
Query, not per-task like Ingest); concurrent with Ingest and Query activity per the
shared write-coordination guard.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Domain Architecture, Strategic DDD & Hexagonal Boundaries)**: PASS.
  No new external system, no new port: the Findings Report store is local-filesystem
  persistence (port-exempt, containment-bound) — confined to a new
  `Grimoire.Hub.LintFindings` namespace, mirroring `Grimoire.Hub.QueryConversations`'s
  containment. `LintToolRegistry`/`Grimoire.LintAgent` follow the existing
  per-agent-assembly containment pattern; no new namespace needed inside
  `Grimoire.AgentRuntime.Guardrails.Coordination` (ADR-016's check lives inside the
  existing `SharedFileWriteGuard`, already containment-tested).
- **Principle II (Pragmatic Testing Strategy)**: PASS. Success-criteria split is
  clean: SC-001–SC-004 (dispatch/lifecycle guarantees, guardrail pass-through,
  concurrency rejection/liveness, structural rule) are deterministic harness
  guarantees at 100%, hermetically tested; SC-005–SC-008 (finding recall/precision,
  tag/confidence proposal quality, inbound-link accuracy) are agent-judgment
  thresholds verified by `Grimoire.EvalRunner` recorded-replay evaluation against
  seeded-defect wiki fixtures — no threshold here is a 100%-deterministic-by-spec-defect.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: **CONDITIONAL PASS.**
  All ADRs in `docs/adr/` (001–015) were read; see the table below. This plan
  introduces one new structural boundary (the frontmatter-only write-scope mode ADR-015
  did not anticipate in this shape), so a new ADR is mandatory:
  `ADR-016-lint-write-scope-frontmatter-only-enforcement.md` is drafted with status
  **proposed**, extending (not superseding) ADR-015 — the project owner will sign off.
  **Gate: ADR-016 MUST reach Accepted before `/speckit-tasks` is invoked** for this
  feature. The first task in `tasks.md` must be the new Lint write-boundary structural
  test with its Red/Green probe.
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new infrastructure
  (one new markdown-file runtime location under the existing data dir; OTel backend
  per ADR-005 unchanged). Full `## Observability` section below, with the mandatory
  logging/trace-contract derivation rules for `tasks.md`.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS. Every finding-level
  judgment (is this a contradiction, is a claim outdated, are two pages related, what
  tag/confidence to propose) lives entirely in `agents/lint/system-prompt.md` — no
  backend rule generates or suppresses a content-quality finding. Backend gains only
  harness capability: dispatch, the frontmatter-only structural check, the Findings
  Report's mechanical persistence. See `## Agentic Boundary` below.

Re-check after Phase 1 design: no change — no unjustified violations, Complexity
Tracking not needed. The only open gate is ADR-016 acceptance (Principle III above).

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*
(All of ADR-001 through ADR-015 were read in full.)

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | Stays on the existing .NET/SvelteKit stack; no new language, runtime, or package. |
| ADR-002 | Ingest agent execution model | `Grimoire.LintAgent` is a standalone spawned child process, same CLI-args/credential-scoping contract as Ingest and Query. |
| ADR-003 | Domain vs. operational state persistence | The Findings Report is operational bookkeeping under `<base>/data/findings/`, outside `wiki/` and git — same split as Conversation Records and Task Artifacts. |
| ADR-004 | Credential scoping | Untouched; Lint needs the same API-key injection as any agent, no new secret. |
| ADR-005 | Observability backend | New `wiki.lint.*`/`lint.*` signals export via the existing OTel SDK/Aspire setup; CI verification via in-memory exporter assertions. |
| ADR-006 | Agent tool-use loop & guarded tool boundary | Lint inherits the loop/guarded-executor/policy pattern as-is; its registry offers `list_files`, `read_file`, `write_file` (all three, unlike Query's two), scoped by its own policy file. |
| ADR-007 | Agent instruction surface | `agents/lint/system-prompt.md` (fail-closed load, hash recorded); no `default-user-prompt.md` — Lint runs take no per-run user prompt (`RequiredInstructionDocuments = { SystemPrompt }`, same as Query). |
| ADR-008 | Agent event channel, run supervision, run queue | Liveness supervision (heartbeat, silence-window failure) reused unchanged; Lint's dispatch shape follows the "Considered dispatch precedent" note below — immediate-rejection semaphore(1,1), not Ingest's persisted FIFO queue. |
| ADR-009 | Runtime path configuration | `FindingsDir` added via the single composition point (`GrimoirePathOptions.DefaultFindingsDirName`), resolved/auto-created by `GrimoirePathResolver`. No ambient discovery. |
| ADR-010 | Hexagonal ports & adapter namespaces | Persistence exemption applies to the Findings Report store (no port); new containment: filesystem writing confined to `Grimoire.Hub.LintFindings` alongside existing Hub writer namespaces. |
| ADR-011 | Query agent shared runtime & concurrency model | Read for precedent only (immediate-rejection semaphore shape) — not a constraint on Lint; Lint is its own coordinator, not a variant of Query's. |
| ADR-012 | Eval runner & recorded replay | New Lint scenarios and recordings added under the existing recorded-replay mechanism, same as every prior agent. |
| ADR-013 | Unified agent platform packaging (feature 010) | Confirmed, not altered: Lint is a new `Grimoire.LintAgent` executable + `AgentProfile`, exactly the shape ADR-013 established — the practical proof of its "adding an agent requires only a profile" claim (010's own FR-003/SC-005). |
| ADR-014 | Query conversation records (feature 011) | Read for precedent only (Hub-written, sentinel-safe record format) — informs the Findings Report format design; not a constraint on Lint. |
| ADR-015 | Query write scope & wiki write coordination (feature 012) | Binding, reused unchanged: the cross-process lock, compare-and-swap, `SharedFileWriteGuard`/`CrossProcessFileLock`, and the `read-write`/`create-only` modes. Lint is wired into it via the same `--write-locks-dir` argument Ingest and Query already receive. |
| ADR-016 | Lint write scope & frontmatter-only enforcement (new, this plan, **proposed**) | Fixes the third `frontmatter-only` write-scope mode and its structural body-preservation check — must be Accepted before `/speckit-tasks`; extends ADR-015, supersedes nothing. |

**New ADR required?**: Yes —
`docs/adr/ADR-016-lint-write-scope-frontmatter-only-enforcement.md`, drafted as part
of this plan with status **proposed** (owner sign-off pending; Constitution Check
gate above is conditional on its acceptance).

### Considered dispatch precedent

Two existing coordinators were compared against FR-003 ("a trigger while one is
active MUST be rejected immediately with a clear message"): `IngestRunCoordinator`
(single-slot, but over-limit submissions join a **persisted FIFO queue** — never
rejected) versus `QueryRunCoordinator` (`SemaphoreSlim.WaitAsync(0, ...)`, a
non-blocking zero-timeout acquire, returning a rejection result immediately with no
queue at all when the limit is reached). FR-003's literal wording — immediate
rejection, no mention of queuing — matches Query's shape, not Ingest's.
`LintRunCoordinator` therefore copies `QueryRunCoordinator`'s immediate-rejection
semaphore shape with the limit fixed at 1 (not configurable — the spec sets it, not
an operator), and needs no queue table in the operational SQLite store at all: a
rejected trigger is simply rejected, nothing to resume after a Hub restart. The
liveness-supervision half (heartbeat, silence-window failure, terminal-state
handling) is identical in both coordinators and is reused as-is.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|-----------------|
| Contradiction/outdated-claim/missing-cross-reference/scattered-concept/gap judgment | Agentic core | `agents/lint/system-prompt.md` |
| Tag and confidence-score proposals, review-candidate reasoning | Agentic core | `agents/lint/system-prompt.md` |
| Finding description and proposed remediation wording | Agentic core | `agents/lint/system-prompt.md` |
| Which tool calls constitute "refresh inbound-link counts" vs. anything else | Agentic core (the agent decides *what* to write; the harness decides *whether* it's allowed) | `agents/lint/system-prompt.md` |
| Dispatch: trigger, reject-if-busy, liveness supervision | Harness | `Grimoire.Hub.LintDispatch.LintRunCoordinator` |
| Frontmatter-only structural enforcement (body must not change) | Harness | `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard` (ADR-016) |
| Findings Report persistence (mechanical: package the run's narrative into the file) | Harness | `Grimoire.Hub.LintFindings.FindingsReportStore` |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (100% fail-closed instruction load with hash; 100% of runs produce a run record + Findings Report or a failed state with reason) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess`/`FakeModelClient`, temp wiki root | Missing/unreadable/empty system-prompt fixtures | Mirrors `QueryInstructionLoadTests`/`IngestInstructionLoadTests` idiom |
| SC-002 (100% of write actions pass the guarded boundary; 100% of out-of-scope attempts denied with reasons; structural rule Red/Green-probed) | Deterministic guarantee | Hermetic integration test + structural (ArchTests) | `FakeAgentProcess`, temp wiki root | Scripted frontmatter-only write (allow), scripted body-changing write (deny), scripted page-creation/deletion attempt (deny — no create-only rule, no delete tool) | New `LintAgentGuardedWriteBoundaryRuleTests` (allow-listed-namespace shape) with Red/Green probe |
| SC-003 (100% of concurrent-trigger rejections while a run is active; 100% of dead runs detected within the liveness window) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` (controllable silence), fake `TimeProvider` | Concurrent trigger fixture; liveness-window fixture | Mirrors `QueryConcurrencyLimitTests`/`IngestLivenessSupervisionTests` idiom |
| SC-004 (100% of frontmatter updates intact under concurrent write activity) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` racing a scripted Ingest/Query-style writer | Reuses 012's `ConcurrentWikiWriteIntegrityTests` fixtures, extended with a Lint-style frontmatter-only writer | Proves ADR-015's guard, composed with ADR-016's body check, still serializes correctly under real contention |
| SC-005 (≥ 85% of seeded defects found, per category) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 85% | Recorded/replayed `IModelClient` (ADR-012) | New scenario(s) seeding a wiki fixture with a contradiction, an orphan, a missing-cross-reference pair, missing tags, missing confidence, a stale low-confidence page | One scenario per defect category or one composite fixture scored per-category — decided in `tasks.md` |
| SC-006 (≥ 90% of sampled findings genuine, not fabricated) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 90% | Recorded/replayed `IModelClient` | Reuses SC-005's fixtures | Scorer cross-checks each reported finding's affected pages actually exhibit the described problem |
| SC-007 (≥ 90% tag-taxonomy conformance; ≥ 90% confidence-convention conformance with coherent reason) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 90% | Recorded/replayed `IModelClient` | Reuses SC-005's fixtures | Deterministic sub-scorer parsing proposed tags/confidence against `agents/ingest/system-prompt.md`'s taxonomy/formula, mirroring `QueryDeterministicScorers`' idiom |
| SC-008 (≥ 95% accurate inbound-link counts after a run) | Agent-judgment threshold (mechanical outcome, agent-triggered) | Evaluation (recorded replay) + deterministic assertion, threshold ≥ 95% | Recorded/replayed `IModelClient` | Wiki fixture with known cross-link graph | Deterministic scorer recomputes the true inbound-link graph and compares to post-run frontmatter — the *comparison* is deterministic even though *whether Lint attempted the refresh at all* is agent-judgment |

Supporting deterministic tests (not SC-numbered but contract-bearing, feeding
`tasks.md`): `WriteRule`/`PolicyDecision`/`PolicyLoader` unit tests for the new
`frontmatter-only` mode value (fail-closed on malformed frontmatter, on a missing
target, on a body change); `SharedFileWriteGuard` unit tests for the frontmatter/body
split in isolation; Findings Report format round-trip and injection-resistance tests
(mirroring `ConversationRecordFormatTests`' idiom, since Findings Reports are
agent-narrative-derived content facing the same prompt-injection surface).

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `wiki.lint.runs_total` | Counter | Lint Runs reaching a terminal state | `outcome=completed\|failed` |
| `wiki.lint.findings_total` | Counter | Findings produced across all runs | `category=content_quality\|metadata_hygiene\|structure` |
| `wiki.lint.inbound_links_refreshed_total` | Counter | Pages whose inbound-link count was updated | none |
| `wiki.lint.triggers_rejected_total` | Counter | Trigger attempts rejected because a run was already active | none |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| `lint.run.triggered` | INFO | A Lint Run is accepted and dispatched | `run_id` |
| `lint.run.rejected` | INFO | A trigger is rejected — a run is already active | none |
| `lint.instructions.loaded` | INFO | System prompt loaded fail-closed, hash recorded | `run_id`, `sha256` |
| `lint.instructions.load_failed` | ERROR | System prompt missing/unreadable/empty | `run_id`, `reason` |
| `lint.run.completed` | INFO | Run reaches a completed terminal state | `run_id`, `findings_count` |
| `lint.run.failed` | ERROR | Run reaches a failed terminal state (incl. liveness timeout) | `run_id`, `reason` |
| `lint.findings_report.created` | INFO | The Findings Report file is written | `run_id`, `path` |

**Note**: out-of-scope write denials (`out_of_scope`, `frontmatter_only_target_missing`,
`frontmatter_only_malformed_document`, `frontmatter_only_body_changed`,
`write_conflict_stale_read`, `write_coordination_timeout`) reuse the **existing**
`wiki.write_conflict.rejected` log event and `wiki.write_conflict.rejections_total`
counter established in feature 012 (ADR-015), extending only their `reason` label
enumeration — no new event/metric for these, per the constitution's preference for one
signal per concept rather than per-agent duplicates.

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md` work —
implementation with stable event name and mandatory fields, deterministic integration
tests validating event name/level/mandatory fields, and CI enforcement in the standard
PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `hub.lint.trigger` | root (HTTP request) | `run_id`, `outcome=accepted\|rejected` |
| `hub.lint.run_supervision` | `hub.lint.trigger` | `run_id` |
| `hub.lint.write_findings_report` | `hub.lint.run_supervision` | `run_id`, `path` |
| `lint_agent.run` | root (agent process) | `run_id` |
| `lint_agent.load_instructions` | `lint_agent.run` | `sha256` |
| `lint_agent.tool_call` | `lint_agent.run` | `tool_name`, `path` |

**Derivation rule (MANDATORY)**: maps to concrete `tasks.md` work — implementation
(span creation with declared parent/child + attributes), deterministic integration
tests (name/linkage/correlation via `run_id`, in-memory exporter per ADR-005), and CI
enforcement. `guardrails.acquire_write_lock` (feature 012) is reused unchanged as a
child of `lint_agent.tool_call` for Lint's frontmatter writes — no new lock-acquisition
span.

## Project Structure

### Documentation (this feature)

```text
specs/013-lint-agent/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── findings-report-format.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/Grimoire.Domain/Guardrails/
├── SafetyPolicy.cs                      # CHANGED: WriteRule.Mode (enum) replaces CreateOnly (bool)
└── PolicyDecision.cs                    # CHANGED: Allow(WriteMode) replaces Allow(bool isCreateOnly)

backend/src/Grimoire.AgentRuntime/
├── Instructions/PolicyLoader.cs         # CHANGED: "frontmatter-only" recognized mode value
└── Guardrails/
    ├── GuardedToolExecutor.cs           # CHANGED: passes Mode + content into EvaluateWriteAsync
    ├── DeniedActionRecord.cs            # CHANGED: 3 new reason strings (doc comment only)
    └── Coordination/SharedFileWriteGuard.cs  # CHANGED: frontmatter/body-split check for FrontmatterOnly

backend/src/Grimoire.LintAgent/           # NEW standalone executable (Grimoire.LintAgent)
├── Program.cs                            # Composition root: AgentProfile + intent hooks + AgentHost
├── LintCliOptions.cs
└── LintToolRegistry.cs                   # list_files, read_file, write_file

backend/src/Grimoire.Hub/
├── LintDispatch/                         # NEW namespace (Grimoire.Hub.LintDispatch)
│   ├── LintRunCoordinator.cs            # immediate-rejection semaphore(1,1), liveness supervision
│   └── LintSubmissionEndpoints.cs       # POST /api/lint-runs (bare trigger, no body)
├── LintFindings/                         # NEW namespace (Grimoire.Hub.LintFindings)
│   ├── FindingsReportStore.cs           # Hub-written, one file per run
│   └── FindingsReportFormat.cs          # writer + parser (sentinel-safe, mirrors ConversationRecordFormat idiom)
└── Runtime/Paths/
    ├── GrimoirePathOptions.cs           # CHANGED: + FindingsDir / DefaultFindingsDirName
    ├── GrimoirePathResolver.cs          # CHANGED: findings_dir resolution/auto-create/report
    └── ResolvedGrimoirePaths.cs         # CHANGED: + FindingsReportPathFor

data/agents/lint/                         # NEW
├── policy.json                           # read pages/,index.md,log.md; write pages/ (frontmatter-only)
└── system-prompt.md

backend/tests/
├── Grimoire.ArchTests/                  # + LintAgentGuardedWriteBoundaryRuleTests, Red/Green probe
├── Grimoire.Domain.UnitTests/           # + WriteMode/PolicyDecision/SafetyPolicy frontmatter-only tests
└── Grimoire.IntegrationTests/           # + Lint dispatch/lifecycle/findings/observability/concurrency tests

backend/src/Grimoire.EvalRunner/
├── Scenarios/LintScenarioDefinitions.cs    # NEW: seeded-defect wiki fixtures
└── Scoring/LintDeterministicScorers.cs     # NEW: tag/confidence/inbound-link sub-scorers

data/evals/recordings/
├── lint-defects-found/                  # NEW
├── lint-metadata-proposals/             # NEW
└── lint-inbound-links-refreshed/        # NEW

frontend/src/routes/lint/
└── +page.svelte                         # NEW: bare trigger button + Findings Report viewer
frontend/src/lib/services/
└── lintApi.ts                           # NEW: typed fetch client

data/findings/                            # NEW runtime location (ADR-009), git-ignored
```

**Structure Decision**: Existing `backend/` + `frontend/` split, unchanged. Two new
Hub namespaces (`LintDispatch`, `LintFindings`) following the established
namespace-per-concern convention; one new standalone agent executable
(`Grimoire.LintAgent`), matching the existing per-agent-process pattern exactly. No
new port (Constitution I's no-extra-assemblies default still holds — this is the
*n*-th instance of the existing agent-process shape, not a new shape).

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — not applicable.

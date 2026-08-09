# Implementation Plan: Query Agent Synthesis Writes

**Branch**: `012-query-synthesis-writes` | **Date**: 2026-07-30 | **Spec**: `specs/012-query-synthesis-writes/spec.md`

**Input**: Feature specification from `/specs/012-query-synthesis-writes/spec.md`

## Summary

Give the Query agent a narrow, structurally-guarded write capability: it may create
new **Synthesis Pages** and append to `index.md`/`log.md` through the same guarded
tool boundary every agent uses, and it may never modify an existing content page.
The Synthesis Decision (does this answer contain a genuinely new insight worth
preserving, and what the page says) stays entirely agentic, in
`agents/query/system-prompt.md`. Enabling this safely requires two harness changes:
(1) `QueryToolRegistry` gains the `write_file` tool and `data/agents/query/policy.json`
gains write rules, one of them `create-only` for `pages/`; (2) a new cross-process
write-coordination mechanism (`SharedFileWriteGuard`) inside `GuardedToolExecutor`
protects `index.md`/`log.md`/existing pages from lost updates now that Query
(up to 3 concurrent processes) and Ingest can write concurrently — and, by
construction, protects the future Lint agent (feature 013) too, since it shares the
same chokepoint. Full rationale: `research.md`; superseding decision:
`docs/adr/ADR-015-query-write-scope-and-wiki-write-coordination.md` (**proposed** —
supersedes ADR-011's Query-is-structurally-write-free framing only).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript / Svelte 5 (frontend) —
unchanged existing stack (ADR-001). No frontend change: the answer text and the
per-turn record already render free-form content and an optional created-pages list
(ADR-014 forward-compat key).

**Primary Dependencies**: Existing `Grimoire.AgentRuntime`/ASP.NET Core stack. No new
packages. No new external system, no new port (persistence exemption, Constitution I)
— the write-coordination mechanism is local-filesystem locking.

**Storage**: New operational location `ResolvedGrimoirePaths.WriteLocksDir`
(`GrimoirePathOptions.DefaultWriteLocksDirName = "write-locks"`, resolved beneath
`DataDir`, ADR-009 composition point) holding one empty lock file per contested wiki
target, named by SHA-256 of its canonical path. Outside `wiki/` and git (ADR-003).
`data/agents/query/policy.json` version bumps to 2 (adds `write` rules + `mode`).

**Testing**: xUnit `Grimoire.IntegrationTests` with `FakeAgentProcess`/`FakeModelClient`
and a real filesystem under a temp base dir for the deterministic guarantees
(SC-001–SC-004), including a genuine **multi-process** concurrency test spawning real
`dotnet run` instances of a test harness against the same wiki root to exercise actual
OS-level file locking (in-process fakes alone cannot prove cross-process exclusion).
`Grimoire.ArchTests` (Mono.Cecil IL scan, existing idiom) for the rewritten Query
write-boundary rule and the new Coordination containment rule, both with Red/Green
probes. `Grimoire.EvalRunner` (ADR-012 recorded replay) for the agent-judgment
thresholds (SC-005–SC-008): new scenarios under `Scenarios/QueryScenarioDefinitions.cs`
with new recordings under `data/evals/recordings/`.

**Target Platform**: Same as existing Hub — cross-platform .NET processes, local dev
and CI; SvelteKit frontend unchanged.

**Project Type**: Web application (existing `backend/` + `frontend/` split).

**Performance Goals**: Lock hold time per guarded write bounded to the
existence/hash-check-plus-atomic-rename critical section (single-digit milliseconds);
acquisition bounded by a configurable backoff cap (default 5s) so a worst-case
contention or stale-lock scenario fails closed with a recorded reason rather than
stalling a turn indefinitely (FR-010).

**Constraints**: FR-005 create-only enforcement must be structural (existence check),
not agent self-restraint; FR-009 no lost updates across concurrent writers; FR-010 no
material streaming/interruption regression versus feature 008/011; FR-011 no rollback
of writes on interruption beyond the existing per-run `WriteJournal` behavior.

**Scale/Scope**: Up to `QueryConcurrencyLimit` (3, unchanged) concurrent Query
processes plus 1 concurrent Ingest process, all potentially writing; contention is
expected to be rare (distinct new-page paths per turn) and, when it occurs, resolved
by one bounded retry via a tool-error round-trip, not a queue.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I (Domain Architecture, Strategic DDD & Hexagonal Boundaries)**: PASS.
  No new external system, no new port: `SharedFileWriteGuard` is local-filesystem
  coordination, explicitly port-exempt but containment-bound — confined to a new
  `Grimoire.AgentRuntime.Guardrails.Coordination` namespace, constructed only from
  `GuardedToolExecutor`, enforced by a new structural containment rule with a
  Red/Green probe. Existing containment rules (C1–C6, C8, D1, D2, N1) are untouched;
  C7 (Query structurally write-free) is superseded by ADR-015 and its test rewritten.
- **Principle II (Pragmatic Testing Strategy)**: PASS. The spec's success-criteria
  split is respected: SC-001–SC-004 (guardrail pass-through, page/index/log
  completeness, concurrency integrity, structural rule) are deterministic harness
  guarantees at 100%, hermetically tested including a real multi-process lock test;
  SC-005–SC-008 (does the agent recognize a synthesis, does it decline routine
  lookups, does the page meet convention, does it decline edit requests) are
  agent-judgment thresholds verified by `Grimoire.EvalRunner` recorded-replay
  evaluation, not reimplemented as deterministic rules — no threshold here is
  100%-deterministic-by-spec-defect.
- **Principle III (ADR-Driven & Test-Enforced Architecture)**: **CONDITIONAL PASS.**
  All ADRs in `docs/adr/` (001–014) were read; see the table below. This plan
  supersedes part of an Accepted decision (ADR-011) and introduces a new structural
  boundary (cross-process write coordination), so a new ADR is mandatory:
  `ADR-015-query-write-scope-and-wiki-write-coordination.md` is drafted with status
  **proposed** — the project owner will sign off. **Gate: ADR-015 MUST reach Accepted
  before `/speckit-tasks` is invoked** for this feature, and feature 013's plan may
  only cite it as a constraint once it has. The first task in `tasks.md` must be the
  rewritten write-boundary structural test with its Red/Green probe.
- **Principle IV (Behavioral & Observable Engineering)**: PASS. No new infrastructure
  (plain lock files under the existing data dir; OTel backend per ADR-005 unchanged).
  Full `## Observability` section below, with the mandatory logging/trace-contract
  derivation rules for `tasks.md`.
- **Principle V (Agentic Core & Deterministic Harness)**: PASS, and this feature's
  central concern. The Synthesis Decision, page content, and confidence/tag choices
  are exercised entirely under `agents/query/system-prompt.md` — no backend heuristic
  scores or vetoes novelty (FR-002). Backend gains only harness capability: a write
  tool registration, a create-only existence check, and a lock/CAS mechanic — none of
  it inspects or judges wiki content. See `## Agentic Boundary` below for the full
  capability split.

Re-check after Phase 1 design: no change — no unjustified violations, Complexity
Tracking not needed. The only open gate is ADR-015 acceptance (Principle III above).

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*
(All of ADR-001 through ADR-014 were read in full.)

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend/frontend tech stack | Stays on the existing .NET/SvelteKit stack; no new language, runtime, or package. |
| ADR-002 | Ingest agent execution model | Query remains a standalone spawned child process that writes its own working tree directly; the new write-locks directory is passed in the same way `--wiki-root` is (a new CLI argument), not routed through the Hub. |
| ADR-003 | Domain vs. operational state persistence | The new write-lock files are operational bookkeeping, outside `wiki/` and git, resolved beneath `DataDir` like `StateDbPath`/`ConversationsDir`. |
| ADR-004 | Credential scoping | Untouched; the write-coordination mechanism needs no credential. |
| ADR-005 | Observability backend | New `wiki.query.synthesis.*`/`wiki.write_lock.*` signals export via the existing OTel SDK/Aspire setup; CI verification via in-memory exporter assertions. |
| ADR-006 | Agent tool-use loop & guarded tool boundary | The write-coordination guard is added *inside* `GuardedToolExecutor.ExecuteWriteFileAsync`, at the same chokepoint — not a second boundary. `WriteJournal` rollback-on-failure semantics are unchanged; the guard releases its lock in `finally` regardless of run outcome. |
| ADR-007 | Agent instruction surface | `agents/query/system-prompt.md` is rewritten to describe the new capability and its limits (fail-closed load/hash recording mechanism itself is unchanged). |
| ADR-008 | Agent event channel, run supervision, run queue | `completed` event metadata gains an optional created-pages list (mechanical, sourced from the run's own `WriteJournal`/`TouchedPaths`); liveness supervision, heartbeat, and `QueryRunCoordinator`'s reject-over-limit semaphore (limit 3, no queue) are unchanged. |
| ADR-009 | Runtime path configuration | `WriteLocksDir` is added via the single composition point (`GrimoirePathOptions.DefaultWriteLocksDirName`), resolved/auto-created by `GrimoirePathResolver`, reported alongside existing locations. No ambient discovery. |
| ADR-010 | Hexagonal ports & adapter namespaces | Persistence exemption applies to `SharedFileWriteGuard` (no port); new containment rule confines `Grimoire.AgentRuntime.Guardrails.Coordination` construction to `GuardedToolExecutor`. Existing C-rules keep passing. |
| ADR-011 | Query agent shared runtime & concurrency model | **Superseded in part by ADR-015**: the "Query is structurally write-free" framing and containment rule C7. Everything else — shared `AgentRuntime`, streaming, bounded concurrency (limit 3, reject-over-limit, no queue), interruption vs. liveness-failure — remains fully in force. |
| ADR-012 | Eval runner & recorded replay | New Query synthesis scenarios and recordings are added under the existing recorded-replay mechanism; existing recordings/fingerprints for unrelated scenarios are untouched. |
| ADR-013 | Unified agent platform packaging (feature 010) | Confirmed, not altered: this feature changes the Query profile's declared `ToolRegistry` and policy file via its own composition-root call in `Grimoire.QueryAgent/Program.cs`, exactly the change ADR-013 anticipated — no platform/packaging change. |
| ADR-014 | Query conversation records (feature 011) | The bookkeeping block's open-YAML-mapping design absorbs a new `created_pages:` key with no restructuring, exactly as ADR-014 reserved. |
| ADR-015 | Query write scope & wiki write coordination (new, this plan, **proposed**) | Fixes the Query write-tool/policy/create-only design and the `SharedFileWriteGuard` cross-process coordination mechanism — must be Accepted before `/speckit-tasks`; feature 013 adopts it by reference. |

**New ADR required?**: Yes —
`docs/adr/ADR-015-query-write-scope-and-wiki-write-coordination.md`, drafted as part
of this plan with status **proposed** (owner sign-off pending; Constitution Check
gate above is conditional on its acceptance).

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|-----------------|
| Synthesis Decision (is this insight genuinely new; worth preserving) | Agentic core | `agents/query/system-prompt.md` |
| Synthesis Page content, frontmatter values, source links, confidence + reason | Agentic core | `agents/query/system-prompt.md` |
| Deciding whether to honor an explicit "save this" request absent a genuine insight | Agentic core | `agents/query/system-prompt.md` |
| Declining to edit existing content when asked | Agentic core | `agents/query/system-prompt.md` (the harness makes the edit structurally impossible regardless, via create-only mode) |
| Write-tool registration (which tools Query may call at all) | Harness | `Grimoire.QueryAgent.QueryToolRegistry` |
| Deny-by-default policy evaluation, path-prefix + mode matching | Harness | `Grimoire.Domain.Guardrails.SafetyPolicy`, `data/agents/query/policy.json` |
| Create-only existence check (cannot overwrite a pre-existing page) | Harness | `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor` |
| Cross-process lock + compare-and-swap for shared targets | Harness | `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard` |
| Reporting which pages a run created (mechanical, from the write journal) | Harness | `Grimoire.AgentRuntime.RunEvents.RunEventEmitter` / `RunCompletionMetadata` |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (100% of writes pass the guarded boundary with a recorded decision; 100% of out-of-scope attempts denied with reasons) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess`/`FakeModelClient`, temp wiki root | Scripted write to a new page (allow), scripted write to an existing page (create-only deny), scripted write outside `pages/`/`index.md`/`log.md` (deny) | Asserts `DeniedActionRecord` shape/reason for each denial kind (`create_only_target_exists`, `out_of_scope`, `write_conflict_stale_read`, `write_coordination_timeout`) |
| SC-002 (100% of pages created by a turn listed on its record, with matching index/log entries) | Deterministic guarantee | Hermetic integration test | `FakeAgentProcess` scripted multi-write turn | One turn creating a page + appending index + log | Asserts `RunCompletionMetadata.CreatedArtifacts` reaches the Conversation Record's `created_pages:` key |
| SC-003 (100% of completed concurrent writes intact; streaming/interruption responsiveness holds) | Deterministic guarantee | Hermetic integration test **+** real multi-process test | In-process: `FakeAgentProcess` pair racing the same `index.md`. Cross-process: two real `dotnet run` child harnesses targeting the same temp wiki root and lock dir | Two synthesis turns creating distinct pages but both appending `index.md`/`log.md` simultaneously; one interleaved with a scripted Ingest-style writer | Proves the compare-and-swap + OS lock genuinely serializes across separate processes, not just in-process fakes; asserts no dropped index/log entry and bounded latency overhead |
| SC-004 (structural write-boundary rule passes with a verified Red/Green probe) | Deterministic guarantee | Structural (ArchTests) | Mono.Cecil IL scan (existing idiom) | Scratch violating class added/removed per probe | Rewritten `QueryAgentGuardedWriteBoundaryRuleTests` (allow-listed-namespace shape) + new Coordination containment rule, each with its own probe |
| SC-005 (≥ 85% of sampled genuine-insight turns preserve a Synthesis Page) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 85% | Recorded/replayed `IModelClient` (ADR-012) | New scenario `query-synthesis-created` samples across topics with a genuine cross-page insight | Scorer checks a page was created via `write_file` in the transcript and its content plausibly reflects the insight (rubric-scored) |
| SC-006 (≥ 90% of sampled routine-lookup turns create no page) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 90% | Recorded/replayed `IModelClient` | New scenario `query-synthesis-declined-routine` | Scorer asserts zero `write_file` calls in the transcript |
| SC-007 (≥ 95% of sampled created pages carry complete, convention-conforming frontmatter and ≥ 1 source link) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 95% | Recorded/replayed `IModelClient` | Reuses `query-synthesis-created` samples | Deterministic sub-scorer (frontmatter parse: tags incl. synthesis marker, confidence + reason, review date, ≥ 1 wikilink) layered under the eval, per the existing `QueryDeterministicScorers` idiom |
| SC-008 (≥ 90% of sampled edit-request prompts receive a declining answer) | Agent-judgment threshold | Evaluation (recorded replay), threshold ≥ 90% | Recorded/replayed `IModelClient` | New scenario `query-synthesis-decline-edit-request` | Scorer checks the answer text declines and explains, independent of SC-001's structural guarantee that the edit cannot happen regardless |

Supporting deterministic tests (not SC-numbered but contract-bearing, feeding
`tasks.md`): `SafetyPolicy`/`PolicyLoader` unit tests for the new `mode` field
(fail-closed on an unrecognized mode value), `SharedFileWriteGuard` unit tests for
read-hash tracking and CAS decision logic in isolation, lock-file naming collision
resistance (SHA-256 keyed), and the full logging/trace-contract tests listed under
Observability.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

Delta against feature 008/011 (`specs/008-query-agent/plan.md`,
`specs/011-query-conversations/plan.md ## Observability`): all existing `query.*` and
`query.conversation.*` rows are unchanged and must keep passing. The rows below are
strictly additive.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|--------------|--------|
| `wiki.query.synthesis_pages_created_total` | Counter | Synthesis Pages successfully created by a Query turn | none |
| `wiki.write_lock.acquisitions_total` | Counter | Write-coordination lock acquisition attempts | `outcome=acquired\|timeout` |
| `wiki.write_lock.wait_seconds` | Histogram | Time spent waiting to acquire a write-coordination lock | none |
| `wiki.write_conflict.rejections_total` | Counter | Writes rejected by compare-and-swap (stale read) or create-only check | `reason=create_only_target_exists\|write_conflict_stale_read` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-------------------|
| `wiki.query.synthesis_page_created` | INFO | A Query turn's `write_file` call creates a new Synthesis Page | `task_id`, `path`, `turn` |
| `wiki.write_lock.timeout` | WARN | Lock acquisition exceeds the backoff cap | `task_id`, `path`, `wait_ms` |
| `wiki.write_conflict.rejected` | WARN | A write is denied by create-only check or compare-and-swap | `task_id`, `path`, `reason`, `turn` |

**Derivation rule (MANDATORY)**: every row above maps to concrete `tasks.md` work —
implementation with stable event name and mandatory fields (extending the existing
`IToolCallInstrumentation`/log-events idiom in `Grimoire.AgentRuntime.Guardrails`),
deterministic integration tests validating event name/level/mandatory fields, and CI
enforcement in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|------------|
| `guardrails.acquire_write_lock` | `query_agent.tool_call` (or `ingest_agent.tool_call`, whichever agent's write triggered it) | `path`, `outcome`, `wait_ms` |

**Derivation rule (MANDATORY)**: maps to concrete `tasks.md` work — implementation
(span creation with declared parent/child + attributes), deterministic integration
tests (name/linkage/correlation via `task_id`, in-memory exporter per ADR-005), and CI
enforcement. Logs/metrics of the coordination path are emitted within this span
context and correlate via `task_id`/`path`.

## Project Structure

### Documentation (this feature)

```text
specs/012-query-synthesis-writes/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── query-write-scope-and-coordination.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/src/Grimoire.Domain/Guardrails/
├── SafetyPolicy.cs                      # CHANGED: write rules carry a mode (read-write | create-only)
└── PolicyDecision.cs                    # CHANGED: Allow() carries IsCreateOnly

backend/src/Grimoire.AgentRuntime/
├── Instructions/PolicyLoader.cs         # CHANGED: PolicyRuleSchema.Mode, fail-closed on unrecognized value
├── Guardrails/
│   ├── GuardedToolExecutor.cs           # CHANGED: write path calls SharedFileWriteGuard before journal+atomic-rename
│   ├── DeniedActionRecord.cs            # CHANGED: new reason strings (create_only_target_exists, write_conflict_stale_read, write_coordination_timeout)
│   └── Coordination/                    # NEW namespace (Grimoire.AgentRuntime.Guardrails.Coordination)
│       ├── SharedFileWriteGuard.cs      # per-run read-hash tracking + lock acquisition + CAS decision
│       └── CrossProcessFileLock.cs      # OS-level exclusive lock on a per-target lock file, bounded backoff
└── RunEvents/RunEventEmitter.cs         # CHANGED: RunCompletionMetadata gains CreatedArtifacts

backend/src/Grimoire.QueryAgent/
├── QueryToolRegistry.cs                 # CHANGED: + ToolRegistry.WriteFileDefinition
└── Program.cs                           # CHANGED: passes --write-locks-dir through to GuardedToolExecutor composition

backend/src/Grimoire.IngestAgent/
└── Program.cs                           # CHANGED: passes --write-locks-dir through (same guard protects Ingest's writes)

backend/src/Grimoire.Hub/Runtime/Paths/
├── GrimoirePathOptions.cs               # CHANGED: + WriteLocksDir / DefaultWriteLocksDirName
├── GrimoirePathResolver.cs              # CHANGED: write_locks_dir resolution/auto-create/report
└── ResolvedGrimoirePaths.cs             # CHANGED: + WriteLocksDir

backend/src/Grimoire.Hub/QueryConversations/
└── ConversationRecordFormat.cs          # CHANGED: writes/parses the optional created_pages: key

data/agents/query/
├── policy.json                          # CHANGED: version 2, write rules incl. create-only pages/
└── system-prompt.md                     # CHANGED: describes the new Synthesis capability and its limits

backend/tests/
├── Grimoire.ArchTests/                  # CHANGED: rewritten Query write-boundary rule; + Coordination containment rule, both with Red/Green probes
└── Grimoire.IntegrationTests/           # + guard/lock/CAS/policy-mode tests, multi-process concurrency test, logging/trace-contract tests

backend/src/Grimoire.EvalRunner/
├── Scenarios/QueryScenarioDefinitions.cs   # CHANGED: + query-synthesis-created, query-synthesis-declined-routine, query-synthesis-decline-edit-request
└── Scoring/QueryDeterministicScorers.cs    # CHANGED: + frontmatter/source-link sub-scorer for created Synthesis Pages

data/evals/recordings/
├── query-synthesis-created/             # NEW
├── query-synthesis-declined-routine/    # NEW
└── query-synthesis-decline-edit-request/# NEW

data/write-locks/                        # NEW runtime location (ADR-009), git-ignored
```

**Structure Decision**: Existing `backend/` + `frontend/` split, unchanged; no
frontend changes. One new namespace
(`Grimoire.AgentRuntime.Guardrails.Coordination`) inside the existing shared runtime
assembly — no new assembly, no new process, no new port (Constitution I's
no-extra-assemblies default). Ingest gains the `--write-locks-dir` argument purely to
share the same protection; its own write behavior and policy are otherwise untouched.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

No violations — not applicable.

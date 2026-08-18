# Implementation Plan: Agent-Owned, Newest-First Wiki Activity Log

**Branch**: `025-agent-owned-log` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/025-agent-owned-log/spec.md`

## Summary

The wiki's activity log becomes what the operator expects it to be: agent-owned wiki content,
newest entry first, one complete entry per action that actually changed the wiki.

Three changes deliver it. **First**, the `log.md` half of ADR-017's guarded-write format
check is inverted from append-only to prepend-only — the current content must be an
unchanged *suffix* of the proposed content rather than an unchanged prefix — with the denial
reason renamed accordingly. **Second**, every harness writer of the file is deleted: the
shared `WikiLogAppender` backstop and its four call sites across the Ingest and Query agent
processes, and the Hub's `RestartReconciler.AppendReconciliationLogAsync`; the structural
allow-list that permitted the backstop's filesystem write is tightened in the same change,
so the deletion cannot regress. **Third**, the one diagnostic the backstop carried that
nothing else does — an agent changed the wiki and did not log it — is preserved as a
harness-side observability signal derived from the run's own allowed-write record, which
writes nothing to the wiki.

What each agent chooses to log, and what the entry says, stays where Constitution Principle V
puts it: two versioned instruction files, verified at evaluation thresholds rather than by
deterministic tests.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); the frontend is untouched by this feature.

**Primary Dependencies**: `Grimoire.AgentRuntime` (guarded tool boundary, write
coordination, wiki-log telemetry), `Grimoire.IngestAgent`, `Grimoire.QueryAgent`,
`Grimoire.Hub` (restart reconciliation), `Grimoire.EvalRunner` (agent-behavior evaluation),
`Mono.Cecil` (existing IL-level architecture tests), OpenTelemetry (existing).

**Storage**: Markdown files at the content root — `log.md` and `index.md`. No schema, no
parsed representation, no database change.

**Testing**: xUnit. `Grimoire.ArchTests` (Boundary Rule, IL scan), `Grimoire.IntegrationTests`
(classicist state-based tests against the real guard, the real reconciler, and real temp
filesystems), `Grimoire.EvalRunner` scenarios under recorded replay (ADR-012) for
agent-judgment criteria.

**Target Platform**: Local/self-hosted Hub process plus spawned agent child processes
(macOS/Linux).

**Project Type**: Backend service + spawned CLI agent processes (existing layout; no new
project).

**Performance Goals**: Unchanged. The prepend check is the same class of operation as the
append check it replaces — one ordinal string comparison and one substring over content
already resident in memory, no additional I/O. Deleting the backstop removes one file read
and one file append per run.

**Constraints**: No migration of existing logs (FR-014). No structured representation of the
log (Assumptions). The entry format itself — heading pattern, required paragraph, wikilink
convention — is out of scope and must not drift (FR-008). Hermetic harness tests must not
require live LLM calls or API keys.

**Scale/Scope**: One guard method inverted; one shared component deleted and replaced by a
write-free observer; four agent call sites and one Hub call site removed; three arch-test
allow-lists tightened; two instruction files edited; three new eval scenarios. No new
assembly, namespace, tool, or infrastructure.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.* — **PASS** (both
evaluations; see the post-design re-check at the end of this section).

| Principle | Gate | Assessment |
| --- | --- | --- |
| **I — Domain architecture & hexagonal boundaries** | No new external system; no new port. | **Pass.** The feature touches only the guarded-write layer (`Grimoire.AgentRuntime.Guardrails.Coordination`), a telemetry-only component in `Grimoire.AgentRuntime.WikiLog`, and Hub operational recovery. The filesystem is a persistence/local-filesystem adapter, explicitly exempt from the port requirement; adapter containment is *tightened*, not loosened (R3). No infrastructure package moves. |
| **II — Pragmatic testing strategy** | Real infrastructure; classicist, state-based; no mocking framework; harness vs. agent split; test what we own. | **Pass.** Every deterministic test drives the real `SharedFileWriteGuard`, the real `RestartReconciler`, and real files in per-test temp directories. No test double is introduced; the existing recorded-replay model adapter (an existing port fake, ADR-012) serves the eval tier. Assertions are state-based throughout — denial reason returned, bytes on disk, emitted signal — never "method X was called". Every criterion asserts a Grimoire-owned contract: the denial reasons, the ordering rule, the reconciler's non-write, and our own event/metric names are all decided by our source. |
| **II — Success-criteria split** | Deterministic guarantees vs. agent-judgment thresholds. | **Pass.** The spec already splits them correctly: SC-001/002/003/004/008/009 are 100% harness guarantees (ordering rule, zero harness authorship, locatability, allow-path, existing operational coverage, the new signal); SC-005/006/007 are ≥90% agent-judgment thresholds (one entry per change, no entry when nothing changed, no day-grouping). The Test Strategy below maps each to its tier without moving any criterion across the line. |
| **III — ADR-driven & test-enforced** | All ADRs read; new boundary → new ADR, Accepted before `/speckit-tasks`; Boundary Rules vs. Feature-Scoped Invariants tagged. | **Pass.** All 27 existing ADRs were read; ADR-028 is drafted and Accepted with bidirectional links to ADR-017 and an index row. It classifies its rules explicitly: one Boundary Rule (BR-1) and two Feature-Scoped Invariants (FSI-1, FSI-2). **Gate satisfied: ADR-028 reached Accepted before `/speckit-tasks`.** |
| **IV — Behavioral & observable engineering** | Mandatory `## Observability`; every log/trace row gets impl + deterministic test + CI tasks; contract tests exercise production wiring. | **Pass.** The Observability section below enumerates one new metric, one new log event, one new span, and the three retired signals. The contract test for the new signal attaches to the same composition root the agent process uses, per the Feature-003 lesson recorded in the constitution. No new infrastructure is introduced. |
| **V — Agentic core & deterministic harness** | Wiki-content judgment lives in instruction files; harness owns mechanics; guarded-write boundary structurally enforced; no deterministic test asserts instruction wording. | **Pass, and this is the feature's point.** The change *restores* a boundary the backstop crossed: harness-generated prose is removed from wiki content entirely. What remains harness-side is purely mechanical — a byte-comparison of proposed against current content, and set arithmetic over the run's own allowed writes. The changes-only criterion and the entry's text move into the two writing agents' `system-prompt.md` files and are verified only at evaluation thresholds. No task in this feature may assert instruction-file wording. |

**Post-Phase-1 re-check**: **PASS, unchanged.** The Phase 1 design added no port, no
adapter, no infrastructure, and no new namespace; it moved no success criterion between
tiers; and the one new component (`WikiLogCoverageObserver`) performs no I/O, which is what
allows Principle I's containment rule to be tightened rather than weakened. The
Complexity Tracking table below is empty because there is nothing to justify.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.* — all
ADR-001 … ADR-027 read via `docs/adr/index.md` and the individual files.

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | The guarded tool boundary is the single chokepoint for agent writes. The new ordering rule composes into that existing chokepoint; no second boundary and no new tool. Denials continue to be recorded and returned as `is_error` results with the run continuing. |
| ADR-007 | Agent Instruction Surface | The newest-first, one-entry-per-action, and changes-only rules are stated in the two writing agents' `system-prompt.md` files — the versioned surface actually loaded into agent context — not in backend code. No `default-user-prompt.md` change. |
| ADR-012 | Standalone Eval Runner and Recorded-Replay | SC-005/006/007 are verified as eval scenarios under recorded replay. New scenarios must respect `ScenarioDefinition.StableSerialization`'s fingerprint discipline; existing recordings for unrelated scenarios must not be invalidated. |
| ADR-013 | Unified Agent Platform Packaging and Naming | The replacement observer stays a shared `Grimoire.AgentRuntime` component that receives the caller's frozen `ActivitySource`/`Meter` rather than owning a static telemetry identity — the same reason `WikiLogEvents`/`WikiLogMetrics` take parameters today. |
| ADR-015 | Query Write Scope and Cross-Process Wiki Write Coordination | The cross-process lock and the read-then-write compare-and-swap are unchanged and still evaluated **before** the ordering check. `write_conflict_stale_read` continues to be the concurrency answer under the new rule (FR-011). |
| ADR-016 | Lint Write Scope — Frontmatter-Only Enforcement | Lint's write scope is untouched: it never writes the activity log. Its instruction file is explicitly out of scope (FR-013). The frontmatter-only check remains an independent, earlier step in the guard. |
| ADR-017 | Structural Format Enforcement for `log.md` and `index.md` | **Amended by ADR-028 for the `log.md` half only.** The heading pattern, the following-paragraph requirement, the check's position in the evaluation order, the `guardrails.format_validate` span, and the whole `index.md` catalog half are binding and unchanged. Only the ordering direction and the one denial reason change. |
| ADR-022 | Minimal Directory Configuration Surface | Instruction files are edited at their versioned source under `backend/src/Grimoire.*Agent/Instructions/` and build-distributed; the gitignored `.grimoire/agents/` copies are never edited directly. |
| ADR-025 | Ingest Task Lifecycle Re-Entry | Restart reconciliation keeps recording the failure in the task artifact and the status history. Only its wiki write is removed; its lifecycle semantics are untouched. |
| **ADR-028** | **Agent-Owned Activity Log — Prepend-Only Ordering and Removal of Harness Authorship** | **New (Accepted).** Defines the prepend-only rule and its renamed denial reason, the deletion of all harness authorship plus the tightened allow-list that enforces it, and the replacement operational signal. Classifies BR-1 as a Boundary Rule and FSI-1/FSI-2 as Feature-Scoped Invariants. |

**New ADR required?**: **Yes — drafted and Accepted.**
[`docs/adr/ADR-028-agent-owned-activity-log-prepend-ordering.md`](../../docs/adr/ADR-028-agent-owned-activity-log-prepend-ordering.md),
status `accepted` (author sign-off, 2026-08-17). ADR-017's header carries the reciprocal
`Amended by ADR-028` link and `docs/adr/index.md` carries the row, so no one-sided link
ships. The Constitution Principle III gate on `/speckit-tasks` is therefore satisfied.

The ADR was drafted as ADR-027 and renumbered to ADR-028 before acceptance: ADR-027 was
already taken by the Accepted GitVersion/GitHub-Flow decision merged in parallel. ADR
numbers are permanent, so the later draft moved.

**Hexagonal gate**: no new external system, no new port, no new adapter namespace, no
infrastructure package moved. The only containment change is a *tightening* of the existing
filesystem-write allow-list (BR-1).

## Agentic Boundary (Constitution Principle V)

| Capability | Side | Where it lives |
| --- | --- | --- |
| Whether a run changed wiki content in a way worth logging | Agentic core | `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md`, `backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md` |
| What a log entry says — the summary phrase, the prose paragraph, which pages it names | Agentic core | the same two `system-prompt.md` files |
| Deciding not to merge a new action into an existing date section | Agentic core | the same two `system-prompt.md` files |
| Composing the full proposed `log.md` content with the new entry on top | Agentic core (the agent composes it; the harness only validates it) | agent output via the existing `write_file` tool |
| Prepend-only structural validation — current content must be an unchanged suffix | Harness | `Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.ValidateLogEntryFormat` |
| Heading-shape and following-paragraph validation | Harness (unchanged, ADR-017) | same method |
| Recording the denial with a reason and continuing the run | Harness (unchanged, ADR-006) | `GuardedToolExecutor`, `DeniedActionRecord` |
| Stale-read conflict detection under concurrent prepends | Harness (unchanged, ADR-015) | `SharedFileWriteGuard.EvaluateWriteAsync` |
| Tracking which wiki-content writes this run was allowed | Harness | `GuardedToolExecutor` (set arithmetic over its own touched paths) |
| Emitting "the wiki changed but nothing was logged" | Harness | `Grimoire.AgentRuntime/WikiLog/WikiLogCoverageObserver` (+ `WikiLogEvents`, `WikiLogMetrics`) |
| Recording a crash-reconciled task's failure | Harness (unchanged, ADR-025) | `Grimoire.Hub/OperationalState/RestartReconciler` — task artifact and status history only, no wiki write |
| **Removed:** authoring a fallback log entry's text | — | `WikiLogAppender` (deleted), `RestartReconciler.AppendReconciliationLogAsync` (deleted) |

The "boundary smell test" is worth stating for this feature specifically: after this change,
altering what the agents log — which actions qualify, how the entry reads — must require
**only** an instruction-file edit. If a future request to change logging behaviour needs a
backend change, that is the signal that something drifted back across the boundary.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method
before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| **SC-001** — 100% of writes that modify/reorder/remove existing content are denied, each denial recorded with a reason | Deterministic guarantee | Hermetic integration test (`Grimoire.IntegrationTests`, extending `LogEntryFormatEnforcementTests`) | None. Real `SharedFileWriteGuard`, real files in a per-test temp dir, real lock directory. No mocking framework. | Existing-log content plus proposed variants: reordered, edited-in-place, entry-removed, prefix-appended (the old shape, now denied) | State-based: assert `decision.DenialReason == "log_entry_not_prepended"`, `IsAllowed == false`, and the file's bytes are unchanged. Product-owned: the reason string and the rule are ours. |
| **SC-002** — 100% of runs (success, failure, no-write) produce zero harness-authored log content | Deterministic guarantee | (a) Architecture test — Boundary Rule BR-1, Red/Green probed; (b) classicist integration tests for FSI-2 | (a) `Mono.Cecil` IL scan of the three agent assemblies + `Grimoire.AgentRuntime`; (b) real `RestartReconciler`, real temp content root, real operational-state store | (b) a content root whose `log.md` has known bytes and a task in `running` state at startup | (a) removes `Grimoire.AgentRuntime.WikiLog` from the allow-list — the probe must be re-run because the tightened rule differs from the one probed in feature 014. (b) asserts `log.md` bytes unchanged while the task artifact and status history *are* updated. Plus per-agent run-level tests: a failed Ingest run and a no-write Query turn leave the file unchanged. |
| **SC-003** — 100% of entries remain locatable by `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` | Deterministic guarantee (carried from 014) | Hermetic integration test | None; real guard, real temp files | A log built by two successive allowed prepends, the second sharing the first's exact heading | Asserts both headings match the pattern and both entries are independently present — the duplicate-heading case FR-009 names. |
| **SC-004** — 100% of conforming prepends are allowed, including the first write into a missing or empty file | Deterministic guarantee | Hermetic integration test | None; real guard, real temp files | (i) no file; (ii) zero-byte file; (iii) file with existing entries | Asserts allow, then that the committed file has the new entry first and the prior bytes as an exact suffix. |
| **SC-005** — ≥90% of sampled runs that changed wiki content write exactly one entry, at the top, accurately describing the change | Agent-judgment threshold | Evaluation with threshold (`Grimoire.EvalRunner`) | Recorded-replay model adapter (existing port fake, ADR-012). No live LLM call at replay. | New scenario `log-newest-first-placement` (Ingest, threshold 0.90) over `empty-topic` + a pre-seeded `log.md` | Placement/cardinality scored deterministically over the resulting file; the "accurately describes" half is already covered by the existing `log-paragraph-specificity` judge scenario, which needs no change. |
| **SC-006** — ≥90% of sampled runs that changed nothing write no entry | Agent-judgment threshold | Evaluation with threshold | Recorded-replay model adapter | New scenario `log-changes-only` (Query, threshold 0.90): routine lookup turns that should write nothing | Scorer asserts `log.md` is byte-for-byte unchanged after the sample. |
| **SC-007** — ≥90% of sampled same-day actions produce a separate complete entry | Agent-judgment threshold | Evaluation with threshold | Recorded-replay model adapter | New scenario `log-no-day-grouping` (Ingest, threshold 0.90) over a new `log-same-day-entry` fixture whose seeded entry is dated to the capture run's date (research.md R5) | Scorer asserts the `## [` heading count grew by exactly one and the pre-existing entry's section is byte-unchanged. The fixture README must record the re-seed-on-re-record caveat. |
| **SC-008** — 100% of no-change and failed runs remain fully accounted for in operational signals and task records | Deterministic guarantee | Hermetic integration tests (confirmation, per FR-012 — no new signal) | None; real task-artifact store, real operational-state repository, real conversation-record store | A failed Ingest run and a completed no-write Query turn | Asserts outcome, stage/status, and correlation reference are all discoverable **without reading `log.md`** — the explicit safety net for deleting the backstop. |
| **SC-009** — 100% of runs with non-zero allowed wiki writes and no log entry emit the FR-012a signal, and it never writes to the wiki | Deterministic guarantee | Observability contract test through the production composition root | None; real `GuardedToolExecutor` and real telemetry registration — **not** a hand-attached listener on the observer | A run that writes a page but not `log.md`; a control run that writes both | Asserts event name/level/fields, metric increment with its `type` label, span name and parent linkage, **and** that `log.md` is absent/unchanged. Negative control: the covered run emits no `wiki.log.change_not_logged`. |

**Cross-cutting test rules for this feature.** No mocking framework may be referenced (none
is today). No test may assert the wording or substance of any `system-prompt.md` — only that
instruction files load byte-exact with recorded hashes, which existing tests already cover.
Every renamed denial reason must be updated in its existing tests
(`LogEntryFormatEnforcementTests`, `QueryWriteConflictRejectionAdr017MetricsTests`) rather
than duplicated, and existing tests asserting backstop behaviour
(`WikiLogAppenderTests`, `WikiLogAppenderMetricsTests`, the backstop cases in
`IngestObservabilityTraceTests`) are **rewritten or deleted with the component**, not left
asserting a deleted contract.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `wiki.log.unlogged_change_total` | Counter | A run whose allowed wiki-content writes were non-zero ended without the activity log among its writes (FR-012a). Never accompanied by a wiki write. | `type` (`ingest` \| `query`) |
| ~~`wiki.log.backstop_appended_total`~~ | **Retired** | Removed with `WikiLogAppender`. `WikiLogAppenderMetricsTests` is deleted with it. | — |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `wiki.log.change_not_logged` | WARN | At run end in either writing agent process, when the run's allowed wiki-content writes are non-zero and the canonical activity-log path is not among the run's touched paths. | `type`, `task_id_or_run_id`, `wiki_content_writes` |
| ~~`wiki.log.backstop_appended`~~ | **Retired** | Removed with `WikiLogAppender`. | — |

**Derivation rule (MANDATORY)**: `wiki.log.change_not_logged` maps to all three task
categories in `tasks.md`: (1) implementation emitting the stable event name with all three
mandatory fields from `WikiLogEvents`; (2) a deterministic integration test validating the
event name, `Warning` level, and every mandatory field, obtained through the production
telemetry registration rather than a test-only listener; (3) a CI task confirming that test
runs in the standard PR pipeline tier. The retired event additionally requires a task
removing its emitter, its tests, and its `WikiLogEvents` declaration together.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `wiki_log.coverage_check` | The agent process's ambient run activity — `ingest_agent.finalize_artifact` in Ingest; root in Query, which has no run-level span at completion. Correlation is carried by `task_id_or_run_id` in both. | `type`, `task_id_or_run_id`, `wiki_content_writes`, `outcome` (`logged` \| `not_logged` \| `no_change`) |
| `wiki.log.change_not_logged` | `wiki_log.coverage_check` (always — the log-event span nests inside the check span, the same idiom `WikiLogEvents.StartLogEventSpan` uses today) | `signal_type=log`, `event_name`, `level=Warning`, `type`, `task_id_or_run_id`, `wiki_content_writes` |
| `guardrails.format_validate` | Unchanged (ADR-017) | Unchanged: `path`, `target=log`, `outcome`; on denial `reason` now carries `log_entry_not_prepended` instead of `log_entry_not_appended` |
| ~~`wiki_log.backstop_append`~~ | **Retired** | Removed with `WikiLogAppender`. |

**Derivation rule (MANDATORY)**: `wiki_log.coverage_check` and its child map to all three
task categories: (1) implementation creating the span with the declared attributes and
starting the event span inside it; (2) a deterministic integration test validating both span
names, the parent/child linkage, and the shared `task_id_or_run_id` correlation attribute,
collected through the production telemetry pipeline; (3) a CI task ensuring those trace tests
run in the standard PR pipeline. `guardrails.format_validate`'s existing trace test is
updated for the new `reason` value rather than duplicated.

**Correlation**: the new event and metric are emitted inside the `wiki_log.coverage_check`
span's context and carry `task_id_or_run_id`, so they join the run's existing trace the same
way the retired backstop signals did.

## Project Structure

### Documentation (this feature)

```text
specs/025-agent-owned-log/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/
│   └── activity-log-write-contract.md   # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command — NOT created here)
```

### Source Code (repository root)

```text
backend/src/
├── Grimoire.AgentRuntime/
│   ├── Guardrails/
│   │   ├── Coordination/SharedFileWriteGuard.cs   # MODIFY: append-only → prepend-only
│   │   ├── GuardedToolExecutor.cs                 # MODIFY: renamed reason; expose
│   │   │                                          #   wiki-content-write / log-written facts
│   │   └── DeniedActionRecord.cs                  # MODIFY: doc comment, renamed reason
│   └── WikiLog/
│       ├── WikiLogAppender.cs                     # DELETE
│       ├── WikiLogCoverageObserver.cs             # NEW: write-free run-end observer
│       ├── WikiLogEvents.cs                       # MODIFY: retire backstop event,
│       │                                          #   add wiki.log.change_not_logged
│       └── WikiLogMetrics.cs                      # MODIFY: retire/replace counter
├── Grimoire.IngestAgent/
│   ├── Program.cs                                 # MODIFY: drop both EnsureLogEntryAsync
│   │                                              #   call sites; invoke the observer
│   └── Instructions/system-prompt.md              # MODIFY: newest-first, one entry per
│                                                  #   action, changes-only (FR-013)
├── Grimoire.QueryAgent/
│   ├── Program.cs                                 # MODIFY: same
│   └── Instructions/system-prompt.md              # MODIFY: same
├── Grimoire.LintAgent/Instructions/system-prompt.md   # UNCHANGED (FR-013, explicit)
├── Grimoire.Hub/OperationalState/RestartReconciler.cs # MODIFY: delete the wiki write
└── Grimoire.EvalRunner/
    ├── Scenarios/{Ingest,Query}ScenarioDefinitions.cs # MODIFY: 3 new scenarios
    └── Scoring/DeterministicScorers.cs                # MODIFY: 3 new scorer cases

backend/tests/
├── Grimoire.ArchTests/
│   ├── IngestAgentGuardedWriteBoundaryRuleTests.cs    # MODIFY: drop WikiLog allow-list
│   ├── QueryAgentGuardedWriteBoundaryRuleTests.cs     # MODIFY: same  (BR-1, Red/Green)
│   └── LintAgentGuardedWriteBoundaryRuleTests.cs      # MODIFY: same
├── Grimoire.IntegrationTests/
│   ├── LogEntryFormatEnforcementTests.cs              # MODIFY: prepend rule (FSI-1)
│   ├── QueryWriteConflictRejectionAdr017MetricsTests.cs # MODIFY: renamed reason
│   ├── WikiLogAppenderTests.cs                        # DELETE with the component
│   ├── WikiLogAppenderMetricsTests.cs                 # DELETE with the component
│   ├── IngestObservabilityTraceTests.cs               # MODIFY: retire backstop-span cases
│   ├── RestartReconcilerActivityLogTests.cs           # NEW: FSI-2, log bytes unchanged
│   └── WikiLogCoverageObservabilityTests.cs           # NEW: SC-009 contract test
└── Grimoire.AgentEvals/Fixtures/log-same-day-entry/   # NEW fixture (+ README caveat)
```

**Structure Decision**: The existing backend layout is used unchanged. No new project,
assembly, namespace, port, adapter, or infrastructure component is introduced — the feature
is an inversion inside one guard method, a deletion of one shared component and its five
call sites, one new write-free telemetry component in the namespace the deleted component
vacated, two instruction-file edits, and three eval scenarios. The `Grimoire.AgentRuntime.WikiLog`
namespace is deliberately retained (rather than deleted alongside `WikiLogAppender`) so the
tightened BR-1 allow-list has a concrete subject: that namespace must now contain zero
filesystem-write calls, which is the enforcement of "no harness component authors the log".

### Delivery shape

**Single pull request.** `tasks.md` will have more than two phase groups beyond Phase 0, so
the CLAUDE.md default points at a stack — and this feature is the exception, said out loud
rather than implied. The prepend inversion and the backstop removal are mutually
load-bearing: the guard's tests assert denial reasons the removal changes, removing the
backstop while the guard still enforces append-only leaves the file in a state no writer can
extend correctly, and shipping the inversion alone leaves BR-1's tightened allow-list red.
The total change is one method inversion, one deletion, one new observability signal, two
instruction files, and three eval scenarios — small enough that a stack would be ceremony.
`tasks.md`'s Implementation Strategy section MUST state "single PR" and this reason, not
describe a stack nobody builds.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations. Both Constitution Check evaluations pass without exception, so this table is
intentionally empty.

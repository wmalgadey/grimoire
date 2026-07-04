# Implementation Plan: Agentic Ingest Core

**Branch**: `002-agentic-ingest-core` | **Date**: 2026-07-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-agentic-ingest-core/spec.md`

## Summary

Replace the deterministic ingest pipeline of 001-ingest-minimal with an agent-driven
execution core. The Ingest agent (still the standalone .NET console app of ADR-002) runs
a **manual tool-use loop** against the Anthropic Messages API: the instruction set
(`agents/ingest/CLAUDE.md` + `skills/*/SKILL.md`) is loaded verbatim into the system
prompt, the agent explores the wiki through guarded read tools and applies every change
through guarded write tools, and a versioned deny-by-default safety policy
(`agents/ingest/policy.json`) is enforced by harness code at the moment of each tool
invocation. Denied actions are recorded and the run continues; a write journal restores
the wiki on failure; the task artifact records pages touched, denials, and the governing
instruction/policy identities. All wiki-maintenance judgment (update-vs-create,
supersession, tagging, catalog/log content) lives in the instruction files — none in
backend code. Full technical rationale: [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (ADR-001); no frontend work in this feature

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR (Hub, unchanged),
`Anthropic` C# SDK (Messages API, manual tool-use loop — research R1), System.Text.Json
(policy parsing), OpenTelemetry .NET SDK (ADR-005). No new dependencies introduced.

**Storage**: Git-tracked markdown files for domain state (wiki pages, task artifacts,
`index.md`, `log.md`) + instruction set and policy file as git-tracked plain files;
embedded SQLite for Hub operational state (ADR-003, unchanged)

**Testing**: xUnit; hermetic harness integration tests via scripted `FakeModelClient`
(research R2 — no live LLM calls, no API keys); Testcontainers-based Hub tests retained;
ArchUnitNET structural boundary tests; opt-in agent-behavior evaluation suite
(`Grimoire.AgentEvals`, `GRIMOIRE_EVAL=1` + real key) for SC-006…SC-010

**Target Platform**: Developer machine (macOS/Linux) — Hub process + spawned agent child
process (ADR-002); local OTel via Aspire Dashboard, CI via in-memory exporter (ADR-005)

**Project Type**: Backend service (Hub) + standalone console agent (Ingest) — existing
structure, execution core replaced

**Performance Goals**: One ingest run at a time (ADR-002 scope); run duration is
LLM-bound (minutes-scale acceptable); turn cap and token cap bound the agent loop
(defaults: 50 turns; see research R1)

**Constraints**: Harness contracts MUST be testable without live LLM calls (Principle
II); guardrails enforced at tool-invocation time, deny-by-default (Principle V); failed
runs leave the wiki untouched (FR-013); no new infrastructure (Principle IV)

**Scale/Scope**: Single trusted user, one source per run; wiki up to a few hundred pages
(agent grounds decisions via catalog + selective page reads, research R6)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance in this plan | Status |
| --- | --- | --- |
| I. Domain Architecture & Strategic DDD | Ubiquitous Language reused from spec (Source, Instruction Set, Safety Policy, Task Artifact, Denied Action Record, Wiki Catalog, Ingest Log). New pure domain logic (`SafetyPolicy`, `PolicyDecision`) lands in dependency-free `Grimoire.Domain.Guardrails`; deterministic wiki judgment (`UpdateOrCreateDecisionService`) is **removed** from the domain, not relocated. Existing arch test extends to the new namespace. | PASS |
| II. Pragmatic Testing Strategy | Harness contracts (dispatch, guardrail enforcement, rollback, artifact lifecycle, log backstop) tested hermetically via `FakeModelClient` — no API keys (research R2, R10). Agent judgment (SC-006…SC-010) tested by evaluation suite against real runs, in the final phase. Unit tests only for `SafetyPolicy` evaluation logic (real invariants). Spec's success criteria are already split deterministic/eval — no 100% guarantee attached to agent judgment. | PASS |
| III. ADR-Driven & Test-Enforced Architecture | All ADRs in `docs/adr/` read (ADR-001…005); constraints listed below. New structural boundary (guarded tool loop + policy) → **ADR-006 drafted** as part of this plan; must be Accepted before `/speckit-tasks`. Phase 0 of tasks.md will carry the new structural rule + Red/Green probe; observability and eval tests go in the final phase. | PASS |
| IV. Behavioral & Observable Engineering | `## Observability` below enumerates metrics/logs/spans (extending 001's set with guardrail/loop signals). No new infrastructure: same SQLite, same OTel backend, same Anthropic API; policy/instruction files are git-tracked plain files (ADR-003 pattern). | PASS |
| V. Agentic Core & Deterministic Harness | The feature exists to satisfy this principle. Judgment → instruction files loaded verbatim into the agent's system prompt (FR-002); harness owns loop, policy enforcement at tool boundary, journal/rollback, artifact lifecycle, log backstop. Deterministic content writers and decision services deleted (research R11). Structural test: filesystem writes in `Grimoire.IngestAgent` reachable only from guarded-tool/harness-record namespaces, with Red/Green probe. | PASS |

No violations. `## Complexity Tracking` remains empty.

**Post-Phase-1 re-check**: Design artifacts (data-model, contracts) introduce no new
projects beyond `Grimoire.AgentEvals` (a test project mandated by Principle II's eval
requirement), no tactical DDD outside the domain core, and no semantic wiki tools that
would move judgment into the harness. Still PASS.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-001 | Backend and Frontend Technology Stack | Agent core stays C#/.NET 10; the tool-use loop uses the existing Anthropic C# SDK — no second language runtime for the agent (research R1). No frontend surface added. |
| ADR-002 | Ingest Agent Execution Model | The agent remains a standalone console app spawned per submission with CLI args + scoped env; this feature replaces its *internals* only. New CLI args (`--instructions-dir`, `--policy-path`) extend, not break, the file-based contract ([contracts/ingest-agent-cli.md](./contracts/ingest-agent-cli.md)). |
| ADR-003 | Domain vs. Operational State Persistence | Wiki pages, task artifacts, catalog, log stay git-tracked markdown; instruction set + policy file join the git-tracked plain-file category. Hub restart reconciliation via SQLite is retained unchanged (FR-015). |
| ADR-004 | Credential Scoping | The Anthropic key continues to be injected only into the Ingest child's environment at spawn. `GRIMOIRE_INGEST_MODEL` uses the same injection point (research R3). No new credentials. |
| ADR-005 | Observability Backend | All new signals below use the OTel .NET SDK → OTLP/Aspire locally, in-memory exporter assertions in CI. No collector infrastructure added. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | Fixes the manual in-process tool-use loop, the three-file-tool guarded surface, the deny-by-default policy file, and the write-journal rollback as the structural boundary for all agentic execution. Drafted at `docs/adr/ADR-006-agent-tool-loop-guarded-boundary.md`. |

**New ADR required?**: **Yes — ADR-006 drafted and since Accepted**
(`docs/adr/ADR-006-agent-tool-loop-guarded-boundary.md`, status: accepted). The
workflow-step-4 gate is satisfied.

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
| --- | --- | --- |
| Update-vs-create-vs-supersede decision | Agentic core | `agents/ingest/skills/wiki-maintenance/SKILL.md` |
| Page types, frontmatter/metadata, tagging, confidence rating | Agentic core | `agents/ingest/CLAUDE.md` + skill files |
| Catalog (`index.md`) upkeep content | Agentic core | `agents/ingest/skills/wiki-maintenance/SKILL.md` |
| Ingest-log entry content (success path) | Agentic core | instruction files (harness appends minimal backstop entry only if absent/failed — research R8) |
| Run narrative ("what was done and why") | Agentic core | agent's final message, copied verbatim into task artifact |
| Source-is-data framing (prompt-injection preamble) | Agentic core | instruction files (`<source>` delimiting rules, research R9) |
| Tool-use loop, turn/token caps, conversation assembly | Harness | `Grimoire.IngestAgent/AgentCore/` |
| Policy evaluation (deny-by-default, canonicalized paths) | Harness | `Grimoire.Domain/Guardrails/SafetyPolicy.cs` |
| Guarded tool execution, denial recording | Harness | `Grimoire.IngestAgent/Guardrails/GuardedToolExecutor.cs` |
| Write journal + rollback on failure | Harness | `Grimoire.IngestAgent/Guardrails/WriteJournal.cs` |
| Instruction/policy loading, hashing, fail-before-write | Harness | `Grimoire.IngestAgent/AgentCore/InstructionSetLoader.cs`, `PolicyLoader` |
| Task-artifact lifecycle + structured fields | Harness | `Grimoire.IngestAgent/TaskArtifact/` (extended) |
| Log-entry existence backstop | Harness | `Grimoire.IngestAgent/IngestLog/` (retained as backstop) |
| Dispatch, credentials, restart reconciliation | Harness | `Grimoire.Hub/*` (unchanged from 001) |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

001's signals (`wiki.ingest.operations_total`, `wiki.ingest.duration_seconds`,
`wiki.ingest.tasks_reconciled_total`, `ingest.task.*`, `hub.ingest.*`) are retained.
`wiki.ingest.pages_touched_total` gains the `superseded` action. New signals for the
agentic core:

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `wiki.ingest.pages_touched_total` | Counter | Wiki pages written by the agent (existing metric, extended) | `action=created\|updated\|superseded` |
| `wiki.ingest.agent_turns_total` | Counter | Model turns consumed per run | `outcome=completed\|failed` |
| `wiki.ingest.tool_calls_total` | Counter | Guarded tool invocations | `tool=list_files\|read_file\|write_file`, `decision=allowed\|denied` |
| `wiki.ingest.actions_denied_total` | Counter | Policy denials (FR-008, SC-002) | `tool`, `reason=out_of_scope\|traversal\|no_rule` |
| `wiki.ingest.runs_rolled_back_total` | Counter | Failed runs whose journal rollback executed (FR-013, SC-004) | `restored_ok=true\|false` |
| `wiki.ingest.instruction_load_failures_total` | Counter | Runs aborted because instructions/policy could not load (FR-003) | `artifact=instructions\|policy` |
| `wiki.ingest.model_tokens_total` | Counter | Input/output tokens reported by the API | `direction=input\|output` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `ingest.instructions.loaded` | INFO | Instruction set + policy loaded and hashed before first model turn (FR-002, FR-012) | `task_id`, `instruction_files` (path+sha256 list), `policy_version`, `policy_sha256` |
| `ingest.instructions.load_failed` | ERROR | Instructions or policy missing/unreadable/empty — run aborts pre-write (FR-003, SC-003) | `task_id`, `artifact`, `path`, `reason` |
| `ingest.tool.allowed` | INFO | Guarded tool call permitted and executed | `task_id`, `tool`, `target`, `turn` |
| `ingest.tool.denied` | WARN | Guarded tool call denied by policy (FR-008) | `task_id`, `tool`, `target`, `reason`, `turn` |
| `ingest.run.rolled_back` | WARN | Write journal restored the wiki after failure (FR-013) | `task_id`, `paths_restored`, `restored_ok` |
| `ingest.log.backstop_appended` | WARN | Harness appended the log entry because the agent did not (research R8) | `task_id`, `outcome` |
| `ingest.agent.completed` | INFO | Agent loop reached `end_turn` within caps | `task_id`, `turns`, `pages_created`, `pages_updated`, `pages_superseded`, `denials` |
| `ingest.agent.cap_exceeded` | ERROR | Turn or token cap breached — run fails, rollback runs | `task_id`, `cap`, `turns` |

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `ingest_agent.run` | `hub.ingest.spawn_agent` (cross-process link via task_id) | `task_id`, `model`, `policy_version` |
| `ingest_agent.load_instructions` | `ingest_agent.run` | `task_id`, `file_count` |
| `ingest_agent.model_turn` | `ingest_agent.run` | `task_id`, `turn`, `stop_reason`, `input_tokens`, `output_tokens` |
| `ingest_agent.tool_call` | `ingest_agent.model_turn` | `task_id`, `tool`, `target`, `decision=allowed\|denied` |
| `ingest_agent.rollback` | `ingest_agent.run` | `task_id`, `paths_restored` |
| `ingest_agent.finalize_artifact` | `ingest_agent.run` | `task_id`, `outcome` |

001 spans `ingest_agent.decide_page_target`, `ingest_agent.write_wiki_page`,
`ingest_agent.update_index`, `ingest_agent.append_log` are **retired** with the
deterministic pipeline; their information now appears as `ingest_agent.tool_call` spans
and the artifact's structured fields.

## Project Structure

### Documentation (this feature)

```text
specs/002-agentic-ingest-core/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── ingest-agent-cli.md
│   ├── guarded-tools.md
│   ├── safety-policy.md
│   └── task-artifact-format.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
agents/
└── ingest/                          # NEW — agentic core (behavior, not code)
    ├── CLAUDE.md                    # agent operating rules (FR-002, FR-010)
    ├── policy.json                  # versioned deny-by-default safety policy (FR-006/007)
    └── skills/
        └── wiki-maintenance/
            └── SKILL.md             # wiki conventions: page types, metadata, tagging,
                                     # supersession, catalog & log upkeep

backend/
├── src/
│   ├── Grimoire.Domain/
│   │   ├── Guardrails/              # NEW — pure policy evaluation (dependency-free)
│   │   │   ├── SafetyPolicy.cs
│   │   │   ├── PolicyRule.cs
│   │   │   └── PolicyDecision.cs
│   │   └── Ingest/                  # UpdateOrCreateDecisionService + PageDecision* DELETED (R11)
│   ├── Grimoire.Hub/                # unchanged except spawn args (--instructions-dir, --policy-path)
│   │   ├── AgentDispatch/
│   │   ├── ContentRoot/
│   │   ├── OperationalState/
│   │   └── Submission/
│   └── Grimoire.IngestAgent/
│       ├── AgentCore/               # NEW — manual tool-use loop
│       │   ├── IModelClient.cs      # seam for hermetic tests (R2)
│       │   ├── AnthropicModelClient.cs
│       │   ├── AgentLoop.cs
│       │   ├── InstructionSetLoader.cs
│       │   └── PolicyLoader.cs
│       ├── Guardrails/              # NEW — guarded tool boundary
│       │   ├── GuardedToolExecutor.cs
│       │   ├── ToolRegistry.cs      # list_files, read_file, write_file
│       │   ├── WriteJournal.cs      # rollback on failure (R7)
│       │   └── DeniedActionRecord.cs
│       ├── Source/                  # retained (SourceReader)
│       ├── TaskArtifact/            # retained, extended (denials, versions, narrative)
│       ├── IngestLog/               # retained as backstop appender (R8)
│       ├── Synthesis/               # DELETED (R11)
│       ├── WikiWrite/               # DELETED (R11)
│       └── WikiIndex/               # DELETED (R11)
└── tests/
    ├── Grimoire.ArchTests/          # + guarded-write boundary rule w/ Red/Green probe
    ├── Grimoire.Domain.UnitTests/   # + SafetyPolicy tests; UpdateOrCreateDecisionTests DELETED
    ├── Grimoire.IntegrationTests/   # + FakeModelClient-driven harness tests (SC-001…005)
    └── Grimoire.AgentEvals/         # NEW — opt-in eval suite (SC-006…010), final phase
```

**Structure Decision**: Existing backend layout is retained; the feature adds two
namespaces inside `Grimoire.IngestAgent` (`AgentCore`, `Guardrails`), one dependency-free
domain namespace (`Grimoire.Domain.Guardrails`), one test project
(`Grimoire.AgentEvals`), and the new repository-root `agents/ingest/` tree that holds
behavior (instructions + policy) outside backend code, per Principle V and ADR-003's
plain-file pattern.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No violations — table intentionally empty.*

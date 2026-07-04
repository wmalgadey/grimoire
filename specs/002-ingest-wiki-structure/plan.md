# Implementation Plan: Ingest Wiki Structure

**Branch**: `002-ingest-wiki-structure` | **Date**: 2026-07-04 | **Spec**: `/specs/002-ingest-wiki-structure/spec.md`

**Input**: Feature specification from `/specs/002-ingest-wiki-structure/spec.md`

## Summary

Extend ingest from single-page synthesis into autonomous, guardrail-enforced wiki structure updates that can create/update source, entity, and concept pages in one run, keep `wiki/index.md` current, and emit a deterministic task artifact capturing created/updated/superseded/denied actions. The implementation keeps ADR-002 child-process execution, enforces policy-driven tool guardrails, applies active ingest instructions from `CLAUDE.md` and `SKILL.md` before writes, and uses hermetic tests that validate local tooling and contracts without live Claude SDK/API interaction.

## Technical Context

**Language/Version**: C# on .NET 10 (backend components); Markdown/YAML contracts and policy files

**Primary Dependencies**: ASP.NET Core/SignalR (Hub), Anthropic SDK (runtime synthesis only), OpenTelemetry .NET SDK, System.Text.Json, YamlDotNet or equivalent policy parser in ingest runtime

**Storage**: Git-tracked markdown files for wiki/task artifacts/index/log; SQLite for Hub operational state

**Testing**: xUnit + integration tests (including Testcontainers where boundary realism is needed) with deterministic/fake synthesis and tool-wrapper tests; no tests may call live Claude APIs or validate Anthropic SDK internals

**Target Platform**: macOS/Linux development, .NET 10 runtime for Hub and ingest agent

**Project Type**: Backend service + child-process CLI agent in monorepo

**Performance Goals**: Complete a single ingest run (one source to multi-page wiki update) within operator-acceptable interactive latency; keep guardrail checks and policy loading sub-100ms per action on warm path

**Constraints**:
- Writes allowed only under `wiki/` and task-artifact output paths for autonomous mode
- Reads limited to approved, versioned allowlist policy entries
- Denied actions must not abort whole run; they must be recorded and processing continues
- No nondeterministic LLM assertions in tests
- No direct SDK/Anthropic integration tests; test project-owned wrappers and contracts only

**Scale/Scope**: Single trusted operator workflow, one ingest at a time currently, multiple wiki page mutations per run

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase-0 Gate Review

- Principle I (DDD boundaries): PASS. Feature extends ingest orchestration and wiki tooling in `Grimoire.IngestAgent`/`Grimoire.Hub` without introducing tactical DDD outside `Grimoire.Domain`.
- Principle II (Pragmatic testing): PASS with explicit constraint. Tests focus on deterministic tool behavior and repository-owned logic; no live Anthropic/LLM interaction.
- Principle III (ADR-driven): PASS with follow-up. ADR-001..005 were reviewed and are accepted; ADR-006 is newly drafted in this plan cycle to govern autonomous guardrails and instruction-loading constraints (must be accepted before `/speckit-tasks`).
- Principle IV (Observable engineering): PASS conditionally. Plan defines mandatory metrics/logs/spans below; implementation must satisfy them before DoD.

### Post-Phase-1 Design Re-Check

- PASS. Design artifacts (research/data-model/contracts/quickstart) preserve ADR constraints, keep architecture unchanged, and include explicit observability and testability requirements.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend and Frontend Technology Stack | Keep implementation in .NET 10 backend stack; any interfaces must remain compatible with future SvelteKit consumption and SignalR-driven task-state updates. |
| ADR-002 | Ingest Agent Execution Model | Ingest remains a standalone child-process CLI with file/CLI contract; autonomous wiki structure behavior must execute inside this process and persist task artifact lifecycle changes. |
| ADR-003 | Domain vs. Operational State Persistence | Wiki pages/index/log and finalized task artifacts stay markdown in git workspace; operational in-flight state remains in SQLite and reconciliation semantics are preserved. |
| ADR-004 | Credential Scoping for the LLM API Key | Secret exposure remains least-privilege via child-process env injection only; tests must not require real keys or external API calls. |
| ADR-005 | Observability Backend (Local and CI) | Instrumentation must use OTel naming and be verifiable locally (Aspire Dashboard) and in CI via in-memory exporter assertions. |
| ADR-006 | Autonomous Ingest Guardrails and Instruction Governance | Autonomous runs must load CLAUDE.md/SKILL.md context before writes, enforce deny-by-default policy-based action authorization, continue on denied actions, and record denials deterministically for audit. |

**New ADR required?**: Yes — drafted `docs/adr/ADR-006-autonomous-ingest-guardrails-and-instruction-governance.md` (status: accepted).

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `ingest.wiki.pages_touched_total` | Counter | Number of wiki pages created or updated in a run | `task_id`, `page_kind=source|entity|concept`, `action=create|update` |
| `ingest.wiki.pages_superseded_total` | Counter | Number of explicit supersession links written | `task_id` |
| `ingest.guardrail.actions_denied_total` | Counter | Count of autonomous actions denied by guardrails | `task_id`, `action_type`, `reason_code` |
| `ingest.guardrail.actions_allowed_total` | Counter | Count of write/read actions allowed by guardrails | `task_id`, `action_type` |
| `ingest.instructions.load_total` | Counter | Number of runs that successfully loaded `CLAUDE.md` and `SKILL.md` context | `task_id`, `status=loaded|missing|invalid` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `ingest.instructions.applied` | INFO | Ingest loads active instructions before wiki writes | `task_id`, `claude_path`, `skill_path`, `instructions_hash` |
| `ingest.guardrail.action_denied` | WARN | Tool action blocked by policy | `task_id`, `action`, `target_path`, `policy_rule`, `reason` |
| `ingest.wiki.structure.completed` | INFO | Wiki structure update completes successfully | `task_id`, `created_count`, `updated_count`, `superseded_count` |
| `ingest.wiki.structure.failed` | ERROR | Run fails after rollback/compensation path | `task_id`, `error_code`, `denied_count`, `partial_rollback` |

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `ingest_agent.process_source` | root | `task_id`, `source_ref`, `source_kind` |
| `ingest_agent.instructions.load` | `ingest_agent.process_source` | `task_id`, `claude_path`, `skill_path`, `status` |
| `ingest_agent.guardrail.evaluate_action` | `ingest_agent.process_source` | `task_id`, `action_type`, `target_path`, `decision=allow|deny` |
| `ingest_agent.plan_wiki_structure` | `ingest_agent.process_source` | `task_id`, `candidate_pages`, `source_page_title` |
| `ingest_agent.apply_wiki_writes` | `ingest_agent.process_source` | `task_id`, `created_count`, `updated_count` |
| `ingest_agent.update_index` | `ingest_agent.process_source` | `task_id`, `index_path`, `entries_touched` |
| `ingest_agent.write_task_artifact` | `ingest_agent.process_source` | `task_id`, `artifact_path`, `status` |

## Project Structure

### Documentation (this feature)

```text
specs/002-ingest-wiki-structure/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── ingest-agent-cli.md
│   ├── task-artifact-format.md
│   └── guardrail-policy-file.md
└── tasks.md
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.Domain/
│   │   └── Ingest/
│   ├── Grimoire.Hub/
│   │   ├── AgentDispatch/
│   │   └── OperationalState/
│   └── Grimoire.IngestAgent/
│       ├── Program.cs
│       ├── Source/
│       ├── Synthesis/
│       ├── WikiWrite/
│       ├── WikiIndex/
│       └── TaskArtifact/
└── tests/
    ├── Grimoire.ArchTests/
    ├── Grimoire.Domain.UnitTests/
    └── Grimoire.IntegrationTests/

wiki/
├── index.md
└── log.md
```

**Structure Decision**: Keep the existing backend monorepo structure. Implement autonomous wiki-structure behavior inside `backend/src/Grimoire.IngestAgent/` with orchestration updates in `backend/src/Grimoire.Hub/AgentDispatch/` only where contract wiring changes are required. Add deterministic validation coverage in `backend/tests/Grimoire.IntegrationTests/` and architecture guardrails in `backend/tests/Grimoire.ArchTests/`.

## Complexity Tracking

No constitution violations or additional complexity exceptions are required for this plan.

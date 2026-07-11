# Implementation Plan: Single Agent System Prompt & Configurable Ingest Submission

**Branch**: `claude/ingest-agent-systemprompt-dclhyu` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-ingest-agent-systemprompt/spec.md`

## Summary

Replace the Ingest agent's misleading two-file instruction set
(`agents/ingest/CLAUDE.md` + `skills/wiki-maintenance/SKILL.md`, today concatenated
verbatim) with a single versioned `agents/ingest/system-prompt.md`; move the hardcoded
per-run steering text into a versioned `agents/ingest/default-user-prompt.md`; extend
the 003 submission surface so users can edit the user prompt per submission and
enable/disable convert steps (currently only `markitdown`), with everything recorded on
the task artifact. Fail-closed loading, SHA-256 traceability, guardrails, and the
ADR-002 child-process contract are preserved. Design details in
[research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10 backend, TypeScript + SvelteKit frontend (ADR-001)

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR, Anthropic Messages API
via existing `IModelClient` seam (ADR-006), MarkItDown execution adapter (003),
OpenTelemetry .NET SDK (ADR-005); frontend: SvelteKit + existing 003 component set

**Storage**: Domain state as git-tracked files (`agents/ingest/*.md` instruction
documents, task artifacts, wiki) per ADR-003; operational state in SQLite (unchanged);
raw source artifacts in 003's raw storage (original + normalized)

**Testing**: xUnit hermetic integration tests (`Grimoire.IntegrationTests`, fake model
client / fake dispatcher), architecture tests (`Grimoire.ArchTests`), frontend Vitest,
agent evaluations (`Grimoire.AgentEvals`) for judgment criteria

**Target Platform**: Linux dev container / developer workstation; browser UI; Hub +
Ingest agent on same repository root

**Project Type**: Web application (backend service + realtime frontend) + agent CLI

**Performance Goals**: unchanged from 003 (submission ack ≤ 2 s p95, board propagation
≤ 2 s p95); defaults endpoint responds ≤ 500 ms locally

**Constraints**: single-concurrent-ingest-run constraint unchanged; user prompt
≤ 8,000 chars; binary formats cannot skip conversion; no new infrastructure

**Scale/Scope**: single trusted user; one source per submission; one registered convert
step (`markitdown`), model extensible by design

**Dependency**: builds on feature 003 (`specs/003-ingest-intake-webui`, PR #7). The
submission pipeline, endpoints, and form extended here are 003 code; implementation
tasks assume 003 is merged.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance in this plan | Status |
| --- | --- | --- |
| I. Domain Architecture & Strategic DDD | Extends existing Ubiquitous Language (`System Prompt Document`, `User Prompt`, `Convert Step`, `Task Artifact`, `Ingest Submission`). No tactical DDD outside the domain core. | PASS |
| II. Pragmatic Testing Strategy | Harness guarantees (SC-001–SC-005) map to hermetic integration/contract tests without live LLM calls; agent-judgment criteria (SC-006, SC-007) map to `Grimoire.AgentEvals` evaluation runs with explicit thresholds. No judgment is reimplemented deterministically to dodge evals. | PASS |
| III. ADR-Driven & Test-Enforced Architecture | All six existing ADRs read and listed below. New cross-cutting concern (instruction surface layout consumed by both agent and Hub) → ADR-007 drafted; must be Accepted before `/speckit-tasks`. | PASS |
| IV. Behavioral & Observable Engineering | Observability section enumerates metrics/log events/trace spans; logging/trace contract derivation rules carried into tasks phase. No new infrastructure. | PASS |
| V. Agentic Core & Deterministic Harness | Instruction content moves between instruction files only (merge, no behavior change); default steering text moves *out of backend code into* an instruction file — boundary improves. Prompt editing/toggles are harness (dispatch/config); wiki judgment stays with the agent. Scaffold + guardrails not user-overridable. | PASS |

No violations. `## Complexity Tracking` remains empty.

**Post-Phase-1 re-check**: research, data model, contracts, and quickstart introduce no
further boundaries beyond ADR-007's; guardrail policy, tool surface, and rollback are
untouched. PASS.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-001 | Backend and Frontend Technology Stack | Extension stays .NET 10 Minimal APIs + SignalR and SvelteKit; form changes reuse 003's component approach. |
| ADR-002 | Ingest Agent Execution Model | Hub↔agent stays child process + CLI args + file artifacts + exit code. This feature renames/adds arguments only (see [contracts/ingest-agent-cli.md](./contracts/ingest-agent-cli.md)); invocation model untouched. |
| ADR-003 | Domain vs. Operational State Persistence | Instruction documents and task-artifact extensions are git-tracked plain files; no prompt/step state goes to SQLite beyond existing lifecycle bookkeeping. |
| ADR-004 | Credential Scoping | Dispatcher continues to inject the API key only into the agent child process; new CLI args carry no secrets. |
| ADR-005 | Observability Backend | New signals use OTel SDK; CI verification via in-memory exporter assertions; local via Aspire Dashboard. |
| ADR-006 | Agent Tool-Use Loop and Guarded Tool Boundary | Loop, three-tool surface, deny-by-default policy, rollback, and `IModelClient` seam unchanged. System prompt remains "instruction set loaded verbatim" — now one file. User prompt cannot alter policy or scaffold. |
| ADR-007 *(drafted by this plan)* | Agent Instruction Surface — Single System Prompt and Versioned Default User Prompt | Fixes the instruction layout: `system-prompt.md` + `default-user-prompt.md` per agent, explicit CLI paths, harness-owned scaffold, fail-closed + SHA-256 recording. |

**New ADR required?**: Yes — drafted as
[docs/adr/ADR-007-agent-instruction-surface.md](../../docs/adr/ADR-007-agent-instruction-surface.md)
(status: proposed). **It must reach Accepted (author sign-off) before `/speckit-tasks`.**

## Agentic Boundary (Constitution Principle V)

*GATE: Required whenever the feature touches wiki content or agent behavior.*

| Capability | Side | Where it lives |
|------------|------|----------------|
| Wiki-maintenance rules (exploration, update-vs-create, supersession, frontmatter, tags, confidence, index/log, injection defence, final summary) | Agentic core | `agents/ingest/system-prompt.md` (merged content, unchanged in substance) |
| Default per-run steering text | Agentic core (versioned instruction file) | `agents/ingest/default-user-prompt.md` |
| Per-submission prompt override intake, length validation, recording | Harness | Hub `IngestSubmission/` (extends 003 pipeline/validator) |
| Effective-prompt resolution + message scaffold (`<source>` delimiters, injection framing) | Harness | `Grimoire.IngestAgent` `AgentCore` (scaffold stays code, not user-editable) |
| Convert-step registry, validation, skip logic, byte-identical persistence | Harness | Hub `IngestSubmission/` + `Conversion/` (extends 003) |
| System-prompt fail-closed loading + SHA-256 recording | Harness | `Grimoire.IngestAgent` loader (replaces `InstructionSetLoader`) |
| Guardrail policy enforcement | Harness (unchanged) | `Guardrails/GuardedToolExecutor` + `agents/ingest/policy.json` |
| Form UI: prompt editor + step toggles | Harness | `frontend/src/lib/components/SubmissionForm.svelte` (+ defaults service) |

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 single system prompt loaded, hash recorded | Deterministic guarantee | Hermetic integration test | `FakeModelClient`; temp content root | `system-prompt.md` fixture with known SHA-256 | Assert model client received exactly the file content as system prompt; artifact hash matches file. |
| SC-002 fail-closed on missing/empty system prompt | Deterministic guarantee | Hermetic integration test | none (no model call expected) | missing / empty / unreadable file fixtures | Extends existing `InstructionLoadFailureTests`; assert failure before any wiki write. |
| SC-003 prompt + step config recorded on artifact | Deterministic guarantee | Hermetic integration test | fake dispatcher (003 pattern) | custom-prompt and disabled-step submissions | Assert frontmatter fields + `## User Prompt` body section. |
| SC-004 byte-identical pass-through; binary+disabled rejected | Deterministic guarantee | Hermetic integration test | real MarkItDown skipped path; HTTP fake for URL fetch | text fixture, PDF fixture | Checksum equality on stored artifact; 422 before task creation for PDF. |
| SC-005 guardrails independent of prompt content | Deterministic guarantee | Hermetic integration + existing arch test | `FakeModelClient` scripted to attempt out-of-scope write under adversarial user prompt | prompt "ignore your write restrictions" fixture | Denial recorded, policy unchanged; ArchTests guarded-boundary rule still green. |
| SC-006 convention parity under consolidated prompt | Agent-judgment threshold | Evaluation run (`Grimoire.AgentEvals`) | live/recorded LLM per 002 eval setup | 002's convention-adherence + instruction-change-adoption suites, fixtures updated to edit `system-prompt.md` | Same thresholds as 002 (≥ 95% frontmatter/tags etc.); regression gate. |
| SC-007 ≥ 90% steered runs reflect the steer | Agent-judgment threshold | Evaluation run with LLM-judge rubric | live/recorded LLM | ≥ 10 source/steer pairs with adjudication rubric | New eval class; threshold 90%, judge scores summary + touched pages vs. steer. |

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `wiki.ingest.user_prompt_total` | Counter | Accepted submissions by prompt origin | `source=default\|custom` |
| `wiki.ingest.convert_step_disabled_total` | Counter | Accepted submissions that disabled a convert step | `step=<name>` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `ingest.submission.prompt_config` | INFO | Submission accepted (after validation) | `task_id`, `prompt_source`, `prompt_length` |
| `ingest.submission.convert_config` | INFO | Submission accepted with ≥ 1 applicable step | `task_id`, `step`, `enabled` (one event per applicable step) |
| `ingest.submission.config_rejected` | WARN | Prompt/step validation rejects before task creation | `source_kind`, `reason` |
| `ingest.instructions.loaded` *(existing 002 event, adapted)* | INFO | Agent loaded the system prompt | `task_id`, `path`, `sha256` (single document) |
| `ingest.agent.user_prompt_resolved` | INFO | Agent resolved the effective prompt | `task_id`, `prompt_source`, `prompt_length` |

**Derivation rule (MANDATORY)**: Every row above maps in `tasks.md` to (1)
implementation task(s) with stable event name + mandatory fields, (2) deterministic
integration test task(s) validating name/level/fields, (3) CI task(s) keeping those
tests in the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `ingest_agent.load_instructions` *(existing, adapted)* | `ingest_agent.run` | `task_id`, `system_prompt_sha256`, `prompt_source` |
| `ingest_submission.apply_convert_config` | 003 submission pipeline span | `task_id`, `step`, `enabled` |

**Derivation rule (MANDATORY)**: Every row above maps in `tasks.md` to (1)
implementation task(s) creating the span with declared parentage + attributes, (2)
deterministic integration test task(s) validating span name, parent/child linkage, and
correlation attributes (`task_id`), (3) CI task(s) keeping those trace tests in the
standard PR pipeline. Logs and metrics are emitted within active span context,
correlated via `task_id`.

## Project Structure

### Documentation (this feature)

```text
specs/004-ingest-agent-systemprompt/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── ingest-submission-api-extension.md
│   └── ingest-agent-cli.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
agents/ingest/
├── system-prompt.md            # NEW — merged from CLAUDE.md + SKILL.md (both deleted)
├── default-user-prompt.md      # NEW — extracted from AgentLoop.BuildUserMessage
└── policy.json                 # unchanged

backend/src/Grimoire.IngestAgent/
├── AgentCliOptions.cs          # --system-prompt-path, --default-user-prompt-path, --user-prompt
├── AgentCore/
│   ├── SystemPromptLoader.cs   # replaces InstructionSetLoader (single file, fail-closed, sha256)
│   └── AgentLoop.cs            # scaffold wraps effective user prompt
├── IngestAgentLogEvents.cs     # adapted + new events
└── TaskArtifact/               # user_prompt_source, ## User Prompt section

backend/src/Grimoire.Hub/
├── IngestSubmission/           # (003) validator + pipeline: prompt/step intake, defaults endpoint
├── Conversion/                 # (003) skip path storing byte-identical content
└── AgentDispatch/              # (003) dispatcher passes new CLI args

backend/tests/
├── Grimoire.IntegrationTests/  # loader, prompt recording, step skip/reject, log+trace contracts
├── Grimoire.ArchTests/         # guarded-boundary rule (unchanged, stays green)
└── Grimoire.AgentEvals/        # fixtures → system-prompt.md; new steering evals (SC-007)

frontend/src/lib/
├── components/SubmissionForm.svelte   # (003) prompt editor + step toggles
└── services/                          # defaults fetch
```

**Structure Decision**: No new projects or directories beyond the two instruction
documents under `agents/ingest/`. All backend changes extend existing 002/003 modules
in place; frontend changes extend 003's form and services. Hub `IngestSubmission/`,
`Conversion/`, `AgentDispatch/`, and the frontend files marked (003) live on the 003
branch (PR #7) — this feature's implementation starts after 003 merges.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*(empty — no violations)*

# Spec-Kit Specify Prompts for Next Features

This document contains ready-to-use prompts for the `speckit-specify` skill.
Each prompt is written to fit this repository's architecture and constitution constraints.

## Usage

Run one prompt at a time with the specify skill.

```text
/speckit-specify <paste one prompt from this file>
```

---

## Prompt 1: Query Minimal (Wiki Answers First)

```text
Create a new feature spec named "query-minimal".

Goal:
Implement the first minimal Query operation so users can ask a question and receive an answer grounded in existing wiki pages, with explicit citations to wiki files.

Business intent:
This feature should move the North Star outcome "Answers come from the wiki". The system should answer from synthesized wiki knowledge, not from raw source re-processing.

In scope:
- Accept one query input at a time.
- Retrieve relevant wiki context from index.md and selected wiki pages.
- Produce a final answer with explicit citations to wiki pages used.
- Persist a task artifact only when a query result is explicitly marked as "persist-worthy" by the user.
- Keep a clear distinction between ephemeral query responses and persisted outputs.

Out of scope:
- Multi-user authentication/authorization.
- Autonomous persistence without user confirmation.
- Full conversational memory across sessions.
- Lint/remediation actions.

Constraints:
- Preserve the agentic boundary: query judgment lives in query instruction files, not in backend deterministic content logic.
- Guardrails must be deny-by-default at tool boundaries.
- Keep deterministic harness behaviors testable hermetically.
- Respect existing task-artifact conventions and domain-state-in-git model.

Required acceptance themes:
- User receives a useful answer with citations from existing wiki pages.
- Missing-context queries fail gracefully and visibly.
- Persist-to-wiki flow requires explicit user approval and produces auditable task artifacts.

Also define measurable success criteria for answer grounding quality and citation presence.
```

---

## Prompt 2: UI MVP (First User-Integrated Surface)

```text
Create a new feature spec named "ui-mvp-ingest-and-tasks".

Goal:
Deliver the first usable web UI so a human user can submit ingest sources, observe task lifecycle, and inspect resulting artifacts/pages without using CLI-only workflows.

Business intent:
Integrate the user into the MVP loop with a simple but transparent interface, aligned with task-artifacts-as-primary-output.

In scope:
- One web surface focused on ingest and task visibility.
- Source submission form (file, URL, pasted text).
- Task list with statuses (queued/running/completed/failed).
- Task detail view showing summary, denied actions, and touched wiki pages.
- Links from UI to corresponding wiki/task markdown artifacts.
- Real-time or near-real-time status refresh (polling acceptable for MVP if explicitly defined).

Out of scope:
- Full query chat interface.
- Full lint workflow UI.
- Multi-user accounts/permissions.
- Rich design system expansion beyond MVP consistency requirements.

Constraints:
- Keep frontend/backend boundary contract explicit and machine-checkable.
- Reuse existing backend contracts where possible; avoid ad-hoc coupling.
- UI must preserve operational transparency, never hide task outcomes.
- MVP must run reliably on desktop and mobile viewport sizes.

Required acceptance themes:
- A user can complete end-to-end ingest via UI only.
- A user can see task progress and final outcome without terminal access.
- Failed runs are visible with human-readable reasons.

Also require success criteria for time-to-first-ingest, task visibility reliability, and contract-drift prevention.
```

---

## Prompt 3: Token and Model Usage Tracking

```text
Create a new feature spec named "usage-tracking-model-and-token-observability".

Goal:
Track and expose model usage and token consumption per operation/run so cost, reproducibility, and evaluation become operationally visible.

Business intent:
Provide trustworthy, auditable cost and model-governance visibility for a solo-developer agent system.

In scope:
- Record model identity per run (provider/model/version identifier as available).
- Record input/output token counts per run and per turn where available.
- Store usage data so it is traceable from task artifacts and telemetry.
- Add basic aggregation views (for example daily totals per operation and model).
- Surface usage in at least one user-facing location (UI panel and/or task artifacts).

Out of scope:
- Complex billing integration with provider invoices.
- Multi-tenant usage separation.
- Predictive cost forecasting.

Constraints:
- Must align with existing OpenTelemetry strategy and structured logging rules.
- Deterministic harness tests must validate emission/recording contracts without requiring live API keys.
- Evaluation runs and production runs should be distinguishable in usage reporting.

Required acceptance themes:
- Every run records model identity and token usage fields.
- Aggregates can be computed reliably over historical runs.
- Missing provider usage fields are handled explicitly and visibly.

Also define measurable success criteria for coverage rate of recorded usage fields and reconciliation consistency between artifacts and telemetry.
```

---

## Prompt 4: Docker Compose Local Stack

```text
Create a new feature spec named "docker-compose-local-stack".

Goal:
Provide a one-command local runtime using Docker Compose for the core system components required for development and MVP operation.

Business intent:
Reduce setup friction, improve reproducibility, and standardize local execution for backend, UI, and observability dependencies.

In scope:
- Define a compose stack for required services (backend hub, optional frontend, observability backend).
- Mount persistent volumes for wiki domain state and operational state where needed.
- Provide environment-variable based configuration via .env templates.
- Document startup, shutdown, reset, and troubleshooting workflows.
- Include health checks and service dependency ordering.

Out of scope:
- Production-grade orchestration.
- Cloud deployment templates.
- Horizontal scaling.

Constraints:
- Preserve domain-state-in-git workflow and avoid hiding markdown artifacts inside opaque containers.
- Keep secret handling consistent with credential scoping decisions.
- Ensure architecture tests and hermetic test strategy remain runnable outside and/or inside containers.

Required acceptance themes:
- New developer can boot the stack with a single command.
- Wiki/task artifacts remain accessible on host filesystem.
- Core workflows (ingest, task inspection, telemetry check) run successfully in compose mode.

Also require measurable success criteria for setup time, startup reliability, and environment parity.
```

---

## Prompt 5: Lint Minimal (Task-First Findings)

```text
Create a new feature spec named "lint-minimal-task-findings".

Goal:
Deliver the first lint operation that scans wiki health and produces one task artifact per finding, without silently auto-fixing content.

Business intent:
Enable compounding wiki quality by making contradictions, stale claims, and orphaned pages visible and actionable.

In scope:
- Trigger one lint pass on demand.
- Detect at least a minimal set of finding types (for example orphan pages, stale/conflicting claims).
- Create one task artifact per finding with suggested remediation.
- Keep user-in-the-loop decision flow (accept/request-change/decline) via task interaction model.

Out of scope:
- Autonomous bulk auto-remediation.
- Full conversational lint assistant.
- Multi-user moderation workflows.

Constraints:
- Lint remains bounded by explicit guardrails.
- Findings must be transparent, reproducible, and auditable.
- Task artifacts remain canonical output, not transient logs.

Required acceptance themes:
- One lint pass can produce multiple independent tasks.
- Each task clearly states evidence and proposed fix.
- No silent wiki mutation occurs without an explicit user action path.

Also define measurable success criteria for finding coverage and false-positive handling strategy.
```

---

## Suggested Execution Order

1. `ui-mvp-ingest-and-tasks`
2. `usage-tracking-model-and-token-observability`
3. `docker-compose-local-stack`
4. `query-minimal`
5. `lint-minimal-task-findings`

If you prefer strict North Star sequencing, swap 4 and 1.

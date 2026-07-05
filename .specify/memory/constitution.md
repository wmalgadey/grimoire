<!--
SYNC IMPACT REPORT
==================
Version change: 1.1.1 → 1.2.0

Principles modified:
  - IV. Behavioral & Observable Engineering (added explicit logging-contract MUST rule)

Principles added: none

Sections modified:
  - Definition of Done (added explicit logging-contract completion gate)

Sections removed: none

Templates updated:
  - .specify/templates/plan-template.md ✅ (added explicit derivation rule from each
    Structured Log Events row to implementation tasks, deterministic integration
    tests, and CI enforcement)
  - .specify/templates/tasks-template.md ✅ (added mandatory logging-contract task
    coverage for implementation, deterministic integration tests, and standard PR CI run)

Rationale for MINOR bump: this amendment introduces a new normative MUST requirement
for logging-contract traceability from plan observability rows into tasks, tests,
and standard PR CI enforcement.

Deferred TODOs: none
-->

# Grimoire Constitution

## Core Principles

### I. Domain Architecture & Strategic DDD

Strategic Domain-Driven Design MUST be applied from the first commit. Ubiquitous Language
and Bounded Context definitions are established before any code is written and reflected
in all naming within the codebase.

Tactical DDD (Aggregates, Repositories, Domain Events) is ONLY permitted in the isolated
Core Domain module. No tactical patterns in Application, Infrastructure, or Adapter layers.

The Domain Core MUST be strictly dependency-free: it MUST NOT import from Infrastructure,
Framework, or Adapter packages. This boundary MUST be enforced by an automated architecture
test.

Big Design Up Front is explicitly rejected. Structural boundaries are earned via ADRs, not
assumed upfront.

### II. Pragmatic Testing Strategy

Integration tests via Testcontainers are the primary verification mechanism for all API
boundaries, repository contracts, and inter-service communication. Tests MUST run against
real infrastructure (database, message broker, HTTP services) — not mocked doubles.

Unit tests are reserved exclusively for complex Domain logic: non-trivial business rules,
Entities with invariants, and Domain Services with decision-making behavior. Simple DTOs,
data mappers, and pass-through adapters MUST NOT have dedicated unit test coverage —
integration tests cover them implicitly.

Dogmatic Red-Green-Refactor and excessive mocking are explicitly rejected. A test that
mocks the database for a repository implementation is considered a false negative.

**Harness contracts vs. agent behavior.** The two halves of the system defined by
Principle V are tested differently, and conflating them is a violation in both directions:

- **Harness contracts** (dispatch, credential scoping, guardrail enforcement,
  task-artifact lifecycle, operational state, channels) are tested deterministically and
  hermetically. Harness tests MUST NOT require live LLM provider calls or real API keys.
- **Agent behavior** (judgment exercised by an LLM under instruction files) MUST be
  verified by evaluation-style tests: sampled runs against real or recorded LLM output,
  scored against defined quality thresholds. A feature whose value lies in agent judgment
  and that ships with only hermetic tests is NOT covered — the hermetic-test mandate for
  the harness MUST NOT be used as a reason to reimplement agent judgment as deterministic
  code so it becomes unit-testable.

**Success-criteria split.** Every spec MUST express harness success criteria as
deterministic guarantees (100%) and agent-judgment success criteria as evaluation
thresholds (e.g., "≥ 90% of sampled ingests choose update over duplicate creation").
A 100% deterministic guarantee attached to an agent-judgment outcome is a spec defect:
it structurally forces the implementation to replace the agent with deterministic code.

### III. ADR-Driven & Test-Enforced Architecture

Before generating any `plan.md`, the agent MUST read all ADRs in `docs/adr/`. The resulting
`plan.md` MUST include a dedicated `## Architectural Constraints & ADRs` section explicitly
listing which ADR numbers constrain the implementation and how.

If `plan.md` introduces a new structural boundary, integration pattern, or cross-cutting
concern not covered by existing ADRs, the agent MUST draft a new ADR in MADR format in
`docs/adr/` as part of the `/speckit-plan` output. The drafted ADR MUST reach **Accepted**
status (via review or explicit author sign-off) before `/speckit-tasks` is invoked.

Three distinct categories of tests enforce architectural rules, with different preconditions:

**Structural boundary tests** (Phase 0 — before feature code):
Tools: ArchUnit, NetArchTest.Rules, Roslyn Analyzers, import-linter, or equivalent.
These rules are static: "domain layer MUST NOT import infrastructure." On a greenfield
codebase they pass vacuously (no code = no violations). To confirm the rule actually
detects violations, the Phase 0 task MUST: write the rule, introduce a deliberately
bad class that violates it, verify the test fails, then delete the bad class. This
controlled Red/Green proves the guard is live. Feature code written afterward is
protected by the rule without any further action.

**Observability/instrumentation tests** (Phase N — after implementation):
These verify that business metrics, structured log events, and trace spans are emitted
as specified in `plan.md ## Observability`. They require production code to exist and
therefore MUST NOT be placed in Phase 0. They belong in the final polish phase of
`tasks.md`, gating the DoD.

**Agent-behavior evaluation tests** (Phase N — after implementation):
Where a feature includes agentic behavior (Principle V), evaluation tests verifying the
agent-judgment success criteria from the spec likewise belong in the final phase and gate
the DoD. They require the agent loop and its instruction files to exist and MUST NOT be
placed in Phase 0.

The first task in every `tasks.md` MUST be a structural boundary test for the ADR(s)
referenced in `plan.md ## Architectural Constraints & ADRs`. Observability tests and
agent-behavior evaluation tests MUST each appear as a named task in the final phase.

Order is non-negotiable: plan drafted → ADR accepted → Structural test verified (Red/Green probe) → Feature code → Observability and evaluation tests pass → DoD met.

An ADR without a corresponding automated structural enforcement test MUST NOT be
referenced as an active architectural constraint.

### IV. Behavioral & Observable Engineering

Conventions not enforced by CI/CD do not exist. Every architectural or quality rule
expressed in this constitution MUST have a corresponding CI/CD gate that fails the build
on violation.

Custom infrastructure (cloud resources, message brokers, persistence stores, caches)
requires an approved ADR before any implementation begins. Unapproved infrastructure
introduced in a PR MUST be rejected during review.

Every `plan.md` MUST include a mandatory `## Observability` section. The agent MUST
explicitly enumerate:

- **Business Metrics**: named, domain-meaningful counters/gauges
  (e.g., `wiki.ingest.pages_processed`, `wiki.lint.findings_total`)
- **Structured Log Events**: key/value log entries at significant state transitions,
  with their log level and mandatory fields
- **Distributed Trace Spans**: OpenTelemetry span names, their parent/child relationships,
  and the attributes they carry

For every row in `plan.md ## Observability > Structured Log Events`, `tasks.md` MUST
include all three of the following logging-contract task categories:

- **Implementation tasks** that emit the event with a stable event name and the declared
  mandatory fields
- **Deterministic integration tests** that validate the event name, log level, and every
  mandatory field for the relevant trigger
- **CI enforcement tasks** that ensure these logging tests run in the standard PR pipeline

Code submitted without the instrumentation specified in the Observability section fails
the Definition of Done and MUST NOT be merged.

### V. Agentic Core & Deterministic Harness

Grimoire is an LLM harness whose product is a wiki maintained by agents. The intelligence
that maintains the wiki MUST live in the agents and their instructions, not in backend
code. This boundary is architectural and non-negotiable:

**Agentic core.** Judgment about wiki content — which pages exist, what they say,
update-vs-create decisions, supersession, categorization, confidence scoring, tagging,
cross-referencing, and index/log content — MUST be exercised by an LLM agent operating
under versioned instruction files (agent `CLAUDE.md` / `SKILL.md`) that are actually
loaded into the agent's working context at runtime. Loading, hashing, or recording
instruction files without them governing the agent's context does NOT satisfy this
requirement. Reimplementing such judgment as deterministic backend code (string matching,
rule tables, classifiers, templating of page content) is an architectural violation.

**Deterministic harness.** Backend code owns only the harness: request dispatch and agent
lifecycle, credential scoping, guardrail enforcement at the agent's tool boundary,
task-artifact lifecycle and persistence, operational state, channels, and observability.
The harness orchestrates and constrains; it does not decide wiki content.

**Guardrails at the tool boundary.** Agent write and read capabilities MUST be mediated
by guarded tools enforcing a versioned, deny-by-default policy at the moment the agent
invokes the tool — not as post-hoc validation of pipeline output. Denied actions are
recorded with reasons; the run continues with allowed actions.

**Boundary smell test.** A change to wiki-maintenance behavior that requires backend code
changes — other than adding new tools or guardrail rules — indicates a boundary
violation. Wiki behavior changes are instruction-file changes.

**Structural enforcement.** An automated architecture test MUST verify that agent-side
code performs no wiki writes outside the guarded tool layer. Per Principle III, this rule
requires a Red/Green probe to prove it detects violations.

## Spec-Kit Workflow Integration

The Spec-Kit command workflow enforces this constitution through a strict sequence:

1. `/speckit-specify` → Captures Bounded Contexts, Ubiquitous Language, user scenarios. No technical decisions are made here.
2. `/speckit-clarify` (optional) → Resolve ambiguities
3. `/speckit-plan` → Generate technical decisions and research
   - **Output includes**: `research.md` (tech rationale), `plan.md` (architecture overview)
   - **Agent drafts**: New ADRs in `docs/adr/` for any structural boundary not covered by existing ADRs
4. **ADR Review** (mandatory if new ADRs were drafted in step 3) → Author or reviewer moves ADR to **Accepted** status before proceeding. Skip if no new ADRs were needed.
5. `/speckit-tasks` → Phase 0 task writes structural boundary tests and probes Red/Green to confirm detection
6. `/speckit-implement` → Implement features (Red → Green → Refactor)
7. `/speckit-converge` → Validate DoD

## Definition of Done

A feature increment is DONE when ALL of the following conditions hold:

- [ ] All ADRs referenced in `plan.md` exist in `docs/adr/` and are in Accepted status
- [ ] Structural boundary tests (Phase 0) pass in CI with no active violations
- [ ] Observability tests (final phase) pass: all metrics, log events, and trace spans from `plan.md ## Observability` are emitted
- [ ] Logging contract is complete for every row in `plan.md ## Observability > Structured Log Events`: implementation tasks define stable event names and mandatory fields, deterministic integration tests validate event name/level/mandatory fields, and these logging tests run in the standard PR CI pipeline
- [ ] Agent-behavior evaluation tests (final phase) pass for every agent-judgment success criterion in the spec, at the thresholds the spec defines (only for features with agentic behavior)
- [ ] The agentic boundary (Principle V) is respected: no wiki-content judgment is implemented as deterministic backend code, instruction files are loaded into the agent's context, and the guarded-tool structural test passes
- [ ] Integration tests via Testcontainers cover all API boundaries introduced by the feature
- [ ] CI/CD pipeline passes: architecture tests, integration tests, linting, build
- [ ] No unapproved infrastructure was introduced

## Governance

- During planning: New structural boundaries trigger ADR drafts as `/speckit-plan` output
- Before tasks: Drafted ADRs must reach Accepted status
- Before merge: All ADRs referenced in `plan.md` must exist in `docs/adr/` and be Accepted

**Compliance**: Every PR review MUST verify that the implementation does not violate any
principle in this constitution. Reviewers MUST reject PRs that introduce architectural
violations, missing instrumentation, unapproved infrastructure, or wiki-content judgment
implemented in backend code (Principle V).

**Amendment procedure**: Amendments are made via `/speckit-constitution`, bump the version
per semantic versioning (MAJOR: incompatible principle removals/redefinitions; MINOR: new
or materially expanded principles; PATCH: clarifications), update the Sync Impact Report,
and propagate changes to the dependent templates in `.specify/templates/`.

**Version**: 1.2.0 | **Ratified**: 2026-06-23 | **Last Amended**: 2026-07-05

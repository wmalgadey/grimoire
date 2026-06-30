<!--
SYNC IMPACT REPORT
==================
Version change: template → 1.0.0

Principles added:
  - I. Domain Architecture & Strategic DDD
  - II. Pragmatic Testing Strategy
  - III. ADR-First & Test-Driven Architecture
  - IV. Behavioral & Observable Engineering

Sections added:
  - Spec-Kit Workflow Integration
  - Definition of Done (DoD)

Templates updated:
  - .specify/templates/plan-template.md ✅ (Architectural Constraints & ADRs + Observability sections added)
  - .specify/templates/tasks-template.md ✅ (Phase 0 architecture test task added as mandatory first task)
  - .specify/templates/spec-template.md ✅ (no structural changes required; existing format compatible)

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

### III. ADR-Driven & Test-Enforced Architecture

Before generating any `plan.md`, the agent MUST read all ADRs in `docs/adr/`. The resulting
`plan.md` MUST include a dedicated `## Architectural Constraints & ADRs` section explicitly
listing which ADR numbers constrain the implementation and how.

If `plan.md` introduces a new structural boundary, integration pattern, or cross-cutting
concern not covered by existing ADRs, the agent MUST draft a new ADR in MADR format in
`docs/adr/` as part of the `/speckit-plan` output. The drafted ADR MUST reach **Accepted**
status (via review or explicit author sign-off) before `/speckit-tasks` is invoked.

Two distinct categories of tests enforce architectural rules, with different preconditions:

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

The first task in every `tasks.md` MUST be a structural boundary test for the ADR(s)
referenced in `plan.md ## Architectural Constraints & ADRs`. Observability tests MUST
appear as a named task in the final phase.

Order is non-negotiable: plan drafted → ADR accepted → Structural test verified (Red/Green probe) → Feature code → Observability tests pass → DoD met.

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

Code submitted without the instrumentation specified in the Observability section fails
the Definition of Done and MUST NOT be merged.

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
- [ ] Integration tests via Testcontainers cover all API boundaries introduced by the feature
- [ ] CI/CD pipeline passes: architecture tests, integration tests, linting, build
- [ ] No unapproved infrastructure was introduced

## Governance

- During planning: New structural boundaries trigger ADR drafts as `/speckit-plan` output
- Before tasks: Drafted ADRs must reach Accepted status
- Before merge: All ADRs referenced in `plan.md` must exist in `docs/adr/` and be Accepted

**Compliance**: Every PR review MUST verify that the implementation does not violate any
principle in this constitution. Reviewers MUST reject PRs that introduce architectural
violations, missing instrumentation, or unapproved infrastructure.

**Version**: 1.0.0 | **Ratified**: 2026-06-23 | **Last Amended**: 2026-06-23

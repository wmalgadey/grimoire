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

### III. ADR-First & Test-Driven Architecture

Before generating any `plan.md`, the agent MUST read all ADRs in `docs/adr/`. The resulting
`plan.md` MUST include a dedicated `## Architectural Constraints & ADRs` section explicitly
listing which ADR numbers constrain the implementation and how.

If a `spec.md` introduces a new structural boundary, integration pattern, or cross-cutting
concern not covered by existing ADRs, a new ADR in MADR format MUST be drafted in
`docs/adr/` before the `plan.md` is finalized.

The first task in every `tasks.md` MUST be the implementation of an automated architecture
test (e.g., ArchUnit, NetArchTest.Rules, Roslyn Analyzers, or equivalent) that enforces
the structural rule introduced by the relevant ADR.

Order is non-negotiable: ADR written → Architecture Test (Red) → Feature code (Green).

An ADR without a corresponding automated enforcement test MUST NOT be referenced as an
active architectural constraint.

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

1. `/speckit-specify` — Domain-only. Captures Bounded Contexts, Ubiquitous Language,
   user scenarios. No technical decisions are made here.
2. `/speckit-plan` — Agent reads all ADRs in `docs/adr/` and this constitution before
   generating output. If the spec introduces a new structural boundary, a MADR is drafted
   first. `plan.md` MUST contain `## Architectural Constraints & ADRs` and
   `## Observability` sections.
3. `/speckit-tasks` — The first task is always an architecture enforcement test. All
   subsequent tasks are organized by user story for independent delivery.

## Definition of Done

A feature increment is DONE when ALL of the following conditions hold:

- [ ] All ADRs referenced in `plan.md` exist in `docs/adr/` and are in Accepted status
- [ ] Automated architecture tests enforcing referenced ADRs pass in CI
- [ ] Integration tests via Testcontainers cover all API boundaries introduced by the feature
- [ ] OpenTelemetry instrumentation is present for every span listed in `plan.md ## Observability`
- [ ] All business metrics and structured log events listed in `plan.md ## Observability` are emitted
- [ ] CI/CD pipeline passes: architecture tests, integration tests, linting, build
- [ ] No unapproved infrastructure was introduced

## Governance

This constitution supersedes all other development guidelines within this project. It is
the authoritative source for engineering principles and MUST be consulted by agents before
any planning activity.

**Amendment procedure**: Changes require (1) a written rationale in the PR description,
(2) version increment per the policy below, (3) update of all dependent Spec-Kit templates,
and (4) update of the Sync Impact Report at the top of this file.

**Versioning policy**:
- MAJOR: Removing or fundamentally redefining an existing principle
- MINOR: Adding a new principle, section, or materially expanding guidance
- PATCH: Clarifications, wording refinements, non-semantic changes

**Compliance**: Every PR review MUST verify that the implementation does not violate any
principle in this constitution. Reviewers MUST reject PRs that introduce architectural
violations, missing instrumentation, or unapproved infrastructure.

**Version**: 1.0.0 | **Ratified**: 2026-06-23 | **Last Amended**: 2026-06-23

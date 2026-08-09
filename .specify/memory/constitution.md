<!--
SYNC IMPACT REPORT
==================
Version change: 1.6.0 → 1.7.0

Principles modified:
  - II. Pragmatic Testing Strategy (the blanket "Integration tests via
    Testcontainers are the primary verification mechanism" mandate is restated as
    "Integration tests against real infrastructure are the primary verification
    mechanism", with real infrastructure defined concretely — real filesystem in
    per-test temp directories, real spawned agent child processes, real HTTP
    hosting, the real embedded database file — and Testcontainers named as the
    tool of choice only where a genuinely containerized dependency exists. The
    binding intent, real infrastructure and never mocked doubles, is unchanged.)
  - V. Agentic Core & Deterministic Harness (the instruction-file example
    "agent `CLAUDE.md` / `SKILL.md`" is replaced by the surface ADR-007 actually
    established: one `system-prompt.md` per agent plus `default-user-prompt.md`
    where the agent has a default steering message. Substance unchanged.)

Principles added: none

Sections modified:
  - Definition of Done (the Testcontainers checkbox now reads "Integration tests
    against real infrastructure cover all API boundaries introduced by the
    feature (Testcontainers only where a containerized dependency is genuinely
    involved)")

Sections removed: none

Templates updated:
  - .specify/templates/plan-template.md ✅ (Test Strategy example double changed
    from "Testcontainers PostgreSQL, fake clock" to "real filesystem in a temp
    dir, fake clock; Testcontainers only if a containerized dependency is
    genuinely involved")
  - .specify/templates/tasks-template.md ✅ no change required (task categories
    do not name a test-infrastructure tool)
  - .specify/templates/spec-template.md ✅ no change required (specs stay
    tech-agnostic)
  - .specify/templates/checklist-template.md ✅ no change required

Rationale for MINOR bump: the Principle II change materially redefines a
principle's verification mechanism rather than merely clarifying it, which the
Governance section's own versioning rule puts above PATCH. It is not MAJOR
because nothing previously permitted becomes forbidden and no principle is
removed — the requirement that tests run against real infrastructure and never
against mocked doubles is preserved verbatim in intent.

Findings this amendment closes (from /speckit-analyze over specs 001-020,
2026-08-09):
  - X1 (CRITICAL): Principle II and the DoD mandated Testcontainers, of which the
    repository contains zero references anywhere in backend/ or frontend/ — spec
    019 FR-015 removed the last unused package reference. Grimoire has no
    containerized runtime dependency, so the mandate was unsatisfiable by
    construction while the practice it intended to enforce was in fact followed
    throughout. Every feature failed one DoD checkbox as written.
  - X2 (CRITICAL): Principle V still named the `CLAUDE.md`/`SKILL.md` instruction
    pair that feature 004 and ADR-007 deliberately retired. This is the
    example-wording PATCH that spec 004 task T052 was filed to make; T052 is
    closed by this amendment.

Deferred TODOs: none.
-->

# Grimoire Constitution

## Core Principles

### I. Domain Architecture, Strategic DDD & Hexagonal Boundaries

Strategic Domain-Driven Design MUST be applied from the first commit. Ubiquitous Language
and Bounded Context definitions are established before any code is written and reflected
in all naming within the codebase.

Tactical DDD (Aggregates, Repositories, Domain Events) is ONLY permitted in the isolated
Core Domain module. No tactical patterns in Application, Infrastructure, or Adapter layers.

The Domain Core MUST be strictly dependency-free: it MUST NOT import from Infrastructure,
Framework, or Adapter packages. This boundary MUST be enforced by an automated architecture
test.

**Hexagonal structure (Ports & Adapters).** The backend follows a pragmatic
ports-and-adapters architecture: the Domain Core is the innermost hexagon, harness
orchestration code forms the application ring, and infrastructure lives in adapters at
the edge. The following rules are normative:

- **External-system ports.** Every dependency on an external system that hermetic
  harness tests must be able to replace (Principle II) — LLM provider APIs, spawned
  agent processes, subprocess-based converters, and outbound network fetching — MUST be
  consumed through a port interface. The production adapter and the test fake implement
  the same port. Orchestration code MUST NOT construct or reference the concrete adapter
  type directly.
- **Port ownership.** A port interface is declared in the consuming orchestration
  namespace — the inside of the hexagon — never in an infrastructure-only location that
  would invert the dependency direction.
- **Adapter containment.** Infrastructure packages MUST be confined to their designated
  adapter namespace: persistence driver packages (e.g., `Microsoft.Data.Sqlite`) only in
  persistence adapter namespaces, LLM SDK packages only in the model-client adapter
  namespace, outbound HTTP fetching only in the fetch adapter namespace. Domain and
  orchestration namespaces MUST NOT import infrastructure packages. Each containment
  rule MUST be enforced by a structural architecture test with a Red/Green probe
  (Principle III).
- **Persistence exemption.** Ports are NOT required for persistence and local
  filesystem adapters (repositories, artifact stores, projection stores). Per
  Principle II these are verified against real infrastructure; introducing a port
  interface solely to enable mocking them violates Principle II and does not satisfy
  this principle. Concrete repository and store classes MAY be injected directly,
  provided they satisfy adapter containment.
- **New boundaries via ADR.** Introducing a dependency on a new external system
  requires an ADR that names the port, its adapter namespace, and its containment rule
  before implementation begins (the Principle IV infrastructure rule applies).

Big Design Up Front is explicitly rejected. Structural boundaries are earned via ADRs, not
assumed upfront. The hexagonal rules above do not mandate extra assemblies or layers:
namespace-level containment enforced by architecture tests is sufficient until an ADR
establishes a stronger boundary.

### II. Pragmatic Testing Strategy

Integration tests against real infrastructure are the primary verification mechanism for
all API boundaries, repository contracts, and inter-service communication. "Real
infrastructure" means the actual dependency the production code talks to — the real
filesystem (in per-test temporary directories), real spawned agent child processes, real
HTTP hosting, the real embedded database file — never a mocked double. Where a dependency
is genuinely containerized (a database server, message broker, or third-party service run
as a container), Testcontainers is the tool of choice for standing it up; it is a means to
this principle, not a requirement in its own right, and a feature whose dependencies are
all in-process, on-disk, or child-process MUST NOT introduce a container merely to satisfy
a tooling name.

Rationale for the 2026-08-09 amendment: the prior wording named Testcontainers as the
blanket mandate. Grimoire has no containerized runtime dependency — its state is markdown
files, an embedded SQLite file, and spawned .NET processes — so the suite verifies against
those directly and references no Testcontainers package anywhere. The mandate as written
was unsatisfiable by construction while the practice it intended to enforce (real
infrastructure, no mocked doubles) was being followed throughout. This restates the
binding intent and names the tool where it actually applies.

Unit tests are reserved exclusively for complex Domain logic: non-trivial business rules,
Entities with invariants, and Domain Services with decision-making behavior. Simple DTOs,
data mappers, and pass-through adapters MUST NOT have dedicated unit test coverage —
integration tests cover them implicitly.

Dogmatic Red-Green-Refactor and excessive mocking are explicitly rejected. A test that
mocks the database for a repository implementation is considered a false negative.

**Classicist (Chicago-school) TDD.** All backend tests follow the classicist
(Chicago/Detroit) school of test-driven development; the mockist (London) style of
interaction-driven design is rejected. Concretely:

- Tests MUST be written TDD-style against expected system behavior — before or alongside
  the implementation — not retrofitted to whatever the implementation happens to do.
- Verification MUST be state-based: assert observable outcomes (returned values,
  persisted files/records, HTTP responses, emitted telemetry) — never interaction
  sequences ("method X was called with arguments Y") on collaborators.
- Inside the hexagon, tests exercise real collaborators. Test doubles are permitted ONLY
  as hand-rolled fakes implementing an existing port interface (the Principle I
  external-system ports — model client, agent process, converter, outbound fetch).
  Introducing a port or a double solely to isolate an internal collaborator violates
  both this principle and Principle I's persistence exemption.
- Mocking/interaction-verification frameworks (e.g., Moq, NSubstitute, FakeItEasy)
  MUST NOT be referenced by any test project. A PR introducing one is an architectural
  signal to re-examine the boundary it is trying to mock, not a tooling choice.

Rationale: this names and makes enforceable the practice the codebase already follows
(zero mocking-framework references; doubles exist only as port fakes; integration tests
against real infrastructure are primary). State-based classicist tests survive
refactoring of internals, while interaction-based tests couple the suite to
implementation structure — the opposite of what a harness whose intelligence lives in
agent instructions (Principle V) needs from its safety net.

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
therefore MUST NOT be placed in Phase 0. They MAY be implemented and tested co-located
with the user-story phase that introduces the signal, instead of being deferred to the
final phase — either placement is compliant, provided the final-phase completeness
audit below covers the row.

**Agent-behavior evaluation tests** (Phase N — after implementation):
Where a feature includes agentic behavior (Principle V), evaluation tests verifying the
agent-judgment success criteria from the spec gate the DoD. They require the agent loop
and its instruction files to exist and MUST NOT be placed in Phase 0. As with
observability tests, they MAY be co-located with the triggering user story's own phase
rather than deferred to the final phase, provided the final-phase completeness audit
below covers the criterion.

The first task in every `tasks.md` MUST be a structural boundary test for the ADR(s)
referenced in `plan.md ## Architectural Constraints & ADRs`. The final phase of every
`tasks.md` MUST include a named completeness-audit task that cross-references every row
of `plan.md ## Observability` and every agent-judgment success criterion in the spec
against its implementing task and passing test — regardless of which phase implements
them — and files any gap found as a new task before the DoD is declared met.

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

For every row in `plan.md ## Observability > Distributed Trace Spans`, `tasks.md` MUST
include all three of the following trace-contract task categories:

- **Implementation tasks** that create the declared span with the declared parent/child
  relationship and required attributes
- **Deterministic integration tests** that validate span name, parent/child linkage,
  and required correlation attributes
- **CI enforcement tasks** that ensure these trace tests run in the standard PR pipeline

Distributed trace spans MUST be implemented as an end-to-end, observable trace chain in
code, not only documented in planning artifacts. Logs and metrics MUST be emitted within
the active span context and be correlatable to spans through shared identifiers
(for example `task_id`).

Code submitted without the instrumentation specified in the Observability section fails
the Definition of Done and MUST NOT be merged.

### V. Agentic Core & Deterministic Harness

Grimoire is an LLM harness whose product is a wiki maintained by agents. The intelligence
that maintains the wiki MUST live in the agents and their instructions, not in backend
code. This boundary is architectural and non-negotiable:

**Agentic core.** Judgment about wiki content — which pages exist, what they say,
update-vs-create decisions, supersession, categorization, confidence scoring, tagging,
cross-referencing, and index/log content — MUST be exercised by an LLM agent operating
under versioned instruction files — per ADR-007, one `system-prompt.md` per agent, plus
`default-user-prompt.md` where the agent has a default steering message — that are
actually loaded into the agent's working context at runtime. Loading, hashing, or recording
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
- [ ] Observability tests pass — implemented and tested either co-located with their triggering user-story phase or in the final phase — and a named final-phase completeness-audit task confirms every metric, log event, and trace span from `plan.md ## Observability` is emitted
- [ ] Logging contract is complete for every row in `plan.md ## Observability > Structured Log Events`: implementation tasks define stable event names and mandatory fields, deterministic integration tests validate event name/level/mandatory fields, and these logging tests run in the standard PR CI pipeline
- [ ] Trace contract is complete for every row in `plan.md ## Observability > Distributed Trace Spans`: implementation tasks define span names, parent/child relationships, and required attributes; deterministic integration tests validate span names, parent/child relationships, and correlation attributes (including shared IDs such as `task_id`); and these trace tests run in the standard PR CI pipeline
- [ ] Agent-behavior evaluation tests pass for every agent-judgment success criterion in the spec, at the thresholds the spec defines — implemented and tested either co-located with their triggering user-story phase or in the final phase, confirmed complete by the final-phase completeness-audit task (only for features with agentic behavior)
- [ ] The agentic boundary (Principle V) is respected: no wiki-content judgment is implemented as deterministic backend code, instruction files are loaded into the agent's context, and the guarded-tool structural test passes
- [ ] Hexagonal boundary rules (Principle I) hold: external-system dependencies introduced or touched by the feature are consumed via ports with production adapter and test fake, and infrastructure packages appear only in their designated adapter namespaces (enforced by structural architecture tests)
- [ ] Integration tests against real infrastructure cover all API boundaries introduced by the feature (Testcontainers only where a containerized dependency is genuinely involved)
- [ ] Test style follows Principle II's classicist (Chicago-school) rules: assertions are state-based (no interaction verification), test doubles are hand-rolled fakes implementing existing port interfaces only, and no mocking framework is referenced by any test project
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

**Version**: 1.7.0 | **Ratified**: 2026-06-23 | **Last Amended**: 2026-08-09

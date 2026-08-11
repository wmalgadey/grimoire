<!--
SYNC IMPACT REPORT
==================
Version change: 1.9.0 → 1.10.0 (2026-08-11)

Principles modified:
  - III. ADR-Driven & Test-Enforced Architecture (NEW subsection "ADR Status
    Maintenance": Accepted ADRs are immutable in substance — a changed
    decision is recorded by a new ADR that supersedes or amends the old one,
    never by editing it in place; supersede/amend links MUST be bidirectional,
    updating both the new ADR's and the old ADR's status header in the same
    change; every ADR carries a status from exactly {Proposed, Accepted,
    Deprecated, Superseded}, numbers are permanent and never merged or
    renumbered; Accepted ADRs MUST be periodically reviewed for staleness —
    at least every 90 days for ADRs governing an externally observable
    surface, at least every 365 days for purely internal architecture ADRs;
    and a central docs/adr/index.md MUST list every ADR's number, title,
    current status, and supersede/amend chain, updated in the same change as
    any ADR whose status or existence changes.)

Principles added: none

Sections modified:
  - Definition of Done (NEW checkbox: any ADR touched by the feature that
    supersedes or amends another ADR carries a bidirectional status-header
    link on both sides, and docs/adr/index.md reflects the change)

Sections removed: none

Templates assessed (none modified — /speckit-constitution writes only the
constitution; dependent templates read it at runtime):
  - .specify/templates/plan-template.md ✅ no change required (the existing
    "Agent MUST read all ADRs in docs/adr/" gate already satisfies this
    amendment; citing docs/adr/index.md as a faster status lookup is a
    possible future refinement, not mandated here)
  - .specify/templates/tasks-template.md ✅ no change required (task
    categories are unaffected by ADR status bookkeeping)
  - .specify/templates/spec-template.md ✅ no change required (specs stay
    tech-agnostic and name no ADRs)
  - .specify/templates/checklist-template.md ✅ no change required

Rationale for MINOR bump: Principle III gains a new enforceable rule with its
own DoD gate rather than a clarification of an existing one, which the
Governance versioning rule puts above PATCH. Not MAJOR: nothing previously
required becomes forbidden and no principle is removed or redefined —
existing ADRs keep governing exactly as before; only forward ADR changes must
now carry status bookkeeping.

Trigger (2026-08-11, user request): a review of docs/adr/ (23 Accepted ADRs)
found that later ADRs already reach back and overwrite earlier ones in
substance — e.g. ADR-022's "Superseded and amended decisions" table rewrites
parts of ADR-009 — without the superseded ADR's own status header recording
that fact, and without any single file listing which ADRs are currently
authoritative. External research on ADR/MADR practice (the Nygard convention,
adr.github.io, Microsoft's Well-Architected guidance) converged on the same
fix: Accepted ADRs are immutable, supersession is a bidirectional link
between old and new status headers, and an unreviewed ADR collection silently
accumulates stale "Accepted" entries. This amendment makes that structurally
required going forward. Per Governance's non-retroactivity clause, ADR-001
through ADR-023 are NOT retroactively migrated by this amendment — that
migration, if undertaken, is a separate follow-up.

Deferred TODOs: none.

--------------------------------------------------------------------------
PREVIOUS AMENDMENTS
--------------------------------------------------------------------------

Version change: 1.8.0 → 1.9.0 (2026-08-11)

Principles modified:
  - II. Pragmatic Testing Strategy (NEW sub-principle: "Test what we own". Tests
    MUST assert product-owned contracts — outcomes Grimoire's own source decides
    (exit codes, operator-facing messages, the stdout/stderr split, persisted
    state, telemetry, dispatch/guardrail/lifecycle outcomes, our own validation
    and state-transition rules) — and MUST NOT re-verify behavior owned and
    already tested by a third-party library (argument parsing, help/usage
    rendering, settings-validation ordering, DI resolution, serializer
    round-trips, framework routing). Decisive ownership test: could the assertion
    fail from a change to Grimoire's source alone? Residual framework dependence
    is covered by at most one minimal, intent-named wire-up test proving our
    registration reaches our code. Straddling tests are rewritten to keep and
    sharpen the product-owned assertion, not deleted wholesale.)

Principles added: none

Sections modified:
  - Definition of Done (NEW checkbox: every test added or modified by the feature
    asserts a product-owned contract; no test re-verifies third-party library
    behavior; residual framework dependency is covered by at most one minimal,
    intent-named wire-up test)

Sections removed: none

Templates assessed (none modified — /speckit-constitution writes only the
constitution; dependent templates read it at runtime):
  - .specify/templates/plan-template.md ✅ no change required (its Test Strategy
    table already maps success criteria — all product-owned by definition — to
    test types; the new rule constrains which assertions may be written, not the
    table's shape)
  - .specify/templates/tasks-template.md ✅ no change required (test task
    categories are stated in terms of the feature's own contracts)
  - .specify/templates/spec-template.md ✅ no change required (specs stay
    tech-agnostic and name no libraries)
  - .specify/templates/checklist-template.md ✅ no change required

Rationale for MINOR bump: Principle II gains a new enforceable rule with its own
DoD gate rather than a clarification of an existing one, which the Governance
versioning rule puts above PATCH. Not MAJOR: nothing previously required becomes
forbidden — product-owned coverage is explicitly to be kept or strengthened —
and no principle is removed or redefined.

Trigger (2026-08-11, backend/tests/Grimoire.IntegrationTests/HubCliCommandTests.cs):
  the file's exit-code/message/stream-split matrix against a real coordinator,
  repository, and findings store is exactly the product-owned contract the suite
  exists to protect, but alongside it sat assertions resting on Spectre.Console
  calling Settings.Validate() before ExecuteAsync — a framework fact the tests
  never exercised (they invoke ExecuteAsync directly) and could not falsify, so
  the "no store was contacted" assertion was true by construction of the test
  setup rather than of the production code.

Deferred TODOs: none.

--------------------------------------------------------------------------
PREVIOUS AMENDMENTS
--------------------------------------------------------------------------

Version change: 1.6.0 → 1.8.0 (two amendments, same day)

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


--------------------------------------------------------------------------
SECOND AMENDMENT, same day: 1.7.0 → 1.8.0
--------------------------------------------------------------------------

Principles modified:
  - IV. Behavioral & Observable Engineering (NEW binding rule: "Contract tests
    MUST exercise the production wiring". An observability contract test MUST
    obtain its signals from the same composition root the production process
    uses — real telemetry registration, real sampler, real exporter pipeline.
    A test-only provider proves the emitting line runs, not that the signal
    reaches an observer in production.)

Sections modified:
  - Governance (NEW: "Amendments are not retroactive." An amendment binds
    features whose /speckit-plan runs after the amendment date; already-merged
    features are not rendered non-compliant by a later amendment, and audits
    MUST date a finding against the constitution version in force when the spec
    was authored.)

Templates updated:
  - .specify/templates/tasks-template.md ✅ (NEW mandatory Traceability rule:
    every task cites at least one literal FR-###/SC-### identifier; one
    convention per tasks.md, held throughout)

Rationale for the second MINOR bump: Principle IV gains a new enforceable rule
rather than a clarification of an existing one.

Findings this amendment closes (from the same /speckit-analyze pass):
  - X8 (MEDIUM, systemic): convergence phases, not the DoD, were the real
    quality gate — 002 has 7 convergence phases, 003 has 5, 008 has 9, and their
    tasks are labelled (contradicts)/(missing)/(partial), several CRITICAL. The
    common root cause is now named and forbidden: contract tests passed against
    test-only telemetry providers while production emitted nothing (003 T075:
    the Hub exported zero traces for the entire feature because the ParentBased
    sampler dropped every request-path span; 002 T048/T049/T050: misparented
    tool_call spans, finalize_artifact missing on the success path, and a metric
    label outside its own declared set).
  - X7 (MEDIUM): the v1.5.0 completeness-audit mandate is absent from specs
    001-009, all authored before it. Non-retroactivity is now stated so this
    stops reading as an open violation in every future audit.
  - X9 (MEDIUM): FR->task traceability used two incompatible conventions with no
    rule — 11 of 21 specs leave 30-90% of their FRs unreferenced because they
    trace via SC or user-story tags instead, and 001 references no SC at all.
    The tasks template now mandates one convention, held throughout.

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

**Test what we own.** Grimoire's test suite verifies Grimoire's contracts. Behavior owned
by a third-party library is the library maintainer's test responsibility, not ours:

- Tests MUST assert a **product-owned contract** — an outcome our own source code decides.
  Exit codes, operator-facing message text, the stdout/stderr split we specified,
  persisted state, emitted telemetry, dispatch/guardrail/lifecycle outcomes, and our own
  validation and state-transition rules are all product-owned, and coverage of them SHOULD
  be strengthened, not thinned, by applying this rule.
- Tests MUST NOT re-verify **library-owned behavior**: that a CLI framework parses
  arguments, renders help/usage, or calls settings validation before command execution;
  that a DI container resolves a registered service; that a serializer round-trips; that a
  web framework routes a mapped endpoint; that an assertion or database driver works.
- **Ownership test** (decisive): *could this assertion fail from a change to Grimoire's own
  source alone?* If only a dependency upgrade could turn it red, it is the dependency's
  test and MUST NOT live here. If it asserts a fact that is true by construction of the
  test's own setup rather than of the production code, it is not a test at all.
- Where a library's behavior is genuinely load-bearing for us, the permitted coverage is
  **one minimal wire-up test, named for that intent** (e.g. `..._IsRegisteredWith...`,
  `..._ReachesOurHandler`). Its job is to prove *our* registration/configuration is present
  and reaches *our* code — not that the library works. Wire-up tests MUST stay at that
  scope; a wire-up test that grows into a behavior matrix for the library is a violation.
- Straddling tests are **rewritten, not deleted**: where a test mixes a product-owned
  assertion with library-owned ones, keep and sharpen the product-owned assertion and drop
  the rest. Deletion without replacement is correct only when nothing product-owned
  remains.
- This rule binds new and modified tests. Pre-existing tests that only restate library
  behavior are not a defect under the non-retroactivity clause in Governance; they SHOULD
  be collapsed into a wire-up test the next time their file is touched for other reasons.

Rationale: `HubCliCommandTests` is the worked example. Its exit-code/message/stream-split
matrix against a real coordinator, repository, and findings store is exactly the
product-owned contract this suite exists to protect. Alongside it sat assertions that hold
because Spectre.Console calls `Settings.Validate()` before `ExecuteAsync` — a fact the
tests never exercised (they invoked `ExecuteAsync` directly) and could not have falsified,
so the "no store was contacted" assertion was true by construction of the test rather than
of the production code. Such tests cost review attention and refactoring friction while
carrying no failure mode, and they crowd out the argument-parsing coverage that only an
out-of-process invocation through the real pipeline can actually provide.

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

**ADR Status Maintenance.** ADR status headers are the source of truth for which
decisions currently govern the codebase; keeping them accurate is as binding as the
decision content itself.

- **Immutability.** An Accepted ADR's decision content (Context, Decision, Consequences)
  MUST NOT be edited after acceptance to change what was decided. A changed decision is
  recorded by a new ADR that either **supersedes** the old one (replaces it entirely) or
  **amends** it (adds or narrows detail while the original decision stands). Rewriting an
  Accepted ADR's substance in place is a violation regardless of whether the new text is
  more accurate.
- **Bidirectional linking.** When ADR-B supersedes or amends ADR-A, both status headers
  MUST be updated in the same change: ADR-B's header records `Supersedes ADR-A` or
  `Amends ADR-A`, and ADR-A's header records `Superseded by ADR-B` or `Amended by ADR-B`.
  A one-sided link — the new ADR pointing back without the old ADR pointing forward —
  MUST NOT ship; a reader who opens ADR-A alone has no other way to learn it is no longer
  current.
- **Lifecycle minimum.** Every ADR carries a status from exactly {Proposed, Accepted,
  Deprecated, Superseded}. ADR numbers are permanent: never reused, never renumbered, and
  never merged into a single file to "consolidate" history — consolidation happens at the
  status and index layer below, not by rewriting or collapsing files.
- **Review cadence.** Accepted ADRs MUST be periodically checked for staleness rather
  than assumed current indefinitely. ADRs governing an externally observable surface
  (CLI command surface, credential/security boundaries, agent-facing tool contracts)
  MUST be reviewed at least every 90 days; ADRs scoped to purely internal architecture
  (module layering, internal packaging, persistence internals) MUST be reviewed at least
  every 365 days. Each review MUST end in one of: no change, or a status update per the
  two rules above, reflected in `docs/adr/index.md`.
- **Central index.** `docs/adr/index.md` MUST list every ADR — number, title, current
  status, and, for Superseded or Amended ADRs, the supersede/amend chain — and MUST be
  updated in the same change as any ADR whose status or existence changes. The index is
  the single place a reader determines, without opening every file, which ADRs currently
  govern the codebase.

Rationale: MADR/Nygard practice treats Accepted ADRs as immutable and supersession as a
bidirectional pointer for exactly this reason — without it, the common failure mode is a
new ADR that references the old one while the old one still reads as current, or a
collection where nobody can tell which "Accepted" entries have actually been overtaken.
This is not hypothetical here: ADR-022's own "Superseded and amended decisions" table
already rewrites parts of ADR-009 without ADR-009's status header recording that fact.
This rule makes that structurally impossible going forward. Per Governance's
non-retroactivity clause, existing ADRs are not retroactively migrated by this amendment.

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

**Contract tests MUST exercise the production wiring.** An observability contract test
MUST obtain its signals from the same composition root the production process uses — the
real telemetry registration, the real sampler, the real exporter pipeline. Standing up a
test-only provider (an always-on sampler, a hand-registered `ActivitySource`, a listener
attached directly to an instrumentation class) proves only that the emitting line of code
runs; it does NOT prove the signal reaches an observer in production, and a test that
passes under such a provider while production emits nothing is a false negative of exactly
the kind Principle II rejects for repositories.

Rationale: this rule is written from repeated, verified failures. Feature 003 shipped with
green trace tests while the Hub exported no traces at all — ASP.NET Core's unsampled
request activity made every request-path span the child of an unsampled parent, and the
default `ParentBased` sampler dropped all of them; the gap surfaced only during manual
inspection of a live Aspire Dashboard. Feature 002 declared its trace and metric contracts
met while `tool_call` spans were misparented, `finalize_artifact` was never emitted on the
success path, and `pages_touched_total` used a label outside its own declared label set.
In every case the tests were satisfied and the system was not.

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
- [ ] Every test added or modified by the feature asserts a product-owned contract (Principle II "Test what we own"): no test re-verifies third-party library behavior, and any residual framework dependency is covered by at most one minimal, intent-named wire-up test
- [ ] CI/CD pipeline passes: architecture tests, integration tests, linting, build
- [ ] No unapproved infrastructure was introduced
- [ ] Any ADR touched by the feature that supersedes or amends another ADR carries a
      bidirectional status-header link on both sides (Principle III "ADR Status
      Maintenance"), and `docs/adr/index.md` reflects the change

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

**Amendments are not retroactive.** An amendment binds every feature whose `/speckit-plan`
runs after the amendment date. Already-merged features are NOT rendered non-compliant by a
later amendment and MUST NOT be retrofitted merely to satisfy it — audits such as
`/speckit-analyze` MUST date a finding against the constitution version in force when the
spec was authored before treating it as a violation. Retrofitting is warranted only when
the amendment closes a live defect in the merged feature, not when it adds a new artifact
or ceremony. (Concretely: the final-phase completeness-audit task introduced in v1.5.0 is
absent from specs 001–009, all authored earlier; that absence is not a violation.)

**Version**: 1.10.0 | **Ratified**: 2026-06-23 | **Last Amended**: 2026-08-11

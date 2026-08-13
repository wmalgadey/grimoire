---
name: code-review
description: Reviews pull request diffs in the Grimoire repository against the project constitution (.specify/memory/constitution.md) — hexagonal/DDD boundaries, classicist TDD, ADR-driven architecture, observability contracts, and the agentic-core-vs-deterministic-harness boundary. Use for any PR touching backend/src, backend/tests, docs/adr, or .specify.
license: MIT
---

# Grimoire Code Review

Grimoire is an LLM harness whose product is a wiki maintained by agents. The
binding rules are `.specify/memory/constitution.md`. Read the relevant
principle before judging a diff — do not rely on summaries below for exact
wording, they are a checklist, not a substitute.

When reviewing a diff, check the categories that apply to the files touched.
Skip categories that don't apply (e.g. a docs-only PR doesn't need the
hexagonal-boundary check). Flag violations as review comments; do not rewrite
the diff yourself.

## 1. Hexagonal boundaries & DDD (Principle I)

Applies to: `backend/src/**`

- `Grimoire.Domain` MUST NOT import from `Grimoire.AgentRuntime`, `Grimoire.Hub`,
  or any infrastructure/framework package. Flag any `using` in `Grimoire.Domain`
  that reaches outside the domain namespace.
- Tactical DDD patterns (Aggregates, Repositories, Domain Events) are only
  permitted inside `Grimoire.Domain`. Flag them if introduced in
  `Grimoire.AgentRuntime`, `Grimoire.Hub`, `Grimoire.IngestAgent`,
  `Grimoire.QueryAgent`, `Grimoire.LintAgent`, or `Grimoire.EvalRunner`.
- A new dependency on an external system (LLM provider API, spawned agent
  process, subprocess converter, outbound network fetch) MUST be consumed
  through a port interface declared in the consuming orchestration namespace,
  not constructed directly against the concrete adapter type. Flag direct
  `new SomeAdapter()` calls outside composition/DI wiring.
  - Exception: persistence and local filesystem adapters (repositories,
    artifact stores, projection stores) do not need a port — direct injection
    of the concrete class is fine there.
- Infrastructure packages (DB drivers, LLM SDKs, outbound HTTP clients) must
  stay confined to their designated adapter namespace. Flag an infrastructure
  package import inside a domain or orchestration namespace.
- A PR introducing a new structural boundary, integration pattern, or
  cross-cutting concern that isn't covered by an existing ADR needs a new ADR
  in `docs/adr/` (MADR format, `status: proposed` or `status: accepted`). If
  the diff clearly adds such a boundary without a corresponding ADR, flag it.

## 2. Testing style (Principle II)

Applies to: `backend/tests/**`, any new/changed test file

- Reject any reference to a mocking/interaction-verification framework (Moq,
  NSubstitute, FakeItEasy, etc.) in a test project — this project uses
  classicist (Chicago-school) TDD only.
- Assertions must be state-based (return values, persisted files/records, HTTP
  responses, emitted telemetry) — not interaction verification ("method X was
  called with Y").
- Test doubles are permitted only as hand-rolled fakes implementing an
  existing port interface (Principle I). A double introduced just to isolate
  an internal collaborator, or a port introduced solely to enable mocking, is
  a violation.
- Repository/filesystem/database tests must run against real infrastructure
  (temp directories, real child processes, the real embedded DB file) — a
  test that mocks the database for a repository implementation is a false
  negative and should be flagged.
- **Ownership test** — for every new or modified assertion, ask: *could this
  fail from a change to Grimoire's own source alone?* If only a dependency
  upgrade could turn it red (CLI-framework parsing, DI container resolution,
  serializer round-trip, web framework routing, config-binder behavior), it's
  testing library-owned behavior and should be removed or collapsed into a
  single minimal wire-up test named for the intent (e.g.
  `..._ReachesOurHandler`).
- Harness contracts (dispatch, credential scoping, guardrails, task-artifact
  lifecycle, operational state) must be tested deterministically, with no live
  LLM calls. Agent-judgment behavior must be verified by evaluation-style
  tests scored against thresholds (e.g. "≥ 90% of sampled runs..."), never by
  a 100%-pass hermetic test — flag a spec or test that pins agent judgment to
  a deterministic 100% guarantee.

## 3. ADR & structural-test discipline (Principle III)

Applies to: `docs/adr/**`, `specs/**/plan.md`, `specs/**/tasks.md`

- An Accepted ADR's Context/Decision/Consequences MUST NOT be edited in place
  to change what was decided — a changed decision needs a new ADR that
  supersedes or amends the old one, with a bidirectional status-header link
  on both ADRs (`Supersedes ADR-NNN` / `Superseded by ADR-NNN`, or
  `Amends`/`Amended by`). Flag any diff that rewrites an Accepted ADR's
  substance without adding a new ADR.
- `docs/adr/index.md` must be updated in the same change as any ADR whose
  status or existence changes.
- Boundary-Rule structural tests (reflection/IL/import-linter style) are only
  appropriate for durable dependency-direction rules, not for Feature-Scoped
  Invariants (a feature's current surface shape, e.g. "exactly N CLI
  switches"). Flag a new reflection-based test that pins a feature-surface
  fact — it belongs in a classicist, state-based integration test instead.
- `plan.md` must have a `## Architectural Constraints & ADRs` section listing
  which ADRs constrain the implementation.

## 4. Observability contracts (Principle IV)

Applies to: `backend/src/**`, `specs/**/plan.md`

- Every metric, structured log event, and trace span enumerated in a
  `plan.md ## Observability` section needs matching implementation and a
  deterministic integration test validating name/level/mandatory fields (logs)
  or name/parent-child/attributes (spans).
- An observability test must exercise the real composition root (real
  telemetry registration, sampler, exporter) — not a test-only
  always-on sampler or a listener hand-attached directly to an
  instrumentation class. Flag tests that stand up their own telemetry
  provider instead of using the production wiring.
- Logs/metrics emitted inside a span must be correlatable to it via a shared
  identifier (e.g. `task_id`).

## 5. Agentic core vs. deterministic harness (Principle V)

Applies to: `backend/src/**`, `backend/src/*/Instructions/**`

- Judgment about wiki content (which pages exist, what they say, update vs.
  create, supersession, categorization, confidence scoring, tagging,
  cross-referencing, index/log content) must live in agent instruction files
  (`system-prompt.md`, `default-user-prompt.md`), not in backend string
  matching, rule tables, classifiers, or content templating. Flag any PR that
  reimplements such judgment as deterministic C# code.
- Agent write/read capability must go through guarded tools enforcing a
  versioned, deny-by-default policy at the point of invocation — not as
  post-hoc validation of pipeline output. Flag agent-side code that writes to
  the wiki outside the guarded tool layer.
- A deterministic test may check only that an instruction file is loaded
  byte-exact, fails closed when missing/empty, and its hash is recorded —
  never that it contains or omits specific wording. Flag a test that
  string-matches required/forbidden content inside a real `system-prompt.md`
  or `default-user-prompt.md`.
- **Boundary smell test**: if a PR changes wiki-maintenance *behavior* by
  editing backend code — rather than adding a new tool or guardrail rule, or
  editing an instruction file — that's very likely a Principle V violation.
  Call it out explicitly.

## 6. Language & documentation hygiene

- All code, comments, and shared documentation must be in English (see
  `CLAUDE.md`). `dev-experience.md` is the one exception (personal log,
  German is fine there).
- `specs/<feature>/` artifacts and ADRs are the only documents that may be
  cited as binding requirements. Flag a plan/spec/ADR that cites
  `docs/befunde-remediation-prompts.md`, `docs/llm-wiki-*.md`,
  `docs/project-conversation.md`, or `dev-experience.md` as a source of
  requirements — those are source material only.

## What not to flag

- Don't ask for a Testcontainers dependency where none of the feature's
  dependencies are containerized (filesystem, embedded SQLite, spawned
  processes are all directly testable per Principle II).
- Don't ask for a Phase 0 structural test for a Feature-Scoped Invariant —
  those get a classicist behavioral test instead (Principle III).
- Don't flag pre-existing ADRs or already-merged specs against amendments
  made after they shipped (Governance: amendments are not retroactive).

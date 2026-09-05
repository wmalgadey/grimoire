---
name: code-review
description: Reviews pull request diffs in the Grimoire repository against the project constitution (.specify/memory/constitution.md, v2.0.1) — hexagonal/DDD boundaries, classicist TDD, ADR-driven architecture, observability contracts, the agentic-core-vs-deterministic-harness boundary, and the Spec-Kit workflow rules in CLAUDE.md. Use for any PR touching backend/src, backend/tests, docs/adr, specs, or .specify.
license: MIT
---

# Grimoire Code Review

Grimoire is an LLM harness whose product is a wiki maintained by agents. The binding rules are
`.specify/memory/constitution.md` (currently **v2.0.1**) plus the workflow and language rules in
`CLAUDE.md`. Read the relevant principle before judging a diff — the checklists below are an index
into those documents, not a substitute for their exact wording.

Check only the categories that apply to the files touched; skip the rest (a docs-only PR needs no
hexagonal-boundary check). Flag violations as review comments; do not rewrite the diff yourself.

**Before flagging anything, read "What not to flag" at the bottom.** Several rules below have
explicit carve-outs that the constitution names as reviewer false positives.

**Amendments are not retroactive** (Governance). A finding must be dated against the constitution
version in force when the artifact was authored. Do not flag an already-merged feature, or a
pre-existing ADR, for failing a rule added after it shipped.

## Repository layout

| Path | What it is |
| --- | --- |
| `backend/src/Grimoire.Domain` | Domain Core — dependency-free, the only place for tactical DDD |
| `backend/src/Grimoire.AgentRuntime` | Shared agent runtime library (ADR-044) + shared `Instructions/` |
| `backend/src/Grimoire.Hub` | Orchestrator: dispatch, CLI, API, operational state |
| `backend/src/Grimoire.{Ingest,Query,Lint}Agent` | The three agent processes, each with `Instructions/` |
| `backend/tests/Grimoire.ArchTests` | Structural boundary tests (Phase 0, reflection/IL level) |
| `backend/tests/Grimoire.IntegrationTests` | Primary verification tier — real infrastructure |
| `backend/tests/Grimoire.Domain.UnitTests` | Complex domain logic only |
| `backend/tests/Grimoire.AgentEvals`, `Grimoire.EvalRunner` | Agent-behavior evaluation (both are *test*-tree projects) |
| `backend/tests/Grimoire.WriteLockTestHarness` | Cross-process write-coordination harness |
| `frontend/` | Frontend app (ADR-026 governs its error presentation) |
| `docs/adr/` | ADRs + `TEMPLATE.md` + `index.md` |
| `specs/<feature>/` | Feature-scoped SDD artifacts |

## 1. Hexagonal boundaries & DDD (Principle I)

Applies to: `backend/src/**`

- `Grimoire.Domain` MUST NOT import from `Grimoire.AgentRuntime`, `Grimoire.Hub`, an agent project,
  or any infrastructure/framework package. Flag any `using` in `Grimoire.Domain` that reaches
  outside the domain namespace.
- Tactical DDD patterns (Aggregates, Repositories, Domain Events) are only permitted inside
  `Grimoire.Domain`. Flag them if introduced in `Grimoire.AgentRuntime`, `Grimoire.Hub`,
  `Grimoire.IngestAgent`, `Grimoire.QueryAgent`, or `Grimoire.LintAgent`.
- A new dependency on an external system (LLM provider API, spawned agent process, subprocess
  converter, outbound network fetch) MUST be consumed through a port interface **declared in the
  consuming orchestration namespace** — never in an infrastructure-only location, which would
  invert the dependency direction. Flag orchestration code that constructs or references the
  concrete adapter type directly (outside composition/DI wiring).
  - **Exception:** persistence and local-filesystem adapters (repositories, artifact stores,
    projection stores) do NOT need a port. Direct injection of the concrete class is correct there;
    per Principle II, introducing a port solely to make them mockable is itself the violation.
- Infrastructure packages must stay confined to their designated adapter namespace: DB drivers
  (`Microsoft.Data.Sqlite`) only in persistence adapters, LLM SDKs only in the model-client adapter,
  outbound HTTP only in the fetch adapter. Flag an infrastructure import inside a domain or
  orchestration namespace.
- A PR introducing a dependency on a **new** external system needs an ADR naming the port, its
  adapter namespace, and its containment rule, accepted *before* implementation. Flag such a diff
  that arrives without one.

## 2. Testing style (Principle II)

Applies to: `backend/tests/**`, any new or changed test

- **No mocking frameworks.** Reject any reference to Moq, NSubstitute, FakeItEasy, or equivalent in
  any test project. A PR introducing one is a signal to re-examine the boundary it wants to mock.
- **State-based assertions only** — returned values, persisted files/records, HTTP responses,
  emitted telemetry. Never interaction verification ("method X was called with Y").
- Test doubles are permitted **only** as hand-rolled fakes implementing an existing Principle I port
  (model client, agent process, converter, outbound fetch). A double introduced to isolate an
  *internal* collaborator is a violation.
- Repository/filesystem/database tests run against real infrastructure — per-test temp directories,
  real spawned child processes, real HTTP hosting, the real embedded SQLite file. A test that mocks
  the database for a repository implementation is a false negative and must be flagged.
- Unit tests are reserved for complex domain logic (non-trivial rules, entities with invariants,
  decision-making domain services). Flag a new dedicated unit test for a simple DTO, mapper, or
  pass-through adapter — integration tests cover those implicitly.
- **Ownership test** — for every new or modified assertion ask: *could this fail from a change to
  Grimoire's own source alone?* If only a dependency upgrade could turn it red (CLI-framework
  parsing, help/usage rendering, DI container resolution, serializer round-trip, web-framework
  routing, config-binder behavior), it is library-owned and must be removed or collapsed into **one**
  minimal wire-up test named for that intent (`..._IsRegisteredWith...`, `..._ReachesOurHandler`).
  A wire-up test that grows into a behavior matrix for the library is itself a violation.
  - Straddling tests are **rewritten, not deleted**: keep and sharpen the product-owned assertion,
    drop the library-owned ones. Deletion without replacement is correct only when nothing
    product-owned remains.
  - An assertion that is true by construction of the test's own setup rather than of the production
    code is not a test at all — flag it.
- **Harness contracts** (dispatch, credential scoping, guardrails, task-artifact lifecycle,
  operational state, channels) are tested deterministically and hermetically, with **no live LLM
  calls and no real API keys**. This bar is not lowered by anything below.
- **Agent judgment is tiered** (v1.12.0). The spec must classify each agent-judgment success
  criterion:
  - **High-stakes** — irreversible wiki writes, supersession or deletion of existing content,
    guardrail-adjacent decisions, anything the spec marks safety- or correctness-critical. These
    MUST be expressed as evaluation thresholds ("≥ 90% of sampled ingests choose update over
    duplicate creation") and gated by a formal eval suite.
  - **Lower-stakes** — day-to-day categorization, tagging, phrasing, update-vs-create calls whose
    cost of being wrong is one wiki edit correctable on a later pass. These MAY be narrative, with
    no numeric threshold, and are satisfied by the **user-reported correction loop** — a formal eval
    suite is OPTIONAL and MUST NOT be demanded in review.
  - An agent-judgment criterion left unclassified defaults to high-stakes — flag the missing
    classification.
  - A **100% deterministic guarantee attached to an agent-judgment outcome, of either tier, is a
    spec defect** — it structurally forces the implementation to replace the agent with
    deterministic code. Flag it.

## 3. ADR discipline (Principle III, v2.0.0 / v2.0.1)

Applies to: `docs/adr/**`, `specs/**/plan.md`, `specs/**/tasks.md`

**Format and scope**

- Every new ADR MUST use the skeleton in `docs/adr/TEMPLATE.md`, copied directly: all sections
  present, in order — Context and Problem Statement, Decision Drivers, Considered Options, Decision
  Outcome, Consequences, **Change Triggers**, More Information. Extra clarifying subsections beneath
  an existing heading are fine; removing, reordering, or renaming a required one is not.
- **Frontmatter contract.** `status` is REQUIRED and one of `proposed | accepted | declined |
  deprecated | superseded`. `supersedes` only when this ADR replaces another. `superseded_by` is an
  **array**, added once replaced. `reason` is REQUIRED whenever status leaves accepted/proposed. A
  field that does not apply is **omitted**, never written as `null` or empty.
- **Single-aspect ADRs.** Each ADR decides exactly one boundary or technology choice. If a plan
  surfaces two, that is two ADRs. An ADR MUST NOT fix feature-level requirements, behavior, scope,
  or implementation detail — those belong in `spec.md` / `plan.md` / `tasks.md`.
- The Decision Outcome MUST tag each rule it introduces as a **Boundary Rule** or a **Feature-Scoped
  Invariant**. Flag an ADR that enumerates rules without classifying them.

**Status changes**

- An Accepted ADR's Context/Decision/Consequences MUST NOT be edited in place to change what was
  decided — regardless of whether the new text is more accurate. Flag any diff that rewrites an
  Accepted ADR's substance instead of adding a new ADR.
- **Supersession is always whole-ADR.** Partial supersession and the old `Amends` / `Amended by`
  mechanism are **retired** for every ADR drafted from v2.0.0 forward. If any part of an Accepted
  ADR's decision changes, the whole ADR goes to `superseded`, and each still-valid aspect is
  re-decided as its own new single-aspect ADR — never inherited by reference, never patched in.
- The link is **bidirectional and in frontmatter**, not prose: the new ADR's `supersedes` names the
  old one, and the old ADR's `superseded_by` array is updated **in the same change**. A one-sided
  link MUST NOT ship — flag it.
- **Invalidation test** (decisive): would honoring the new requirement mean reversing, narrowing, or
  contradicting what the ADR actually decided? **No** → it is an *extension* (a new consumer of an
  existing port, an additional row in an adopted schema, a new switch on a ratified CLI surface):
  **no status change**, optionally an `Extends ADR-N` note. **Yes** → whole-ADR supersession. There
  is no third option.
- **Cross-reference notes** (v2.0.1) are informational prose, never frontmatter, and carry no status
  obligation: `Extends` / `Extended by` (reciprocal, added in the same change) and `Related:`
  (topical adjacency only). Format is **one** top-of-file blockquote per ADR headed `**Status
  notes** (informational, no status change):`, with one bullet per relationship — a second note adds
  a bullet to the existing block, never a new stacked blockquote. Flag a note used to paper over
  what the Invalidation test classifies as an invalidation.
- `docs/adr/index.md` MUST be updated in the same change as any ADR whose status or existence
  changes. ADR numbers are permanent — never reused, renumbered, or merged to "consolidate".

**Tests the ADR implies**

- **Boundary Rules** (dependency direction: which package/namespace/layer may depend on which) get a
  Phase 0 structural test in `Grimoire.ArchTests`, with a Red/Green probe — write the rule, add a
  deliberately violating class, verify red, delete it.
- **Feature-Scoped Invariants** (a feature's current surface shape — "exactly N CLI switches", "no
  literal duplicates a config default") get a classicist, state-based integration test exercising
  the real observable behavior instead. Flag a new reflection/IL/internal-catalog test that pins a
  feature-surface fact.
- A Feature-Scoped Invariant test MUST NOT assert a bare cardinality or literal enumeration as an
  end in itself. If the ADR's real concern is unbounded silent regrowth of a surface, the test must
  assert *that*, and the ADR must say growing the surface is a recognized single-file amendment.
- The Ownership Test (§2) is a **precondition** on both categories: a rule whose only failure mode
  is a config binder, CLI parser, or serializer behaving as documented gets no structural test at
  all — at most one wire-up test.
- A Boundary Rule named in an Accepted ADR with no structural enforcement test MUST NOT be cited as
  an active constraint.

**tasks.md structure**

- Phase 0 covers every Boundary Rule from `plan.md ## Architectural Constraints & ADRs`. Where the
  feature introduces none, Phase 0 MUST say so explicitly ("no Boundary Rule introduced by this
  feature") rather than being silently omitted.
- The final phase MUST include a named **completeness-audit task** cross-referencing every
  `plan.md ## Observability` row and every high-stakes agent-judgment criterion against its
  implementing task and passing test, confirming each lower-stakes criterion is covered by a
  hermetic test and recorded as subject to the correction loop, and filing any gap as a new task.
- `plan.md` MUST have a `## Architectural Constraints & ADRs` section listing which ADRs constrain
  the implementation and how.

## 4. Observability contracts (Principle IV)

Applies to: `backend/src/**`, `specs/**/plan.md`

- Every metric, structured log event, and trace span enumerated in `plan.md ## Observability` needs
  all three task categories: **implementation** (stable event/span name, declared mandatory fields
  or attributes and parent/child linkage), **deterministic integration test** (validating
  name/level/mandatory fields for logs; name/parent-child/correlation attributes for spans), and
  **CI enforcement** (these tests run in the standard PR pipeline). Flag a row missing any of them.
- **Contract tests must exercise the production wiring** — the same composition root the production
  process uses: real telemetry registration, real sampler, real exporter pipeline. Flag a test that
  stands up a test-only always-on sampler, hand-registers an `ActivitySource`, or attaches a
  listener directly to an instrumentation class. That proves the emitting line runs, not that the
  signal reaches an observer. (Feature 003 shipped green trace tests while the Hub exported nothing
  — the default `ParentBased` sampler dropped every request-path span.)
- Trace spans must exist as an end-to-end observable chain in code, not only in planning artifacts.
- Logs and metrics must be emitted inside the active span context and correlatable via a shared
  identifier such as `task_id`.
- **Operator loop** (Principle V): where a spec relies on the correction loop for a lower-stakes
  agent-judgment criterion, `plan.md ## Observability` MUST name the user-facing surface on which
  the user observes that signal. A signal emitted but consumable nowhere does not close the loop.

## 5. Agentic core vs. deterministic harness (Principle V)

Applies to: `backend/src/**`, `backend/src/*/Instructions/**`

- Judgment about wiki content (which pages exist, what they say, update vs. create, supersession,
  categorization, confidence scoring, tagging, cross-referencing, index/log content) must live in
  the versioned instruction files actually loaded into the agent's context — per ADR-053/ADR-054,
  the shared `foundation-prompt.md` composed with each agent's `system-prompt.md`, plus
  `default-user-prompt.md` where the agent has one. Flag a PR that reimplements such judgment as
  deterministic C#: string matching, rule tables, classifiers, or templating of page content.
- Loading, hashing, or recording an instruction file without it governing the agent's runtime
  context does NOT satisfy the principle.
- The harness's inputs — which task runs and which instruction files steer an agent — are
  user-supplied parameters. Flag harness code that special-cases or reinterprets their content.
- **Task artifacts are harness records.** The user supplies the task *request*, never a task-artifact
  file. The Hub alone owns their scaffolding (structure, frontmatter, lifecycle); the narrative
  content is agent-authored and passed through verbatim. Flag any design that expects the user to
  author or deliver a task artifact.
- **Durable state lives in files.** The wiki, task artifacts, and harness records persist as files.
  The embedded SQLite database holds only the Hub's internal operational state — deleting it MUST
  lose no wiki content, no task artifact, no harness record. Flag a change that makes durable
  content depend on the database.
- Agent write/read capability must go through guarded tools enforcing a versioned, deny-by-default
  policy **at the moment of invocation** — not post-hoc validation of pipeline output. Denied
  actions are recorded with reasons; the run continues with allowed actions. Flag agent-side code
  that writes to the wiki outside the guarded tool layer.
- **Host stability guarantee.** A feature that spawns or reconfigures agent processes must prove, by
  hermetic test, that the agent cannot write outside its designated roots or launch subprocesses
  beyond its guarded tools' scope — and this must hold regardless of instruction-file content. An
  instruction file MUST NOT be able to loosen it. This is a containment guarantee (*where* the agent
  may act), proven deterministically, never by an eval.
- A deterministic test may check only that an instruction file is loaded byte-exact, fails closed
  when missing or empty, and has its hash recorded — **never** that it contains or omits specific
  wording. Flag a test that string-matches required or forbidden content inside a real
  `foundation-prompt.md`, `system-prompt.md`, or `default-user-prompt.md`. Instruction-file content
  is never a Feature-Scoped Invariant.
- **Boundary smell test**: if a PR changes wiki-maintenance *behavior* by editing backend code —
  rather than adding a tool, adding a guardrail rule, or editing an instruction file — that is very
  likely a Principle V violation. Call it out explicitly.

## 6. Spec-Kit workflow discipline (CLAUDE.md)

Applies to: `specs/**`, PR shape

- **A requirements change is made on the spec layer via `/speckit-clarify`, never as a direct edit.**
  Planning or task work routinely surfaces a real problem with what the spec asks for; the fix
  belongs in `spec.md`, recorded as a dated `## Clarifications` entry with Recommended/options
  framing and a checklist re-validation. Flag: a raw `spec.md` rewrite reacting to plan feedback
  with no Clarifications entry, and a `plan.md`/`tasks.md` patch that encodes a requirement
  `spec.md` still does not state. Layers above a corrected spec are rebased and regenerated, not
  hand-patched to match.
- **Delivery shape is decided out loud between `/speckit-tasks` and `/speckit-implement`.** Where
  `tasks.md` has more than two phase groups beyond Phase 0, the default is a stack of small PRs
  (one layer per phase group). `tasks.md`'s Implementation Strategy section must state the shape
  actually delivered. Flag a tasks.md that describes a cut nobody built — a recorded intent that
  implementation ignored reads like the decision was made (Feature 024 shipped 64 tasks as one
  72-file PR after recording a four-way cut). "Single PR" is a legitimate answer; it just has to be
  written down as the answer, with a reason.
- Do not demand a retro-split of a PR already under review — that discards the review rather than
  shortening it.

## 7. Language & documentation hygiene (CLAUDE.md)

- All code, comments, and shared documentation in **English**. `dev-experience.md` is the one
  file-level exception (personal German log; also never citable as a requirement).
- **Verbatim user input is a record, not authored content.** A block quoting what the user actually
  said — `spec.md`'s `**Input**: User description:` field, a quoted clarification answer, an issue
  excerpt — MUST be preserved unedited in the language the user used. Everything *derived* from it
  (requirements, scenarios, acceptance criteria, surrounding prose) must be English.
- Binding-document hierarchy — only `.specify/memory/constitution.md`, `specs/<feature>/` artifacts,
  and **Accepted** ADRs may be cited as requirements. Flag a spec/plan/ADR citing
  `docs/foundational/decision-context-overview.md` (North Star — binding only via extraction into
  the constitution or an ADR), `docs/foundational/llm-wiki-*.md`, `docs/ideas/*.md` (including
  `befunde-remediation-prompts.md`, `project-conversation.md`), or anything else under `docs/` as a
  source of requirements. Where a file matches both a specific row and the `docs/**` catch-all, the
  specific row wins — an Accepted ADR stays binding.
- A new document needs a declared reader (which process step consumes it?). If none exists, the
  content belongs in `dev-experience.md`, not a new file.
- Markdown prose wraps to roughly 90–100 characters for diff readability. This is an **orientation,
  not a hard cap** — never flag a line for running slightly long, and never ask for a break that
  fractures a phrase mid-clause. Content dense with copied/grepped/piped material (task artifacts,
  log entries, generated records, verbatim quotes) is correctly left unwrapped, one line per
  paragraph.

## What not to flag

- **Do not ask for an eval suite for a lower-stakes agent-judgment criterion.** The constitution is
  explicit: reviewers MUST NOT request one solely because a criterion involves an LLM. The
  user-reported correction loop is a sufficient and legitimate verification path there.
- **Do not ask for resource governance in the harness.** CPU, memory, disk, and process-count
  ceilings are deliberately out of the host stability guarantee's scope — a deployment concern for a
  container or OS-level sandbox. The harness's only obligation is exposing consumption through the
  Principle IV signals, never enforcement.
- **Do not ask for a Testcontainers dependency** where nothing is containerized. Filesystem,
  embedded SQLite, and spawned processes are directly testable, and the constitution forbids adding
  a container merely to satisfy a tooling name.
- **Do not ask for a port** around a persistence or local-filesystem adapter. That is the explicit
  Principle I exemption, and adding one to enable mocking violates Principle II.
- **Do not ask for a Phase 0 structural test for a Feature-Scoped Invariant** — those get a
  classicist behavioral test instead.
- **Do not ask for supersession when the Invalidation test says "extension."** Using more of an
  already-decided boundary changes no ADR's status.
- **Do not flag a non-English verbatim `**Input**` block** or other quoted user input. CLAUDE.md
  names this a false positive; the rule to apply is "is it marked as a quote, and is everything
  derived from it English?"
- **Do not flag pre-existing ADRs or already-merged specs against later amendments.** Pre-v2.0.0
  `Amends` / `Amended by` chains and prose-only status headers in older ADRs are historical record.
  The final-phase completeness-audit task is absent from specs 001–009 by construction, not by
  defect.
- **Do not flag pre-existing tests that merely restate library behavior.** The Ownership Test binds
  new and modified tests; older ones should be collapsed into a wire-up test the next time their
  file is touched for other reasons.

<!--
SYNC IMPACT REPORT
==================
Version change: 1.12.0 → 2.0.0 (2026-08-25)

Principles modified:
  - III. ADR-Driven & Test-Enforced Architecture (NEW rule "Single-aspect ADRs; no
    feature content": every ADR MUST decide exactly one system boundary or technology
    choice; where a plan.md surfaces more than one such decision, the agent MUST draft
    one ADR per aspect rather than a single combined record; ADRs MUST NOT fix
    feature-level requirements, behavior, or implementation detail. "ADR Status
    Maintenance" REDEFINED: the `Amends`/`Amended by` link is retired for ADRs drafted
    from this date forward — superseding an ADR now always invalidates the ENTIRE ADR,
    never a section, table row, or "in part" scope; any still-valid aspect of a
    superseded ADR MUST be re-decided as its own new, independent, single-aspect ADR in
    the Mandatory ADR format, never inherited by reference from or patched into the
    superseded text. NEW rule "Extension is not invalidation," with a decisive
    Invalidation test (would honoring the new requirement reverse, narrow, or contradict
    what the ADR actually decided?) distinguishing extensions — which MUST NOT touch the
    original ADR's status, not even in part — from invalidations, which MUST go through
    whole-ADR supersession; drafting an ADR now requires recording, under its own
    Decision Outcome / Change Triggers, which future changes are anticipated extensions
    versus anticipated invalidations, so the distinction is made at drafting time rather
    than inferred later. NEW "Mandatory ADR format": every ADR drafted from this date
    forward MUST use the specified MADR-based skeleton verbatim, including a required
    "Change Triggers" section listing anticipated extensions and invalidations.)

Principles added: none

Sections modified:
  - Definition of Done (the ADR-status-header checkbox now requires the Mandatory ADR
    format, exactly one decided aspect per touched ADR, and whole-ADR — never partial —
    supersession wherever an ADR is touched)

Sections removed: none

Templates assessed (none modified — /speckit-constitution writes only the constitution;
dependent templates read it at runtime):
  - .specify/templates/plan-template.md ✅ no change required (the existing "Agent MUST
    read all ADRs" / "draft a new ADR for any structural boundary not covered" gate
    already routes a plan surfacing several boundaries toward several ADRs; this
    amendment makes that mandatory rather than incidental, with no template shape change)
  - .specify/templates/tasks-template.md ✅ no change required (Phase 0 / ADR-reference
    handling is unaffected by how an ADR records its own supersession)
  - .specify/templates/spec-template.md ✅ no change required (specs stay tech-agnostic
    and name no ADRs)
  - .specify/templates/checklist-template.md ✅ no change required

Rationale for MAJOR bump: this redefines, backward-incompatibly, how an existing governance
mechanism works. The v1.9.0/v1.10.0 amendments made ADR supersession bidirectional but still
explicitly permitted it to be partial ("Supersedes ADR-003 (in part)", "Amends ADR-002,
ADR-004, ADR-006, ADR-007, ADR-008" — see `docs/adr/index.md` as it stood before this
change). This amendment forbids that pattern outright for every ADR drafted from now on:
`Amends`/`Amended by` MUST NOT appear in a new ADR's status header, and what was previously
recorded as a partial amendment or partial supersession must instead be a whole-ADR
supersession plus new, single-aspect ADRs for whatever remains valid. A drafting practice
that was previously compliant — amend part of an Accepted ADR, leave the rest standing — is
now a violation when repeated going forward: exactly the "incompatible principle
removal/redefinition" the Governance section reserves for MAJOR, not MINOR.

Trigger (2026-08-25, user request, German-language input): the user observed that ADRs are
currently being adjusted "in Teilen" (in parts) often enough that it measurably reduces
their usefulness and clarity. `docs/adr/index.md` bears this out at the time of this
amendment: ADR-002 carries four partial "Amended by" entries, ADR-009 combines a partial
"Supersedes ADR-003 (in part)" with five partial "amends," and ADR-022 both amends five
ADRs and partially supersedes a sixth — understanding what any one of these currently
decides requires reading its full amendment chain rather than one Accepted document. The
user asked for four things, all incorporated: (1) no more partial amendment or
supersession — if any part of an ADR must be invalidated, the whole ADR is invalidated and
new ADRs are drafted for whatever still stands, in full MADR format; (2) each ADR MUST
cover exactly one aspect, so that "the whole ADR" is a meaningful, narrow unit to
invalidate; (3) the existing "ADRs only for genuine system boundaries or technology
decisions" scope stays unchanged, and ADRs MUST NOT encode feature-level requirements or
aspects of a feature; (4) an extension of already-decided functionality MUST NOT
invalidate the ADR, not even in part — operationalized as the Invalidation test and the
requirement to record anticipated extensions/invalidations at drafting time, which is also
the user's requested "basis for ADRs considers the reason for possible changes." The
existing `Extends` convention already informally used in `docs/adr/index.md` (ADR-016,
ADR-017, ADR-026) modeled part of this distinction; this amendment makes it binding. The
user also asked for a mandatory ADR format template; per this command's own Scope Guard
("Write only .specify/memory/constitution.md; do not create or modify template source
files"), the format is specified inline in the constitution itself (see "Mandatory ADR
format" under Principle III) rather than as a new file under `.specify/templates/` or
`docs/adr/` — `/speckit-constitution` does not create source files outside the
constitution.

Deferred TODOs: retroactively splitting the ADRs that currently carry partial
Amends/Supersedes links (per `docs/adr/index.md`: at least ADR-002, ADR-003, ADR-004,
ADR-006 through ADR-013, ADR-016 through ADR-018, ADR-020, ADR-022 through ADR-024,
ADR-028 through ADR-033) into single-aspect, whole-ADR-supersession form is real
ADR-authoring work, not a constitution change, and is out of scope for this amendment
(Scope Guard). Governance's non-retroactivity clause already grandfathers them as
historical record; a follow-up ADR-restructuring pass is recommended but not mandated —
see Next Actions in this command's own output.

--------------------------------------------------------------------------
PREVIOUS AMENDMENTS
--------------------------------------------------------------------------

Version change: 1.11.0 → 1.12.0 (2026-08-24)

Principles modified:
  - II. Pragmatic Testing Strategy ("Harness contracts vs. agent behavior" no longer
    mandates a formal evaluation-style test suite for every agent-judgment success
    criterion. Agent-judgment criteria are now classified high-stakes (large/hard-to-
    reverse blast radius — irreversible writes, supersession/deletion, guardrail-adjacent
    decisions) or lower-stakes (day-to-day categorization/tagging/phrasing/update-vs-
    create calls). Only high-stakes criteria require a formal eval suite gating the DoD.
    NEW subsection "User-reported correction loop": for lower-stakes criteria, the user
    observing agent misbehavior — via the Hub's observability signals (Principle IV)
    and/or the wiki output itself — having the agent's instruction files adjusted, and
    verifying the fix themselves is a sufficient, legitimate verification path — no
    CI-gated automated eval threshold required. "Success-criteria split" now requires
    specs to classify each agent-judgment criterion into one of the two tiers (defaulting
    to high-stakes if unclassified); lower-stakes criteria may be expressed narratively
    instead of as a numeric threshold. Harness-contract testing (dispatch, credential
    scoping, guardrail enforcement, task-artifact lifecycle, persistence, observability)
    is explicitly reaffirmed as exhaustive and unchanged — this amendment recalibrates
    only the agentic half of Principle II, per Principle V's agentic-core framing.)
  - III. ADR-Driven & Test-Enforced Architecture ("Agent-behavior evaluation tests" now
    gate the DoD only for high-stakes agent-judgment criteria; lower-stakes criteria are
    satisfied by the user-reported correction loop and MUST NOT be required to have a
    formal eval suite. The final-phase completeness-audit task now cross-references
    high-stakes criteria against passing eval tests and separately confirms lower-stakes
    criteria are covered by a hermetic test and recorded as subject to the correction
    loop.)
  - V. Agentic Core & Deterministic Harness ("Deterministic harness" bullet expanded:
    tasks (ingest/query/lint/future types) and instruction files are now explicitly named
    as user-supplied parameters, not harness-authored logic, and the Hub is named as the
    concrete orchestrator responsible for supplying each run's parameters and for
    observability. NEW bullet "Host stability guarantee": regardless of task or
    instruction content — including malformed or adversarial content — the harness MUST
    ensure the agent process cannot corrupt the host by moving outside the guarded tool
    boundary/credential scope already in force (writes outside its designated roots,
    subprocesses beyond what its guarded tools require); an instruction file MUST NOT be
    able to loosen this, and it MUST be proven by hermetic tests, never by an
    agent-behavior evaluation. Resource governance (CPU/memory/disk/process-count
    ceilings) is explicitly out of scope — a deployment concern addressed by container/
    sandbox isolation (per ADR-002's own deferred direction), not a harness
    responsibility; the harness's obligation there is monitoring/observability only,
    never enforcement. NEW bullet "Human in the loop (operator loop)": the user is the fourth actor —
    supplying input (task requests, instruction files) and consuming observability through
    user-facing surfaces (frontend today; channels and OTel dashboards as they arrive) to
    evaluate and steer agent behavior; Principle IV signals MUST be reachable through at
    least one such surface, and where a lower-stakes criterion relies on the correction
    loop, plan.md ## Observability MUST name the surface the user observes it on. The
    user's evaluation is judgment, not a gate to automate away. NEW bullet "Task artifacts
    and durable state": the user supplies task requests, never task-artifact files — the
    Hub alone prepares/maintains task-artifact scaffolding for run tracking and storage,
    with agent-authored narrative passed through verbatim; durable state lives in files
    (wiki, task artifacts, harness records), and the embedded state database holds only
    the Hub's internal runtime operational state and MUST NOT be required for durable
    persistence — deleting it MUST lose no wiki content, task artifact, or record. Both
    name and make enforceable the existing practice: OperationalStateRepository is the
    only SQLite consumer, every artifact store writes markdown files.)

Principles added: none

Sections modified:
  - Definition of Done (the agent-behavior-evaluation checkbox now scopes the mandatory
    eval-suite requirement to high-stakes agent-judgment success criteria and requires
    lower-stakes criteria to be documented as covered by the user-reported correction
    loop instead — including, per the operator loop, that plan.md ## Observability names
    the user-facing surface each such criterion's signal is observed on; the Principle V
    agentic-boundary checkbox now also requires that any feature spawning or
    reconfiguring agent processes proves the host stability guarantee via a hermetic
    test of write- and subprocess-containment, not resource limits)

Sections removed: none

Templates updated:
  - .specify/templates/spec-template.md ✅ (Success Criteria ACTION REQUIRED block now
    instructs classifying each agent-judgment criterion as high-stakes or lower-stakes,
    with lower-stakes criteria expressible narratively rather than as a threshold)
  - .specify/templates/plan-template.md ✅ (Test Strategy ACTION REQUIRED block and table
    now route lower-stakes agent-judgment criteria to a hermetic test plus the
    user-reported correction loop instead of a mandatory evaluation run; high-stakes
    criteria keep the evaluation-with-threshold mapping unchanged)
  - .specify/templates/tasks-template.md ✅ (the Agent-behavior evaluation completeness
    audit task now scopes to high-stakes criteria and adds confirming lower-stakes
    criteria are covered by the correction loop)
  - .specify/templates/plan-template.md ✅ (additionally: the Observability ACTION
    REQUIRED block now instructs naming the user-facing surface for every signal a
    correction-loop-covered criterion relies on, per the Principle V operator loop)
  - .specify/templates/checklist-template.md ✅ no change required
  - (The Host stability guarantee, Principle V, needed no template change: it is a
    harness/dispatch-scoped rule, not a per-feature success-criteria concern these
    templates route; it will surface in a future dispatch-touching feature's own
    plan.md Architectural Constraints & ADRs section and Test Strategy table like any
    other harness contract, using the templates' existing, unchanged machinery for
    deterministic guarantees.)

Rationale for MINOR bump: this amendment does two related things in one pass: (1)
narrows the blanket "every agent-judgment criterion needs a formal eval" default in
Principle II to a stakes-based tier, adding a new recognized verification path (the
user-reported correction loop, anchored to the Hub's observability signals); and (2)
adds a new enforceable rule to Principle V (Host stability guarantee) plus expands its
Deterministic harness bullet to name user-supplied inputs and Hub responsibilities
explicitly. Neither removes or redefines a principle. Not MAJOR: nothing previously
forbidden becomes required and no principle is deleted; a PR that already ships a formal
eval suite for a lower-stakes criterion remains fully compliant (the eval suite is now
optional there, not disallowed), and the Agentic core, Guardrails at the tool boundary,
Boundary smell test, and Structural enforcement bullets of Principle V are unchanged
verbatim. The deterministic-harness testing mandate (Principle II's other half, and
Principle III/IV in full) is explicitly reaffirmed unchanged throughout, so this is a
recalibration/expansion of two related sub-rules, which the Governance versioning rule
places at MINOR — consistent with the project's own precedent (v1.10.0→1.11.0 similarly
narrowed which ADR rules require a structural test via a new tiering, as a MINOR bump).

Trigger (2026-08-24, user request, two related asks in the same PR): the project was
generating agent-behavior evaluation suites for every agent-judgment success criterion
regardless of what was actually at stake, burning development cost (tokens, review time,
eval maintenance) disproportionate to the risk being verified. The user asked for the
constitution to reaffirm that Grimoire is fundamentally an agentic system whose judgment
is expected to be corrected by adjusting instruction files rather than pinned down
exhaustively in tests, to recognize a direct user-feedback-and-instruction-adjustment
loop as a legitimate, cheaper verification path for lower-stakes judgment, and to keep
rigor concentrated on the deterministic harness and orchestrator (Hub) — dispatch,
credential scoping, guardrail enforcement, task-artifact lifecycle, persistence,
observability — which stays at its existing, unweakened bar. In a follow-up message on
the same PR, the user asked the constitution to state plainly that the agent alone owns
the wiki, that the harness/Hub's job is limited to steering agents, guardrails, and the
tools an agent may use, that tasks and instructions are always user-supplied input
regardless of their content, and — new ground — that whatever an instruction or task
says, the harness must still guarantee the LLM cannot corrupt the host, with the Hub
responsible for feeding the harness correct parameters and for monitoring/observability
rather than for 100%-style tests of agent behavior. A research pass over the codebase
before drafting the Host stability rule found `AgentProcessHost`
(backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs) spawns
Ingest/Query/Lint/Lint-remediation agent processes via a plain `Process.Start`, and that
ADR-002 explicitly defers process containment to a later deployment change. The first
draft of this rule (and the `/speckit-specify` follow-up it recommended, spec
`027-host-stability`) read that gap as a resource-quota gap and drafted
operator-configurable CPU/memory/disk/wall-clock/process-count ceilings enforced by the
harness itself. On review the user corrected this: ADR-002's deferred containment IS the
project's answer to resource governance — a container or comparable OS-level sandbox
around the agent process, a deployment concern — and a harness that reimplements
CPU/memory/disk quota enforcement duplicates what that sandbox already provides. What the
harness itself owns is containment: the agent process must not be able to write outside
its designated roots or launch subprocesses beyond what its guarded tools require,
regardless of task or instruction content. This amendment's Host stability guarantee is
worded to that corrected scope and, per Governance's non-retroactivity clause, gates
`/speckit-plan` runs from this date forward; monitoring resource consumption (never
enforcing limits on it) remains a Hub/observability obligation per the operator loop.
Spec `027-host-stability` (opened before this correction, as the base of a
since-drafted implementation stack) is revised separately to match. A third ask on the
same PR added the human-in-the-loop framing: the user
wanted the operator named as the fourth actor (input, Hub communication, and monitoring
happen through the frontend — later channels and OTel dashboards — and that is where the
observability for evaluating and steering the agent must be consumable), an explicit
statement that the user is never expected to deliver task-artifact files (the Hub alone
prepares them, for run tracking and storage), and the state database pinned to Hub-internal
runtime state only, never required for durable persistence. Verified against the code
before writing: OperationalStateRepository (hub_flags, queue state) is the sole SQLite
consumer, while task artifacts, findings reports, and remediation records all persist as
markdown files — the rule names existing practice rather than mandating a migration.

Deferred TODOs: proving the Host stability guarantee (hermetic tests that the agent
process cannot write outside its designated roots or launch subprocesses beyond its
guarded tools' scope, even under malformed/adversarial task or instruction content) is
real backend work, not a constitution change, and is out of scope for this amendment (see
Scope Guard in /speckit-constitution). Recorded as a deferred, non-governance intent —
tracked in the revised spec `027-host-stability`.

--------------------------------------------------------------------------
PREVIOUS AMENDMENTS
--------------------------------------------------------------------------

Version change: 1.10.0 → 1.11.0 (2026-08-11)

Principles modified:
  - II. Pragmatic Testing Strategy (NEW bullet under "Test what we own": the
    Ownership Test is now an explicit precondition on Principle III's Phase 0
    structural tests and Feature-Scoped Invariant tests, not just standalone
    guidance next to them)
  - III. ADR-Driven & Test-Enforced Architecture (Structural boundary tests are
    now scoped to a NEW category, "Dependency & Layering Boundary Rules" —
    durable dependency-direction rules in Principle I/V's family. A second NEW
    category, "Feature-Scoped Invariants," is split out for ADR/plan rules that
    protect one feature's current surface shape (option counts, forbidden
    literals, options-graph shape) rather than a dependency direction: these
    MUST NOT get a Phase 0 reflection/IL-based structural test, MUST be tagged
    as such in the ADR's Decision Outcome, and MUST be covered by a classicist
    behavioral test instead. "The first task MUST be a structural boundary
    test" and "an ADR without an enforcement test MUST NOT be referenced as an
    active constraint" are both re-scoped to Boundary Rules only.)
  - V. Agentic Core & Deterministic Harness (NEW carve-out: a deterministic
    test verifies only an instruction file's load mechanism — existence,
    byte-exact loading, fail-closed behavior, hash recording — never the
    wording or substance of its content; content correctness is exclusively
    Principle II's agent-behavior evaluation tests' job)

Principles added: none

Sections modified:
  - Definition of Done (the Phase 0 checkbox now names Dependency & Layering
    Boundary Rules specifically and adds that Feature-Scoped Invariants use
    classicist tests instead; NEW checkbox that no deterministic test asserts
    instruction-file content)

Sections removed: none

Templates updated:
  - .specify/templates/tasks-template.md ✅ (Phase 0 section: ACTION REQUIRED
    block now instructs classifying each ADR rule as a Boundary Rule or a
    Feature-Scoped Invariant before writing T000; Feature-Scoped Invariants are
    pointed at their normal implementation phase, not Phase 0)
  - .specify/templates/plan-template.md ✅ no change required (its
    "Architectural Constraints & ADRs" section already just lists which ADRs
    constrain the feature and how; the classification happens inside the ADR
    itself, per the Principle III rule, not in this section's shape)
  - .specify/templates/spec-template.md ✅ no change required (specs stay
    tech-agnostic and name no ADR rules)
  - .specify/templates/checklist-template.md ✅ no change required

Rationale for MINOR bump: this narrows which ADR rules receive a mandatory,
permanent, reflection/IL-based structural test — a materially new constraint
on how Principle III is satisfied, not a wording clarification — while
strengthening (not weakening) the underlying guarantee: Boundary Rules keep
their Red/Green-probed enforcement verbatim, and Feature-Scoped Invariants
trade a brittle structural proxy for a classicist test of the actual protected
behavior. Not MAJOR: no principle is removed or redefined, and nothing that
was previously forbidden becomes permitted — mocking, interaction-based
verification, and library-behavior testing remain banned exactly as before.

Trigger (2026-08-11, PR #70 / 022-memory-directory-root, review
https://github.com/wmalgadey/grimoire/pull/70#pullrequestreview-4909924868):
implementing ADR-024's four named rules (M1-M4) under the prior, undifferentiated
wording of Principle III produced four reflection/IL-based ArchTests
(`DirectorySwitchSurfaceRuleTests` pinning "exactly 4 switches",
`NoCodeLevelPathDefaultsRuleTests` IL-scanning for two literal strings,
`PathOptionsGroupingRuleTests` pinning the exact options-graph shape) plus one
lexical content test of the real `system-prompt.md` files
(`InstructionFilesWikiScopeTests`) that duplicates coverage the project's own
agent evals already provide. The PR author's review comments on all four files
converged independently on the same diagnosis before this amendment was
drafted: these are feature-scoped facts expected to change as the feature
grows, not durable architectural boundaries, and Principle III's blanket
per-ADR-rule mandate — with no ownership-test gate and no distinction from
Principle I's dependency-direction family — was what generated them.

Deferred TODOs: PR #70 (unmerged as of this amendment) still carries the four
now-recategorized tests; they are not grandfathered by the non-retroactivity
clause (the PR has not merged), so the PR SHOULD be updated to reclassify
ADR-024's M1-M4 rules and replace the reflection/IL tests with classicist
behavioral tests before merge. Not performed as part of this amendment because
PR #70 is a separate, in-flight change on another branch.

--------------------------------------------------------------------------
PREVIOUS AMENDMENTS
--------------------------------------------------------------------------

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
required going forward. The user separately requested the retroactive
migration in the same change set: ADR-001 through ADR-023 were reviewed for
existing one-sided supersede/amend prose, and every reverse pointer found
missing was added (see `docs/adr/index.md` and each affected ADR's own status
header). This is a one-time migration explicitly requested alongside the
amendment, not a standing exception to Governance's non-retroactivity clause —
future amendments still do not obligate retrofitting past ADRs.

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
  hermetically, and exhaustively — this is where verification rigor concentrates. Harness
  tests MUST NOT require live LLM provider calls or real API keys, and nothing in this
  principle lowers the bar for them.
- **Agent behavior** (judgment exercised by an LLM under instruction files) is verified
  proportionately to its stakes, not exhaustively by default. Grimoire is, at its base,
  an agentic system (Principle V): agent judgment is expected to vary and be imperfect,
  and the primary way it improves is by adjusting instruction files, not by pinning every
  outcome down like a deterministic contract. Formal evaluation-style tests (sampled runs
  against real or recorded LLM output, scored against defined quality thresholds) are
  REQUIRED only for **high-stakes** agent judgment — decisions with a large or
  hard-to-reverse blast radius: irreversible wiki writes, supersession or deletion of
  existing content, guardrail-adjacent decisions, or any criterion the spec marks safety-
  or correctness-critical. For **lower-stakes** agent judgment — day-to-day
  categorization, tagging, phrasing, or update-vs-create calls whose cost of being wrong
  is a single wiki edit correctable on a later pass — a formal eval suite is OPTIONAL;
  the user-reported correction loop below satisfies this principle instead.

**User-reported correction loop.** For lower-stakes agent judgment, the following is a
sufficient and legitimate verification path in place of a mandatory automated eval suite:
the user observes the agent's actual behavior — via the Hub's observability signals
(Principle IV's business metrics, structured log events, and trace spans), consumed
through a user-facing surface per Principle V's operator loop, and/or the wiki output
itself — reports the specific misbehavior to the agent, the agent's own
instruction files (`system-prompt.md` / `default-user-prompt.md`, per ADR-007) are
adjusted in response, and the user — not an automated gate — verifies afterward whether
the adjustment worked. This loop is deliberately informal and human-in-the-loop; it relies
on the Hub making behavior visible (Principle V's Hub responsibilities), not on a
dedicated new artifact. It does NOT require a CI-gated automated eval threshold to count
as done for lower-stakes criteria, and it does NOT substitute for a formal eval suite
where the criterion is classified high-stakes above.

A feature whose value lies in high-stakes agent judgment and that ships with only
hermetic tests is NOT covered — the hermetic-test mandate for the harness MUST NOT be
used as a reason to reimplement high-stakes agent judgment as deterministic code so it
becomes unit-testable. For lower-stakes agent judgment, hermetic harness tests plus the
user-reported correction loop above ARE sufficient coverage; adding a formal eval suite
there anyway is permitted but never required, and reviewers MUST NOT ask for one solely
because a criterion happens to involve an LLM.

Rationale: eval suites are expensive — they burn tokens on sampled LLM runs and add
review/maintenance surface — and the prior wording made them mandatory for every
agent-judgment criterion regardless of what was actually at stake, which drove eval
spend disproportionate to the risk being verified. This does not weaken verification of
the part of the system that is cheap and cost-effective to verify exhaustively: the
deterministic harness keeps its hermetic-test mandate unchanged. It recalibrates only the
agentic half, where Grimoire's own architecture (Principle V) already accepts that
judgment is exercised by an LLM under instructions rather than pinned-down code, and
where a human noticing and correcting a bad instruction is a legitimate, cheaper
verification path than an automated eval for anything short of high-stakes judgment.

**Success-criteria split.** Every spec MUST express harness success criteria as
deterministic guarantees (100%) and MUST classify each agent-judgment success criterion
as **high-stakes** or **lower-stakes** per the tiering above. High-stakes criteria MUST
be expressed as evaluation thresholds (e.g., "≥ 90% of sampled ingests choose update over
duplicate creation") and gate the DoD via a formal eval suite. Lower-stakes criteria MAY
be expressed narratively (the expected behavior, without a numeric threshold) and are
satisfied by the user-reported correction loop rather than a mandatory eval suite. An
agent-judgment success criterion that is not explicitly classified defaults to
high-stakes. A 100% deterministic guarantee attached to an agent-judgment outcome, of
either tier, is a spec defect: it structurally forces the implementation to replace the
agent with deterministic code.

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
- **Precondition for Principle III structural tests.** This Ownership Test MUST be applied
  before writing any Phase 0 structural boundary test or Feature-Scoped Invariant test
  (Principle III) — it is a precondition on those tests, not general guidance alongside
  them. A rule whose only observable failure mode is a configuration binder, CLI-parsing
  library, or serializer behaving as documented MUST NOT receive a dedicated structural or
  reflection-based test; cover it, if at all, via the one minimal wire-up test above.

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
concern not covered by existing ADRs, the agent MUST draft a new ADR in the Mandatory ADR
format (below) in `docs/adr/` as part of the `/speckit-plan` output. The drafted ADR MUST
reach **Accepted** status (via review or explicit author sign-off) before `/speckit-tasks`
is invoked.

**Single-aspect ADRs; no feature content.** Each ADR MUST decide exactly one aspect — one
genuine system boundary or one technology choice — per the existing "ADRs only for real
system boundaries or technology decisions" scope (Principle I's `New boundaries via ADR`;
Principle IV's infrastructure-approval rule). If `plan.md` surfaces more than one such
decision, the agent MUST draft one ADR per aspect rather than folding them into a single
combined record. An ADR MUST NOT fix feature-level requirements, behavior, scope, or
implementation detail — those live in `spec.md`, `plan.md`, and `tasks.md`; an ADR records
only the durable boundary or technology decision a feature happens to surface, never the
feature itself.

Four distinct categories of tests enforce architectural rules, with different preconditions:

**Structural boundary tests** (Phase 0 — before feature code) apply to **Dependency &
Layering Boundary Rules** only: rules about which package, namespace, or layer may
depend on, import, or construct which other (Principle I's domain-purity and
adapter-containment family; Principle V's guarded-write boundary). These describe a
durable dependency *direction* that holds regardless of how any one feature's surface
grows — the reason a permanent, reflection/IL-level, Red/Green-probed test is
low-maintenance by construction for this category and only this category.

Tools: ArchUnit, NetArchTest.Rules, Roslyn Analyzers, import-linter, or equivalent.
These rules are static: "domain layer MUST NOT import infrastructure." On a greenfield
codebase they pass vacuously (no code = no violations). To confirm the rule actually
detects violations, the Phase 0 task MUST: write the rule, introduce a deliberately
bad class that violates it, verify the test fails, then delete the bad class. This
controlled Red/Green proves the guard is live. Feature code written afterward is
protected by the rule without any further action.

**Feature-Scoped Invariants** are a distinct category and MUST NOT default to a Phase 0
structural test. These are ADR/plan rules that protect one feature's *current surface
shape* rather than a dependency direction — e.g. "the CLI exposes exactly N named path
switches," "no code-level literal duplicates a config default," "the options graph
mirrors the config file's grouping." Unlike Boundary Rules, the fact they assert is
expected to change the next time the feature area itself grows (a new switch, a renamed
group), so pinning it with reflection/IL-inspection turns an ordinary feature change into
a broken test rather than a caught regression. Instead:

- The ADR MUST tag each rule it enumerates as either a **Boundary Rule** or a
  **Feature-Scoped Invariant** in its Decision Outcome — a classification made once by
  design, not inferred later from how a test happens to be written.
- A Feature-Scoped Invariant MUST be covered by a classicist, state-based integration test
  (Principle II) exercising the real observable behavior the rule protects — e.g. start
  the Hub with a superseded key and assert the documented failure message; start without
  an option and assert the documented default — never by reflecting over a type's shape,
  an assembly's IL, or an internal catalog.
- A Feature-Scoped Invariant test MUST NOT assert a bare cardinality or literal
  enumeration ("exactly 4 switches," "exactly these 2 literals") as an end in itself. If
  the ADR's real concern is unbounded, silent regrowth of a surface — not the enumeration
  being correct today — the test MUST assert that concern directly, and the ADR must say
  that growing the surface is a recognized, single-file amendment, not an incidentally
  broken test.
- Both categories remain subject to Principle II's Ownership Test: neither licenses
  testing library-owned behavior (configuration binding, CLI-framework parsing,
  serialization).
- Content of instruction files (`system-prompt.md`, `default-user-prompt.md` — Principle
  V) is never a Feature-Scoped Invariant: see Principle V's own carve-out below.

**Observability/instrumentation tests** (Phase N — after implementation):
These verify that business metrics, structured log events, and trace spans are emitted
as specified in `plan.md ## Observability`. They require production code to exist and
therefore MUST NOT be placed in Phase 0. They MAY be implemented and tested co-located
with the user-story phase that introduces the signal, instead of being deferred to the
final phase — either placement is compliant, provided the final-phase completeness
audit below covers the row.

**Agent-behavior evaluation tests** (Phase N — after implementation):
Where a feature includes agentic behavior (Principle V), evaluation tests verifying the
**high-stakes** agent-judgment success criteria from the spec (Principle II's
success-criteria split) gate the DoD. Lower-stakes agent-judgment success criteria are
satisfied by the user-reported correction loop (Principle II) and MUST NOT be required to
have a formal eval suite as a DoD gate. Where an eval test is required, it requires the
agent loop and its instruction files to exist and MUST NOT be placed in Phase 0. As with
observability tests, it MAY be co-located with the triggering user story's own phase
rather than deferred to the final phase, provided the final-phase completeness audit
below covers the criterion.

The first task in every `tasks.md` MUST cover every Boundary Rule referenced in
`plan.md ## Architectural Constraints & ADRs` with a Phase 0 structural test.
Feature-Scoped Invariants are covered in their normal implementation phase per the rule
above, not in Phase 0. Where an ADR names no Boundary Rule at all, Phase 0 MUST say so
explicitly ("no Boundary Rule introduced by this feature") rather than being silently
omitted. The final phase of every `tasks.md` MUST include a named completeness-audit task
that cross-references every row of `plan.md ## Observability` and every **high-stakes**
agent-judgment success criterion in the spec against its implementing task and passing
eval test — regardless of which phase implements them — confirms each lower-stakes
agent-judgment criterion is covered by a hermetic test and recorded as subject to the
user-reported correction loop rather than a formal eval, and files any gap found as a new
task before the DoD is declared met.

Order is non-negotiable: plan drafted → ADR accepted → Structural test verified (Red/Green probe) for any Boundary Rule → Feature code (with Feature-Scoped Invariant tests alongside it) → Observability and evaluation tests pass → DoD met.

A Boundary Rule named in an Accepted ADR, without a corresponding automated structural
enforcement test, MUST NOT be referenced as an active architectural constraint. A
Feature-Scoped Invariant is active once its classicist behavioral test (above) passes —
it does not require, and MUST NOT be given, a reflection/IL-based structural test to count.

**ADR Status Maintenance.** ADR status headers are the source of truth for which
decisions currently govern the codebase; keeping them accurate is as binding as the
decision content itself.

- **Immutability.** An Accepted ADR's decision content (Context, Decision, Consequences)
  MUST NOT be edited after acceptance to change what was decided. A changed decision is
  recorded by a new ADR that **supersedes** the old one, replacing it entirely. Rewriting
  an Accepted ADR's substance in place is a violation regardless of whether the new text
  is more accurate.
- **Whole-ADR supersession only — no partial amendment or supersession.** Superseding an
  ADR always invalidates the **entire** ADR, never a section, a table row, or "this aspect
  only" of it. The prior mechanism of "amending" an ADR — narrowing or overriding part of
  its decision while the rest stayed Accepted — is retired for every ADR drafted from this
  amendment forward: `Amends` / `Amended by` MUST NOT appear in the status header of a new
  ADR. If any part of an Accepted ADR's decision needs to change, the whole ADR MUST
  transition to **Superseded**, and every aspect of it that is still valid MUST be
  re-decided as its own new, independent, single-aspect ADR in the Mandatory ADR format
  below — never inherited by reference from the superseded text and never patched into it.
  `Supersedes` / `Superseded by` is the only decision-changing link a new ADR may carry.
- **Extension is not invalidation.** Using more of an already-decided boundary or
  technology within the scope that decision already covers — a new consumer of an
  existing port, an additional row in an already-adopted schema, a new switch alongside
  an already-ratified CLI surface — MUST NOT supersede, amend, or otherwise change the
  status of the original ADR, not even in part. **Invalidation test** (decisive): would
  honoring the new requirement mean reversing, narrowing, or contradicting what the ADR
  actually decided? If no — everything the ADR decided remains true, the change only adds
  to it — it is an extension: no ADR status change, optionally cross-referenced with an
  `Extends ADR-N` note that carries no supersede/amend obligation on either side. If yes,
  it is an invalidation and MUST go through whole-ADR supersession above; there is no
  third option. Every ADR's own drafting MUST record, under its Change Triggers (Mandatory
  ADR format below), which future changes the author expects would be mere extensions
  (no ADR action) versus which would invalidate the decision (a full supersession) — this
  is the basis on which later authors and reviewers apply the test above rather than
  guessing after the fact.
- **Bidirectional linking.** When ADR-B supersedes ADR-A, both status headers MUST be
  updated in the same change: ADR-B's header records `Supersedes ADR-A`, and ADR-A's
  header records `Superseded by ADR-B`. A one-sided link — the new ADR pointing back
  without the old ADR pointing forward — MUST NOT ship; a reader who opens ADR-A alone
  has no other way to learn it is no longer current.
- **Lifecycle minimum.** Every ADR carries a status from exactly {Proposed, Accepted,
  Deprecated, Superseded} (case-insensitive; the repository's convention is lowercase
  YAML frontmatter — `status: accepted` — the Title Case above names the four states for
  prose, not a required casing). ADR numbers are permanent: never reused, never
  renumbered, and never merged into a single file to "consolidate" history —
  consolidation happens at the status and index layer below, not by rewriting or
  collapsing files.
- **Review cadence.** Accepted ADRs MUST be periodically checked for staleness rather
  than assumed current indefinitely. ADRs governing an externally observable surface
  (CLI command surface, credential/security boundaries, agent-facing tool contracts)
  MUST be reviewed at least every 90 days; ADRs scoped to purely internal architecture
  (module layering, internal packaging, persistence internals) MUST be reviewed at least
  every 365 days. Each review MUST end in one of: no change, or a status update per the
  rules above, reflected in `docs/adr/index.md`.
- **Central index.** `docs/adr/index.md` MUST list every ADR — number, title, current
  status, and, for Superseded ADRs, the supersede chain — and MUST be updated in the same
  change as any ADR whose status or existence changes. The index is the single place a
  reader determines, without opening every file, which ADRs currently govern the codebase.

**Mandatory ADR format.** Every ADR drafted from this amendment forward MUST use the
following MADR-based skeleton. Every listed section MUST be present, in this order, and
MUST NOT be omitted; the angle-bracket placeholders are the only parts that vary. Authors
MAY add extra clarifying subsections beneath an existing required heading, but MUST NOT
remove or reorder a required one.

```markdown
---
status: proposed
---

# ADR-NNN: <One-line title naming the single aspect this ADR decides>

## Context and Problem Statement

<Why this one boundary or technology decision is needed now; the forces in tension.
Scoped to exactly one aspect — see "Single-aspect ADRs; no feature content" above.>

## Decision Drivers

- <driver 1>
- <driver 2>

## Considered Options

1. <option 1>
2. <option 2>

## Decision Outcome

Chosen option: **<option>**, because <justification>.

- <supporting decision detail>
- Tag each Boundary Rule or Feature-Scoped Invariant this ADR introduces, per this
  Principle's "Structural boundary tests" / "Feature-Scoped Invariants" categories.

### Consequences

- Good, because <benefit>.
- Bad, because <cost>, mitigated by <mitigation, if any>.
- Neutral, because <accepted tradeoff>.

## Change Triggers

- **Extensions (do not invalidate this ADR):** <foreseeable uses of this decision that
  stay within what it decided — no ADR action needed when they occur>.
- **Invalidations (would require full supersession):** <foreseeable changes that would
  reverse, narrow, or contradict this decision — any of these retires this ADR wholesale
  via a new Superseding ADR, per "Whole-ADR supersession only" above>.

## More Information

<Cross-references to other ADRs this one depends on or is expected to be read alongside.
MUST NOT restate or narrow their decisions — see "Extension is not invalidation" above.>
```

Rationale: MADR/Nygard practice treats Accepted ADRs as immutable and supersession as a
bidirectional pointer for exactly this reason — without it, the common failure mode is a
new ADR that references the old one while the old one still reads as current, or a
collection where nobody can tell which "Accepted" entries have actually been overtaken.
The v1.10.0 amendment made supersession bidirectional but still let it be partial
("Supersedes ADR-003 (in part)", "Amends ADR-002, ADR-004, ADR-006, ADR-007, ADR-008") —
in practice this let single ADRs (ADR-002, ADR-009, ADR-022 among others) accumulate
several partial amendments each, so that understanding what any one of them currently
decides required reading its full amendment chain rather than one Accepted document. This
amendment removes that middle ground: an ADR going forward is either untouched, wholly
superseded, or merely extended — and extension was never a decision change and never
needed a supersede/amend link at all. `docs/adr/index.md`'s existing `Extends` convention
(ADR-016, ADR-017, ADR-026) already modeled that third case informally; this amendment
makes it binding and adds the "why would this need to change" question to drafting time
(the Change Triggers section), so authors write the extension/invalidation distinction
down in advance rather than a later reader inferring it from a diff. Existing partial
`Amends`/`Amended by`/`Supersedes ... (in part)` links recorded in ADRs and
`docs/adr/index.md` before this amendment are grandfathered under Governance's
non-retroactivity clause — historical record, not a pattern to repeat — and are not
required to be retroactively split into single-aspect ADRs by this change.

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
Conversely, a deterministic/harness test MUST verify only the load *mechanism* of an
instruction file (it exists, is loaded byte-exact, fails closed when missing or empty, and
its hash is recorded) — never the wording or substance of what it says. What an
instruction file's content causes the agent to do is exclusively the domain of Principle
II's agent-behavior evaluation tests; a deterministic test that string-matches required or
forbidden content in a real `system-prompt.md` or `default-user-prompt.md` duplicates that
coverage with a brittle proxy and MUST NOT be added.

**Deterministic harness.** Backend code owns only the harness: request dispatch and agent
lifecycle, credential scoping, guardrail enforcement at the agent's tool boundary,
task-artifact lifecycle and persistence, operational state, channels, and observability.
The harness orchestrates and constrains; it does not decide wiki content. Concretely, the
harness's inputs — which tasks run (ingest, query, lint, and any future task type) and
which instruction files steer an agent — are user-supplied parameters, not
harness-authored logic: the harness accepts and executes them without special-casing or
reinterpreting their content, which would smuggle wiki-content judgment back into backend
code. Within the harness, the Hub is the concrete orchestrator: it owns supplying each run
with the correct parameters (task, target, applicable instruction files) and ensuring the
run is observed via the business metrics, structured log events, and trace spans
specified in Principle IV.

**Human in the loop (operator loop).** The user is the human in the loop and the fourth
actor of the system, alongside the agents, the harness, and the Hub. The user interacts
with the system through its user-facing surfaces — today the frontend; channels and
OpenTelemetry dashboards as they arrive — to supply input (task requests and instruction
files), to communicate with the Hub, and to consume the observability they need to
evaluate agent behavior (Principle II's user-reported correction loop) and steer it
through instruction-file changes. Two consequences are binding:

- The Principle IV signals MUST be reachable by the user through at least one user-facing
  surface. A signal that is emitted but consumable nowhere the user actually looks does
  not close the operator loop; where a spec relies on the correction loop for a
  lower-stakes agent-judgment criterion, `plan.md ## Observability` MUST name the surface
  on which the user is expected to observe the relevant signal.
- The user's evaluation is judgment, not a gate to automate away: whether an
  instruction-file adjustment worked is verified by the user through these surfaces, per
  Principle II — not by converting the question into a deterministic backend check.

**Task artifacts and durable state.** The user supplies the task *request* — which task
runs, against what target — never task-artifact files. Task artifacts (the per-run
markdown records) are harness records: the Hub alone prepares and maintains their
scaffolding — structure, frontmatter, lifecycle state — so runs can be tracked and have a
durable storage; the narrative content they carry is agent-authored and passed through
verbatim (Principle II's task-artifact lifecycle). Expecting the user to author or
deliver a task artifact is a boundary violation on the same footing as reimplementing
agent judgment in backend code. Durable state lives in files: the wiki, task artifacts,
and harness records (findings reports, remediation records) persist as files on disk.
The embedded state database holds only the Hub's internal runtime operational state
(flags, queue state, coordination) and MUST NOT be required for durable persistence:
deleting it MUST lose no wiki content, no task artifact, and no harness record. This
names and makes enforceable the existing practice — `OperationalStateRepository` is the
only SQLite consumer, and every artifact store writes markdown files.

**Host stability guarantee.** Regardless of what a task or an instruction file says —
including content that is malformed, adversarial, or simply wrong — the harness MUST
ensure the agent process cannot corrupt the host by moving outside the boundary already
in force: it MUST NOT write to paths outside its designated roots, launch subprocesses
beyond what its guarded tools require, or otherwise act outside the guarded tool boundary
and credential scope. This is a containment guarantee — where the agent may act, not how
much of the host it may consume — and it holds independently of instruction-file content;
an instruction file MUST NOT be able to loosen it. It is a deterministic harness contract
(Principle II) and MUST be proven by hermetic tests, never by an agent-behavior
evaluation, since it must hold even when the agent is actively misbehaving.

Resource governance — CPU, memory, disk, and process-count ceilings — is deliberately out
of this guarantee's scope: it is a deployment concern, addressed by running the agent
process inside a container or comparable OS-level sandbox (the direction ADR-002 already
defers to), not by the harness reimplementing what that sandbox already provides. The
harness's own obligation there is limited to observability: the Hub MUST monitor and
expose agent process resource consumption through Principle IV's signals so the operator
can see and act on abnormal consumption through the operator loop — never enforcement.

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
- [ ] Structural boundary tests (Phase 0) for Dependency & Layering Boundary Rules pass in
      CI with no active violations; Feature-Scoped Invariants (Principle III) are verified
      by classicist behavioral tests, never by reflection/IL-based structural tests
- [ ] No deterministic/harness test asserts the wording or substance of an instruction
      file's content (Principle V) — only its load mechanism; content-level correctness is
      covered exclusively by agent-behavior evaluation tests
- [ ] Observability tests pass — implemented and tested either co-located with their triggering user-story phase or in the final phase — and a named final-phase completeness-audit task confirms every metric, log event, and trace span from `plan.md ## Observability` is emitted
- [ ] Logging contract is complete for every row in `plan.md ## Observability > Structured Log Events`: implementation tasks define stable event names and mandatory fields, deterministic integration tests validate event name/level/mandatory fields, and these logging tests run in the standard PR CI pipeline
- [ ] Trace contract is complete for every row in `plan.md ## Observability > Distributed Trace Spans`: implementation tasks define span names, parent/child relationships, and required attributes; deterministic integration tests validate span names, parent/child relationships, and correlation attributes (including shared IDs such as `task_id`); and these trace tests run in the standard PR CI pipeline
- [ ] Agent-behavior evaluation tests pass for every **high-stakes** agent-judgment success criterion in the spec, at the thresholds the spec defines; lower-stakes agent-judgment success criteria are documented as covered by the user-reported correction loop (Principle II) rather than a formal eval suite, with `plan.md ## Observability` naming the user-facing surface on which each such criterion's signal is observed (Principle V operator loop) — implemented and tested either co-located with their triggering user-story phase or in the final phase, confirmed complete by the final-phase completeness-audit task (only for features with agentic behavior)
- [ ] The agentic boundary (Principle V) is respected: no wiki-content judgment is implemented as deterministic backend code, instruction files are loaded into the agent's context, and the guarded-tool structural test passes; any feature that spawns or reconfigures agent processes proves the host stability guarantee (Principle V) via a hermetic test that the agent process cannot write outside its designated roots or launch subprocesses beyond its guarded tools' scope
- [ ] Hexagonal boundary rules (Principle I) hold: external-system dependencies introduced or touched by the feature are consumed via ports with production adapter and test fake, and infrastructure packages appear only in their designated adapter namespaces (enforced by structural architecture tests)
- [ ] Integration tests against real infrastructure cover all API boundaries introduced by the feature (Testcontainers only where a containerized dependency is genuinely involved)
- [ ] Test style follows Principle II's classicist (Chicago-school) rules: assertions are state-based (no interaction verification), test doubles are hand-rolled fakes implementing existing port interfaces only, and no mocking framework is referenced by any test project
- [ ] Every test added or modified by the feature asserts a product-owned contract (Principle II "Test what we own"): no test re-verifies third-party library behavior, and any residual framework dependency is covered by at most one minimal, intent-named wire-up test
- [ ] CI/CD pipeline passes: architecture tests, integration tests, linting, build
- [ ] No unapproved infrastructure was introduced
- [ ] Any ADR touched by the feature follows the Mandatory ADR format and decides exactly
      one aspect (Principle III "Single-aspect ADRs; no feature content"); if it
      supersedes another ADR, the supersession is whole (never in part) and carries a
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

**Version**: 2.0.0 | **Ratified**: 2026-06-23 | **Last Amended**: 2026-08-25

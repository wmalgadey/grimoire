# Implementation Plan: Prompt Conformance — Lifecycle Fields, Confidence Signals, Source Traceability

**Branch**: `030-prompt-conformance-02-plan` (layer 2 of a stack; layer 1 is
`claude/grimoire-lifecycle-confidence-nmb3mu`, PR #236) | **Date**: 2026-09-09 | **Spec**:
[spec.md](./spec.md)

**Input**: Feature specification from `/specs/030-prompt-conformance/spec.md`

## Summary

Four instruction documents disagree with each other about three things, and the disagreements are
what issues #109, #110 and #111 report. This feature edits those documents so they agree, and edits
nothing else.

Concretely: the shared frontmatter standard gains the two lifecycle fields lint already consumes
(`inbound_links`, `last_reviewed`) with a per-field statement of which run writes each; the shared
confidence convention is reduced to the signals every agent can actually observe and its thresholds
re-baselined so the bands are usefully distributed rather than demanding a perfect score, with lint's
link-graph signals kept as a documented extension and query's narrative variant reconciled; and the ingest role document gains the
`sources/<slug>.md` requirement plus a corrected, self-justifying integration-depth expectation.

**The technical approach is that there is no technical approach.** Every requirement in the spec is
satisfied by changing the *content* of instruction files. No backend code changes, no new tool, no
new guardrail rule, no new port, no schema. The one place this plan touches code at all is the eval
suite — not to add tests, but because ADR-012's staleness gate will fire on every recording whose
fingerprint covers the edited documents, and those recordings must be re-captured before merge.

## Technical Context

**Language/Version**: none for the deliverable — the changed artifacts are Markdown instruction
documents. The surrounding harness is .NET 9 / C# and is not modified.

**Primary Dependencies**: none added. The feature consumes existing mechanisms only: ADR-053's
two-document composition, ADR-043's build distribution of instruction files, ADR-012's recorded
replay and its fingerprint staleness gate.

**Storage**: N/A. Wiki pages, task artifacts and findings reports are markdown files on disk
(Principle V); nothing about their storage changes.

**Testing**: `Grimoire.IntegrationTests` for the harness plumbing that is already covered and must
stay green; `Grimoire.AgentEvals` (SlowEval tier, ADR-033) for the replay recordings that go stale
and need re-capture. **No new deterministic test asserts instruction-file wording** — Principle V
forbids it and the spec forbids it twice.

**Target Platform**: unchanged — the agents run as spawned .NET child processes (ADR-036) on the
same Linux host as today.

**Project Type**: instruction-content change inside an existing agentic harness.

**Performance Goals**: N/A. No runtime path is touched. The one second-order effect worth naming is
that the foundation document grows by roughly two frontmatter rows and a reworked scoring table,
which adds a small constant to every agent's system prompt on every run.

**Constraints**: Principle V is the binding one — none of this may be reimplemented as backend code,
and no deterministic test may assert what the documents say. FR-010a additionally forbids
implementing the degradation behaviour as a harness check on instruction-document content. The
2026-09-10 clarifications add a second constraint of the same family, aimed at the *documents* rather
than the harness: **the confidence convention carries no MUST-level property and no aggregation
rule** (FR-005, FR-006a). It is a judgment aid handed to an agent, so neither this plan nor the
instruction documents may turn it into a formula the agent executes or a shape anything verifies.

**Scale/Scope**: four instruction documents
(`Grimoire.AgentRuntime/Instructions/foundation-prompt.md` plus the ingest, lint and query
`system-prompt.md` files) and re-capture of the replay recordings whose fingerprints cover them.
No ADR is added: see *Architectural Constraints & ADRs* below.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Verdict | Basis |
|------|---------|-------|
| **I — Domain architecture, hexagonal boundaries** | Pass, not engaged | No new external system, no new port, no infrastructure package moves. Nothing in `Grimoire.Domain` is touched. |
| **II — Pragmatic testing, classicist style** | Pass | No new test doubles at all, so the mocking-framework prohibition and the port-fake-only rule are trivially satisfied. The eval work is re-capture of existing recordings, not new scenarios. |
| **II — Success-criteria split** | Pass | Every agent-judgment criterion (SC-003, SC-004, SC-005) is explicitly classified **lower-stakes** in spec.md with the argument stated, so no formal eval suite gates the DoD. SC-001/SC-002 are deterministic harness guarantees and keep their 100% form. |
| **II — Test what we own** | Pass | The feature adds no test. The Ownership Test is applied below to the question of whether FR-010 warrants a structural test; the answer is no. |
| **III — ADR-driven** | Pass | All 54 ADRs read via the index; the eight that constrain this feature are tabled below. Every one of them is *extended*, none invalidated, and the feature introduces no new system boundary and no technology choice — so no new ADR is drafted. The reasoning, including a withdrawn draft, is recorded below and in research.md R2. |
| **III — Boundary Rule vs Feature-Scoped Invariant** | Pass | This feature introduces **no Boundary Rule**. Nothing it changes is a dependency direction between packages, namespaces or layers; every rule it carries is agent behaviour under instruction files. `tasks.md` Phase 0 MUST state that explicitly rather than omitting the phase. |
| **IV — Observability** | Pass, with an honest zero | The feature emits no new signal because it adds no code path. The `## Observability` section below records that, and names the existing surfaces the operator loop depends on rather than inventing signals to fill a table. |
| **IV — Unapproved infrastructure** | Pass | None introduced. |
| **V — Agentic core** | Pass, and this is the point | Every behaviour this feature changes is wiki-content judgment and lands exclusively in instruction files. The Agentic Boundary table below assigns each capability. |
| **V — Instruction-file content is not deterministically tested** | Pass | Stated as a constraint in three places and carried into the Test Strategy: the only deterministic coverage is load-mechanism, which already exists and is unchanged. |
| **V — Judgment aids are not turned into deterministic rules** | Pass, after a correction | The 2026-09-10 clarifications removed FR-005's coherence MUST and withdrew this plan's own "weakest reading wins" aggregation rule for synthesis confidence. Both were prescriptions over an agent's judgment aid. The thresholds survive as a recommendation with recorded reasoning (research.md R1); nothing verifies them. |

**Result: PASS.** No ADR gate blocks `/speckit-tasks`.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

All 54 ADRs were reviewed via `docs/adr/index.md` (number, title, status, supersede chain); the
eight below were read in full because they constrain this feature. Superseded and deprecated ADRs
were excluded from consideration as constraints — notably ADR-007 (superseded by ADR-053/ADR-054)
and ADR-016 (superseded by ADR-031), both of which would otherwise look relevant.

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-053 | An Agent's System Prompt Is a Shared Foundation Document Composed With Its Role Document | Decides *where* each statement lives. The frontmatter standard and the confidence convention are wiki-wide, so they belong in the foundation document; lint's link-graph extension is role-specific, so it belongs in lint's role document. ADR-053's Change Triggers name "growth or rewriting of either document's content" and "moving text between the two documents" as **extensions** — so every content edit in this feature extends ADR-053 and does not supersede it. |
| ADR-031 | Lint Holds Full Authority Over Wiki Content, in Both Modes | Makes FR-002b implementable without any policy change: lint already holds `read-write` on the content root with no exclusions, so writing `last_reviewed` grants no new capability. Extension, not invalidation. |
| ADR-018 | Human-Authorized Remediation Action Execution | Bounds D5's answer. A remediation *proposal* is produced during an ordinary lint run and is what FR-002b keys on; a remediation *execution* is a separate human-authorised run. FR-002b must key on the proposal, not the execution, or the review date would depend on a human acting. |
| ADR-030 | Guarded Retrieval Tools — Search, Ranged Read, and Read-Only Batch | Supplies the reading vocabulary D5 was decided against. `read_file(frontmatter_only)` is what lint uses to survey every page; because D5 resolved to "produced a finding", the qualifying act does not depend on which retrieval tool was used, which keeps FR-002b independent of ADR-030's surface. |
| ADR-015 | Query Agent Write Scope and Cross-Process Wiki Write Coordination | Constrains FR-002c. Query creates pages under a create-only rule, so any lifecycle field it writes must be written at creation; it can never come back and amend one. |
| ADR-012 | Standalone Eval Runner and Recorded-Replay at the Model Port | Supplies SC-001's mechanism and its cost. The manifest fingerprints cover the instruction surface, so editing these documents marks every covering recording stale, and "replay tests failing on staleness in the standard PR pipeline are the merge gate for instruction changes". Re-capture is mandatory, not optional. |
| ADR-033 | SlowEval Replay Class Set Reduced by the Lower-Stakes Eval Removal | Names the class set in scope for re-capture. It enumerates **four** classes — `IngestReplayEvalTests`, `LintReplayEvalTests`, `QueryReplayEvalTests`, `RemediationReVerificationEvalTests` — and this feature touches the *shared* foundation document, which every one of them fingerprints; the lint role document is additionally fingerprinted by remediation re-verification. All four are therefore in FR-012's scope. It is also the precedent for this feature's lower-stakes classification. |
| ADR-043 | Build-Distributed Agent Artifacts and Single Launch Mode | Owns where instruction documents physically live and how they reach a running agent. The edited `foundation-prompt.md` and the three role documents are build-delivered artifacts, so a changed document only takes effect after the agent artifacts are rebuilt — which is what makes the manual validation steps in `quickstart.md` require a build, not just an edit. Extension: no new file location, no new delivery mechanism. |

**New ADR required?**: **No.**

An ADR (ADR-055, "a role document degrades when the foundation document is silent, and the harness
never looks") was drafted with the first version of this plan and then **withdrawn**. Recording why
is more useful than pretending it never existed:

- Its first rule — the agent carries out the parts of its role whose inputs are defined and names
  what it skipped — is **agent behaviour**, which is feature content. Principle III's "Single-aspect
  ADRs; no feature content" puts it in `spec.md`, where it already lives as FR-010.
- Its second rule — the harness never inspects instruction *content* to decide what to do — reads
  like a boundary, but it is **already decided**, and not by an ADR: Constitution Principle V states
  that the harness "accepts and executes [instruction files] without special-casing or reinterpreting
  their content". Restating a constitutional rule in an ADR adds no decision; it adds a second place
  to look.
- The problem it solved is **speculative**. Nothing in the system today has a foundation document
  that is silent on these fields, and Principle I is explicit that structural boundaries are earned,
  not assumed upfront.

So D3's resolution stays exactly where `/speckit-clarify` put it — FR-010 and FR-010a in `spec.md`,
carried into the lint role document as instruction content.

**Hexagonal gate**: not engaged. No dependency on a new external system, therefore no port, no
adapter namespace, and no containment rule.

## Agentic Boundary (Constitution Principle V)

| Capability | Side | Where it lives |
|------------|------|----------------|
| Which lifecycle fields a page carries, and their meaning | Agentic core | `Grimoire.AgentRuntime/Instructions/foundation-prompt.md` (frontmatter standard) |
| Which run writes each lifecycle field | Agentic core | foundation document, stated per field (FR-002a) |
| What counts as a review, for writing the review date | Agentic core | `Grimoire.LintAgent/Instructions/system-prompt.md` (FR-002b, FR-002d, FR-002e) |
| The confidence signal set and its thresholds | Agentic core | foundation document (FR-005, FR-006) — stated as a judgment aid; no MUST property, and nothing verifies the band distribution |
| Lint's link-graph confidence extension and its own thresholds | Agentic core | lint role document (FR-006) |
| How synthesis-page confidence is scored | Agentic core | `Grimoire.QueryAgent/Instructions/system-prompt.md` (FR-006a) — the *choice* of the shared formula over a narrative rule is stated; the weighting where inherited signals disagree is left to the agent |
| Producing a source-summary page and keeping citations resolvable | Agentic core | `Grimoire.IngestAgent/Instructions/system-prompt.md` (FR-008) |
| Integration-depth expectation | Agentic core | foundation + ingest role documents (FR-009) |
| Degrading when the foundation document is silent | Agentic core | lint role document (FR-010) |
| Loading and composing the two documents, fail-closed, hashed | Harness (**unchanged**) | `AgentHost` composition per ADR-053 |
| Guarded write boundary and policy scope | Harness (**unchanged**) | `Grimoire.*/Instructions/policy.json`, `GuardedToolExecutor` |
| Noticing that the foundation document omits a definition, and naming what was skipped | Agentic core | lint role document (FR-010) — the agent reads its own composed context and reports the gap |
| **The harness** detecting that omission, by any inspection of instruction content | **Forbidden** | FR-010a, restating Constitution Principle V. Not "nobody does it": the *agent* does it, the harness must not. |

The last two rows are the load-bearing pair, and the distinction between them is the whole of
FR-010 vs FR-010a: the *agent* notices the gap and says so, the *harness* never looks. Collapsing
them into one "forbidden" row — as an earlier draft of this plan did — misroutes the implementation
by implying nobody detects the omission at all. Neither row needs an ADR to hold: Principle V
already forbids the harness from reinterpreting instruction content, and a constitutional rule
outranks an ADR.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before
tasks are generated.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| **SC-001** — every recording invalidated by these edits is flagged stale and re-captured; none scores against a stale recording | Deterministic guarantee | `EvalRunner status` (exit 3 while anything is stale) over **all four** scenario sets, then `capture`, then the replay eval classes | Live LLM provider **for capture only**; replay thereafter uses `ReplayModelClient` | Existing scenario fixtures, unchanged | ADR-012's designed gate firing correctly, and the feature's one real cost. **Not** `StalenessTests`: that is a Fast-tier test of the staleness *mechanism* against a copied fake repo root with synthetic drift, so editing the real documents can never turn it red. `EvalRunner status` is the check that enumerates the committed Ingest, Lint, Query and remediation manifests. |
| **SC-002** — every run loads the post-change documents; the load *mechanism* is unchanged | Deterministic guarantee | Existing ADR-053 composition tests, run unmodified | None | None | Deliberately **no new test**. A new test here could only assert document content, which Principle V forbids. The existing composition test proves the mechanism; that it now composes different text is the feature, not a new contract. |
| **SC-003** — pages a creating run produces carry the lifecycle metadata | Agent judgment — **lower-stakes** | Hermetic plumbing (the guarded write lands, the run completes) + user-reported correction loop | Existing tool-call fake only | None — no eval suite required | Per Principle II and spec.md's stated argument. Reviewers MUST NOT require an eval here on the grounds that an LLM is involved. |
| **SC-004** — the review-candidate list contains pages overdue for *review*, not merely old since *ingest* | Agent judgment — **lower-stakes** | Same as SC-003 | Existing tool-call fake only | None | Observed by the operator on the findings board. |
| **SC-005** — an ingest run leaves a source-summary page and its citation wikilinks resolve | Agent judgment — **lower-stakes** | Same as SC-003; deviations surface as lint Structure findings, which already exist | Existing tool-call fake only | None | The correction path is a capability the product already has, which is a large part of why this is lower-stakes. |
| **SC-006** — the four documents agree, and each deliberate deviation is stated | Review outcome, **not a test** | Human review of the diff + the final-phase completeness audit | None | None | Asserting this with a test would mean string-matching instruction files. Explicitly out of scope per spec.md and Principle V. |
| **FR-010** — lint degrades rather than fails against a foundation document that omits the fields | Split | Hermetic: **a new integration test** dispatching a lint run against a fixture foundation document and asserting the run reaches terminal *completion* rather than erroring. Agent judgment: that the report names the skipped categories → correction loop | Fixture foundation document lacking the lifecycle rows | One fixture foundation document | No existing test covers this: the current foundation-load tests cover *absent*, *unreadable* and *whitespace-only* documents — all fail-closed paths — not a readable document that simply omits a definition, which must **not** fail closed. Applying Principle II's Ownership Test, the assertion is a product-owned outcome (our run completes); a structural or reflection-based test would instead assert document wording, which is not ours. |

**No new eval scenario is added by this feature.** Existing scenarios are re-captured, not extended.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

**This feature emits no new signal, and that is a finding rather than an omission.** It adds no code
path, no dispatch decision, no lifecycle transition and no failure mode that the harness can observe
— every behaviour it changes happens inside an agent's judgment, whose only externally visible trace
is the wiki content and the findings report it already produces. Enumerating invented metrics to
fill the tables below would produce exactly the "emitted but consumable nowhere" signals Principle V
warns about.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| *(none added)* | — | — | — |

Existing metrics that already cover the runs this feature changes, and are not modified:
`hub.ingest_submissions_total`, `hub.lint_lifecycle_updates_total`, `lint.tool_calls_total`,
`wiki.identity.foundation_resolved_total`.

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| *(none added)* | — | — | — |

The derivation rule is therefore vacuous for this feature: there is no row, so `tasks.md` carries no
logging-contract implementation, test or CI task. This must be stated explicitly in `tasks.md`
rather than silently omitted, so a reviewer can tell the difference between "no rows" and "rows
forgotten".

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| *(none added)* | — | — |

Same vacuity, same obligation to say so in `tasks.md`.

### Operator loop surfaces (Principle V — MANDATORY for lower-stakes criteria)

Every lower-stakes agent-judgment criterion in spec.md relies on the user-reported correction loop,
so each one must name the surface where the user actually observes it:

| Criterion | Surface the operator observes it on | Why that surface is sufficient |
|-----------|-------------------------------------|--------------------------------|
| SC-003 — lifecycle fields on created pages | The wiki itself (the page's frontmatter), and the task artifact for the run under `frontend/src/routes/tasks` | The field is either in the frontmatter or it is not; this is directly readable, no instrumentation needed. |
| SC-004 — review-candidate list means review age | The findings board on the submission page, `frontend/src/routes/+page.svelte` | The board was merged into `/` (`frontend/src/routes/board` is only a 308 redirect to it). The review-candidate section renders there today; a wrong list is visible as a wrong list. |
| SC-005 — source-summary page and resolvable citations | The wiki's `sources/` folder, plus lint Structure findings on the same board (`frontend/src/routes/+page.svelte`) | A dangling wikilink already surfaces as a Structure finding, so the loop closes without new work. |
| FR-010 — degraded lint naming its skipped categories | The findings report for that run, on the board | The report is agent-authored narrative, which is exactly why it can carry the reason — the harness never sees the omission. |

## Project Structure

### Documentation (this feature)

```text
specs/030-prompt-conformance/
├── spec.md              # /speckit-specify + /speckit-clarify output (layer 1, PR #236)
├── plan.md              # This file (layer 2)
├── research.md          # Phase 0 output (layer 2)
├── data-model.md        # Phase 1 output (layer 2)
├── quickstart.md        # Phase 1 output (layer 2)
├── contracts/
│   └── document-consistency.md   # Phase 1 output (layer 2)
├── checklists/
│   └── requirements.md  # spec quality checklist (layer 1)
└── tasks.md             # /speckit-tasks output — NOT created here
```

### Source Code (repository root)

The changed surface is four instruction documents. No ADR, no source tree added or restructured.

```text
backend/src/
├── Grimoire.AgentRuntime/Instructions/
│   └── foundation-prompt.md        # CHANGED — frontmatter standard, confidence convention,
│                                   #           integration depth
├── Grimoire.IngestAgent/Instructions/
│   └── system-prompt.md            # CHANGED — source-summary requirement, integration depth
├── Grimoire.LintAgent/Instructions/
│   └── system-prompt.md            # CHANGED — review date written definitely; link-graph
│                                   #           extension documented; degradation behaviour
└── Grimoire.QueryAgent/Instructions/
    └── system-prompt.md            # CHANGED — synthesis-page confidence reconciled; lifecycle
                                    #           fields on created pages

backend/tests/Grimoire.AgentEvals/Fixtures/recordings/   # RE-CAPTURED, not edited by hand
```

**Structure Decision**: no structural change. The feature's entire deliverable is instruction
content in the four directories above, which is the outcome Principle V's boundary smell test
predicts for a wiki-behaviour change: "wiki behavior changes are instruction-file changes."

## Complexity Tracking

No Constitution Check violations, so this table is empty by design rather than unfilled.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| *(none)* | — | — |

# Feature Specification: Lint at Scale

**Feature Branch**: `028-lint-at-scale`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Lint at scale: The Lint agent currently reads the entire wiki in one context window ('read the whole wiki, judge its condition across all three Finding Categories, refresh any stale inbound-link counts you find, and produce the Findings Report as your final message' — backend/src/Grimoire.LintAgent/Program.cs:207-212). On the self-hosted deployment the wiki is 633 markdown pages / ~1.6M characters (~400k tokens), which does not fit in one context window and never will as the wiki grows. This is GitHub issue #108. Two directions were identified in the issue, not mutually exclusive: Direction A (instruction-file change, no ADR needed): teach the Lint agent to work from index.md and page frontmatter/search, pulling full page bodies only for pages it has reason to suspect, restoring the 'read the index first' navigation rule from the source Karpathy pattern that Grimoire's Lint prompt deviated from. Direction B (harness change, needs an ADR): shard the run — the harness partitions the wiki into windows, runs an agent loop per window with its own budget, and merges partial Findings Reports into one. Spec 026 (#159, merged to main) already landed the retrieval primitives this feature needs: search_files, a ranged read_file (offset/limit and frontmatter_only), and a read-only batch tool in LintToolRegistry. PR #179 (spec 026's Phase N) already rewrote system-prompt.md's 'Choosing how to read' section toward frontmatter-first/search-first reading and recorded an 86% reduction in median content tokens read on a 'lint-at-scale' eval corpus (655 pages) as an incidental byproduct of proving spec 026's own eval scenario — not a dedicated delivery of this issue. Acceptance direction from the issue: a Lint run over a wiki of the current size (600+ pages) completes rather than aborting; the strategy for 'more pages than fit in one context' is stated, not implicit; whatever bounds the reading is observable, so a partial pass is distinguishable from a complete one; the agent-judgment half is covered by evaluation tests, not deterministic assertions on instruction-file wording. Related, not in scope: #64 (lint content/body remediation), #42 (inbound-link refresh reliability — Direction A alone may make this worse), #88 (same context-window problem on Ingest), #107 (AgentLoop token-cap accounting bug, independent failure)."

## Clarifications

### Session 2026-08-25

- Q: Should this feature validate "the wiki no longer fits in one context window" by
  generating large synthetic wiki corpora sized close to production (600+ pages, and
  1200+ for the 2x check), or by reusing a small fixture with its context budget
  deliberately lowered relative to corpus size? → A: reuse the small budget-constrained
  fixture relation (no new large corpus generation); keep the eval footprint proportionate.
  Superseded/subsumed by the second answer below, which resolves the same concern through
  the project's own binding policy rather than an ad hoc choice.
- Q: How does Constitution v1.12.0's high-stakes/lower-stakes agent-judgment tiering
  (ratified on `main` after this feature's spec was first drafted, in response to the same
  eval-cost concern raised above) apply to SC-004 and SC-005? → A: both are lower-stakes
  (a missed cross-page finding or a stale inbound-link count is correctable on a later
  pass, not a destructive or hard-to-reverse outcome) — expressed narratively, satisfied
  primarily by the user-reported correction loop, with a formal recorded-replay eval suite
  optional rather than mandatory for the Definition of Done. SC-001/SC-002/SC-003/SC-006
  remain deterministic harness guarantees, unaffected by this tiering, and SC-003 in
  particular is validated via the same small-fixture relation as the first answer, not a
  literal-scale corpus, per that same eval-cost concern.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A health-check run over the full wiki completes (Priority: P1)

An operator triggers a Lint run against the production wiki (currently 633 pages, growing).
Today that run fails partway through because the agent is instructed to read every page into
one conversation, and the wiki no longer fits. The operator needs the run to finish and
produce a Findings Report, the same way it does on a small wiki today.

**Why this priority**: This is the failure the issue exists to fix. Without it, Lint simply
does not work on the deployment that matters, and nothing else in this feature has value
until a run can complete at all.

**Independent Test**: Start a Lint run against a wiki of at least 600 pages / ~400k tokens of
content. The run reaches a terminal success state and produces a Findings Report, without
aborting on a context or token-budget failure.

**Acceptance Scenarios**:

1. **Given** a wiki of 633 pages (~400k tokens of page content), **When** an operator starts
   a Lint run, **Then** the run completes and produces a Findings Report rather than aborting.
2. **Given** a wiki that grows past today's size in a later run, **When** an operator starts a
   Lint run, **Then** the run still completes — the fix is not a one-time accommodation of the
   current page count.

---

### User Story 2 - An operator can tell a complete pass from a partial one (Priority: P1)

Whatever bounds how much of the wiki a run actually looks at, an operator reviewing a
Findings Report needs to know whether this run judged the whole wiki or only part of it,
so that "Lint found nothing wrong" is never silently mistaken for "Lint looked at
everything and found nothing wrong" when it did neither.

**Why this priority**: The issue calls this out explicitly as a required property of
whatever strategy is chosen — coverage must be observable, not assumed. A run that quietly
narrows its scope without saying so is worse than one that fails loudly, because it produces
false confidence.

**Independent Test**: Inspect the Findings Report (or accompanying run metadata) of any Lint
run and determine, without reading the instruction files or the run's raw tool-call log,
how much of the wiki that run actually covered.

**Acceptance Scenarios**:

1. **Given** a completed Lint run that examined the entire wiki, **When** an operator reviews
   its output, **Then** the output states that coverage was complete.
2. **Given** a completed Lint run that, for any reason, examined only part of the wiki,
   **When** an operator reviews its output, **Then** the output states that coverage was
   partial and indicates its extent (e.g., what was and was not covered).

---

### User Story 3 - Findings that span multiple pages are not silently lost (Priority: P2)

Some Lint findings — contradictions between pages, duplicate content, stale cross-references,
inbound-link counts — depend on comparing information across pages, not just judging one
page in isolation. An operator needs these categories of finding to keep working once the
agent stops reading every page body by default, not to quietly stop being detected.

**Why this priority**: This is the risk both issue comments flag: narrowing what the agent
reads is exactly what makes cross-page judgment harder, and issue #42 (inbound-link
accuracy) is named as the concrete case already in trouble. It is not the primary failure
(a P1) because a run that completes with degraded cross-page recall is still strictly better
than one that never completes — but shipping this feature without addressing it trades one
known problem for another.

**Independent Test**: An operator reading a completed run's Findings Report can judge whether
cross-page findings (e.g., two pages that contradict each other) and inbound-link counts look
right, at least as reliably as before Direction A's narrowing — confirmed primarily via the
user-reported correction loop, and optionally via one small recorded-replay check per
criterion, per Constitution v1.12.0's lower-stakes tiering (see SC-004/SC-005).

**Acceptance Scenarios**:

1. **Given** a planted contradiction between two pages, **When** Lint runs against them,
   **Then** the resulting Findings Report surfaces the contradiction at least as reliably as
   before narrowing — verified via the correction loop and, where one exists, an optional
   recorded-replay check (SC-004).
2. **Given** a page whose recorded inbound-link count no longer matches its actual inbound
   links, **When** Lint runs a health check that includes that page, **Then** the refreshed
   count in the Findings Report matches the actual count at least as reliably as the
   pre-existing baseline — verified via the correction loop and, where one exists, an optional
   recorded-replay check (SC-005).

---

### Edge Cases

- What happens when a single page's own body — not the whole wiki, one page — is too large
  to read in one call? (Distinct from the whole-wiki problem; the strategy should not assume
  every individual page is small.)
- What happens when the agent's bounded-reading strategy causes it to reasonably skip a page
  that, unknown to it, contains the one finding an operator cares about that run? Coverage
  reporting (User Story 2) must make this distinguishable from "Lint checked and found
  nothing," even when it is the agent's own judgment call, not a hard budget cutoff.
- What happens when the wiki is small enough (today's few-hundred-page eval corpora, or a
  fresh wiki) that the old whole-wiki read still fits? The chosen strategy must not make a
  small wiki's runs worse, slower, or less thorough than they are today.
- What happens on a run that exhausts its reading budget partway through? Coverage reporting
  must distinguish this from a run that judged the whole wiki relevant scope was reachable
  and stopped by choice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A Lint run MUST be able to complete against a wiki of at least the current
  production size (633 pages / ~400k tokens of page content) without aborting due to a
  context-window or token-budget failure.
- **FR-002**: The strategy for handling "more wiki content than fits in one run" MUST be
  explicitly defined and documented, not left as an implicit consequence of whatever token
  cap happens to be configured.
- **FR-003**: Every completed Lint run MUST report, as part of or alongside its Findings
  Report, how much of the wiki it actually covered — sufficient for an operator to
  distinguish a run that judged the whole wiki from one that judged only part of it, without
  needing to inspect the run's raw tool-call log or the instruction files.
- **FR-004**: The coverage report required by FR-003 MUST be produced by the harness from
  the run's own observable activity (e.g., which pages/files were actually read), not
  self-reported by agent narrative alone — a partial run must not be able to describe itself
  as complete.
- **FR-005**: The chosen strategy MUST NOT regress Lint's ability to detect finding
  categories that depend on comparing content across multiple pages (contradictions,
  duplicate content, stale cross-references) below the expected behavior stated in SC-004 —
  a lower-stakes agent-judgment criterion (Constitution v1.12.0), verified primarily via the
  user-reported correction loop, with a numeric threshold applying only where an optional
  eval check exists.
- **FR-006**: The chosen strategy MUST NOT regress the accuracy of inbound-link count
  refreshing (the subject of the related issue #42) below the expected behavior stated in
  SC-005 — same lower-stakes tiering and verification approach as FR-005; this feature is not
  required to *improve* inbound-link accuracy, only to avoid making it worse as a side effect
  of bounding what the agent reads.
- **FR-007**: A Lint run over a wiki small enough to fit the prior whole-wiki-read approach
  MUST NOT become slower, less thorough, or less reliable as a result of this feature.
- **FR-008**: The system MUST continue to complete Lint runs as the wiki grows beyond its
  current size, within the scale envelope defined in Success Criteria — the fix must not be
  a point accommodation of exactly 633 pages.
- **FR-009**: Whatever design is chosen MUST be consistent with Constitution Principle V:
  judgment about which pages matter and what a finding means remains the agent's, exercised
  under instruction files; any harness-side change introduced to make runs complete (e.g.,
  partitioning or scheduling work) MUST NOT itself decide wiki content or findings.

### Key Entities

- **Lint Run**: A single execution of the Lint agent against the wiki, producing one
  Findings Report as its outcome. Existing concept; this feature changes what it reads and
  what it reports about its own coverage, not its identity.
- **Coverage Report**: A new, harness-observable statement of how much of the wiki a given
  Lint run actually examined (e.g., pages considered vs. total pages, or an equivalent
  measure), attached to or derived from the run's record. Distinguishes complete from
  partial passes per User Story 2 / FR-003 / FR-004.
- **Findings Report**: Existing concept (per spec 013) — the agent's final output describing
  what it found across the three Finding Categories. Unchanged in shape by this feature;
  what changes is how much of the wiki informed it and whether that scope is now stated.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Lint runs against a wiki of at least 633 pages / ~400k tokens of page
  content reach a terminal success state and produce a Findings Report, with zero runs
  aborting due to a context-window or token-budget failure. *(Deterministic harness
  guarantee.)*
- **SC-002**: 100% of completed Lint runs carry a coverage report stating whether the pass
  was complete or partial, derived from the run's own observed activity. *(Deterministic
  harness guarantee.)*
- **SC-003**: The "more wiki than fits one context window" property is validated as a
  *relation* between corpus size and reading budget — not by generating a corpus at literal
  production scale — and holds at more than one point on that relation (e.g., a fixture
  whose budget is set to a smaller fraction of what a full read would need than the
  baseline case), with reading volume not growing super-linearly as that fraction shrinks.
  *(Deterministic harness guarantee, scale envelope. This is deliberately a cheap,
  repeatable relation check, not a large generated corpus — see Assumptions.)*
- **SC-004**: Lint's Findings Report continues to surface cross-page findings (contradictions,
  duplicate content between two pages) once reading is narrowed by Direction A, at least as
  reliably as before narrowing. *(Lower-stakes agent-judgment criterion per Constitution
  v1.12.0 Principle II — a missed cross-page finding is correctable on the next run, not a
  destructive or hard-to-reverse outcome. Satisfied primarily by the user-reported correction
  loop: an operator who notices a missed finding via the Findings Report adjusts
  `system-prompt.md` and verifies the fix themselves — no CI-gated eval suite is required for
  this to count as done. One small, specific recorded-replay check MAY additionally exist for
  extra confidence, but its absence does not fail the Definition of Done.)*
- **SC-005**: Lint's inbound-link count refresh continues to match the actual inbound-link
  graph once reading is narrowed, at least as reliably as the pre-existing baseline (out of
  scope: improving it further, per FR-006 — see also #42). *(Lower-stakes agent-judgment
  criterion per Constitution v1.12.0 Principle II — a stale count is a single, correctable
  wiki edit, not a destructive outcome. Same correction-loop treatment as SC-004: no
  mandatory eval suite; one small optional recorded-replay check MAY exist for extra
  confidence.)*
- **SC-006**: On the eval scenario already used to measure this (spec 026's
  `lint-at-scale-survey`, a small fixture with its context budget deliberately set below
  what a whole-wiki read would need), a run's total content tokens read stays at or below
  the ~86% reduction already observed incidentally under spec 026's own eval scenario — i.e.,
  this feature does not regress the token-efficiency gain that already landed, at whatever
  scale that scenario runs at. *(Deterministic harness measurement of an agent-driven
  outcome; evaluated by observation, not asserted as an agent-judgment threshold.)*

## Assumptions

- **Scope boundary — inbound-link accuracy (#42) is a hold-steady requirement, not a
  deliverable.** The issue's first comment flags that Direction A alone risks making #42
  worse; this spec (FR-006, SC-005) requires the chosen design not to regress it below its
  present baseline, but fixing #42 to a higher bar is out of scope and tracked separately.
- **Scope boundary — content/body remediation (#64) and the token-cap accounting bug (#107)
  are out of scope.** #107 is called out in the issue as an independent failure that this
  feature does not need to fix (though it may no longer be reachable if this feature's
  design avoids exhausting the cap in the first place); #64 is a larger remediation surface
  that depends on Lint completing at all, which is what this feature delivers.
- **The retrieval primitives from spec 026 (`search_files`, ranged `read_file`,
  `frontmatter_only`, read-only `batch`) already exist in `LintToolRegistry` and are assumed
  available; this feature does not redeliver them.** Whether this feature's design uses them
  as the complete mechanism (Direction A), adds a harness-side complement (Direction B), or
  both, is a planning-level decision informed by, but not made in, this spec.
- **The wiki continues to be represented as markdown files with an `index.md` and
  page-level frontmatter**, consistent with the existing storage model (spec 014); this
  feature does not change wiki storage.
- **"Terminal success state" and "Findings Report" reuse their existing definitions from
  spec 013** (the original Lint Agent feature); this feature does not redefine what a
  successful run or a Findings Report is, only what informs it and what it reports about
  its own coverage.
- **SC-003 does not require generating a corpus at literal production scale (633 or
  1200+ pages).** Building and maintaining a several-hundred-page synthetic wiki fixture for
  this alone was judged disproportionate to what it proves — the same "reading bounded well
  below total content" property is demonstrated more cheaply, and more repeatably, by
  tightening the reading budget against a small fixture than by growing the fixture to match
  the budget. SC-001's 633-page / ~400k-token guarantee is validated by this relation
  generalizing across fixture sizes and budgets, not by every eval run operating on a
  full-size corpus.
- **SC-004 and SC-005 are classified lower-stakes per Constitution v1.12.0's Principle II
  tiering** (agent-judgment criteria whose cost of being wrong is a single, correctable wiki
  edit or a missed finding surfaced on a later pass — not an irreversible or hard-to-reverse
  outcome). This is why they are expressed narratively rather than as hard-gating numeric
  thresholds, and why a formal recorded-replay eval suite is optional rather than mandatory
  for them, unlike SC-001/002/003/006, which remain deterministic harness guarantees. This
  keeps this feature's own eval footprint proportionate to what issue #108 actually needs
  verified, consistent with the same cost-consciousness that motivated the v1.12.0 amendment
  and the removal of 19 lower-stakes eval scenarios project-wide (ADR-033).

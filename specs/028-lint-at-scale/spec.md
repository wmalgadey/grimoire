# Feature Specification: Lint at Scale

**Feature Branch**: `028-lint-at-scale`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Lint at scale: The Lint agent currently reads the entire wiki in one context window ('read the whole wiki, judge its condition across all three Finding Categories, refresh any stale inbound-link counts you find, and produce the Findings Report as your final message' — backend/src/Grimoire.LintAgent/Program.cs:207-212). On the self-hosted deployment the wiki is 633 markdown pages / ~1.6M characters (~400k tokens), which does not fit in one context window and never will as the wiki grows. This is GitHub issue #108. Two directions were identified in the issue, not mutually exclusive: Direction A (instruction-file change, no ADR needed): teach the Lint agent to work from index.md and page frontmatter/search, pulling full page bodies only for pages it has reason to suspect, restoring the 'read the index first' navigation rule from the source Karpathy pattern that Grimoire's Lint prompt deviated from. Direction B (harness change, needs an ADR): shard the run — the harness partitions the wiki into windows, runs an agent loop per window with its own budget, and merges partial Findings Reports into one. Spec 026 (#159, merged to main) already landed the retrieval primitives this feature needs: search_files, a ranged read_file (offset/limit and frontmatter_only), and a read-only batch tool in LintToolRegistry. PR #179 (spec 026's Phase N) already rewrote system-prompt.md's 'Choosing how to read' section toward frontmatter-first/search-first reading and recorded an 86% reduction in median content tokens read on a 'lint-at-scale' eval corpus (655 pages) as an incidental byproduct of proving spec 026's own eval scenario — not a dedicated delivery of this issue. Acceptance direction from the issue: a Lint run over a wiki of the current size (600+ pages) completes rather than aborting; the strategy for 'more pages than fit in one context' is stated, not implicit; whatever bounds the reading is observable, so a partial pass is distinguishable from a complete one; the agent-judgment half is covered by evaluation tests, not deterministic assertions on instruction-file wording. Related, not in scope: #64 (lint content/body remediation), #42 (inbound-link refresh reliability — Direction A alone may make this worse), #88 (same context-window problem on Ingest), #107 (AgentLoop token-cap accounting bug, independent failure)."

**Extension (2026-08-25)**: User request: "I would like to extend the spec to also include the fix to https://github.com/wmalgadey/grimoire/issues/201, because lint should also update log.md, and we need a fix for that too." GitHub issue #201's body: "`log.md` is prepend-only (ADR-028) and the only write primitive the guarded tool surface offers is whole-file `write_file`. So adding one log entry requires the agent to re-emit the entire existing file, byte-for-byte, inside a single tool call — the cost of one entry is O(file size), on a file that grows with every run and is never truncated. On the self-hosted server that cost has already exceeded what the agent is allowed to produce, and log writes now fail deterministically. [...] Proposed fix: Give the guarded tool surface a prepend primitive: the agent supplies only its new entry, and the harness concatenates head + current under the lock it already holds. [...] `index.md` has the same whole-file rewrite shape but is only 3 KB — out of scope here [...] This changes the guarded tool contract, so per the constitution it needs an ADR amending ADR-028 (and ADR-030 for the tool surface) and a run through the spec-kit workflow." Full issue: https://github.com/wmalgadey/grimoire/issues/201.

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
- Q: Should issue #201 (log.md's O(file-size) write cost) be a separate feature, or merged
  into this one? → A: merged, at the user's explicit direction, overriding the initial
  recommendation to keep them separate. Rationale recorded for traceability: Lint's own
  instruction file (`agents/lint/system-prompt.md`) already reads and writes `log.md` as
  part of reconciling `index.md`/`log.md` and recording deletions — so #201's already-observed
  production failure (log writes now fail deterministically once `log.md` exceeds the
  agent's output-token budget) is a live blocker on Lint's own write path, not just Ingest's.
  #108's read-side scaling fix would otherwise hand Lint a write path already known to fail
  at production scale. At the time of this clarification, `plan.md` and `tasks.md` (built
  for the narrower, pre-merge scope) had just been deleted and this feature restarted
  planning from this spec — see Assumptions for what that implied for the ADR gate. (Both
  now exist again in this PR, regenerated against the merged spec by the subsequent
  `/speckit-plan` pass; a drafted ADR was later retracted — see Assumptions.)

### Session 2026-08-25 (continued, post-merge)

- Q: Should the new bounded-cost `log.md` write primitive be usable by Ingest and Query as
  well as Lint, or scoped to Lint only? → A: all three. Issue #201's own production
  evidence — the writes already failing — comes entirely from Ingest, not Lint; scoping the
  fix to Lint only would leave the actually-observed failure unresolved. This feature's
  write-side work now explicitly includes `agents/ingest/system-prompt.md` and
  `agents/query/system-prompt.md`, not only Lint's.
- Q: Should the primitive be a new mode on the existing `write_file` tool, or a distinct new
  tool? → A: a new `write_file` mode (named `WriteMode.Prepend` in this decision, for
  traceability into the eventual ADR/plan — later refined by research.md R7 to a
  schema-level `write_file` call parameter, *not* a new value on the policy-level
  `Grimoire.Domain.Guardrails.WriteMode` enum this naming could be misread as; the
  substance of this answer, a mode on `write_file` rather than a distinct tool, is
  unchanged), consistent with how ranged `read_file` (ADR-030 R3) and `FrontmatterOnly`
  (ADR-016) were both added as modes/parameters on an existing tool rather than new tools,
  keeping the tool count flat.

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

### User Story 3 - A log entry can be written without re-emitting the whole file (Priority: P1)

Ingest, Query, and Lint each write `log.md` to record their own actions (agent-exclusive
authorship, ADR-035) — Lint's
own instructions additionally have it read and write `log.md` as part of reconciling
`index.md` and `log.md` and recording page deletions. Today, adding one entry requires an
agent to reproduce `log.md`'s entire existing content inside the write call, because the only
write primitive is whole-file `write_file` and the prepend-ordering guarantee (the
activity-log write contract) requires the proposed content to end with the current content
byte-for-byte. On the
self-hosted deployment `log.md` has grown to ~128KB / ~35k tokens, which already exceeds what
an agent is allowed to produce in one response — log writes now fail deterministically for
Ingest today (issue #201), and Lint's own write path depends on the identical mechanism. An
operator needs a log write to succeed regardless of how large the file has grown, for
whichever agent is writing it.

**Why this priority**: This is an already-observed, live production failure on Ingest's write
path, not a projected one, and Lint's own write path shares the exact same broken mechanism.
Shipping User Story 1's read-side fix without this one would let Lint survey a wiki it
currently cannot, only to still be unable to record what it found in `log.md`, or to record a
page deletion's cleanup obligations (`agents/lint/system-prompt.md`'s "Reconciling
`index.md` and `log.md`" step) — while leaving Ingest's already-failing writes unfixed.

**Independent Test**: With `log.md` already at or beyond its current production size, submit
one well-formed new entry through the guarded tool surface. The write succeeds, the entry
appears newest-first, and the agent did not need to reproduce the file's existing content to
do it.

**Acceptance Scenarios**:

1. **Given** `log.md` is 128KB / ~35k tokens (today's production size), **When** an agent
   submits one new, well-formed log entry, **Then** the write succeeds and the entry appears
   newest-first, without the agent reproducing the file's existing content in its write call.
2. **Given** `log.md` continues to grow with every run, **When** an agent submits one new
   entry, **Then** the output-token cost of that write stays proportional to the entry's own
   size, not to `log.md`'s total size.
3. **Given** a malformed entry (wrong heading shape, or no body paragraph following it),
   **When** an agent submits it through the new write path, **Then** the write is denied for
   the same structural reasons the activity-log format contract already defines — the new
   write path does not weaken any existing format guarantee.
4. **Given** two agents race to write a `log.md` entry at nearly the same time, **When**
   both writes are evaluated, **Then** neither entry is lost or silently overwritten — the
   writes are serialized so each agent's entry is prepended onto the latest content in
   lock-acquisition order, without either agent needing to have read the file first.

---

### User Story 4 - Findings that span multiple pages are not silently lost (Priority: P2)

Some Lint findings — contradictions between pages, duplicate content, stale cross-references,
inbound-link counts — depend on comparing information across pages, not just judging one
page in isolation. An operator needs these categories of finding to keep working once the
agent stops reading every page body by default, not to quietly stop being detected.

**Why this priority**: This is the risk both issue comments flag: narrowing what the agent
reads is exactly what makes cross-page judgment harder, and issue #42 (inbound-link
accuracy) is named as the concrete case already in trouble. It is not a primary failure
(a P1) because a run that completes with degraded cross-page recall is still strictly better
than one that never completes, or that cannot record its own results — but shipping this
feature without addressing it trades one known problem for another.

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
- What happens to two agents (e.g., Ingest and Lint) racing to write a `log.md` entry at
  nearly the same time under the new write primitive? The existing cross-process lock still
  serializes the writes so neither entry is lost — a prepend needing no prior read means no
  compare-and-swap check applies, not that concurrent writers go unserialized (User Story 3,
  issue #201's own "decide this explicitly" note on compare-and-swap; research.md R8).
- What happens to `index.md`, which has the same whole-file-rewrite shape but is far smaller
  (~3KB) than `log.md`? Explicitly out of scope for this feature (issue #201 names it "the
  same class of problem on a slower fuse") — noted, not fixed, here.

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
- **FR-010**: The guarded tool surface MUST offer a write primitive — a new call-shape
  parameter on the existing `write_file` tool (a `mode` value, distinct from the
  policy-level `WriteMode` enum that governs which edits a *path* allows — see research.md
  R7), per clarification — that lets Ingest, Query, and Lint each add one new entry to
  `log.md` without reproducing the file's existing content in the write call. The
  output-token cost of one entry write MUST be proportional to the entry's own size, not to
  `log.md`'s total size, for all three agents.
- **FR-011**: The new write primitive MUST continue to enforce every structural guarantee
  the activity-log format contract already places on `log.md` writes — prepend ordering, the
  heading-pattern check, and the non-empty-body-paragraph check — unweakened.
- **FR-012**: The new write primitive MUST guarantee no `log.md` entry is lost or silently
  overwritten under concurrent writers — two agents racing to write are serialized so each
  entry lands, in lock-acquisition order, onto the latest content. This is the safety
  property the primitive must uphold, not a specific mechanism: for a full-content
  (`ReadWrite`-mode) write, that mechanism remains the existing stale-write denial; for the
  new primitive, the equivalent safety is satisfied by reading fresh content under the write
  lock rather than by a denial path (see research.md R8) — there is no scenario where a
  prepend can be "stale," since it never asserts anything about the file's prior content.
- **FR-013**: This feature's write-side fix is scoped to `log.md` only. `index.md` has the
  same whole-file-rewrite cost shape but is far smaller today; it is explicitly out of scope
  here and tracked as the same class of problem, deferred.
- **FR-014**: Whatever design is chosen for FR-010 MUST be consistent with Constitution
  Principle V exactly as FR-009 requires for the read side: what to log and what an entry
  says remains the agent's judgment under instruction files; the harness gains only a
  cheaper way to commit an agent-authored entry, never authorship of the entry itself.
- **FR-015**: Because FR-010's primitive is only exercised if an agent actually calls it,
  `agents/ingest/system-prompt.md`, `agents/query/system-prompt.md`, and
  `agents/lint/system-prompt.md` MUST each be updated to write `log.md` entries through the
  new primitive instead of the current "read the whole file, then write your entry followed
  by exactly what you read" pattern — landing only the harness capability without this would
  leave Ingest's already-observed production failure (issue #201) unfixed.

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
- **Log Entry**: One agent-authored addition to `log.md` (the activity-log format contract: a
  `## [date] Title | ...` heading followed by at least one body paragraph), written by Lint,
  Ingest, or Query. This
  feature changes how an entry is committed to disk (a bounded-cost write primitive), not its
  shape, its ordering guarantee, or what triggers an agent to write one.

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
- **SC-007**: 100% of well-formed, single-entry `log.md` writes succeed and cost output
  tokens proportional to the entry's own size — regardless of `log.md`'s existing size,
  including at the ~128KB / ~35k-token size already observed failing in production.
  *(Deterministic harness guarantee.)*
- **SC-008**: 100% of `log.md` writes that violate an existing structural rule (wrong
  ordering, malformed heading, missing body paragraph) are still denied under the new write
  primitive, with the same reasons as before; and 100% of concurrent writes to `log.md`
  leave every entry intact — none lost, none silently overwritten — regardless of write
  timing. *(Deterministic harness guarantee — the new primitive must not weaken any
  structural guarantee the activity-log format contract already established, and must uphold the same
  no-lost-writes safety property `ReadWrite` mode's compare-and-swap check provides, by a
  different, lock-serialized mechanism — see research.md R8.)*

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
- **Scope boundary — `index.md`'s equivalent whole-file-rewrite cost (FR-013) is out of
  scope**, per issue #201's own scoping. It is materially smaller today (~3KB vs. `log.md`'s
  ~128KB) and not yet observed failing in production; it is the same class of problem on a
  slower fuse, tracked separately rather than bundled in here.
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
  for them, unlike SC-001/002/003/006/007/008, which remain deterministic harness guarantees.
  This keeps this feature's own eval footprint proportionate to what it actually needs
  verified, consistent with the same cost-consciousness that motivated the v1.12.0 amendment
  and the removal of 19 lower-stakes eval scenarios project-wide (ADR-033).
- **This feature initially concluded it required a new ADR**, reversing the earlier
  (pre-merge) conclusion that none was needed — drafted and reaching Accepted status as
  "ADR-035". FR-010's write primitive changes the guarded tool contract (a new
  `write_file` call-shape parameter, not a new value on the policy-level `WriteMode` enum
  — see research.md R7 and FR-010's updated wording), which was read, at the time, as
  needing an Accepted ADR before `/speckit-tasks` could run, per issue #201's own
  anticipation. This conclusion did not survive review (see below); this feature's final
  state needs **no new ADR** at all.
- **Constitution v2.0.0 (2026-08-25) landed on `main` while this feature was in flight**,
  requiring single-aspect ADRs and retiring partial `Amends`/`Amended by` for ADRs drafted
  from that amendment forward, in favor of an Invalidation test: does the new decision
  reverse, narrow, or contradict what an earlier ADR decided? This feature's then-drafted
  ADR did not, for the ADRs it extended, so it was treated as an extension under this test.
  See research.md R11 for that first correction's full rationale.
- **A second, independent, much larger ADR restructuring later landed on `main`**, also
  mid-flight, superseding the original ADR-028 wholesale with a new, differently-scoped
  ADR-035 ("Agent-Exclusive Authorship of the Wiki Activity Log" — decides *who* may author
  `log.md` content, not the prepend-ordering mechanism this feature's own ADR had drafted
  under that same number) and deprecating ADR-017 entirely, reclassifying its
  format-enforcement content as feature-scoped and moving it to a contract document rather
  than an ADR. This forced this feature's own ADR to be renumbered "ADR-035" → "ADR-051"
  (the next free number) and re-pointed to extend `main`'s new ADR-035 instead. See
  research.md R12 for the full rationale, including why the pre-existing ADR-028/ADR-031
  bidirectional-linking gap this feature had separately identified became moot once
  ADR-028 was wholly Superseded.
- **The PR author's own review then rejected the premise of drafting an ADR here at all**,
  independent of either restructuring above: an optional call-shape parameter added to an
  already-existing tool, defaulting to current behavior and granting no capability the
  tool's existing contract did not already permit, decides neither a new system boundary
  nor a new technology choice — Constitution Principle III's existing "Single-aspect ADRs;
  no feature content" test, unchanged, already answers this. ADR-051 was retracted before
  merge; its two rules (the `mode` schema addition and its no-baseline dispatch mechanism)
  are now Feature-Scoped Invariants in `plan.md`, covered by classicist tests, under the
  already-Accepted guarded-tool-boundary ADR-006. See research.md R13 for the full
  rationale.
- **A first attempt at generalizing this into a constitution amendment (v2.0.0 → v2.1.0)
  was itself reverted**, again on the PR author's direct review: the amendment's own new
  subsection was a narrower restatement of the test Principle III already stated, and
  adding it risked the same proliferating-specificity problem this feature's ADR history
  had just been corrected for, one level up. The constitution stays at v2.0.0. See
  research.md R14 for the full rationale.

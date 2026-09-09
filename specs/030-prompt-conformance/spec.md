# Feature Specification: Prompt Conformance — Lifecycle Fields, Confidence Signals, Source Traceability

**Feature Branch**: `claude/grimoire-lifecycle-confidence-nmb3mu`

**Created**: 2026-09-08

**Status**: Draft

**Input**: User description: "Prompt conformance: restore the lifecycle fields, the confidence
signals, and source traceability that drifted out of Grimoire's instruction files.

This feature covers GitHub issues #109, #110 and #111 as ONE unit (all milestone
1.0.0). They belong together because they touch the same two files, the same
frontmatter block, and would otherwise cost three separate eval re-captures.
Feature 028 explicitly declined to fold them in; they are the leftover.

## Verified current state (checked against main at ade31fe, 2026-09-08)

IMPORTANT: the file/line references inside issues #109, #110 and #111 are STALE.
They were written 2026-08-18, before feature 029 shipped. Feature 029 moved the
wiki-wide conventions out of the three role documents into a single shared
foundation document, so the fix surface has changed. Do not follow the issues'
line numbers; use these:

- backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md (310 lines)
  - L95-118  Frontmatter standard. Requires type, title, description, timestamp,
             tags, confidence, confidence_reason. Has NO inbound_links and NO
             last_reviewed. This is issue #109's defect, now in one place.
  - L145-160 Confidence Scoring: five signals, thresholds >= 2 high / 0-1 medium
             / < 0 low. The two inbound-link signals from the source pattern
             (inbound links >= 3 -> +1, orphan -> -1) are missing while the
             thresholds are unchanged. This is issue #110's defect, now in one
             place.
  - L58, 79, 82, 120, 305: the `Source summary` page type, its `sources/<slug>.md`
             path, and the `resource` rule already exist here.
- backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md (110 lines)
  - L9-45    Steps 1-3. Nothing asks for a `sources/<slug>.md` page. Issue #111's
             first half.
  - L34      \"One source typically touches 5-15 pages\" vs the source pattern's
             \"10-15\". Issue #111's second half.
- backend/src/Grimoire.LintAgent/Instructions/system-prompt.md (562 lines)
  - L86, 133, 191, 217, 298-329, 411: Lint reads and writes inbound_links and
             last_reviewed today. It is the consumer of the fields #109 restores;
             its own text needs no change, but it is the reason the gap bites.
- backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md
  - L110, 126: adapts the shared confidence rules for synthesis pages. Check
             whether it needs a matching change once #110 is decided.

## The three defects and what each costs

1. #109 - every ingested page arrives without inbound_links, so Lint must create
   the field wiki-wide on every run and can never do an incremental refresh; and
   because last_reviewed never exists, Lint's review window falls back to
   timestamp, i.e. it measures ingest age, not review age. A page reviewed
   yesterday reads as overdue.
2. #110 - the maximum attainable confidence score fell from +3 to +2 while the
   `high` bar stayed at >= 2. A page can now only be `high` if both remaining
   positive signals fire and no negative one does. Strong interlinking no longer
   contributes, and the orphan penalty is gone, so Lint's Structure findings have
   no coupling to the score at all.
3. #111 - the sources/<slug>.md summary page became optional in practice, so a
   citation like [^2]: [[sources/some-source]] can point at a page that was never
   created, and the \"3 or more independent sources\" confidence signal has no
   durable per-source record to be judged against. Separately, the integration
   depth expectation was silently halved at its lower bound.

## Decisions to route through /speckit-clarify (do NOT resolve them in specify)

- #109: restore the two fields in the foundation document's frontmatter standard
  (option A, boundary-correct), or remove them from Lint instead and have the
  harness compute the link count (option B, touches the guard and #42's eval
  scenario). Recommendation in the issue: A.
- #110: (A) restore both inbound-link rows, which depends on #109 landing first;
  (B) keep five signals and re-baseline the thresholds to the reduced range,
  stating in the document why the range differs from the source pattern; or
  (C) declare the omission deliberate and record the reasoning, which still
  requires B's threshold re-check.
- #111: whether \"5-15\" or \"10-15\" is the intended expectation; if the lower bound
  was deliberate, the document must say why.
- NEW, created by feature 029 and not present in any of the three issues: the
  frontmatter standard and the confidence formula now live in the foundation
  document, which an instance MAY replace wholesale. Lint's role document hard-
  depends on inbound_links and last_reviewed. Decide what happens when an
  instance's own foundation document does not define those fields - does Lint
  degrade, fail closed, or is a minimal field set product-owned regardless of the
  foundation document? This also interacts with #224 (instance-owned role
  documents, decided 2026-09-08, deferred to 2.0.0) - name the interaction, do not
  design for it.

## Constraints

- Constitution Principle V: this is instruction-file content. Do NOT reimplement
  any of it as deterministic backend code. No harness change is expected beyond
  what a decision explicitly requires (option B of #109 would be such a case, and
  is the reason it is a decision rather than a default).
- Constitution Principle V again: do NOT add deterministic tests that assert the
  wording or presence of text in an instruction file. Load-mechanism tests only.
- Constitution Principle II: classify each agent-judgment success criterion as
  high-stakes or lower-stakes explicitly. An unclassified criterion defaults to
  high-stakes and drags in a mandatory eval suite, so classify deliberately.
  Confidence scoring and frontmatter lifecycle fields are plausibly lower-stakes
  (a single wiki edit, correctable on a later pass) - argue it in the spec rather
  than assuming it.
- Whatever is decided must leave the foundation document and the three role
  documents consistent with each other. They disagreed before, which is how this
  drifted in the first place.

## Downstream, for sequencing awareness only (out of scope here)

- #38 (OKF 0.2) is `blocked` on exactly these three issues because it rewrites the
  same frontmatter block. #34 waits behind #38. Getting this wrong means editing
  the same block twice.
- #42 (inbound-link eval) is already closed and re-captured; do not re-open it.
- #110 is currently labelled `blocked` on #109. Inside this feature that block
  dissolves - sequence #109 before #110.

## Acceptance direction (from the issues, do not treat as final)

- A page created by an ingest run carries the same lifecycle fields Lint maintains.
- Lint's review-candidate finding distinguishes \"reviewed long ago\" from \"ingested
  long ago\".
- An ingest run produces a source-summary page for its source, and no citation
  wikilink in a page it wrote dangles.
- The confidence decision is applied consistently and its rationale is written
  where the next reader of the document will see it.
- All of the above verified by eval scenarios (agent behaviour), never by
  deterministic assertions on document wording."

## Clarifications

### Session 2026-09-09

- Q: Where should the two lifecycle fields (`inbound_links`, `last_reviewed`) be defined and maintained? → A: Option A — both become part of the shared frontmatter standard in the foundation document, written by agents at page-creation time. No backend change; the lint role document is untouched. Rationale: option B (harness-computed link count) only solves half of #109 — a harness can derive a link count, but `last_reviewed` is not a derivable value, so B leaves the review-window defect unfixed — and it would put wiki-page writes in the harness, which today only guarded agent tools perform. This keeps the whole feature inside instruction content (Principle V) and matches the recommendation recorded on #109.
- Q: What does an ingest run write into the two lifecycle fields? → A: Option C — an ingest run writes `inbound_links` as the count it can actually observe (the links the run itself created, plus `index.md`), and does **not** write `last_reviewed` at all. `last_reviewed` is written only by a run that performed an actual review pass, i.e. lint. Rationale: this makes the field's presence meaningful — `last_reviewed` exists on a page if and only if that page has genuinely been reviewed — and it makes lint's existing fallback to `timestamp` for pages without the field semantically correct rather than a workaround, since a never-reviewed page really is overdue measured from its ingest date. It also avoids both failure modes the other options carried: option A would have left `last_reviewed == timestamp` forever on pages ingest keeps touching (relocating #109's defect), and option B would have flagged pages ingest had just re-read. Consequence recorded in Assumptions: unlike A and B, C requires a change to the **lint** role document — lint's write of `last_reviewed` is currently permissive ("you *may* also set …", `LintAgent/system-prompt.md:328`) and only a rider on an inbound-link refresh write, so under C it must become definite or the field never materialises.

## Open Decisions — Routed to `/speckit-clarify`

This spec deliberately left four decisions open, plus a fifth (the lifecycle-field write semantics)
that D1 surfaced. D1 and the write semantics are resolved — see Clarifications above. D2 and D3 are
carried as inline `[NEEDS CLARIFICATION]` markers on the requirements they change; D4 is stated as a
requirement that holds under either answer, so the number itself is the only thing left to pick.

| # | Decision | Options | Marker |
| --- | --- | --- | --- |
| D1 | ~~Where the lifecycle fields live (#109)~~ | **RESOLVED: A** — both become part of the shared frontmatter standard in the foundation document; no backend change | FR-002 (resolved) |
| D1a | ~~What an ingest run writes into them~~ | **RESOLVED: C** — ingest writes the inbound-link count it can observe; the review date is written only by a run that actually reviewed the page, which makes lint its sole writer and requires lint's permissive wording to become definite | FR-002a, FR-002b (resolved) |
| D2 | How the confidence formula and its thresholds are made coherent (#110) | **A** — restore both inbound-link signals to the shared formula (depends on D1-A); **B** — keep five signals and re-baseline the thresholds to the attainable range, with the deviation from the source pattern stated in the document; **C** — declare the omission deliberate, record the reasoning, and still re-check the thresholds | FR-006 |
| D3 | What happens when an instance replaces the foundation document without defining the lifecycle fields | **A** — Lint degrades (skips the finding categories that need them); **B** — Lint fails closed; **C** — a minimal field set is product-owned and holds regardless of the foundation document | FR-010 |
| D4 | The integration-depth expectation (#111) | `5–15` or `10–15` pages per source | none — FR-009 requires the two documents to agree and the chosen bound to be justified in-document, which is testable either way |

D2 depended on D1: option D2-A is available only because D1 resolved to A. D1 was therefore sequenced
first, which is also how issues #109 and #110 are ordered on the board (#110's `blocked` label on
#109 dissolves inside this feature).

D3 is new. It did not exist when #109/#110/#111 were written; feature 029 created it by moving the
frontmatter standard and the confidence formula into a document an operator may replace wholesale
while the three role documents stayed product-owned. It interacts with #224 (instance-owned role
documents, decided 2026-09-08, deferred to 2.0.0): if role documents later become instance-owned
too, D3's answer is the precedent that decides whether a role document may depend on a field the
foundation document does not define. Naming that interaction is in scope; designing for it is not.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lifecycle fields exist from the moment a page is created (Priority: P1)

An operator ingests a source. The pages the ingest run creates carry the same lifecycle metadata
that the lint agent maintains, so the next lint run inherits a wiki whose pages already have the
fields it works with, rather than a wiki where every page is missing them.

Today this is not the case. A page arrives with `type`, `title`, `description`, `timestamp`,
`tags`, `confidence` and `confidence_reason`, but with neither `inbound_links` nor `last_reviewed`.
Two consequences follow, and both are visible to the operator:

- Lint's inbound-link refresh has no incremental mode available to it: on every run, every page it
  reads is a page whose field it must create. What should be "recount the pages the last ingest
  touched" is "recount the wiki".
- Lint's review-candidate finding falls back to `timestamp` when `last_reviewed` is absent — and it
  is always absent. The review window therefore measures how long ago a page was *ingested*, not how
  long ago it was *reviewed*. A page a human read yesterday still reports as overdue, and an
  operator who acts on the finding re-reads pages that need nothing.

**Why this priority**: it is the defect the other two build on. D2's option A is unavailable until
the field exists; #38 (OKF 0.2) rewrites this same frontmatter block and is blocked behind it. It is
also the only one of the three whose cost compounds — every ingest run that happens before it lands
adds another page Lint has to bootstrap.

**Independent Test**: run an ingest against a wiki fixture, then inspect the frontmatter of the
pages the run wrote; then run lint over a wiki whose pages carry a recent `last_reviewed` and an
older `timestamp`, and read the review-candidate section of its findings report. Delivers value on
its own: the review window becomes meaningful even if nothing about confidence scoring changes.

**Acceptance Scenarios**:

1. **Given** a wiki fixture and a source to ingest, **When** the ingest run completes, **Then** the
   pages it created carry the lifecycle fields the shared frontmatter standard defines, in the form
   that standard defines.
2. **Given** a wiki containing a page whose `last_reviewed` is recent and whose `timestamp` is older
   than the review window, **When** a lint run surveys it, **Then** the page is not listed as a
   review candidate.
3. **Given** a wiki containing a page whose `timestamp` is recent and whose `last_reviewed` is older
   than the review window, **When** a lint run surveys it, **Then** the page *is* listed as a review
   candidate.
4. **Given** an ingest run that updated an existing page rather than creating one, **When** the run
   completes, **Then** the page's lifecycle fields reflect what that run actually did, per the rule
   the frontmatter standard states for updates (see Edge Cases).

---

### User Story 2 - A confidence score means the same thing whoever assigns it (Priority: P2)

An operator reads a `confidence: high` page and reads the formula in the foundation document. The
score they see is one the formula can actually produce, and it is the score any of the three agents
would have assigned to the same evidence.

Neither holds today, and the second is worse than the arithmetic problem the issue reported:

- The shared formula carries five signals — two positive, three negative — so its attainable maximum
  is `+2`, while the `high` threshold is `≥ 2`. A page reaches `high` only by scoring *both*
  positives with *no* negative. The band has collapsed to a single point.
- The lint role document adds two further signals for its own use (`inbound_links ≥ 3 → +1`,
  orphan → `−1`), stating that ingest and query never see them because the count is only known once
  lint has computed it. That reasoning is sound, but its effect is not recorded anywhere: lint scores
  on a `−4 … +3` range and ingest on a `−3 … +2` range, against one shared set of thresholds. The
  same page, on the same evidence, is legitimately `high` to lint and `medium` to ingest — and
  nothing in either document tells the operator that, so a lint proposal that "corrects" an ingest
  score can be a disagreement between two scales rather than a finding about the page.
- The query role document adapts the same section narratively for synthesis pages ("a connection you
  are highly confident in … scores `high`") without reference to the numeric formula at all, which
  is a third reading of one convention.

**Why this priority**: it is a correctness defect in a convention three documents claim to share,
but its cost per occurrence is one frontmatter field on one page, correctable by a later lint
proposal. It is ranked below US1 because D2's most likely resolution depends on US1 having landed.

**Independent Test**: read the resulting documents as a reviewer and check that the attainable score
range and the thresholds are consistent and that any per-role deviation is stated where the role
that deviates is described; then run ingest and lint over the same fixture page and compare the
scores they assign and the reasons they give.

**Acceptance Scenarios**:

1. **Given** the shared confidence section after this feature, **When** a reader computes the
   maximum and minimum attainable totals from the signals it lists, **Then** every band the
   thresholds define is reachable by at least one combination of those signals.
2. **Given** a page with evidence that scores identically under every shared signal, **When** an
   ingest run and a lint run each assign it a confidence score, **Then** they assign the same score,
   or the documents state where and why the two roles' scales differ.
3. **Given** the resulting documents, **When** a reader asks why the formula differs from the source
   pattern it derives from, **Then** the answer is written in the document that carries the formula,
   not only in this spec or an issue.

---

### User Story 3 - Every source leaves a durable trace in the wiki (Priority: P3)

An operator ingests a source and can afterwards open one page that represents that source: what it
was, where it came from, and what the wiki took from it. Pages that cite the source link to that
page, and the link resolves.

Today the `sources/<slug>.md` summary page is a page *type* the foundation document defines, with a
folder, a `resource` field rule and a citation example that points at it — but nothing in the ingest
role document asks for one. Two consequences:

- A citation of the form `[^2]: [[sources/some-source]]` can point at a page no run ever created. The
  broken link is real wiki content, and it is left for lint to find.
- The "3 or more independent sources" confidence signal has no durable per-source record to be judged
  against. Whoever scores the page — at ingest time or at lint time — has to reconstruct what the
  sources were from the citation list alone.

The same document also states an integration depth ("one source typically touches 5–15 pages") that
disagrees with the source pattern it derives from ("10-15"), at the lower bound, in the direction of
doing less.

**Why this priority**: a missing source page is correctable — a later ingest of the same source, or
a lint remediation, can create it — and a dangling wikilink is already inside lint's "fix it
yourself" scope. It is ranked last because its cost is recoverable and it does not block D1 or D2.
It is nonetheless in this feature rather than a later one because it edits the same two documents,
and splitting it out would cost a second eval re-capture for no reviewer benefit.

**Independent Test**: run an ingest against a wiki fixture and check the `sources/` folder for a page
representing the source, then check that every citation wikilink in the pages that run wrote resolves
to a page that exists.

**Acceptance Scenarios**:

1. **Given** a source to ingest, **When** the ingest run completes, **Then** a source-summary page
   representing that source exists in the wiki and carries the `resource` field pointing at the
   original source.
2. **Given** the pages an ingest run wrote, **When** each citation wikilink in them is resolved,
   **Then** every one names a page that exists in the wiki.
3. **Given** the ingest and foundation documents after this feature, **When** a reader compares what
   each says about how many pages a source touches, **Then** they state the same expectation and the
   document gives the reason for the bound it states.

---

### Edge Cases

- **An ingest run cannot know a page's wiki-wide inbound-link count.** Ingest reads `index.md` and
  the pages its source overlaps with — not the whole wiki. Any `inbound_links` value it writes is a
  count over what it saw, not over the wiki. D1 must say what ingest writes: a count of the links it
  itself created, a zero placeholder whose only job is that the field exists, or nothing at all with
  the field deferred to lint. The value of the field existing (lint gains an incremental mode) is
  separable from the value of it being correct, and D1 has to pick which of the two it is buying.
- **`last_reviewed` on an update.** If an ingest run that updates a page sets `last_reviewed` to
  today, an actively-ingested page never becomes a review candidate however long it has gone without
  a human reading it — the field would then measure agent activity, which is what `timestamp` already
  measures. If it does not set it, a page's `last_reviewed` can stay older than its body. D1 must say
  which, and the reason belongs in the document.
- **A page created before this feature.** The wiki already contains pages with neither field. Whether
  lint treats an absent `last_reviewed` as "never reviewed" (a review candidate) or falls back to
  `timestamp` as it does today changes how much of the existing wiki surfaces as overdue on the first
  run after this lands.
- **An instance that replaced the foundation document.** Covered by D3 (FR-010).
- **A source that is already in the wiki.** A second ingest of the same source must not produce a
  second source-summary page under a different slug; whether it updates the existing one or
  supersedes it follows the supersession rules the foundation document already states.
- **A source with no canonical URI** (pasted text, an uploaded file with no origin URL). The
  `resource` field rule says to set it on source-summary pages; the documents must not require a
  value that does not exist.

## Requirements *(mandatory)*

All requirements below are satisfied by changes to instruction-file content — the shared foundation
document and the three role documents — unless a routed decision explicitly requires otherwise (D1's
option B is the one such case). No requirement here authorises reimplementing wiki-content judgment
as deterministic backend code (Constitution Principle V).

**How the document-consistency requirements are verified.** FR-005, FR-007, FR-008 and FR-009
constrain what the instruction documents say. Principle V forbids covering them with deterministic
tests that assert an instruction file's wording or content. They are verified by review — the
reviewer reads the documents against each other — and by the final-phase completeness audit, never
by a test. A reviewer asking for a string-matching test against these requirements is asking for a
constitutional violation.

### Functional Requirements

- **FR-001**: A wiki page created by an ingest run MUST carry the inbound-link count, so that a lint
  run over a freshly ingested wiki finds that field already present rather than absent on every page.
  The review date is *not* written at ingest time: it MUST appear on a page only once that page has
  actually been reviewed (FR-002a), so that its presence is itself the evidence that a review
  happened.
- **FR-002**: The lifecycle metadata in FR-001 MUST be defined in the shared frontmatter standard in
  the foundation document — the one place every agent reads — and every agent that reads or writes it
  MUST use that definition rather than a per-role variant. It is written by agents at page-creation
  time; no harness code computes or writes it. *(D1 resolved to A.)*
- **FR-002a**: The frontmatter standard MUST state, per field, which runs write it. An ingest run
  writes the inbound-link count it can actually observe — the links that run itself created, plus
  `index.md` — rather than a placeholder, because for a newly created page that count is normally the
  true one. An ingest run MUST NOT write the review date, on create or on update. *(D1/FR-002a
  resolved to option C.)*
- **FR-002b**: A run that performs an actual review of a page MUST record the review date on it. This
  obligation MUST be stated definitely rather than permissively, because under FR-002a the reviewing
  run is the only writer of that field: if it stays optional, the field never materialises and FR-003
  cannot hold. *(This is the one change to the lint role document this feature requires — see
  Assumptions.)*
- **FR-003**: A lint run MUST distinguish a page that has not been *reviewed* recently from a page
  that has not been *ingested* recently, and its review-candidate finding MUST be the former.
- **FR-004**: A lint run MUST NOT be required to recreate the lifecycle metadata for pages an
  earlier ingest run already wrote it for.
- **FR-005**: The confidence scoring convention MUST be internally coherent: every band its
  thresholds define MUST be reachable from the signals it lists.
- **FR-006**: Where an agent role scores confidence on a different set of signals than the shared
  convention lists, that difference and its consequence for the shared thresholds MUST be stated in
  the documents, at the point where the deviating role is described. [NEEDS CLARIFICATION: D2 — is
  coherence restored by returning the two inbound-link signals to the shared convention (option A,
  available only if D1 resolves to A), by re-baselining the thresholds to the range the five
  remaining signals span (option B), or by recording the omission as deliberate and re-checking the
  thresholds against it (option C)? Whichever is chosen must also settle whether the lint role keeps
  its private two-signal addition and whether the query role's narrative adaptation for synthesis
  pages stays as it is.]
- **FR-007**: The rationale for the confidence convention's shape — specifically why it differs from
  the source pattern it derives from — MUST be recorded in the document that carries the convention,
  where the next reader of that document encounters it, not only in an issue or in this spec.
- **FR-008**: An ingest run MUST produce a durable in-wiki record of the source it processed, and
  every citation wikilink in the pages that run wrote MUST resolve to a page that exists.
- **FR-009**: The foundation document and the ingest role document MUST state the same expectation
  for how many pages a source typically touches, and the document stating it MUST give the reason
  for the bound it states. *(D4 decides the number; this requirement holds under either answer.)*
- **FR-010**: The system MUST have a defined, documented behaviour for the case where an instance's
  own foundation document does not define the lifecycle metadata the lint role depends on.
  [NEEDS CLARIFICATION: D3 — does the lint run degrade (skip the finding categories that need the
  fields, and say so in its report), fail closed (refuse to run against a foundation document that
  does not define them), or is a minimal lifecycle field set product-owned and in force regardless
  of what the foundation document says? This decision is the precedent for #224 (instance-owned role
  documents, deferred to 2.0.0); name that interaction, do not design for it.]
- **FR-011**: After this feature, the foundation document and the three role documents MUST agree
  with each other on the frontmatter standard, the confidence convention, and the source-traceability
  expectation. Any deliberate per-role deviation MUST be stated as such in both documents that carry
  it.
- **FR-012**: Every replay eval scenario invalidated by the instruction-file changes MUST be
  re-captured before this feature's Definition of Done is declared met, and no scenario may be left
  in a stale state.

### Key Entities

- **Shared foundation document**: the single instruction document every agent loads in addition to
  its own role document (feature 029). Carries the frontmatter standard, the page types, the tag
  taxonomy, the confidence convention and the citation rules. An instance MAY replace it wholesale,
  which is what makes D3 a question.
- **Role document**: the per-agent instruction document (ingest, query, lint) stating what is
  specific to that role. Product-owned today; #224 would change that in 2.0.0.
- **Wiki page frontmatter**: the metadata block on every page except `index.md` and `log.md`. Carries
  identity (`type`, `title`, `description`), provenance (`timestamp`, `resource`, `supersedes`,
  `superseded_by`), classification (`tags`), assessment (`confidence`, `confidence_reason`) and —
  the subject of US1 — lifecycle (`inbound_links`, `last_reviewed`).
- **Source-summary page**: a `Source summary` page under `sources/`, the durable in-wiki record of
  one ingested source and the target of citation wikilinks that point into the wiki rather than out
  to a URL.
- **Confidence convention**: the signal table plus thresholds that map evidence about a page to
  `high`/`medium`/`low`. Shared, with one documented per-role addition today (lint) and one
  undocumented per-role adaptation (query).
- **Replay eval scenario**: a recorded agent run, fingerprinted against the instruction files it ran
  under. Editing those files marks the scenario stale and requires re-capture before it can score
  again.

## Success Criteria *(mandatory)*

**Stakes classification.** Every agent-judgment criterion below is classified explicitly, as
Principle II requires. All three are classified **lower-stakes**, and the argument is made here
rather than assumed:

- Each is a **frontmatter field or a single additional page on a newly written page** — an additive
  wiki edit. None of them supersedes, deletes, or overwrites existing content; none is
  guardrail-adjacent (the write scope the guarded tools enforce is unchanged by this feature, now
  that D1 resolved to A); none is safety-critical.
- Each has a **standing correction path already in the product**: lint recomputes and refreshes
  `inbound_links` on every run, lint proposes confidence corrections as findings, and a dangling
  wikilink is explicitly inside lint's "fix it yourself" scope. The cost of a wrong outcome is that
  the next lint run fixes it or proposes the fix — the exact profile Principle II names as
  lower-stakes.
- The consequence: **no formal eval suite gates this feature's DoD.** The user-reported correction
  loop satisfies these criteria — the operator observes the ingest run's output and the lint findings
  report, reports misbehaviour, the instruction file is adjusted, and the operator verifies. Capturing
  eval scenarios anyway is permitted and may well be worth it here (the documents are already
  fingerprinted by the replay suite), but under Principle II it is a choice this feature's plan makes,
  not an obligation, and a reviewer MUST NOT require one on the grounds that an LLM is involved.
- **D1's resolution to A keeps this classification intact.** Option B would have moved the link
  count into the harness, making it deterministic harness behaviour rather than agent judgment and
  raising the verification bar (hermetic tests, guarded-tool surface). With A, every outcome below
  stays agent-authored frontmatter and the lower-stakes argument holds as written.

### Measurable Outcomes

- **SC-001** *(deterministic harness guarantee)*: 100% of eval scenarios invalidated by this
  feature's instruction-file changes are flagged stale by the existing fingerprint check and
  re-captured before the DoD is declared met; zero scenarios score against a stale recording.
- **SC-002** *(deterministic harness guarantee)*: 100% of agent runs load the shared foundation
  document and the role document unchanged by this feature's edits — the load mechanism, its
  fail-closed behaviour and its per-document hash recording are unaffected. No new deterministic test
  asserts the wording or presence of any text inside those documents.
- **SC-003** *(agent judgment — **lower-stakes**)*: pages an ingest run creates carry the lifecycle
  metadata the frontmatter standard defines. Deviations are observed by the operator in the ingested
  wiki and in the lint findings report, reported, and corrected by adjusting the ingest role document;
  the operator verifies the adjustment on a later run. No numeric threshold and no eval gate.
- **SC-004** *(agent judgment — **lower-stakes**)*: a lint run's review-candidate list contains pages
  overdue for *review* and not pages merely old since *ingest* — an operator acting on the list finds
  pages that genuinely warrant a fresh look. Same correction loop as SC-003.
- **SC-005** *(agent judgment — **lower-stakes**)*: an ingest run leaves a source-summary page for its
  source, and the citation wikilinks in the pages it wrote resolve. Deviations surface as lint
  Structure findings (dangling wikilinks are already reported and fixable there) and are corrected
  through the same loop.
- **SC-006** *(review outcome, not a test)*: a reviewer reading the foundation document and the three
  role documents after this feature finds no disagreement between them on the frontmatter standard,
  the confidence convention, or the source-traceability expectation, and finds the reason for each
  deliberate deviation stated in the documents themselves.

**Observability note for the plan.** Because SC-003, SC-004 and SC-005 rely on the user-reported
correction loop, `plan.md ## Observability` must name the user-facing surface on which the operator
observes each of them (Principle V's operator loop) — the ingested wiki itself and the lint findings
report are the candidates, and the plan must say which signal the operator is expected to look at,
not merely that one is emitted.

## Assumptions

- The three GitHub issues' file and line references are stale (written 2026-08-18, before feature 029
  moved the shared conventions into the foundation document). The current-state references in the
  Input above were verified against `main` at `ade31fe` on 2026-09-08 and supersede them. The issues'
  *intent* is still authoritative; their *locations* are not.
- **Corrected by FR-002a's resolution.** The original assumption was that the lint role document
  needs no textual change for US1, because it already reads and writes both lifecycle fields. That is
  wrong under option C: lint's write of the review date is permissive today ("you *may* also set
  …", `LintAgent/system-prompt.md:328`) and is only a rider on an inbound-link refresh write, so a
  lint run that reviews a page without refreshing its count writes nothing. Since option C makes lint
  the *sole* writer of that field, the permissive wording has to become definite (FR-002b). Lint
  remains untouched for everything else in US1.
- The guarded-tool write scope is unchanged by this feature. No new tool, no new guardrail rule, no
  change to what an agent may write. D1's resolution to A is what secures this.
- The existing replay eval suite fingerprints the foundation document and the role documents, so
  editing them marks the affected scenarios stale and prints the re-capture command. This is the
  mechanism SC-001 relies on; it exists today and this feature does not build it.
- Issue #42 (inbound-link eval) is closed and its scenario re-captured. This feature does not reopen
  it; if its recording is invalidated by these edits, that is an SC-001 re-capture, not a reopening
  of the issue.
- The wiki this instance maintains uses the shipped default foundation document. D3 concerns
  instances that replaced it; no such instance is assumed to exist today, which is why D3 is a
  documented-behaviour requirement (FR-010) rather than a migration.

## Out of Scope

- **#38 (OKF 0.2) and #34.** #38 rewrites the same frontmatter block and is blocked on exactly these
  three issues. It is sequenced after this feature precisely so the block is edited once; nothing in
  #38's scope is delivered here.
- **#224 (instance-owned role documents).** Decided 2026-09-08 and deferred to 2.0.0. D3's answer is
  a precedent for it; this feature names the interaction and designs nothing for it.
- **#72 (human-in-the-loop step in ingest)** and **#113 (agent-visible raw source layer)**. Both are
  deviations from the same source pattern, recorded in `docs/llm-wiki-pattern-conformance.md`, and
  both are out of this feature's scope.
- Any reimplementation of frontmatter production, confidence scoring, source-summary creation, or
  citation resolution as deterministic backend code. Constitution Principle V; this is instruction
  content.
- Any deterministic test asserting the wording, presence, or absence of text inside an instruction
  file. Principle V permits load-mechanism coverage only.

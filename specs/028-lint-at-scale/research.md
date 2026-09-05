# Phase 0 Research: Lint at Scale (+ log.md prepend primitive)

**Feature**: [spec.md](./spec.md) | **Branch**: `028-lint-at-scale`

## R1 — Direction A vs. Direction B (issue #108's central open question)

**Decision**: Retain and extend **Direction A** (instruction-file narrowing over
`index.md`/frontmatter/search, pulling full bodies only when justified). **Direction B**
(harness-side sharding of the run into windowed sub-runs with partial-report merging) is
explicitly **not** adopted by this feature.

**Rationale**:

- ADR-030 ("Guarded Retrieval Tools", Accepted) already states the #108 problem verbatim in
  its own Context and Problem Statement and delivers the retrieval primitives
  (`search_files`, ranged `read_file` with `frontmatter_only`, read-only `batch`) that
  Direction A needs. Those primitives are live in `LintToolRegistry`
  (`backend/src/Grimoire.LintAgent/LintToolRegistry.cs:34-42`) today.
- PR #179 (spec 026's Phase N, already merged) already rewrote
  `system-prompt.md`'s "Choosing how to read" section toward frontmatter-first/search-first
  reading and measured an 86% reduction in median content tokens read on the
  `lint-at-scale-survey` eval scenario as an incidental byproduct of proving spec 026's own
  SC-011. That is direct evidence the mechanism works, not a projection.
- `AgentLoop`'s two caps (`DefaultContextTokenCap = 200_000` per turn,
  `DefaultSpendTokenCap = 1_000_000` cumulative billed) leave wide headroom once reading is
  narrowed.
- Direction B is a materially larger undertaking than the evidence available justifies —
  Constitution Principle I ("Big Design Up Front is explicitly rejected") argues against
  building it speculatively.

**Alternative rejected**: Direction B now. Revisit only if a future SC-003 re-validation
shows reading volume growing super-linearly with wiki size.

## R2 — What "coverage" means and where it's computed

**Decision**: A page is **considered** by a run if any read-shaped tool call
(`read_file` in any mode, or a page-body result from `batch`) returns content naming that
page, or the page appears as a `search_files` match. `list_files` alone does not count.
**Coverage** = distinct pages considered ÷ total pages at run start. **Coverage status** is
`complete` when every page was considered, `partial` otherwise.

**Where it's computed**: `GuardedToolExecutor` gains a `ConsideredPaths` accumulator,
sibling to its existing `TouchedPaths`/`CreatedPaths`/`DeletedPaths` (write-side) lists —
no equivalent exists for reads today. The resulting `(PagesTotal, PagesConsidered,
CoverageStatus)` triple flows through the same pipeline that already carries `DeniedActions`
and `InboundLinksRefreshed`: `GuardedToolExecutor` → `RunCompletionMetadata` → NDJSON
terminal event → `LintRunCoordinator.PersistFindingsReportAsync` →
`FindingsReportFormat.Build`.

**Alternative rejected**: reusing `FindingsReport.Partial` for this. Confirmed: `Partial`
means "the run failed/crashed mid-analysis" — a distinct axis. The new signal gets its own
field, `WikiCoverage`.

## R3 — Evaluation strategy for SC-001/SC-002/SC-003/SC-006 (read-side)

**Decision**: SC-001/SC-002 are pure harness mechanics (cap enforcement, coverage
computation) — proven hermetically with a hand-rolled fake `IModelClient` and a small ad hoc
temp-dir content root, independent of the shared eval fixture and of any real/recorded model
output. SC-003 (scale headroom) is different in kind: a claim about the *real* agent's real
behavior generalizing to a tighter budget-to-content ratio, which a scripted fake model
cannot demonstrate — this gets exactly one new addition, a second `lint-at-scale-survey`
scenario variant with a lower `ContextBudgetTokens` against the *same* existing
`LintAtScaleFixture` (~69 pages / ~50,895 tokens), run as recorded-replay. No new large
corpus is generated for this feature — an earlier draft of this plan proposed raising
`FillerPageCount` toward production/2x scale; that was rejected as disproportionate to what
the property needs proven (research.md history; see spec.md's Assumptions and
Clarifications).

**Rationale**: `Grimoire.EvalRunner`'s capture/replay machinery already runs the real
`Grimoire.LintAgent` executable against recorded model responses in the standard PR
pipeline (ADR-012). Budget-only tuning proves the same "bounded reading regardless of how
much content exists beyond the budget" mechanism as growing the corpus would, at a fraction
of the cost.

## R4 — Observability signal shape (coverage)

**Decision**: Add coverage attributes to the existing `lint_agent.run` root span (no new
span), plus one new structured log event and two new metrics — reusing the existing
`LintAgentTracing`/`LintAgentMetrics`/`LintAgentInstrumentation` wiring, proven to reach a
real exporter (Constitution Principle IV's production-composition-root requirement).

## R5 — Eval footprint proportionate to risk (Constitution v1.12.0)

**Decision**: SC-004 (cross-page findings) and SC-005 (inbound-link accuracy) are
classified **lower-stakes** per Constitution v1.12.0's Principle II tiering (ratified on
`main` while this feature was in flight, in direct response to the same eval-cost concern
the user raised about this feature's own original plan). Both are satisfied primarily by
the user-reported correction loop against the persisted Findings Report (plan.md's
Observability section names this surface, per the constitution's new Principle V
requirement); a formal recorded-replay eval suite is optional, not mandatory, for either —
at most one small, targeted fixture addition per criterion if kept at all.

**Alternative rejected**: dropping SC-004/SC-005 eval coverage to zero. The user's own words
asked for "a case or two," not none; the constitution makes a formal suite optional, not
forbidden.

## R6 — Merging issue #201 (log.md prepend cost) into this feature

**Decision**: Merged at the user's explicit direction (spec.md Clarifications, session
2026-08-25). Rationale: Lint's own instructions already read and write `log.md`
(`agents/lint/system-prompt.md`'s "Reconciling `index.md` and `log.md`" step), so #201's
already-observed production failure sits directly on a write path this feature's own User
Story 1 would otherwise leave broken. Per two further clarify answers: the fix applies to
Ingest, Query, and Lint alike (not Lint-only — #201's own production evidence is from
Ingest), and takes the shape of a new `write_file` capability rather than a distinct tool.

## R7 — The write-mode mechanism, precisely (supersedes the spec's placeholder naming)

**Context**: spec.md's Clarifications record the shape decision as "a new `write_file`
mode (named `WriteMode.Prepend` in this decision, for traceability into the eventual
ADR/plan)" — explicitly flagged there as provisional. Deep research into
`SharedFileWriteGuard.cs`/`SafetyPolicy.cs` surfaced that this name, taken literally, would
be **wrong**: `Grimoire.Domain.Guardrails.WriteMode` (`SafetyPolicy.cs:19-24`, exactly
`ReadWrite`/`CreateOnly`/`FrontmatterOnly`) is a **policy-rule-scoped** concept — which
class of edit a *path* is allowed to receive, decided by `SafetyPolicy.Evaluate` from the
agent's declared policy, invisible to the model. The new prepend capability is a
**schema-visible, per-call parameter** on the `write_file` tool itself — the same shape as
ranged `read_file`'s `offset`/`limit`/`frontmatter_only` (ADR-030 R3), which the *agent*
chooses per call, not the policy layer. These are orthogonal axes: a path with
`WriteRule.Mode = ReadWrite` (log.md's actual rule, per ADR-031's full-authority grant) can
receive either a full-content write or a prepend-entry write: the policy doesn't need a new
enum value at all.

**Decision**: The new capability is a `write_file` call-shape addition — a `mode` parameter
on the tool's JSON schema (values `"replace"` [default, current behavior, omitting `mode`
keeps existing callers working unchanged] and `"prepend"`), where `content` is reinterpreted
as *the entry only* when `mode: "prepend"`. This is implemented as a new code path in
`GuardedToolExecutor.ExecuteWriteFileAsync`/`SharedFileWriteGuard.EvaluateWriteAsync`,
**not** a new member of `Grimoire.Domain.Guardrails.WriteMode`. Naming it in code:
`WriteContentMode` (or similar, decided at implementation time) to avoid colliding with the
existing `WriteMode` type name. spec.md's "`WriteMode.Prepend`" phrasing is superseded by
this more precise mechanism — the *substance* of the clarify answer (a mode on `write_file`,
not a distinct tool) is unchanged and this is a naming/mechanism refinement discovered
during planning, not a requirements change requiring another clarify round.

**Alternative rejected**: a policy-level `WriteMode.Prepend` `WriteRule.Mode` value that
would make `log.md` structurally prepend-only, forbidding full-content writes to it
entirely at the policy layer. Rejected: this would narrow ADR-031's "Lint holds full
authority over wiki content, in both modes" grant (log.md included) — a real behavior
change requiring reconciliation with that ADR, for no benefit the schema-level parameter
doesn't already provide (nothing needs log.md's full-overwrite path to be *forbidden*,
only for a cheaper alternative to *exist*).

## R8 — Conflict safety under prepend: no baseline needed, and no denial path either

**Context**: issue #201 flagged "under the lock, a prepend is safe without a baseline —
decide this explicitly" as an open design point. Research confirms
`SharedFileWriteGuard.EvaluateExistingTargetChecksAsync` already re-reads current bytes
fresh from disk (`:207`) while holding the per-target `CrossProcessFileLock` — the *CAS
comparison* uses this fresh read against the caller's `_readHashes` baseline; the lock
itself is what actually prevents interleaving.

**Decision**: Prepend-mode writes read `log.md`'s current content fresh, under the lock,
at write time — then concatenate `entry + currentContent` and commit atomically. This
requires **no prior `OnReadFile` baseline and no CAS comparison at all** for prepend writes,
and this is not a weaker guarantee than `ReadWrite` mode's CAS check — it is a *different,
sufficient* one. Two agents racing to prepend are simply serialized by the lock: each reads
the latest content in lock-acquisition order and prepends onto it. Nothing is lost, nothing
is silently clobbered, and — unlike full-content `ReadWrite`, where a stale base genuinely
can overwrite a concurrent change — there is no scenario where a prepend write is
legitimately "stale," because it never asserted anything about prior content in the first
place.

**What this means for FR-012/SC-008**: FR-012's requirement ("continue to detect and deny a
stale concurrent write") and SC-008's wording ("race a stale concurrent write... denied")
describe the *safety property that must hold* (no corruption, no lost entries under
concurrent writers), not a specific mechanism. For prepend mode specifically, that property
is satisfied by construction (lock-serialized fresh-read-then-concatenate) rather than by an
explicit denial path — there is no stale-write scenario left to deny. The Feature-Scoped
Invariant test for this (tasks.md, later) proves the *absence* of a corruption/loss scenario
under concurrent prepends, not a `write_conflict_stale_read`-style denial. `ReadWrite`
mode's existing CAS/denial behavior is completely unchanged by this feature.

**Alternative rejected**: requiring prepend callers to supply a prior-read hash anyway (for
symmetry with `ReadWrite`). Rejected: it would reintroduce exactly the "must have read the
whole file first" cost this feature exists to remove, for a staleness scenario that cannot
occur under the lock-serialized design.

## R9 — Precedent check against ADR-028's own rejected option

**Context**: ADR-028's "Considered Options → Ordering, option 3" explicitly rejected a
dedicated `prepend_log_entry` structured tool, reasoning "the harness would own the file's
assembly, and it buys nothing over a suffix check on a `write_file` the agent already
composes."

**Decision**: This feature's design does not contradict that rejection. ADR-028 rejected a
tool where the *harness* would own composing/formatting the log entry from structured
fields (title, date, body) — this feature's `mode: "prepend"` parameter still has the
*agent* compose the entry's full text (heading + paragraph) exactly as it does today; the
harness only concatenates two strings it's handed (`entry + currentContent`) rather than
requiring the agent to also retransmit `currentContent`. The new ADR amending ADR-028 must
state this distinction explicitly so a future reader doesn't read it as contradicting
ADR-028's own precedent.

## R10 — Scope boundaries confirmed

- **`index.md` is untouched.** ADR-017's `ValidateCatalogEntryFormat`/the `_indexPath`
  branch is a structurally separate check (per-line `- [...]` shape, not suffix/prefix
  ordering) with no relationship to `log.md`'s prepend mechanism. Confirmed genuinely
  orthogonal — the new ADR touches only the `log.md` code paths.
- **`WikiLogCoverageObserver` is unaffected.** It derives its outcome purely from
  `GuardedToolExecutor.TouchedPaths`/`ActivityLogWritten`, populated identically regardless
  of which write mode committed the change.
- **ADR-030 needs no amendment.** It is scoped entirely to retrieval (`search_files`,
  ranged `read_file`, `batch`) and does not touch `write_file`/`WriteMode` at all — this
  reverses spec.md's Assumptions bullet naming ADR-030 as one of the ADRs this feature
  amends. The new ADR instead amends ADR-017 (mechanism) and ADR-028 (prepend-ordering
  check, retargeted to validate the supplied entry directly rather than a computed "head").
  ADR-011 (shared runtime, per-agent `ToolRegistry` declaration) needs no amendment either,
  once confirmed: all three per-agent registries already declare the identical shared
  `WriteFileDefinition` constant (`LintToolRegistry.cs:34-42`,
  `IngestToolRegistry.cs:15-20`, `QueryToolRegistry.cs:20-25`) — widening that one constant's
  schema reaches all three simultaneously with no registry-file change, so there is no new
  registry-scope *decision* for ADR-011 to record (unlike ADR-030 R6, which deliberately
  scoped three genuinely new tools to Lint-only).
- **Line-number citations drift fast in this codebase.** Issue #201's own
  `GuardedToolExecutor.cs:594-598` citation for the atomic-commit path is already stale
  (current: `WriteFileAtomicallyAsync`, `:607-651`, `File.Move` at `:636`). The new ADR and
  plan.md cite method names as the stable reference, with line numbers as a secondary,
  expected-to-drift pointer.

## R11 — Constitution v2.0.0 (2026-08-25): ADR-035 is an extension, not an amendment

**Context**: `main` was independently amended to Constitution v2.0.0 while this feature was
in flight (after ADR-035 was originally drafted using the pre-v2.0.0 `Amends`/`Amended by`
convention this document's R9/R10 above describe). v2.0.0 requires every ADR to decide
exactly one aspect, retires partial `Amends`/`Amended by` for ADRs drafted from that
amendment forward, and introduces a decisive Invalidation test: would honoring the new
decision reverse, narrow, or contradict what the earlier ADR actually decided? If no, it is
an **extension** — the earlier ADR's status MUST NOT change, not even in part.

**Decision**: ADR-035 is unambiguously an extension of ADR-017 and ADR-028 under this test —
its own Decision Outcome states plainly that ADR-028's ordering guarantee "stands exactly as
decided" and ADR-017's heading/paragraph checks are "unchanged in substance." ADR-035 was
rewritten accordingly: its frontmatter now carries `supersedes: null` / `superseded_by: []`
per the Mandatory ADR format, its opening note reads `**Extends** ADR-017, ADR-028 — no
supersession` rather than `Amends`, and the `Amended by ADR-035` blockquotes this feature
had added to ADR-017 and ADR-028 themselves are removed — an extension carries no
obligation on the extended ADR's side at all, not even a cross-reference. R9's "the new ADR
amending ADR-028 must state this distinction" (above) and R10's "amends ADR-017... amends
ADR-028..." language are superseded by this entry; both remain in this document as the
historical record of what was decided before v2.0.0 landed, per this project's own
research.md convention (see R5's identical treatment of the v1.12.0 amendment).

**Not affected**: the pre-existing, pre-v2.0.0 one-sided-link gap this feature separately
closed between ADR-028 and ADR-031 (ADR-028's O4 statement, made false by ADR-031, never
recorded as such) stays on the old `Amends`/`Amended by` convention — both of those ADRs
predate v2.0.0 and are grandfathered under Governance's non-retroactivity clause; fixing
their own historical cross-reference is maintenance on grandfathered content, not a new ADR
drafted under the retired pattern.

**Alternative rejected**: keeping ADR-035's original `Amends` framing on the theory that
this feature's own `/speckit-plan` began before v2.0.0 was ratified. Rejected because this
feature is still unmerged and under active revision — the same reasoning this project
already applied when v1.12.0 landed mid-flight (R5 above) — and because the user's own
request that triggered this rebase ("überprüfe die spec und vor allem den plan anhand der
anpassungen") asked directly for the plan to be checked against exactly this kind of
upstream adjustment.

## R12 — Second ADR restructuring (2026-08-27): this feature's ADR renumbered ADR-035 → ADR-051

**Context**: A second, independent, and much larger ADR restructuring landed on `main`
while this feature was still in flight — separate from, and later than, the Constitution
v2.0.0 amendment R11 documents above. That restructuring: (1) superseded the original
ADR-028 ("Agent-Owned Activity Log — Prepend-Only Ordering and Removal of Harness
Authorship") wholesale with a new ADR, also numbered **ADR-035**, but decisively
differently scoped — "Agent-Exclusive Authorship of the Wiki Activity Log," which decides
*who* may author `log.md` content (the agents, exclusively, structurally enforced by
removing `Grimoire.AgentRuntime.WikiLog` from the guarded-write-boundary allow-list), not
the prepend-ordering mechanism this feature's own ADR had been drafted under that same
number to extend; and (2) deprecated ADR-017 ("Structural Format Enforcement for `log.md`
and `index.md` Entries") entirely, with `status: deprecated` and a `reason` explaining that
its format-enforcement content is feature-scoped, owned by
`specs/025-agent-owned-log/contracts/activity-log-write-contract.md`, not an architectural
decision. `main`'s new ADR-035 makes this explicit in its own "More Information" section:
"The prepend-only ordering check on activity-log writes is deliberately **not** decided
here: it is feature-scoped format content whose contract lives in
[the activity-log write contract] ... not an architectural decision."

This collided directly with this feature's own ADR, which had been drafted (post-R11) as
"ADR-035, extending ADR-017 and ADR-028" — the same number `main` had independently
assigned to an unrelated decision, and a scope (format validation, `index.md` non-
involvement) that `main`'s own restructuring had just relegated to contract-document status
for the *identical* content class.

**Decision**: This feature's ADR was renumbered to **ADR-051** (the next free number after
ADR-050) and rewritten under the Invalidation test (Constitution Principle III) to:

- Re-point its "Extends" note from the now-Superseded ADR-028 / now-Deprecated ADR-017 to
  `main`'s new ADR-035 (agent-exclusive authorship) — the harness gains a cheaper way to
  *commit* an agent-authored entry, never authorship of it, so this is still a pure
  extension, no supersession, of the ADR that now actually governs the log's authorship
  question.
- Narrow its Decision Outcome to keep only the two rules that are genuinely
  guarded-tool-boundary-capability material: R1 (the `write_file` schema gains an optional
  `mode` parameter) and R2 (no-baseline, lock-serialized prepend dispatch) — the two
  Boundary Rules that gate Phase 0 of `tasks.md`.
- Drop what had been R3 (format-validation retargeting) and R4 (`index.md` non-involvement)
  as separately-tagged ADR rules. By the exact same logic the restructuring itself applied
  to ADR-017 project-wide, this content is feature-scoped format/scope content, not a
  guarded-tool-boundary decision — it now lives in
  `specs/028-lint-at-scale/contracts/log-prepend-write.md` as plain contract prose, cross-
  referencing this ADR rather than being decided by it.
- Drop the previously-planned ADR-028/ADR-031 one-sided-link gap-fix (R9/R10 above). It is
  now moot: ADR-028 is wholly Superseded, so a partial-link correction on a fully-retired
  document serves no purpose to a reader, who is directed to `main`'s ADR-035 instead.

**Rationale**: this is the same "which content is ADR-worthy vs. contract-worthy" line
`main`'s own restructuring drew, applied consistently to this feature's ADR rather than
carving out an exception for it. ADR-030 and ADR-016 remain the positive precedent (a
guarded-tool *capability* change — new tool, new schema field, new dispatch branch — is
legitimate ADR material); ADR-017's deprecation is the negative precedent (a pure
format/content rule for one file's shape is not, once a contract document exists to own
it). Every reference to "ADR-035" in this feature's own spec/plan/data-model/contract
documents that meant *this feature's* ADR was renamed to ADR-051 accordingly; references to
"ADR-035" that mean `main`'s agent-exclusive-authorship ADR were left as ADR-035 and,
where ambiguous, disambiguated with "`main`'s ADR-035" or similar. R9's and R10's own
"amends ADR-028"/"amends ADR-017" language (written under the pre-R12 numbering) remains in
this document unedited, as the historical record of what was decided before this second
restructuring landed — this project's established append-only convention for research.md
(R5's treatment of the v1.12.0 amendment; R11's own treatment of R9/R10 for the *first*
restructuring).

**Alternative rejected**: keeping this feature's ADR at number "035" and asking the
restructuring's authors to renumber their own new ADR instead. Rejected — `main`'s
restructuring had already merged by the time this rebase happened; renumbering a
merged, Accepted ADR on `main` to accommodate an unmerged feature branch would be exactly
backwards, and ADR numbers are permanent once assigned (Constitution Principle III,
"Lifecycle and frontmatter contract").

## R13 — ADR-051 retracted: the write-side fix never needed an ADR (Constitution v2.1.0)

**Context**: with ADR-051 renumbered and re-pointed per R12, the PR author reviewed it
directly on PR #193 and rejected the premise of drafting an ADR here at all — in four
review comments, independent of either restructuring above: "This still is no adr, it is
just a feature description, no new technical aspekt or a system boundry. It is just
another tool and a guardrail"; "Wie vermeiden wir zukünftig solche ADRs? ... wenn wir für
jedes Tool einen adr machen, sind die adrs viel zu spezifisch und es ändern sich keine
technologischen Entscheidung durch den adr, es werden nur neue aspekte des eigentlichen
Features ... hinzugefügt"; and "No extensions allowed with new constitution 2.0" —
rejecting even the `Extends ADR-N` framing (R11/R12's own mechanism) as a way to justify
this being an ADR at all.

**Decision**: Agreed, and generalized into a constitution amendment (v2.0.0 → v2.1.0,
Principle III's new "Guarded tool surface ADR-triggering test") rather than resolved as a
one-off exception for this feature. Comparing against the project's own actual precedent
settles the criterion cleanly: ADR-030 earned its own ADR by introducing new tool *names*
(`search_files`, `batch`); ADR-016 earned one by adding a new *value* to the policy-level
`WriteMode` enum. This feature's write-side fix did neither — it widened one existing
tool's call shape with an optional, default-preserving `mode` parameter, granting no
access `write_file`'s existing contract did not already cover (the tool could always fully
overwrite `log.md`; prepend is only a cheaper path to the same end state). Generalized:
a guarded-tool-surface change needs a new ADR only for (a) a new tool name, (b) a new/
changed policy-level enum value, or (c) a new external-system dependency — none of which
this feature introduces.

**Consequences for this feature**:

- `docs/adr/ADR-051-write-file-prepend-mode-for-log-md.md` is deleted (never merged to
  `main`, so no supersession/deprecation tombstone is needed — the number is simply
  unused, consistent with the R12 precedent that an in-flight, unmerged ADR number is
  renumberable).
- `docs/adr/index.md`'s ADR-051 row is removed.
- ADR-051's two rules — R1 (schema addition) and R2 (no-baseline, lock-serialized
  dispatch) — are recorded in `plan.md`'s Architectural Constraints & ADRs section as
  Feature-Scoped Invariants FSI-1/FSI-2, covered by the same classicist integration tests
  that were already planned (`SharedFileWriteGuardPrependTests.cs`), now with no
  `Grimoire.ArchTests`/Phase 0 structural-test obligation at all.
- ADR-006 (the guarded tool-use loop and write journal, already Accepted) is the ADR that
  governs this feature's write-side change — as a constraint it was already reading and
  writing through, not a boundary this feature itself decides.
- `main`'s ADR-035 (agent-exclusive activity-log authorship) is confirmed unaffected and
  needs no `Extends` cross-reference from this feature at all, since this feature no
  longer drafts an ADR to carry one.

**Rationale**: this is the same instinct that drove R5 (proportionate eval footprint) and
the v1.12.0/v2.0.0 amendments before it — verification and documentation rigor should
track what actually changed, not accumulate ceremony per touched file. An ADR for every
tool-schema widening would mean this project drafts an ADR per feature almost by
definition, since nearly every feature that touches the guarded tool surface adds some
parameter to some tool; that dilutes what "Accepted ADR" signals for the genuine
boundary/technology decisions ADR-006, ADR-030, and ADR-016 actually represent.

**Alternative rejected**: keeping ADR-051 as a `declined` (or `deprecated`) tombstone
entry in `docs/adr/index.md` for historical traceability. Rejected: ADR-051 was never
merged to `main` — it existed only inside this still-open PR — so there is no shared
history to preserve a tombstone against; this research.md entry and the PR's own review
thread already carry that record. A permanent index row for a document that never
reached the shared `docs/adr/` history the index describes would misrepresent what the
index is for.

## R14 — Constitution amendment reverted: the existing Principle III test was already sufficient

**Context**: R13's decision generalized the ADR-051 retraction into a constitution
amendment (v2.0.0 → v2.1.0, a new "Guarded tool surface ADR-triggering test" subsection).
The PR author reviewed this directly and rejected it: "I don't think we should ammend the
constitution here, and i don't see why an adr, like the one you created is needed with
Constitution 2.0.0" — and, on the added subsection's own text: "That is specific no reason
for an adr! There are only two reasons 1. a new or changed system boundary 2. a new or
changed technical desicion" and "Also no reason for an adr."

**Decision**: Agreed. Constitution v2.0.0's existing Principle III language already states
the complete test, verbatim: "Single-aspect ADRs; no feature content. Each ADR MUST decide
exactly one aspect — one genuine system boundary or one technology choice..." R13's own
three-part enumeration (new tool name / new-or-changed policy enum value / new external
dependency) was not a new rule — it was a narrower restatement of this exact pre-existing
two-part test, scoped to one recurring case. Codifying it as a standalone constitution
subsection risked exactly the failure mode this whole feature's ADR history has been
about: an overly specific rule that can drift from the general principle it was meant to
illustrate, rather than the general principle itself doing the work. The constitution
amendment is reverted in full: `.specify/memory/constitution.md` and
`.specify/templates/plan-template.md` are restored byte-identical to their pre-amendment
(v2.0.0) state.

**Consequences**: ADR-051's retraction (R13) stands unchanged on its own merits — an
optional, default-preserving call-shape parameter on an already-existing tool decides
neither a new system boundary nor a new technology choice, so it still fails Principle
III's existing test. Every artifact in this feature (`plan.md`, `spec.md`,
`checklists/requirements.md`, `contracts/log-prepend-write.md`, `quickstart.md`) is
reworded to cite that existing test directly, dropping every reference to "Constitution
v2.1.0" or a "Guarded tool surface ADR-triggering test" that no longer exists.

**Rationale**: the same proportionality concern that has run through this whole feature
(research.md R5's eval footprint, R13's ADR retraction) applies one level up here: don't
add governance ceremony beyond what a single feature's edge case actually needs. The
existing two-part test already answered the question; a new, project-wide constitution
subsection to restate it for one tool-surface case was itself the kind of unnecessary
specificity the user has been pushing back on throughout.

**Alternative rejected**: keeping the v2.1.0 amendment on the theory that a named worked
example helps future authors apply the general test consistently. Rejected directly by
the user — the general test is not, in their judgment, ambiguous enough to warrant a
named special case, and a special case invites the same proliferating specificity this
session's arc has otherwise been about removing.

## R15 — `log.md` format/ordering checks reclassified: deny → monitor (Clarifications 2026-08-27)

**Context**: reviewing this feature's write-side design directly, the user made a further
architectural call, stated plainly: "It is again an llm decision what to do with the log,
we just give the features/tools to use and add system level security by adding guardrails
to it. The log entry format is nothing we validate, we could monitor it." Clarified via two
structured questions: (1) scope — both the existing full-content write path and the new
prepend-mode path, not just this feature's new capability; (2) the prepend-only ordering
check specifically — also monitor-only, not kept as a security-relevant exception (this was
the recommended, more conservative option; the user picked the more permissive one anyway,
with the consequence — order violations allowed through, not denied — stated plainly in
the question).

**Code-level grounding**: `SharedFileWriteGuard.EvaluateExistingTargetChecksAsync`
(`backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs:194-244`)
composes four checks in sequence for every `ReadWrite`-mode write, short-circuiting on the
first non-null reason: `WriteMode.FrontmatterOnly` target-missing, compare-and-swap
(`write_conflict_stale_read`, ADR-015), `FrontmatterOnly` body-preservation (ADR-016), and —
only when the canonical target is `log.md` or `index.md` — the format/ordering check
(`ValidateLogEntryFormat`, ADR-017/ADR-028 lineage, called identically for full-content
writes at line 247 and reused by this feature's own prepend-mode path per
`contracts/log-prepend-write.md`). This confirms two things: (a) the format/ordering check
already applies today, identically, to full-content `mode: "replace"` writes — this
feature's prepend mode was never going to introduce a *new* denial path, only reuse the
existing one; and (b) it is a structurally separate branch from the CAS/FrontmatterOnly
checks, so relaxing it does not touch `write_conflict_stale_read` or `FrontmatterOnly`
enforcement at all — those remain exactly as they are (FR-012/SC-008 unaffected).

**Decision**: `EvaluateFormatIfTarget`'s result, when the target is `log.md` specifically,
no longer contributes a denial reason to `EvaluateExistingTargetChecksAsync`'s returned
tuple. The write commits regardless of whether `ValidateLogEntryFormat` finds a deviation;
the deviation (if any) is instead recorded via a new structured log event and counter
metric. `index.md`'s catalog-format check is unaffected (out of scope, per this feature's
existing FR-013) — this reclassification is `log.md`-specific, not a change to
`EvaluateFormatIfTarget`'s shared plumbing beyond how its `log.md` branch's result is used.

**Signal naming**: grounded in the existing, shipped
`Grimoire.AgentRuntime.WikiLog.WikiLogMetrics`/`WikiLogEvents` components (025-agent-owned-log/
ADR-028), which already carry exactly this kind of "harness noticed something about the
activity log, without deciding wiki content" signal (`wiki.log.unlogged_change_total` /
`wiki.log.change_not_logged`). The new metric (`wiki.log.format_deviation_total`) and log
event (`wiki.log.format_deviation`, WARN) extend these existing components rather than
introducing a new telemetry surface, matching their exact naming and parameter-passing
conventions (a `Meter`/`ActivitySource` instance passed in per calling agent process, per
ADR-005/ADR-013's frozen-meter-identity rule — never owned by `WikiLog` itself). The
existing `guardrails.format_validate` span (`SharedFileWriteGuard.cs:292`) still fires,
with its `outcome` tag retargeted from "allowed/denied" to "conforming/deviated" — the span
is a per-call trace attribute, not a substitute for an aggregable counter metric or a
durably queryable log event, so this feature adds the latter two rather than treating the
existing span as sufficient.

**Consequences**: FR-011 reverses (writes are never denied for format/ordering shape).
FR-012/SC-008 narrow to the concurrency-safety property alone (unaffected, since it's a
structurally separate lock/CAS mechanism). A new FR-016/SC-009 pair requires the observable
signal, classified lower-stakes agent-judgment per Constitution v1.12.0 (a malformed or
misordered entry is a single, correctable wiki edit — the correction loop, reading the new
signal, is sufficient; no mandatory eval suite).

**Rationale**: this is the same "harness owns capability and safety, agent owns content"
line Constitution Principle V already draws project-wide (Agentic core: "judgment about
wiki content... index/log content — MUST be exercised by an LLM agent"; Guardrails at the
tool boundary: mediates "write and read *capabilities*", not content shape) — applied,
for the first time in this feature's history, to a check that predates the feature itself
rather than to something this feature was drafting fresh. Unlike the ADR-051 saga (R11-R14,
which was about *how a decision gets recorded*), this is a genuine *behavior* change:
`log.md` writes that would have failed today succeed after this feature ships, on both
write paths.

**Alternative rejected**: scoping the relaxation to the new prepend-mode path only, leaving
the existing full-content path's denial behavior untouched. Considered and explicitly
rejected by the user (first clarifying question) — the underlying judgment ("format is the
agent's, not the harness's") does not depend on which call shape produced the write, and a
mode-scoped relaxation would leave the identical content judgment enforced differently
depending on which tool parameter an agent happened to use, an arbitrary distinction.

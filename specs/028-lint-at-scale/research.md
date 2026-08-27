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

# Idea: Stakeholder-Centric Spec-as-Database

> **Role of this document.** Decision context (source material), in the sense of the
> Document Map in `CLAUDE.md`: it is **not binding** for SDD and MUST NOT be cited as a
> requirement in any spec, plan, or ADR. Its declared reader is whoever next
> reconsiders (a) the ordering/freeze discipline of `spec.md` relative to
> `/speckit-plan`, or (b) whether a queryable, cross-feature index over `specs/` is
> worth building. Statements here become enforceable only once extracted into the
> constitution or an Accepted ADR — this document explicitly defers that step (see
> §6, Decision).

**Date:** 2026-08-25
**Trigger:** User request to explore moving `specs/` content into "a database of user
stories and specs" as a single source of truth, explicitly in the direction of the
"stakeholder-centric" flavor of spec-driven development described in
[Two Flavors of Spec-Driven Development, and Why I Clearly Prefer One](https://martinelli.ch/two-flavors-of-spec-driven-development-and-why-i-clearly-prefer-one/)
(Gregor Martinelli).

**Verdict (TL;DR):** Do not implement now. The idea splits into two separable
questions with very different answers. Adopting a more *stakeholder-centric spec
style* is worthwhile and largely already present in Grimoire's existing Spec Kit
flow — the concrete gap is a missing explicit freeze/sign-off point for `spec.md`
before `/speckit-plan` starts. Building a literal *database* as spec/story
single-source-of-truth is not worthwhile — it directly re-opens a question this
project has already answered (ADR-003, ADR-014) in favor of git-tracked markdown, and
would need a new ADR to introduce as infrastructure under Principle IV even in its
weakest, non-authoritative form. Kept here as decision context; not moved into any
binding artifact.

---

## Original request (verbatim, German)

> ich möchte die inhalte der aktuellen spec-verzeichnisse in eine datenbank der
> user-stories und specs überführen, die als single-source-of-truth verwendet werden
> kann:
>
> in Richtung Stakeholder-Centric optimiert für Korrektheit — die Spec ist das
> eigentliche Artefakt, das Missverständnisse zwischen Beteiligten verhindert.

## 1. What the article actually argues

Martinelli distinguishes two flavors of SDD:

- **Task-driven / developer-centric** (his framing of tools like Kiro and GitHub
  Spec-Kit): idea → generated spec → decomposed tasks → AI-assisted code. Specs are
  intermediate artifacts optimized for "idea to working code" speed.
- **Stakeholder-centric**: specs are the central project artifact, built from
  observable-outcome use cases rather than technical decomposition, representing
  shared understanding between stakeholders and developers. Workflow: vision →
  requirements → system use cases → entity model → architectural constraints, with
  implementation following only once these stabilize.

He states his preference for the second explicitly, for long-lived, business-critical
systems maintained across teams and years.

**Important correction to the framing that triggered this idea:** the article's
central claim is not about a storage backend. It is that "the memory of the project
is not stored in the model's context window, it is stored in version-controlled
documentation" — i.e., specs must be durable and versioned, not disposable
AI-prompt scaffolding. Nothing in the article calls for a database; his own
description ("version-controlled documentation") describes exactly git-tracked
markdown, which is what Grimoire already does. The "database" framing came from the
triggering request, not from the source material — worth naming explicitly so a
future reader doesn't treat "put specs in a DB" as something the article recommends.

## 2. Two separable questions

The request conflates two independent decisions:

- **(A) Spec *style/discipline*:** should `spec.md` be authored and gated more like
  Martinelli's stakeholder-centric flow (use-case-first, frozen before planning,
  explicit stakeholder sign-off)?
- **(B) Spec *storage*:** should spec/story content live in a queryable database
  instead of (or alongside) the current per-feature markdown files?

These have different costs and, as shown below, different answers.

## 3. Option A — Stakeholder-centric spec discipline (worthwhile, mostly already present)

Comparison of Martinelli's stakeholder-centric steps against Grimoire's current Spec
Kit flow:

| Martinelli's step | Grimoire's current equivalent |
| --- | --- |
| Vision → Requirements | `spec.md` (User Scenarios, `FR-###`) |
| System Use Cases (observable behavior) | User Story blocks with Given/When/Then acceptance scenarios |
| Entity Model | `data-model.md` per feature |
| Architectural constraints stabilize before implementation | ADR-gate between `/speckit-plan` and `/speckit-tasks` (Constitution Principle III) |
| Requirements stabilize before implementation proceeds | `/speckit-clarify` |

Much of the structure the article asks for is already in place. Grimoire's own
`CLAUDE.md` already codifies a closely related discipline: a requirements change
discovered mid-stack must go through `/speckit-clarify` on the spec layer, never as a
direct edit on `plan.md`/`tasks.md` (the Feature 026 lesson). That rule is exactly
stakeholder-centric thinking — it exists specifically to keep `spec.md` the honest,
single place requirements live.

**The actual gap:** that rule is currently scoped to changes discovered *mid-stack*
(after planning has started). Martinelli's flow implies the same discipline should
apply from the start — `spec.md` should have an explicit freeze/stakeholder-sign-off
point *before* `/speckit-plan` is invoked at all, not only protection against drift
after the fact.

**If this is revisited**, the concrete, low-cost path (no new ADR needed — no new
structural boundary, infrastructure, or cross-cutting concern is introduced) would be:

1. Generalize the existing "requirements change → `/speckit-clarify`" rule in the
   constitution so it applies to the full spec lifecycle, not just mid-stack changes.
2. Add an explicit status/sign-off field to `.specify/templates/spec-template.md`
   (e.g. a "Reviewed" marker) that `/speckit-plan` treats as a precondition, making
   today's implicit freeze point visible and checkable.
3. No new artifact, no new persistence: `specs/<feature>/spec.md` remains the sole
   source of truth; traceability continues via the existing `FR-###`/`SC-###` join
   keys that `/speckit-analyze` already matches between `spec.md` and `tasks.md`.

## 4. Option B — Spec database as single source of truth (rejected as stated)

This directly conflicts with decisions this project has already made deliberately,
not accidentally:

- **ADR-003** (Domain vs. Operational State Persistence, Accepted): domain-like
  content is git-tracked plain files; SQLite is reserved for the Hub's internal
  operational bookkeeping only. The general rule it establishes — "git-tracked plain
  files for anything the user should be able to read or edit directly, a small
  embedded store for anything that is purely internal runtime bookkeeping" — applies
  directly to spec content.
- **ADR-014** (Query Conversation Records): considered and **explicitly rejected**
  the near-identical shape of this idea ("server-side store in SQLite with the
  markdown record as a rendered projection") in favor of plain markdown as the record.
- **Constitution Principle V**: durable state MUST live in files; the embedded
  database "MUST NOT be required for durable persistence" — deleting it must lose no
  durable record. A spec/story database serving as SSOT would violate this by
  construction.
- **Constitution Principle IV**: any custom infrastructure (a new database) requires
  an approved ADR before implementation begins, and would have to argue against the
  ADR-014 precedent to be accepted.

**Not even the weakest form is free.** Even a non-authoritative, regenerable,
read-only *projection* (e.g., a derived index over `specs/*/spec.md` for cross-feature
search/reporting, files remaining the SSOT) still counts as new infrastructure under
Principle IV and would need an ADR that explicitly frames it as disposable and
rebuildable, never authoritative — to avoid quietly becoming a second source of truth
that drifts from the files. No such need has been identified yet; nothing today asks
"which features touch entity X" or similar across all 27 feature folders.

## 5. Why "correctness via stakeholder alignment" doesn't argue for a database

The user's own stated motivation — optimizing for correctness by preventing
misunderstanding between stakeholders — is served by the spec being **legible,
versioned, and diff-reviewable**, not by it being queryable. Git-tracked markdown
already gives per-line blame, PR review, and diff-based change visibility for free;
a database would trade that away (or require re-deriving it) without addressing the
actual failure mode Martinelli describes (specs treated as disposable AI context).
The alignment goal is closer to Option A (freeze/sign-off discipline) than to Option B
(a different storage engine).

## 6. Decision

Not implementing either option now, per explicit user instruction ("ich will die
Gedanken dazu behalten, aber nicht umsetzen aktuell"). This document exists solely to
preserve the analysis for a future session. Per the Document Map, nothing here is
binding; if Option A is picked up later, it goes through `/speckit-constitution` (for
the lifecycle-wide freeze rule) and a `.specify/templates/spec-template.md` edit — no
ADR required. If Option B is ever revisited, it starts with drafting an ADR that
addresses why the ADR-014 precedent no longer holds, not with implementation.

# Specification Quality Checklist: Prompt Conformance — Lifecycle Fields, Confidence Signals, Source Traceability

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-08
**Last validated**: 2026-09-09 (after `/speckit-clarify`, the Copilot review, and the D5 follow-up clarification)
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Constitution Alignment (project-specific)

- [x] Every agent-judgment success criterion is explicitly classified high-stakes or lower-stakes
      (Principle II) — all three are classified lower-stakes with the argument stated, not assumed
- [x] No success criterion attaches a 100% deterministic guarantee to an agent-judgment outcome
- [x] Spec requires no deterministic test asserting instruction-file wording or content
      (Principle V) — stated as an explicit Out of Scope item and in the Requirements preamble
- [x] Spec requires no reimplementation of wiki-content judgment as backend code (Principle V) —
      reinforced by FR-010a, which forbids implementing the degradation behaviour as a harness check
      on instruction-document content
- [x] Document-consistency requirements name their verification path (review + completeness audit),
      so they cannot be misread as demanding a string-matching test

## Notes

**21/21 checklist items pass and the spec is ready for `/speckit-plan`.** No `[NEEDS CLARIFICATION]`
markers remain, every functional requirement has acceptance coverage (FR-010 gained a scenario during
the review round), and D5 — the one question that was blocking planning — is resolved.

**Requirements added during clarification**: FR-002a/FR-002b (which run writes each lifecycle field,
and the obligation on a reviewing run to record the review date definitely rather than permissively),
FR-006a (the query role's synthesis-page confidence treatment must be reconciled rather than left as
an undeclared third reading), FR-010a (none of the degradation behaviour may be a harness check on
instruction content).

**Requirements added or corrected after the 2026-09-09 Copilot review**, which found twelve issues,
eleven of them correct:

- FR-002 no longer claims all lifecycle metadata is written at page-creation time, which contradicted
  FR-002a/FR-002b.
- FR-002a now defines the inbound-link count canonically and marks a creating run's value explicitly
  provisional; FR-004 no longer over-claims that lint is spared recomputation (it is spared the
  wiki-wide *create*, not the *read*).
- FR-002c is new: the lifecycle rule must cover query-created pages too, or state an exception. D1a
  named only ingest and lint, which left query-created synthesis pages unspecified.
- FR-007 and FR-009 no longer require the instruction documents to justify themselves by comparison
  to `docs/foundational/llm-wiki-*`, which `CLAUDE.md` classifies as source material never to be
  cited as requirements. The provenance stays in Clarifications as decision history.
- The lower-stakes argument no longer claims nothing overwrites existing content — FR-002a and
  FR-002b both write to existing pages. It now argues from reversibility and from never touching page
  body content.
- SC-002's "load the documents unchanged by this feature's edits" was impossible as written; SC-003
  pointed the correction loop at a fixed document rather than the one owning the violated rule.
- The US1 review-window scenarios now specify `low` confidence, without which they passed or failed
  vacuously; US3 gained a scenario for sources with no canonical URI.
- A factual error in the D2 rationale was corrected in place with a dated note: query-created pages do
  not have "zero inbound links by construction". The decision is unaffected.

**D5 is resolved (option B) and no longer blocks planning.** A review is an act of observation, not
of mutation: a run that produces a substantive finding or a remediation proposal about a page has
reviewed it, whether or not the page's content changes. Two consequences are now explicit
requirements rather than implications — FR-002d excludes the review-candidate listing itself, which
would otherwise make the check self-clearing, and FR-002e states that a page producing no finding is
intentionally never stamped, so the candidate list reads "low-confidence pages nothing has been said
about". The `## Deferred Decisions` section is retired.

**One assumption was corrected rather than confirmed.** The spec originally assumed the lint role
document needs no textual change. D1a's resolution makes lint the sole writer of the review date, and
lint's current wording is permissive (`LintAgent/system-prompt.md:328`), so that wording must become
definite or the field never materialises.

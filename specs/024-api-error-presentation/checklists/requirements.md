# Specification Quality Checklist: Readable API Error Presentation

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-16
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

## Constitution-Specific Checks

- [x] **Success-criteria split (Principle II)**: the spec states explicitly that this is a
      harness-only feature and therefore carries deterministic guarantees only, with **no**
      agent-judgment thresholds. This is the correct handling of the split for a feature with no
      agentic surface — not an omission. Attaching a "≥ 90% of sampled runs" threshold to a
      deterministic presentation contract would itself be a spec defect.
- [x] **Agentic boundary (Principle V)**: no requirement asks backend code to make a judgment about
      wiki content. Error copy is operator-facing harness text, not wiki content.
- [x] **Instruction-file content (Principle V)**: no requirement asserts the wording or substance of
      an agent instruction file.

## Validation Notes

Three items were re-checked after the first pass and required spec edits:

1. **"Prominent" was initially unmeasurable.** The original phrasing of User Story 3 asked for a
   "prominent" error. Prominence is a design opinion, not a testable outcome. Rewritten as three
   verifiable properties: a consistently styled distinct region, announcement to assistive
   technology, and no focus theft (FR-009, SC-004's sibling criteria).
2. **FR-001 initially named a wire format.** An early draft named a specific standard error
   envelope. That is a `/speckit-plan` decision, not a spec one — the spec now states only the four
   facts the structure must carry, leaving the transport shape to the plan and its ADR.
3. **Scope boundary against #88 was implicit.** Prompt-length handling is an obvious neighbour of
   "surface API errors clearly" and would have crept in. Now stated twice — in the Clarifications
   decision record and in Out of Scope — so the plan cannot silently absorb it.

A fourth issue surfaced during `/speckit-plan` and was corrected in the spec rather than worked
around in the plan:

4. **Category and retryability contradicted each other.** As first written, FR-007 classified a
   failure by whether its body parsed, while FR-008/SC-004 made exactly two categories retryable.
   Those rules disagree on the common case of a gateway answering a 502 with an HTML page: body-
   driven classification calls it "unexpected" and therefore non-retryable, which is wrong — a
   faulting gateway is precisely the case worth retrying. FR-007 now classifies by *what happened to
   the request*, and body recognizability determines only how much can be said about it. User Story
   4 gained a fifth scenario pinning the corrected behaviour, and the edge case was rewritten to
   match. Caught before any task was written; no implementation consequence beyond the corrected
   rule.

Two deliberate departures from the template worth flagging for review:

- **The Clarifications section records decisions, not answered questions.** The operator asked for
  the workflow to run without questions, so `/speckit-clarify` was not run interactively. The three
  decisions it would have surfaced are recorded with their rationale so the reasoning is auditable
  rather than invisible.
- **An `## Out of Scope` section was added** (not in the template). Four adjacent issues (#88, #39,
  and the recorded-failure-data and CLI surfaces) sit close enough to this feature that leaving the
  boundary to inference would be a scope risk.

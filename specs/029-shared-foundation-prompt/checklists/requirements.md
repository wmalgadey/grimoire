# Specification Quality Checklist: Shared Foundation Prompt and Deployment Identity Wizard

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-05
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

- [x] Success criteria are split per Principle II: SC-001..SC-007 are deterministic harness
      guarantees (100%); SC-008 and SC-009 are explicitly classified as **lower-stakes**
      agent judgment and stated narratively, satisfied by the user-reported correction loop
- [x] No 100% deterministic guarantee is attached to an agent-judgment outcome
- [x] The agentic boundary (Principle V) is preserved: the foundation document is instruction
      content; FR-004 forbids the harness transforming it and FR-010 forbids it widening what
      an agent may do
- [x] Verbatim user input is preserved unedited in the `**Input**` field (including its
      original wording and typos); all derived requirement text is English

## Notes

- Re-validated 2026-09-05 after the clarification session (two questions answered): FR-009 now names
  the extraction boundary concretely, FR-013a fixes who authors specialised content, and Assumptions
  records the unavoidable eval-recording staleness. All items above still pass; none regressed.
- Re-validated again 2026-09-05 after the third clarification: US1-AS1, FR-006, SC-001 and SC-003 were
  restated as outcomes, so "No implementation details" and "Success criteria are technology-agnostic"
  hold more strictly than before — the per-run record's shape (two task-artifact entries, each with a
  SHA-256) now lives only in `plan.md`/ADR-053. All items still pass; none regressed.
- Re-validated 2026-09-05 after the fourth clarification session (four questions): US1-AS4 and FR-007
  removed (evaluation is internal machinery), Part 2 moved from the deployment script into the system
  itself, the drafting agent placed on the deploy host rather than inside the system, and identity
  reporting split into FR-018/FR-018a. "No implementation details" holds better than before — the spec
  no longer names `grimoire-server` commands or shell flags outside the verbatim Input block. Two items
  need watching at plan time rather than here: the wizard is now system code, so its ADR and
  observability obligations are real (Principle III/IV), and the spec's own carve-out for relaxed
  conventions now covers only the deployment script's glue.

- The one deliberate open decision — where the shared document physically lives and how an
  instance-specific one reaches a containerized deployment — is *not* marked
  [NEEDS CLARIFICATION]: the user explicitly assigned it to `/speckit-plan` and required both
  candidate shapes to be weighed there. The spec constrains the outcome only through FR-008 (a shipped
  default applies when the instance sets nothing) and FR-017 (an instance-specific document survives
  redeployment).
  It is recorded in Assumptions rather than as a spec gap.
- Terms like "task record", "content hash" and "guarded tool boundary" are existing
  ubiquitous-language terms of this project (task artifact, SHA-256 recording, ADR-006/030),
  not implementation detail introduced by this spec.
- `deploy/server/grimoire-server` is no longer named in the spec's own requirements (only in the
  verbatim Input block and in one Assumption describing what stays there): after the 2026-09-05
  clarification the wizard belongs to the system, and the deployment script's role is stated
  behaviourally (FR-018a: it starts the system's wizard and surfaces what the system reports).

# Specification Quality Checklist: The Guarded Tool and Policy Surface Lint Needs

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-22
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

- [x] Success criteria split per Principle II: deterministic harness guarantees at 100%
      (SC-001..SC-010), agent-judgment outcomes as evaluation thresholds (SC-011..SC-014)
- [x] No wiki-content judgment specified as deterministic backend behavior (FR-023 states
      the boundary explicitly)
- [x] Every added capability is specified as passing through the guarded tool boundary
      (FR-022) — no shell, no second boundary
- [x] ADRs expected to be touched are named in Dependencies (ADR-006, ADR-011, ADR-015,
      ADR-016 — superseded, not amended — ADR-018) for the `/speckit-plan` ADR pass

## Notes

- All items pass as of the `/speckit-clarify` session on 2026-08-22. The three original
  [NEEDS CLARIFICATION] markers are resolved, plus two more the session surfaced:

  | # | Question | Answer |
  |---|----------|--------|
  | 1 | Width of the remediation write grant | Wiki-wide for the run, not scoped to the authorized page |
  | 2 | Survey vs. execution policy split | No split — one scope, one policy, both modes |
  | 3 | Page creation and deletion | Permitted; git history is the safety net |
  | 4 | Access to the index and activity log | Included; only the content root bounds the scope |
  | 5 | Search pattern language | Regex with grep semantics, from the "mimic the shell tools" rule |

- **Scope grew materially during clarification.** The spec now supersedes ADR-016 rather than
  amending it: `frontmatter-only` is removed in both modes, so the Lint agent holds full
  authority over wiki content in an unattended survey run. `/speckit-plan` inherits a
  superseding-ADR obligation with bidirectional status links and a `docs/adr/index.md` update
  (Principle III "ADR Status Maintenance").
- **Deferred to `/speckit-plan`, deliberately, as documented defaults rather than open
  questions**: the search result cap, the search timeout value, the maximum batch size, and the
  regex pattern-size bound. FR-005/FR-006/FR-007a require each to exist and be observable; none
  of them names a number, and the plan must.

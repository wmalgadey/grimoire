# Specification Quality Checklist: Simplify Hub CLI Configuration

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-06
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

## Notes

All items pass on first validation pass. No [NEEDS CLARIFICATION] markers were needed: the
one genuinely open question (backward compatibility with the existing 16-switch surface) has
a reasonable default given the hub's internal, single-maintainer usage context — a clean
breaking change with no deprecated aliases — and is documented in the Assumptions section
rather than left as a blocking question.

**2026-08-06 clarification session (round 1)**: four points from the feature owner were
integrated — findings/remediation-tasks moved from the working/data directory to the wiki
directory (FR-003, FR-005); legacy-option rejection was dropped in favor of the CLI parser's
standard "unrecognized option" error (removed old FR-008/SC-005, reflecting the
pre-1.0/no-legacy-users context); a new configuration-file-only escape hatch for internal
sub-path layout was added (FR-010, User Story 4, SC-005); and the wiki directory's default was
initially kept nested under the working/data directory. All checklist items still passed.

**2026-08-06 clarification session (round 2)**: the feature owner reversed the last point
above — the working/data directory and the wiki directory now default to separate, sibling
locations, with only the Agent Directory nesting inside the working/data directory by default
(FR-008, FR-009, Key Entities, Assumptions, US3 AC2, the wiki/data-nesting edge case). The
round-1 "kept nested" answer was superseded and removed rather than left as contradictory
text. All checklist items still pass after this change.

**2026-08-06 clarification session (round 3)**: four answers triggered a substantial rewrite.
The CLI surface grew from 2 options to 3 (runtime data folder, agent folder, wiki folder) with
precedence CLI > env > config file; the configuration file became the mandatory, git-versioned
sole source of default paths, with no code-level fallback and a hard failure when it is missing
or empty. Agent instruction files are now produced and refreshed by the agent build rather than
seeded by the hub, with a required build-output redirect mechanism and a hard failure on an
empty agent directory. Defaults were fixed at `.grimoire/` and `llm-wiki/`, and no on-disk
migration of the old layout is performed. Requirements were renumbered (FR-001..FR-015) and
success criteria expanded (SC-001..SC-008); the round-1 "exactly 2 options / config file as
override-only escape hatch" answer was rewritten in place rather than left contradictory.

**Deliberate exception to "no implementation details"**: the specification names
`appsettings.json` and refers to build scripts / build properties. These are user-facing
operator surface — the file the operator edits and the mechanism they invoke — not internal
implementation, and the feature owner specified them directly. All functional requirements are
otherwise phrased against "the configuration file" rather than a concrete format.

**Success-criteria split (Constitution Principle II)**: every criterion here is a deterministic
harness guarantee stated at 100%. This is correct rather than a defect — the feature changes
configuration resolution and directory layout only and introduces no agent-judgment behavior,
so no evaluation-threshold criteria apply. A note to that effect is embedded in the spec's
Success Criteria section.

All 16 checklist items still pass after round 3.

**2026-08-06 clarification session (round 4)**: closed the eval-data gap flagged at the end of
round 3. Eval recordings move out of the runtime data folder into a fixture folder inside the
test project, resolved from a hardcoded location with the recordings-root switch removed
(FR-016). The eval runner's agent-instruction resolution becomes repo-anchored against the
agent project sources rather than the runtime agent directory or build output (FR-017), and
eval resolution is required to be independent of all three directory options so eval results
never vary with operator configuration (FR-018, SC-009, SC-010). With recordings relocated,
the runtime data folder now holds only genuine runtime state.

One eval-runner path was settled by inference rather than explicit direction — the runner's
local secrets/env file, which is assumed to follow the runtime data folder's new default. It
is called out as such in the Assumptions section and is worth confirming during planning.

All 16 checklist items still pass after round 4.

**2026-08-06 clarification session (round 5)**: the secrets-file inference left open in round 4
was corrected by the feature owner. The `.env` file lives at the project root next to the
example file already there — outside all three configurable folders, read from that one place
by both the hub and the eval runner (FR-019, SC-011, new Secrets File entity). Secrets were
removed from the runtime data folder throughout (FR-006, SC-003, US3 test and acceptance
scenario, Runtime Data Folder entity), which resolves the existing papercut where
`.env-example` sits at the repo root while the live `.env` sits in `data/`. No inferred paths
remain in the spec.

All 16 checklist items still pass after round 5.

---
status: accepted
---

# ADR-033: SlowEval Replay Class Set Reduced by the Lower-Stakes Eval Removal

> **Amends [ADR-021](ADR-021-test-tier-taxonomy-and-deterministic-wait-enforcement.md)**:
> the SlowEval tier's enumerated replay-eval class set shrinks from five to four —
> `LintRemediationProposalRelevanceEvalTests` is deleted. The tier taxonomy itself
> (three trait-declared tiers), the fixed-wait convention, the concurrency levers, and
> every other part of ADR-021 are unchanged.

## Context and Problem Statement

Constitution v1.12.0 (Principle II) reclassified agent-judgment success criteria into
high-stakes and lower-stakes tiers; formal eval suites remain mandatory only for
high-stakes criteria, and the project removed its lower-stakes eval scenarios to cut
recapture and maintenance cost. One removed scenario — `lint-remediation-proposals` —
was the sole scenario of `LintRemediationProposalRelevanceEvalTests`, so the class was
deleted with it.

ADR-021's accepted Decision Outcome enumerates the SlowEval tier's membership as exactly
five named replay-eval classes, including the deleted one, and
`AgentEvalsTierMembershipRuleTests` enforces that enumeration — a Boundary Rule's
permanent, Red/Green-probed structural test asserting an exact class set. Per Principle
III's ADR immutability, a changed decision is recorded by a new ADR — not by silently
diverging from the accepted enumeration, and not by hand-editing ADR-021's Decision
Outcome to match the new reality after the fact.

## Decision Drivers

- Principle III Immutability: ADR-021's enumerated five-class SlowEval membership is
  Accepted decision content; it cannot be edited in place to remove a class, however
  small the change, without a new ADR recording it.
- `AgentEvalsTierMembershipRuleTests` asserts the enumeration exactly — leaving the
  deleted class listed (with zero backing scenarios) would either fail that test
  outright or require the test itself to special-case an empty class, either way
  papering over a decision that has genuinely changed.
- Constitution v1.12.0's eval-scope reduction (Principle II) is a real, external cause,
  not a judgment call this ADR is re-litigating — the reduction only needs to be
  recorded here, not re-argued.

## Considered Options

1. **Leave the empty `LintRemediationProposalRelevanceEvalTests` class declared with
   `[Trait("Tier","SlowEval")]` and ADR-021's enumeration unchanged.** Rejected: a
   zero-scenario class listed as a genuine SlowEval replay-eval class misrepresents
   the tier's actual membership and leaves dead code in the test project.
2. **Delete the empty class and quietly update `AgentEvalsTierMembershipRuleTests`'
   enumeration without a new ADR — treat it as a trivial follow-on to the eval
   pruning, not a decision of its own.** Rejected: this is exactly what Principle
   III's Immutability rule forbids — ADR-021's Accepted Decision Outcome would be
   edited in place to match code that has since diverged from it, with no record of
   why or when the enumeration changed.
3. **Delete the empty class, record the reduced enumeration via a new ADR that amends
   ADR-021 with a bidirectional status-header link.** Chosen — satisfies Immutability
   without re-deciding anything ADR-021 already settled (the tier taxonomy, the
   fixed-wait convention, the concurrency levers all stand unchanged).

## Decision

The SlowEval tier's genuine replay-eval scenario classes are exactly:

- `IngestReplayEvalTests`
- `LintReplayEvalTests`
- `QueryReplayEvalTests`
- `RemediationReVerificationEvalTests`

`LintRemediationProposalRelevanceEvalTests` is removed from the set (the class no longer
exists). ADR-021's membership rule — SlowEval contains only genuine replay-eval scenario
classes, declared by `[Trait("Tier","SlowEval")]`, enforced by
`AgentEvalsTierMembershipRuleTests` — continues to apply verbatim to the reduced set.

Future changes to the set (a new replay-eval class for a new high-stakes criterion, or a
further removal) are recognized, single-file amendments to this ADR's enumeration plus
the tier trait on the class itself — the tier-membership rule test then follows the
amended enumeration.

## Consequences

- `AgentEvalsTierMembershipRuleTests`' exact-set assertions reflect the four-class set.
- ADR-021 carries `Amended by ADR-033` in its status header; `docs/adr/index.md`
  reflects the chain (Principle III, ADR Status Maintenance).
- The scenario *contents* of the remaining classes (which eval scenarios each carries)
  were never part of ADR-021's decision and are governed by the constitution's
  success-criteria tiering, not by this ADR.

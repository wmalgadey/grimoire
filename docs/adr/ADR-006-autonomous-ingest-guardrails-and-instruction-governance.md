---
status: accepted
---

# ADR-006: Autonomous Ingest Guardrails and Instruction Governance

## Context and Problem Statement

Feature 002 requires autonomous ingest behavior that can perform direct tool actions while
remaining constrained by repository-defined safety boundaries. The specification requires
that every ingest run apply the active agent instructions from CLAUDE.md and SKILL.md
before wiki writes, and that tool actions be guarded by a versioned policy controlling
write and read scopes. Existing ADRs define stack, execution model, persistence,
credential scoping, and observability, but do not define governance for autonomous action
authorization or instruction-context loading semantics.

Without a dedicated decision, these constraints risk being implemented as ad-hoc runtime
checks with no architectural contract for behavior such as partial-denial continuation,
auditability of denied actions, and policy traceability.

## Decision Drivers

- Autonomous ingest must remain safe by default while still producing useful output when a
  subset of actions is denied.
- Instruction context (CLAUDE.md + SKILL.md) must be mandatory input to write planning,
  not optional metadata.
- Guardrail policy must be versioned in repository history to make behavior changes
  reviewable and auditable.
- Test strategy must verify repository-owned wrappers and policy enforcement deterministically,
  without relying on live external LLM provider behavior.

## Considered Options

1. Versioned policy + mandatory instruction loading + per-action allow/deny with
   continue-on-deny semantics (selected)
2. Hard-coded path checks and optional instruction loading inside ingest runtime
3. External policy daemon/service for authorization decisions

## Decision Outcome

Chosen option: **Option 1 — policy-driven autonomous authorization with mandatory
instruction governance.**

- Ingest runtime MUST load active instruction context from CLAUDE.md and declared SKILL.md
  files before any wiki write planning begins.
- Every autonomous read/write tool action MUST be evaluated against a versioned repository
  policy file.
- Guardrails are deny-by-default.
- A denied action blocks only that action; ingest continues with remaining allowed actions.
- Denied actions MUST be recorded in task artifact output with action, target path, and
  denial reason.
- Testing MUST validate policy evaluation, tool-wrapper behavior, and artifact/log
  outputs without direct live-provider assertions against Anthropic/Claude APIs.

### Consequences

- Good, because autonomous operation gets explicit architectural safety boundaries and
  deterministic audit semantics.
- Good, because policy behavior becomes change-controlled through repository history.
- Good, because deterministic tests stay focused on project code instead of external SDK
  and network behavior.
- Bad, because ingest now has additional runtime requirements (instruction loading +
  policy parse/evaluation) that can fail if configuration is invalid.
- Neutral, because this decision does not replace ADR-002 child-process execution; it
  refines execution constraints within that model.

## Relationship to Existing ADRs

- ADR-002 remains the process/execution boundary.
- ADR-003 remains persistence split for domain vs operational state.
- ADR-004 remains credential scoping for external API keys.
- ADR-005 remains observability backend requirements.
- ADR-006 adds autonomous authorization and instruction-governance constraints that those
  ADRs do not define.

## More Information

This ADR applies to ingest and any future autonomous agent mode that performs repository
writes via tool actions. If future agents adopt the same autonomy model, they must either
conform to this ADR or supersede it with a new accepted ADR that defines stricter
boundaries.

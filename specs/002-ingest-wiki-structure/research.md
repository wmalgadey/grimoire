# Research: Ingest Wiki Structure

## Decision 1: Keep hybrid execution (autonomous tools + deterministic task artifact)

- Decision: Retain ADR-002 child-process ingest model and require every run to both execute wiki updates and emit a structured task artifact in the same run.
- Rationale: This preserves autonomous behavior while keeping deterministic validation and auditability for FR-006 and FR-016.
- Alternatives considered:
  - Artifact-only planning mode (rejected: does not satisfy direct wiki update behavior).
  - Tool-only mode without artifact (rejected: weak auditability and reconciliation).

## Decision 2: Enforce repository-driven guardrails with allow/deny semantics per action

- Decision: Introduce a versioned guardrail policy file in repository source and evaluate every autonomous read/write tool action against it.
- Rationale: FR-014 and FR-017 require tool-level enforcement that is traceable in git history.
- Alternatives considered:
  - Hard-coded path checks in code (rejected: non-versioned behavior, harder policy review).
  - Process-wide sandbox only (rejected: lacks explicit policy intent and denial reason traceability).

## Decision 3: Load natural language guardrails before write planning

- Decision: The ingest runtime must load active CLAUDE.md and selected SKILL.md context before planning wiki writes, then include instruction-load evidence in logs/artifact metadata.
- Rationale: FR-013 requires instruction-governed operation every run, not best effort.
- Alternatives considered:
  - Optional instruction loading (rejected: violates FR-013).
  - One-time boot cache with no per-run verification (rejected: stale instruction risk).

## Decision 4: Deny violating actions but continue run

- Decision: Guardrail violations block only the specific action; remaining actions continue, and every denial is recorded with target path and reason in task artifact output.
- Rationale: Required by FR-015 and SC-009, while reducing total run failure rate from isolated violations.
- Alternatives considered:
  - Fail-fast on first denial (rejected: violates continue-processing requirement).
  - Silent skip (rejected: fails auditability requirements).

## Decision 5: Represent wiki updates as explicit typed actions

- Decision: Model ingest output as typed actions: create, update, supersede, denied. Keep source pages immutable and derive index updates from accepted write actions.
- Rationale: Supports FR-001..FR-005, FR-008, and deterministic artifact checks.
- Alternatives considered:
  - Implicit file diff-only detection (rejected: difficult to assert intent and supersession semantics).
  - Manual index curation (rejected: violates FR-003).

## Decision 6: Deterministic test strategy focused on project code, not external SDK behavior

- Decision: Test tool wrappers, policy evaluation, artifact encoding, and orchestration using hermetic fixtures/fakes; do not execute live Claude/Anthropic SDK calls in tests.
- Rationale: User constraint and Principle II. External SDK behavior belongs to vendor tests; project tests must verify repository-owned logic only.
- Alternatives considered:
  - Live Anthropic API integration tests (rejected: nondeterministic, secret-dependent, tests library/network behavior).
  - Mock-everything unit-only approach (rejected: insufficient boundary confidence for this architecture).

## Decision 7: Add a dedicated ADR for autonomous guardrails and instruction governance

- Decision: Draft ADR-006 to formalize mandatory CLAUDE.md/SKILL.md instruction loading, deny-by-default policy enforcement, continue-on-denied-action behavior, and deterministic project-owned testing boundaries.
- Rationale: Existing ADR-001 through ADR-005 do not explicitly govern autonomous authorization and instruction-context semantics required by FR-013 through FR-017.
- Alternatives considered:
  - Keep requirements only in feature plan/contracts (rejected: insufficient architectural governance and poor long-term discoverability).
  - Amend ADR-002 alone (rejected: execution model ADR would become overloaded and less clear).

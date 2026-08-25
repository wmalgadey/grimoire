---
status: accepted
---

# ADR-034: Path and Subprocess Containment Hardening

## Context and Problem Statement

Constitution v1.12.0 (Principle V) requires a **Host stability guarantee**: regardless
of what a task or an instruction file says, the harness must ensure the agent process
cannot destabilize the host by writing outside its guarded roots or by causing an
unsanctioned process to run. No existing ADR names path-traversal safety or
subprocess-spawn safety as a dedicated topic — ADR-006 documents the guarded tool
boundary's canonicalize-then-match design in passing, and ADR-002 documents the agent
spawn model without naming injection safety as a concern. `specs/027-host-stability/`
found both mechanisms already correct for the plain cases (`GuardedToolExecutor`,
`PathTraversalTests`; `AgentProcessHost`, `MarkItDownConverter`), but with residual
adversarial-input gaps in path resolution and no structural guarantee that the
spawn-site set stays closed as the codebase grows. This is a cross-cutting concern
(Principle III) requiring a dedicated ADR before implementation.

## Decision Drivers

- The guarantee must hold "regardless of task or instruction-file content," including
  content specifically constructed to test the boundary (spec.md FR-006) — it cannot
  depend on agent good behavior.
- Verification must be hermetic, against the real containment mechanism (a real
  filesystem, real symlinks, the real compiled spawn sites) — never an agent-behavior
  evaluation (spec.md FR-007, Constitution Principle II).
- Resource governance (CPU/memory/disk/wall-clock) is explicitly out of scope — a
  deployment-level concern (container/sandbox isolation, ADR-002's deferred direction),
  not a harness responsibility (spec.md Out of Scope, corrected constitution scope).
- No new infrastructure, port, or external system is introduced — this hardens two
  existing internal mechanisms (Principle IV, ADR-001).

## Considered Options

1. Harden the existing `GuardedToolExecutor` path-resolution walk in place, and add a
   Mono.Cecil IL-scan structural test enumerating process-spawn call sites (extending
   the established idiom already used for `NonBlockingDispatchRuleTests`)
2. Introduce a dedicated path-safety library/port with its own adapter boundary
3. Contain agent processes in per-run OS-level sandboxes (containers, chroot, seccomp)
   as the primary containment mechanism, superseding in-process checks

## Decision Outcome

Chosen option: **Option 1 — harden the existing mechanisms in place, structurally pin
the spawn-site set.**

- **R1 (Boundary Rule)**: Only `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess.
  AgentProcessHost` and `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown.
  MarkItDownConverter` may construct a `System.Diagnostics.Process`/
  `ProcessStartInfo` anywhere in `Grimoire.Hub`. Enforced by a permanent, Red/Green-
  probed Mono.Cecil IL-scan test (Phase 0), modeled on `NonBlockingDispatchRuleTests`.
  Adding a new spawn site (a future agent type, a future converter) is a normal,
  single-file amendment to the allowlist alongside the new call site — not a violation
  of the rule itself, which stays "no site outside the enumerated, reviewed set."
- **R2 (Boundary Rule)**: Both allowlisted sites use `ProcessStartInfo.ArgumentList`
  exclusively — the shell-parsed `Arguments` string property is never set. Enforced by
  the same structural test (same IL scan, sibling assertion).
- **R3 (Feature-Scoped Invariant)**: `GuardedToolExecutor`'s canonicalize-then-match
  path resolution denies every adversarial path variant in scope (plain traversal,
  absolute override, symlink escape, chained/nested symlink, percent-encoding and
  Unicode-normalization variants, null-byte truncation, and a symlink swapped in
  between validation and the mutating write/delete). Enforced by a classicist,
  state-based integration test against a real filesystem and real symlinks, extending
  `PathTraversalTests`. The resolution walk gains two behavioral changes to make this
  true: (a) chained-symlink resolution recurses through the full remaining path instead
  of stopping after the first hop, capped at 40 hops (`symlink_loop` denial beyond
  that); (b) an immediate revalidation recomputes the canonical path right before the
  mutating filesystem call and denies (`revalidation_failed`) on any mismatch against
  the originally validated target. An embedded NUL byte is caught as a controlled
  `malformed_path` denial rather than an unhandled `ArgumentException`.
- **R4 (Feature-Scoped Invariant)**: A filename- or content-derived value that selects
  a converter or code path (a submission's file extension) is validated against a
  fixed allowlist before use. Already true today (`IngestSubmissionValidator`);
  pinned by one dedicated adversarial-input test (an unlisted extension is rejected
  before any conversion path runs).

Resource governance is explicitly not addressed by this ADR: per the corrected
constitution scope, that is a deployment concern (container/sandbox isolation around
the agent process), not a harness containment mechanism — Option 3 above is rejected
as the *primary* mechanism for that reason, though nothing here precludes adding
container isolation later as a complementary, deployment-level hardening.

### Consequences

- Good, because both rules attach to mechanisms that already exist — no new port,
  adapter, or infrastructure is introduced, and the two Boundary Rules reuse an
  already-proven IL-scan idiom (`ArchScan.cs`) rather than inventing a new enforcement
  style.
- Good, because the Feature-Scoped Invariants are covered by tests that exercise real
  observable behavior (a real symlink swap, a real null byte) rather than reflecting
  over the resolution code's shape — consistent with ADR-032's correction of the
  reflection-vs-behavioral test boundary.
- Bad, because the post-validation revalidation (R3b) narrows but does not
  mathematically eliminate the time-of-check-to-time-of-use race window; true
  elimination would require native `openat`/`O_NOFOLLOW` interop, rejected here as
  disproportionate (see `research.md` D3). Accepted because spec.md's acceptance
  criterion is "denied or provably confined to the originally validated target," which
  an immediate re-check before the mutating call satisfies.
- Neutral, because R1/R2's enumerated allowlist is expected to grow (a future agent
  type, a future converter) — that growth is a deliberate, reviewed amendment to this
  ADR and the structural test's allowlist, not friction the rule is meant to prevent.

## More Information

Detailed rationale and rejected alternatives: `specs/027-host-stability/research.md`
(D1–D6). Entity shapes: `specs/027-host-stability/data-model.md`. This ADR must be
**Accepted** before `/speckit-tasks` runs for feature 027 (Constitution, Spec-Kit
Workflow step 4) — accepted directly on drafting, consistent with this project's
solo-operator sign-off convention already used for ADR-032/ADR-033.

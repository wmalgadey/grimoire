# Contract: Deterministic-Tier Fixed-Wait Structural Rule

**Feature**: `019-fast-test-tier` | **Enforces**: FR-003, FR-004, FR-005, FR-010, SC-004, SC-007

`Grimoire.ArchTests.DeterministicTierNoFixedWaitRuleTests` (Phase 0 structural boundary
test per Constitution Principle III — the first task of `tasks.md`).

## Scanned assemblies

- `Grimoire.Domain.UnitTests`
- `Grimoire.ArchTests`
- `Grimoire.IntegrationTests`
- `Grimoire.AgentEvals`

## Denied calls

Any IL `call`/`callvirt` instruction targeting:
- `System.Threading.Tasks.Task::Delay`
- `System.Threading.Thread::Sleep`

## Allow-list (exemptions)

A denied call is **not** a violation when either is true:

1. The call site is inside `Grimoire.IntegrationTests.TestSupport.PollAsync` itself (the
   one sanctioned poll-tick implementation).
2. The containing method, or its declaring type, carries
   `[Trait("TimingDependent", "true")]` (detected via Mono.Cecil custom-attribute
   inspection — same detection style already used for other trait-driven test metadata
   in this codebase).

Every other call site is a reported violation, named by assembly, type, and method
(same violation-reporting shape as `RuntimePathsBoundaryRuleTests`).

## Red/Green probe (required before this rule is cited as an active constraint)

1. **Red**: add a scratch test method containing an un-exempted `Task.Delay(1000)` call
   to one of the four scanned assemblies. Run the rule; confirm it fails and names the
   exact call site.
2. **Green**: remove the scratch method. Run the rule; confirm it passes.
3. The probe's execution is recorded in the implementing task's commit history (no probe
   code is merged) — same discipline as every other Red/Green-probed rule in this
   codebase (ADR-009/010/011/013/015/016/017).

## CI placement

No new pipeline step: this rule lives in `Grimoire.ArchTests`, which `ci.yml`'s existing
`Run architecture tests` step already executes on every PR.

## Non-goals

- This rule does not evaluate whether a `[Trait("TimingDependent", "true")]` exemption is
  *justified* — that judgment (is this test's subject genuinely time-based?) is a review-
  time, human judgment call the rule cannot and does not make; the rule only enforces that
  every fixed wait is either routed through the shared poll helper or explicitly marked.
- This rule does not scan production assemblies — fixed waits in application code are out
  of this feature's scope entirely (the spec is about the test suite).

---
status: accepted
---

# ADR-032: Behavioral Enforcement for Feature-Scoped Path-Surface Invariants

> **Supersedes [ADR-024](ADR-024-memory-directory-root.md) — Structural Enforcement
> section only (M1, M2, M4 enforcement mechanism)**: ADR-024's own decision for M1, M2,
> and M4 is unchanged and remains governing — this ADR replaces only the section
> describing *how* those three rules are proven. That section is not narrowed or
> extended, it is reversed (see Context: why supersession, not amendment): the
> reflection/IL Phase 0 tests it named, and the "Principle III escape valve"
> justification for keeping them, are retracted outright and replaced by this ADR's
> Decision.
>
> **Supersedes [ADR-022](ADR-022-minimal-directory-configuration-surface.md) —
> Structural Enforcement section only (R2 enforcement mechanism)**: same relationship,
> for rule R2's IL-scan enforcement.

## Context and Problem Statement

Constitution v1.11.0 split Principle III's test mandate into **Dependency & Layering
Boundary Rules** (permanent reflection/IL structural tests, Red/Green-probed) and
**Feature-Scoped Invariants** (FSIs), and ruled that an FSI "MUST NOT be given, a
reflection/IL-based structural test to count" — it is covered by a classicist,
state-based integration test of the real observable behavior instead. ADR-024's own
Decision Outcome classifies M1, M2, and M4 as Feature-Scoped Invariants.

Their reflection/IL tests nevertheless survived, resting on this claim (ADR-024,
"Structural Enforcement" section):

> M1, M2, and M4 keep their reflection/IL Phase 0 tests under Principle III's escape
> valve (a Feature-Scoped Invariant may stay reflection-enforced where this plan
> explicitly justifies why no runtime-observable behavior can catch the violation
> before merge)

That clause does not exist. No version of the constitution — v1.11.0 included — has
ever contained an escape valve permitting reflection enforcement of an FSI; the wording
was introduced by spec 022's convergence task T067 and cited as if it were
constitutional text. The v1.11.0 Sync Impact Report, conversely, states explicitly that
these tests were *not* grandfathered (PR #70 was unmerged at amendment time) and should
be replaced with classicist behavioral tests before merge.

The per-rule justifications T067 recorded ("no runtime-observable behavior can catch
the violation") also turn out to be overstated: the behavioral test suite that exists
today *does* observably catch each violation class (see Decision).

**Why this is supersession, not amendment.** The constitution defines *amends* as
"adds or narrows detail while the original decision stands" and *supersedes* as
"replaces it entirely." What ADR-024's and ADR-022's Structural Enforcement sections
asserted — that reflection/IL tests prove M1/M2/M4/R2, licensed by an escape valve —
is not narrowed or extended here; it is factually wrong and retracted outright, with a
different mechanism substituted in its place. An earlier draft of this ADR recorded
that reversal as an "Amends" relationship, reasoning that M1/M2/M4/R2's *substance* was
unchanged. Review caught that this understated the change: the substance is indeed
unchanged, but the enforcement claim — the actual content of the superseded section — is
not narrowed, it is reversed, and folding a reversal into a one-line "Amended by" status
note buried the decision instead of recording it. This ADR is scoped to exactly the
Structural Enforcement section of each document — `docs/adr/index.md` already uses this
same **scoped supersession** pattern for ADR-009 (superseded in part by ADR-022) and
ADR-011 (superseded in part by ADR-014, ADR-015): the superseding ADR replaces a named
subsection while the rest of the original ADR, and its own `Accepted` status, stand
unchanged. Full retirement of an ADR's core decision (the pattern reserved for ADR-016)
does not apply here — ADR-024's fourth-root decision and ADR-022's three-tier
precedence/switch-cap/no-code-defaults decisions are untouched.

## Decision Drivers

- Principle III requires a Feature-Scoped Invariant to be proven by a classicist,
  state-based integration test; a reflection/IL-based structural test MUST NOT be used
  to enforce one, with no documented exception — the "escape valve" ADR-024 cited does
  not exist in any constitution version.
- Whatever replaces the retired reflection tests must still catch the same violation
  classes they were built to catch (a fifth path switch, a reintroduced code-level root
  default, a malformed options-graph shape) — retracting a false justification must not
  also silently drop real regression coverage.
- Principle III requires every enforcement mechanism to be *proven* live, not merely
  asserted: a Boundary Rule's structural test needs a Red/Green probe; a claimed
  behavioral FSI test needs to actually observe the violation, not just exist.
- The fix must not reintroduce, even partially, the pattern being retracted — keeping a
  reflection assertion "just in case" alongside a new behavioral test would still be
  reflection-based FSI enforcement in substance, defeating the correction.

## Considered Options

1. **Retract the false "escape valve" citation only; leave the reflection/IL tests
   running unchanged.** Fixes the misleading documentation but leaves M1/M2/M4/R2
   enforced exactly as Principle III forbids for an FSI — a real constitutional
   violation remains, simply no longer excused by a citation that never existed.
2. **Delete the reflection/IL tests outright, add no replacement.** Ends the violation
   but drops real regression coverage — a fifth `--*-dir` switch, or a code-level
   root default reintroduced by a future change, would go undetected until it reached
   production rather than failing CI.
3. **Replace the reflection/IL tests with classicist, state-based behavioral tests
   against each rule's real observable surface**, confirmed to actually catch the
   violation class each rule protects.
4. **Hybrid — keep a lightweight reflection "smoke test" alongside new behavioral
   tests**, on the reasoning that belt-and-suspenders coverage is safer. Rejected: a
   reflection smoke test asserting an FSI's current shape is still reflection-based FSI
   enforcement in substance — exactly what Principle III forbids "with no exception" —
   and would itself need retiring the next ordinary time the feature's surface grows
   (a new switch, a renamed group), reproducing the same false-alarm failure mode this
   ADR exists to close.

## Decision

Chosen option: **Option 3 — replace reflection/IL enforcement with classicist behavioral
tests, confirmed to catch each rule's real violation class.**

M1, M2, M4 (ADR-024) and R2 (ADR-022) remain binding exactly as originally decided —
this ADR changes no rule's substance, only how each is proven. The three reflection/IL
test files are deleted:

- `DirectorySwitchSurfaceRuleTests.cs` (M1)
- `PathOptionsGroupingRuleTests.cs` (M4)
- `NoCodeLevelPathDefaultsRuleTests.cs` (R2 assembly-wide fact + M2 namespace fact)

Enforcement per rule:

- **M1 — exactly four path switches, each with a description.** Enforced
  out-of-process against the real binary: `HubHelpUsageTests` spawns the built
  `Grimoire.Hub.dll`, runs `--help`, and asserts the set of `--*-dir` switches printed
  is exactly `--data-dir`, `--agent-dir`, `--wiki-dir`, `--memory-dir`, each carrying
  non-empty description text. The two in-process reflection facts previously in
  `HubHelpUsageTests` (CommandOption/Description parity via `PathSwitchCatalog`) are
  replaced by this assertion — the CLI's rendered help *is* the observable surface the
  rule protects.
- **M2 — memory root default exists only in `appsettings.json`.** Enforced by the
  existing fail-loudly facts in `StartupValidationTests`: omitting
  `Grimoire:Paths:Memory:Dir` (key or whole group) throws `ConfigurationMissing`
  naming the full key path. A code-level `"memory"` fallback anywhere in the
  resolution path would make these tests fail (resolution would silently succeed).
  `DefaultLayoutTests` and `ZeroConfigStartupTests` pin the config-file-tier default
  values themselves.
- **R2 — no code-level root defaults (`.grimoire`, `llm-wiki`).** Same mechanism:
  `StartupValidationTests.EmptyConfiguration_ThrowsConfigurationMissing_NamingAllFourRoots_BeforeTouchingAnyDirectory`
  fails if any root acquires a code-level default.
- **M4 — options graph mirrors the config tree; `SecretsFile` is the only ungrouped
  property.** Enforced by the existing behavioral suite: `PathPrecedenceTests` (each
  root binds through its nested `Grimoire__Paths__<Group>__Dir` form, independently),
  `PathGroupingInvariantTests` (relocating one group's `Dir` moves exactly that
  group's derived locations), and `StartupValidationTests` (group-shape binding for
  `Memory`). The exact-shape *enumeration* ("no fifth property") is not pinned by any
  test.

**Surface growth is an amendment, not a broken test.** A fifth path switch, a new
options group, or a new ungrouped property is a deliberate, single-file amendment to
this ADR (updating the enumerations above), plus the ordinary behavioral tests for the
new surface. Per Principle III, the concern behind M1/M4 is unbounded *silent* regrowth;
the out-of-process help assertion (M1) catches silent switch growth directly, and
options-graph growth is caught in review by this ADR's enumeration rather than by a
reflection test that turns an ordinary feature change into a false alarm.

## Consequences

- The ArchTests project retains only Dependency & Layering Boundary Rules (domain
  purity, adapter containment, guarded-write boundary, instruction authorship) — all
  legitimately reflection/IL-based per Principle III.
- `specs/022-memory-directory-root/plan.md`'s "escape valve" section is annotated with
  a dated correction pointing here; the historical text remains readable.
- ADR-024 and ADR-022 carry `Superseded in part by ADR-032` in their status headers,
  scoped to their Structural Enforcement sections only — both remain `Accepted` and
  govern everything else they decided; `docs/adr/index.md` reflects the chain
  (Principle III, ADR Status Maintenance).
- Neutral, because this scoped-supersession framing — rather than "Amends" — is a
  correction in its own right: it makes the reversal (not narrowing) of the Structural
  Enforcement claim visible in the status header itself, matching the pattern this
  project already uses for ADR-009/ADR-011/ADR-014/ADR-015, instead of relying on a
  reader opening the linked ADR to discover that the "amendment" actually replaced the
  section wholesale.

## More Information

Detailed rationale for each retired test and its replacement: `specs/022-memory-
directory-root/plan.md`'s dated correction note. This ADR must be **Accepted** before
any dependent `/speckit-tasks` run (Constitution, Spec-Kit Workflow step 4) — accepted
directly on drafting/revision, consistent with this project's solo-operator sign-off
convention.

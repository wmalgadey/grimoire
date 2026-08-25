---
status: accepted
---

# ADR-032: Behavioral Enforcement for Feature-Scoped Path-Surface Invariants

> **Amends [ADR-024](ADR-024-memory-directory-root.md)**: the substance of rules M1, M2,
> and M4 is unchanged; what changes is their enforcement mechanism. The reflection/IL
> Phase 0 tests those rules kept "under Principle III's escape valve" are replaced by
> classicist behavioral tests, and the escape-valve justification itself is withdrawn —
> no such clause has ever existed in any version of the constitution (see Context).
>
> **Amends [ADR-022](ADR-022-minimal-directory-configuration-surface.md)**: R2 ("no
> code-level literal duplicates a config default") likewise keeps its substance but loses
> its IL-scan enforcement in favor of the behavioral fail-loudly tests that already
> detect a code-level default at runtime.

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

## Decision

M1, M2, M4 (ADR-024) and R2 (ADR-022) remain binding as written. Their enforcement
moves to classicist behavioral tests; the three reflection/IL test files are deleted:

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
- ADR-024 and ADR-022 carry `Amended by ADR-032` in their status headers;
  `docs/adr/index.md` reflects the chain (Principle III, ADR Status Maintenance).

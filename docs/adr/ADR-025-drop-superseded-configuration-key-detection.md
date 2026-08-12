---
status: Accepted
---

# ADR-025: Drop Superseded-Configuration-Key Detection

> **Amends [ADR-024](ADR-024-memory-directory-root.md)**: withdraws rule M6
> (superseded-configuration-key detection at startup). Every other rule and consequence
> in ADR-024 — the fourth `MemoryDir` root, the four-group `Grimoire:Paths` regrouping,
> and rules M1–M5 — is unchanged.

## Context and Problem Statement

ADR-024 accepted rule M6: before the mandatory-root gate runs, `GrimoirePathResolver`
probes the bound configuration for eleven flat legacy keys superseded by the
`Grimoire:Paths` regrouping (e.g. `Grimoire:Paths:DataDir` → `Grimoire:Paths:Data:Dir`)
and fails startup naming each one found, together with its replacement — on the
reasoning that an unrecognized configuration key silently resolving to a default is a
worse failure mode than the breaking rename itself, since an unrecognized CLI switch is
at least a parser error.

On author review before this feature merged, that reasoning was judged to over-engineer
a case that cannot occur: Grimoire is pre-1.0 (alpha), and there is no external
installation carrying a pre-regrouping key name for the guard to protect. The
eleven-entry legacy-key table would be permanent compatibility ballast — production code
enumerating dead key names — maintained against a scenario the project's own maturity
stage rules out. M6 was a considered design choice, not a bug; per Constitution
Principle III that reversal is recorded as an amending ADR rather than an in-place edit
of ADR-024's already-Accepted text.

## Decision Drivers

- Pre-1.0 (alpha): no external installation carries a pre-regrouping key name.
- A fixed eleven-entry legacy-key table is permanent code for a case that cannot occur,
  not a bounded migration aid.
- ADR-022 already established the precedent that this project accepts silent/breaking
  pre-1.0 configuration changes without a compatibility shim — its own switch-cap change
  was itself unguarded.
- Dropping M6 does not weaken the regrouping itself: `appsettings.json` remains the sole
  source of root defaults (FR-006), and every root still fails loudly if genuinely absent
  from every configuration tier.

## Considered Options

- **Keep M6 as accepted.** Rejected: builds and maintains detection code and a table of
  eleven dead key names for zero real installations to protect against.
- **Narrow M6 to a smaller subset of "likely to still be set" keys.** Rejected: any
  subset is still a guess about which key names an operator might have, and the project
  has none — the premise for detection is absent, not just its scope.
- **Drop M6, accept the silent-fallback failure mode.** Chosen: an operator still
  exporting an old key name gets the same ordinary silent-ignore treatment configuration
  systems give any unrecognized key. They discover the mismatch by observing the resolved
  location does not match their expectation, not through a named startup failure — the
  same treatment ADR-022 already gave its own layout change.

## Decision

1. Rule M6 ("Every superseded flat configuration key is detected and reported at startup
   with its replacement") is withdrawn from ADR-024's Structural Enforcement table.
2. `GrimoirePathResolver` performs no probe over legacy flat configuration keys. An
   unrecognized key — file-based or environment-variable-based — is silently ignored by
   the configuration binder, exactly as any other unrecognized key would be.
3. No `paths_configuration_superseded` log/span event or `configuration_superseded`
   metric label exists. `grimoire.hub.path_resolution_failures_total{reason}`'s label set
   stays `configuration_missing`, `agent_directory_empty`, `location_invalid`.
4. Every other ADR-024 rule (M1–M5), the fourth `MemoryDir` root, and the four-group
   `Grimoire:Paths` regrouping are unaffected.

## Consequences

- **Good**: no compatibility-ballast code — no fixed eleven-entry legacy-key table, no
  dedicated exception type, no dedicated log event — for a case the project's own pre-1.0
  status rules out.
- **Bad / accepted**: the key rename's failure mode for an operator still exporting an
  old key name is now purely silent — they get a default they did not choose, with no
  named startup error, discoverable only by comparing the resolved location against their
  expectation. Accepted as the same bounded, pre-1.0 breaking-change treatment ADR-022
  already gave its own layout change.
- Withdraws FR-014/SC-010 from `specs/022-memory-directory-root/spec.md` (recorded there,
  in the Assumptions section, as a withdrawn companion requirement).

## Structural Enforcement

No new structural rule is introduced — this ADR removes a rule and the code path it
governed, and there is no invariant to enforce over that path's *absence*. The removal is
verified behaviorally: an integration test exercising a legacy flat key (file-based and
environment-variable-based) asserts the location silently resolves to its default rather
than throwing, replacing the deleted `SupersededConfigurationKeyTests` table-driven
assertion of the opposite behavior.

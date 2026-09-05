---
status: accepted
supersedes: ADR-022
---

# ADR-041: Independent Cwd-Anchored Directory Roots

> **Extends [ADR-004](ADR-004-credential-scoping.md), [ADR-007](ADR-007-agent-instruction-surface.md),
> [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md)**: this ADR is the current
> owner of the `SecretsFile` path detail those three ADRs' credential/instruction/path handling
> depends on (working-directory-anchored, default `.env`, belonging to no root — see Consequences
> below), and of the agent/wiki root switches ADR-007's agent CLI paths resolve under. Nothing
> those three ADRs themselves decided is reversed or narrowed.

## Context and Problem Statement

Every durable thing the Hub touches on disk — runtime data, the wiki, the agent runtime,
harness bookkeeping — needs an operator-controllable location. Under ADR-009 those
locations were derived from one consolidated `BaseDir` and each got its own "friendly
switch", which grew the CLI to sixteen path switches, eleven of them internal layout
details an operator never needed. The consolidated base also coupled locations that must
be independently placeable: relocating runtime data dragged the wiki along, and no single
switch answered "where does my knowledge base live?". This ADR decides the shape of the
Hub's operator-facing on-disk surface: how many independent locations exist at the top
level, how each is anchored, and what earns a command-line switch.

## Decision Drivers

- An operator relocates locations for different reasons (version-control the wiki, put
  runtime data on different storage, point at a deployed agent runtime, place bookkeeping
  under a retention policy) — no relocation may drag an unrelated location along.
- The switch surface must stay small and operator-meaningful, and must not regrow the way
  it did under ADR-009, where "each new location gets its own switch" produced sixteen
  switches by accretion.
- Internal layout details are not operator decisions and must not surface as switches.
- Relocating any part of the layout must never separate an operator from their
  credentials.
- Pre-1.0 posture: a breaking configuration change is allowed to be clean (no aliases, no
  deprecation window).

## Considered Options

1. **Independent roots, each its own cwd-anchored location with exactly one CLI switch**;
   sub-paths anchor beneath a root and are configuration-file-only.
2. **Keep a consolidated `BaseDir` and derive every location from it.** Rejected:
   relocating runtime data must leave the wiki where it is — under a shared base, moving
   the base moves everything, and re-pinning the wiki needs a second option anyway.
3. **One root with the wiki nested inside it.** Rejected: the wiki must be independently
   version-controllable, which requires it to be a separate sibling, not a subtree.
4. **Cap the switch surface by convention and review rather than enforcement.** Rejected —
   Constitution Principle IV: conventions not enforced by CI do not exist, and the
   sixteen-switch drift is the proof.

## Decision Outcome

Chosen option: **independent, cwd-anchored roots with exactly one CLI switch each and no
consolidated base directory**, because it makes every operator-relevant location
independently placeable while keeping the operator-facing surface minimal and enforced.

- **The Hub's on-disk surface is a small set of independent directory roots.** Today
  those are the data root (`--data-dir`), the wiki root (`--wiki-dir`), and the agent
  root (`--agent-dir`) established when this shape was first adopted, plus the memory
  root (`--memory-dir`) added by [ADR-024](ADR-024-memory-directory-root.md). The count
  is not fixed by this ADR: root growth through the same pattern is an extension, and
  ADR-024 is the precedent for adding one deliberately.
- **Each root anchors at the process working directory** — never beneath another root and
  never at a shared base. An absolute configured value overrides the anchor; a relative
  value resolves against it. Shipped defaults may happen to nest (the agent root's
  default `.grimoire/agents` sits under the data root's default `.grimoire`), but that is
  a coincidence of values, not a coupling: relocating one root never moves another. Root
  independence is a **Feature-Scoped Invariant**, covered by classicist integration tests
  against the real resolver and filesystem (`PathGroupingInvariantTests`,
  `DataDirRelocationTests`, `WikiDirIsolationTests` in `Grimoire.IntegrationTests`).
- **There is no consolidated base directory.** No option, switch, or code path derives
  one root from another or from a shared parent.
- **Exactly one CLI switch per root; sub-paths never get switches.** Locations that are
  internal layout beneath a root (raw intake, the state database, write locks, the
  bookkeeping sub-paths) are configured only in the configuration file
  ([ADR-042](ADR-042-mandatory-configuration-file.md)); adding a new sub-path means a new
  options field and configuration key — never a new switch.
- **No silent regrowth of the switch surface** (formerly ADR-022's rule R1, the switch
  cap): the set of path switches the CLI exposes is exactly one per root, each with a
  description. This is a **Feature-Scoped Invariant**, enforced behaviorally per
  [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md): the
  out-of-process `--help` assertion in `HubHelpUsageTests` runs the built binary and
  fails on any switch the surface silently gained. Growing the surface by a new root is a
  deliberate, reviewed extension of the enumeration that test asserts — not an
  incidentally broken test.

### Consequences

- Good, because each root can be independently version-controlled, backed up, retained,
  or placed on different storage, and the switch surface an operator must understand is
  one obvious switch per root.
- Good, because the secrets file (`SecretsFile`, default `.env`) anchors at the process
  working directory and belongs to no root — relocating any root never separates an
  operator from their credentials. This placement was fixed when ADR-022 moved the file
  out of the data directory (amending
  [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md)'s
  `<base>/data/.env` anchoring); ADR-019's credential-delivery mechanism itself is
  unchanged.
- Bad, because there is no single knob that relocates the whole installation — an
  operator moving everything sets each root. Accepted: that coupling is exactly what the
  consolidated base was removed to eliminate.
- Bad, because adopting this shape was a clean breaking change (thirteen switches removed
  with no aliases and no data migration) — accepted on the pre-1.0 posture.
- Neutral, because an operator who explicitly configures one root to equal or nest inside
  another gets exactly that; the resolver anchors and validates but does not police a
  deliberate choice.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new root added deliberately through the
  same pattern — cwd-anchored, mutually independent, exactly one described CLI switch,
  its sub-paths configuration-file-only (ADR-024 is the precedent); new sub-paths added
  beneath an existing root in the configuration file; a new consumer of an existing
  root's resolved path.
- **Invalidations (would require full supersession):** reintroducing a consolidated base
  directory or any derivation of one root from another; ambient root discovery (deriving
  a root from a repo checkout, executable location, or OS convention instead of explicit
  configuration); a root without an explicit CLI switch; sub-paths regaining switches.

## More Information

Supersedes [ADR-022](ADR-022-minimal-directory-configuration-surface.md). ADR-022's other
two aspects are re-decided in [ADR-042](ADR-042-mandatory-configuration-file.md) (the
mandatory configuration file as the sole source of defaults) and
[ADR-043](ADR-043-build-distributed-agent-artifacts.md) (build-distributed agent
artifacts and the single launch mode).

Read alongside:
[ADR-040](ADR-040-runtime-path-composition.md) — the single
composition point, per-option precedence evaluation, fail-fast validation, and the
no-ambient-discovery rule; [ADR-024](ADR-024-memory-directory-root.md) — the memory root
and the anchor-grouped configuration-file shape;
[ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) — why
path-surface Feature-Scoped Invariants are enforced by behavioral tests rather than
reflection/IL scans; [ADR-019](ADR-019-devcontainer-host-runtime-and-credential-access.md)
— how the working-directory-anchored secrets file reaches a devcontainer. None of their
decisions are restated or narrowed here.

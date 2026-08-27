---
status: accepted
supersedes: ADR-022
superseded_by: []
reason: null
---

# ADR-042: Mandatory Configuration File as the Sole Source of Configuration Defaults

## Context and Problem Statement

Somewhere, every configurable location and layout value needs a default. ADR-009 ended
its precedence chain with "> code defaults": every default lived as a constant in code,
invisible to an operator, which is exactly what let a sixteen-switch surface feel
optional-but-invisible — the effective layout could not be read anywhere. A code default
also creates two sources of truth the moment a configuration file ships alongside it,
with a silent winner. This ADR decides where configuration defaults live: in a mandatory,
versioned configuration file, or in code.

## Decision Drivers

- The full effective layout must be readable in one versioned file — defaults must be
  visible, diffable, and reviewable, not buried as code constants.
- Two sources of default truth (code constant plus file value) are worse than either
  alone: whichever wins does so silently.
- A missing or incomplete configuration must be a named startup failure, not a silent
  fall-through to values the operator cannot see.
- Constitution Principle IV: the rule must be enforceable in CI, not a convention.

## Considered Options

1. **`appsettings.json` is mandatory and is the only source of default values**; a
   missing or incomplete file is a startup failure naming the file and the missing keys.
2. **Code defaults retained as a safety net, configuration file optional.** Rejected: it
   is the shape that produced the invisible-default problem — the operator cannot see
   the effective layout anywhere.
3. **Code defaults retained, configuration file mandatory.** Rejected as the worst of
   both: two sources of truth with a silent winner.

## Decision Outcome

Chosen option: **mandatory `appsettings.json` as the single source of configuration
defaults**, because it makes the effective layout readable in one versioned file and
turns a missing configuration into a loud, named failure instead of a silent fallback.

- **The file is mandatory and versioned.** `backend/src/Grimoire.Hub/appsettings.json`
  is tracked in git, ships the default values for every directory root and sub-path, and
  is copied to the build output so a deployed installation carries it.
- **No code-level defaults.** No production code constant holds a default for a
  configured root or sub-path, and no code-level literal duplicates a value the
  configuration file owns. The terminal "> code defaults" step of the former precedence
  chain does not exist: per-option precedence is CLI switch > environment variable >
  configuration file, and ends there.
- **Startup fails fast without it.** After binding, resolution validates that every root
  carries a non-empty configured value and fails with a `GrimoirePathValidationException`
  naming `appsettings.json` and the missing keys as full key paths (e.g.
  `Grimoire:Paths:Memory:Dir`) when one does not. There is no path by which a missing
  file or key silently produces a working layout.
- **No code-level literal duplicates a config default** (formerly ADR-022's rule R2):
  this is a **Feature-Scoped Invariant**, enforced behaviorally per
  [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md): the
  fail-loudly facts in `StartupValidationTests` (an empty configuration throws
  `ConfigurationMissing` naming every root before touching any directory — a code-level
  fallback anywhere in the resolution path would make resolution silently succeed and
  the test fail), with `DefaultLayoutTests` and `ZeroConfigStartupTests` pinning the
  config-file-tier default values themselves.
  [ADR-024](ADR-024-memory-directory-root.md)'s rule M2 extends the same guarantee to
  the memory root and is owned there.

### Consequences

- Good, because a first run needs nothing but a checkout: the shipped file *is* the
  default configuration, and everything an operator might override is visible in one
  place with git history.
- Good, because configuration drift is loud — a deployment that loses or truncates its
  configuration file fails at startup naming the file and keys, instead of silently
  running against invisible built-in values.
- Bad, because the application cannot start without its configuration file — the file
  becomes a mandatory deployment artifact. Mitigated: the build output already carries
  it, so any normally-built deployment has it by construction.
- Neutral, because an unrecognized configuration key is silently ignored by the
  configuration binder, as ADR-024 decided (no superseded-key detection) — this ADR
  guarantees loud failure for *missing* required values, not for misspelled ones.
- Neutral, because the *shape* of the file's content (the anchor-grouped `Grimoire:Paths`
  tree) is owned by [ADR-024](ADR-024-memory-directory-root.md); this ADR fixes only that
  the file is mandatory and that defaults live nowhere else.

## Change Triggers

- **Extensions (do not invalidate this ADR):** new configuration keys or groups added to
  the file for new options, roots, or sub-paths — ADR-024's regrouping of the flat key
  list into anchor groups was exactly such an evolution of the file's content; new
  configuration sections for future subsystems, provided their defaults live in the file
  and nowhere else.
- **Invalidations (would require full supersession):** reintroducing code-level fallback
  defaults for any configured value; making the configuration file optional; adding a
  second source of default truth (embedded resource defaults, a generated file, an OS
  convention) alongside or beneath the file.

## More Information

Supersedes [ADR-022](ADR-022-minimal-directory-configuration-surface.md). ADR-022's other
two aspects are re-decided in [ADR-041](ADR-041-independent-directory-roots.md) (the
independent directory roots and switch surface) and
[ADR-043](ADR-043-build-distributed-agent-artifacts.md) (build-distributed agent
artifacts and the single launch mode).

Read alongside:
[ADR-040](ADR-040-runtime-path-composition.md) — the single
composition point that binds the file, the per-option precedence mechanism, and the
general fail-fast validation of resolved locations;
[ADR-024](ADR-024-memory-directory-root.md) — the anchor-grouped configuration-file
shape, key naming, and the decision not to detect superseded keys;
[ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) — the behavioral
enforcement mechanism for this ADR's invariant. None of their decisions are restated or
narrowed here.

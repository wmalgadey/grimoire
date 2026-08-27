---
status: accepted
supersedes: ADR-009
---

# ADR-040: Runtime Path Composition at One Explicit Configuration Point with Fail-Fast Validation

## Context and Problem Statement

Before ADR-009, every runtime location Grimoire used — wiki content root, raw intake
storage, the operational-state database, the secrets file, agent instruction files, the
agent worker binary — was derived from the root of the git checkout the process happened
to run in (`git rev-parse --show-toplevel`), through hard-coded project-layout fragments
scattered across `Program.cs` and per-concern path helpers. A deployed installation has
no git checkout and no source layout, so the application could not run outside a
developer clone; defaults were invisible to operators; and different call sites could
resolve the "same" location differently. How runtime paths are resolved is a
cross-cutting shape every process (Hub, current and future agents) inherits, so it is a
system boundary fixed by ADR.

This ADR restates ADR-009's still-current core as one self-contained decision: where
runtime paths come from and when they are checked. It deliberately does not decide which
roots exist or how the directory layout is shaped — ADR-009's consolidated
`BaseDir`/two-homes layout was retired by ADR-022, and the current root model
(independent cwd-anchored roots) is owned by ADR-041.

## Decision Drivers

- The application must run in a directory with no repository structure and without
  version-control tooling installed (spec 005 FR-002).
- Every runtime location must be defined and resolved in one obvious place, so a new
  location has exactly one home and an operator has exactly one surface to inspect.
- Prod and dev must differ only in configuration values, never in code paths.
- A misconfigured location must fail at startup with an actionable message, not surface
  mid-request as a confusing I/O error.
- Constitution Principles III/IV: the boundary needs automated structural enforcement and
  CI gating, using no new configuration infrastructure beyond the stock .NET providers.

## Considered Options

1. **Single path-options composition point + standard configuration layering, no ambient
   discovery anywhere**
2. Keep repo-root discovery but allow overriding individual paths
3. OS-conventional app-data directories (XDG/AppData) as defaults
4. A single mandatory base-directory switch with no defaults (fully explicit always)

## Decision Outcome

Chosen option: **Option 1**, because it makes the application deployable to any
directory while keeping every location centrally defined, operator-visible, and
verifiable at startup. Option 2 keeps the undeployable git dependency as the default
path; Option 3 hides the layout in per-OS conventions an operator cannot read in one
place; Option 4 trades away the zero-flag first run for explicitness the configuration
file already provides.

- **Single composition point.** One options graph (`GrimoirePathOptions`, bound from the
  `Grimoire:Paths` configuration section) and one resolver (`GrimoirePathResolver`,
  namespace `Grimoire.Hub.Runtime.Paths`) define and resolve every runtime location into
  one `ResolvedGrimoirePaths` record. No other production code derives a root path or
  reads ambient process context (current directory, executable directory) for path
  purposes. Adding a runtime location means adding it to this options graph — never
  resolving it locally at the consuming site.
- **Standard configuration layering, one precedence.** Path options bind through the
  stock `Microsoft.Extensions.Configuration` providers with per-option precedence:
  command-line switch > environment variable > `appsettings.json`. No profile mechanism,
  no custom configuration source. Which options carry CLI switches, and the rule that the
  configuration file is the sole source of defaults (no code-default terminal in the
  chain), are owned by ADR-041 and ADR-042 respectively.
- **No ambient discovery anywhere.** No production code performs repo-root discovery
  (`git rev-parse`), walks parent directories, or probes the executable's location to
  find a runtime location. Ambient process-context reads exist only inside the
  composition point itself, where the working directory serves as the anchor for
  relative configured values.
- **Fail-fast validation at startup.** After binding, the resolver validates every
  location before the process serves or a command runs: a missing required input aborts
  startup with a `GrimoirePathValidationException` naming the logical location, the
  configured value, and the resolved path; writable data locations are auto-created; and
  every successful start emits one `paths_resolved` report listing all resolved
  locations. No path error is deferred to first use.
- **Spawned agents receive their paths explicitly.** As a direct consequence of the
  no-ambient-discovery rule, every agent child process is handed the paths it needs as
  explicit spawn parameters by the Hub — it never re-derives a location from its own
  process context. The spawn contract itself (argument set, lifecycle, streams) is owned
  by ADR-036.

Rules introduced by this ADR, classified per Constitution Principle III:

- **Boundary Rule — ambient-context confinement.** In production assemblies, calls to
  `Directory.GetCurrentDirectory`, `Environment.CurrentDirectory`, and
  `AppContext.BaseDirectory` are permitted only in namespace
  `Grimoire.Hub.Runtime.Paths`, and no production assembly contains the IL string
  literals `rev-parse` or `--show-toplevel` (tripwire against reintroducing repo
  discovery). Enforced by `Grimoire.ArchTests/RuntimePathsBoundaryRuleTests`
  (Mono.Cecil IL scan, Red/Green probed).
- **Feature-Scoped Invariant — fail-fast startup validation.** An invalid or missing
  location fails startup naming the logical location, configured value, and resolved
  path; writable locations are auto-created; the `paths_resolved` report is emitted on
  every successful start. Enforced by classicist, state-based integration tests against
  the real resolver and real filesystem
  (`Grimoire.IntegrationTests/PathConfiguration/StartupValidationTests`,
  `ZeroConfigStartupTests`, `PathLoggingContractTests`), per the behavioral-enforcement
  direction ADR-032 established for path-surface invariants.

### Consequences

- Good, because the application is deployable to any directory: nothing about path
  resolution assumes a source checkout, a git binary, or a particular install layout.
- Good, because every current and future runtime location has exactly one place to be
  defined, one precedence story, and one startup report an operator can read — path
  behavior is diagnosable from configuration plus one log event.
- Good, because misconfiguration is a named startup failure instead of a latent
  mid-request error, and because child processes cannot drift from the Hub's view of the
  layout (they are told, they do not guess).
- Bad, because all path flexibility funnels through one options graph: a consumer with a
  genuinely new location must touch the central composition point rather than resolving
  locally. Accepted — that friction is the mechanism that keeps the surface auditable.
- Neutral, because the composition point constrains *where* paths are composed, not
  *what* the layout is; layout decisions evolve independently (ADR-041) without
  reopening this boundary.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new path option — root or sub-path —
  added to `GrimoirePathOptions` and flowing through the same resolver, precedence, and
  validation (ADR-024's memory root did exactly this); a new configuration provider
  slotted into the standard precedence chain; a new consumer reading locations from
  `ResolvedGrimoirePaths`; additional validation categories for new location kinds; a
  new agent type receiving its paths as spawn parameters.
- **Invalidations (would require full supersession):** any production component
  discovering a path ambiently (repo-root discovery, parent-directory walking,
  executable-relative probing) outside the composition point; a second composition point
  or per-component path resolution; replacing fail-fast startup validation with lazy or
  on-first-use resolution; a spawned process deriving its own locations instead of
  receiving them as spawn parameters; abandoning the stock configuration-provider
  precedence for a custom resolution order.

## More Information

- [ADR-041](ADR-041-independent-directory-roots.md) owns the root model:
  which independent cwd-anchored roots exist, their switches, and the sub-path anchoring
  graph this composition point resolves.
- [ADR-042](ADR-042-mandatory-configuration-file.md) owns the default-value
  policy: the mandatory configuration file as the sole source of default paths.
- [ADR-036](ADR-036-agent-child-process-spawn-contract.md) owns the agent spawn
  contract through which resolved paths reach agent processes.
- [ADR-003](ADR-003-domain-operational-state-persistence.md) owns the domain-state vs.
  operational-state persistence split the resolved locations serve.
- [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) established
  the behavioral (non-reflection) enforcement style this ADR's Feature-Scoped Invariant
  follows; [ADR-034](ADR-034-path-and-subprocess-containment-hardening.md) owns
  adversarial path-traversal containment at the guarded tool boundary, a separate
  concern from configuration-time composition.
- Supersedes [ADR-009](ADR-009-runtime-path-configuration.md), whose consolidated
  `BaseDir`/two-homes directory layout and per-location switch surface were retired by
  ADR-022 (itself superseded by ADR-041/042/043); the composition-point, precedence,
  no-ambient-discovery, and fail-fast rules restated here are ADR-009's surviving core.

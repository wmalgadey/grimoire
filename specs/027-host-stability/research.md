# Phase 0 Research: Host Stability Guarantee for Agent Runs

**Feature**: `specs/027-host-stability/spec.md` | **Date**: 2026-08-25

This feature has no `NEEDS CLARIFICATION` markers in Technical Context — it hardens an
existing mechanism (`GuardedToolExecutor`'s canonicalize-then-match design, ADR-006) and
an existing invariant (fixed-executable, argument-list process spawning, ADR-002) inside
the current .NET/C# stack, introducing no new dependency, storage, or runtime. Research
below resolves the *design* questions the spec's User Stories raise, not stack unknowns.

## D1: Percent-encoding, Unicode-normalization, and null-byte path variants

**Decision**: Add explicit, controlled rejection of embedded NUL characters in
`GuardedToolExecutor.Canonicalize` (catch the `ArgumentException` .NET's `Path` APIs
already throw for an embedded `\0` and turn it into a normal policy denial —
`malformed_path` — rather than letting it propagate as an unhandled exception). For
percent-encoding and Unicode-normalization variants, no decoding step is added anywhere
in the guarded-tool path; a dedicated hermetic test suite proves the existing
canonicalize-then-match mechanism already denies or safely contains them.

**Rationale**: The guarded-tool boundary never URL-decodes a `path` argument — it is
passed to `Path.Combine`/`Path.GetFullPath` as a literal string. On Linux (this project's
target platform, ADR-001), filenames are opaque byte sequences with no percent-decoding
or Unicode normalization performed by the OS or the .NET path APIs. A string like
`%2e%2e%2Foutside` or a fullwidth-solidus confusable therefore never resolves to a
traversal — it resolves to a (usually nonexistent) literal filename that either denies
normally (`no_rule`/`out_of_scope`, file not found) or, if it happens to name a real file
inside an allowed prefix, is legitimately inside the root by construction. Decoding it
ourselves would be solving a problem the code does not have, and would introduce a new
attack surface (a path that was never meant to be reinterpreted, being reinterpreted).
What genuinely needs fixing is the null-byte case: unlike encoding tricks, an embedded
`\0` does not fail closed today — it throws an unhandled `ArgumentException` out of
`Canonicalize`, which is a *worse* outcome than denial (an unhandled exception on the
tool-call path risks the run itself, which is the opposite of what a host-stability
feature should do).

**Alternatives considered**:
- *Explicitly percent-decode or Unicode-normalize the path before canonicalizing, then
  re-check* — rejected: adds a decoding layer that does not correspond to how the
  filesystem or any other part of the pipeline interprets the string, and could make a
  previously-safe literal filename (one that happens to contain a `%2e` substring by
  coincidence) be reinterpreted as something else.
- *Reject any path containing `%` or non-ASCII characters outright* — rejected: wiki
  content and filenames legitimately use non-ASCII characters (the project's own pages);
  an overly broad reject-list would break legitimate content for no containment benefit.

## D2: Multi-hop / chained symlink resolution

**Decision**: `GuardedToolExecutor.ResolvePhysicalPathInRepository`'s per-segment walk
resolves only the *first* reparse point it encounters per top-level call, then appends
the remaining path segments literally without re-checking them for further reparse
points. Fix: after resolving one segment's link target and reconstructing the candidate
path, recurse into the same resolution walk on that reconstructed path (instead of
`break`-ing out) so a target that is itself reached through further symlinks is fully
resolved, capped at a fixed hop limit.

**Rationale**: Spec Edge Case ("a path mixes multiple obfuscation techniques at once...
through a symlinked intermediate directory") requires a symlink *chain* to resolve to its
true final physical target, not just its first hop. `FileSystemInfo.ResolveLinkTarget
(returnFinalTarget: true)` (already used by `TryResolveLinkTarget`) does collapse a chain
*at one segment*, but a second symlink appearing later in the *remaining*, unresolved
tail of the path is never walked. Recursing the whole method on the reconstructed path
re-applies the same per-segment check to every remaining segment, closing this gap. A hop
cap of 40 (Linux's conventional `MAXSYMLINKS`/`ELOOP` limit) guards against a symlink
cycle turning this into unbounded recursion; exceeding it is a denial (`symlink_loop`).

**Alternatives considered**:
- *P/Invoke into a platform `realpath(3)`* — rejected: introduces native interop outside
  any adapter namespace for a problem the existing managed per-segment walker already
  solves once made recursive; `realpath` also cannot resolve a path whose final segment
  does not yet exist (the common case for a `write_file` target), which is exactly why
  the hand-rolled walker exists in the first place.

## D3: Post-validation symlink swap (time-of-check-to-time-of-use)

**Decision**: Immediately before the mutating filesystem operation executes — the
write's temp-file `File.Move` and the delete's `File.Delete` — recompute the canonical
physical path via the same resolution walk and compare it, ordinally, to the canonical
path that was originally validated against policy. A mismatch denies the operation
(`revalidation_failed`) instead of proceeding.

**Rationale**: Spec Acceptance Scenario 4 requires the outcome to be "denied or...
provably confined to the originally validated target — never the swapped-in
destination," not full elimination of the race window. A revalidate-immediately-before-
use check shrinks the window between "path judged safe" and "path acted upon" to the
smallest span achievable in managed code without OS-level atomic primitives, and makes
the remaining window a matter of nanoseconds rather than the width of the write itself
(journal + temp-file write + rename). This is the pragmatic, in-process mitigation
consistent with Principle I's rejection of Big-Design-Up-Front: it does not require a new
adapter, port, or native dependency, and it is fully covered by a hermetic test that
swaps a symlink between validation and execution and asserts denial.

**Alternatives considered**:
- *`openat`/`O_NOFOLLOW`-based safe path resolution via native interop* — rejected as
  disproportionate for this feature: it would require a new native-interop adapter
  (its own containment namespace, ADR review) to close a window that is already narrow
  and already covered by acceptance scenario 4's "provably confined" alternative. Revisit
  only if the revalidation mitigation is shown insufficient in practice.
- *Hold an OS file lock across validation and execution* — rejected: the guarded-tool
  boundary already coordinates cross-process writes through `SharedFileWriteGuard`
  (ADR-015) for a different purpose (write conflicts, not path safety); overloading it
  for TOCTOU protection would conflate two independent concerns and does not, by itself,
  prevent a symlink swap performed by a process outside that coordination scheme.

## D4: Spawn-site registry — structural enforcement mechanism (FR-004)

**Decision**: Add a new Mono.Cecil IL-scan architecture test to `Grimoire.ArchTests`,
modeled directly on the existing `NonBlockingDispatchRuleTests` idiom (which already
enumerates every `Process.WaitForExit*` call site in `Grimoire.Hub.dll` and allowlists
exactly `AgentProcessHost` and `MarkItDownConverter`). The new test scans for
`newobj System.Diagnostics.Process::.ctor`/`System.Diagnostics.ProcessStartInfo::.ctor`
and `call System.Diagnostics.Process::Start` instructions across the whole assembly and
asserts the enclosing outermost type is in the same two-member allowlist.

**Rationale**: This is the FR-004 requirement made literal: "the set of process-spawn
call sites... MUST be enumerable and closed." Today's inspection confirms exactly two
production types construct a process — `AgentProcessHost` (5 internal `Start*Process`
methods, one per agent/mode) and `MarkItDownConverter` (1) — matching the spec's
Assumptions. This is a **Dependency & Layering Boundary Rule** (Principle III): "which
type may construct a process" is a durable dependency direction unrelated to how any one
feature's surface grows, so it earns a permanent, Red/Green-probed structural test, not a
Feature-Scoped Invariant. `ArchScan.cs`'s shared IL-walk helpers (`FindCalls`,
`FindConstructions`) already provide the exact primitives this rule needs, reused as-is.

**Alternatives considered**:
- *NetArchTest namespace-dependency rule* — rejected: NetArchTest (already used
  elsewhere in this suite) restricts assembly/namespace dependencies, but cannot express
  "only these specific call sites may call this specific method" at the IL instruction
  level; Mono.Cecil is the established idiom in this codebase for exactly that shape of
  rule (`ArchScan.cs`, `NonBlockingDispatchRuleTests`).

## D5: Argument-list / fixed-executable invariant enforcement (FR-003)

**Decision**: Extend the same structural test to also assert that neither allowlisted
spawn site ever sets `ProcessStartInfo.Arguments` (the shell-parsed string property) —
only `ArgumentList.Add` calls are present. Inspection of both files today confirms this
already holds (`grep` found zero `.Arguments =` assignments across `backend/src`); the
test pins it as a **Boundary Rule** so a future edit cannot silently regress to shell-
string construction without the CI gate catching it.

**Rationale**: FR-003 requires "arguments passed via an argument list rather than a
single shell-interpreted command string" to hold permanently, not just today. Structural
IL enforcement is the correct mechanism per the same Boundary Rule reasoning as D4 — it
is a property of *how* a call site is written, checkable without running any code, and
belongs in the same Phase 0 Red/Green-probed test as D4 rather than a separate one, since
both protect the same two call sites and the same underlying invariant (ADR-034 groups
them as one rule pair).

## D6: Filename-derived extension allowlist (FR-005)

**Decision**: No design or code change. `IngestSubmissionValidator`'s fixed
`HashSet<string>` allowlists (`_markdownExtensions`, `_officeExtensions`, plus a literal
`.pdf` check) already validate a submission's extension against its declared kind before
any conversion or storage path is reached, and are already exercised incidentally by
`IngestSubmissionApiTests`/`IngestConvertStepTests`. This feature adds one dedicated,
adversarial-input integration test asserting an unlisted extension (e.g. `.exe`, `.sh`)
is rejected before any converter or storage code runs — closing SC-004 with an intent-
named test rather than relying on incidental coverage.

**Rationale**: This is a **Feature-Scoped Invariant** (Principle III): the fact asserted
("only these extensions are accepted") is expected to change in an ordinary, reviewable
way whenever the project adds a new supported document format, so it is pinned by a
classicist behavioral test against `IngestSubmissionValidator`'s real validation
behavior — never by a reflection/IL test enumerating the current extension list.

## Summary: Boundary Rules vs. Feature-Scoped Invariants

Per Constitution Principle III, ADR-034 (drafted alongside this plan) must tag each rule
it enumerates. This research resolves that classification for `tasks.md` Phase 0:

| Rule | FR | Category | Enforcement |
|------|----|----|----|
| R1 — only `AgentProcessHost`/`MarkItDownConverter` construct a `Process` | FR-004 | Boundary Rule | Phase 0, Mono.Cecil IL scan, Red/Green probe |
| R2 — those two sites use `ArgumentList` only, never `Arguments` (string) | FR-003 | Boundary Rule | Phase 0, same IL scan, Red/Green probe |
| R3 — guarded path resolution denies every adversarial path variant (D1-D3) | FR-001, FR-002 | Feature-Scoped Invariant | Classicist integration test against a real filesystem (extends `PathTraversalTests`) |
| R4 — filename-derived extensions are validated against a fixed allowlist before use | FR-005 | Feature-Scoped Invariant | Classicist integration test against `IngestSubmissionValidator` |

# Feature Specification: Host Stability Guarantee for Agent Runs

**Feature Branch**: `027-host-stability`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Host stability guarantee for agent runs. Constitution v1.12.0 (Principle V, \"Host stability guarantee\") requires: regardless of what a task or an instruction file says — including malformed or adversarial content — the harness MUST ensure the agent process cannot destabilize the host: unbounded CPU, memory, disk, or subprocess consumption, or any action outside the guarded tool boundary and credential scope already in force. This guarantee holds independently of instruction-file content and must be proven by hermetic tests exercising real resource pressure (never by agent-behavior evaluation, since it must hold even when the agent is actively misbehaving). Today this is a known gap (recorded in the constitution's Sync Impact Report for v1.12.0): agent child processes are spawned with no CPU/memory/disk quota and no wall-clock ceiling on the dispatch path; a spawned agent may itself spawn arbitrary subprocesses (the existing tree-kill is reactive only, tied to the liveness window); the markdown converter child process has a timeout but no memory cap and its stdout is buffered unbounded in Hub memory; URL fetching downloads without any size limit; guarded writes have no per-write or per-run content-size cap, so disk growth within policy scope is unbounded. Already bounded today (keep, not in scope to change): agent turn cap, context cap, and spend cap in the agent loop; the converter wall-clock timeout; the liveness-window supervision. The feature: the operator can rely on the host surviving any single agent run. Every resource vector an agent run can consume (CPU time, resident memory, disk writes, downloaded/converted content size, number of child processes, total run wall-clock) is bounded by an operator-configurable limit with a safe default; hitting a limit terminates or denies the offending operation deterministically, is recorded with a reason (like guardrail denials are today), surfaces to the operator through the Hub's observability (metrics/log events/spans per Principle IV, visible on a user-facing surface per the operator loop), and never corrupts durable state (task artifacts and records reflect the terminated run's true state). An instruction file or task input must have no way to raise or disable these limits. All success criteria for this feature are deterministic harness guarantees (100%) — there is no agent-judgment criterion in scope, so no eval suite; verification is hermetic tests exercising real resource pressure per the constitution."

**Revision input**: User correction (2026-08-25), verbatim: "Ich würde das feature in frage stellen, da es das ziel dieses projekteS ist, agenten im container zu isolieren. Dort gibt es inherente möglichkeiten die ressourcen der agenten zu limitieren bzw zu kontrollieren. Wenn das feature ein direkter schluss aus der änderung der constitution ist, müssen wir hier nachschärfen bzw. Genauer beschreiben was das ziel ist. Sicher ist es sinnvoll ressourcenverbrauch zu monitoren, aber zu reglementieren finde ich nicht notwendig, dafür sind container ideal und das ist eher eine deployment variante. Was ich meinte ist, dass der harness sicherstellen soll, das der agent sich nur so im system „bewegen" kann dass der host nicht korrumpiert wird, z.b. durch das schreiben in falsche dateien oder durch starten von anwendungen, die das system instabil machen. Das llm muss sicher betrieben werden, der hub monitored und orchestriert"

**Note on this revision**: The original draft above read the constitution's Host stability
guarantee as a resource-quota problem (CPU/memory/disk/wall-clock ceilings enforced by the
harness). The user corrected this: resource governance is a deployment concern — a
container or comparable OS-level sandbox around the agent process already provides it
(the direction ADR-002 defers to), and the harness reimplementing it would duplicate that
sandbox rather than complement it. The constitution's Host stability guarantee (amended in
the same PR that opened this correction) now reads as a **containment** guarantee: the
harness must ensure the agent process cannot corrupt the host by writing to the wrong
files or by causing the wrong things to run — not that it must meter how much CPU or
memory the agent consumes. Everything below reflects that corrected scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Adversarial paths cannot escape the guarded write boundary (Priority: P1)

The operator relies on every guarded write, read, and delete staying inside the wiki and
memory roots no matter what a task, an ingested document, or an instruction file
contains. Today's guarded-tool boundary already resolves plain traversal (`../`),
absolute-path overrides, and simple symlink escapes back to their canonical target before
allowing an action — but an adversary rarely stops at the plain case. A path expressed
through encoding tricks (percent-encoding, Unicode normalization variants of `.` and `/`),
a null byte appended to a permitted-looking prefix, or a symlink swapped in after
validation but before the write executes must be rejected exactly as reliably as the
plain case.

**Why this priority**: This is the constitutional guarantee itself (Principle V, Host
stability guarantee) as corrected: where the agent may act, enforced regardless of task
or instruction content. The happy-path mechanism already exists (`GuardedToolExecutor`'s
canonicalize-then-match design, ADR-006) and is already tested for the plain cases
(`PathTraversalTests`); this story closes its remaining adversarial-input gaps and pins
the whole guarantee down with hermetic, adversarial tests so it cannot regress silently.

**Independent Test**: Can be fully tested by submitting each adversarial path variant
(encoded traversal, null-byte suffix, post-validation symlink swap) to the guarded write,
read, and delete tools against a real filesystem and asserting denial plus an untouched
target outside the root.

**Acceptance Scenarios**:

1. **Given** a write request whose path uses plain `../` segments to point outside its
   root, **When** it is submitted, **Then** the write is denied and no file outside the
   root is created or modified (already covered today — regression-guarded here).
2. **Given** a write request whose path is percent-encoded or uses a Unicode
   normalization variant to represent an out-of-root traversal, **When** it is submitted,
   **Then** it is denied identically to the plain-text case.
3. **Given** a path containing a null byte followed by an out-of-root suffix, **When** it
   is submitted, **Then** the write is denied — the path is never silently truncated to a
   permitted-looking prefix and then acted on.
4. **Given** a symlink inside the write scope that is swapped to point outside the root
   between the boundary check and the actual filesystem write, **When** the write
   executes, **Then** it is denied or is provably confined to the originally validated
   target — never the swapped-in destination.
5. **Given** any of the above adversarial variants arriving via task input, an ingested
   document, or instruction-file content, **When** the run executes, **Then** the outcome
   is identical regardless of which source carried it — nothing an agent reads or a task
   supplies can loosen the boundary.

---

### User Story 2 - Every process the harness spawns is a known, non-injectable one (Priority: P2)

The operator relies on the harness never spawning a process the operator did not sanction,
and never letting task, document, or agent-generated content be interpreted as a command.
Every process-spawn site in the codebase today (the agent worker launch, the document
converter) already uses a fixed executable path with argument-list invocation rather than
a shell-parsed command string — but that safety is a property of how the code happens to
be written today, not a guarantee anything currently proves or a future change is
prevented from breaking.

**Why this priority**: Subprocess containment is the second half of the corrected Host
stability guarantee. It is lower priority than User Story 1 only because today's spawn
sites are already safe by construction (confirmed by inspection: fixed executables,
`ArgumentList` rather than a shell string, filename-derived values validated against an
allowlist before use) — this story turns that implicit property into a structurally
enforced, regression-proof one.

**Independent Test**: Can be fully tested by a structural test enumerating every
process-spawn call site in the production codebase and asserting each matches the
required pattern (fixed executable, argument-list construction), Red/Green-probed by
introducing a violating call site and confirming detection.

**Acceptance Scenarios**:

1. **Given** the current codebase, **When** the structural spawn-site test runs, **Then**
   every process-spawn call site is accounted for in an enumerated, reviewed set, and
   each uses a fixed executable path with argument-list invocation.
2. **Given** a new process-spawn call site is added without being added to the enumerated
   set, **When** the test suite runs, **Then** the structural test fails, naming the new
   site.
3. **Given** a spawn invocation whose arguments include task-, document-, or
   agent-generated content, **When** the process is constructed, **Then** that content
   reaches the process only via the argument list — never concatenated into a single
   shell-interpreted command string.
4. **Given** an ingested document whose filename-derived extension selects which
   converter or code path handles it, **When** it is processed, **Then** only an
   allowlisted extension is accepted; anything else is rejected before it can influence
   process construction.

---

### Edge Cases

- A symlink swap lands in the narrow window between the boundary check resolving a path
  and the filesystem write actually executing (time-of-check-to-time-of-use).
- A path mixes multiple obfuscation techniques at once (e.g., percent-encoded traversal
  through a symlinked intermediate directory).
- A future agent type or tool adds its own process-spawn call site — must be caught by
  the structural test before merge, not discovered later.
- A future converter or fetch path is added that shells out to a different executable
  with different argument-construction conventions — must satisfy the same fixed-
  executable/argument-list pattern or fail the structural test.
- An adversarial path or filename is valid enough to pass OS-level validation but is
  still meant to probe the boundary (e.g., a path that is technically inside the root
  after resolution but was clearly constructed to test the edges of the canonicalization
  logic) — the boundary check must be exercised by these near-miss cases too, not just
  obviously-outside ones.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The harness MUST deny any guarded write, read, or delete whose target path
  resolves — after normalization, encoding-decoding, and symlink resolution — outside its
  policy-designated root, regardless of how the path is expressed: plain relative
  traversal, absolute-path override, symlink indirection, percent-encoding, Unicode
  normalization tricks, or null-byte truncation.
- **FR-002**: Path containment MUST use the resolved physical target, not the literal
  input string, as the authority for the boundary check, and MUST close the gap between
  validating a path and acting on it closely enough that a symlink swapped in during that
  window cannot redirect the action outside the root.
- **FR-003**: Every process the harness spawns MUST use a fixed, non-shell-parsed
  invocation: an executable path that is never derived from task, document, or
  agent-generated content, with arguments passed via an argument list rather than a
  single shell-interpreted command string.
- **FR-004**: The set of process-spawn call sites in the production codebase MUST be
  enumerable and closed: an automated structural test MUST fail if a new spawn call site
  is introduced without being added to that reviewed, enumerated set.
- **FR-005**: Filename- or content-derived values that influence which converter, tool,
  or code path handles a piece of content (e.g., a file extension) MUST be validated
  against a fixed allowlist before use, independent of what the requester supplied.
- **FR-006**: Both guarantees (FR-001–FR-002 path containment, FR-003–FR-005 subprocess
  containment) MUST hold regardless of task input or instruction-file content — no
  content an agent reads or a task supplies can loosen, disable, or bypass them, including
  content specifically constructed to test the boundary.
- **FR-007**: The guarantee MUST be verified by hermetic tests exercising the real
  containment mechanism — a real filesystem with real symlinks and adversarial path
  strings, the real spawn call sites — never by agent-behavior evaluation, since it must
  hold even when the agent is actively misbehaving.
- **FR-008**: This feature MUST NOT introduce resource ceilings (CPU, memory, disk,
  wall-clock, or process-count limits) or any enforcement mechanism for them; resource
  governance is explicitly out of scope (see Out of Scope).

### Key Entities

- **Containment boundary**: the policy-designated root(s) a guarded tool call is confined
  to, and the resolved physical path a requested action is checked against after
  normalization and symlink resolution.
- **Spawn-site registry**: the enumerated, reviewed set of process-spawn call sites in the
  production codebase, each declaring its fixed executable and confirming argument-list
  (non-shell) invocation.

## Success Criteria *(mandatory)*

All criteria below are deterministic harness guarantees (100%) per Principle II's
success-criteria split. There is no agent-judgment success criterion in this feature —
the guarantee must hold precisely when agent judgment has failed — so no criterion
carries a high-stakes/lower-stakes classification and no eval suite is in scope.

### Measurable Outcomes

- **SC-001**: 100% of adversarial path variants tested (plain traversal, absolute
  override, symlink escape, percent-encoded traversal, Unicode-normalization traversal,
  null-byte truncation, post-validation symlink swap) are denied by the guarded write,
  read, and delete boundary, with zero actions reaching outside the designated root.
- **SC-002**: 100% of process-spawn call sites in the production codebase are covered by
  the enumerated, structurally enforced set; introducing an unlisted spawn site fails the
  structural test in 100% of Red/Green probe attempts.
- **SC-003**: 100% of spawned-process invocations use argument-list construction with a
  fixed executable path; 0 invocations build a shell-interpreted command string from
  task, document, or agent-generated content.
- **SC-004**: 100% of filename- or content-derived values that select a converter, tool,
  or code path are validated against a fixed allowlist before use.

## Assumptions

- The happy-path containment mechanism already exists (`GuardedToolExecutor`'s
  canonicalize-then-match design, ADR-006) and already covers plain traversal, absolute
  overrides, and simple symlink escapes (`PathTraversalTests`). This feature hardens its
  residual adversarial-input gaps and pins the whole guarantee down with a dedicated,
  hermetic adversarial test suite — it is not a rewrite of the existing mechanism.
- Today's two process-spawn sites (the agent worker launch, the document converter)
  already follow the required pattern by construction (fixed executable, argument-list
  invocation, allowlisted file extensions). This feature makes that an enforced,
  structurally tested invariant rather than an implicit property that could regress
  silently when a new spawn site or converter is added later.
- Resource governance (CPU, memory, disk, wall-clock, process-count) is explicitly not
  this feature's concern. Per the corrected constitution, it is a deployment concern
  addressed by container or comparable OS-level sandbox isolation around the agent
  process (the direction ADR-002 already defers to); the harness's own obligation there
  is limited to monitoring/observability, which is a separate, general Hub concern not
  gated by this feature's Definition of Done.
- No ADR currently governs path-traversal safety or subprocess-spawn safety as a
  dedicated topic (ADR-006 documents the canonicalize-then-match design in passing;
  ADR-002 documents the spawn model without naming injection safety as a concern).
  Planning for this feature is expected to draft one, per Principle III.

## Out of Scope

- CPU, memory, disk, or wall-clock resource ceilings, or any mechanism to enforce them —
  explicitly rejected as within this feature's scope; that governance belongs to
  container/sandbox-level deployment isolation (ADR-002's deferred direction), not the
  harness.
- Implementing resource-consumption monitoring or metrics. The constitution names this as
  a general Hub/observability obligation, but it is a separate concern from this
  feature's containment guarantee and is not gated by this feature's Definition of Done.
- Containerizing or otherwise re-architecting how agent processes are hosted; this
  feature strengthens containment within the current execution model. A future ADR may
  still choose containerization for resource isolation — nothing here precludes or
  requires it.
- Network egress restrictions beyond what credential scoping already provides.
- Rewriting the existing guarded-tool-boundary mechanism; this feature hardens its edge
  cases and structurally pins its guarantees, not a redesign.

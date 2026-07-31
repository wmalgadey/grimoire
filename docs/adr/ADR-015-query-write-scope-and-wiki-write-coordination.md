---
status: accepted
---

# ADR-015: Query Agent Write Scope and Cross-Process Wiki Write Coordination

## Context and Problem Statement

Feature 008 gave the Query agent read-only wiki access and ADR-011 made that guarantee
structural: `Grimoire.QueryAgent` has no `write_file` tool at all, and containment rule
C7 asserts no reachable filesystem-write API anywhere in its assembly. ADR-013
(feature 010) restated the platform's durable capability guarantee — "an agent's
effective capabilities are exactly its profile's declared tool registry, enforced at
the guarded tool boundary at invocation time" — and explicitly flagged that this
feature would need to change the Query profile, its policy, and that structural rule,
without touching the platform packaging itself.

Feature 012 (`specs/012-query-synthesis-writes/spec.md`) requires the Query agent to
preserve genuinely new insights as **Synthesis Pages**: it must create new wiki pages
and append to `index.md`/`log.md`, through the same guarded tool boundary every agent
uses, and must never be able to modify an existing content page (FR-001–FR-008). Its
Assumptions section requires this ADR to also settle a **write-coordination mechanism**,
because the premise that justified concurrent Query processes (ADR-011: "queries never
occupy or wait for the ingest slot," enforced by a reject-over-limit counting semaphore
with no cross-writer coordination) was that queries never wrote. Once Query writes, up
to three concurrent `Grimoire.QueryAgent` processes (`QueryRunCoordinator`, limit 3) and
one concurrent `Grimoire.IngestAgent` process (`IngestRunCoordinator`, single slot) can
all call `write_file` against the same `index.md`/`log.md` from separate OS processes,
and (per FR-014 of the sibling feature 013 spec) a future Lint agent will update
existing page frontmatter under the same exposure. **No existing mechanism protects
against this**: Ingest's "single writer" guarantee today is pure process-count
discipline (one Ingest process at a time), not a file lock; `GuardedToolExecutor`'s
temp-file-plus-rename write is atomic only per individual file, and `WriteJournal` is
in-memory and per-run. A second concurrent writer touching the same target was
structurally impossible before this feature and becomes possible the moment Query's
tool registry gains `write_file`. This is exactly a "new boundary" under Constitution
Principle I: local-filesystem coordination is persistence-exempt (no port required) but
still containment-bound, and per Principle I "introducing a dependency on a new
external system requires an ADR" — a cross-process coordination primitive is new
structural surface no existing ADR covers.

This ADR is written jointly with feature 013 (lint-agent) in mind: 013's spec pins its
own frontmatter-update integrity outcome (FR-014/SC-004) to "the writer-coordination
decision shared with feature 012" rather than drafting a second, possibly conflicting
mechanism. The coordination design below is deliberately agent-agnostic so 013 can
adopt it by reference, with no changes to this ADR.

## Decision Drivers

- FR-001/FR-003: Query must create Synthesis Pages and maintain `index.md`/`log.md`
  through the existing guarded tool boundary — no new tool-dispatch chokepoint.
- FR-002/FR-008 (Constitution Principle V): the Synthesis Decision and page content stay
  agentic; this ADR may add harness *mechanics* only (lock acquisition, existence and
  hash checks), never content judgment.
- FR-005/FR-006: Query's Write Scope must structurally exclude modifying existing
  content pages, and no wiki content can widen it — the guardrail, not agent
  self-restraint, is the enforcement point.
- FR-009/SC-003: concurrent writers (Ingest + Query, later Lint) must never corrupt
  `index.md`, `log.md`, or any page — explicitly including **lost updates**, not only
  byte-level interleaving.
- FR-010/SC-003: write coordination must not degrade Query's established streaming and
  interruption responsiveness (ADR-011).
- FR-011: interruption must not roll back completed writes — any new mechanism must not
  add rollback semantics beyond the existing per-run `WriteJournal`.
- ADR-006: the guarded tool boundary is the single physical chokepoint every agent write
  passes through; a coordination mechanism belongs there, not duplicated per agent.
  ADR-013: capability changes must be expressible as a profile/policy/registry change,
  not a platform change. ADR-009: any new runtime location goes through the single path
  composition point. ADR-003: coordination bookkeeping is operational state, outside
  `wiki/` and git. Constitution Principle I: persistence/local-filesystem mechanisms are
  port-exempt but containment-bound.
- Shared with feature 013: the mechanism must protect arbitrary existing-page updates
  (Lint's frontmatter refresh), not only Query's narrower create-only case.

## Considered Options

1. **Per-target cross-process lock + read-hash compare-and-swap inside
   `GuardedToolExecutor`, applied uniformly to every guarded write**, plus a
   create-only rule mode in the policy schema for Query's page-creation scope.
2. Route all wiki writes through the Hub (a single in-process writer), with agents
   sending write *requests* instead of writing their own working tree.
3. Reduce Query's concurrency limit to 1 and serialize Ingest/Query at the Hub
   dispatch level (no file-level coordination).
4. Adopt a full transactional store (SQLite or an embedded document store) for wiki
   content instead of plain files.

## Decision Outcome

Chosen option: **Option 1.**

### Write Scope: Query's guarded write capability

- `Grimoire.QueryAgent`'s tool registry (`QueryToolRegistry`) is extended to register
  `ToolRegistry.WriteFileDefinition` alongside the existing `list_files`/`read_file`
  tools. This is the only registry change; `GuardedToolExecutor` dispatch logic is
  unchanged (it already special-cases "a tool name not in the run's registry is
  rejected as unknown," so today's structural argument for "Query cannot write" moves,
  unchanged in mechanism, to "Query's registry now legitimately includes one write
  tool, scoped by policy").
- `data/agents/query/policy.json` gains write rules with a new optional per-rule
  `"mode"` field (default `"read-write"` when absent, preserving Ingest's existing
  policy file unchanged):
  ```json
  {
    "version": 2,
    "defaultDecision": "deny",
    "read": [ { "pathPrefix": "pages/" }, { "pathPrefix": "index.md" }, { "pathPrefix": "log.md" } ],
    "write": [
      { "pathPrefix": "pages/", "mode": "create-only" },
      { "pathPrefix": "index.md" },
      { "pathPrefix": "log.md" }
    ]
  }
  ```
  (As of 014-wiki-storage-restructure/ADR-017: the `pages/` wrapper is retired —
  `pathPrefix` values become `.`, reordered after the `index.md`/`log.md` exact-match
  entries, with an `excludePrefixes: ["index.md", "log.md"]` guard on the catch-all
  write rule. See ADR-017 for the full mechanism; the CAS/lock/mode machinery this ADR
  defines is otherwise unchanged.)
  `pages/` is `create-only`: `GuardedToolExecutor.ExecuteWriteFileAsync` denies (reason
  `create_only_target_exists`) any write whose canonical target already exists on disk —
  this is the structural form of "creates new Synthesis Pages, never modifies existing
  content pages" (FR-005); it needs no knowledge of what a Synthesis Page is, only
  whether the target pre-exists. `index.md`/`log.md` keep the default `read-write` mode
  because Query legitimately updates them in place, protected instead by the
  compare-and-swap guard below.
- `Grimoire.Domain.Guardrails.SafetyPolicy`'s write-rule representation gains the mode
  alongside each prefix; `PolicyDecision.Allow()` gains an `IsCreateOnly` flag so
  `GuardedToolExecutor` can apply the existence check without `SafetyPolicy` doing any
  I/O (it stays pure/dependency-free, matching its existing contract). `PolicyLoader`'s
  `PolicyRuleSchema` gains an optional `Mode` property; an unrecognized mode value is a
  fail-closed load error, not a silent default.
- `data/agents/query/system-prompt.md` is rewritten (implementation, not this ADR) to
  describe the new capability and its limits per FR-002/FR-004/FR-008; this is the
  instruction-file surface, not backend logic.

### Write coordination: `SharedFileWriteGuard`

- New type `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard`, owned
  by (constructed alongside) each run's `GuardedToolExecutor` — one instance per agent
  process run, matching `WriteJournal`'s existing per-run lifecycle. It has two
  responsibilities, both pure harness mechanics:
  1. **Read tracking**: on every successful `read_file`, records the SHA-256 of the
     content just read, keyed by canonical path, in an in-memory per-run map.
  2. **Guarded write**: before `ExecuteWriteFileAsync` performs its existing
     journal-then-atomic-rename sequence, `SharedFileWriteGuard.AcquireAsync(canonicalPath, cancellationToken)`
     takes an OS-level exclusive lock scoped to that single path (see below), then,
     while holding it: if the policy rule is `create-only`, denies when the file
     already exists (`create_only_target_exists`); otherwise, if the file exists on
     disk, compares its current SHA-256 against the run's last-recorded read-hash for
     that path (or, if this run never read it, against the run's own just-completed
     write, so a run's second write to a path it created itself always succeeds) — a
     mismatch denies with reason `write_conflict_stale_read` instead of writing,
     returned to the agent as a normal tool error so its own loop can re-read and
     retry. On success the write proceeds exactly as today (journal, temp file,
     atomic rename) *while the lock is held*, the guard's read-hash for that path is
     updated to the new content's hash, and the lock is released in a `finally` block
     regardless of outcome — including on run interruption, so a hung or killed run
     can never leave a target permanently locked beyond the lock's own liveness
     handling (below).
  - **Why compare-and-swap and not a lock held across the read/write pair**: holding a
    lock across an agent's entire read-then-reason-then-write cycle would tie lock
    duration to LLM latency (seconds to tens of seconds) and directly threaten FR-010.
    Compare-and-swap needs the lock only for the duration of the existence/hash check
    plus the atomic write itself (single-digit milliseconds), so contention — already
    rare, since it requires two writers targeting the *same* file inside the same brief
    window — costs at most one bounded retry wait, never a multi-second stall. A
    rejected write is not data loss: nothing was applied, the denial is recorded with a
    reason (matching the existing `DeniedActionRecord` shape, extended with these two
    new reason strings), and the run continues — consistent with the spec's existing
    "the insight is simply not preserved" outcome for policy denials.
- **Lock implementation**: an exclusive OS file lock (`FileStream` opened with
  `FileShare.None`) on a lock file per canonical target, named by the SHA-256 of the
  target's canonical absolute path, under a new resolved location
  `ResolvedGrimoirePaths.WriteLocksDir` (`GrimoirePathOptions.DefaultWriteLocksDirName =
  "write-locks"`, resolved beneath `DataDir` via the ADR-009 composition point — outside
  `wiki/` and git, per ADR-003 operational-state placement). The agent process receives
  this directory the same way it receives `--wiki-root` today (a new CLI argument on
  both `Grimoire.IngestAgent` and `Grimoire.QueryAgent`, since the guard runs inside the
  spawned agent's own process, not the Hub, per ADR-002's contract that agents write
  their own working tree directly). Acquisition polls with bounded backoff (default
  cap 5s, configurable); exceeding the cap denies with reason
  `write_coordination_timeout` rather than blocking indefinitely — this bounds the
  worst case for FR-010 even under pathological contention or a crashed holder (an OS
  file lock is released automatically by the kernel when the holding process exits or
  is killed, so a crashed run cannot wedge the lock permanently).
- **Scope of protection**: because the guard is inside `GuardedToolExecutor`, which
  every agent's tool loop already routes through (ADR-006), it protects Ingest, Query,
  and (feature 013) Lint uniformly with no per-agent special-casing — satisfying the
  "writer-coordination decision shared with feature 012" requirement in the 013 spec by
  construction; feature 013's plan references this ADR and introduces no coordination
  logic of its own, only its own policy rules and profile wiring.

### Structural enforcement (Constitution III)

- `QueryAgentGuardedWriteBoundaryRuleTests` (currently: zero reachable filesystem-write
  APIs anywhere in `Grimoire.QueryAgent`) is rewritten to the allow-listed-namespace
  shape already used by `IngestAgentGuardedWriteBoundaryRuleTests`: reachable writes are
  permitted only from `Grimoire.AgentRuntime.Guardrails` (now including
  `Grimoire.AgentRuntime.Guardrails.Coordination`) — every other type in
  `Grimoire.QueryAgent` must still show zero reachable write calls, proven with a
  Red/Green probe (a scratch `File.WriteAllText` call added outside the allow-list must
  turn the rule red, naming that call site, before being removed).
- New containment rule: types in `Grimoire.AgentRuntime.Guardrails.Coordination` are
  constructed only from `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor` (or its
  own namespace) — proven with its own Red/Green probe, following the ADR-010/ADR-013
  namespace-containment idiom. No port is introduced: per Principle I this is a
  persistence/local-filesystem mechanism, exempt from the port requirement but bound by
  this containment rule.
- `QueryRunCoordinator`'s bounded-concurrency dispatch (limit 3, reject-over-limit, no
  queue) is **unchanged** — this ADR deliberately keeps write coordination at the file
  level so the Hub-level concurrency model established by ADR-011 needs no revision.

### Turn record: reporting created pages

- `Grimoire.AgentRuntime.RunEvents`' `completed` event gains an optional
  `createdArtifacts` list, populated mechanically from `GuardedToolExecutor.TouchedPaths`
  filtered to the run's create-only write prefix(es) — the harness reports what already
  happened in its own journal, no content judgment. The Hub's Conversation Record
  bookkeeping block (ADR-014) consumes this into the forward-compatible, already-open
  `created_pages:` key it reserved for this feature — no record-format restructuring.

### Supersession scope

This ADR **supersedes only** the "Query is structurally write-free" framing of ADR-011
(its C7 containment rule and the "no `write_file` tool at all" statement) and narrows
ADR-013's forward note that this feature "changes the Query profile, policy, and that
structural rule via its own ADR" into the concrete design above. **Everything else in
ADR-011 and ADR-013 remains in force**: the shared `Grimoire.AgentRuntime` library and
its `AgentProfile`/`AgentHost` composition model, token streaming, Hub-side bounded
concurrency and its reject-over-limit semantics, interruption vs. liveness-failure
handling, and all other containment rules (C1–C6, C8, D1, D2, N1). ADR-006's guarded
tool boundary and rollback-via-`WriteJournal` are extended (a coordination step added
inside the existing write path), not superseded.

### Consequences

- Good, because the coordination mechanism lives once, at the single existing
  chokepoint every agent's writes already pass through — Ingest, Query, and Lint (013)
  share it with zero duplication and no per-agent special-casing.
- Good, because compare-and-swap plus a millisecond-scale lock protects against lost
  updates without tying lock duration to LLM reasoning latency, keeping FR-010 intact.
- Good, because the create-only policy mode is a pure existence check — it needs no
  concept of "Synthesis Page," keeping page-type judgment entirely in instruction files
  (Principle V).
- Good, because a crashed or killed run cannot wedge a lock: OS file locks release on
  process exit, and the bounded-backoff cap gives a deterministic worst case even before
  that.
- Bad, because agents must now handle a `write_conflict_stale_read` tool error by
  re-reading and retrying; accepted because this is a normal, already-idiomatic
  tool-loop error-recovery pattern (identical in shape to a policy denial) and genuine
  same-file contention is rare by construction (bounded Ingest/Query concurrency,
  narrow Write Scope).
- Neutral, because this introduces one new operational directory
  (`ResolvedGrimoirePaths.WriteLocksDir`) and one new CLI argument on both agent
  executables; both follow existing ADR-009 conventions exactly.

## More Information

Detailed rationale: `specs/012-query-synthesis-writes/research.md`. Contracts:
`specs/012-query-synthesis-writes/contracts/`. Sibling feature 013
(`specs/013-lint-agent/`) adopts this ADR's coordination mechanism by reference for its
own frontmatter updates; it introduces no coordination logic of its own. Per
Constitution Principle III this ADR MUST reach **Accepted** (project-owner sign-off)
before `/speckit-tasks` runs for feature 012 (and before feature 013's plan can rely on
it as an existing, accepted ADR); it is deliberately left `proposed` by this planning
run.

> **Extended by [ADR-016](ADR-016-lint-write-scope-frontmatter-only-enforcement.md)
> (013-lint-agent):** adds a third write-scope mode, `frontmatter-only`, alongside
> `read-write` and `create-only` above — Lint's narrower structural guarantee (may
> change a page's frontmatter, never its body). The cross-process lock and
> compare-and-swap mechanism described here are reused entirely unchanged; only the
> `WriteRule`/`PolicyDecision` mode enumeration gains a third case.

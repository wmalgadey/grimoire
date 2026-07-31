---
status: accepted
---

# ADR-016: Lint Write Scope — Structural Frontmatter-Only Enforcement

## Context and Problem Statement

Feature 013 (`specs/013-lint-agent/spec.md`) gives the Lint agent exactly one write
action: refreshing inbound-link counts (and, per its Findings Report's proposals,
eventually other metadata) in the **frontmatter** of existing wiki pages — never their
body content, never page creation or deletion. FR-010 states this limitation must be
enforced "via the guarded tool boundary's versioned deny-by-default policy," and
User Story 3 (Acceptance Scenario 2) requires a "page content edit" attempt to be
"denied and recorded with a reason" at the policy level — SC-002 lists this
explicitly among the spec's **deterministic harness guarantees (100%)**, not an
agent-judgment threshold. This is a real structural requirement, not aspirational
phrasing: per Constitution Principle II, a 100%-guarantee success criterion must be
backed by an enforceable mechanism, and per the spec's own framing, downgrading it to
"the agent is instructed not to" (an evaluation-sampled guarantee) would silently
weaken a criterion the spec deliberately placed in the deterministic tier.

ADR-015 (`012-query-synthesis-writes`) gave the guarded tool boundary two write-scope
modes — `read-write` (compare-and-swap protected) and `create-only` (existence-checked)
— and anticipated that Lint would reuse its cross-process coordination mechanism "by
reference... introduces no coordination logic of its own." That anticipation holds for
the *locking* half of the mechanism (unchanged, reused as-is), but neither existing mode
can express "this write may change the frontmatter block but the body must stay
byte-identical" — `read-write` permits any content change to an existing file,
`create-only` forbids writing an existing file at all. This is a third, genuinely new
write-scope shape no existing ADR covers, and per Constitution Principle I ("new
boundaries via ADR"), it needs one before implementation.

## Decision Drivers

- FR-010/FR-011/SC-002: the frontmatter-only limitation and instruction-injection
  resistance MUST be structural (guarded-tool-boundary-enforced), not agent
  self-restraint — Constitution Principle II's success-criteria split forbids treating
  a stated 100% deterministic guarantee as an evaluation threshold instead.
  Constitution Principle V: the check must be purely mechanical (does the body differ),
  never a judgment about whether a *specific* frontmatter change is a good one — that
  judgment stays in `agents/lint/system-prompt.md`.
  Constitution Principle II — "no test in this feature requires a live LLM call except
  the one-time recorded-eval capture" — the frontmatter/body split must be checkable with
  a pure string operation, not a model call.
- ADR-015: the cross-process lock and compare-and-swap mechanism must be reused
  unchanged for concurrency-integrity (FR-014/SC-004 of feature 013's own spec,
  "the writer-coordination decision shared with feature 012"); this ADR must not
  duplicate or bypass it.
- ADR-006: the guarded tool boundary is the single physical chokepoint every write
  passes through; the new check belongs there, not in a second boundary.
- Minimal surface: extend the existing `WriteRule`/`PolicyDecision` shape (already a
  discriminated mode, not yet three-way) rather than inventing a parallel policy
  concept.

## Considered Options

1. **A third `WriteRule` mode, `frontmatter-only`**: `GuardedToolExecutor` denies the
   write unless the target already exists and the proposed new content's body (the
   markdown after the closing `---` frontmatter delimiter) is byte-identical to the
   current on-disk body; composed with the existing compare-and-swap check for
   concurrency-integrity, not a replacement for it.
2. Leave policy at plain `read-write` on `pages/`; treat frontmatter preservation as an
   instruction-file (agentic) guarantee only, verified by evaluation sampling.
3. A dedicated `update_frontmatter` tool taking structured key/value pairs instead of
   whole-file `write_file`, with the harness re-serializing the page.
4. Content-addressed diff/patch tool generalizing beyond frontmatter (e.g. arbitrary
   unified diffs), enforced by the harness.

## Decision Outcome

Chosen option: **Option 1.**

### `frontmatter-only` write mode

- `Grimoire.Domain.Guardrails` gains `WriteMode { ReadWrite, CreateOnly, FrontmatterOnly }`,
  replacing `WriteRule`'s and `PolicyDecision`'s boolean `CreateOnly`/`IsCreateOnly` with
  this enum (`WriteRule(string Prefix, WriteMode Mode)`,
  `PolicyDecision.Allow(WriteMode mode = WriteMode.ReadWrite)`). Existing `read-write`/
  `create-only` behavior for Ingest/Query is unchanged — this is an additive third case,
  not a reinterpretation of the first two. `PolicyLoader`'s `mode` string parsing gains
  `"frontmatter-only"` as a third recognized value; any other value remains a fail-closed
  load error.
- `data/agents/lint/policy.json` declares its one write rule as
  `{ "pathPrefix": "pages/", "mode": "frontmatter-only" }` (as of
  014-wiki-storage-restructure/ADR-017: `{ "pathPrefix": ".", "mode": "frontmatter-only",
  "excludePrefixes": ["index.md", "log.md"] }` — the `pages/` wrapper is retired and the
  rule now needs an explicit exclusion so its directory-style catch-all never matches
  the two reserved files).
- `Grimoire.AgentRuntime.Guardrails.Coordination.SharedFileWriteGuard.EvaluateWriteAsync`
  gains the proposed new content as a parameter (needed only for this mode; `read-write`/
  `create-only` continue to ignore it) and, for `FrontmatterOnly`:
  1. Deny `frontmatter_only_target_missing` if the canonical target does not exist —
     Lint never creates pages, so a frontmatter-only write always targets something
     that already exists.
  2. Otherwise apply the **existing** compare-and-swap check unchanged (deny
     `write_conflict_stale_read` on a stale read, exactly as `read-write` does) — this
     mode composes with, not replaces, ADR-015's concurrency protection.
  3. Split both the current on-disk content and the proposed new content at their
     closing `---` frontmatter delimiter (first `---` line = open, next `---` line =
     close; everything after is body). Deny `frontmatter_only_malformed_document` if
     either content lacks a well-formed two-delimiter frontmatter block (fail closed —
     a document the check cannot parse is never assumed safe). Deny
     `frontmatter_only_body_changed` if the two bodies are not byte-identical.
  4. Otherwise allow — the frontmatter block itself is unconstrained (Lint may add,
     remove, or change any frontmatter key; only the body is protected).
  - This is a pure string operation over content already resident in memory (the
    current bytes read for the CAS check; the proposed content already held by
    `GuardedToolExecutor`) — no additional I/O, no model call, no judgment about
    whether a given frontmatter change is *correct*, only whether the body changed at
    all.
- `GuardedToolExecutor.ExecuteWriteFileAsync` passes `policyResult.Mode` (replacing its
  `IsCreateOnly` bool) and the call's `content` into `EvaluateWriteAsync`; its
  post-write create-only bookkeeping (`_createdPaths`/`RecordCreateOnlyWriteSucceeded`)
  is unchanged, keyed off `Mode == WriteMode.CreateOnly` instead of a bool.
- New `DeniedActionRecord` reasons: `frontmatter_only_target_missing`,
  `frontmatter_only_malformed_document`, `frontmatter_only_body_changed` — surfaced
  identically to ADR-015's existing denial reasons (recorded, `is_error` tool result,
  run continues).

### Structural enforcement (Constitution III)

- Extends the existing Red/Green-probed containment discipline: no new namespace is
  introduced (the check lives inside `SharedFileWriteGuard`, already confined to
  `Grimoire.AgentRuntime.Guardrails.Coordination` and containment-tested by
  `GuardrailsCoordinationContainmentRuleTests`), so no new containment rule is required
  — only a new `LintAgentGuardedWriteBoundaryRuleTests` (allow-listed-namespace shape,
  mirroring Query's and Ingest's) proving Lint's assembly reaches the filesystem-write
  API only through `Grimoire.AgentRuntime.Guardrails`, plus deterministic tests proving
  a body-changing write is denied and a frontmatter-only write succeeds, each with a
  Red/Green probe per the constitution's Phase 0 requirement.

### Relationship to ADR-015

This ADR **extends** ADR-015; it does not supersede any part of it. The cross-process
lock, the compare-and-swap concurrency check, `CrossProcessFileLock`, and the
`create-only`/`read-write` modes are reused entirely unchanged — Lint composes with
them, adding one more mode value and one more content-level check layered on top of the
existing existence/CAS checks. ADR-015's "More Information" anticipation that feature
013 "introduces no coordination logic of its own" holds for the *coordination*
(locking/CAS) mechanism; it did not anticipate that Lint's own *write-scope shape*
would need a new mode, which this ADR supplies.

### Consequences

- Good, because the frontmatter/body split is a pure, mechanical, deterministic check —
  exactly the category of harness mechanic Principle V permits, with no risk of
  reimplementing wiki-content judgment in backend code.
- Good, because it composes with, rather than duplicates or bypasses, ADR-015's
  concurrency protection — a frontmatter-only write is exactly as safe against lost
  updates as a plain read-write one.
- Good, because Ingest's and Query's existing policy files and behavior are completely
  unaffected — this is a purely additive third mode.
- Bad, because the frontmatter/body boundary is defined lexically (first two `---`
  lines) rather than via a real YAML parse; accepted because every page in this wiki is
  already required to open with exactly that shape (per `agents/ingest/system-prompt.md`'s
  frontmatter convention), and a lexical check is simpler, faster, and has no YAML-library
  dependency to add to a dependency-light domain/guardrails layer.
- Neutral, because `WriteRule`/`PolicyDecision`'s boolean `CreateOnly`/`IsCreateOnly`
  becomes a three-case enum; both existing call sites (Ingest, Query) are updated
  mechanically with no behavior change.

## More Information

Detailed rationale: `specs/013-lint-agent/research.md`. Contracts:
`specs/013-lint-agent/contracts/`. Per Constitution Principle III this ADR MUST reach
**Accepted** (project-owner sign-off) before `/speckit-tasks` runs for feature 013; it
is deliberately left `proposed` by this planning run.

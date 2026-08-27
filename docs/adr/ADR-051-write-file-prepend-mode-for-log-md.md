---
status: accepted
supersedes: null
superseded_by: []
---

# ADR-051: A `write_file` Prepend Mode for Cheap `log.md` Writes

**Extends [ADR-035](ADR-035-agent-exclusive-activity-log-authorship.md)** — no supersession,
and ADR-035's status does not change. ADR-035 decided *who* may author activity-log content
(the agents, exclusively) and explicitly left the log's prepend-ordering/format shape as
"feature-scoped format content" owned by a contract document, not an architectural decision
(ADR-035 "More Information"). This ADR decides a second, narrower aspect within the same
guarded tool boundary: *how cheaply* an agent-authored entry reaches disk. It does not touch
who may write, what an entry must look like, or any existing write path's behavior when the
new capability is not used. Per the Invalidation test (Constitution Principle III), using
this capability is a use of what the guarded tool boundary already permits, not a reversal
of it — an extension.

## Context and Problem Statement

`log.md`'s prepend-only ordering (a feature-scoped format contract, not this ADR's concern —
see `specs/025-agent-owned-log/contracts/activity-log-write-contract.md`) requires a
proposed write to end with the file's current content byte-for-byte. The only write
primitive the guarded tool surface offers is whole-file `write_file` — so satisfying that
contract requires the agent to reproduce the entire existing file inside its own write call.
The cost of adding one entry is therefore O(file size), on a file that grows with every run
and is never truncated.

On the self-hosted deployment that cost has already exceeded what an agent is allowed to
produce in one response: `log.md` is 128,576 bytes / 1,950 lines / ~35,000 tokens; the
default output-token budget is 8,192. Log writes now fail deterministically — not
occasionally, not under adversarial input, but as the ordinary, expected outcome of the file
having grown (GitHub issue #201). The evidence is entirely from Ingest (two runs denied
`log_entry_not_prepended`, one run whose completion narrative falsely claimed a log write it
never attempted), but the mechanism is shared: Lint's own instructions
(`agents/lint/system-prompt.md`, "Reconciling `index.md` and `log.md`") read and write the
same file the same way, and Query writes it too. A stopgap
(`GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS=64000`) buys headroom against today's file size but not
against the file continuing to grow — the ceiling is now the model's maximum output, with
nowhere further to raise it.

This ADR settles what write primitive the guarded tool surface offers instead of "reproduce
the whole file," and how it stays safe under concurrent writers without reintroducing the
cost it removes. It does not re-decide the format contract itself (heading pattern, paragraph
requirement, `index.md`'s separate catalog check) — that stays exactly as
`specs/025-agent-owned-log/contracts/activity-log-write-contract.md` and its successor
`specs/028-lint-at-scale/contracts/log-prepend-write.md` define it; this ADR only supplies
the tool-level mechanism those contracts' rules now also apply to.

## Decision Drivers

- **This is a guarded tool *capability* decision, not a content-format decision** — the
  precedent for treating this class of change as ADR material, not feature-scoped format
  content, is ADR-030 (adding `search_files`, ranged `read_file` parameters, and read-only
  `batch` to the guarded tool surface) and ADR-016 (introducing the `FrontmatterOnly` write
  mode) — both genuine ADRs, neither deprecated by the v2.0.0 restructuring that retired
  ADR-017's purely format-level content.
- **Constitution Principle V, agentic core** — what an entry says remains the agent's
  judgment under instruction files; the harness may gain a cheaper way to *commit* an
  agent-authored entry, never authorship of the entry itself (spec 028 FR-014; ADR-035's own
  authorship decision, unchanged by this one).
- **The activity-log format contract is a 100% deterministic guarantee** — whatever this ADR
  adds must not weaken prepend ordering, the heading-pattern check, or the non-empty-paragraph
  check for any write path, old or new. Those checks' *content* stays contract-governed; this
  ADR only decides that a new call shape can reach them.
- **Concurrent-write safety must hold without the cost the fix removes.** A design that
  requires a prior whole-file read to establish a staleness baseline reintroduces exactly the
  O(file-size) cost this ADR exists to eliminate.
- **Minimal surface.** No new tool, no new port, no new external system. `write_file` has
  handled every prior extension (`FrontmatterOnly` policy mode, `WriteMode.CreateOnly`) as an
  addition to its existing dispatch, not a parallel mechanism.
- **Spec 028's clarification** (session 2026-08-25) already settled two shape questions: the
  capability is available to Ingest, Query, and Lint alike (not Lint-only — issue #201's own
  evidence is from Ingest), and it takes the shape of a `write_file` call parameter rather
  than a distinct tool.
- **ADR-028's own precedent (before its supersession) already rejected a structured
  `prepend_log_entry` tool** ("Considered Options → Ordering, option 3") because "the harness
  would own the file's assembly." This ADR does not revive that option: the agent still
  composes the entry's full text (heading and body) exactly as today; the harness only
  concatenates two strings it is handed (`entry + currentContent`) instead of requiring the
  agent to also retransmit `currentContent`.

## Considered Options

1. **A `mode` parameter on `write_file`** (`"replace"` default, `"prepend"` new): when
   `mode: "prepend"`, `content` is reinterpreted as the entry only; the harness reads
   `log.md`'s current content itself, under the write lock, and commits
   `entry + currentContent` atomically.
2. A policy-level `WriteMode.Prepend` value on `Grimoire.Domain.Guardrails.WriteMode`, making
   `log.md`'s `WriteRule` itself prepend-only — forbidding a full-content write to `log.md`
   at the policy layer entirely.
3. A distinct new tool (e.g. `prepend_log_entry`) — the shape ADR-028 already considered and
   rejected for the identical reason, before its supersession.
4. Require prepend callers to supply a prior-read content hash (mirroring `ReadWrite` mode's
   compare-and-swap), so a "stale" prepend can still be denied symmetrically with
   full-content writes.

## Decision Outcome

Chosen option: **1 — a `mode` parameter on `write_file`, harness-read-under-lock, no
compare-and-swap baseline for prepend writes.**

This `mode` parameter is a schema-level `write_file` call argument, not a new value on the
policy-level `Grimoire.Domain.Guardrails.WriteMode` enum — the two are orthogonal, and this
ADR does not touch the latter. It also does not touch ADR-030, which is scoped entirely to
retrieval (`search_files`, ranged `read_file`, read-only `batch`) and never touches
`write_file`; nor ADR-011's successors, since all three per-agent tool registries already
declare the identical shared `WriteFileDefinition` constant, so widening its schema reaches
Ingest, Query, and Lint simultaneously with no registry-file change and no new
registry-scope decision to record (unlike ADR-030 R6, which deliberately scoped three
genuinely new tools to Lint only).

### R1 — Schema addition (Boundary Rule: guarded tool-surface capability)

`ToolRegistry.WriteFileDefinition`'s JSON schema gains an optional `mode` string property
(`enum: ["replace", "prepend"]`, default `"replace"`) alongside the existing `path`/
`content`. Because the schema currently declares `additionalProperties: false`
(`ToolRegistry.cs:267`), `mode` must be added to the schema itself — the executor cannot
accept it permissively. Omitting `mode` is unchanged, existing behavior (`content` is the
full file). `required` stays `["path", "content"]`. This is one shared constant referenced
identically by `LintToolRegistry`, `IngestToolRegistry`, and `QueryToolRegistry` today, so no
registry file changes.

### R2 — Prepend dispatch: no baseline, read-under-lock (Boundary Rule: guarded write-safety mechanism)

`GuardedToolExecutor.ExecuteWriteFileAsync` forwards `mode` into
`SharedFileWriteGuard.EvaluateWriteAsync` alongside the existing `WriteMode` (the
policy-level enum, unchanged and orthogonal — see above). When `mode == "prepend"`:

1. The write-target's `CrossProcessFileLock` is acquired exactly as today (unchanged).
2. `log.md`'s current content is read fresh from disk **inside the lock**, not from any
   prior `OnReadFile` baseline. No compare-and-swap check runs for this write — there is no
   staleness scenario to compare against, because a prepend never asserted anything about
   the file's prior content.
3. The proposed full content is assembled as `entry + currentContent` and passed through the
   *same* format-validation and atomic-commit path every write already uses — validation
   content is the activity-log contract's concern (`specs/028-lint-at-scale/contracts/
   log-prepend-write.md`), not this ADR's; `OnWriteCommitted` still re-baselines the file for
   any subsequent write in the same run.

Two writers racing to prepend are simply serialized by the lock: each reads the latest
content in lock-acquisition order and prepends onto it. Nothing is lost, nothing is silently
overwritten. This is not a weaker guarantee than `ReadWrite` mode's CAS check — it is a
different, sufficient one, and it is why prepend mode needs no prior read at all. `ReadWrite`
mode's existing CAS/denial behavior for full-content writes (to `log.md` or anywhere else) is
entirely unchanged.

### Scope notes (not separately-tagged rules)

- **Format validation, `index.md` non-involvement, and instruction-file updates are feature
  content, not this ADR's decision.** Exactly which checks a prepend-mode entry must satisfy,
  that `index.md`'s catalog mechanism is unreachable from any prepend-mode code path, and how
  `agents/ingest`/`agents/query`/`agents/lint`'s system prompts change to use the new
  capability are all recorded in `specs/028-lint-at-scale/contracts/log-prepend-write.md` and
  `specs/028-lint-at-scale/data-model.md` — the same "feature-scoped format content lives in
  a contract, not an ADR" pattern ADR-035 itself already established when it deferred the
  ordering check. This ADR decides only that the capability exists at the guarded tool
  boundary (R1) and how it stays safe under concurrency (R2).
- **Instruction files remain agent judgment (Constitution Principle V).** No deterministic
  test asserts the wording of any instruction file; adopting `mode: "prepend"` in each
  agent's prompt is tracked as feature work, not an ADR rule.

### Rule Classification (Principle III)

| Rule | Category | Enforcement |
|---|---|---|
| R1 schema stays `additionalProperties: false`-compatible across all three registries | Boundary Rule | Phase 0 structural test (schema shape) + behavioral test (an unlisted field is rejected) |
| R2 a prepend write requires no prior read/baseline and performs no CAS check | Boundary Rule | Phase 0 structural test (no `OnReadFile` call reachable from the prepend path) + behavioral test (a prepend succeeds with no preceding read in the same run) |

### Consequences

- Good, because the cost of adding one log entry drops from O(file size) to O(entry size)
  for every agent that writes `log.md` — fixing the failure issue #201 reports as already
  occurring in production, not a projected one.
- Good, because no new tool, no new port, no new external system is introduced;
  `write_file`'s existing dispatch, lock, and format-validation machinery are extended, not
  duplicated.
- Good, because concurrent-write safety is *simpler* under this design than `ReadWrite`
  mode's, not weaker — there is no staleness window to protect against for a prepend, so
  there is nothing to detect or deny.
- Good, because keeping this ADR to just R1/R2 mirrors the same scope discipline the
  concurrent v2.0.0 restructuring applied project-wide — format/content rules live in
  contracts, guarded-tool-boundary capability changes live in ADRs.
- Bad, because `write_file`'s contract is no longer single-meaning — a `mode: "prepend"`
  call means something structurally different from the default — mitigated by following the
  precedent ranged `read_file` already set (one tool name, multiple call shapes via optional
  parameters) rather than proliferating tool names.
- Bad, because three instruction files (Ingest, Query, Lint) all need coordinated updates,
  mitigated by `mode` defaulting to `"replace"`: an agent whose instructions are not yet
  updated keeps using the old, expensive path, which still works until the file grows too
  large again — a rollout reality, not a correctness gap.
- Neutral, because the ADR does not use option 2 (a policy-level prepend-only
  `WriteRule.Mode` for `log.md`, forbidding full-content writes entirely). That option would
  be a *stronger* guarantee (an agent could never regress to the expensive path) at the cost
  of narrowing ADR-031's "full authority... in both modes" grant for `log.md` specifically,
  which no current requirement calls for.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a fourth agent type gaining `log.md` write
  access and using `mode: "prepend"` the same way; raising or lowering R1's documented
  defaults; an instruction-file rewrite that changes *when* an agent decides to log
  something, as long as it keeps calling `write_file` with `mode: "prepend"` to commit it;
  extending `mode` with further values for other files, as long as `log.md`'s existing
  `"prepend"`/`"replace"` behavior is unchanged; any change to the activity-log format
  contract's own rules (heading shape, paragraph requirement) that does not change R1/R2's
  mechanism.
- **Invalidations (would require full supersession):** adopting Considered Option 2 (making
  `log.md` prepend-only at the policy layer, forbidding `mode: "replace"` for it) would
  narrow this ADR's "both modes remain available" decision and would supersede it wholesale,
  not amend it in part; requiring a compare-and-swap baseline for prepend writes after all
  (reversing R2's core "no prior read needed" decision) would likewise invalidate this ADR,
  not extend it.

## More Information

Detailed rationale and code-level grounding: `specs/028-lint-at-scale/research.md` (R6-R12).
Contract (format validation, `index.md` non-involvement, instruction-file changes):
`specs/028-lint-at-scale/contracts/log-prepend-write.md`. Read alongside
[ADR-035](ADR-035-agent-exclusive-activity-log-authorship.md) (who may author activity-log
content — unaffected by this ADR) and
[ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) (the guarded tool-use loop and write
journal every `write_file` call, in any mode, passes through). Per Constitution Principle
III this ADR MUST reach **Accepted** before `/speckit-tasks` runs for feature 028.

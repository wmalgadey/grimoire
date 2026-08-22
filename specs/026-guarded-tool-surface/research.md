# Phase 0 Research: Guarded Tool and Policy Surface

**Feature**: 026-guarded-tool-surface | **Date**: 2026-08-22

The spec carried no `[NEEDS CLARIFICATION]` markers into planning — all five were resolved in
the `/speckit-clarify` session of 2026-08-22. What remained were four values the spec
deliberately deferred, plus three technical choices the ADRs needed grounding for.

## D1 — Regex engine

**Decision**: `RegexOptions.NonBacktracking` with an explicit `matchTimeout`.

**Rationale**: The threat is not a slow search, it is a pattern that never terminates. A
timeout alone turns that into a run that burns its whole budget before failing.
`NonBacktracking` (.NET 7+) guarantees time linear in input length regardless of pattern, so
the pathological case stops being possible rather than being caught late. The timeout stays as
a backstop for pathological *input* size.

**Cost, stated plainly**: `NonBacktracking` does not support lookaround, backreferences, or
atomic groups. An agent that writes `(?<!^)foo` gets a rejection. This is a real capability gap
against `grep -P`, mitigated only by the denial naming the reason (ADR-030 R2).

**Alternatives**: interpreted `Regex` + timeout (rejected: the timeout is checked between
backtracking steps, so a catastrophic pattern still consumes the budget); compiled `Regex`
(same problem, plus per-pattern JIT cost on agent-supplied input); literal substring only
(rejected in clarification — cannot express frontmatter-field absence, which is the highest
volume use).

## D2 — Search result cap: 200 matches (max 1000)

**Rationale**: A truncated result must still be useful *and* affordable. 200 `path:line:text`
triples is roughly 3–6k tokens — a few percent of the 200k guard — and is far more than enough
to see the shape of a match set and narrow. The point of search is to avoid reading the wiki,
so a cap that permits reading much of it back through match lines defeats the purpose.
`max_results` may lower the cap; the 1000 ceiling exists so it cannot be used to bypass it.

## D3 — Search time budget: 2 seconds

**Rationale**: A backstop, not a performance target. The reference wiki is ~4 MB of markdown;
a non-backtracking scan runs at well over 100 MB/s, so a full-root search is expected in the
low tens of milliseconds — roughly two orders of magnitude inside the budget. A search that
hits 2 s means something is wrong (a pathological input, a mounted path that is not the wiki),
and the agent should be told so rather than left waiting.

## D4 — Max batch size: 20 calls

**Rationale**: Sized for the motivating case — sweeping the frontmatter of a page set found by
one search — while keeping a single tool result bounded. 20 frontmatter reads is a plausible
tool result; 200 would be a context problem disguised as an optimization.

## D5 — Regex pattern size: 1000 characters

**Rationale**: Bounds compile cost on agent-supplied input. Chosen far above any legitimate
pattern so it never fires in normal use; it exists to make "the agent emitted something
enormous" a clean rejection rather than a memory event.

## D6 — Deletion is a separate policy scope, not part of `write`

**Decision**: The policy gains a `delete` scope, deny-by-default, declared only by Lint.

**Rationale**: This is the finding that most changed the design. Ingest's policy already
declares `read-write` on the content root. Had deletion been evaluated as a write, **Ingest
would have silently gained the ability to delete every page in the wiki** as a side effect of
a feature about Lint. No agent may acquire deletion by inheritance, so it needs its own scope.

**Alternatives**: a `WriteMode.Delete` (rejected: modes qualify *how* an existing path may be
written, not whether a different verb is permitted); a tool-registry-only restriction
(rejected: ADR-006 puts authorization in the policy, not in which tools are offered — the
registry is a second line, not the line).

## D7 — Ranged reads and the compare-and-swap baseline

**Decision**: only a full read calls `SharedFileWriteGuard.OnReadFile`.

**Rationale**: ADR-015 detects a stale-read conflict by comparing a write against the content
last read in the same run. A ranged read that fed the baseline would record a fragment as
"what this run saw", so a subsequent whole-file overwrite would compare clean and the conflict
would go undetected — a partial read would silently license a full overwrite. The rule is not
an optimization; it is what keeps ADR-015 sound once reads can be partial.

**Consequence**: an agent that ranged-reads a page and then writes it is refused, and must read
it fully first. That is the correct trade: the alternative is undetectable lost updates.

## D8 — `FrontmatterOnly` retained in the policy model

**Decision**: keep the enum value and its parser case; no shipped policy declares it.

**Rationale**: `PolicyLoader` fails closed on an unrecognized `mode` string. Removing the value
would turn any operator policy file still declaring it into a hard load error on upgrade —
converting a silent no-op into an outage. Retaining an unused vocabulary word is the cheaper
failure mode. ADR-031 R5 records that it is unused *by design* so a future reader does not
read its absence from policies as a bug.

## D9 — Baseline capture for SC-014

**Decision**: capture the pre-feature median content-tokens-read on the eval fixture **before**
implementation starts.

**Rationale**: SC-014 is a ≥ 50% reduction against a baseline. Measured after the fact, the
baseline is a reconstruction. This is a task ordering constraint, not a design decision, and it
belongs in the first implementation phase.

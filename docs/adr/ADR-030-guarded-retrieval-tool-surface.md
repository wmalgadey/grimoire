---
status: accepted
---

# ADR-030: Guarded Retrieval Tools — Search, Ranged Read, and Read-Only Batch

> **Amends [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md)**: ADR-006 fixed the tool
> surface at exactly three file-level tools (`list_files`, `read_file`, `write_file`). This
> ADR adds `search_files` and a read-only `batch`, and adds optional range parameters to
> `read_file`. ADR-006's guarded tool-use loop, its deny-by-default policy model, and its
> rule that content semantics stay in instruction files are unchanged — ADR-006 itself
> names "adding a tool" as the sanctioned form of backend extension, which is what this is.
>
> **Amends [ADR-011](ADR-011-query-agent-shared-runtime-and-concurrency-model.md)**: the
> per-agent `ToolRegistry` gains three definitions. R3/R11's rule — a tool name the
> registry does not declare is rejected as unknown even when a dispatch branch exists —
> is what keeps these additive for Ingest and Query, which do not declare them.

## Context and Problem Statement

Lint reads the wiki through `read_file`, which returns whole files, and has no way to search.
At 633 pages / ~400k tokens, answering "which pages mention X" costs more content than the
run's 200k context guard permits, so Lint cannot survey a wiki of the size it exists to
maintain (#108, #159). Two narrower costs ride along: checking one frontmatter field costs a
whole-page read, and checking twenty pages costs twenty model turns.

The obvious escape hatch — give the agent a shell — is precisely what the guarded boundary
exists to prevent (ADR-006). The question this ADR settles is what to add instead, and in
what shape.

Feature 026's spec answers the shape question with a rule stated during clarification: the
agent should have every capability a shell would give it, confined to this wiki, so the
guarded tools should imitate the shell tools it would otherwise reach for (spec FR-024–FR-026).

## Decision Drivers

- The 200k context guard is a hard ceiling; retrieval must narrow before reading (spec SC-011).
- Every new capability must pass through `GuardedToolExecutor` and the existing read policy —
  a search must never surface a path a `read_file` would have been denied (spec FR-003/SC-001).
- Principle V: what to search for stays agent judgment; the harness only decides permission.
- An LLM is already fluent in `grep`, `sed`, `head`, `ls`. A bespoke API must be taught in an
  instruction file and invites malformed calls; a familiar one does not.
- Principle I: no new external system, so no new port (the persistence/filesystem exemption
  applies). Adapter containment is unchanged.
- Unbounded work at the tool boundary is a denial-of-service on the run itself.

## Considered Options

1. **Shell-shaped guarded tools**: `search_files` (grep), range parameters on `read_file`
   (sed/head), a read-only `batch`.
2. Bespoke structured predicates (`field_absent`, `links_to`) over frontmatter as data.
3. A sandboxed shell (chroot/container) with the wiki mounted.
4. Pre-built index (inverted index / embedded search engine) queried by a tool.

## Decision Outcome

Chosen option: **1 — shell-shaped guarded tools.**

### R1 — `search_files` (Boundary Rule)

Signature mirrors `grep -rn`: `pattern` (required), `path` (optional prefix), `ignore_case`,
`max_results`. Returns `path:line:text` triples. Executed inside `GuardedToolExecutor`.

Every candidate path is evaluated against the run's **read** policy before the file is opened.
A path the policy denies is **omitted from results silently** — it is not reported as a denial,
because reporting it would itself disclose that the path exists. A denial is recorded only for
the search's own `path` argument when that root is out of scope. This is the rule that makes
search incapable of widening the read scope.

### R2 — Bounded regex (Boundary Rule)

The pattern is a .NET regular expression evaluated with `RegexOptions.NonBacktracking` and an
explicit match timeout. Non-backtracking makes catastrophic backtracking structurally
impossible rather than merely time-limited; its cost is that lookaround and backreferences are
unsupported. A pattern the engine rejects, or one exceeding the size bound, comes back as a
recorded denial naming the reason — never a hung run and never a silent empty result
(spec FR-007a).

### R3 — Ranged `read_file` (Feature-Scoped Invariant)

`read_file` gains optional `offset`/`limit` (line-based, as `sed -n 'X,Yp'`) and
`frontmatter_only`. Omitting all three returns the whole file exactly as today, so existing
instruction files are unaffected.

**A ranged read MUST NOT establish the compare-and-swap baseline.** `GuardedToolExecutor`
currently calls `_writeGuard.OnReadFile(path, content)` on every read; ADR-015's conflict
detection compares a later write against that recorded content. Feeding it a fragment would
make a stale-read conflict undetectable — a partial read would silently license a whole-file
overwrite. Only a full read may call `OnReadFile`.

### R4 — Read-only `batch` (Boundary Rule)

`batch` takes a list of calls and returns their results together. It accepts **only** read-only
tool names. A batch containing a write, a delete, or a nested batch is rejected in full before
any member executes. Each member is evaluated against the policy individually and each denial
is recorded individually — `batch` carries no authority of its own, only the sum of its
members'.

### R5 — Documented defaults

The spec requires each bound to exist and be observable but names no value. These are the
defaults, and they live in exactly one place in code:

| Bound | Default | Reasoning |
|---|---|---|
| Search result cap | 200 matches | A truncated result still fits comfortably in context; the agent narrows rather than reads |
| Search time budget | 2 seconds | A backstop, not a budget — a non-backtracking scan of ~4 MB is roughly two orders of magnitude under this |
| Max batch size | 20 calls | Covers a frontmatter sweep of a page set while keeping one tool result bounded |
| Regex pattern size | 1000 characters | Far beyond any legitimate pattern; bounds compile cost |

`max_results` may lower the cap per call, never raise it above 1000.

### R6 — Registry scope

The three definitions are added to `ToolRegistry` and declared by `LintToolRegistry` only.
Ingest and Query are unchanged and, by ADR-011 R3/R11, cannot reach them. Whether either
later declares them is that agent's own decision; this ADR does not make it.

### Consequences

- Good: Lint can survey a wiki it currently cannot, and #108's spec may assume these exist.
- Good: no new external system, no new port, no new infrastructure; the guarded boundary
  remains the single chokepoint.
- Good: instruction files need no explanation of a bespoke API.
- Bad: three more tools is three more surfaces to keep guarded; the batch rule in particular
  is a place where a future "just one write" would quietly break the boundary.
- Bad: non-backtracking regex will reject patterns an agent might reasonably write
  (lookaround). The denial names the reason, but it is a capability gap against real `grep`.
- Neutral: the caps are guesses informed by the current wiki's size. R5 is a
  Feature-Scoped Invariant, so changing a number is a one-file amendment, not a broken
  structural test.

## Rule Classification (Principle III)

| Rule | Category | Enforcement |
|---|---|---|
| R1 search cannot surface a path outside the read policy | Boundary Rule | Phase 0 structural test + behavioral test |
| R2 regex is non-backtracking and bounded | Boundary Rule | Phase 0 structural test (engine option) + behavioral test |
| R3 a ranged read never sets the CAS baseline | Feature-Scoped Invariant | Classicist integration test |
| R4 batch admits read-only calls only | Boundary Rule | Phase 0 structural test + behavioral test |
| R5 default values | Feature-Scoped Invariant | Classicist integration test asserting observable behavior |
| R6 registry declares tools per agent | Feature-Scoped Invariant | Classicist integration test |

# Phase 1 Data Model: Guarded Tool and Policy Surface

**Feature**: 026-guarded-tool-surface | **Date**: 2026-08-22

Types below are harness types. None of them models wiki content — that stays markdown on disk
and judgment in instruction files (Principle V).

## SafetyPolicy (`Grimoire.Domain.Guardrails`) — changed

The deny-by-default path-prefix policy gains a third scope alongside `read` and `write`.

| Field | Type | Notes |
|---|---|---|
| `Read` | `IReadOnlyList<ReadRule>` | Unchanged |
| `Write` | `IReadOnlyList<WriteRule>` | Unchanged shape; `WriteMode` unchanged (`ReadWrite`, `CreateOnly`, `FrontmatterOnly`) |
| `Delete` | `IReadOnlyList<DeleteRule>` | **New.** Absent ⇒ no deletion permitted. Deny-by-default, like the others |

**`DeleteRule(string PathPrefix, IReadOnlyList<string>? ExcludePrefixes)`** — new. Deliberately
has no mode: deletion has no variants.

**Validation**: evaluation canonicalizes the path first, exactly as read/write do, so traversal
and symlinks collapse before matching. No rule matches ⇒ denied with reason `no_rule`.

**Why a separate scope** — see research.md D6. Folding deletion into `write` would have given
Ingest wiki-wide deletion by inheritance.

## Lint policy artifact — changed

`Grimoire.LintAgent/Instructions/policy.json`, version bumped:

| Scope | Before | After |
|---|---|---|
| `read` | `.` plus explicit `index.md`, `log.md` | Unchanged |
| `write` | `.` mode `frontmatter-only`, excluding `index.md`, `log.md` | `.` mode `read-write`, **no exclusions** |
| `delete` | — (absent) | `.` |

Ingest and Query policies are **not** changed: neither declares `delete`, so neither can delete.

## SearchRequest / SearchMatch (`Grimoire.AgentRuntime.Guardrails`) — new

**`SearchRequest(string Pattern, string? Path, bool IgnoreCase, int? MaxResults)`**

| Field | Validation |
|---|---|
| `Pattern` | Required; ≤ 1000 chars; must compile under `NonBacktracking` |
| `Path` | Optional prefix; canonicalized and evaluated against the **read** scope |
| `IgnoreCase` | Default `false` |
| `MaxResults` | Default 200; clamped to 1000 |

**`SearchMatch(string Path, int LineNumber, string Line)`** — the returned unit. `Path` is
content-root-relative. 1-based `LineNumber`, matching `grep -n`.

**`SearchOutcome`** — `Completed`, `Truncated`, `TimedOut`, `Denied`, `PatternRejected`. Carried
into the `outcome` label on `wiki.search.invocations_total` and the span attribute.

## ReadRequest — changed

`read_file` gains three optional inputs. All absent ⇒ today's whole-file read, byte for byte.

| Field | Type | Notes |
|---|---|---|
| `Offset` | `int?` | 1-based first line, as `sed -n 'X,Yp'` |
| `Limit` | `int?` | Maximum lines returned |
| `FrontmatterOnly` | `bool?` | Returns the frontmatter block only |

**Invariant (ADR-030 R3)**: a request with any of the three set is a *partial* read and MUST
NOT call `SharedFileWriteGuard.OnReadFile`. `ReadShape` (`Full`, `Range`, `Frontmatter`) is the
recorded discriminator and the `shape` metric label.

## BatchRequest — new

**`BatchRequest(IReadOnlyList<BatchCall> Calls)`**, `BatchCall(string Tool, string InputJson)`.
`InputJson` is the serialized form of the `input` object each batch member carries on the
wire (contracts/guarded-tool-surface.md) — the schema's `input` is a JSON object, and
`InputJson` is that object serialized to a string for internal dispatch, not a second,
string-typed wire field.

| Rule | Behavior on violation |
|---|---|
| `Tool` ∈ {`list_files`, `read_file`, `search_files`} | Whole batch rejected, `reason=tool_not_allowed_in_batch` |
| No nested `batch` | Whole batch rejected, `reason=nested_batch` |
| `Calls.Count` ≤ 20 | Whole batch rejected, `reason=too_many_calls` |

`tool_not_allowed_in_batch` (not `write_in_batch`) because the rule is an allowlist, not a
write-specific check: it rejects `delete_file` and any other forbidden tool name exactly as
it rejects a write.

Rejection happens **before any member executes**. Each member that does run is policy-evaluated
and recorded individually.

## WriteJournal — changed

| Entry kind | Prior state captured | Rollback action |
|---|---|---|
| Create | (none — file absent) | Delete the created file |
| Overwrite | Previous content | Restore previous content |
| **Delete** (new) | **Deleted content** | **Recreate the file with its content** |

Reverse-order restoration is unchanged (ADR-006). The new kind exists so rollback has no action
it cannot undo (ADR-031 R4).

## State transitions

None. This feature adds no lifecycle state; the remediation task state machine is untouched
(ADR-018 amended in authority only, not in states).

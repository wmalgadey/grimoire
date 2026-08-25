# Phase 1 Data Model: Host Stability Guarantee for Agent Runs

**Feature**: `specs/027-host-stability/spec.md` | **Date**: 2026-08-25

This feature introduces no new persisted entity, database table, or task-artifact
schema — it hardens the decision logic and structural guarantees around two existing
harness mechanisms. The "entities" below are the two Key Entities spec.md names,
expressed as the shapes this plan's implementation operates on. Both are in-memory /
structural concepts; neither is durable state (Constitution Principle V: durable state
lives in the wiki, task artifacts, and harness records — this feature adds none).

## Containment boundary

The policy-designated root(s) a guarded tool call is confined to, and the resolved
physical path a requested action is checked against.

| Field | Type | Notes |
|---|---|---|
| `RequestedPath` | `string` | The literal `path` property the model supplied in the tool call — never used as the authority for the boundary check, only for denial messages and logging. |
| `CanonicalPath` | `string` | The resolved physical absolute path, produced by `GuardedToolExecutor.Canonicalize` → `ResolvePhysicalPathInRepository`: lexical normalization (`Path.GetFullPath`), then a segment-by-segment symlink walk that now recurses through chained reparse points (research.md D2), capped at 40 hops. |
| `RevalidatedPath` | `string` | New in this feature (research.md D3): the same resolution recomputed immediately before the mutating write/delete executes. Compared ordinally to `CanonicalPath`; a mismatch is a `revalidation_failed` denial. |
| `IsWithinRoot` | `bool` | Derived: whether `CanonicalPath` (and, for a write/delete, `RevalidatedPath`) falls under the repository root and, per `SafetyPolicy`, under an allowed read/write/delete prefix. |
| `DenialReason` | `string?` | One of the existing reasons (`traversal`, `out_of_scope`, `no_rule`, …) plus three new ones this feature adds: `malformed_path` (embedded NUL, research.md D1), `symlink_loop` (hop cap exceeded, D2), `revalidation_failed` (post-validation swap detected, D3). |

**Validation rules** (state transitions, not a persisted lifecycle):

1. A request with an embedded NUL byte in its `path` property is rejected as
   `malformed_path` before any filesystem call is attempted (D1).
2. Symlink resolution recurses through chained reparse points; exceeding 40 hops is
   rejected as `symlink_loop` (D2).
3. A path whose `CanonicalPath` resolves outside the repository root, or outside every
   allowed prefix for the requested action (read/write/delete), is rejected with the
   existing `traversal`/`out_of_scope`/`no_rule` reasons — unchanged by this feature.
4. For a write or delete only: immediately before the mutating call, `RevalidatedPath`
   is recomputed and compared to `CanonicalPath`; a mismatch is rejected as
   `revalidation_failed`, and nothing outside the originally validated target is ever
   touched (D3).

## Spawn-site registry

The enumerated, reviewed set of process-spawn call sites in the production codebase.
Not a runtime data structure — a structural fact asserted by a Phase 0 architecture
test (research.md D4/D5) and recorded here for traceability between the spec's Key
Entity and the ADR/tasks that enforce it.

| Spawn site (outermost type) | Namespace (adapter containment) | Executable | Argument construction | Content-derived? |
|---|---|---|---|---|
| `AgentProcessHost` (5 internal `Start*Process` methods: Ingest, Query, Lint, remediation-execution, message-turn) | `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` | Fixed literal `"dotnet"` | `ArgumentList.Add` only | No — `FileName` is a literal; every `ArgumentList` entry is either a literal flag or a Hub-resolved path/id, never task/document/agent-generated content interpreted as a flag or shell token |
| `MarkItDownConverter` (1) | `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown` | `_options.ExecutablePath` (loaded from configuration at startup) | `ArgumentList.Add` only | No — the executable path is a deployment-time configuration value, never derived from a submission; the one argument (`inputPath`) is a Hub-generated storage path, not the submitted file's original name |

**Validation rule**: an automated structural test (Phase 0, Boundary Rule R1/R2 per
research.md) asserts that no production type outside this table constructs a
`System.Diagnostics.Process`/`ProcessStartInfo`, and that neither listed site ever sets
the shell-parsed `Arguments` string property. Growing this table (a new agent type, a
new converter) is a normal feature change that updates the allowlist in the same PR as
the new call site — not a structural violation, per ADR-034's framing of R1/R2 as a
closed-but-amendable set (Constitution Principle III: surface growth is a deliberate
amendment, not a broken test, for the *enumeration*; the *rule* — no site outside the
enumerated set — stays permanently enforced).

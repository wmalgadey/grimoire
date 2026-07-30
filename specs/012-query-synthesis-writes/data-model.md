# Data Model: Query Agent Synthesis Writes

Entities from spec.md `## Key Entities`, refined with the decisions in `research.md`
and ADR-015. Query's read-side data model (Query Turn, Conversation Record turn
bookkeeping) is otherwise unchanged from `specs/011-query-conversations/data-model.md`
except for the extension below.

## Write Rule *(config, one entry per policy file rule)*

Extends the existing policy-file rule shape (`data/agents/*/policy.json` `write[]`
entries). Backward compatible: `mode` absent ⇒ `read-write` (Ingest's existing policy
file is unaffected without edits).

| Field | Type | Notes |
|---|---|---|
| `pathPrefix` | string | Unchanged existing field |
| `mode` | `read-write` \| `create-only` | New. `create-only`: denied if the canonical target already exists on disk. `read-write`: existing-target writes go through the compare-and-swap check (below) instead |

`data/agents/query/policy.json` (version bumps 1 → 2):

```json
{
  "version": 2,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "pages/" },
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" }
  ],
  "write": [
    { "pathPrefix": "pages/", "mode": "create-only" },
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" }
  ]
}
```

## Run Read-Hash Map *(in-memory, per agent-process run)*

Owned by `SharedFileWriteGuard`, one instance per `GuardedToolExecutor` (same
lifecycle as the existing `WriteJournal`). Not persisted; discarded when the run
ends.

| Field | Type | Notes |
|---|---|---|
| `canonicalPath` | string | Key |
| `contentSha256` | string | Hash captured at the most recent successful `read_file` of this path this run; updated again after this run's own successful write to the same path |

## Write-Coordination Lock *(file, one per contested target, cross-process)*

Stored at `<base>/data/write-locks/<sha256(canonicalTargetPath)>.lock`
(`ResolvedGrimoirePaths.WriteLocksDir`, ADR-009 pattern, outside `wiki/` and git per
ADR-003). Held only for the duration of one guarded write's existence/hash-check plus
atomic rename (milliseconds); acquired via OS-level exclusive file open
(`FileShare.None`); released in `finally` regardless of outcome; released
automatically by the OS if the holding process dies. Not a domain entity — pure
operational plumbing, never referenced by wiki content or by the agent.

## Denied Action *(extends the existing `DeniedActionRecord`)*

No shape change; two new values for the existing `reason` field, plus one more
introduced by lock timeout:

| Reason | Meaning |
|---|---|
| `create_only_target_exists` | Write denied: rule is `create-only` and the target already exists |
| `write_conflict_stale_read` | Write denied: target's current content hash does not match this run's last-read hash for it |
| `write_coordination_timeout` | Write denied: lock acquisition exceeded the bounded backoff cap |

Existing reasons (`no_rule`, `out_of_scope`, `traversal`) are unchanged.

## Run Completion Metadata *(extends `RunCompletionMetadata`)*

| Field | Type | Notes |
|---|---|---|
| `CreatedArtifacts` | list of string \| null | New. Canonical paths from this run's `GuardedToolExecutor.TouchedPaths` that matched a `create-only` rule — i.e., pages this turn created. Null/empty for a turn that created nothing |

Surfaces into the Conversation Record's per-turn bookkeeping (ADR-014's reserved
extension point) as:

| Field | Type | Notes |
|---|---|---|
| `created_pages` | list of string | Wiki-root-relative paths of pages this turn created (empty list, not omitted, when none) |

## Synthesis Page *(wiki content — agentic, not a backend-modeled entity)*

A wiki page like any other (per spec's Key Entities), distinguished only by
convention the agent applies under its instruction file: a source-type tag marking
it as synthesized content, links to the pages it drew from, and origin attribution.
The harness has no `SynthesisPage` type, no validation of its frontmatter shape
beyond what already applies to any page, and no knowledge of what makes content "a
synthesis" — that judgment is entirely `agents/query/system-prompt.md`'s (Principle
V; FR-002). The harness's only structural fact about a Synthesis Page is that its
path did not previously exist (the create-only check) and that it lives under the
`pages/` write scope granted to Query.

## State transitions

No new state machine. The guarded-write path gains one decision point:

```text
write_file(path, content)
  → SafetyPolicy.Evaluate(canonical, isWrite: true)
      deny (no_rule | out_of_scope | traversal) → recorded, tool error, run continues
      allow (mode = create-only | read-write)
          → SharedFileWriteGuard.AcquireAsync(canonical)
              lock timeout → deny (write_coordination_timeout), recorded, run continues
              lock acquired:
                mode = create-only AND File.Exists(canonical)
                    → deny (create_only_target_exists), release lock, recorded, run continues
                mode = read-write AND File.Exists(canonical) AND hash(canonical) != run's last-read hash
                    → deny (write_conflict_stale_read), release lock, recorded, run continues
                otherwise
                    → WriteJournal.RecordAsync (existing) → atomic temp+rename write (existing)
                    → update run's read-hash for canonical to the new content's hash
                    → release lock
                    → TouchedPaths.Add(canonical) (existing)
```

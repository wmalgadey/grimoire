# Contract: Guarded Tool Surface

The complete tool surface exposed to the Ingest agent's model loop. Every call is
mediated by `GuardedToolExecutor`: canonicalize target → evaluate
[safety policy](./safety-policy.md) → deny (record + `is_error` tool result, run
continues) or allow (journal for writes → execute → record). No other tools exist; no
filesystem access bypasses this layer (enforced by the Phase 0 structural test).

Paths supplied by the model are interpreted relative to the repository root. The
executor canonicalizes them (`Path.GetFullPath`, `..`/symlink traversal collapsed)
before policy evaluation; the canonical path is what the policy sees.

## `list_files`

Read-scope tool. Lists files under a directory so the agent can explore the wiki.

```json
{
  "name": "list_files",
  "description": "List files and directories under a path inside the allowed read scope.",
  "input_schema": {
    "type": "object",
    "properties": {
      "path": { "type": "string", "description": "Directory path relative to the repository root." }
    },
    "required": ["path"]
  }
}
```

**Result (allowed)**: newline-separated relative paths (directories suffixed `/`).
**Result (denied)**: `is_error: true`, body `denied: <reason>` — see Denial semantics.
**Errors that are not denials**: nonexistent directory ⇒ ordinary `is_error` result
("not found"), no `DeniedActionRecord`.

## `read_file`

Read-scope tool.

```json
{
  "name": "read_file",
  "description": "Read the full content of a file inside the allowed read scope.",
  "input_schema": {
    "type": "object",
    "properties": {
      "path": { "type": "string", "description": "File path relative to the repository root." }
    },
    "required": ["path"]
  }
}
```

**Result (allowed)**: file content as UTF-8 text.
**Result (denied / not found)**: as for `list_files`.

## `write_file`

Write-scope tool. Creates or fully overwrites one file. The **only** wiki mutation
primitive — supersession, catalog updates, and log entries are all expressed as
`write_file` calls whose *content* follows the instruction files.

```json
{
  "name": "write_file",
  "description": "Create or overwrite a file inside the allowed write scope with the given content.",
  "input_schema": {
    "type": "object",
    "properties": {
      "path": { "type": "string", "description": "File path relative to the repository root." },
      "content": { "type": "string", "description": "Full new file content (UTF-8 markdown)." }
    },
    "required": ["path", "content"]
  }
}
```

**Executor obligations before an allowed write** (order is contractual):

1. Journal the target's prior state (`existedBefore`, previous bytes) — FR-013.
2. Create parent directories inside the write scope as needed.
3. Write atomically (temp file + rename within the same directory).
4. Record the touched path for the task artifact and emit
   `ingest.tool.allowed` / `wiki.ingest.tool_calls_total{decision=allowed}`.

## Denial semantics (all tools)

On a policy denial the executor MUST, in order:

1. Append a `DeniedActionRecord{action, target, reason}` to the run's denial list
   (surfaces in the task artifact — FR-008, SC-002).
2. Emit `ingest.tool.denied` (WARN) and increment
   `wiki.ingest.actions_denied_total`.
3. Return a `tool_result` with `is_error: true` and body
   `denied: <reason>. This action is outside the safety policy; continue with your remaining allowed work.`
4. Continue the loop — a denial never terminates the run.

## Loop-level contract

- Tools are offered to the model on every turn; the loop ends at `stop_reason:
  "end_turn"` or when the turn/token cap trips (cap breach ⇒ run failure ⇒ rollback).
- Tool executor behavior MUST be identical under `AnthropicModelClient` and
  `FakeModelClient` — hermetic tests exercise this exact contract (Principle II).
- Adding a tool to the registry is the sanctioned extension mechanism (ADR-006);
  tools with wiki-content semantics (e.g. `create_wiki_page`) are prohibited
  (Principle V).

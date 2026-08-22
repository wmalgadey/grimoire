# Contract: Guarded Tool Surface (Lint)

**Feature**: 026-guarded-tool-surface | **ADRs**: ADR-006, ADR-011, ADR-030, ADR-031

The tool surface offered to the Lint agent. Ingest and Query declare only the original three
and, per ADR-011 R3/R11, cannot reach the rest even if the model requests them by name.

Every schema declares `additionalProperties: false` alongside its `required` list (#127), so
the provider validates shape before the call reaches us. Shape validation is not authorization:
a schema-valid call to a forbidden path is still denied by policy.

## `search_files` — new

Mimics `grep -rn`.

```json
{
  "type": "object",
  "properties": {
    "pattern":     { "type": "string", "description": "Regular expression. Non-backtracking syntax: no lookaround or backreferences." },
    "path":        { "type": "string", "description": "Optional directory or file prefix to narrow the search, relative to the content root." },
    "ignore_case": { "type": "boolean", "description": "Case-insensitive matching. Default false." },
    "max_results": { "type": "integer", "description": "Cap on returned matches. Default 200, maximum 1000." }
  },
  "required": ["pattern"],
  "additionalProperties": false
}
```

**Result**: one match per line, `path:line:text`, in stable path then line order. A truncated
result set ends with an explicit truncation line naming the cap — the agent is never left to
infer completeness.

| Condition | Result | Recorded |
|---|---|---|
| `path` outside the read scope | `is_error`, denial reason | Denial (`lint.tool.denied`) |
| A matching file outside the read scope | **Omitted silently** | Nothing — reporting it would disclose the path |
| Pattern > 1000 chars, or unsupported syntax | `is_error`, reason | `wiki.search.pattern_rejected` |
| Cap reached | Results + truncation marker | `wiki.search.truncated` |
| 2 s budget exhausted | Partial results + incomplete marker | `wiki.search.timed_out` |

## `read_file` — changed, backward compatible

Mimics `cat`, or `sed -n 'X,Yp'` / `head` when ranged.

```json
{
  "type": "object",
  "properties": {
    "path":             { "type": "string" },
    "offset":           { "type": "integer", "description": "1-based first line to return." },
    "limit":            { "type": "integer", "description": "Maximum number of lines to return." },
    "frontmatter_only": { "type": "boolean", "description": "Return only the frontmatter block." }
  },
  "required": ["path"],
  "additionalProperties": false
}
```

With none of `offset`/`limit`/`frontmatter_only`, behavior is byte-for-byte what it is today,
including setting the write-coordination baseline. **With any of them set, the read is partial
and does not set that baseline** — a subsequent write to the same path is refused until the
page has been read in full (ADR-030 R3).

## `batch` — new

No single shell equivalent; the nearest idiom is a sequence of read commands in one invocation.

```json
{
  "type": "object",
  "properties": {
    "calls": {
      "type": "array",
      "maxItems": 20,
      "items": {
        "type": "object",
        "properties": {
          "tool":  { "type": "string", "enum": ["list_files", "read_file", "search_files"] },
          "input": { "type": "object" }
        },
        "required": ["tool", "input"],
        "additionalProperties": false
      }
    }
  },
  "required": ["calls"],
  "additionalProperties": false
}
```

The `enum` makes a write unrepresentable at the schema level; the executor rejects it again at
runtime, because the schema is the provider's guarantee and the executor is ours. Rejection is
wholesale — no member executes.

## `delete_file` — new

Mimics `rm`.

```json
{
  "type": "object",
  "properties": {
    "path": { "type": "string", "description": "File to delete, relative to the content root." }
  },
  "required": ["path"],
  "additionalProperties": false
}
```

Evaluated against the **`delete` scope**, never the write scope. The deletion is journaled with
its content before the file is removed, so a later failure in the same run restores it.

## `list_files` / `write_file` — unchanged

Shapes and semantics as ADR-006 defined them.

## Surface summary

| Tool | Shell analogue | Scope evaluated | Declared by |
|---|---|---|---|
| `list_files` | `ls` / `find` | read | Ingest, Query, Lint |
| `read_file` | `cat` / `sed -n` / `head` | read | Ingest, Query, Lint |
| `search_files` | `grep -rn` | read | Lint |
| `batch` | — | per member | Lint |
| `write_file` | `>` redirect | write | Ingest, Query, Lint |
| `delete_file` | `rm` | **delete** | Lint |

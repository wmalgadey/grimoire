# Contract: Query Agent Guarded Tool Surface (Read-Only)

Analogous to `specs/002-agentic-ingest-core/contracts/guarded-tools.md`, but the Query
agent's tool surface has **no write tool at all** (FR-011, ADR-011 R3) — this is not the
same guarantee as "write denied by policy," it is "no write tool exists to call."

Every call is mediated by the shared `Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor`:
canonicalize target → evaluate `agents/query/policy.json` → deny (record + `is_error`
tool result, run continues) or allow (execute → record). Paths supplied by the model
are interpreted relative to the wiki root (`--wiki-root`, same convention as Ingest).

## `list_files`

Identical schema and behavior to the Ingest contract's `list_files`.

```json
{
  "name": "list_files",
  "description": "List files and directories under a path inside the allowed read scope.",
  "input_schema": {
    "type": "object",
    "properties": {
      "path": { "type": "string", "description": "Directory path relative to the wiki root." }
    },
    "required": ["path"]
  }
}
```

## `read_file`

Identical schema and behavior to the Ingest contract's `read_file`.

```json
{
  "name": "read_file",
  "description": "Read the full content of a file inside the allowed read scope.",
  "input_schema": {
    "type": "object",
    "properties": {
      "path": { "type": "string", "description": "File path relative to the wiki root." }
    },
    "required": ["path"]
  }
}
```

## `write_file` — deliberately absent

`Grimoire.QueryAgent`'s tool registry does not declare `write_file`; the model is
never offered it as a choice on any turn. This is the structural half of FR-011. The
policy half (below) is a second, independent layer — even a hypothetical future model
behavior that tries to invoke an unknown tool name is rejected the same way any
unknown tool is (`is_error`, unrecognized tool), never reaching a filesystem write.

## Denial semantics (both tools)

Identical to the Ingest contract: on denial, the executor appends a
`DeniedActionRecord`, emits `query.tool.denied` (WARN) and increments
`query.tool_calls_total{decision=denied}`, and returns
`is_error: true` with body `denied: <reason>. This action is outside the safety
policy; continue with your remaining allowed work.` A denial never terminates the run
(FR-012).

## Policy file: `agents/query/policy.json`

Same schema and evaluation algorithm as
`specs/002-agentic-ingest-core/contracts/safety-policy.md`
(`Grimoire.Domain.Guardrails.SafetyPolicy`, dependency-free, deny-by-default). Only a
`read` scope is meaningful (no write tool exists to gate), but the file still declares
an empty `write` array for schema uniformity with the Ingest policy file and to make
the "no write authority" statement explicit and auditable in the file itself:

```json
{
  "version": 1,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "pages/" },
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" }
  ],
  "write": []
}
```

Note `tasks/` is **not** in the read scope — spec Assumptions: "the wiki" means page
content, not ingest task artifacts; the Query agent never reads Task Artifacts or
ingest sources (FR-013's injection-resistance guarantee is unaffected either way, since
policy enforcement doesn't depend on content).

Identity (`{path, version, sha256}`) is recorded on every Query Run Artifact (FR-016),
same as Ingest's policy identity on its Task Artifact.

## Structural enforcement (FR-014)

`Grimoire.ArchTests` gains a Red/Green-probed rule (ADR-011 C7) asserting that no
filesystem-write API (`File.*Write*`, `File.Delete`, `Directory.Delete`, etc.) is
reachable from `Grimoire.QueryAgent`'s assembly outside
`Grimoire.AgentRuntime.Guardrails` — which, for this process, contains no write branch
at all, since its registered tool set never includes `write_file`. The probe: introduce
a deliberate write call in a `Grimoire.QueryAgent`-only scratch class, verify the rule
fails, remove it, verify green.

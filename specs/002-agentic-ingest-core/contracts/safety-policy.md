# Contract: Safety Policy File Format

The versioned, deny-by-default authority definition for an agent run (FR-006, FR-007).
Lives git-tracked at `agents/ingest/policy.json`, passed to the agent via
`--policy-path`. Parsed with System.Text.Json; evaluated by
`Grimoire.Domain.Guardrails.SafetyPolicy` (dependency-free).

## Schema

```json
{
  "version": 1,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "wiki/" },
    { "pathPrefix": "agents/ingest/" }
  ],
  "write": [
    { "pathPrefix": "wiki/pages/" },
    { "pathPrefix": "wiki/index.md" },
    { "pathPrefix": "wiki/log.md" },
    { "pathPrefix": "wiki/tasks/" }
  ]
}
```

| Field | Type | Rules |
| --- | --- | --- |
| `version` | integer ≥ 1 | Required. Bumped on any rule change; recorded in every task artifact together with the file's SHA-256 (FR-012). |
| `defaultDecision` | string | Required. MUST be `"deny"` — the parser rejects any other value; there is no allow-by-default mode. |
| `read` | array of rules | Allow rules evaluated for `list_files` and `read_file`. |
| `write` | array of rules | Allow rules evaluated for `write_file`. |
| `rule.pathPrefix` | string | Repo-root-relative prefix. A prefix ending in `/` matches the directory subtree; otherwise it matches that exact file path. |

Unknown JSON properties are rejected (fail-closed parsing). Empty `read`/`write` arrays
are legal and mean "deny everything in that scope" (the misconfigured-policy edge case in
the spec: the run ends with every intended action denied and recorded, not silently
empty).

## Evaluation algorithm (normative)

1. Resolve the declared prefixes once at load time against the actual content-root /
   agents directories supplied by the Hub, yielding canonical absolute prefixes.
2. For each tool call: canonicalize the requested target (`Path.GetFullPath`; `..` and
   symlink traversal collapsed) **before** any filesystem access.
3. If the canonical target does not start with any allowed canonical prefix for the
   tool's scope (read vs. write) ⇒ **deny** with reason `no_rule` /
   `out_of_scope`; if canonicalization escaped the repository root ⇒ deny with reason
   `traversal`.
4. There are no deny rules and no rule ordering — the only decisions are "matches an
   allow rule" or "denied by default".

## Failure modes

| Condition | Behavior |
| --- | --- |
| Policy file missing | Run fails before any wiki change (no policy = no authority), FR-003-style clear reason |
| Unparseable / schema-invalid | Same — fail before first model turn |
| `defaultDecision` ≠ `"deny"` | Parse error (fail closed) |
| All-denying policy | Run proceeds; every write attempt is denied and recorded; run record shows intended actions (spec edge case) |

## Identity & traceability (FR-012)

Policy identity recorded in each task artifact = `{path, version, sha256(file bytes)}`.
Git history of `agents/ingest/policy.json` is the audit trail of authority changes.

## Change protocol

Editing rules requires bumping `version` in the same commit. Widening the write scope is
a guardrail change — sanctioned backend-adjacent change per the constitution's boundary
smell test, but it MUST happen in the policy file, never as code that bypasses
evaluation.

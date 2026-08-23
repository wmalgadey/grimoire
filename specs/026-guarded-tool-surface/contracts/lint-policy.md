# Contract: Lint Policy Artifact

**Feature**: 026-guarded-tool-surface | **ADRs**: ADR-006, ADR-016 (superseded), ADR-031

One policy file governs every Lint run. All three coordinators — `LintRunCoordinator`,
`RemediationRunCoordinator`, `RemediationMessageTurnCoordinator` — pass
`_paths.Lint.PolicyPath`, unchanged. **The harness must not branch on run mode when deciding
what a write or delete may touch** (ADR-031 R1).

## After this feature

```json
{
  "version": 2,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" },
    { "pathPrefix": "." }
  ],
  "write": [
    { "pathPrefix": "." }
  ],
  "delete": [
    { "pathPrefix": "." }
  ]
}
```

Changes from version 1: the write rule loses `"mode": "frontmatter-only"` (defaulting to
`read-write`) and its `excludePrefixes` for `index.md`/`log.md`; a `delete` scope is added.

## Rules

| Rule | Behavior |
|---|---|
| Unknown `mode` string | Fail closed at load — unchanged |
| Missing or unparseable file | Run fails before any wiki change — unchanged |
| `delete` absent | No deletion permitted (deny-by-default) |
| Policy identity | Version + SHA-256 recorded in every task artifact — unchanged |

## Unchanged agents

| Agent | `write` | `delete` |
|---|---|---|
| Ingest | `.` read-write, plus `index.md`, `log.md` | **absent** |
| Query | `.` create-only, plus `index.md`, `log.md` | **absent** |
| Lint | `.` read-write | `.` |

Ingest already holds `read-write` on the content root. Had deletion been evaluated as a write
rather than as its own scope, this feature would have granted Ingest wiki-wide deletion as a
side effect. That is the reason for the third scope, and this table is the check on it.

## What is *not* enforced here

Whether `index.md` still agrees with the page set after Lint creates or deletes a page is not a
harness invariant. Reconciling it is within the agent's power and therefore within its judgment
(Principle V). ADR-017's entry-format rules and ADR-028's prepend ordering continue to bind the
*shape* of what Lint writes to those files.

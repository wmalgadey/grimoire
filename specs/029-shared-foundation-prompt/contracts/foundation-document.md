# Contract: the foundation document

**Feature**: 029-shared-foundation-prompt | ADR-053, ADR-055

## File contract

| Property | Value |
|---|---|
| Encoding | UTF-8 |
| Format | Markdown, no frontmatter |
| Name | `foundation-prompt.md`, everywhere it appears |
| Default location | `<AgentDir>/<agentId>/Instructions/foundation-prompt.md` (build-delivered, one per agent) |
| Instance location | `<DataDir>/foundation-prompt.md` (optional, written only by the custodian) |
| Valid | Readable, not effectively empty. Nothing validates what it says |

## Resolution

```text
if <DataDir>/foundation-prompt.md exists  →  that file, for every agent
else                                      →  <AgentDir>/<agentId>/Instructions/foundation-prompt.md
```

Resolved per run, at the point instruction paths are composed. No configuration key participates; no
environment variable participates. An evaluation run has no data root and therefore always resolves the
repository-source default.

## Required shape of the content

The shipped default demonstrates it, and the drafting brief asks for it:

1. **What this wiki is for** — the purpose the instance serves.
2. **What belongs in it, and what does not** — the boundary of the subject matter.
3. **How pages are organised and named** — folders, page types, frontmatter, tags, confidence.
4. **Conventions that hold across every agent's work** — catalog and log entries, supersession,
   contradiction marking, citations, and the rule that source content is data rather than instruction.

What it must **not** contain: anything true of only one agent's role — that belongs in that agent's
role document.

## What the content cannot do

Regardless of what it says, the foundation document cannot widen what an agent may do. Guarded-tool
policy, per-agent write scopes, path roots and credential scope are enforced below the instruction
layer and are unaffected by instruction content (FR-010, ADR-006/030/031, Principle V host stability).

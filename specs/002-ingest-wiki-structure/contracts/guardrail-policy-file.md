# Contract: Guardrail Policy File (Feature 002)

## Purpose

Define the versioned repository policy contract that controls autonomous ingest tool reads and writes.

## File Format

YAML

## Canonical Location

- Recommended: wiki/policy/ingest-guardrails.yml
- Alternate paths are allowed when passed via --guardrail-policy-path

## Schema

```yaml
version: "1"
deny_by_default: true
write_allow_prefixes:
  - wiki/
  - wiki/tasks/
read_allow_paths:
  - CLAUDE.md
  - .claude/skills/
  - specs/002-ingest-wiki-structure/spec.md
  - specs/002-ingest-wiki-structure/plan.md
  - docs/adr/
rules:
  - id: no-env-secret-dump
    action: read
    path_prefix: .env
    decision: deny
    reason: secrets file is not part of approved ingest context
```

## Evaluation Rules

- deny_by_default=true means actions are denied unless explicitly allowed.
- Write actions are allowed only when target path starts with one of write_allow_prefixes.
- Read actions are allowed only when path matches read_allow_paths or rule-based allow entries.
- Rule evaluation order is top-to-bottom; first match wins.
- If no rule matches, deny by default.

## Runtime Obligations

- Every autonomous tool action must produce an allow or deny decision.
- Denied actions must include policy rule id (if matched) and reason in logs/artifact.
- Denial of one action must not abort unrelated allowed actions.

## Validation Rules

- version is required.
- deny_by_default must be true for autonomous mode.
- write_allow_prefixes must include wiki/ and task-artifact destination.
- Absolute paths and parent-directory traversal entries (.. segments) are invalid.

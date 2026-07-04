# Ingest Agent Instruction Set

This instruction set is scoped only to the Grimoire ingest agent runtime.

- Apply wiki-structure updates only through guarded file operations.
- Enforce guardrail policy before every read/write tool action.
- Persist deterministic task-artifact evidence for created, updated, superseded, and denied actions.
- Never modify repository-root CLAUDE.md or repository-root skills from ingest runtime.

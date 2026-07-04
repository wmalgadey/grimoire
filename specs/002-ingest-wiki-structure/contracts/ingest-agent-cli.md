# Contract: Ingest Agent CLI (Feature 002)

## Purpose

Define the CLI interface for autonomous ingest runs that produce complete wiki structure updates and deterministic artifacts.

## Invocation

```bash
dotnet run --project backend/src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj -- [args]
```

## Required Arguments

- --task-id: Unique task identifier
- --source-ref: Source path/URI/reference
- --source-kind: file | pasted_text | url
- --pages-dir: Directory for wiki pages
- --tasks-dir: Directory for task artifacts
- --index-path: Path to wiki index markdown
- --log-path: Path to append-only ingest log markdown
- --guardrail-policy-path: Path to versioned guardrail policy file
- --instructions-root: Repository root used to resolve CLAUDE.md and SKILL.md context

## Optional Arguments

- --skill-path: Repeatable path argument for additional skill context files
- --dry-run: true|false (default false); when true, writes are not applied but proposed actions are emitted in artifact

## Exit Codes

- 0: Completed successfully (may include denied actions that were properly handled)
- 1: Failed due to unrecoverable ingest error
- 2: Invalid argument or missing required contract input
- 3: Guardrail policy load/validation error

## Behavioral Requirements

- Runtime MUST load CLAUDE.md and selected SKILL.md context before write planning.
- Runtime MUST evaluate every autonomous read/write action against policy.
- Policy-denied actions MUST be recorded and skipped, while other allowed actions continue.
- Runtime MUST emit task artifact updates for running and terminal states.

## Output Artifacts

- Updated wiki markdown files under allowed paths
- Updated index markdown
- Task artifact markdown in tasks directory following task-artifact-format.md
- Log entries in log markdown

## Non-Goals

- This contract does not define LLM prompt wording.
- This contract does not guarantee deterministic content semantics from model output.
- This contract does define deterministic guardrail and artifact behavior.

# Contract: Ingest Agent CLI — 004 Changes

The Hub↔agent contract remains ADR-002's child-process model (arguments in, task
artifact + wiki files + exit code out). This feature changes the instruction-related
arguments only.

## Removed arguments

| Argument | Replaced by |
|----------|-------------|
| `--instructions-dir` | `--system-prompt-path` + `--default-user-prompt-path` |

## New arguments

| Argument | Required | Value |
|----------|----------|-------|
| `--system-prompt-path` | yes | Path to `agents/ingest/system-prompt.md` |
| `--default-user-prompt-path` | yes | Path to `agents/ingest/default-user-prompt.md` |
| `--user-prompt` | no | Custom steering text (≤ 8,000 chars, already validated by Hub); when present it overrides the default file's content |

All other arguments (`--task-id`, `--source-ref`, `--source-kind`, path arguments,
`--policy-path`, …) are unchanged.

## Fail-closed rules (exit code ≠ 0 before any wiki write)

| Condition | Behavior |
|-----------|----------|
| System prompt file missing / unreadable / whitespace-only | Run fails; task artifact records human-readable reason |
| `--user-prompt` absent AND default-prompt file missing / unreadable / whitespace-only | Run fails; task artifact records human-readable reason |

## Prompt assembly (harness-owned, not user-editable)

The initial user message is composed by the agent harness as:

```text
Task ID: <task_id>
Source reference: <source_ref>

<effective user prompt>

<source>
<source content — untrusted data>
</source>
[injection framing unchanged from 002]
```

The effective user prompt is `--user-prompt` if provided, else the content of
`--default-user-prompt-path`. The scaffold (task/source header, `<source>` delimiters,
injection framing) cannot be altered by any submission input.

## Task artifact recording

| Field | Value |
|-------|-------|
| `instruction_files` (existing list shape) | Exactly one entry: system-prompt path + SHA-256 |
| `user_prompt_source` | `default` or `custom` |
| `## User Prompt` body section | Effective prompt verbatim |

## Compatibility note

`agents/ingest/CLAUDE.md` and `agents/ingest/skills/wiki-maintenance/SKILL.md` are
deleted. Any leftover copies are ignored — the agent reads only
`--system-prompt-path`. Dispatcher (`IngestAgentDispatcher`) and eval harness
(`Grimoire.AgentEvals`) must pass the new arguments.

# Contract: Ingest Agent CLI Invocation (v2 — agentic core)

Supersedes the 001 contract for feature 002. The spawn model (Hub → child process,
ADR-002) and the 001 argument set are retained; two arguments and one environment
variable are added. The Hub does not hold ingest business logic in-process.

## Invocation

```text
grimoire-ingest-agent \
  --task-id <task_id> \
  --source-ref <path-or-url-or-literal-marker> \
  --source-kind file|url|pasted_text \
  --pages-dir <path/to/content-root/pages> \
  --tasks-dir <path/to/content-root/tasks> \
  --index-path <path/to/content-root/index.md> \
  --log-path <path/to/content-root/log.md> \
  --instructions-dir <path/to/agents/ingest> \
  --policy-path <path/to/agents/ingest/policy.json>
```

Pasted text arrives via stdin when `--source-kind pasted_text` (unchanged). All paths
are absolute, resolved by the Hub before spawning.

**New in 002**:

| Argument | Meaning |
| --- | --- |
| `--instructions-dir` | Root of the instruction set: `CLAUDE.md` + `skills/*/SKILL.md`, loaded verbatim into the agent's system prompt (FR-002) |
| `--policy-path` | The [safety policy file](./safety-policy.md) governing all tool calls (FR-006/FR-007) |

## Environment

| Variable | Notes |
| --- | --- |
| `ANTHROPIC_AUTH_TOKEN` | Injected only into this child process's environment by the Hub (ADR-004); never present in the Hub's own process environment |
| `GRIMOIRE_INGEST_MODEL` | Optional; effective model id for the tool-use loop. Default `claude-opus-4-8` (research R3). Same injection point as the credential. |

## Responsibilities (agent-process-owned)

1. **Startup, before any model call**: create the task artifact at
   `<tasks-dir>/<task_id>.md` with `status: running` (SC-001).
2. **Load & verify governance**: read instruction files and policy, compute SHA-256
   identities, record them in the task artifact (FR-012). Any load failure ⇒ terminate
   with `status: failed`, human-readable reason, zero wiki changes (FR-003, SC-003).
3. **Read the source** per `--source-kind`; never modify it (FR-014); present it to the
   model as delimited untrusted data (research R9).
4. **Run the tool-use loop**: system prompt = instruction set; tools per
   [guarded-tools.md](./guarded-tools.md); every call policy-checked at invocation;
   denials recorded and returned as `is_error` tool results while the run continues
   (FR-008).
5. **On success** (`end_turn` within caps): finalize the task artifact to
   `status: completed` with pages created/updated/superseded, denied actions, governing
   identities, and the agent's narrative; verify a `log.md` entry for this task id
   exists, appending the minimal backstop entry if the agent omitted it (research R8).
6. **On failure** (exception, API failure, cap breach): roll back all journaled writes
   in reverse order (FR-013), finalize the artifact to `status: failed` with reason and
   `rolled_back` outcome, and append the failure log entry (harness-owned).

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Task artifact reached `status: completed` |
| `1` | Task artifact reached `status: failed` (handled failure — includes instruction/policy load failure, all-denied runs that produced no result, cap breach) |
| `>1` | Unhandled crash; Hub restart reconciliation (ADR-003) detects and reconciles the stuck task. Journal rollback cannot run in this case — reconciliation marks the task failed and the git working tree is the recovery mechanism of last resort. |

## Changes vs. 001 contract

- Multi-page fan-out is now **in scope**: the agent may touch any number of pages per
  run (001's "exactly one primary page" rule is retired with the deterministic
  pipeline).
- Update-vs-create is no longer described as a single decision point; it is agent
  judgment across the whole run (FR-005).
- Progress streaming remains out of scope (fire-and-forget; Hub observes the final
  artifact state after process exit).

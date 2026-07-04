# Contract: Ingest Agent CLI Invocation

The Ingest agent (`Grimoire.IngestAgent`, ADR-002) is invoked by the Hub as a child
process. This is the interface contract between the Hub and the agent for this feature.

## Invocation

```text
grimoire-ingest-agent \
  --task-id <task_id> \
  --source-ref <path-or-url-or-literal-marker> \
  --source-kind file|url|pasted_text \
  --pages-dir <path/to/content-root/pages> \
  --tasks-dir <path/to/content-root/tasks> \
  --index-path <path/to/content-root/index.md> \
  --log-path <path/to/content-root/log.md>
```

Pasted text is passed via stdin when `--source-kind pasted_text` is set (not as a CLI
argument, to avoid shell-length/escaping limits).

All four paths are absolute and resolved by the Hub before spawning the agent, from a
single content-root directory (default name `wiki`, configurable via the Hub's
`ContentRootDirName` setting or `--content-root`, see `plan.md ## Project Structure`); the
agent itself has no knowledge of the content-root name or its configurability.

## Environment

| Variable | Notes |
|---|---|
| `ANTHROPIC_AUTH_TOKEN` | Injected only into this child process's environment by the Hub (ADR-004); never present in the Hub's own process environment |

## Responsibilities (agent-owned; Hub does not touch these files directly)

1. On startup, before any LLM call: create the task artifact at
   `<tasks-dir>/<task_id>.md` with `status: running` (satisfies FR-002/FR-003 — the Hub
   only needs to observe the file existing with a non-terminal status; the *Hub* also
   records the operational-state row per ADR-003, independent of this file).
2. Read the source per `--source-kind`; the source MUST NOT be modified (FR-009).
3. Decide update-vs-create for exactly one primary `WikiPage` using its own semantic
   judgment informed by `index.md` (FR-012); no deterministic lookup rule required.
4. On success: write/update the wiki page, update `index.md` under the correct category
   (FR-014), append a `log.md` entry (FR-015), and update the task artifact to
   `status: completed` with `pages_touched` populated and a human-readable summary
   (FR-005, FR-006).
5. On failure at any point: make no partial writes to `pages/` or `index.md` (FR-008);
   update the task artifact to `status: failed` with a human-readable `failure_reason`;
   append a `log.md` entry recording the failure (FR-015).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Task artifact reached `status: completed` |
| `1` | Task artifact reached `status: failed` (handled failure — e.g., unreadable/empty source) |
| `>1` | Unhandled crash; the Hub's operational-state row (ADR-003) is what allows the Hub to detect this and, on its own restart, reconcile any resulting stuck task |

## Out of scope for this contract

- Live progress streaming (SignalR, ADR-001) — this feature is fire-and-forget/batch
  (decision-context-overview.md §0); the Hub only observes the task artifact's final
  status once the child process exits.
- Multi-page fan-out — exactly one primary page per invocation (FR-004).

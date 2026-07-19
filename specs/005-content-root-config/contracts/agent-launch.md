# Contract: Hub → Ingest Agent Launch Arguments (delta)

ADR-002/ADR-008 child-process contract, amended by this feature. The agent receives
**every** path it operates on explicitly (FR-007); it performs no discovery.

## Argument changes

| Argument | Status | Semantics |
| --- | --- | --- |
| `--wiki-root <abs path>` | **new, required** | The resolved wiki content root. Anchor for (a) relative page paths in task artifacts and result reports (FR-009) and (b) safety-policy path-prefix resolution. |
| `--task-id`, `--source-ref`, `--source-kind`, `--pages-dir`, `--tasks-dir`, `--index-path`, `--log-path`, `--system-prompt-path`, `--default-user-prompt-path`, `--policy-path`, `--user-prompt` | unchanged | As today; all absolute, Hub-resolved. |

## Behavioral changes in the agent

1. `FindRepoRoot` (git shell-out) is deleted; startup requires `--wiki-root`. Missing
   `--wiki-root` → argument error, fail-closed before any write.
2. `PolicyLoader` resolves policy path prefixes against `--wiki-root`.
3. `PagesTouched` / `PagesCreated` / `PagesUpdated` / `PagesSuperseded` in the task
   artifact are relative to `--wiki-root` (previously repo root). Task-artifact
   consumers see content-root-relative paths — e.g. `pages/foo.md` instead of
   `wiki/pages/foo.md`.

## Policy file migration (`policy.json`)

Path prefixes become content-root-relative: `wiki/pages/` → `pages/`, `wiki/tasks/` →
`tasks/`, `wiki/index.md` → `index.md`, `wiki/log.md` → `log.md`. Deny-by-default
semantics, schema, and versioning (ADR-006) are unchanged; the policy `version` field
is bumped. This makes one policy file valid for any deployment regardless of where the
content root lives.

## Environment

Unchanged: scoped credential injection at spawn (ADR-004), NDJSON stdout event channel
(ADR-008). The secrets file the Hub reads from now defaults to `<data>/.env`.

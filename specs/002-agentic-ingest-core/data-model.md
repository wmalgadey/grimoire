# Data Model: Agentic Ingest Core

**Feature**: `002-agentic-ingest-core` | **Date**: 2026-07-04 | **Plan**: [plan.md](./plan.md)

Entities from the spec, refined by [research.md](./research.md). Domain state is plain
markdown/JSON files (ADR-003); the only relational state is the Hub's unchanged
operational-state row. C# types named here are harness types; nothing below encodes wiki
*content* judgment (Principle V).

## Entity Overview

```text
Source ──(read once)──▶ IngestRun ──governed by──▶ InstructionSet + SafetyPolicy
                            │
                            ├─ tool calls ──▶ GuardedToolExecutor ─┬─ allowed ─▶ WriteJournalEntry → WikiPage / WikiCatalog / IngestLog
                            │                                      └─ denied ──▶ DeniedActionRecord
                            └─ lifecycle ──▶ TaskArtifact (created → running → completed | failed)
```

## Source

The raw human-provided input. Immutable during the run (FR-014).

| Field | Type | Notes |
| --- | --- | --- |
| `kind` | `file \| url \| pasted_text` | From CLI `--source-kind` (001 contract, unchanged) |
| `reference` | string | Path, URL, or literal marker; pasted text arrives via stdin |
| `content` | string | Loaded once by `SourceReader`; wrapped in `<source>` delimiters as untrusted data (research R9) |

**Validation**: unreadable/empty source ⇒ run fails before any wiki change.

## InstructionSet

The versioned, human-readable files governing the agent (FR-002, FR-010, FR-012).

| Field | Type | Notes |
| --- | --- | --- |
| `rootDir` | path | From CLI `--instructions-dir` (default `agents/ingest/`) |
| `files[]` | list of `InstructionFile` | `CLAUDE.md` + every `skills/*/SKILL.md`, loaded verbatim into the system prompt |
| `InstructionFile.path` | repo-relative path | Recorded in task artifact |
| `InstructionFile.sha256` | hex string | Identity for FR-012 traceability |

**Validation / state rules**:

- `CLAUDE.md` missing, unreadable, or effectively empty (whitespace-only) ⇒ run fails
  **before** any wiki-affecting action with a human-readable reason (FR-003, SC-003).
- Files are read exactly once at run start; the hash recorded is the hash of the content
  actually placed in the agent's context (no TOCTOU gap).

## SafetyPolicy

Versioned deny-by-default read/write authority (FR-006, FR-007). Full schema:
[contracts/safety-policy.md](./contracts/safety-policy.md).

| Field | Type | Notes |
| --- | --- | --- |
| `version` | integer | Bumped on any rule change |
| `defaultDecision` | `"deny"` (fixed) | Parser rejects any other value |
| `read[]` | list of `PolicyRule{pathPrefix}` | Allow rules for `list_files`/`read_file` |
| `write[]` | list of `PolicyRule{pathPrefix}` | Allow rules for `write_file` |
| `sha256` | hex string (derived) | Policy identity = `version` + content hash (FR-012) |

**Validation / state rules**:

- Missing or unparseable policy ⇒ run fails before any wiki change ("no policy = no
  authority").
- Evaluation input is always the canonicalized absolute target path (symlinks/`..`
  collapsed) — evaluation happens **before** any filesystem access.
- Pure evaluation logic lives in `Grimoire.Domain.Guardrails` (dependency-free,
  unit-tested).

## GuardedToolCall / PolicyDecision

One attempted tool invocation flowing through `GuardedToolExecutor` (research R6).

| Field | Type | Notes |
| --- | --- | --- |
| `tool` | `list_files \| read_file \| write_file` | The only tools registered |
| `target` | canonical absolute path | Derived from the agent-supplied path |
| `turn` | integer | Model turn index (observability) |
| `decision` | `allowed \| denied` | Output of `SafetyPolicy.Evaluate` |
| `reason` | string | For denials: `out_of_scope`, `traversal`, `no_rule` |

**State transitions**: `requested → evaluated → (executed | denied-and-recorded)`.
A denial never aborts the run (FR-008); an executed write always journals first (R7).

## DeniedActionRecord

One policy refusal, persisted into the task artifact (FR-008, SC-002).

| Field | Type | Notes |
| --- | --- | --- |
| `action` | string | Tool name |
| `target` | string | The path/target as requested (plus canonical form) |
| `reason` | string | Human-readable denial reason |
| `turn` | integer | When in the run it happened |

## WriteJournalEntry

Prior state captured before every allowed write, enabling rollback (FR-013, research R7).

| Field | Type | Notes |
| --- | --- | --- |
| `path` | canonical absolute path | Journal keyed in write order |
| `existedBefore` | bool | `false` ⇒ rollback deletes the file |
| `previousContent` | bytes | Only when `existedBefore` |

**State rules**: journal is in-memory, per-run; rollback replays entries in reverse
order; task artifact and failure log entry are exempt (harness-owned records, FR-011/015).
Rollback outcome (`restored_ok`) is recorded in the artifact and logged.

## TaskArtifact (extended from 001)

Per-run record, harness-owned lifecycle (FR-011, FR-012, SC-001). Full format:
[contracts/task-artifact-format.md](./contracts/task-artifact-format.md).

New fields on top of 001's format:

| Field | Type | Notes |
| --- | --- | --- |
| `pages_created[]` / `pages_updated[]` / `pages_superseded[]` | wikilink lists | From the harness's write journal + agent-declared action classification |
| `denied_actions[]` | list of `DeniedActionRecord` | Structured, harness-recorded |
| `instruction_files[]` | list of `{path, sha256}` | FR-012 |
| `policy` | `{path, version, sha256}` | FR-012 |
| `model` | string | Effective model id (`GRIMOIRE_INGEST_MODEL`) |
| `rolled_back` | bool/null | Set on failure; `restored_ok` semantics |

**Lifecycle**: `running` (written at start, before first model turn) →
`completed \| failed` (terminal). Hub restart reconciliation (SQLite row ⇒ artifact
patched to `failed`) is unchanged from 001.

**Narrative**: body text below frontmatter = the agent's final summary message, copied
verbatim (agent judgment); structured fields come only from harness records.

## WikiPage / WikiCatalog (index.md) / IngestLog (log.md)

Unchanged in *format ownership*: they are plain markdown under the content root
(ADR-003). What changes is *authorship*: their content is now produced by the agent via
`write_file` under instruction-file conventions (FR-010, FR-016). The harness constrains
only *where* they may be written (policy write scope) and provides the log backstop
(research R8):

- Harness verifies at run end that `log.md` contains an entry for the task id; if
  absent — and always on failure — it appends a minimal factual entry
  (`ingest.log.backstop_appended`).
- No backend code parses or validates page frontmatter, categories, or tags — that is
  eval territory (SC-007).

## Hub operational state (unchanged)

The SQLite row (`task_id`, non-terminal status, timestamps) from 001/ADR-003 is reused
as-is; this feature adds no columns.

# Contract: Task Artifact Format & Ingest Agent CLI — 023 Changes

**Feature**: `023-task-ui-improvements` | **Requirement**: FR-003 | **Tasks**: T045, T046

Baseline: [`specs/002-agentic-ingest-core/contracts/task-artifact-format.md`](../../002-agentic-ingest-core/contracts/task-artifact-format.md)
(v2 frontmatter) and [`specs/004-agent-instruction-files/contracts/ingest-agent-cli.md`](../../004-agent-instruction-files/contracts/ingest-agent-cli.md)
(current CLI surface). Following feature 004's precedent, the additions are recorded here
in the current feature's contracts folder rather than by retro-editing the 002 contract —
the baseline document keeps describing what it decided, and this file states the delta.

Everything not listed below is unchanged: the file location
(`<MemoryDir>/tasks/{taskId}.md`, ADR-024), the frontmatter/body split, every existing
field, the NDJSON event channel, and ADR-002's per-process artifact ownership (the Hub
writes the pre-agent stages, the agent writes its own — neither read-modify-writes the
other's file).

## New frontmatter field: `title`

| Property | Value |
| --- | --- |
| Key | `title` |
| Position | Immediately after `task_id`, in both writers |
| Type | Double-quoted string, or the bare literal `null` |
| Escaping | `\` → `\\` and `"` → `\"` inside the quotes; readers strip exactly one enclosing quote pair and reverse both escapes |
| `null` case | Written when no label was supplied to the writer — in practice only a manual agent run launched without `--title` (the Hub always supplies one) |
| Length | Bounded by the fallback chain's own source: an extracted heading is capped at 120 characters (`SourceArtifactStore.TitleMaxLength`); a filename, URL, or task id is written as-is |
| Absent | Artifacts written before this feature carry no `title` key at all and read back as `null` — "defaults of their time" |

Quoting is what makes the two characters a heading most often contains — `:` (the
frontmatter key/value separator) and `"` — safe. A newline cannot occur: the extracted
heading is a single line by construction.

### Value: the label fallback chain

`title` is a **mirrored copy**, never an independent source of truth. It is always
resolved through the one chain in `KanbanBoardProjectionStore.ResolveTitle` over the
Hub-owned source-artifact manifest (`data-model.md` §3):

```text
manifest.Title  →  manifest.OriginalFileName  →  manifest.SourceUrl  →  taskId
```

Because that is the same chain the board rows and the task-detail response read, the
frontmatter and the UI cannot disagree. The manifest remains the source of truth; a
reader that wants the authoritative label reads the manifest, not this field.

### Which stage first carries the extracted heading

The manifest is written by `SourceArtifactStore.PersistNormalizedAsync` — that is, only
after conversion has produced normalized markdown. The stage writes therefore fall out as:

| Stage | Writer | `title` value |
| --- | --- | --- |
| `received` | Hub | Task id — the submission has not been converted yet, so no heading exists |
| `converting` | Hub | Task id — same reason |
| `queued` | Hub | Extracted heading (or filename/URL fallback) — the manifest now exists |
| `running` | Agent | The `--title` value handed in at launch |
| `completed` / `failed` (agent-reported) | Agent | The `--title` value handed in at launch |
| `failed` (Hub-reported: start failure, liveness exhaustion) | Hub | Re-resolved from the manifest |
| `queued` (after a manual restart, FR-010) | Hub | Re-resolved from the manifest |

The task-id value on the first two writes is correct behavior, not a defect: those writes
precede the only artifact from which a heading can be read. A conversion that fails before
the manifest exists (e.g. a URL fetch that 404s) leaves the terminal `failed` artifact on
the task-id fallback for the same reason.

## New CLI argument: `--title`

| Argument | Required | Value |
| --- | --- | --- |
| `--title` | no | The Hub-resolved label for this task, exactly as above |

Wiring: `IngestAgentRequest.Title` → `AgentProcessHost` appends `--title <value>` when
non-empty → `IngestCliOptions.Title` → carried verbatim into every `TaskArtifactDocument`
the agent writes.

This mirrors how `convert_steps` already survives the handover (004 FR-014), with one
deliberate difference: `convert_steps` is recovered by reading the Hub's pre-existing
artifact, whereas the title is passed **explicitly as a launch input**. Passing it avoids
adding a second read-modify-write on the shared artifact file and keeps each process's
artifact I/O its own (ADR-002).

When `--title` is omitted — a manual agent invocation, or the `SubmissionService` CLI
submit path where no manifest is produced — the agent writes `title: null`. Readers fall
back to the manifest chain, which yields the task id in exactly those cases, so the label
shown in the UI is unaffected.

## Reader behavior

Both frontmatter readers gained the field and the escaping-aware unquote:

| Reader | Namespace | Property |
| --- | --- | --- |
| `TaskArtifactFrontmatter` | `Grimoire.Hub.IngestSubmission` | `Title` (`string?`) |
| `TaskArtifactDocument` via `TaskArtifactStore.ReadAsync` | `Grimoire.IngestAgent.TaskArtifact` | `Title` (`string?`) |

Unquoting is now the exact inverse of the writers' escaping (strip one enclosing quote
pair, then reverse `\"` and `\\`) rather than a `Trim('"')`. This applies to every quoted
frontmatter value, not just `title` — a `source_ref` or `failure_reason` containing a
quote round-trips correctly for the same reason.

## Verification

`backend/tests/Grimoire.IntegrationTests/IngestTaskTitleTests.cs` (T044) asserts
state-based that the artifact's `title` equals the `title` the detail endpoint returns —
after a Hub-written stage, after the agent's own write, for each fallback in the chain,
for a heading containing `:` and `"`, and for the pre-conversion stages where the task-id
fallback is the correct answer.

# Data Model: Hexagonal Architecture Alignment & Task Detail Markdown View

**Feature**: 006-hexagonal-arch-tasks-ui | **Date**: 2026-07-19

## TaskRecord (read model served to the UI)

The per-task markdown artifact at `<TasksDir>/<taskId>.md` (task-artifact-format v2,
feature 003/004), exposed to the detail view as:

| Field | Type | Source | Notes |
| --- | --- | --- | --- |
| `taskId` | string | frontmatter `task_id` | Identity; must match the path segment |
| `status` | string | frontmatter `status` | Lifecycle column value |
| `agent` | string | frontmatter `agent` | e.g. `ingest` |
| `startedAt` | ISO-8601 string | frontmatter `started_at` | |
| `completedAt` | ISO-8601 string \| null | frontmatter `completed_at` | |
| `sourceRef` | string \| null | frontmatter `source_ref` | |
| `originalRef` | string \| null | frontmatter `original_ref` | |
| `failureReason` | string \| null | frontmatter | First line only (writer contract) |
| `body` | string (markdown) | file content minus frontmatter block | Rendered by the UI |

**Validation**: frontmatter parse reuses `TaskArtifactFrontmatter.TryParse`. Parse failure
or missing file ⇒ record "unavailable" (HTTP 404) — never a 500 for a malformed file.

**State transitions**: none owned by this feature. The record is written by the Hub
pipeline (pre-agent stages) and the agent process (run stages) under the existing
atomic-rename discipline; this feature only reads and observes it.

## TaskRecordChanged (realtime event)

| Field | Type | Notes |
| --- | --- | --- |
| `eventId` | string | Unique per publish (dedupe on client) |
| `taskId` | string | Task whose record changed |
| `changedAt` | ISO-8601 string | Debounced observation time |

Emitted at most once per task per debounce window (300 ms); carries no content —
consumers refetch the TaskRecord.

## Ports & Adapters inventory (structural model)

| Port (owner namespace) | Methods (contract essence) | Production adapter (namespace) | Test fake |
| --- | --- | --- | --- |
| `IAgentProcessLauncher` (`Grimoire.Hub.AgentDispatch`) | `StartAsync`, `RunToExitAsync` | `AgentProcessHost` (`Grimoire.Hub.Adapters.AgentProcess`) | `FakeAgentProcess` (exists) |
| `IMarkdownConverter` (`Grimoire.Hub.IngestSubmission`) | `ConvertAsync(inputPath) → MarkItDownConversionResult` | `MarkItDownConverter` (`Grimoire.Hub.Adapters.MarkItDown`) | `FakeMarkdownConverter` (new) |
| `IUrlContentFetcher` (`Grimoire.Hub.IngestSubmission`) | `FetchAsync(url) → UrlFetchResult` | `UrlContentFetcher` (`Grimoire.Hub.Adapters.HttpFetch`) | `FakeUrlContentFetcher` (new) |
| `IModelClient` (`Grimoire.IngestAgent.AgentCore`) | `NextTurnAsync` | `AnthropicModelClient` (`Grimoire.IngestAgent.Adapters.Anthropic`) | `FakeModelClient` (exists) |

**Persistence exemption** (no ports, containment only): `OperationalStateRepository` et al.
remain concrete in `Grimoire.Hub.OperationalState` — the sole namespace allowed to import
`Microsoft.Data.Sqlite`. File-based stores (`TaskArtifactStore`, `SourceArtifactStore`,
`KanbanBoardProjectionStore`, artifact writers) remain concrete local-filesystem adapters.

**Composition-root exemption**: each process's `Program.cs` may construct concrete
adapters to bind them to ports; no other orchestration code may reference adapter types.

## Frontend view model additions (`$lib/types.ts`)

- `TaskRecord`: mirror of the TaskRecord read model above.
- `TaskRecordChangedEvent`: mirror of the realtime event.
- `ConnectionState` reused for the detail view's staleness indicator.

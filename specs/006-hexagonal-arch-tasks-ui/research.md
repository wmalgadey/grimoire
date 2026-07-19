# Research: Hexagonal Architecture Alignment & Task Detail Markdown View

**Feature**: 006-hexagonal-arch-tasks-ui | **Date**: 2026-07-19

## Decision 1 — Adapter namespace scheme and composition-root exemption

**Decision**: Introduce per-process adapter namespaces `Grimoire.Hub.Adapters.<System>` and
`Grimoire.IngestAgent.Adapters.<System>`. Concrete external-system adapters move there:

| External system | Port (stays with consumer) | Adapter type | New adapter namespace |
| --- | --- | --- | --- |
| Spawned agent process | `IAgentProcessLauncher` (`Grimoire.Hub.AgentDispatch`) | `AgentProcessHost` | `Grimoire.Hub.Adapters.AgentProcess` |
| Subprocess converter (MarkItDown CLI) | `IMarkdownConverter` (new, `Grimoire.Hub.IngestSubmission`) | `MarkItDownConverter` | `Grimoire.Hub.Adapters.MarkItDown` |
| Outbound HTTP fetching | `IUrlContentFetcher` (new, `Grimoire.Hub.IngestSubmission`) | `UrlContentFetcher` | `Grimoire.Hub.Adapters.HttpFetch` |
| LLM provider API | `IModelClient` (`Grimoire.IngestAgent.AgentCore`, exists) | `AnthropicModelClient` | `Grimoire.IngestAgent.Adapters.Anthropic` |

The composition root (`Program.cs` of each process) is explicitly exempt from the
"no concrete adapter references" rule — it is the one place that wires adapters to ports.
NetArchTest rules encode the exemption by excluding the top-level-program type.

**Rationale**: Namespace-level containment (no new assemblies) is exactly what the amended
constitution prescribes ("namespace-level containment enforced by architecture tests is
sufficient until an ADR establishes a stronger boundary"). Ports remain owned by their
consuming orchestration namespace, matching the two ports that already exist.

**Alternatives considered**: Separate adapter assemblies per external system — rejected as
Big Design Up Front; constitution explicitly says no extra assemblies are mandated.
A single flat `Adapters` namespace without per-system segments — rejected because
containment rules ("LLM SDK only in the model-client adapter namespace") need per-system
namespaces to be expressible as NetArchTest rules.

## Decision 2 — Containment rules to enforce (NetArchTest, each with Red/Green probe)

**Decision**: Extend `Grimoire.ArchTests` with:

1. `Microsoft.Data.Sqlite` referenced only from `Grimoire.Hub.OperationalState` (designated
   persistence adapter namespace; persistence exemption applies, no port required).
2. `Anthropic` SDK referenced only from `Grimoire.IngestAgent.Adapters.Anthropic`.
3. `System.Net.Http.HttpClient` usage in Hub confined to `Grimoire.Hub.Adapters.HttpFetch`
   (SignalR/OTel framework wiring in `Program.cs`/`TelemetryExtensions` is composition-root
   configuration, not orchestration consumption).
4. `System.Diagnostics.Process` usage confined to `Grimoire.Hub.Adapters.AgentProcess` and
   `Grimoire.Hub.Adapters.MarkItDown` (Hub) — no process spawning elsewhere.
5. Orchestration namespaces (`Grimoire.Hub.*` except `Adapters.*` and the composition root)
   must not reference concrete adapter types (`AgentProcessHost`, `MarkItDownConverter`,
   `UrlContentFetcher`); mirror rule in the agent for `AnthropicModelClient`.

Existing rules (Domain dependency freedom, guarded write boundary, dispatch boundaries,
runtime paths) stay untouched.

**Rationale**: These five rules operationalize every normative bullet of the constitution's
hexagonal section for the systems this codebase actually touches. NetArchTest is already
the project's structural-test tool (Principle III).

**Alternatives considered**: Roslyn analyzers — heavier to author, no added value at this
scale. import-linter — wrong ecosystem.

## Decision 3 — Known violations to remediate (behavior-preserving)

**Decision**: The alignment is a pure restructuring:

- `SubmissionService` currently depends on concrete `AgentProcessHost`; switch to
  `IAgentProcessLauncher` (extend the port with the `RunToExitAsync` capability it uses,
  or split a second port method — resolved during implementation, contract stays hermetic).
- `IngestSubmissionPipeline` currently depends on concrete `MarkItDownConverter` and
  `UrlContentFetcher`; switch to the new ports; existing result records
  (`MarkItDownConversionResult`, `UrlFetchResult`) become part of the port contracts.
- `AnthropicModelClient` moves out of `AgentCore` into its adapter namespace; `AgentLoop`
  already consumes `IModelClient` only.
- Test fakes (`FakeModelClient`, `FakeAgentProcess`, new `FakeMarkdownConverter`,
  `FakeUrlContentFetcher`) implement the same ports; hermetic integration tests switch to
  fakes where they currently construct concrete adapters.

**Rationale**: FR-005/SC-003 demand zero behavioral change; type moves + interface
extraction achieve conformance without touching logic.

**Alternatives considered**: Wrapping adapters instead of moving them (delegating shims) —
rejected: leaves the violation in place and doubles the surface.

## Decision 4 — Task-record change detection and live updates

**Decision**: A Hub-hosted `TaskRecordWatcher` (background service) watches the resolved
`TasksDir` with a `FileSystemWatcher`, debounces per task id (300 ms), and publishes a
`taskRecordChanged { taskId, changedAt }` event on the existing SignalR
`IngestLifecycleHub`. The detail view refetches the record via the REST endpoint on every
event for its task, and refetches unconditionally on SignalR reconnect.

**Rationale**: Reuses the established realtime channel (ADR-001/ADR-008 shapes) and the
atomic-rename write discipline already used by both artifact writers — a rename generates
a watcher event and readers never see torn content (FR-011). Meets the ≤5 s freshness
criterion with no polling load. Watching the directory catches all writers (Hub pipeline
stages and the agent process, which writes the file directly).

**Alternatives considered**:
- Client-side polling every 3 s — simplest, but constant background load per open view and
  no reuse of the existing realtime channel; rejected.
- Deriving record changes from lifecycle/run-activity events — misses agent-side artifact
  writes that occur between events; rejected as incorrect.
- Server-Sent Events endpoint — second realtime transport alongside SignalR; rejected
  (ADR-001 fixed one transport).

## Decision 5 — Serving the record: parsed metadata + raw markdown body

**Decision**: New endpoint `GET /api/ingest-submissions/{taskId}/task-record` returns JSON:
frontmatter parsed into a structured `metadata` object (reusing `TaskArtifactFrontmatter`)
plus the markdown `body` with the frontmatter block stripped. Missing/unreadable record →
`404` with a problem payload; the UI renders its placeholder from that. The existing
detail endpoint (`GET /api/ingest-submissions/{taskId}`) is unchanged (FR-012).

**Rationale**: The Hub already parses this frontmatter; parsing once server-side spares the
frontend a YAML dependency and keeps the metadata presentation (FR-007) trivially testable
in hermetic integration tests.

**Alternatives considered**: Serving the raw file (`text/markdown`) and parsing frontmatter
client-side — pushes a YAML parser and the torn-frontmatter edge cases into the browser;
rejected.

## Decision 6 — Frontend markdown rendering

**Decision**: Render the body with `marked` (CommonMark + GFM tables/lists) and sanitize the
resulting HTML with `dompurify` before insertion (`{@html}` in a dedicated
`TaskRecordView` Svelte component). Both are plain runtime dependencies, no Svelte
integration layer needed.

**Rationale**: `marked` + `dompurify` is the smallest widely-maintained pairing; the task
record is system/agent-authored but sanitization is still mandatory because agent output
embeds arbitrary source-derived text. No SSR concerns (board is a client-rendered page).

**Alternatives considered**: `svelte-exmarkdown`/`carta-md` — heavier, plugin-oriented;
`markdown-it` — equivalent capability but larger API surface for no benefit here.

## Decision 7 — Detail navigation

**Decision**: The task card's "Details" action becomes an internal SvelteKit route
`/tasks/[taskId]`. The board API's `taskLink` field keeps pointing at the JSON detail
endpoint for machine consumers (contract unchanged, FR-012); the card builds the route
from `taskId` and no longer navigates to `taskLink`.

**Rationale**: Preserves the published API contract while giving operators the rendered
view as the default. A route (vs. modal) gives a shareable URL per task and survives
reloads.

**Alternatives considered**: Repointing `taskLink` at the new route — silently changes an
existing API contract consumed by tests/machine clients; rejected. Modal overlay on the
board — loses deep-linking; rejected.

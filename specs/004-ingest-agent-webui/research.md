# Research: Ingest Agent & Web-UI Channel

**Feature**: 004-ingest-agent-webui | **Date**: 2026-06-24

---

## 1. .NET Agent Process Architecture

**Decision**: .NET 9 Generic Host (`IHostedService`) with embedded Minimal API (`WebApplication`)

**Rationale**: The agent must expose an HTTP API for Hub integration (trigger, feedback, conversation) AND run background services (file watcher, pipeline). The Generic Host provides both through a single process entry point — `IHostedService` for background work, `WebApplication` for HTTP.

**Pattern**:
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<SourceWatcherService>();
builder.Services.AddHostedService<HeartbeatService>();
// ... other DI registrations
var app = builder.Build();
app.MapPost("/ingest/runs", ...);
app.MapGet("/health", ...);
await app.RunAsync();
```

**Alternatives considered**:
- Pure `WebApplication` without Generic Host: loses clean `IHostedService` background worker pattern
- Separate processes for HTTP and watcher: operational complexity without benefit

---

## 2. Claude SDK for .NET

**Decision**: `Anthropic.SDK` NuGet package (community-maintained, official .NET client)

**Rationale**: Provides `AnthropicClient` with full Messages API support. Supports structured output via tool use (JSON schema). Async-first, compatible with .NET 9.

**Usage pattern for structured extraction**:
```csharp
var client = new AnthropicClient(apiKey);
var response = await client.Messages.GetClaudeMessageAsync(
    new MessageParameters {
        Model = model,
        MaxTokens = 2048,
        Messages = [new Message { Role = RoleType.User, Content = prompt }],
        Tools = [structuredOutputTool]  // forces JSON schema output
    });
```

**Retry strategy**: Exponential backoff on 429 (rate limit) and 503 (overload). Max 3 attempts. On final failure, `IngestRecord.Status = failed`.

**Alternatives considered**:
- Semantic Kernel: heavier abstraction layer, not needed for direct API calls
- Raw `HttpClient`: viable but reimplements SDK functionality

---

## 3. File Watching Strategy

**Decision**: .NET `FileSystemWatcher` wrapped in a debounced `IHostedService`

**Rationale**: `FileSystemWatcher` is built-in, handles recursive directory watching via `IncludeSubdirectories = true`. Events fire multiple times per save (OS behavior) — a debounce buffer (300 ms) collapses rapid events into a single trigger.

**Pattern**:
```csharp
_watcher = new FileSystemWatcher(sourceDir) {
    IncludeSubdirectories = true,
    EnableRaisingEvents = true,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
};
_watcher.Created += OnFileEvent;
_watcher.Changed += OnFileEvent;
```

**Alternatives considered**:
- Polling on a timer: less efficient, higher latency
- `Chokidar` (Node): wrong runtime

---

## 4. SHA256 Cache Persistence

**Decision**: SQLite database (`ingest-cache.db`) in the agent's working directory

**Rationale**: Consistent with ADR-008's SQLite-for-operational-state pattern. Survives agent restarts. Provides efficient key-value lookup by file path. Schema is agent-owned and independent from Hub's `grimoire.db`.

**Schema**:
```sql
CREATE TABLE IF NOT EXISTS IngestRecords (
    FilePath TEXT PRIMARY KEY,
    Sha256   TEXT NOT NULL,
    Status   TEXT NOT NULL,  -- 'processed' | 'failed' | 'skipped'
    ProcessedAt TEXT NOT NULL,
    ChunkCount INTEGER NOT NULL DEFAULT 0,
    ErrorMessage TEXT,
    UserCorrections TEXT       -- JSON blob for corrections from conversation
);
CREATE TABLE IF NOT EXISTS ConversationTurns (
    ConversationId TEXT NOT NULL,
    TurnIndex INTEGER NOT NULL,
    Role TEXT NOT NULL,        -- 'agent' | 'user'
    Message TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    PRIMARY KEY (ConversationId, TurnIndex)
);
```

**Alternatives considered**:
- JSON file: no atomic updates, harder concurrent access
- Hub's `grimoire.db`: breaks the separation between agent operational state and Hub operational state

---

## 5. Git Commits via LibGit2Sharp

**Decision**: `LibGit2Sharp` NuGet package for programmatic git operations

**Rationale**: No shell-out required, fully managed, supports staging and committing. Works with credential helpers and SSH keys.

**Pattern**:
```csharp
using var repo = new Repository(repoPath);
Commands.Stage(repo, processedPaths);
var author = new Signature("Grimoire Ingest", "ingest@grimoire", DateTimeOffset.UtcNow);
repo.Commit(message, author, author);
```

**Commit message format**: `ingest: {fileCount} file(s), {chunkCount} chunks — {ISO8601}`

**Alternatives considered**:
- `git` CLI via `Process.Start`: works but fragile in containerized environments
- Custom git pack writer: unnecessary complexity

---

## 6. Hub ↔ Agent Communication Protocol

**Decision**: Direct HTTP (JSON REST) — Hub holds a typed `IngestAgentClient` (`HttpClient`) and calls the agent's Minimal API endpoints directly. No proxy abstraction layer.

**Rationale**: ADR-002 (amended) clarifies that `IAgentWorker` is for in-process agents only. Standalone agents are called via a typed HTTP client registered in the Hub's DI container — simple, transparent, no indirection. The Hub is still the orchestrator; it routes channel requests to the agent and broadcasts agent events back to the frontend via SignalR.

**Communication pattern**:
```
Web-UI Channel (browser)
  ↓ upload / trigger / feedback / conversation message
Hub HTTP endpoints  (src/backend/Grimoire.Api/Ingest/Endpoints/)
  ↓ IngestAgentClient (typed HttpClient)
Agent HTTP API      (src/agents/ingest/Api/)
  ↓ progress / feedback requests / conversation events  (HTTP POST callback)
Hub callback endpoint
  ↓ IngestHub.BroadcastAsync(...)
Frontend (SignalR)
```

**Agent port**: Configurable via `INGEST_HTTP_PORT` env var (default: `5100`). Hub reads `INGEST_AGENT_URL` from its own config.

**Progress reporting**: Agent POSTs progress events to a Hub callback URL (`POST /api/ingest/callback/progress`) after each file. Hub persists to SQLite and broadcasts via `IngestHub`.

**Feedback flow**:
1. Agent detects ambiguous file → POSTs `FeedbackRequest` to Hub callback
2. Hub stores in SQLite, broadcasts `IngestFeedbackRequest` via SignalR
3. User submits decision → `POST /api/ingest/runs/{id}/feedback`
4. Hub forwards via `IngestAgentClient` → agent unblocks processing

**Conversation flow**:
1. Agent finishes file → POSTs `ConversationOpened` to Hub callback (with opening message)
2. Hub stores in SQLite, broadcasts `IngestConversationOpened` via SignalR
3. User message → `POST /api/ingest/conversations/{id}/messages`
4. Hub forwards via `IngestAgentClient` → agent calls Claude SDK → returns response synchronously
5. Hub persists turn, broadcasts `IngestConversationTurn` via SignalR

**Alternatives considered**:
- `IAgentWorker` proxy wrapping HttpClient ("RemoteAgent"): rejected — adds abstraction without value; the typed HTTP client is sufficient and transparent
- gRPC: viable but adds proto tooling complexity with no benefit at this scale
- WebSocket agent→Hub push: more complex lifecycle; HTTP callbacks are simpler and sufficient

---

## 7. SignalR Hub Strategy

**Decision**: Dedicated `IngestHub` SignalR hub at `/hubs/ingest`

**Rationale**: Separates real-time ingest events from general agent lifecycle events already on `/hubs/agents`. Clients interested in ingest subscribe only to `IngestHub`. Prevents event namespace pollution on `AgentHub`.

**Hub structure**: All clients join a broadcast group. No per-user targeting in v1 (FR-019: no auth).

**Alternatives considered**:
- Extend `AgentHub` with ingest events: pollutes existing hub, breaks ADR-009 screaming architecture boundary
- Server-Sent Events: no bidirectional support needed from Hub→client perspective, but SignalR is already the project standard

---

## 8. Ingest Pipeline LLM Prompt Strategy

**Decision**: Single structured-output call per chunk using Claude's tool-use JSON mode

**Per-chunk output schema**:
```json
{
  "summary": "string",
  "topics": ["string"],
  "entities": [{ "name": "string", "type": "string" }],
  "key_claims": ["string"],
  "content_type": "string"
}
```

**Document-level assembly**: Chunk analyses are merged into a single Markdown document written to `wiki/`. YAML frontmatter carries extracted `topics`, `entities`, and `content_type`. Body contains the concatenated summaries and key claims.

**Chunking strategy**: Split by Markdown headings (H1/H2) first; fall back to paragraph boundaries; hard cap at 3000 characters to stay within LLM context comfort zone.

---

## 9. Frontend Routing Strategy

**Decision**: Single-page approach — `IngestPage.svelte` mounted conditionally; no SvelteKit router

**Rationale**: The current frontend is a plain Vite + Svelte SPA (no SvelteKit). The `IngestPage` is exposed as a named view, toggled from a nav link. No new routing infrastructure needed.

**SignalR connection**: `ingestHub.ts` service manages the `@microsoft/signalr` `HubConnection` lifecycle (auto-reconnect, disposal on page unmount via Svelte `onDestroy`).

---

## 10. File Upload Path

**Decision**: Hub receives multipart upload at `POST /api/ingest/upload`, writes to `raw/sources/`, responds 202 Accepted

**Rationale**: Files must land in `raw/sources/` which may be a bind-mounted volume accessible to both Hub and Agent. Hub is the trusted entry point per ADR-006 (all input via Hub). The file watcher picks up the file automatically within the detection window.

**Size limit**: 100 MB (Hub-side), configurable via `INGEST_FILE_SIZE_LIMIT_MB` forwarded to agent for Feedback Request threshold.

**Alternatives considered**:
- Direct upload to agent HTTP API: bypasses Hub; violates ADR-006 hub-spoke pattern
- Pre-signed URL to shared volume: unnecessary complexity in v1

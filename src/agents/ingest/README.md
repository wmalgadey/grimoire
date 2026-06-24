# Ingest Agent

Standalone .NET 9 worker service for autonomous file ingestion, processing, and LLM analysis.

## Quick Start

```bash
dotnet run
```

## Environment Variables

### Required

- `ANTHROPIC_API_KEY` - Claude API key (required for LLM analysis)
- `ANTHROPIC_MODEL` - Claude model ID (default: `claude-3-5-sonnet-20241022`)

### Hub Integration

- `INGEST_HUB_URL` - Hub API base URL (default: `http://localhost:5001`)
- `INGEST_HTTP_PORT` - Agent HTTP port (default: `5100`)

### File Processing

- `INGEST_SOURCE_DIR` - Source directory to watch (default: `raw/sources`)
- `INGEST_FILE_SIZE_LIMIT_MB` - Max file size in MB (default: `10`)

### Git Integration

- `INGEST_GIT_AUTHOR_NAME` - Git commit author (default: `Grimoire Ingest`)
- `INGEST_GIT_AUTHOR_EMAIL` - Git commit email (default: `ingest@grimoire.local`)
- `INGEST_GIT_REPO_PATH` - Git repository root (default: current directory)

### Database

- `INGEST_DB_PATH` - SQLite database path (default: `./ingest-cache.db`)

## Architecture

### File Processing Pipeline

1. **SourceWatcher** - Detects new/modified files in `INGEST_SOURCE_DIR`
2. **Chunker** - Splits documents by Markdown headings, capped at 3000 chars per chunk
3. **LlmAnalyzer** - Calls Claude SDK with structured output (topics, entities, key claims)
4. **Indexer** - Writes Markdown with YAML frontmatter to `wiki/`
5. **IngestGitService** - Auto-commits via LibGit2Sharp

### Caching

- **IngestCache** - SHA256-based deduplication prevents reprocessing
- **SQLite** (`ingest-cache.db`) - Persistent cache of IngestRecord, ConversationTurns, FeedbackRequests

### Communication

- **HubClient** - Registers agent with Hub, sends heartbeats
- **HubReporter** - POSTs progress updates, feedback requests, conversation events

## API Endpoints

### Agent-Side

- `POST /ingest/runs` - Trigger ingest run (with concurrency guard)
- `GET /health` - Health check
- `POST /ingest/runs/{id}/feedback` - Submit user feedback (Process/Skip/Tag)
- `POST /ingest/conversations/{conversationId}/turns` - Send conversation message

### Output

Processed files written to `wiki/` with metadata:

```markdown
---
source: raw/sources/example.pdf
ingested_at: 2026-06-24T12:34:56Z
topics: [topic1, topic2]
entities: [entity1, entity2]
content_type: document
chunk_count: 5
---

# Content indexed from file
```

## Development

### Build

```bash
dotnet build
```

### Run Tests

```bash
dotnet test
```

### Logs

JSON console logging to stdout (structured, machine-parseable).

### Observability

- OpenTelemetry ActivitySource: `Grimoire.Ingest`
- Metrics: `grimoire.ingest.*` counters and gauges
- Trace spans: `ingest.run`, `ingest.file.process`, `ingest.pipeline.*`

## Integration with Hub

Agent can run standalone (no Hub needed) or integrated with Grimoire Hub for real-time UI feedback:

- Hub relays upload triggers → Agent HTTP POST
- Agent reports progress → Hub broadcasts via SignalR
- Hub manages run lifecycle, feedback requests, conversation turns

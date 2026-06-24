# Ingest Channel (Hub)

Hub-side domain for Ingest Agent integration. Provides real-time progress updates, feedback requests, and conversation management via SignalR.

## Architecture

### Services

- **IngestAgentClient** - Typed HttpClient for agent communication (trigger, feedback, conversation)
- **IngestOrchestrationHandler** - Orchestrates Hub-agent workflows (trigger, feedback, callbacks)
- **IngestRepository** - SQLite persistence (IngestRuns, ConversationTurns, FeedbackRequests)

### SignalR Hub

**IngestHub** at `/hubs/ingest` broadcasts real-time events:

- `IngestRunStarted` - Run initiated
- `IngestProgress` - File processing progress
- `IngestLogEntry` - Structured log events
- `IngestFeedbackRequest` - User feedback needed
- `IngestRunCompleted` - Batch summary available
- `IngestConversationOpened` - Conversation ready for input
- `IngestConversationTurn` - New message in conversation

## Database Schema

### IngestRuns

Tracks batch run metadata and results.

```sql
CREATE TABLE IngestRuns (
  RunId TEXT PRIMARY KEY,
  Status TEXT NOT NULL,
  StartedAt TEXT NOT NULL,
  CompletedAt TEXT,
  TotalFiles INTEGER,
  ProcessedCount INTEGER,
  FailedCount INTEGER,
  SkippedCount INTEGER,
  TotalChunks INTEGER
);
```

### ConversationTurns

Persists conversation history.

```sql
CREATE TABLE ConversationTurns (
  ConversationId TEXT NOT NULL,
  TurnIndex INTEGER NOT NULL,
  Role TEXT NOT NULL,
  Message TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  PRIMARY KEY (ConversationId, TurnIndex)
);
```

## Endpoints

### Upload & Trigger

- `POST /api/ingest/upload` - Accept multipart files, write to `raw/sources/{subDirectory}/`
- `POST /api/ingest/trigger` - Trigger manual ingest run (409 if run active)
- `GET /api/ingest/runs/{runId}` - Get run metadata

### Feedback & Conversation

- `POST /api/ingest/runs/{runId}/feedback` - Submit user feedback (relay to agent)
- `POST /api/ingest/conversations/{conversationId}/messages` - Send conversation message
- `GET /api/ingest/conversations/{conversationId}` - Retrieve conversation history

### Agent Callbacks

- `POST /api/ingest/callbacks/progress` - Agent POSTs progress updates
- `POST /api/ingest/callbacks/feedback-request` - Agent requests user feedback
- `POST /api/ingest/callbacks/conversation-opened` - Agent opens conversation
- `POST /api/ingest/callbacks/conversation-turn` - Agent sends conversation turn
- `POST /api/ingest/callbacks/run-completed` - Agent reports batch summary

## Integration

Agent must be configured with Hub URL:

```bash
export INGEST_HUB_URL=http://localhost:5001
```

Hub must know agent URL:

```bash
export IngestAgent__BaseUrl=http://localhost:5100
```

## Testing

Integration tests in `Grimoire.Api.Tests/Integration/Ingest/`:

- Upload endpoint accepts files
- Trigger endpoint enforces concurrency
- Feedback endpoint relays to agent
- Conversation endpoint persists turns
- RunCompleted event broadcasts summary

Run: `dotnet test Grimoire.Api.Tests.csproj -k Ingest`

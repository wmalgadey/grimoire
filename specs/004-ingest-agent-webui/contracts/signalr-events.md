# Contract: IngestHub SignalR Events

**Hub URL**: `/hubs/ingest`
**Client library**: `@microsoft/signalr`
**Auth**: None (v1)

Clients connect and receive broadcast events for the current ingest session. All events are broadcast to all connected clients (no per-user targeting in v1).

---

## Connection

```typescript
const connection = new HubConnectionBuilder()
  .withUrl("/hubs/ingest")
  .withAutomaticReconnect()
  .build();

await connection.start();
```

---

## Server → Client Events

### `IngestRunStarted`

Fired when an ingest run begins.

```typescript
connection.on("IngestRunStarted", (payload: {
  runId: string;
  startedAt: string;       // ISO 8601 UTC
  fileCount: number;       // estimated files to process
}) => { /* ... */ });
```

---

### `IngestProgress`

Fired after each file is evaluated (processed, failed, or skipped).

```typescript
connection.on("IngestProgress", (payload: {
  runId: string;
  filePath: string;
  status: "Processed" | "Failed" | "Skipped";
  chunkCount: number;
  durationMs: number;
  processedSoFar: number;  // running count for progress bar
  totalFiles: number;
  errorMessage?: string;   // present when status = "Failed"
}) => { /* ... */ });
```

---

### `IngestLogEntry`

Fired for significant log events during a run (current file, stage transitions).

```typescript
connection.on("IngestLogEntry", (payload: {
  runId: string;
  level: "info" | "warn" | "error";
  message: string;
  timestamp: string;       // ISO 8601 UTC
}) => { /* ... */ });
```

---

### `IngestFeedbackRequest`

Fired when the agent needs user input on an ambiguous file. Processing for this file is blocked until `IngestFeedbackResolved` is sent back via the Hub HTTP API.

```typescript
connection.on("IngestFeedbackRequest", (payload: {
  runId: string;
  requestId: string;
  filePath: string;
  reason: "UnknownFormat" | "Oversized" | "MissingMetadata";
  options: Array<{
    action: "process" | "skip" | "tag";
    label: string;
  }>;
}) => { /* ... */ });
```

---

### `IngestRunCompleted`

Fired when a run finishes (success or failure).

```typescript
connection.on("IngestRunCompleted", (payload: {
  runId: string;
  status: "Completed" | "Failed";
  completedAt: string;     // ISO 8601 UTC
  summary: {
    totalFiles: number;
    processedCount: number;
    failedCount: number;
    skippedCount: number;
    totalChunks: number;
    durationMs: number;
    files: Array<{
      filePath: string;
      status: "Processed" | "Failed" | "Skipped";
      chunkCount: number;
      durationMs: number;
      conversationId?: string;   // present when status = "Processed"
    }>;
  };
}) => { /* ... */ });
```

---

### `IngestConversationOpened`

Fired after a file is processed, when the agent opens an IngestConversation.

```typescript
connection.on("IngestConversationOpened", (payload: {
  conversationId: string;
  runId: string;
  filePath: string;
  openingMessage: string;  // agent-generated document summary + invitation
  createdAt: string;       // ISO 8601 UTC
}) => { /* ... */ });
```

---

### `IngestConversationTurn`

Fired when the agent responds to a user message in a conversation.

```typescript
connection.on("IngestConversationTurn", (payload: {
  conversationId: string;
  turnIndex: number;
  role: "agent" | "user";
  message: string;
  createdAt: string;       // ISO 8601 UTC
}) => { /* ... */ });
```

---

## Client → Server Messages

There are no client-to-server SignalR messages for the IngestHub. All user actions (trigger, feedback, conversation message) are submitted via the Hub HTTP API and broadcast back as server events.

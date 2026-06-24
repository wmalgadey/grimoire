namespace Grimoire.Api.Ingest.Models.SignalREvents;

public record IngestRunStarted(
    string RunId,
    string StartedAt,
    int FileCount
);

public record IngestProgress(
    string RunId,
    string FilePath,
    string Status,
    int ChunkCount,
    int DurationMs,
    int ProcessedSoFar,
    int TotalFiles,
    string? ErrorMessage = null
);

public record IngestLogEntry(
    string RunId,
    string Level,
    string Message,
    string Timestamp
);

public record IngestFeedbackRequest(
    string RunId,
    string RequestId,
    string FilePath,
    string Reason,
    object[] Options
);

public record IngestRunCompleted(
    string RunId,
    string Status,
    string CompletedAt,
    object Summary
);

public record IngestConversationOpened(
    string ConversationId,
    string RunId,
    string FilePath,
    string OpeningMessage,
    string CreatedAt
);

public record IngestConversationTurn(
    string ConversationId,
    int TurnIndex,
    string Role,
    string Message,
    string CreatedAt
);

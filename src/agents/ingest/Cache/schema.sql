CREATE TABLE IF NOT EXISTS IngestRecords (
    FilePath TEXT PRIMARY KEY,
    Sha256 TEXT NOT NULL,
    Status TEXT NOT NULL,
    ProcessedAt TEXT NOT NULL,
    ChunkCount INTEGER NOT NULL DEFAULT 0,
    ErrorMessage TEXT,
    UserCorrections TEXT,
    FeedbackAction TEXT,
    FeedbackTag TEXT
);

CREATE TABLE IF NOT EXISTS ConversationTurns (
    ConversationId TEXT NOT NULL,
    TurnIndex INTEGER NOT NULL,
    Role TEXT NOT NULL,
    Message TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    PRIMARY KEY (ConversationId, TurnIndex)
);

CREATE TABLE IF NOT EXISTS FeedbackRequests (
    RequestId TEXT PRIMARY KEY,
    RunId TEXT NOT NULL,
    FilePath TEXT NOT NULL,
    Reason TEXT NOT NULL,
    RaisedAt TEXT NOT NULL,
    ResolvedAt TEXT
);

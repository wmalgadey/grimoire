-- SQLite schema for Hub Foundation + Agent Lifecycle feature
-- Operational state persistence (not domain content)

CREATE TABLE IF NOT EXISTS AgentDescriptors (
    AgentId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Status INTEGER NOT NULL,  -- enum: Unregistered=0, Starting=1, Running=2, Stopping=3, Stopped=4, Faulted=5
    Capabilities TEXT,  -- JSON array of capabilities
    RegisteredAt TEXT NOT NULL,  -- ISO 8601 UTC
    LastHealthCheckAt TEXT  -- ISO 8601 UTC, nullable
);

CREATE TABLE IF NOT EXISTS AgentJobs (
    JobId TEXT PRIMARY KEY,
    AgentId TEXT NOT NULL,
    Payload TEXT NOT NULL,  -- JSON payload
    Status INTEGER NOT NULL,  -- enum: Pending=0, Running=1, Completed=2, Failed=3
    CreatedAt TEXT NOT NULL,  -- ISO 8601 UTC
    StartedAt TEXT,  -- ISO 8601 UTC, nullable
    CompletedAt TEXT,  -- ISO 8601 UTC, nullable
    FailedAt TEXT,  -- ISO 8601 UTC, nullable
    ErrorMessage TEXT,  -- nullable
    FOREIGN KEY (AgentId) REFERENCES AgentDescriptors(AgentId) ON DELETE CASCADE
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_AgentJobs_AgentId ON AgentJobs(AgentId);
CREATE INDEX IF NOT EXISTS idx_AgentJobs_Status ON AgentJobs(Status);
CREATE INDEX IF NOT EXISTS idx_AgentJobs_CreatedAt ON AgentJobs(CreatedAt);

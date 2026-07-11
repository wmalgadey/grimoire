namespace Grimoire.Domain.Ingest;

public enum IngestTaskStatus
{
    // Ingest-submission phase (Hub-owned, feature 003): received -> converting -> queued|failed.
    Received,
    Converting,
    // Ingest-run phase (agent-owned, unchanged since 001/002): queued -> running -> completed|failed.
    Queued,
    Running,
    Completed,
    Failed,
}

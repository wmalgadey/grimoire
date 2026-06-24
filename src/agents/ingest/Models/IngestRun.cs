namespace Grimoire.Ingest.Models;

public class IngestRun
{
    public string RunId { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public int TotalFiles { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalChunks { get; set; }
    public List<IngestFileResult> FileResults { get; } = new();
}

public class IngestFileResult
{
    public string FilePath { get; set; } = "";
    public string Status { get; set; } = "";
    public int ChunkCount { get; set; }
    public long DurationMs { get; set; }
    public string? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum RunStatus { Running, Completed, Failed }

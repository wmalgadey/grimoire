namespace Grimoire.Api.Ingest.Models;

public class IngestRunRecord
{
    public string RunId { get; set; } = Guid.NewGuid().ToString();
    public string Status { get; set; } = "pending"; // pending, running, completed, failed
    public string StartedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string? CompletedAt { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int FailedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public List<FileProcessingResult> FileResults { get; set; } = new();
}

public class FileProcessingResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // processed, failed, skipped, feedback_requested
    public int ChunkCount { get; set; }
    public long DurationMs { get; set; }
    public string? FeedbackReason { get; set; }
    public string? ErrorMessage { get; set; }
}

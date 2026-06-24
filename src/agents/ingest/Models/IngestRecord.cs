namespace Grimoire.Ingest.Models;

public class IngestRecord
{
    public string FilePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public IngestStatus Status { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UserCorrections { get; set; }
    public string? FeedbackAction { get; set; }
    public string? FeedbackTag { get; set; }
}

public enum IngestStatus { Processed, Failed, Skipped }

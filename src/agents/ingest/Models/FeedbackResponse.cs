namespace Grimoire.Ingest.Models;

public class FeedbackResponse
{
    public string RequestId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public FeedbackAction Action { get; set; }
    public string? TagValue { get; set; }
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum FeedbackAction { Process, Skip, Tag }

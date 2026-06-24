namespace Grimoire.Ingest.Models;

public class FeedbackRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string RunId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public FeedbackReason Reason { get; set; }
    public DateTimeOffset RaisedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum FeedbackReason { UnknownFormat, Oversized, MissingMetadata }

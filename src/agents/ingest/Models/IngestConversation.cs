using Grimoire.Ingest.Pipeline;

namespace Grimoire.Ingest.Models;

public class IngestConversation
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = "";
    public string RunId { get; set; } = "";
    public string OpeningMessage { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DismissedAt { get; set; }
    public List<ConversationTurn> Turns { get; } = new();
    // In-memory context for LLM calls
    public string DocumentContent { get; set; } = "";
    public List<ChunkAnalysis> ChunkAnalyses { get; set; } = new();
}

public class ConversationTurn
{
    public string ConversationId { get; set; } = "";
    public int TurnIndex { get; set; }
    public TurnRole Role { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum TurnRole { Agent, User }

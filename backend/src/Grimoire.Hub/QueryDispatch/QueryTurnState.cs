using Grimoire.Hub.AgentDispatch;

namespace Grimoire.Hub.QueryDispatch;

/// <summary>
/// Terminal-event metadata a Query agent run reports so the Hub can finalize the Query
/// Run Artifact entirely from the event stream (R3, FR-016) — absent on a failure that
/// occurred before instructions finished loading.
/// </summary>
public sealed record QueryTurnCompletionMetadata(
    string? SystemPromptSha256,
    string? PolicyPath,
    int? PolicyVersion,
    string? PolicySha256,
    string? Model,
    int? TurnsUsed,
    IReadOnlyList<AgentRunEventDeniedAction> DeniedActions);

/// <summary>Terminal-inclusive state machine for one Query Turn (data-model.md QueryTurn).</summary>
public enum QueryTurnStatus
{
    Running,
    Completed,
    Interrupted,
    Failed,
}

/// <summary>
/// Hub-side authoritative view of one Query Turn — what <see cref="QueryRunCoordinator"/>
/// tracks in memory while a turn is live, and what <c>GET /api/query-turns/{turnId}</c>
/// serves. Answer text accumulates from <c>answer_chunk</c> events as they arrive.
/// </summary>
public sealed class QueryTurnState
{
    private readonly System.Text.StringBuilder _answer = new();
    private readonly Lock _lock = new();

    public QueryTurnState(string turnId, string conversationId, int position, string prompt, DateTimeOffset startedAt)
    {
        TurnId = turnId;
        ConversationId = conversationId;
        Position = position;
        Prompt = prompt;
        StartedAt = startedAt;
        Status = QueryTurnStatus.Running;
    }

    public string TurnId { get; }
    public string ConversationId { get; }
    public int Position { get; }
    public string Prompt { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public QueryTurnStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public QueryTurnCompletionMetadata? CompletionMetadata { get; private set; }
    private int _sequence;

    public string Answer
    {
        get { lock (_lock) { return _answer.ToString(); } }
    }

    /// <summary>Appends a streamed delta and returns its 1-based monotonic sequence number.</summary>
    public int AppendAnswerChunk(string text)
    {
        lock (_lock)
        {
            _answer.Append(text);
            return ++_sequence;
        }
    }

    public bool IsTerminal => Status is QueryTurnStatus.Completed or QueryTurnStatus.Interrupted or QueryTurnStatus.Failed;

    /// <summary>First-transition-wins terminal state change; returns false if already terminal.</summary>
    public bool TryTransitionTo(
        QueryTurnStatus status, string? failureReason, DateTimeOffset completedAt, QueryTurnCompletionMetadata? metadata = null)
    {
        lock (_lock)
        {
            if (IsTerminal)
            {
                return false;
            }

            Status = status;
            FailureReason = failureReason;
            CompletedAt = completedAt;
            CompletionMetadata = metadata;
            return true;
        }
    }
}

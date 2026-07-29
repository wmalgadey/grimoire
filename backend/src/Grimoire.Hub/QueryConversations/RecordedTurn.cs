using Grimoire.Hub.AgentDispatch;

namespace Grimoire.Hub.QueryConversations;

/// <summary>
/// One denied tool action as recorded in a turn's bookkeeping block
/// (data-model.md Turn Bookkeeping, SC-002) — same shape the retired Query Run
/// Artifact recorded and the ADR-006 terminal-event metadata reports.
/// </summary>
public sealed record RecordedDeniedAction(
    string Action,
    string RequestedTarget,
    string CanonicalTarget,
    string Reason,
    int Turn);

/// <summary>
/// One terminal turn of a Conversation Record (data-model.md "Recorded Turn"):
/// the full Turn Bookkeeping plus the verbatim prompt/answer transcript bodies.
/// Produced by the coordinator on a turn's terminal transition (append path) and by
/// the contract parser (context-recovery path).
/// </summary>
public sealed record RecordedTurn(
    string TurnId,
    int Position,
    string State,
    string? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Model,
    int? TurnsUsed,
    string? InstructionFilePath,
    string? InstructionFileSha256,
    string? PolicyPath,
    int? PolicyVersion,
    string? PolicySha256,
    IReadOnlyList<RecordedDeniedAction> DeniedActions,
    string Prompt,
    string Answer)
{
    /// <summary>
    /// The <c>{ position, prompt, answer, state }</c> context projection — exactly the
    /// <see cref="QueryPriorTurn"/> shape the launcher port already carries, so the
    /// agent-facing contract is unchanged (research.md R7, SC-005).
    /// </summary>
    public QueryPriorTurn ToPriorTurn() => new(Position, Prompt, Answer, State);

    /// <summary>
    /// Value equality including the denied-actions list (the default record equality
    /// would compare the list by reference) — writer→parser round-trip tests compare
    /// whole turns.
    /// </summary>
    public bool Equals(RecordedTurn? other) =>
        other is not null &&
        TurnId == other.TurnId &&
        Position == other.Position &&
        State == other.State &&
        FailureReason == other.FailureReason &&
        StartedAt.Equals(other.StartedAt) &&
        Nullable.Equals(CompletedAt, other.CompletedAt) &&
        Model == other.Model &&
        TurnsUsed == other.TurnsUsed &&
        InstructionFilePath == other.InstructionFilePath &&
        InstructionFileSha256 == other.InstructionFileSha256 &&
        PolicyPath == other.PolicyPath &&
        PolicyVersion == other.PolicyVersion &&
        PolicySha256 == other.PolicySha256 &&
        Prompt == other.Prompt &&
        Answer == other.Answer &&
        DeniedActions.SequenceEqual(other.DeniedActions);

    public override int GetHashCode() => HashCode.Combine(TurnId, Position, State, Prompt, Answer);
}

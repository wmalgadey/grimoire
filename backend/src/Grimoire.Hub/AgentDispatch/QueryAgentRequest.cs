namespace Grimoire.Hub.AgentDispatch;

/// <summary>One prior turn of the conversation, as supplied by the client (research.md R6, FR-009).</summary>
public sealed record QueryPriorTurn(int Position, string Prompt, string Answer, string State);

/// <summary>
/// One Query Turn's process-spawn request (data-model.md QueryAgentRequest, ADR-011).
/// Flows through the same <see cref="IAgentProcessLauncher"/> port Ingest uses, via a
/// dedicated <c>StartAsync</c> overload — the port itself is unchanged (ADR-011).
/// </summary>
public sealed record QueryAgentRequest(
    string TurnId,
    string ConversationId,
    string Prompt,
    IReadOnlyList<QueryPriorTurn> PriorTurns,
    string WikiRoot,
    string PagesDir,
    string IndexPath,
    string LogPath,
    string SystemPromptPath,
    string PolicyPath);

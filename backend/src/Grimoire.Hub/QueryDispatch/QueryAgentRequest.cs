using Grimoire.Hub.AgentDispatch;
namespace Grimoire.Hub.QueryDispatch;

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
    string ContentRoot,
    string IndexPath,
    string LogPath,
    string SystemPromptPath,
    string PolicyPath,
    // ADR-015 (012-query-synthesis-writes): the cross-process write-coordination lock
    // directory (contracts/query-write-scope-and-coordination.md §4), supplied the same
    // way as WikiRoot/PolicyPath — a single Hub-resolved composition point
    // (ResolvedGrimoirePaths.WriteLocksDir, ADR-009), not agent-discovered.
    string WriteLocksDir);

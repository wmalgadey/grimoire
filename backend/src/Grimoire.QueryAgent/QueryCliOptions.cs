namespace Grimoire.QueryAgent;

/// <summary>
/// CLI options for one Query Turn's process spawn (ADR-002 pattern, data-model.md
/// QueryAgentRequest). Unlike Ingest, there is no task-artifact path — the Query agent
/// process never writes anything (R3/ADR-011); the Hub owns 100% of Query Run Artifact
/// persistence from the NDJSON events this process emits on stdout.
/// </summary>
public sealed record QueryCliOptions(
    string TurnId,
    string WikiRoot,
    string PagesDir,
    string IndexPath,
    string LogPath,
    string SystemPromptPath,
    string PolicyPath,
    int HeartbeatSeconds = 10);

/// <summary>One prior turn of the conversation, supplied by the client (research.md R6).</summary>
public sealed record PriorTurnInput(int Position, string Prompt, string Answer, string State);

/// <summary>
/// The stdin JSON payload for one Query Turn: the Query Prompt and (for follow-ups) the
/// conversation's prior turns, including partial answers of interrupted turns (FR-009).
/// Read from stdin rather than a CLI arg since conversation history has no practical
/// length bound (mirrors Ingest's existing pasted-text-via-stdin convention).
/// </summary>
public sealed record QueryConversationInput(string Prompt, IReadOnlyList<PriorTurnInput>? PriorTurns);

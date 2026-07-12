using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Hub.AgentDispatch;

/// <summary>
/// One Agent Run Event received from the agent's stdout event channel
/// (contracts/agent-run-events.md, ADR-008). Carries loop mechanics only —
/// never wiki-content judgment beyond the verbatim completion summary.
/// </summary>
public sealed record AgentRunEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("modelTurns")] int? ModelTurns = null,
    [property: JsonPropertyName("toolCalls")] int? ToolCalls = null,
    [property: JsonPropertyName("toolCallsByName")] IReadOnlyDictionary<string, int>? ToolCallsByName = null,
    [property: JsonPropertyName("currentAction")] string? CurrentAction = null,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    public const string TypeStarted = "started";
    public const string TypeHeartbeat = "heartbeat";
    public const string TypeActivity = "activity";
    public const string TypeCompleted = "completed";
    public const string TypeFailed = "failed";

    public bool IsTerminal => Type is TypeCompleted or TypeFailed;
}

/// <summary>
/// Tolerant NDJSON parser for the stdout event channel: non-JSON lines, JSON without a
/// valid type/taskId, and unknown fields never fail the run — only the liveness window
/// does (ADR-008).
/// </summary>
public static class AgentRunEventParser
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> _knownTypes =
    [
        AgentRunEvent.TypeStarted,
        AgentRunEvent.TypeHeartbeat,
        AgentRunEvent.TypeActivity,
        AgentRunEvent.TypeCompleted,
        AgentRunEvent.TypeFailed,
    ];

    public static AgentRunEvent? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        AgentRunEvent? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AgentRunEvent>(line, _options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null ||
            string.IsNullOrWhiteSpace(parsed.Type) ||
            string.IsNullOrWhiteSpace(parsed.TaskId) ||
            !_knownTypes.Contains(parsed.Type))
        {
            return null;
        }

        return parsed;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Hub.AgentDispatch;

/// <summary>One denied tool action, as reported on a Query terminal event (data-model.md DeniedActionRecord).</summary>
public sealed record AgentRunEventDeniedAction(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("requestedTarget")] string RequestedTarget,
    [property: JsonPropertyName("canonicalTarget")] string CanonicalTarget,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("turn")] int Turn);

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
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("text")] string? Text = null,
    // ADR-011: terminal-event metadata a write-less agent process (Query) reports so the
    // Hub can finalize its own Query Run Artifact entirely from the event stream (R3).
    [property: JsonPropertyName("systemPromptSha256")] string? SystemPromptSha256 = null,
    [property: JsonPropertyName("policyPath")] string? PolicyPath = null,
    [property: JsonPropertyName("policyVersion")] int? PolicyVersion = null,
    [property: JsonPropertyName("policySha256")] string? PolicySha256 = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("turnsUsed")] int? TurnsUsed = null,
    [property: JsonPropertyName("deniedActions")] IReadOnlyList<AgentRunEventDeniedAction>? DeniedActions = null)
{
    public const string TypeStarted = "started";
    public const string TypeHeartbeat = "heartbeat";
    public const string TypeActivity = "activity";
    public const string TypeCompleted = "completed";
    public const string TypeFailed = "failed";

    /// <summary>ADR-011: streamed answer delta (contracts/query-run-events.md). Never emitted by Ingest.</summary>
    public const string TypeAnswerChunk = "answer_chunk";

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
        AgentRunEvent.TypeAnswerChunk,
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

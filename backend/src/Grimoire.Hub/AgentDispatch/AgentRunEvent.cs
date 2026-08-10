using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Hub.AgentDispatch;

/// <summary>
/// One agent-proposed remediation action, as reported on the Lint run's terminal
/// <c>completed</c> event (015-lint-board-parity contracts/remediation-lifecycle-events.md,
/// ADR-008/ADR-018 event-vocabulary extension). All fields are agent-authored free text,
/// harness-opaque (Principle V); <see cref="TargetPath"/> is an optional hint, never
/// validated or enforced by the Hub.
/// </summary>
public sealed record AgentRunEventProposedAction(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("targetPath")] string? TargetPath = null);

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
    [property: JsonPropertyName("deniedActions")] IReadOnlyList<AgentRunEventDeniedAction>? DeniedActions = null,
    // ADR-015 (012-query-synthesis-writes): canonical paths of pages this turn created
    // (RunCompletionMetadata.CreatedArtifacts, contracts/query-write-scope-and-coordination.md
    // §5) — null/empty when the turn created nothing.
    [property: JsonPropertyName("createdPages")] IReadOnlyList<string>? CreatedPages = null,
    // ADR-018 (015-lint-board-parity): remediation actions the Lint agent judged
    // actionable, riding the lint-run terminal event like deniedActions/createdPages.
    // Null/empty ⇒ no tasks created. Entry-level tolerance: a malformed entry is
    // skipped, never failing the whole event (see the converter below).
    [property: JsonPropertyName("proposedActions")]
    [property: JsonConverter(typeof(TolerantProposedActionListConverter))]
    IReadOnlyList<AgentRunEventProposedAction>? ProposedActions = null,
    // ADR-018: the remediation-execution mode's re-verification judgment on its terminal
    // completed event — "applied" | "not_applicable" (reason reuses the existing Reason
    // field). Transported only, never computed by the harness (Principle V).
    [property: JsonPropertyName("remediationOutcome")] string? RemediationOutcome = null,
    // ADR-023 (022-align-wiki-structure, Phase 5, FR-017/SC-011): the ordered list of
    // reserved harness-surface names this run was permitted to read
    // (RunCompletionMetadata.GrantedHarnessSurfaces). Null/empty ⇒ none granted
    // (deny-by-default) — DO NOT rename the pre-existing createdPages field alongside
    // this one; that rename is Phase 6's job (contracts/terminology-rename-map.md).
    [property: JsonPropertyName("grantedHarnessSurfaces")] IReadOnlyList<string>? GrantedHarnessSurfaces = null)
{
    public const string TypeStarted = "started";
    public const string TypeHeartbeat = "heartbeat";
    public const string TypeActivity = "activity";
    public const string TypeCompleted = "completed";
    public const string TypeFailed = "failed";

    /// <summary>ADR-011: streamed answer delta (contracts/query-run-events.md). Never emitted by Ingest.</summary>
    public const string TypeAnswerChunk = "answer_chunk";

    /// <summary>ADR-018: remediation-execution re-verification outcomes (contracts/remediation-lifecycle-events.md).</summary>
    public const string RemediationOutcomeApplied = "applied";
    public const string RemediationOutcomeNotApplicable = "not_applicable";

    public bool IsTerminal => Type is TypeCompleted or TypeFailed;
}

/// <summary>
/// Entry-tolerant reader for the <c>proposedActions</c> list (ADR-008's tolerance
/// philosophy applied one level down): a malformed entry — not an object, missing or
/// non-string <c>title</c>/<c>description</c> — is skipped rather than failing the whole
/// terminal event, so one bad proposal can never cost the run its completion. A value
/// that is not an array at all deserializes to <c>null</c> (treated as absent).
/// </summary>
public sealed class TolerantProposedActionListConverter : JsonConverter<IReadOnlyList<AgentRunEventProposedAction>?>
{
    public override IReadOnlyList<AgentRunEventProposedAction>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var actions = new List<AgentRunEventProposedAction>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = TryGetNonEmptyString(element, "title");
            var description = TryGetNonEmptyString(element, "description");
            if (title is null || description is null)
            {
                continue;
            }

            var targetPath = TryGetNonEmptyString(element, "targetPath");
            actions.Add(new AgentRunEventProposedAction(title, description, targetPath));
        }

        return actions;
    }

    public override void Write(
        Utf8JsonWriter writer, IReadOnlyList<AgentRunEventProposedAction>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var action in value)
        {
            writer.WriteStartObject();
            writer.WriteString("title", action.Title);
            writer.WriteString("description", action.Description);
            if (action.TargetPath is not null)
            {
                writer.WriteString("targetPath", action.TargetPath);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string? TryGetNonEmptyString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           property.GetString() is { } value &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
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

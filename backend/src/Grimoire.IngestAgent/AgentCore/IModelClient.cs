namespace Grimoire.IngestAgent.AgentCore;

/// <summary>
/// One tool-use request issued by the model.
/// </summary>
public sealed record ToolUseRequest(
    string ToolUseId,
    string ToolName,
    string InputJson);

/// <summary>
/// Canonical stop reasons returned by the model contract.
/// </summary>
public enum ModelStopReason
{
    Unknown = 0,
    EndTurn,
    ToolUse,
    MaxTokens,
    PauseTurn,
    StopSequence,
    Refusal,
}

/// <summary>
/// Normalization helpers for converting between SDK values and protocol values.
/// </summary>
public static class ModelStopReasonContract
{
    public static string ToProtocolString(this ModelStopReason stopReason)
        => stopReason switch
        {
            ModelStopReason.EndTurn => "end_turn",
            ModelStopReason.ToolUse => "tool_use",
            ModelStopReason.MaxTokens => "max_tokens",
            ModelStopReason.PauseTurn => "pause_turn",
            ModelStopReason.StopSequence => "stop_sequence",
            ModelStopReason.Refusal => "refusal",
            _ => "unknown",
        };

    public static ModelStopReason FromRawValue(object? stopReason)
    {
        var raw = stopReason?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ModelStopReason.Unknown;
        }

        if (Enum.TryParse<ModelStopReason>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var normalized = raw.Trim().Replace('-', '_');
        return normalized.ToLowerInvariant() switch
        {
            "endturn" or "end_turn" => ModelStopReason.EndTurn,
            "tooluse" or "tool_use" => ModelStopReason.ToolUse,
            "maxtokens" or "max_tokens" => ModelStopReason.MaxTokens,
            "pauseturn" or "pause_turn" => ModelStopReason.PauseTurn,
            "stopsequence" or "stop_sequence" => ModelStopReason.StopSequence,
            "refusal" => ModelStopReason.Refusal,
            _ => ModelStopReason.Unknown,
        };
    }
}

/// <summary>
/// One turn response from the model.
/// <see cref="StopReason"/> is always a normalized enum value with protocol
/// conversion handled by <see cref="ModelStopReasonContract"/>.
/// </summary>
public sealed record ModelTurn(
    string? AssistantText,
    IReadOnlyList<ToolUseRequest> ToolUseRequests,
    ModelStopReason StopReason,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// One message in the conversation history, representing either a user turn
/// (source context or tool results) or an assistant turn.
/// </summary>
public sealed record ConversationMessage(string Role, string Content);

/// <summary>
/// Seam between the agent loop and the underlying model API. Implementations:
/// <list type="bullet">
///   <item><see cref="AnthropicModelClient"/> — production Anthropic Messages API</item>
///   <item><c>FakeModelClient</c> — scripted test double for hermetic harness tests</item>
/// </list>
/// </summary>
public interface IModelClient
{
    /// <summary>The model identifier this client sends with every request.</summary>
    string ModelId { get; }

    /// <summary>
    /// Sends the current conversation state to the model and returns the next turn.
    /// </summary>
    /// <param name="systemPrompt">The verbatim instruction set loaded by the harness.</param>
    /// <param name="conversation">All prior messages in the conversation (user + assistant).</param>
    /// <param name="tools">Tool definitions available to the model on this turn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken);
}

/// <summary>JSON schema and metadata for a tool offered to the model.</summary>
public sealed record ToolDefinition(string Name, string Description, string InputSchemaJson);

using System.Linq;

namespace Grimoire.AgentRuntime.Core;

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
        var lower = normalized.ToLowerInvariant();

        // Be defensive: SDK unions can stringify to wrapper/object forms.
        if (lower.Contains("end_turn") || lower.Contains("endturn"))
        {
            return ModelStopReason.EndTurn;
        }

        if (lower.Contains("tool_use") || lower.Contains("tooluse"))
        {
            return ModelStopReason.ToolUse;
        }

        if (lower.Contains("max_tokens") || lower.Contains("maxtokens"))
        {
            return ModelStopReason.MaxTokens;
        }

        if (lower.Contains("pause_turn") || lower.Contains("pauseturn"))
        {
            return ModelStopReason.PauseTurn;
        }

        if (lower.Contains("stop_sequence") || lower.Contains("stopsequence"))
        {
            return ModelStopReason.StopSequence;
        }

        if (lower.Contains("refusal"))
        {
            return ModelStopReason.Refusal;
        }

        return lower switch
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
/// The provider's own account of why it declined a request: the <c>stop_details</c> that
/// accompany <c>stop_reason: "refusal"</c>. A refusal is a normal HTTP 200 outcome, not a
/// transport or protocol error, and it is the one class of model rejection that arrives
/// with a machine-readable reason — so the reason is carried on the turn rather than
/// dropped, and reaches the operator the way a provider error already does (#119).
/// Both fields are optional: the provider may send a refusal with no details at all.
/// </summary>
public sealed record ModelRefusalDetails(string? Category, string? Explanation);

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
    int OutputTokens,
    ModelRefusalDetails? Refusal = null);

/// <summary>
/// One message in the conversation history, representing either a user turn
/// (source context or tool results) or an assistant turn.
/// </summary>
public sealed class ConversationMessage
{
    public ConversationMessage(string role, string content)
        : this(role, [new ConversationTextBlock(content)])
    {
    }

    public ConversationMessage(string role, IReadOnlyList<ConversationContentBlock> contentBlocks)
    {
        Role = role;
        ContentBlocks = contentBlocks;
    }

    public string Role { get; }

    public IReadOnlyList<ConversationContentBlock> ContentBlocks { get; }

    // Kept for test/debug compatibility where only text blocks are asserted.
    public string Content => string.Join(
        "\n",
        ContentBlocks
            .OfType<ConversationTextBlock>()
            .Select(static block => block.Text));
}

/// <summary>One typed content block in a conversation message.</summary>
public abstract record ConversationContentBlock;

/// <summary>Plain text block for user/assistant messages.</summary>
public sealed record ConversationTextBlock(string Text) : ConversationContentBlock;

/// <summary>Assistant-declared tool_use block.</summary>
public sealed record ConversationToolUseBlock(string ToolUseId, string ToolName, string InputJson)
    : ConversationContentBlock;

/// <summary>User-returned tool_result block for a prior tool_use id.</summary>
public sealed record ConversationToolResultBlock(string ToolUseId, bool IsError, string Content)
    : ConversationContentBlock;

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
    /// <param name="onTextDelta">
    /// ADR-011: when non-null, the model turn is streamed and this callback is invoked
    /// once per incremental assistant text delta as the underlying API stream is
    /// consumed (used by Grimoire.QueryAgent's answer streaming, SC-003). When null
    /// (Ingest's call sites), behavior is unchanged — a single non-streamed call.
    /// </param>
    Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken,
        Action<string>? onTextDelta = null);
}

/// <summary>JSON schema and metadata for a tool offered to the model.</summary>
public sealed record ToolDefinition(string Name, string Description, string InputSchemaJson);

/// <summary>
/// 023 T051: a model-provider rejection, translated into a harness-owned type at the port
/// so provider-SDK exception types never leave the adapter namespace (ADR-010 containment).
/// <para>
/// <see cref="Exception.Message"/> is the operator-facing text: a single line, length-capped,
/// of the form <c>Model API error 400 (invalid_request_error): &lt;provider message&gt;</c>.
/// It is what the agents' unhandled-failure path sanitizes and records, so it reaches the
/// board card, the task detail, and the status history through the one existing code path —
/// replacing the bare status that used to be all an operator could see.
/// </para>
/// </summary>
public sealed class ModelApiException : Exception
{
    public ModelApiException(string message, int statusCode, string? errorType, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }

    /// <summary>HTTP status the provider answered with.</summary>
    public int StatusCode { get; }

    /// <summary>The provider's own error classification (e.g. <c>invalid_request_error</c>), when it sent one.</summary>
    public string? ErrorType { get; }
}

/// <summary>
/// #119: the model declined the request. This is a documented, successful-transport
/// outcome (HTTP 200 with <c>stop_reason: "refusal"</c>), not a malformed protocol
/// response — the loop used to report it as an unexpected stop reason, which pointed an
/// operator at the harness instead of at the safety classifier that actually declined.
/// <para>
/// <see cref="Exception.Message"/> is the operator-facing text, composed by
/// <see cref="FromDetails"/> and shaped by
/// <see cref="OperatorFacingText.SingleLineCapped"/> exactly as
/// <see cref="ModelApiException"/>'s is, so it reaches the board card, the task detail,
/// and the status history through the same unhandled-failure path.
/// </para>
/// </summary>
public sealed class ModelRefusalException : Exception
{
    public ModelRefusalException(string message, string? category, string? explanation)
        : base(message)
    {
        Category = category;
        Explanation = explanation;
    }

    /// <summary>The provider's refusal category, when it sent one.</summary>
    public string? Category { get; }

    /// <summary>The provider's own explanation of the refusal, when it sent one.</summary>
    public string? Explanation { get; }

    /// <summary>
    /// Composes the operator-facing refusal message from whatever the provider sent.
    /// The <c>Model refusal</c> prefix mirrors <c>Model API error</c> so the two model
    /// rejection classes read as siblings in a status history; the turn number tells an
    /// operator whether the refusal hit the source document on the first turn or
    /// something the run built up to.
    /// </summary>
    public static ModelRefusalException FromDetails(ModelRefusalDetails? details, int turn)
    {
        var text = "Model refusal";
        if (!string.IsNullOrWhiteSpace(details?.Category))
        {
            text += $" ({details.Category})";
        }

        text += $" on turn {turn}";
        text += !string.IsNullOrWhiteSpace(details?.Explanation)
            ? $": {details.Explanation}"
            : ": the provider's safety classifier declined the request and sent no explanation.";

        return new ModelRefusalException(
            OperatorFacingText.SingleLineCapped(text), details?.Category, details?.Explanation);
    }
}

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>
/// One tool-use request issued by the model.
/// </summary>
public sealed record ToolUseRequest(
    string ToolUseId,
    string ToolName,
    string InputJson);

/// <summary>
/// One turn response from the model.
/// </summary>
public sealed record ModelTurn(
    string? AssistantText,
    IReadOnlyList<ToolUseRequest> ToolUseRequests,
    string StopReason,
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

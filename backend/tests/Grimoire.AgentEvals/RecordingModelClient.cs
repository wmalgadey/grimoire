using System.Security.Cryptography;
using System.Text;
using Grimoire.AgentRuntime.Core;

namespace Grimoire.AgentEvals;

/// <summary>
/// Decorator over an <see cref="IModelClient"/> that keeps an in-memory record of every
/// turn (request fingerprint + verbatim response) for a single eval run, so a test can
/// assert against the exact sequence of model turns without needing the provider itself
/// to expose transcript access.
/// </summary>
public sealed class RecordingModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private int _turn;

    public RecordingModelClient(IModelClient inner)
    {
        _inner = inner;
    }

    public string ModelId => _inner.ModelId;

    public List<RecordedEvalTurn> Calls { get; } = [];

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken,
        Action<string>? onTextDelta = null)
    {
        var turn = await _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken, onTextDelta);
        _turn++;

        Calls.Add(new RecordedEvalTurn(
            _turn,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(systemPrompt))),
            conversation.Select(c => new RecordedEvalMessage(c.Role, c.Content)).ToList(),
            tools.Select(t => t.Name).ToList(),
            turn.StopReason,
            turn.ToolUseRequests.Select(t => new RecordedEvalToolUse(t.ToolUseId, t.ToolName, t.InputJson)).ToList(),
            turn.AssistantText,
            turn.InputTokens,
            turn.OutputTokens));

        return turn;
    }
}

public sealed record RecordedEvalTurn(
    int Turn,
    string SystemPromptSha256,
    IReadOnlyList<RecordedEvalMessage> Conversation,
    IReadOnlyList<string> ToolNames,
    ModelStopReason StopReason,
    IReadOnlyList<RecordedEvalToolUse> ToolUses,
    string? AssistantText,
    int InputTokens,
    int OutputTokens);

public sealed record RecordedEvalMessage(string Role, string Content);

public sealed record RecordedEvalToolUse(string ToolUseId, string ToolName, string InputJson);

namespace Grimoire.IngestAgent.AgentCore.Adapters.Replay;

/// <summary>
/// Decorator over the live <see cref="IModelClient"/> adapter (ADR-011): forwards every
/// call and appends the turn — request fingerprints + verbatim response — to the capture
/// file. The file is rewritten after each turn so a crashed run still leaves its captured
/// prefix. The eval runner later enriches the file with sample metadata, judge verdicts,
/// and the captured outcome.
/// </summary>
public sealed class TurnCaptureModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private readonly string _capturePath;
    private readonly List<RecordedTurn> _turns = [];

    public TurnCaptureModelClient(IModelClient inner, string capturePath)
    {
        _inner = inner;
        _capturePath = capturePath;
    }

    public string ModelId => _inner.ModelId;

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var turn = await _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken);

        _turns.Add(new RecordedTurn(
            Turn: _turns.Count + 1,
            SystemPromptSha256: RecordingSerialization.Hash(systemPrompt),
            Conversation: conversation.Select(m => new RecordedMessage(m.Role, RecordingSerialization.HashMessage(m))).ToList(),
            ToolNames: tools.Select(t => t.Name).ToList(),
            StopReason: turn.StopReason.ToProtocolString(),
            ToolUses: turn.ToolUseRequests.Select(t => new RecordedToolUse(t.ToolUseId, t.ToolName, t.InputJson)).ToList(),
            AssistantText: turn.AssistantText,
            InputTokens: turn.InputTokens,
            OutputTokens: turn.OutputTokens));

        RecordingSerialization.Save(_capturePath, new RecordedSample(
            SchemaVersion: RecordingSerialization.CurrentSchemaVersion,
            Sample: 0,
            TaskId: string.Empty,
            Model: _inner.ModelId,
            Turns: _turns,
            JudgeVerdicts: null,
            Outcome: null));

        return turn;
    }
}

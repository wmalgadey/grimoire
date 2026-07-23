namespace Grimoire.IngestAgent.AgentCore.Adapters.Replay;

/// <summary>
/// Thrown when the conversation the harness conducts diverges from the recorded
/// interaction (spec 009 FR-010) — surfaced as an infrastructure failure, never as an
/// agent-judgment score. Named "replay_mismatch" in run output for diagnosability.
/// </summary>
public sealed class ReplayMismatchException : Exception
{
    public ReplayMismatchException(int turn, string component, string detail)
        : base($"replay_mismatch at turn {turn} ({component}): {detail}")
    {
        Turn = turn;
        Component = component;
    }

    public int Turn { get; }

    public string Component { get; }
}

/// <summary>
/// <see cref="IModelClient"/> adapter that serves recorded turns instead of calling a
/// provider (ADR-011). Call <em>k</em> returns turn <em>k</em>'s recorded response after
/// verifying the request fingerprints (system prompt hash, per-message conversation
/// hashes, tool-name list) match the recording — first divergence fails fast with
/// <see cref="ReplayMismatchException"/> (research.md R2). Requires no credential and
/// performs no I/O beyond reading the recording file.
/// </summary>
public sealed class ReplayModelClient : IModelClient
{
    private readonly RecordedSample _sample;
    private int _nextTurnIndex;

    public ReplayModelClient(string replayPath)
    {
        _sample = RecordingSerialization.Load(replayPath);
    }

    public string ModelId => _sample.Model;

    public Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var turnNumber = _nextTurnIndex + 1;
        if (_nextTurnIndex >= _sample.Turns.Count)
        {
            throw new ReplayMismatchException(
                turnNumber,
                "turn_count",
                $"the harness requested turn {turnNumber} but the recording contains only {_sample.Turns.Count} turns.");
        }

        var recorded = _sample.Turns[_nextTurnIndex];

        var systemPromptHash = RecordingSerialization.Hash(systemPrompt);
        if (!string.Equals(systemPromptHash, recorded.SystemPromptSha256, StringComparison.Ordinal))
        {
            throw new ReplayMismatchException(turnNumber, "system_prompt",
                $"current hash {systemPromptHash} differs from recorded {recorded.SystemPromptSha256}.");
        }

        if (conversation.Count != recorded.Conversation.Count)
        {
            throw new ReplayMismatchException(turnNumber, "conversation_length",
                $"the harness sent {conversation.Count} messages but the recording expects {recorded.Conversation.Count}.");
        }

        for (var i = 0; i < conversation.Count; i++)
        {
            var currentHash = RecordingSerialization.HashMessage(conversation[i]);
            if (!string.Equals(conversation[i].Role, recorded.Conversation[i].Role, StringComparison.Ordinal)
                || !string.Equals(currentHash, recorded.Conversation[i].ContentSha256, StringComparison.Ordinal))
            {
                throw new ReplayMismatchException(turnNumber, $"conversation[{i}]",
                    $"message role/content diverges from the recording (current {conversation[i].Role}/{currentHash}).");
            }
        }

        var currentToolNames = tools.Select(t => t.Name).ToList();
        if (!currentToolNames.SequenceEqual(recorded.ToolNames, StringComparer.Ordinal))
        {
            throw new ReplayMismatchException(turnNumber, "tool_names",
                $"current tool set [{string.Join(", ", currentToolNames)}] differs from recorded [{string.Join(", ", recorded.ToolNames)}].");
        }

        _nextTurnIndex++;

        return Task.FromResult(new ModelTurn(
            AssistantText: recorded.AssistantText,
            ToolUseRequests: recorded.ToolUses.Select(t => new ToolUseRequest(t.ToolUseId, t.ToolName, t.InputJson)).ToList(),
            StopReason: ModelStopReasonContract.FromRawValue(recorded.StopReason),
            InputTokens: recorded.InputTokens,
            OutputTokens: recorded.OutputTokens));
    }
}

using Grimoire.AgentRuntime.Core;

namespace Grimoire.AgentRuntime.Core.Adapters.Replay;

/// <summary>
/// Decorator over the live <see cref="IModelClient"/> adapter (ADR-012): forwards every
/// call and appends the turn — request fingerprints + verbatim response — to the capture
/// file. The file is rewritten after each turn so a crashed run still leaves its captured
/// prefix. The eval runner later enriches the file with sample metadata, judge verdicts,
/// and the captured outcome.
/// </summary>
public sealed class TurnCaptureModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private readonly string _capturePath;
    private readonly List<QueryRecordedTurn> _turns = [];

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
        CancellationToken cancellationToken,
        Action<string>? onTextDelta = null)
    {
        var turn = await _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken, onTextDelta);

        // #173: QueryRecordedTurn has no field for ModelTurn.HasIncompleteToolCall, and
        // ReplayModelClient always reconstructs it as false — persisting this turn as-is
        // would capture a recording that replays a different harness nudge
        // (ContinuePrompt) than the one the live run actually took
        // (AgentLoop.IncompleteToolCallPrompt), which then fails replay's own
        // conversation-hash check on the very next turn. Representing the flag durably is
        // a recording-format change (spec 009's contract requires a schema_version bump
        // for that, which would invalidate every existing fixture), so a capture run that
        // hits this rare, non-deterministic condition is failed outright instead — rerun
        // it rather than trust a recording that cannot faithfully replay.
        //
        // The file is deleted, not just left unwritten: every earlier turn in this run was
        // already persisted (the file is rewritten after each successful turn, by design,
        // so a crash still leaves its prefix — see the class doc). Left in place, that
        // prefix is itself a schema-valid, complete-looking recording that a pipeline
        // which only checks "does the sample file exist" — as Grimoire.EvalRunner's
        // capture pipeline does — would happily publish as a trustworthy, if shorter,
        // sample. Only this specific rejection path deletes it; any other crash still
        // leaves the prefix for debugging exactly as before.
        if (turn.HasIncompleteToolCall)
        {
            if (File.Exists(_capturePath))
            {
                File.Delete(_capturePath);
            }

            throw new InvalidOperationException(
                $"Turn {_turns.Count + 1} had an incomplete or invalid tool call and cannot be captured " +
                "(recordings do not yet represent an incomplete tool call, and replaying one built " +
                "without that context would diverge from this run). Re-run the capture.");
        }

        _turns.Add(new QueryRecordedTurn(
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

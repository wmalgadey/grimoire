using Grimoire.AgentRuntime.Core;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Scripted test double for <see cref="IModelClient"/>. Plays a predefined
/// sequence of <see cref="ModelTurn"/>s and records every conversation payload
/// it receives for assertions (Principle II, R2).
/// </summary>
public sealed class FakeModelClient : IModelClient
{
    private readonly Queue<ModelTurn> _script;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptedDelta>> _deltaScript;
    private readonly List<RecordedCall> _calls = [];
    private int _turnIndex;

    public FakeModelClient(
        IEnumerable<ModelTurn> script,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptedDelta>>? deltaScript = null)
    {
        _script = new Queue<ModelTurn>(script);
        _deltaScript = deltaScript ?? new Dictionary<int, IReadOnlyList<ScriptedDelta>>();
    }

    public string ModelId => "fake-model";

    /// <summary>All conversation payloads received by the fake, in call order.</summary>
    public IReadOnlyList<RecordedCall> Calls => _calls;

    /// <summary>Number of times <see cref="NextTurnAsync"/> was called.</summary>
    public int CallCount => _calls.Count;

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken,
        Action<string>? onTextDelta = null)
    {
        var callIndex = _turnIndex;
        _calls.Add(new RecordedCall(callIndex, systemPrompt, [.. conversation], [.. tools]));
        _turnIndex++;

        if (_script.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeModelClient exhausted its script at call #{_turnIndex}. " +
                "Add more scripted turns or verify the loop terminates as expected.");
        }

        var turn = _script.Dequeue();

        if (onTextDelta is not null)
        {
            if (_deltaScript.TryGetValue(callIndex, out var deltas))
            {
                foreach (var delta in deltas)
                {
                    if (delta.Delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delta.Delay, cancellationToken);
                    }

                    onTextDelta(delta.Text);
                }
            }
            else if (!string.IsNullOrEmpty(turn.AssistantText))
            {
                // No explicit delta script for this call — emit the whole assistant
                // text as a single chunk so streaming call sites still see something.
                onTextDelta(turn.AssistantText);
            }
        }

        return turn;
    }

    // ── Script builder helpers ────────────────────────────────────────────────────

    /// <summary>Creates a turn that calls a single tool.</summary>
    public static ModelTurn ToolCallTurn(string toolUseId, string toolName, string inputJson)
        => new(
            AssistantText: null,
            ToolUseRequests: [new ToolUseRequest(toolUseId, toolName, inputJson)],
            StopReason: ModelStopReason.ToolUse,
            InputTokens: 100,
            OutputTokens: 50);

    /// <summary>Creates a final turn that ends the run with a narrative.</summary>
    public static ModelTurn FinalTurn(string narrative)
        => new(
            AssistantText: narrative,
            ToolUseRequests: [],
            StopReason: ModelStopReason.EndTurn,
            InputTokens: 200,
            OutputTokens: 100);

    /// <summary>Creates a scripted tool-call turn for write_file.</summary>
    public static ModelTurn WriteFileTurn(string id, string path, string content)
        => ToolCallTurn(id, "write_file",
            System.Text.Json.JsonSerializer.Serialize(new { path, content }));

    /// <summary>Creates a scripted tool-call turn for read_file.</summary>
    public static ModelTurn ReadFileTurn(string id, string path)
        => ToolCallTurn(id, "read_file",
            System.Text.Json.JsonSerializer.Serialize(new { path }));

    /// <summary>Creates a scripted tool-call turn for list_files.</summary>
    public static ModelTurn ListFilesTurn(string id, string path)
        => ToolCallTurn(id, "list_files",
            System.Text.Json.JsonSerializer.Serialize(new { path }));
}

/// <summary>One recorded invocation of <see cref="FakeModelClient.NextTurnAsync"/>.</summary>
public sealed record RecordedCall(
    int TurnIndex,
    string SystemPrompt,
    IReadOnlyList<ConversationMessage> Conversation,
    IReadOnlyList<ToolDefinition> Tools);

/// <summary>
/// One scripted streaming text delta (ADR-011/T025), with an optional delay before it is
/// delivered — lets tests script SC-003 timing scenarios (e.g. first chunk immediate,
/// later chunks after a deliberate delay) deterministically, without a live LLM call.
/// </summary>
public sealed record ScriptedDelta(string Text, TimeSpan Delay = default);

using System.Net;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.IntegrationTests.TestSupport;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #173 — a tool call truncated mid-stream (the output cap, or a connection that ends before
/// <c>content_block_stop</c>) used to be accumulated as raw text and handed on as
/// <see cref="ToolUseRequest.InputJson"/> verbatim, with no check that it was still valid
/// JSON. <see cref="AgentLoop"/> replayed it on the next turn, <c>BuildContentBlocks</c>
/// deserialized it with no guard, and the run died with a raw JSON-parser message instead of
/// anything an operator could act on.
/// <para>
/// This exercises the adapter against a real HTTP listener streaming the exact SSE shape a
/// truncated tool call takes on the wire (<see cref="FakeAnthropicEndpoint.StreamingToolUseBody"/>)
/// — the SDK's own streaming parse runs, so the fix is proven against the real accumulator,
/// not a hand-rolled stand-in for it.
/// </para>
/// </summary>
public class StreamingToolUseTruncationTests
{
    [Fact]
    public async Task ATruncatedToolCall_IsDroppedFromTheTurn_NotReplayedAsAnInvalidToolUseRequest()
    {
        // The exact byte-exact reproduction from the issue: a complete "path" property with
        // the closing brace missing — 19 bytes, matching the production BytePositionInLine.
        // No content_block_stop either (StreamingToolUseBody's closeBlock defaults false) —
        // a truncated stream never emits the close event for the block it interrupted.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: """{"path": "index.md" """, stopReason: "max_tokens"),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        Assert.Empty(turn.ToolUseRequests);
    }

    [Fact]
    public async Task ATruncatedToolCall_StillCarriesTheProvidersStopReason()
    {
        // The loop's existing no-tool-turn handling already knows how to continue on
        // max_tokens — dropping the corpse only helps if the stop reason survives with it.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: """{"path": "index.md" """, stopReason: "max_tokens"),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        Assert.Equal(ModelStopReason.MaxTokens, turn.StopReason);
    }

    [Fact]
    public async Task ATruncatedToolCall_FlagsTheTurnAsHavingAnIncompleteToolCall()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: """{"path": "index.md" """, stopReason: "max_tokens"),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        Assert.True(turn.HasIncompleteToolCall);
    }

    [Fact]
    public async Task ACompleteToolCall_StreamedNormally_StillSurvives()
    {
        // The regression guard for the fix itself: a well-formed streamed call — including
        // the content_block_stop that closes it — must not be caught by the new checks.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: """{"path": "index.md"}""", stopReason: "tool_use", closeBlock: true),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        var request = Assert.Single(turn.ToolUseRequests);
        Assert.Equal("tool-1", request.ToolUseId);
        Assert.Equal("read_file", request.ToolName);
        Assert.Equal("""{"path": "index.md"}""", request.InputJson);
        Assert.False(turn.HasIncompleteToolCall);
    }

    [Fact]
    public async Task ACompleteButUnclosedToolCall_IsStillDroppedAsIncomplete()
    {
        // Copilot review on #178: JSON validity alone is not enough — a stream interrupted
        // exactly where the accumulated text happens to close every brace it opened must not
        // read as complete. Only content_block_stop, the provider's own signal, does that.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: """{"path": "index.md"}""", stopReason: "max_tokens", closeBlock: false),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        Assert.Empty(turn.ToolUseRequests);
        Assert.True(turn.HasIncompleteToolCall);
    }

    [Fact]
    public async Task AClosedToolCallWithNoDeltasAtAll_SynthesizesEmptyInput_AndIsNotDroppedAsIncomplete()
    {
        // A block the provider itself closed with zero deltas is a deliberate, complete
        // (if perhaps schema-invalid) call — content_block_stop is authoritative. Whether
        // an empty input satisfies the tool's schema is GuardedToolExecutor's question, not
        // the accumulator's.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: "", stopReason: "tool_use", closeBlock: true),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        var request = Assert.Single(turn.ToolUseRequests);
        Assert.Equal("{}", request.InputJson);
        Assert.False(turn.HasIncompleteToolCall);
    }

    [Fact]
    public async Task WhenOneOfSeveralToolCallsIsTruncated_TheCompleteOnesSurvive_AndTheTurnIsFlagged()
    {
        // Copilot review on #178: a turn can carry both a complete call and one the output
        // cap cut off. The complete one must not be discarded just because its sibling was —
        // and the turn still has to say something was lost.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingMultiToolUseBody(
                [
                    new FakeAnthropicEndpoint.StreamedToolBlock(
                        "tool-1", "read_file", """{"path": "index.md"}""", CloseBlock: true),
                    new FakeAnthropicEndpoint.StreamedToolBlock(
                        "tool-2", "write_file", """{"path": "new.md", "content": "cut off""", CloseBlock: false),
                ],
                stopReason: "max_tokens"),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        var request = Assert.Single(turn.ToolUseRequests);
        Assert.Equal("tool-1", request.ToolUseId);
        Assert.True(turn.HasIncompleteToolCall);
    }

    [Fact]
    public async Task AnInvalidStoredToolUseBlock_FailsWithAMessageNamingTheToolAndId_NotAByteOffset()
    {
        // Defense in depth for the invariant the adapter's own accumulator now guarantees:
        // if a ConversationToolUseBlock with invalid InputJson ever reaches replay anyway
        // (a corrupted capture, a future caller that skips the guard), the failure has to
        // name what broke, not describe a JSON parser's position in the payload.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.MessageBody("end_turn", text: "Unreachable."));
        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var client = new AnthropicModelClient(
            logger: null!,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar);

        var conversation = new List<ConversationMessage>
        {
            new("user", [new ConversationTextBlock("Do the task.")]),
            new("assistant", [new ConversationToolUseBlock("tool-1", "read_file", """{"path": "index.md" """)]),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.NextTurnAsync(
            "You are a test agent.",
            conversation,
            ToolRegistry.Default.Tools,
            CancellationToken.None));

        Assert.Contains("read_file", exception.Message);
        Assert.Contains("tool-1", exception.Message);
        Assert.DoesNotContain("BytePositionInLine", exception.Message);
    }

    [Fact]
    public async Task ANullStoredToolUseBlock_FailsWithAMessageNamingTheToolAndId_NotAnArgumentNullException()
    {
        // Copilot review on #178: a recorded capture can deserialize a missing/null
        // input_json into a non-null-annotated string at runtime — nullable annotations
        // aren't enforced by the deserializer — and JsonSerializer.Deserialize(null) throws
        // ArgumentNullException, not JsonException. The same tool/id message has to cover it.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.MessageBody("end_turn", text: "Unreachable."));
        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var client = new AnthropicModelClient(
            logger: null!,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar);

        var conversation = new List<ConversationMessage>
        {
            new("user", [new ConversationTextBlock("Do the task.")]),
            new("assistant", [new ConversationToolUseBlock("tool-1", "read_file", null!)]),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.NextTurnAsync(
            "You are a test agent.",
            conversation,
            ToolRegistry.Default.Tools,
            CancellationToken.None));

        Assert.Contains("read_file", exception.Message);
        Assert.Contains("tool-1", exception.Message);
    }

    [Fact]
    public async Task AgentLoop_NudgesTheModelToReissue_WhenATurnHasAnIncompleteToolCall()
    {
        // Copilot review on #178: the dropped call needs its own trace in the conversation —
        // without it, a valid sibling call's results carry no sign that anything was lost.
        var root = Path.Combine(Path.GetTempPath(), $"incomplete-tool-call-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var policy = new SafetyPolicy(root, readPrefixes: [], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), root);
            var truncatedTurn = new ModelTurn(
                AssistantText: null,
                ToolUseRequests: [new ToolUseRequest("tool-1", "read_file", """{"path": "index.md"}""")],
                StopReason: ModelStopReason.MaxTokens,
                InputTokens: 100,
                OutputTokens: 50,
                HasIncompleteToolCall: true);
            var fake = new FakeModelClient([truncatedTurn, FakeModelClient.FinalTurn("Done.")]);
            var loop = new AgentLoop(fake, executor);

            await loop.RunAsync(
                "You are a test agent.",
                [new ConversationMessage("user", "Do the task.")],
                "task-incomplete-tool-call",
                CancellationToken.None);

            Assert.Equal(2, fake.CallCount);
            var lastMessage = fake.Calls[1].Conversation[^1];
            Assert.Equal("user", lastMessage.Role);
            Assert.Contains(
                lastMessage.ContentBlocks.OfType<ConversationTextBlock>(),
                block => block.Text.Contains("cut off", StringComparison.Ordinal)
                    && block.Text.Contains($"<{AgentLoop.HarnessInstructionTag}>", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task<ModelTurn> NextTurnAgainstAsync(FakeAnthropicEndpoint provider)
    {
        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var client = new AnthropicModelClient(
            logger: null!,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar);

        return await client.NextTurnAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            ToolRegistry.Default.Tools,
            CancellationToken.None,
            onTextDelta: _ => { });
    }
}

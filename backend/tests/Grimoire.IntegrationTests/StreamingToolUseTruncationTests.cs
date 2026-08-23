using System.Net;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
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
    public async Task ACompleteToolCall_StreamedNormally_StillSurvives()
    {
        // The regression guard for the fix itself: a well-formed streamed call must not be
        // caught by the new validity check.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.StreamingToolUseBody(
                "tool-1", "read_file", rawInputJson: """{"path": "index.md"}""", stopReason: "tool_use"),
            FakeAnthropicEndpoint.StreamingContentType);

        var turn = await NextTurnAgainstAsync(provider);

        var request = Assert.Single(turn.ToolUseRequests);
        Assert.Equal("tool-1", request.ToolUseId);
        Assert.Equal("read_file", request.ToolName);
        Assert.Equal("""{"path": "index.md"}""", request.InputJson);
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

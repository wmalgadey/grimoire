using Grimoire.AgentRuntime.Core;

namespace Grimoire.AgentEvals;

/// <summary>
/// T015 (US1) — FR-013: a single provider call exceeding the bound fails the sample without
/// hanging the run. Uses an injected short timeout (not the real 120s) against a fake
/// <see cref="IModelClient"/> whose call never completes, so this is fast and hermetic.
/// </summary>
public class TimeoutEnforcingModelClientTests
{
    [Fact]
    public async Task NextTurnAsync_InnerNeverCompletes_ThrowsModelCallTimeoutException()
    {
        var inner = new NeverCompletingModelClient();
        var timeout = TimeSpan.FromMilliseconds(50);
        var client = new TimeoutEnforcingModelClient(inner, timeout);

        var exception = await Assert.ThrowsAsync<ModelCallTimeoutException>(
            () => client.NextTurnAsync("system prompt", [], [], CancellationToken.None));

        Assert.Equal(timeout, exception.Timeout);
    }

    [Fact]
    public async Task NextTurnAsync_InnerCompletesBeforeTimeout_ReturnsInnerResult()
    {
        var expected = new ModelTurn(
            AssistantText: "done",
            ToolUseRequests: [],
            StopReason: ModelStopReason.EndTurn,
            InputTokens: 1,
            OutputTokens: 1);
        var inner = new ImmediateModelClient(expected);
        var client = new TimeoutEnforcingModelClient(inner, TimeSpan.FromSeconds(30));

        var result = await client.NextTurnAsync("system prompt", [], [], CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public void ModelId_DelegatesToInnerClient()
    {
        var inner = new ImmediateModelClient(new ModelTurn(null, [], ModelStopReason.EndTurn, 0, 0));
        var client = new TimeoutEnforcingModelClient(inner);

        Assert.Equal(inner.ModelId, client.ModelId);
    }

    private sealed class NeverCompletingModelClient : IModelClient
    {
        public string ModelId => "never-completing";

        public Task<ModelTurn> NextTurnAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversation,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken,
            Action<string>? onTextDelta = null)
            => new TaskCompletionSource<ModelTurn>().Task;
    }

    private sealed class ImmediateModelClient(ModelTurn turn) : IModelClient
    {
        public string ModelId => "immediate";

        public Task<ModelTurn> NextTurnAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> conversation,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken,
            Action<string>? onTextDelta = null)
            => Task.FromResult(turn);
    }
}

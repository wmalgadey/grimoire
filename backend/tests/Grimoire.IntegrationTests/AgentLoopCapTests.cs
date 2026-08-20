using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #107 — the loop's limits, pinned with a scripted fake client and known usage values.
/// The context guard compares the current turn's InputTokens (the live conversation as
/// re-sent per request) against the window-sized cap; the spend cap sums billed
/// input + output across the run. Summing under one shared cap double-counted the
/// conversation once per turn, so a run breached "200k" with a fraction of that live.
/// </summary>
public class AgentLoopCapTests : IDisposable
{
    private readonly string _root;
    private readonly GuardedToolExecutor _executor;

    public AgentLoopCapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"agent-loop-caps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var policy = new SafetyPolicy(_root, readPrefixes: [], writePrefixes: []);
        _executor = new GuardedToolExecutor(policy, new WriteJournal(), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // A turn that neither calls tools nor ends the run: stop_reason max_tokens makes the
    // loop append its continue prompt and take another turn, so usage values can be
    // scripted turn by turn without any tool plumbing.
    private static ModelTurn ContinueTurn(int inputTokens, int outputTokens)
        => new(
            AssistantText: null,
            ToolUseRequests: [],
            StopReason: ModelStopReason.MaxTokens,
            InputTokens: inputTokens,
            OutputTokens: outputTokens);

    private static ModelTurn FinalTurn(int inputTokens, int outputTokens)
        => new(
            AssistantText: "Done.",
            ToolUseRequests: [],
            StopReason: ModelStopReason.EndTurn,
            InputTokens: inputTokens,
            OutputTokens: outputTokens);

    private Task<AgentLoopResult> RunAsync(AgentLoop loop)
        => loop.RunAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            "task-cap-1",
            CancellationToken.None);

    [Fact]
    public async Task ContextGuard_DoesNotSumTheConversationAcrossTurns()
    {
        // The regression from the live Lint failure: a conversation that stays at 800
        // tokens per request never outgrows a 1000-token window, however many turns it
        // takes. The old single cap summed the re-sent context and aborted the run.
        var fake = new FakeModelClient([
            ContinueTurn(inputTokens: 800, outputTokens: 10),
            ContinueTurn(inputTokens: 800, outputTokens: 10),
            ContinueTurn(inputTokens: 800, outputTokens: 10),
            FinalTurn(inputTokens: 800, outputTokens: 10),
        ]);
        var loop = new AgentLoop(fake, _executor, contextTokenCap: 1000, spendTokenCap: 100_000);

        var result = await RunAsync(loop);

        Assert.Equal(4, result.TurnsUsed);
        Assert.Equal(3200, result.TotalInputTokens);
        Assert.Equal(40, result.TotalOutputTokens);
    }

    [Fact]
    public async Task ContextGuard_FailsTheTurnWhoseConversationOutgrowsTheCap_WithObservedNumbers()
    {
        var fake = new FakeModelClient([
            ContinueTurn(inputTokens: 400, outputTokens: 20),
            ContinueTurn(inputTokens: 900, outputTokens: 20),
            ContinueTurn(inputTokens: 1100, outputTokens: 20),
        ]);
        var loop = new AgentLoop(fake, _executor, contextTokenCap: 1000, spendTokenCap: 100_000);

        var ex = await Assert.ThrowsAsync<AgentLoopCapException>(() => RunAsync(loop));

        Assert.Equal("context", ex.Cap);
        Assert.Equal(3, ex.TurnsUsed);
        Assert.Equal(1000, ex.CapValue);
        Assert.Equal(1100, ex.ContextTokens);
        Assert.Equal(2460, ex.RunTotalTokens);
        Assert.Equal(
            "Context cap exceeded: context 1100, run total 2460, cap 1000, turn 3 of 50. Rolled back.",
            ex.Message);
    }

    [Fact]
    public async Task SpendCap_SumsBilledTokensAcrossTheRun_WithObservedNumbers()
    {
        // Constant 500-token context stays far under the window; the billed total
        // (input + output) crosses 2000 on the third turn: 800 → 1600 → 2400.
        var fake = new FakeModelClient([
            ContinueTurn(inputTokens: 500, outputTokens: 300),
            ContinueTurn(inputTokens: 500, outputTokens: 300),
            ContinueTurn(inputTokens: 500, outputTokens: 300),
        ]);
        var loop = new AgentLoop(fake, _executor, contextTokenCap: 100_000, spendTokenCap: 2000);

        var ex = await Assert.ThrowsAsync<AgentLoopCapException>(() => RunAsync(loop));

        Assert.Equal("spend", ex.Cap);
        Assert.Equal(3, ex.TurnsUsed);
        Assert.Equal(2000, ex.CapValue);
        Assert.Equal(500, ex.ContextTokens);
        Assert.Equal(2400, ex.RunTotalTokens);
        Assert.Equal(
            "Spend cap exceeded: run total 2400 (input 1500, output 900), context 500, cap 2000, turn 3 of 50. Rolled back.",
            ex.Message);
    }

    [Fact]
    public async Task TurnCap_FailsWithObservedTokenTotals()
    {
        var fake = new FakeModelClient([
            ContinueTurn(inputTokens: 400, outputTokens: 50),
            ContinueTurn(inputTokens: 700, outputTokens: 50),
        ]);
        var loop = new AgentLoop(fake, _executor, turnCap: 2, contextTokenCap: 100_000, spendTokenCap: 100_000);

        var ex = await Assert.ThrowsAsync<AgentLoopCapException>(() => RunAsync(loop));

        Assert.Equal("turns", ex.Cap);
        Assert.Equal(2, ex.TurnsUsed);
        Assert.Equal(2, ex.CapValue);
        Assert.Equal(700, ex.ContextTokens);
        Assert.Equal(1200, ex.RunTotalTokens);
        Assert.Equal(
            "Turn cap exceeded: context 700, run total 1200, cap 2 turns, turn 2 of 2. Rolled back.",
            ex.Message);
    }
}

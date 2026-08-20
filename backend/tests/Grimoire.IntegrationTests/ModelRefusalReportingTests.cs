using System.Net;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.IntegrationTests.TestSupport;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #119 — a model refusal is a documented HTTP 200 outcome carrying its own reason, and it
/// has to reach the operator as one. Before this, <c>stop_reason: "refusal"</c> fell into
/// <see cref="AgentLoop"/>'s <c>default</c> branch and was reported as a malformed protocol
/// response ("unexpected stop_reason"), with the instrumentation recording
/// <c>invalid_stop_reason</c> — both pointing at the harness rather than at the safety
/// classifier that actually declined, and both discarding the <c>stop_details</c> the API
/// sent alongside.
/// <para>
/// Two halves, tested where each lives: the adapter's job is to carry <c>stop_details</c>
/// off the wire onto the turn (exercised against a real HTTP listener, so the SDK's own
/// deserialization runs), and the loop's job is to end the run with the provider's reason
/// (exercised with the scripted <see cref="FakeModelClient"/>, no provider involved).
/// </para>
/// </summary>
public class ModelRefusalReportingTests : IDisposable
{
    private readonly string _root;
    private readonly GuardedToolExecutor _executor;

    public ModelRefusalReportingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"model-refusal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var policy = new SafetyPolicy(_root, readPrefixes: [], writePrefixes: []);
        _executor = new GuardedToolExecutor(policy, new WriteJournal(), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private Task<AgentLoopResult> RunAsync(IModelClient client)
        => new AgentLoop(client, _executor).RunAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            "task-refusal-1",
            CancellationToken.None);

    // ── The loop: a refusal ends the run with the provider's reason ───────────────────

    [Fact]
    public async Task Refusal_FailsTheRun_WithTheProvidersCategoryAndExplanation()
    {
        var fake = new FakeModelClient([
            FakeModelClient.RefusalTurn("harmful_content", "The source document asks for malware."),
        ]);

        var exception = await Assert.ThrowsAsync<ModelRefusalException>(() => RunAsync(fake));

        Assert.Equal("harmful_content", exception.Category);
        Assert.Equal("The source document asks for malware.", exception.Explanation);
        Assert.Contains("harmful_content", exception.Message);
        Assert.Contains("The source document asks for malware.", exception.Message);
    }

    [Fact]
    public async Task Refusal_IsNotReportedAsAnUnexpectedStopReason()
    {
        // The regression this issue is about: the failure text an operator reads must not
        // describe the harness's protocol expectations, and must name the refusal.
        var fake = new FakeModelClient([FakeModelClient.RefusalTurn()]);

        var exception = await Assert.ThrowsAsync<ModelRefusalException>(() => RunAsync(fake));

        Assert.Contains("refusal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unexpected stop_reason", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refusal_WithNoStopDetails_StillFailsWithAReadableReason()
    {
        // stop_details is optional. A refusal without it must still produce a reason an
        // operator can act on, never an empty or dangling message.
        var fake = new FakeModelClient([FakeModelClient.RefusalTurn(category: null, explanation: null)]);

        var exception = await Assert.ThrowsAsync<ModelRefusalException>(() => RunAsync(fake));

        Assert.Null(exception.Category);
        Assert.Null(exception.Explanation);
        Assert.Contains("refusal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no explanation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refusal_ReportsTheTurnItHappenedOn_AndStaysSingleLine()
    {
        // Both artifact writers persist only `failure_reason.Split('\n')[0]`, so a
        // multi-line explanation from the provider would silently lose its tail.
        var fake = new FakeModelClient([
            FakeModelClient.ToolCallTurn("tool-1", "list_files", """{"path":"."}"""),
            FakeModelClient.RefusalTurn("harmful_content", "Declined.\nSecond line.\nThird line."),
        ]);

        var exception = await Assert.ThrowsAsync<ModelRefusalException>(() => RunAsync(fake));

        Assert.Contains("turn 2", exception.Message);
        Assert.DoesNotContain('\n', exception.Message);
        Assert.DoesNotContain('\r', exception.Message);
        Assert.Contains("Third line.", exception.Message);
    }

    [Fact]
    public async Task Refusal_IsTerminal_TheLoopDoesNotTakeAnotherTurn()
    {
        // max_tokens/pause_turn continue; a refusal must not, or the run burns its whole
        // turn cap re-asking a question the classifier has already declined.
        var fake = new FakeModelClient([
            FakeModelClient.RefusalTurn(),
            FakeModelClient.FinalTurn("This turn must never be reached."),
        ]);

        await Assert.ThrowsAsync<ModelRefusalException>(() => RunAsync(fake));

        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task ARefusalRecordsTheRefusalOutcome_NotInvalidStopReason()
    {
        var instrumentation = new RecordingInstrumentation();
        var loop = new AgentLoop(
            new FakeModelClient([FakeModelClient.RefusalTurn()]),
            _executor,
            instrumentation: instrumentation);

        await Assert.ThrowsAsync<ModelRefusalException>(() => loop.RunAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            "task-refusal-2",
            CancellationToken.None));

        Assert.Equal([(ModelStopReason.Refusal, "refusal")], instrumentation.NoToolTurns);
        Assert.Equal([(1, "failed")], instrumentation.AgentTurns);
    }

    // ── The adapter: stop_details comes off the wire onto the turn ────────────────────

    [Fact]
    public async Task TheAdapter_CarriesStopDetailsFromTheWireOntoTheTurn()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.MessageBody(
                stopReason: "refusal",
                refusalCategory: "harmful_content",
                refusalExplanation: "Declined by the safety classifier."));

        var turn = await NextTurnAgainstAsync(provider);

        Assert.Equal(ModelStopReason.Refusal, turn.StopReason);
        Assert.NotNull(turn.Refusal);
        Assert.Equal("harmful_content", turn.Refusal!.Category);
        Assert.Equal("Declined by the safety classifier.", turn.Refusal.Explanation);
    }

    [Fact]
    public async Task TheAdapter_LeavesRefusalNull_OnAnOrdinaryTurn()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK,
            FakeAnthropicEndpoint.MessageBody(stopReason: "end_turn", text: "All done."));

        var turn = await NextTurnAgainstAsync(provider);

        Assert.Equal(ModelStopReason.EndTurn, turn.StopReason);
        Assert.Null(turn.Refusal);
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
            CancellationToken.None);
    }

    /// <summary>
    /// Collects what the loop reported, so the assertions stay state-based (Principle II):
    /// what was recorded, not which calls were made in which order on a mock.
    /// </summary>
    private sealed class RecordingInstrumentation : IAgentLoopInstrumentation
    {
        public List<(ModelStopReason StopReason, string Outcome)> NoToolTurns { get; } = [];
        public List<(int Turns, string Outcome)> AgentTurns { get; } = [];

        public System.Diagnostics.Activity? StartModelTurnActivity(string taskId, int turn) => null;
        public void RecordAgentTurns(int turns, string outcome) => AgentTurns.Add((turns, outcome));
        public void RecordModelTokens(int inputTokens, int outputTokens) { }
        public void RecordModelToolRequests(int toolRequestCount, ModelStopReason stopReason) { }
        public void RecordNoToolTurn(ModelStopReason stopReason, string outcome)
            => NoToolTurns.Add((stopReason, outcome));
    }
}

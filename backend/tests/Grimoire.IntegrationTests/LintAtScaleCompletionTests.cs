using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 028-lint-at-scale, US1 (FR-001, FR-008, SC-001) — proves that <em>given</em> the
/// small, roughly constant per-turn context the frontmatter-first strategy (ADR-030's
/// guarded retrieval tools + PR #179's prompt rewrite, both already shipped) is designed
/// to produce, a Lint run completes over an arbitrary number of turns without breaching
/// <see cref="AgentLoop"/>'s context cap. This test scripts turn sizes directly via
/// <see cref="FakeModelClient"/> and asserts nothing about actual wiki reads or prompt
/// behavior — it is a hermetic mechanics test of <c>AgentLoop</c>'s cap enforcement under
/// that shape, not a verification that the frontmatter-first strategy itself produces it.
/// No live/recorded LLM call, no eval fixture — mirroring <c>AgentLoopCapTests</c>'s
/// scripted-turn idiom, scoped to <see cref="LintToolRegistry"/> and a realistic small
/// context cap rather than the loop's production defaults.
/// </summary>
public class LintAtScaleCompletionTests : IDisposable
{
    private readonly string _root;
    private readonly GuardedToolExecutor _executor;

    public LintAtScaleCompletionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"lint-at-scale-completion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var policy = new SafetyPolicy(_root, readPrefixes: [], writePrefixes: []);
        _executor = new GuardedToolExecutor(policy, new WriteJournal(), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // Mirrors the frontmatter-first strategy's actual shape: many turns, each re-sending
    // only a small, roughly constant slice of context (index.md/frontmatter/search results),
    // never the whole wiki at once — the property that lets a run stay bounded regardless of
    // total page count (FR-008).
    private static ModelTurn ContinueTurn(int inputTokens, int outputTokens)
        => new(
            AssistantText: null,
            ToolUseRequests: [],
            StopReason: ModelStopReason.MaxTokens,
            InputTokens: inputTokens,
            OutputTokens: outputTokens);

    private static ModelTurn FinalTurn(int inputTokens, int outputTokens, string narrative)
        => new(
            AssistantText: narrative,
            ToolUseRequests: [],
            StopReason: ModelStopReason.EndTurn,
            InputTokens: inputTokens,
            OutputTokens: outputTokens);

    [Fact]
    public async Task RunCompletes_WithManyTurns_AsLongAsEachTurnStaysSmall_RegardlessOfTurnCount()
    {
        // Ten turns of a small, constant per-turn context — the shape a frontmatter-first
        // read strategy produces on an arbitrarily large wiki. A cap that summed context
        // across turns (the #107 regression) would fail this; the real context guard, which
        // checks only the current turn's own size, does not (FR-001, FR-008).
        var turns = new List<ModelTurn>();
        for (var i = 0; i < 9; i++)
        {
            turns.Add(ContinueTurn(inputTokens: 900, outputTokens: 50));
        }

        turns.Add(FinalTurn(inputTokens: 900, outputTokens: 50, narrative: "Wiki health check complete."));

        var fake = new FakeModelClient(turns);
        var loop = new AgentLoop(
            fake,
            _executor,
            contextTokenCap: 1_000,
            spendTokenCap: 100_000,
            registry: LintToolRegistry.Default);

        var result = await loop.RunAsync(
            "You are a test lint agent.",
            [new ConversationMessage("user", "Perform the wiki health check now.")],
            "run-lint-at-scale-completion",
            CancellationToken.None);

        Assert.Equal(10, result.TurnsUsed);
        Assert.Equal("Wiki health check complete.", result.Narrative);

        // Makes the "Lint-shaped" wiring explicit — without this, the test above exercises
        // only AgentLoop's generic cap mechanics and would pass identically for any
        // registry, duplicating AgentLoopCapTests with no Lint-specific coverage.
        Assert.Equal(
            LintToolRegistry.Default.Tools.Select(t => t.Name),
            fake.Calls[0].Tools.Select(t => t.Name));
    }

    [Fact]
    public async Task RunThatExceedsTheCapEvenOnce_StillFails_ConfirmingThisIsNotAVacuousTest()
    {
        // Red/Green sanity: a run whose context genuinely outgrows the cap on one turn
        // still throws — proving RunCompletes_WithManyTurns above is exercising real
        // cap-enforcement, not a guard that has been disabled or bypassed for Lint.
        var fake = new FakeModelClient([
            ContinueTurn(inputTokens: 900, outputTokens: 50),
            ContinueTurn(inputTokens: 1_100, outputTokens: 50),
        ]);
        var loop = new AgentLoop(
            fake,
            _executor,
            contextTokenCap: 1_000,
            spendTokenCap: 100_000,
            registry: LintToolRegistry.Default);

        await Assert.ThrowsAsync<AgentLoopCapException>(() => loop.RunAsync(
            "You are a test lint agent.",
            [new ConversationMessage("user", "Perform the wiki health check now.")],
            "run-lint-at-scale-completion-negative",
            CancellationToken.None));
    }
}

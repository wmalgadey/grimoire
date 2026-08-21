using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Xunit;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #131: consecutive model turns must not run together in the streamed text.
///
/// <para>
/// Every turn of the loop streams its assistant text through the same callback, and the loop
/// emitted nothing between them — so a turn that wrote "…searching the wiki now." before
/// calling a tool was followed immediately by the next turn's "I found three pages", with no
/// space and no paragraph break. This is not only a live-view artifact: the Hub appends the
/// chunks verbatim and stores the accumulation as the recorded answer, so the same
/// run-together string is what gets re-rendered as markdown afterwards, where a heading or
/// list opening a later turn loses its block structure.
/// </para>
///
/// <para>
/// The boundary is marked where the boundary actually is — between turns, in the loop — not
/// patched into a Svelte component, so the live stream, the persisted record and the rendered
/// markdown are fixed in one place. The separator marks text the agent wrote; it never edits
/// it (Principle V).
/// </para>
/// </summary>
public class AgentLoopTurnBoundaryTests : IDisposable
{
    private readonly string _root;
    private readonly GuardedToolExecutor _executor;

    public AgentLoopTurnBoundaryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"agent-loop-boundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var policy = new SafetyPolicy(_root, readPrefixes: [], writePrefixes: []);
        _executor = new GuardedToolExecutor(policy, new WriteJournal(), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task TextFromTwoTurns_IsSeparated_RatherThanConcatenated()
    {
        var streamed = await RunAsync(
            script:
            [
                FakeModelClient.ToolCallTurn("call-1", ToolRegistry.ReadFile, """{"path":"index.md"}"""),
                FakeModelClient.FinalTurn("done"),
            ],
            deltas: new Dictionary<int, IReadOnlyList<ScriptedDelta>>
            {
                [0] = [new ScriptedDelta("Let me look at the wiki index.")],
                [1] = [new ScriptedDelta("I found three pages that cover this.")],
            });

        Assert.Equal(
            "Let me look at the wiki index.\n\nI found three pages that cover this.",
            streamed);
    }

    /// <summary>
    /// The separator is armed at the turn boundary but written only when more text actually
    /// follows, so a run whose later turns produce no prose ends exactly where its text ended.
    /// A trailing blank line would be just as visible in the recorded answer as a missing one.
    /// </summary>
    [Fact]
    public async Task ALaterTurnWithoutText_LeavesNoTrailingSeparator()
    {
        var streamed = await RunAsync(
            script:
            [
                FakeModelClient.ToolCallTurn("call-1", ToolRegistry.ReadFile, """{"path":"index.md"}"""),
                FakeModelClient.FinalTurn("done"),
            ],
            deltas: new Dictionary<int, IReadOnlyList<ScriptedDelta>>
            {
                [0] = [new ScriptedDelta("Checking the index.")],
                // Explicitly empty rather than absent: with no entry the fake falls back to
                // streaming the turn's AssistantText, which would make this a two-text run.
                [1] = [],
            });

        Assert.Equal("Checking the index.", streamed);
    }

    /// <summary>
    /// Deltas within one turn are the model's own token stream and are still concatenated
    /// verbatim — the boundary being marked is between turns, not between chunks.
    /// </summary>
    [Fact]
    public async Task DeltasWithinOneTurn_AreStillJoinedWithoutASeparator()
    {
        var streamed = await RunAsync(
            script: [FakeModelClient.FinalTurn("done")],
            deltas: new Dictionary<int, IReadOnlyList<ScriptedDelta>>
            {
                [0] = [new ScriptedDelta("One "), new ScriptedDelta("sentence, "), new ScriptedDelta("split.")],
            });

        Assert.Equal("One sentence, split.", streamed);
    }

    private async Task<string> RunAsync(
        IReadOnlyList<ModelTurn> script,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptedDelta>> deltas)
    {
        // Mirrors how the Hub accumulates the answer: chunks appended verbatim, in order.
        var streamed = new System.Text.StringBuilder();
        var loop = new AgentLoop(
            new FakeModelClient(script, deltas),
            _executor,
            onTextDelta: text => streamed.Append(text));

        await loop.RunAsync(
            systemPrompt: "system",
            initialConversation: [new ConversationMessage("user", [new ConversationTextBlock("question")])],
            taskId: "turn-boundary-1",
            CancellationToken.None);

        return streamed.ToString();
    }
}

using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #126 (ADR-029) — the harness's own steering message and untrusted source content both
/// travel in the <c>user</c> role, and the scaffold tells the agent not to follow
/// instructions that arrive there. Until this was fixed the continuation prompt was a bare
/// undelimited sentence in exactly that channel: indistinguishable, to the agent, from a
/// sentence a source document had put there. The delimiter is what makes the operator
/// channel identifiable, so it is asserted on the conversation the model client actually
/// receives — not on the constant.
/// </summary>
public class HarnessOperatorTurnTests
{
    private static (AgentLoop Loop, FakeModelClient Model, string Root) BuildLoop(IEnumerable<ModelTurn> script)
    {
        var root = Path.Combine(Path.GetTempPath(), $"harness-turn-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiDir);

        var policy = new SafetyPolicy(
            root,
            readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
            writePrefixes: [wikiDir + Path.DirectorySeparatorChar]);
        var executor = new GuardedToolExecutor(policy, new WriteJournal(), root, taskId: "task-harness-turn");
        var model = new FakeModelClient(script);
        return (new AgentLoop(model, executor), model, root);
    }

    /// <summary>
    /// A <c>max_tokens</c> stop with no tool calls is the loop's continuation path: it
    /// re-sends the conversation with the harness's steering message appended.
    /// </summary>
    private static ModelTurn TruncatedTurn(string text)
        => new(text, [], ModelStopReason.MaxTokens, InputTokens: 100, OutputTokens: 50);

    private static string LastUserText(RecordedCall call)
    {
        var last = call.Conversation[^1];
        Assert.Equal("user", last.Role);
        return string.Join(
            "\n",
            last.ContentBlocks.OfType<ConversationTextBlock>().Select(static block => block.Text));
    }

    private const string Open = "<" + AgentLoop.HarnessInstructionTag + ">";
    private const string Close = "</" + AgentLoop.HarnessInstructionTag + ">";

    /// <summary>
    /// The text the marker encloses, and — as the second element — whatever the turn carries
    /// outside it. The property under test is about both halves: what the harness said has to
    /// be inside, and nothing of the harness's may be left outside.
    ///
    /// <para>
    /// Bound to the outermost pair, because the enclosed text names the tags when it explains
    /// them: the first inner mention would otherwise close the block early and hide the rest
    /// of the harness's own words from the assertion.
    /// </para>
    /// </summary>
    private static (string Inside, string Outside) SplitOnMarker(string turn)
    {
        var open = turn.IndexOf(Open, StringComparison.Ordinal);
        var close = turn.LastIndexOf(Close, StringComparison.Ordinal);
        Assert.True(open >= 0, $"No {Open} in the harness turn: {turn}");
        Assert.True(close > open, $"No {Close} after {Open} in the harness turn: {turn}");

        var contentStart = open + Open.Length;
        return (
            turn[contentStart..close],
            turn[..open] + turn[(close + Close.Length)..]);
    }

    [Fact]
    public async Task TheHarnessSteeringMessage_ArrivesInsideItsOwnMarker_NotAsBareUserText()
    {
        var (loop, model, root) = BuildLoop([
            TruncatedTurn("Half a thought, cut off by the output ceiling"),
            FakeModelClient.FinalTurn("Done."),
        ]);

        try
        {
            await loop.RunAsync(
                systemPrompt: "You are a test agent.",
                userPrompt: "Integrate the source.",
                taskId: "task-harness-turn",
                sourceRef: "source.md",
                sourceContent: "# source",
                cancellationToken: CancellationToken.None);

            Assert.Equal(2, model.CallCount);
            var (inside, outside) = SplitOnMarker(LastUserText(model.Calls[1]));

            // Inside the marker, not merely somewhere in the same turn.
            Assert.Contains("Continue the task.", inside, StringComparison.Ordinal);

            // And nothing of the harness's is left outside it: an explanation or a future
            // steering line sitting one line below the block would be undelimited harness
            // prose in the user channel, which is the defect this delimiter removes.
            Assert.True(
                string.IsNullOrWhiteSpace(outside),
                $"Harness-authored text outside the marker: [{outside}]");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Query and Lint assemble their own initial conversation and never send the Ingest
    /// scaffold, so the marker has to explain itself where it appears — otherwise the
    /// delimiter is meaningless to two of the three agents.
    /// </summary>
    [Fact]
    public async Task TheMarkerExplainsItself_ForCallersThatSendNoScaffold()
    {
        var (loop, model, root) = BuildLoop([
            TruncatedTurn("Half an answer"),
            FakeModelClient.FinalTurn("Done."),
        ]);

        try
        {
            await loop.RunAsync(
                systemPrompt: "You are a test agent.",
                initialConversation: [new ConversationMessage("user", "What decisions exist?")],
                taskId: "task-harness-turn",
                cancellationToken: CancellationToken.None);

            var (inside, outside) = SplitOnMarker(LastUserText(model.Calls[1]));

            Assert.Contains("harness", inside, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not from any source document", inside, StringComparison.Ordinal);
            Assert.True(
                string.IsNullOrWhiteSpace(outside),
                $"The self-description must be inside the marker, not beside it: [{outside}]");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

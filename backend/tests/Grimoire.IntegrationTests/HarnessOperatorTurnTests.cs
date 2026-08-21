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
            var continuation = LastUserText(model.Calls[1]);

            Assert.Contains($"<{AgentLoop.HarnessInstructionTag}>", continuation, StringComparison.Ordinal);
            Assert.Contains($"</{AgentLoop.HarnessInstructionTag}>", continuation, StringComparison.Ordinal);
            Assert.Contains("Continue the task.", continuation, StringComparison.Ordinal);

            // The instruction is inside the marker, not merely somewhere in the same turn.
            var open = continuation.IndexOf($"<{AgentLoop.HarnessInstructionTag}>", StringComparison.Ordinal);
            var close = continuation.IndexOf($"</{AgentLoop.HarnessInstructionTag}>", StringComparison.Ordinal);
            var instruction = continuation.IndexOf("Continue the task.", StringComparison.Ordinal);
            Assert.InRange(instruction, open, close);
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

            var continuation = LastUserText(model.Calls[1]);

            Assert.Contains("harness", continuation, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not from any source document", continuation, StringComparison.Ordinal);
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

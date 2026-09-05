using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T008 — hermetic contract tests for the ADR-012 record/replay seam at the
/// <see cref="IModelClient"/> port: capture→replay round-trip fidelity, first-divergence
/// mismatch (FR-010), exhausted recordings, schema validity, and the composition root's
/// fail-fast both-env-vars configuration error.
/// </summary>
public class ReplayAdapterTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "grimoire-replay-adapter", Guid.NewGuid().ToString("N"));

    public ReplayAdapterTests()
    {
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private string CaptureTwoTurnRecording(out List<ConversationMessage[]> conversations)
    {
        var capturePath = Path.Combine(_scratch, "sample-01.json");
        var fake = new FakeModelClient(
        [
            FakeModelClient.WriteFileTurn("t1", "concepts/example.md", "content"),
            FakeModelClient.FinalTurn("done"),
        ]);
        var capture = new TurnCaptureModelClient(fake, capturePath);

        var firstConversation = new[] { new ConversationMessage("user", "hello agent") };
        var turn1 = capture.NextTurnAsync("system prompt", firstConversation, ToolStubs.Tools, CancellationToken.None).Result;

        var secondConversation = new[]
        {
            firstConversation[0],
            new ConversationMessage("assistant", [new ConversationToolUseBlock("t1", "write_file", turn1.ToolUseRequests[0].InputJson)]),
            new ConversationMessage("user", [new ConversationToolResultBlock("t1", false, "ok")]),
        };
        _ = capture.NextTurnAsync("system prompt", secondConversation, ToolStubs.Tools, CancellationToken.None).Result;

        conversations = [firstConversation, secondConversation];
        return capturePath;
    }

    [Fact]
    public async Task CaptureThenReplay_RoundTrip_ServesIdenticalTurns()
    {
        var capturePath = CaptureTwoTurnRecording(out var conversations);

        var replay = new ReplayModelClient(capturePath);

        var turn1 = await replay.NextTurnAsync("system prompt", conversations[0], ToolStubs.Tools, CancellationToken.None);
        Assert.Equal(ModelStopReason.ToolUse, turn1.StopReason);
        var toolUse = Assert.Single(turn1.ToolUseRequests);
        Assert.Equal("write_file", toolUse.ToolName);
        Assert.Equal("t1", toolUse.ToolUseId);

        var turn2 = await replay.NextTurnAsync("system prompt", conversations[1], ToolStubs.Tools, CancellationToken.None);
        Assert.Equal(ModelStopReason.EndTurn, turn2.StopReason);
        Assert.Equal("done", turn2.AssistantText);
    }

    [Fact]
    public async Task Replay_DivergentConversation_ThrowsMismatchNamingTurnAndComponent()
    {
        var capturePath = CaptureTwoTurnRecording(out _);

        var replay = new ReplayModelClient(capturePath);
        var divergent = new[] { new ConversationMessage("user", "different content") };

        var exception = await Assert.ThrowsAsync<ReplayMismatchException>(
            () => replay.NextTurnAsync("system prompt", divergent, ToolStubs.Tools, CancellationToken.None));

        Assert.Equal(1, exception.Turn);
        Assert.StartsWith("conversation[0]", exception.Component, StringComparison.Ordinal);
        Assert.Contains("replay_mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_DivergentSystemPrompt_ThrowsMismatch()
    {
        var capturePath = CaptureTwoTurnRecording(out var conversations);

        var replay = new ReplayModelClient(capturePath);

        var exception = await Assert.ThrowsAsync<ReplayMismatchException>(
            () => replay.NextTurnAsync("DIFFERENT system prompt", conversations[0], ToolStubs.Tools, CancellationToken.None));

        Assert.Equal("system_prompt", exception.Component);
    }

    [Fact]
    public async Task Replay_ExhaustedRecording_ThrowsMismatch()
    {
        var capturePath = CaptureTwoTurnRecording(out var conversations);
        var replay = new ReplayModelClient(capturePath);
        _ = await replay.NextTurnAsync("system prompt", conversations[0], ToolStubs.Tools, CancellationToken.None);
        _ = await replay.NextTurnAsync("system prompt", conversations[1], ToolStubs.Tools, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReplayMismatchException>(
            () => replay.NextTurnAsync("system prompt", conversations[1], ToolStubs.Tools, CancellationToken.None));

        Assert.Equal("turn_count", exception.Component);
    }

    [Fact]
    public async Task Capture_RejectsATurnWithAnIncompleteToolCall_RatherThanRecordingIt()
    {
        // #173 (Copilot review on #178): QueryRecordedTurn has no field for
        // ModelTurn.HasIncompleteToolCall, and ReplayModelClient always reconstructs it as
        // false. Capturing this turn as-is would produce a recording that replays a
        // different harness nudge than the live run actually took, diverging on the very
        // next turn's conversation-hash check. Capture must fail outright instead of
        // writing a recording it cannot faithfully replay.
        var capturePath = Path.Combine(_scratch, "sample-incomplete.json");
        var incompleteTurn = new ModelTurn(
            AssistantText: null,
            ToolUseRequests: [],
            StopReason: ModelStopReason.MaxTokens,
            InputTokens: 100,
            OutputTokens: 50,
            HasIncompleteToolCall: true);
        var fake = new FakeModelClient([incompleteTurn]);
        var capture = new TurnCaptureModelClient(fake, capturePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => capture.NextTurnAsync(
            "system prompt", [new ConversationMessage("user", "hello agent")], ToolStubs.Tools, CancellationToken.None));

        Assert.Contains("incomplete or invalid", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(capturePath), "A rejected turn must not be persisted.");
    }

    [Fact]
    public async Task Capture_DeletesAnAlreadyPersistedPrefix_WhenALaterTurnHasAnIncompleteToolCall()
    {
        // Copilot review on #178: the file is rewritten after every successful turn, so by
        // the time an incomplete tool call shows up on turn 2+, turn 1 is already on disk —
        // a schema-valid, complete-looking recording that a pipeline checking only "does the
        // sample file exist" would publish as a trustworthy (if shorter) sample. Rejecting
        // just the offending turn isn't enough; the stale prefix has to go too.
        var capturePath = Path.Combine(_scratch, "sample-multi-turn-incomplete.json");
        var fake = new FakeModelClient(
        [
            FakeModelClient.WriteFileTurn("t1", "concepts/example.md", "content"),
            new ModelTurn(
                AssistantText: null,
                ToolUseRequests: [],
                StopReason: ModelStopReason.MaxTokens,
                InputTokens: 100,
                OutputTokens: 50,
                HasIncompleteToolCall: true),
        ]);
        var capture = new TurnCaptureModelClient(fake, capturePath);

        var firstConversation = new[] { new ConversationMessage("user", "hello agent") };
        var turn1 = await capture.NextTurnAsync("system prompt", firstConversation, ToolStubs.Tools, CancellationToken.None);
        Assert.True(File.Exists(capturePath), "The first, complete turn should have persisted a prefix.");

        var secondConversation = new ConversationMessage[]
        {
            firstConversation[0],
            new("assistant", [new ConversationToolUseBlock("t1", "write_file", turn1.ToolUseRequests[0].InputJson)]),
            new("user", [new ConversationToolResultBlock("t1", false, "ok")]),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => capture.NextTurnAsync(
            "system prompt", secondConversation, ToolStubs.Tools, CancellationToken.None));

        Assert.Contains("incomplete or invalid", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(capturePath), "The stale complete-looking prefix must not survive a later rejection.");
    }

    [Fact]
    public void CaptureFile_IsSchemaValid_WithRequestHashesAndVerbatimResponses()
    {
        var capturePath = CaptureTwoTurnRecording(out _);

        var sample = RecordingSerialization.Load(capturePath);

        Assert.Equal(RecordingSerialization.CurrentSchemaVersion, sample.SchemaVersion);
        Assert.Equal("fake-model", sample.Model);
        Assert.Equal(2, sample.Turns.Count);
        Assert.All(sample.Turns, t => Assert.StartsWith("sha256:", t.SystemPromptSha256, StringComparison.Ordinal));
        Assert.All(sample.Turns, t => Assert.All(t.Conversation, m => Assert.StartsWith("sha256:", m.ContentSha256, StringComparison.Ordinal)));
        Assert.Equal("tool_use", sample.Turns[0].StopReason);
        Assert.Equal("done", sample.Turns[1].AssistantText);
    }

    [Fact]
    public async Task Agent_BothReplayAndCaptureEnvSet_ExitsNonZeroNamingTheConflict()
    {
        var wikiRoot = Path.Combine(_scratch, "wiki");
        Directory.CreateDirectory(wikiRoot);
        Directory.CreateDirectory(Path.Combine(wikiRoot, "tasks"));
        var agentDir = Path.Combine(_scratch, "agents");
        Directory.CreateDirectory(agentDir);
        File.WriteAllText(Path.Combine(agentDir, "system-prompt.md"), "prompt");
        File.WriteAllText(Path.Combine(agentDir, "default-user-prompt.md"), "user prompt");
        File.WriteAllText(Path.Combine(agentDir, "policy.json"), "{}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Grimoire.EvalRunner.Workspace.IngestAgentProcessInvoker.ResolveAgentDllPath(
            Grimoire.EvalRunner.Workspace.EvalPaths.Discover().RepoRoot));
        foreach (var (name, value) in new[]
        {
            ("--task-id", "conflict-probe"),
            ("--source-ref", "eval://conflict"),
            ("--source-kind", "pasted_text"),
            ("--wiki-root", wikiRoot),
            ("--content-root", wikiRoot),
            ("--tasks-dir", Path.Combine(wikiRoot, "tasks")),
            ("--index-path", Path.Combine(wikiRoot, "index.md")),
            ("--log-path", Path.Combine(wikiRoot, "log.md")),
            ("--system-prompt-path", Path.Combine(agentDir, "system-prompt.md")),
            ("--default-user-prompt-path", Path.Combine(agentDir, "default-user-prompt.md")),
            ("--policy-path", Path.Combine(agentDir, "policy.json")),
            ("--write-locks-dir", Path.Combine(_scratch, "write-locks")),
        })
        {
            startInfo.ArgumentList.Add(name);
            startInfo.ArgumentList.Add(value);
        }

        startInfo.Environment["GRIMOIRE_MODEL_REPLAY_PATH"] = Path.Combine(_scratch, "r.json");
        startInfo.Environment["GRIMOIRE_MODEL_CAPTURE_PATH"] = Path.Combine(_scratch, "c.json");
        startInfo.Environment.Remove("ANTHROPIC_AUTH_TOKEN");

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Write("probe content");
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        Assert.NotEqual(0, process.ExitCode);

        var artifactPath = Path.Combine(wikiRoot, "tasks", "conflict-probe.md");
        Assert.True(File.Exists(artifactPath), "The failed run must still write its task artifact.");
        var artifact = File.ReadAllText(artifactPath);
        Assert.Contains("GRIMOIRE_MODEL_REPLAY_PATH", artifact, StringComparison.Ordinal);
        Assert.Contains("GRIMOIRE_MODEL_CAPTURE_PATH", artifact, StringComparison.Ordinal);
    }

    private static class ToolStubs
    {
        public static readonly IReadOnlyList<ToolDefinition> Tools =
        [
            new ToolDefinition("write_file", "writes", """{"type":"object"}"""),
        ];
    }
}

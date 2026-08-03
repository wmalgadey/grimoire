using System.Security.Cryptography;
using System.Text;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T026 (US1, SC-001/FR-003) — the Query System Prompt Document is loaded verbatim with
/// its SHA-256 recorded, and a missing/unreadable/empty document fails the turn before
/// any agent output, with a human-readable reason recorded on the Query Run Artifact
/// (via the terminal <c>failed</c> event's <c>reason</c> field, R3: the Hub finalizes
/// the artifact from the event stream, since the agent process never writes anything).
/// </summary>
public class QueryInstructionLoadTests
{
    [Fact]
    public async Task RealQuerySystemPrompt_LoadsVerbatim_WithSha256Recorded()
    {
        var repoRoot = FindRepositoryRoot();
        var systemPromptPath = Path.Combine(repoRoot, "data", "agents", "query", "system-prompt.md");
        Assert.True(File.Exists(systemPromptPath), $"Expected the real Query system prompt at '{systemPromptPath}'.");

        var loader = new SystemPromptLoader();
        var result = await loader.LoadAsync(systemPromptPath, CancellationToken.None);

        Assert.True(result.IsFirst(out var loaded));
        var expectedContent = await File.ReadAllTextAsync(systemPromptPath);
        Assert.Equal(expectedContent, loaded!.Content);

        var expectedSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedContent)));
        Assert.Equal(expectedSha256, loaded.Sha256);
    }

    [Fact]
    public async Task Loader_FailsClosed_WhenQuerySystemPromptMissingUnreadableOrWhitespaceOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"query-instruction-load-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var loader = new SystemPromptLoader();

            var missingPath = Path.Combine(root, "missing", "system-prompt.md");
            var missing = await loader.LoadAsync(missingPath, CancellationToken.None);
            Assert.True(missing.IsSecond(out var missingFailure));
            Assert.Contains("not found", missingFailure!.Reason, StringComparison.OrdinalIgnoreCase);

            var whitespacePath = Path.Combine(root, "whitespace-system-prompt.md");
            await File.WriteAllTextAsync(whitespacePath, " \n\r\t ");
            var whitespace = await loader.LoadAsync(whitespacePath, CancellationToken.None);
            Assert.True(whitespace.IsSecond(out var whitespaceFailure));
            Assert.Contains("whitespace", whitespaceFailure!.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EndToEnd_InstructionLoadFailure_MarksTurnFailed_WithReasonOnConversationRecord_AndNoAnswer()
    {
        var reason = "Instruction document not found at 'agents/query/system-prompt.md'. Cannot start a run without agent operating rules.";
        var launcher = new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: reason, autoPlay: true);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);

        var coordinator = host.Services.GetRequiredService<Grimoire.Hub.QueryDispatch.QueryRunCoordinator>();
        var submission = await coordinator.SubmitTurnAsync("c-fail", "What is in the wiki?");
        var accepted = Assert.IsType<Grimoire.Hub.QueryDispatch.QuerySubmissionResult.Accepted>(submission);
        var turnId = accepted.Turn.TurnId;

        // Wait for the fake's auto-play terminal event to propagate.
        await PollAsync.WaitAsync(
            () => coordinator.GetTurn(turnId) is { Status: Grimoire.Hub.QueryDispatch.QueryTurnStatus.Failed },
            TimeSpan.FromSeconds(5),
            $"Expected turn '{turnId}' to reach Failed status within 5s.");
        var turn = coordinator.GetTurn(turnId);

        Assert.NotNull(turn);
        Assert.Equal(Grimoire.Hub.QueryDispatch.QueryTurnStatus.Failed, turn!.Status);
        Assert.Equal(reason, turn.FailureReason);
        Assert.Equal(string.Empty, turn.Answer);

        // The turn's in-memory status flips synchronously before FinishTurnAsync's own
        // await of the record append completes (QueryRunCoordinator.FinishTurnAsync),
        // so the file write is still a separate, slightly-later async operation — poll
        // for it too rather than assuming it's already on disk the instant Status flips.
        // 011-query-conversations (T019 cutover): the reason lands in the Conversation
        // Record's turn bookkeeping instead of the retired per-turn artifact.
        var resolvedPaths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = resolvedPaths.ConversationRecordPathFor("c-fail");
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath),
            TimeSpan.FromSeconds(5),
            $"Expected the Conversation Record at '{recordPath}' to exist within 5s.");

        Assert.True(File.Exists(recordPath));
        var record = await File.ReadAllTextAsync(recordPath);
        var parsed = Assert.IsType<Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed>(
            Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(record));
        var recordedTurn = Assert.Single(parsed.Turns);
        Assert.Equal("failed", recordedTurn.State);
        Assert.Equal(reason, recordedTurn.FailureReason);
        // Pre-load failure: nullable instruction identity, empty answer, block parseable.
        Assert.Null(recordedTurn.InstructionFileSha256);
        Assert.Equal(string.Empty, recordedTurn.Answer);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "data", "agents", "query")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root for Query instruction-load tests.");
    }
}

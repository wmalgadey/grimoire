using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.QueryConversations;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T029/T030 (US2, SC-001 field-level / SC-002) — every field of data-model.md's Turn
/// Bookkeeping table equals the scripted terminal metadata and submitted prompt for
/// each terminal state; denied tool actions are preserved verbatim from the ADR-006
/// terminal-event metadata, including hostile target strings that must survive the
/// escape/unescape round-trip without terminating the bookkeeping comment early.
/// </summary>
public class QueryConversationRecordBookkeepingTests
{
    [Fact]
    public async Task DeniedActions_MatchTheScriptedDenialsExactly_IncludingHostileStrings()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-denials", "Read something out of scope?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "I could not read that file." });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);
        handle.EmitEvent("completed", turnId, new
        {
            summary = "done",
            deniedActions = new object[]
            {
                new
                {
                    action = "read_file",
                    requestedTarget = "../secrets/.env",
                    canonicalTarget = "/base/secrets/.env",
                    reason = "outside read scope",
                    turn = 2,
                },
                new
                {
                    action = "read_file",
                    requestedTarget = "--> \"hostile\" target\nwith newline",
                    canonicalTarget = "/canonical/--> path",
                    reason = "reason with --> terminator and \"quotes\"",
                    turn = 3,
                },
            }
        });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-denials");
        // 019-fast-test-tier (ADR-021 edge case: genuine race surfaced by suite
        // parallelization, fixed at the root): File.Exists becoming true does not mean the
        // write has finished — poll for a successful structured parse too, not just
        // existence, to avoid reading mid-write content under heavy concurrent-suite load.
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath) && QueryConversationRecordFormat.Parse(File.ReadAllText(recordPath)) is QueryConversationRecordParseResult.Parsed { Turns.Count: >= 1 },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to exist and parse with at least one turn within 10s.");

        var content = await File.ReadAllTextAsync(recordPath);

        // The hostile strings cannot terminate the bookkeeping comment early: the only
        // literal '-->' occurrences on disk are each block's real closing line.
        var closeCount = content.Split("-->").Length - 1;
        Assert.Equal(1, closeCount);

        var parsed = Assert.IsType<QueryConversationRecordParseResult.Parsed>(QueryConversationRecordFormat.Parse(content));
        var recordedTurn = Assert.Single(parsed.Turns);
        Assert.Equal(2, recordedTurn.DeniedActions.Count);

        Assert.Equal(
            new QueryRecordedDeniedAction("read_file", "../secrets/.env", "/base/secrets/.env", "outside read scope", 2),
            recordedTurn.DeniedActions[0]);
        Assert.Equal(
            new QueryRecordedDeniedAction(
                "read_file",
                "--> \"hostile\" target\nwith newline",
                "/canonical/--> path",
                "reason with --> terminator and \"quotes\"",
                3),
            recordedTurn.DeniedActions[1]);
    }

    [Fact]
    public async Task CompletedTurn_BookkeepingCarriesEveryFieldOfTheTerminalMetadata()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-bk-completed", "All fields recorded?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "Yes, all of them." });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);
        handle.EmitEvent("completed", turnId, new
        {
            summary = "done",
            systemPromptSha256 = "sha-sp-1",
            policyPath = "agents/query/policy.json",
            policyVersion = 3,
            policySha256 = "sha-pol-1",
            model = "claude-sonnet-4-5",
            turnsUsed = 7,
        });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-bk-completed");

        Assert.Equal(turnId, recordedTurn.TurnId);
        Assert.Equal(1, recordedTurn.Position);
        Assert.Equal("completed", recordedTurn.State);
        Assert.Null(recordedTurn.FailureReason);
        Assert.NotNull(recordedTurn.CompletedAt);
        Assert.True(recordedTurn.StartedAt <= recordedTurn.CompletedAt);
        Assert.Equal("claude-sonnet-4-5", recordedTurn.Model);
        Assert.Equal(7, recordedTurn.TurnsUsed);
        Assert.Equal("agents/query/system-prompt.md", recordedTurn.InstructionFilePath);
        Assert.Equal("sha-sp-1", recordedTurn.InstructionFileSha256);
        Assert.Equal("agents/query/policy.json", recordedTurn.PolicyPath);
        Assert.Equal(3, recordedTurn.PolicyVersion);
        Assert.Equal("sha-pol-1", recordedTurn.PolicySha256);
        Assert.Empty(recordedTurn.DeniedActions);
        Assert.Equal("All fields recorded?", recordedTurn.Prompt);
        Assert.Equal("Yes, all of them.", recordedTurn.Answer);
    }

    /// <summary>
    /// 025-agent-owned-log T031 (FR-012, SC-008): a turn that answers without writing any
    /// wiki content stays fully accounted for in the conversation record — its completion,
    /// its correlation reference, and the fact that it produced no wiki changes are all
    /// discoverable <em>without consulting the wiki's activity log</em>. This is the
    /// safety net for deleting the harness fallback entry: FR-012 requires the existing
    /// coverage be confirmed by test rather than replaced.
    /// </summary>
    [Fact]
    public async Task NoWriteTurn_IsFullyAccountedForInTheConversationRecord_WithoutTheActivityLog()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(
            client, "c-bk-no-write", "What do we already know about retrieval patterns?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "Answered from the existing pages." });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);
        handle.EmitEvent("completed", turnId, new
        {
            summary = "routine lookup",
            systemPromptSha256 = "sha-sp-1",
            policyPath = "agents/query/policy.json",
            policyVersion = 3,
            policySha256 = "sha-pol-1",
            model = "claude-sonnet-4-5",
            turnsUsed = 2,
            createdArtifacts = Array.Empty<string>(),
        });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "completed");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-bk-no-write");

        // The run happened, completed, and is correlatable — all from the record alone.
        Assert.Equal(turnId, recordedTurn.TurnId);
        Assert.Equal("completed", recordedTurn.State);
        Assert.NotNull(recordedTurn.CompletedAt);
        Assert.Null(recordedTurn.FailureReason);
        Assert.Equal("What do we already know about retrieval patterns?", recordedTurn.Prompt);
        Assert.Equal("Answered from the existing pages.", recordedTurn.Answer);

        // ...and it hit no guardrail, so nothing about the turn is unexplained.
        Assert.Empty(recordedTurn.DeniedActions);

        // The activity log was never created: no agent wrote it, and no harness component
        // can (FR-001). Its absence costs nothing above.
        Assert.False(File.Exists(Path.Combine(root, "log.md")));
    }

    [Fact]
    public async Task InterruptedTurn_BookkeepingCarriesStateTimestampsAndNullableMetadata()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-bk-interrupted", "Interrupted?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "Partial " });
        await QueryConversationRecordLifecycleTests.WaitForAnswerAsync(client, turnId);
        (await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null)).EnsureSuccessStatusCode();
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "interrupted");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-bk-interrupted");

        Assert.Equal("interrupted", recordedTurn.State);
        Assert.Null(recordedTurn.FailureReason);
        Assert.NotNull(recordedTurn.CompletedAt);
        Assert.True(recordedTurn.StartedAt <= recordedTurn.CompletedAt);
        // No terminal event reached the Hub: model-loop metadata is nullable.
        Assert.Null(recordedTurn.Model);
        Assert.Null(recordedTurn.TurnsUsed);
        Assert.Null(recordedTurn.InstructionFilePath);
        Assert.Null(recordedTurn.InstructionFileSha256);
        Assert.Equal("Partial ", recordedTurn.Answer);
    }

    [Fact]
    public async Task FailedTurn_BookkeepingCarriesReasonAndMetadata_FromTheFailedEvent()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-bk-failed", "Fails with metadata?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("failed", turnId, new
        {
            reason = "Model loop exhausted its turn cap.",
            systemPromptSha256 = "sha-sp-2",
            policyPath = "agents/query/policy.json",
            policyVersion = 1,
            policySha256 = "sha-pol-2",
            model = "claude-sonnet-4-5",
            turnsUsed = 12,
        });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "failed");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-bk-failed");

        Assert.Equal("failed", recordedTurn.State);
        Assert.Equal("Model loop exhausted its turn cap.", recordedTurn.FailureReason);
        Assert.Equal("sha-sp-2", recordedTurn.InstructionFileSha256);
        Assert.Equal(1, recordedTurn.PolicyVersion);
        Assert.Equal("sha-pol-2", recordedTurn.PolicySha256);
        Assert.Equal("claude-sonnet-4-5", recordedTurn.Model);
        Assert.Equal(12, recordedTurn.TurnsUsed);
        Assert.Equal(string.Empty, recordedTurn.Answer);
    }

    // T030: an instruction-load-failure turn (terminal `failed` event carrying no
    // instruction identity) records nullable identity without breaking the block.
    [Fact]
    public async Task InstructionLoadFailure_RecordsNullableInstructionIdentity_BlockStillParseable()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var turnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-bk-preload", "Instructions missing?");
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("failed", turnId, new { reason = "Instruction document not found." });
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, turnId, "failed");

        var recordedTurn = await ReadSingleTurnAsync(root, "c-bk-preload");

        Assert.Equal("failed", recordedTurn.State);
        Assert.Equal("Instruction document not found.", recordedTurn.FailureReason);
        Assert.Null(recordedTurn.InstructionFilePath);
        Assert.Null(recordedTurn.InstructionFileSha256);
        Assert.Null(recordedTurn.PolicyPath);
        Assert.Null(recordedTurn.PolicyVersion);
        Assert.Null(recordedTurn.PolicySha256);
    }

    private static async Task<QueryRecordedTurn> ReadSingleTurnAsync(string root, string conversationId)
    {
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor(conversationId);

        // 019-fast-test-tier (ADR-021 edge case: genuine race surfaced by suite
        // parallelization, fixed at the root): the turn's in-memory status flips before
        // FinishTurnAsync's own await of the record append completes, so File.Exists
        // becoming true does not mean the write has finished — under heavy concurrent-suite
        // CPU/IO contention a read can land mid-write and see an Unreadable/incomplete
        // parse. Poll until the file both exists and parses with the expected turn, not
        // just until it exists.
        QueryConversationRecordParseResult.Parsed? parsed = null;
        await PollAsync.WaitAsync(
            async () =>
            {
                if (!File.Exists(recordPath))
                {
                    return false;
                }

                return QueryConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)) is QueryConversationRecordParseResult.Parsed { Turns.Count: >= 1 } p
                    && (parsed = p) is not null;
            },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to exist and parse with at least one turn within 10s.");

        return Assert.Single(parsed!.Turns);
    }
}

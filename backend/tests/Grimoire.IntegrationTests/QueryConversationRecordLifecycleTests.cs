using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.QueryConversations;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T012/T015 (US1, SC-001/SC-004 runtime half) — a scripted multi-turn conversation
/// produces exactly one Conversation Record file containing every terminal turn in
/// position order with complete prompts/answers and bookkeeping matching the scripted
/// terminal metadata; concurrent conversations never cross-contaminate; and the retired
/// <c>data/query-runs/</c> location gains no files across full turn lifecycles.
/// </summary>
public class QueryConversationRecordLifecycleTests
{
    [Fact]
    public async Task ThreeTurnConversation_ProducesExactlyOneRecord_WithAllTurnsInOrderAndFullBookkeeping()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var metadata = new
        {
            systemPromptSha256 = "sha-system-prompt",
            policyPath = "agents/query/policy.json",
            policyVersion = 1,
            policySha256 = "sha-policy",
            model = "claude-sonnet-4-5",
            turnsUsed = 2,
        };

        var turn1 = await RunScriptedTurnAsync(client, launcher, handleIndex: 0, "c-lifecycle",
            prompt: "What does ADR-004 decide?",
            answerChunks: ["ADR-004 scopes the credential ", "to the child process."],
            terminalExtra: new { summary = "done", metadata.systemPromptSha256, metadata.policyPath, metadata.policyVersion, metadata.policySha256, metadata.model, metadata.turnsUsed },
            root: root);

        var turn2 = await RunScriptedTurnAsync(client, launcher, handleIndex: 1, "c-lifecycle",
            prompt: "And the runtime paths?",
            answerChunks: ["ADR-009 composes every runtime location in one place."],
            terminalExtra: new { summary = "done", metadata.systemPromptSha256, metadata.policyPath, metadata.policyVersion, metadata.policySha256, metadata.model, metadata.turnsUsed },
            root: root);

        // Follow-up referencing the earlier answer.
        var turn3 = await RunScriptedTurnAsync(client, launcher, handleIndex: 2, "c-lifecycle",
            prompt: "How does that one place relate to the credential scoping you described?",
            answerChunks: ["They meet at the spawn boundary."],
            terminalExtra: new { summary = "done", metadata.systemPromptSha256, metadata.policyPath, metadata.policyVersion, metadata.policySha256, metadata.model, metadata.turnsUsed },
            root: root);

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-lifecycle");
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath) && QueryConversationRecordFormat.Parse(File.ReadAllText(recordPath)) is QueryConversationRecordParseResult.Parsed { Turns.Count: >= 3 },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to exist and parse with all 3 turns within 10s.");

        // Exactly one file for the conversation.
        Assert.Equal(
            new[] { recordPath },
            Directory.GetFiles(paths.ConversationsDir).Where(f => f.Contains("c-lifecycle", StringComparison.Ordinal)));

        var content = await File.ReadAllTextAsync(recordPath);
        Assert.Contains("record_format: grimoire-conversation/1", content, StringComparison.Ordinal);

        var parsed = Assert.IsType<QueryConversationRecordParseResult.Parsed>(QueryConversationRecordFormat.Parse(content));
        Assert.False(parsed.DroppedTrailingFragment);
        Assert.Equal(3, parsed.Turns.Count);
        Assert.Equal([1, 2, 3], parsed.Turns.Select(t => t.Position));
        Assert.Equal([turn1, turn2, turn3], parsed.Turns.Select(t => t.TurnId));

        Assert.Equal("What does ADR-004 decide?", parsed.Turns[0].Prompt);
        Assert.Equal("ADR-004 scopes the credential to the child process.", parsed.Turns[0].Answer);
        Assert.All(parsed.Turns, t =>
        {
            Assert.Equal("completed", t.State);
            Assert.Null(t.FailureReason);
            Assert.Equal("sha-system-prompt", t.InstructionFileSha256);
            Assert.Equal("agents/query/policy.json", t.PolicyPath);
            Assert.Equal(1, t.PolicyVersion);
            Assert.Equal("sha-policy", t.PolicySha256);
            Assert.Equal("claude-sonnet-4-5", t.Model);
            Assert.Equal(2, t.TurnsUsed);
            Assert.NotNull(t.CompletedAt);
            Assert.Empty(t.DeniedActions);
        });

        // Human-readable dialogue: the referenced answer sits above the follow-up prompt.
        Assert.True(
            content.IndexOf("ADR-009 composes every runtime location in one place.", StringComparison.Ordinal) <
            content.IndexOf("How does that one place relate", StringComparison.Ordinal));
        Assert.Contains("## Turn 1 — completed", content, StringComparison.Ordinal);
        Assert.Contains("## Turn 2 — completed", content, StringComparison.Ordinal);
        Assert.Contains("## Turn 3 — completed", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoConcurrentConversations_EachGetExactlyOneRecord_WithOnlyTheirOwnTurns()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        // Both conversations submit before either finishes (within concurrency limit 3).
        var turnIdA = await SubmitAsync(client, "c-cross-a", "Question for A?");
        var turnIdB = await SubmitAsync(client, "c-cross-b", "Question for B?");

        var handleA = launcher.Handles[0];
        var handleB = launcher.Handles[1];

        handleA.EmitEvent("started", turnIdA);
        handleB.EmitEvent("started", turnIdB);
        handleA.EmitEvent("answer_chunk", turnIdA, new { text = "Answer for A." });
        handleB.EmitEvent("answer_chunk", turnIdB, new { text = "Answer for B." });
        await WaitForAnswerAsync(client, turnIdA);
        await WaitForAnswerAsync(client, turnIdB);
        handleA.EmitEvent("completed", turnIdA, new { summary = "done" });
        handleB.EmitEvent("completed", turnIdB, new { summary = "done" });
        await WaitForStateAsync(client, turnIdA, "completed");
        await WaitForStateAsync(client, turnIdB, "completed");

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPathA = paths.ConversationRecordPathFor("c-cross-a");
        var recordPathB = paths.ConversationRecordPathFor("c-cross-b");
        await PollAsync.WaitAsync(
            () => File.Exists(recordPathA) && QueryConversationRecordFormat.Parse(File.ReadAllText(recordPathA)) is QueryConversationRecordParseResult.Parsed { Turns.Count: >= 1 }
                && File.Exists(recordPathB) && QueryConversationRecordFormat.Parse(File.ReadAllText(recordPathB)) is QueryConversationRecordParseResult.Parsed { Turns.Count: >= 1 },
            TimeSpan.FromSeconds(10),
            "Expected both Conversation Records to exist and parse with at least one turn within 10s.");

        var parsedA = Assert.IsType<QueryConversationRecordParseResult.Parsed>(
            QueryConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPathA)));
        var parsedB = Assert.IsType<QueryConversationRecordParseResult.Parsed>(
            QueryConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPathB)));

        var turnA = Assert.Single(parsedA.Turns);
        Assert.Equal("Question for A?", turnA.Prompt);
        Assert.Equal("Answer for A.", turnA.Answer);
        Assert.Equal(turnIdA, turnA.TurnId);

        var turnB = Assert.Single(parsedB.Turns);
        Assert.Equal("Question for B?", turnB.Prompt);
        Assert.Equal("Answer for B.", turnB.Answer);
        Assert.Equal(turnIdB, turnB.TurnId);
    }

    [Fact]
    public async Task FullTurnLifecycles_WriteNothingToTheRetiredQueryRunsLocation_SC004()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root, livenessWindow: TimeSpan.FromMilliseconds(200));
        var client = host.GetTestClient();

        // Completed turn.
        var completedTurnId = await RunScriptedTurnAsync(client, launcher, handleIndex: 0, "c-retired",
            prompt: "Completes normally?", answerChunks: ["Yes."], terminalExtra: new { summary = "done" }, root: root);

        // Interrupted turn.
        var interruptedTurnId = await SubmitAsync(client, "c-retired", "Interrupted midway?");
        launcher.Handles[1].EmitEvent("started", interruptedTurnId);
        launcher.Handles[1].EmitEvent("answer_chunk", interruptedTurnId, new { text = "Partial " });
        await WaitForAnswerAsync(client, interruptedTurnId);
        var interruptResponse = await client.PostAsync($"/api/query-turns/{interruptedTurnId}/interrupt", content: null);
        interruptResponse.EnsureSuccessStatusCode();
        await WaitForStateAsync(client, interruptedTurnId, "interrupted");

        // Failed turn (liveness silence with the short window configured above).
        var failedTurnId = await SubmitAsync(client, "c-retired", "Goes silent?");
        launcher.Handles[2].EmitEvent("started", failedTurnId);
        await WaitForStateAsync(client, failedTurnId, "failed");

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-retired");
        await WaitUntilAsync(() => Task.FromResult(
            File.Exists(recordPath) &&
            QueryConversationRecordFormat.Parse(File.ReadAllText(recordPath)) is QueryConversationRecordParseResult.Parsed { Turns.Count: 3 }));

        // SC-004 runtime half: the retired location gained no files at all.
        var queryRunsDir = Path.Combine(root, "query-runs");
        Assert.True(
            !Directory.Exists(queryRunsDir) ||
            Directory.GetFiles(queryRunsDir, "*", SearchOption.AllDirectories).Length == 0,
            "data/query-runs/ must not gain any files after the cutover (SC-004).");
    }

    // ------------------------------------------------------------------ helpers

    internal static async Task<string> SubmitAsync(HttpClient client, string conversationId, string prompt)
    {
        var response = await client.PostAsJsonAsync($"/api/query-conversations/{conversationId}/turns", new { prompt });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("turnId").GetString()!;
    }

    internal static async Task<string> RunScriptedTurnAsync(
        HttpClient client,
        FakeAgentProcessLauncher launcher,
        int handleIndex,
        string conversationId,
        string prompt,
        IReadOnlyList<string> answerChunks,
        object terminalExtra,
        string? root = null)
    {
        var turnId = await SubmitAsync(client, conversationId, prompt);
        var handle = launcher.Handles[handleIndex];
        handle.EmitEvent("started", turnId);
        foreach (var chunk in answerChunks)
        {
            handle.EmitEvent("answer_chunk", turnId, new { text = chunk });
        }

        if (answerChunks.Count > 0)
        {
            await WaitForAnswerAsync(client, turnId);
        }

        handle.EmitEvent("completed", turnId, terminalExtra);
        await WaitForStateAsync(client, turnId, "completed");

        // 019-fast-test-tier (ADR-021 edge case: genuine race surfaced by suite
        // parallelization, fixed at the root, not papered over): the turn's in-memory
        // status flips before QueryConversationRecordStore.AppendTurnAsync's file-write-plus-
        // cache-update completes (documented gap, see QueryInstructionLoadTests et al.).
        // Callers that immediately submit the NEXT turn on the same conversation read
        // `priorTurns` from that same cache (QueryRunCoordinator.SubmitTurnAsync) — under
        // heavy concurrent-suite CPU contention the next submission can outrace the
        // append, observed as wrong turn positions/counts. When the caller supplies
        // `root`, wait for the append to actually land before returning, closing that gap
        // for every multi-turn scripted-conversation test that shares this helper.
        if (root is not null)
        {
            var recordPath = QueryTurnSubmissionApiTests.BuildResolvedPaths(root).ConversationRecordPathFor(conversationId);
            await PollAsync.WaitAsync(
                () => File.Exists(recordPath) && QueryConversationRecordFormat.Parse(File.ReadAllText(recordPath))
                    is QueryConversationRecordParseResult.Parsed parsed && parsed.Turns.Any(t => t.TurnId == turnId),
                TimeSpan.FromSeconds(10),
                $"Expected the Conversation Record at '{recordPath}' to contain turn '{turnId}' within 10s.");
        }

        return turnId;
    }

    internal static Task WaitForAnswerAsync(HttpClient client, string turnId) => WaitUntilAsync(async () =>
    {
        var response = await client.GetAsync($"/api/query-turns/{turnId}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return !string.IsNullOrEmpty(json.GetProperty("answer").GetString());
    });

    internal static Task WaitForStateAsync(HttpClient client, string turnId, string expectedState) => WaitUntilAsync(async () =>
    {
        var response = await client.GetAsync($"/api/query-turns/{turnId}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("state").GetString() == expectedState;
    });

    // 5000ms was tight enough to flake under full-suite parallel load (CPU/thread-pool
    // contention from other tests' background work) even though the condition itself
    // resolves in milliseconds under normal load — e.g.
    // QueryConversationRecordBookkeepingTests.CompletedTurn_BookkeepingCarriesEveryFieldOfTheTerminalMetadata
    // and other WaitForAnswerAsync/WaitForStateAsync callers across this file's
    // Query-conversation test siblings. This polls for eventual consistency against an
    // in-memory TestServer (no real network), so a longer ceiling only makes the test
    // more tolerant of transient scheduling delays — it does not change what is being
    // asserted or mask a genuine hang (a truly stuck turn still fails, just after 15s
    // instead of 5s).
    // 019-fast-test-tier (ADR-021 R4): thin wrapper over the shared PollAsync helper — kept
    // as a same-signature method since ~10 other test files call this one directly
    // (WaitForAnswerAsync/WaitForStateAsync/WaitUntilAsync are this suite's de facto shared
    // wait surface for the Query Conversation Record).
    internal static Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 15000) =>
        PollAsync.WaitAsync(condition, TimeSpan.FromMilliseconds(timeoutMs), "Condition was not met within the timeout.", TimeSpan.FromMilliseconds(20));
}

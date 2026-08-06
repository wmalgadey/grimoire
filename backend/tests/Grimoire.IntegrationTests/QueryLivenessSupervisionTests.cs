using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// FR-015/SC-005 (analysis follow-up C2) — a Query agent run that goes silent
/// (crash/hang, no heartbeat/activity, no terminal event) is marked <c>failed</c> with a
/// liveness reason within the configured liveness window, and the leftover agent process
/// is terminated. Mirrors <see cref="IngestRunSupervisionTests"/>'s
/// <c>EventSilence_BeyondLivenessWindow_FailsRun_TerminatesProcess_AndAdvancesQueue</c> for
/// Ingest, applied to <c>QueryRunCoordinator.SuperviseAsync</c>'s watchdog.
/// </summary>
public class QueryLivenessSupervisionTests
{
    private static readonly TimeSpan ShortWindow = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task EventSilence_BeyondLivenessWindow_FailsTurn_AndTerminatesProcess()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot(), livenessWindow: ShortWindow);
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-silent/turns", new { prompt = "What does the wiki say about ADR-004?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        // ... then silence: no heartbeat, no answer_chunk, no terminal event.

        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/query-turns/{turnId}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("state").GetString() == "failed";
        });

        Assert.True(handle.Terminated, "The leftover agent process must be terminated on liveness failure.");

        var finalResponse = await client.GetAsync($"/api/query-turns/{turnId}");
        var finalJson = await finalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", finalJson.GetProperty("state").GetString());
        Assert.Contains("liveness", finalJson.GetProperty("failureReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PipeCloseWithoutTerminalEvent_DoesNotTransition_UntilLivenessWindowFires()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot(), livenessWindow: ShortWindow);
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-crash/turns", new { prompt = "What does the wiki say about ADR-004?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        // Hard crash: the stdout pipe closes without a terminal event.
        handle.ClosePipe();

        // Per ADR-008 the pipe close itself is not a transition — the liveness window is.
        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/query-turns/{turnId}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("state").GetString() == "failed";
        });

        var finalResponse = await client.GetAsync($"/api/query-turns/{turnId}");
        var finalJson = await finalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("liveness", finalJson.GetProperty("failureReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    // T028 (011-query-conversations, US2): a liveness-silence failure appends exactly
    // one block with state: failed, the human-readable liveness reason, and the partial
    // answer produced so far.
    [Fact]
    public async Task LivenessFailure_AppendsFailedBlock_WithReasonAndPartialAnswer()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root, livenessWindow: ShortWindow);
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-liveness-record/turns", new { prompt = "Goes silent midway?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "Partial before the silence" });
        // ... then silence until the watchdog fires.

        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/query-turns/{turnId}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("state").GetString() == "failed";
        });

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-liveness-record");
        // 019-fast-test-tier (ADR-021 edge case: genuine race surfaced by suite
        // parallelization, fixed at the root): File.Exists becoming true does not mean the
        // write has finished — poll for a successful structured parse too.
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath) && Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(File.ReadAllText(recordPath))
                is Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed { Turns.Count: >= 1 },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to exist and parse with at least one turn within 10s.");

        var parsed = Assert.IsType<Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed>(
            Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)));
        var recordedTurn = Assert.Single(parsed.Turns);
        Assert.Equal("failed", recordedTurn.State);
        Assert.Contains("liveness", recordedTurn.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Partial before the silence", recordedTurn.Answer);
    }

    // T028: a turn that fails before producing any output records an empty answer body
    // (answer_chars: 0).
    [Fact]
    public async Task FailureBeforeAnyOutput_RecordsAnEmptyAnswerBody()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root, livenessWindow: ShortWindow);
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-liveness-empty/turns", new { prompt = "Never answers?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        // No answer_chunk at all before the silence.

        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/query-turns/{turnId}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("state").GetString() == "failed";
        });

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-liveness-empty");
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath) && Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(File.ReadAllText(recordPath))
                is Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed { Turns.Count: >= 1 },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to exist and parse with at least one turn within 10s.");

        var content = await File.ReadAllTextAsync(recordPath);
        Assert.Contains("answer_chars: 0", content, StringComparison.Ordinal);
        var parsed = Assert.IsType<Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed>(
            Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(content));
        Assert.Equal(string.Empty, Assert.Single(parsed.Turns).Answer);
    }

    // 019-fast-test-tier (ADR-021 R4): thin wrapper over the shared PollAsync helper —
    // kept as a same-signature local method so every call site above is unchanged.
    private static Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000) =>
        PollAsync.WaitAsync(condition, TimeSpan.FromMilliseconds(timeoutMs), "Condition was not met within the timeout.", TimeSpan.FromMilliseconds(20));
}

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T048 (US2, FR-006/FR-007, SC-004) — interrupting an active turn
/// (<c>POST /api/query-turns/{turnId}/interrupt</c>) halts the agent process via
/// <see cref="ScriptedAgentProcessHandle.Terminate"/> promptly, preserves the buffered
/// partial answer, marks the turn <c>interrupted</c> (not <c>failed</c>); interrupting an
/// already-terminal turn is a no-op that returns the turn's actual current state.
/// Mirrors the <c>IngestRunCoordinator</c> liveness-failure test idiom (R5) applied to
/// user-triggered <c>Terminate()</c> instead of watchdog-triggered.
/// </summary>
public class QueryInterruptionTests
{
    [Fact]
    public async Task Interrupt_ActiveTurn_TerminatesProcess_PreservesPartialAnswer_MarksInterrupted()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        // 019-fast-test-tier (ADR-021 edge case: genuine race surfaced by suite
        // parallelization): this test's liveness window is a background safety-net, not
        // itself under test (unlike Interrupt_AppendsExactlyOneInterruptedBlock_..._
        // EvenRacingSupervision below, which deliberately races a short window). 100ms left
        // no real headroom for the interrupt HTTP round-trip under heavy concurrent-suite
        // CPU contention, so the watchdog could fire first and mark the turn "failed"
        // instead of "interrupted" — observed in practice. Widened, matching this file's
        // own SC-004 budget (interrupt must complete within 2s).
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot(), livenessWindow: TimeSpan.FromSeconds(5));
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-interrupt/turns", new { prompt = "What does the wiki say about ADR-004?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "ADR-004 scopes the credential " });

        await PollAsync.WaitAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/query-turns/{turnId}");
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                return !string.IsNullOrEmpty(json.GetProperty("answer").GetString());
            },
            TimeSpan.FromSeconds(5),
            "Condition was not met within the timeout.");

        // SC-004: interruption must halt answer delivery within 2 seconds.
        var interruptStopwatch = Stopwatch.StartNew();
        var interruptResponse = await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null);
        interruptStopwatch.Stop();
        Assert.Equal(HttpStatusCode.OK, interruptResponse.StatusCode);
        Assert.True(
            interruptStopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"SC-004 budget exceeded: interrupt response took {interruptStopwatch.ElapsedMilliseconds}ms (budget: 2000ms).");

        var interruptJson = await interruptResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(turnId, interruptJson.GetProperty("turnId").GetString());
        Assert.Equal("interrupted", interruptJson.GetProperty("state").GetString());

        // QueryRunCoordinator.InterruptAsync calls Terminate() synchronously before
        // returning (SC-004) — already true by the time the response above arrived.
        Assert.True(handle.Terminated, "Interrupting an active turn must terminate the agent process.");

        var finalResponse = await client.GetAsync($"/api/query-turns/{turnId}");
        var finalJson = await finalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("interrupted", finalJson.GetProperty("state").GetString());
        Assert.Equal("ADR-004 scopes the credential ", finalJson.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Interrupt_AlreadyTerminalTurn_ReturnsActualState_NoOp()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, terminalStatus: "completed");
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-terminal/turns", new { prompt = "What does the wiki say about ADR-004?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        await PollAsync.WaitAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/query-turns/{turnId}");
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                return json.GetProperty("state").GetString() == "completed";
            },
            TimeSpan.FromSeconds(5),
            "Condition was not met within the timeout.");

        var interruptResponse = await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null);
        Assert.Equal(HttpStatusCode.OK, interruptResponse.StatusCode);

        var interruptJson = await interruptResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(turnId, interruptJson.GetProperty("turnId").GetString());
        Assert.Equal("completed", interruptJson.GetProperty("state").GetString());
    }

    // T028 (011-query-conversations, US2): a user-triggered interrupt appends exactly
    // one block with state: interrupted, failure_reason: null, and the accumulated
    // partial answer — and interrupt racing supervision (short liveness window) still
    // yields a single block (first-transition-wins).
    [Trait("TimingDependent", "true")]
    [Fact]
    public async Task Interrupt_AppendsExactlyOneInterruptedBlock_WithPartialAnswer_EvenRacingSupervision()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var root = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root, livenessWindow: TimeSpan.FromMilliseconds(150));
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-interrupt-record/turns", new { prompt = "Interrupted midway?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "The partial answer so " });
        await PollAsync.WaitAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/query-turns/{turnId}");
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                return !string.IsNullOrEmpty(json.GetProperty("answer").GetString());
            },
            TimeSpan.FromSeconds(5),
            "Condition was not met within the timeout.");

        (await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null)).EnsureSuccessStatusCode();

        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        var recordPath = paths.ConversationRecordPathFor("c-interrupt-record");
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath)
                  && Grimoire.Hub.QueryConversations.QueryConversationRecordFormat.Parse(File.ReadAllText(recordPath))
                      is Grimoire.Hub.QueryConversations.QueryConversationRecordParseResult.Parsed { Turns.Count: >= 1 },
            TimeSpan.FromSeconds(5),
            $"Expected the Conversation Record at '{recordPath}' to parse with at least one turn within 5s.");

        // 019-fast-test-tier (ADR-021 R4): letting the liveness watchdog fire well past its
        // window IS the behavior under test (supervision must not append a second block for
        // an already-interrupted turn) — there is no earlier observable signal for "the
        // watchdog chose not to act". Exempt from the fixed-wait ban (FR-005).
        await Task.Delay(500);

        var parsed = Assert.IsType<Grimoire.Hub.QueryConversations.QueryConversationRecordParseResult.Parsed>(
            Grimoire.Hub.QueryConversations.QueryConversationRecordFormat.Parse(await File.ReadAllTextAsync(recordPath)));
        var recordedTurn = Assert.Single(parsed.Turns);
        Assert.Equal(turnId, recordedTurn.TurnId);
        Assert.Equal("interrupted", recordedTurn.State);
        Assert.Null(recordedTurn.FailureReason);
        Assert.Equal("The partial answer so ", recordedTurn.Answer);
        Assert.Equal("Interrupted midway?", recordedTurn.Prompt);
    }

    [Fact]
    public async Task Interrupt_UnknownTurnId_Returns404()
    {
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            new FakeAgentProcessLauncher(), root: QueryTurnSubmissionApiTests.CreateTempRoot());
        var client = host.GetTestClient();

        var response = await client.PostAsync("/api/query-turns/never-submitted/interrupt", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

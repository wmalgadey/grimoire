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
        using var host = await QueryTurnSubmissionApiTests.BuildHostAsync(
            launcher, root: QueryTurnSubmissionApiTests.CreateTempRoot(), livenessWindow: TimeSpan.FromMilliseconds(100));
        var client = host.GetTestClient();

        var submitResponse = await client.PostAsJsonAsync(
            "/api/query-conversations/c-interrupt/turns", new { prompt = "What does the wiki say about ADR-004?" });
        submitResponse.EnsureSuccessStatusCode();
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var turnId = submitJson.GetProperty("turnId").GetString()!;

        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", turnId);
        handle.EmitEvent("answer_chunk", turnId, new { text = "ADR-004 scopes the credential " });

        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/query-turns/{turnId}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return !string.IsNullOrEmpty(json.GetProperty("answer").GetString());
        });

        var interruptResponse = await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null);
        Assert.Equal(HttpStatusCode.OK, interruptResponse.StatusCode);

        var interruptJson = await interruptResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(turnId, interruptJson.GetProperty("turnId").GetString());
        Assert.Equal("interrupted", interruptJson.GetProperty("state").GetString());

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

        await WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/query-turns/{turnId}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("state").GetString() == "completed";
        });

        var interruptResponse = await client.PostAsync($"/api/query-turns/{turnId}/interrupt", content: null);
        Assert.Equal(HttpStatusCode.OK, interruptResponse.StatusCode);

        var interruptJson = await interruptResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(turnId, interruptJson.GetProperty("turnId").GetString());
        Assert.Equal("completed", interruptJson.GetProperty("state").GetString());
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }
}

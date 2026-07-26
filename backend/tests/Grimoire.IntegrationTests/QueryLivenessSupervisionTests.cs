using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// FR-015/SC-005 (analysis follow-up C2) — a Query agent run that goes silent
/// (crash/hang, no heartbeat/activity, no terminal event) is marked <c>failed</c> with a
/// liveness reason within the configured liveness window, and the leftover agent process
/// is terminated. Mirrors <see cref="RunSupervisionTests"/>'s
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

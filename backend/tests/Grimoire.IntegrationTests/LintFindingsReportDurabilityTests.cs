using System.Net;
using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #146, lint side — and unlike the ingest half, a defect an operator can hit directly
/// rather than only in CI: <c>LintRunCoordinator.FinishRunAsync</c> transitioned the run to
/// its terminal status and broadcast it before persisting the Findings Report, so
/// <c>GET /api/lint-runs/{id}</c> could answer <c>completed</c> while
/// <c>GET /api/lint-runs/{id}/findings</c> had nothing to return.
///
/// <para>
/// Asserted on the same response that first reports the terminal status.
/// <c>hasFindingsReport</c> is computed from the run's report path, so a single response
/// carrying <c>status: completed</c> alongside <c>hasFindingsReport: false</c> is the
/// violation — with no second round trip that could race it away. The findings read that
/// follows confirms the report is genuinely retrievable, not merely path-stamped.
/// </para>
/// </summary>
[Trait("TimingDependent", "true")]
public sealed class LintFindingsReportDurabilityTests
{
    [Fact]
    public async Task LintRun_FirstResponseReportingATerminalStatus_AlreadyCarriesItsFindingsReport()
    {
        using var harness = await LintTriggerHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(20)));
        var client = harness.Host.GetTestClient();

        var triggerResponse = await client.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResponse.StatusCode);
        using var acceptedBody = JsonDocument.Parse(await triggerResponse.Content.ReadAsStringAsync());
        var runId = acceptedBody.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));

        // Throttled deliberately. Unlike the ingest queue-drain window, the gap this guards
        // spans a lifecycle broadcast and a file write, so a millisecond between polls still
        // lands inside it — while an unthrottled loop would issue tens of thousands of HTTP
        // requests over the timeout if a regression left the run `running`, starving the
        // tests running alongside it.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string status;
        bool hasFindingsReport;
        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), timeout.Token);

            var runResponse = await client.GetAsync($"/api/lint-runs/{runId}", timeout.Token);
            using var runBody = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync(timeout.Token));
            status = runBody.RootElement.GetProperty("status").GetString()!;
            hasFindingsReport = runBody.RootElement.GetProperty("hasFindingsReport").GetBoolean();

            if (status != "running")
            {
                break;
            }
        }

        Assert.Equal("completed", status);
        Assert.True(
            hasFindingsReport,
            "The run reported a terminal status while its Findings Report was not yet persisted (#146).");

        var findingsResponse = await client.GetAsync($"/api/lint-runs/{runId}/findings", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, findingsResponse.StatusCode);
    }
}

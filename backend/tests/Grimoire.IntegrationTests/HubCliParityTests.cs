using System.Net;
using System.Text.Json;
using Grimoire.Hub.Cli;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.TestHost;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T020 (018-hub-cli-commands, US1, SC-005): proves a lint run triggered via the HTTP
/// endpoint handler path (<see cref="LintSubmissionEndpoints.MapLintRunEndpoints"/>) and
/// one triggered via <see cref="LintRunCommand"/> — against identically seeded harnesses
/// — produce indistinguishable outcomes. Parity holds largely "by construction" here:
/// both entry paths call the exact same <see cref="LintRunCoordinator.TriggerAsync"/>
/// method (verified by reading <c>LintSubmissionEndpoints.PostTriggerAsync</c> and
/// <see cref="LintRunCommand"/> side by side) — this test is the regression guard SC-005
/// asks for, not a discovery mechanism.
///
/// This file is the home for the CLI-vs-HTTP parity matrix across every user story in
/// this feature — later phases (remediation, ingest, query) add their own sections here.
/// </summary>
public class HubCliParityTests
{
    [Fact]
    public async Task LintRun_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        // Identical seed for both harnesses: same scripted terminal metadata, same
        // (short, deterministic) simulated run duration.
        var scriptedMetadata = new Dictionary<string, object?>
        {
            ["systemPromptSha256"] = "parity-test-sha256",
            ["policyPath"] = "agents/lint/policy.json",
            ["policyVersion"] = 1,
            ["policySha256"] = "parity-policy-sha256",
            ["model"] = "claude-parity-test",
            ["turnsUsed"] = 2,
        };
        var simulatedDuration = TimeSpan.FromMilliseconds(50);

        using var httpHarness = await LintTriggerHostHarness.CreateAsync(new FakeAgentProcessLauncher(
            simulatedRunDuration: simulatedDuration) { ScriptedLintTerminalMetadata = scriptedMetadata });
        using var cliHarness = HubCliLintTestHarness.Create(new FakeAgentProcessLauncher(
            simulatedRunDuration: simulatedDuration) { ScriptedLintTerminalMetadata = scriptedMetadata });

        // --- HTTP path: POST to trigger, GET to poll for the terminal state, GET findings. ---
        var httpClient = httpHarness.Host.GetTestClient();
        var triggerResponse = await httpClient.PostAsync("/api/lint-runs/", content: null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResponse.StatusCode);

        using var acceptedBody = JsonDocument.Parse(await triggerResponse.Content.ReadAsStringAsync());
        var httpRunId = acceptedBody.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(httpRunId));

        var httpStatus = "running";
        var httpHasFindingsReport = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (httpStatus == "running" && DateTime.UtcNow < deadline)
        {
            var runResponse = await httpClient.GetAsync($"/api/lint-runs/{httpRunId}");
            using var runBody = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
            httpStatus = runBody.RootElement.GetProperty("status").GetString()!;
            httpHasFindingsReport = runBody.RootElement.GetProperty("hasFindingsReport").GetBoolean();
            if (httpStatus == "running")
            {
                await Task.Delay(25);
            }
        }

        var findingsResponse = await httpClient.GetAsync($"/api/lint-runs/{httpRunId}/findings");
        Assert.Equal(HttpStatusCode.OK, findingsResponse.StatusCode);
        using var findingsBody = JsonDocument.Parse(await findingsResponse.Content.ReadAsStringAsync());
        var httpReportContent = findingsBody.RootElement.GetProperty("content").GetString()!;

        // --- CLI path: the production command, invoked exactly as HubCliApp would. ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunLintRunCommandAsync();
        var cliRunId = cliHarness.Coordinator.LatestRunId;
        Assert.False(string.IsNullOrWhiteSpace(cliRunId));
        var cliRun = cliHarness.Coordinator.GetRun(cliRunId!);
        Assert.NotNull(cliRun);
        Assert.NotNull(cliRun!.FindingsReportPath);
        var cliReportContent = await File.ReadAllTextAsync(cliRun.FindingsReportPath!);

        // --- Parity assertions: both entry paths reach the identical outcome. ---
        Assert.Equal("completed", httpStatus);
        Assert.Equal(LintRunStatus.Completed, cliRun.Status);
        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Contains($"Lint run {cliRunId} completed. Findings report: {cliRun.FindingsReportPath}", cliStdout);

        Assert.True(httpHasFindingsReport);

        // Both Findings Reports carry the identical instruction-identity/outcome fields
        // (record format, outcome state, denied-actions/partial flags, and the scripted
        // instruction sha256/policy identity) — the only per-run-varying fields are the
        // run id and timestamps, deliberately excluded from this comparison.
        foreach (var expectedFragment in new[]
        {
            "record_format: grimoire-findings/1",
            "outcome_state: completed",
            "partial: false",
            "sha256: \"parity-test-sha256\"",
            "path: \"agents/lint/system-prompt.md\"",
        })
        {
            Assert.Contains(expectedFragment, httpReportContent, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, cliReportContent, StringComparison.Ordinal);
        }
    }

    // ── remediation authorize / dismiss / withdraw (T027, US2, SC-005) ─────────────
    // Each transition performed once via the HTTP endpoint handler
    // (RemediationTaskEndpoints) and once via its CLI command, against identically
    // seeded harnesses. Both paths now call the exact same
    // RemediationTaskTransitionService method (verified by reading
    // RemediationTaskEndpoints.AuthorizeAsync/DismissAsync/WithdrawAuthorizationAsync and
    // the three RemediationXxxCommand classes side by side, T021/T022) — this is the
    // regression guard SC-005 asks for, not a discovery mechanism. The remediation
    // execution queue is left paused in every case so the transition itself (not any
    // eagerly-dispatched execution, already covered by HubCliCommandTests) is what's
    // being compared.

    [Fact]
    public async Task RemediationAuthorize_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        using var httpHarness = await RemediationEndpointHostHarness.CreateAsync();
        await httpHarness.Repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, true);
        const string httpTaskId = "2026-08-01-remediation-parityauth-http";
        await httpHarness.InsertTaskAsync(httpTaskId, RemediationTaskStates.Proposed);

        using var cliHarness = await HubCliRemediationTestHarness.CreateAsync();
        await cliHarness.Repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, true);
        const string cliTaskId = "2026-08-01-remediation-parityauth-cli";
        await cliHarness.InsertTaskAsync(cliTaskId, RemediationTaskStates.Proposed);

        // --- HTTP path ---
        var httpResponse = await httpHarness.Client.PostAsync($"/api/remediation-tasks/{httpTaskId}/authorize", content: null);
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        using var httpBody = JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync());

        // --- CLI path ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunAuthorizeCommandAsync(cliTaskId);

        // --- Parity assertions ---
        var httpRow = Assert.Single(await httpHarness.Repository.GetRemediationTasksAsync());
        var cliRow = Assert.Single(await cliHarness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Authorized, httpRow.State);
        Assert.Equal(RemediationTaskStates.Authorized, cliRow.State);
        Assert.NotNull(httpRow.AuthorizedAt);
        Assert.NotNull(cliRow.AuthorizedAt);

        Assert.Equal("authorized", httpBody.RootElement.GetProperty("state").GetString());
        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Equal($"Remediation task {cliTaskId} authorized at {cliRow.AuthorizedAt:O}.", cliStdout.Trim());
    }

    [Fact]
    public async Task RemediationDismiss_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        using var httpHarness = await RemediationEndpointHostHarness.CreateAsync();
        const string httpTaskId = "2026-08-01-remediation-paritydism-http";
        await httpHarness.InsertTaskAsync(httpTaskId, RemediationTaskStates.Proposed);

        using var cliHarness = await HubCliRemediationTestHarness.CreateAsync();
        const string cliTaskId = "2026-08-01-remediation-paritydism-cli";
        await cliHarness.InsertTaskAsync(cliTaskId, RemediationTaskStates.Proposed);

        // --- HTTP path ---
        var httpResponse = await httpHarness.Client.PostAsync($"/api/remediation-tasks/{httpTaskId}/dismiss", content: null);
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        // --- CLI path ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunDismissCommandAsync(cliTaskId);

        // --- Parity assertions: repository rows ---
        var httpRow = Assert.Single(await httpHarness.Repository.GetRemediationTasksAsync());
        var cliRow = Assert.Single(await cliHarness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Dismissed, httpRow.State);
        Assert.Equal(RemediationTaskStates.Dismissed, cliRow.State);

        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Equal($"Remediation task {cliTaskId} dismissed.", cliStdout.Trim());

        // --- Parity assertions: the dismiss outcome entry appended to both records ---
        var httpParsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(await httpHarness.RecordStore.ReadAsync(httpTaskId));
        var cliParsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(await cliHarness.RecordStore.ReadAsync(cliTaskId));
        var httpOutcome = Assert.Single(httpParsed.Entries.OfType<RemediationTaskRecordEntry.Outcome>());
        var cliOutcome = Assert.Single(cliParsed.Entries.OfType<RemediationTaskRecordEntry.Outcome>());
        Assert.Equal(RemediationTaskStates.Dismissed, httpOutcome.State);
        Assert.Equal(RemediationTaskStates.Dismissed, cliOutcome.State);
        Assert.Null(httpOutcome.Reason);
        Assert.Null(cliOutcome.Reason);
    }

    [Fact]
    public async Task RemediationWithdraw_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        using var httpHarness = await RemediationEndpointHostHarness.CreateAsync();
        const string httpTaskId = "2026-08-01-remediation-paritywd-http";
        await httpHarness.InsertTaskAsync(httpTaskId, RemediationTaskStates.Authorized);

        using var cliHarness = await HubCliRemediationTestHarness.CreateAsync();
        const string cliTaskId = "2026-08-01-remediation-paritywd-cli";
        await cliHarness.InsertTaskAsync(cliTaskId, RemediationTaskStates.Authorized);

        // --- HTTP path ---
        var httpResponse = await httpHarness.Client.PostAsync($"/api/remediation-tasks/{httpTaskId}/withdraw-authorization", content: null);
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        // --- CLI path ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunWithdrawCommandAsync(cliTaskId);

        // --- Parity assertions ---
        var httpRow = Assert.Single(await httpHarness.Repository.GetRemediationTasksAsync());
        var cliRow = Assert.Single(await cliHarness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Proposed, httpRow.State);
        Assert.Equal(RemediationTaskStates.Proposed, cliRow.State);
        Assert.Null(httpRow.AuthorizedAt);
        Assert.Null(cliRow.AuthorizedAt);

        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Equal($"Remediation task {cliTaskId} authorization withdrawn (state: proposed).", cliStdout.Trim());
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.Cli;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestTaskArtifact;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    // ── ingest-retrigger / ingest-resume (T031, US3, SC-005) ────────────────────
    // Each performed once via the HTTP endpoint handler (IngestSubmissionEndpoints'
    // /retrigger and /resume routes) and once via its CLI command, against identically
    // seeded harnesses. Both paths call the exact same IngestRunCoordinator methods
    // (verified by reading IngestSubmissionEndpoints.PostRetriggerAsync/PostResumeAsync
    // and IngestRetriggerCommand/IngestResumeCommand side by side) — this is the
    // regression guard SC-005 asks for, not a discovery mechanism.

    [Fact]
    public async Task IngestRetrigger_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        var simulatedDuration = TimeSpan.FromMilliseconds(50);

        using var httpHarness = await IngestEndpointHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));
        const string httpTaskId = "2026-08-01-ingest-parityretrig-http";
        await httpHarness.EnqueueQueuedTaskAsync(httpTaskId);

        using var cliHarness = HubCliIngestTestHarness.Create(
            new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));
        const string cliTaskId = "2026-08-01-ingest-parityretrig-cli";
        await cliHarness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await cliHarness.EnqueueAsync(cliTaskId);

        // --- HTTP path ---
        var httpResponse = await httpHarness.Client.PostAsync($"/api/ingest-submissions/{httpTaskId}/retrigger", content: null);
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        using var httpBody = JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync());
        Assert.True(httpBody.RootElement.GetProperty("retriggered").GetBoolean());
        await httpHarness.Fixture.WaitForStatusAsync(httpTaskId, status => status is "completed" or "failed");

        // --- CLI path ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunRetriggerCommandAsync(cliTaskId);

        // --- Parity assertions ---
        var httpProjection = await httpHarness.Fixture.BoardStore.GetByTaskIdAsync(httpHarness.Fixture.ContentPaths.TasksDir, httpTaskId);
        var cliProjection = await cliHarness.Store.GetByTaskIdAsync(cliHarness.ContentPaths.TasksDir, cliTaskId);
        Assert.Equal("completed", httpProjection!.Column);
        Assert.Equal("completed", cliProjection!.Column);

        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Equal($"Ingest task {cliTaskId} completed.", cliStdout.Trim());
    }

    [Fact]
    public async Task IngestRetrigger_NotInQueue_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        using var httpHarness = await IngestEndpointHostHarness.CreateAsync();
        const string httpTaskId = "2026-08-01-ingest-paritynq-http";
        // Queue not paused: the task auto-starts and completes immediately, so by the
        // time /retrigger is called it is no longer queued.
        await httpHarness.EnqueueQueuedTaskAsync(httpTaskId, pauseFirst: false);
        await httpHarness.Fixture.WaitForStatusAsync(httpTaskId, status => status is "completed" or "failed");

        using var cliHarness = HubCliIngestTestHarness.Create();
        const string cliTaskId = "2026-08-01-ingest-paritynq-cli";
        await cliHarness.EnqueueAsync(cliTaskId);
        await cliHarness.Fixture.WaitForStatusAsync(cliTaskId, status => status is "completed" or "failed");

        // --- HTTP path ---
        var httpResponse = await httpHarness.Client.PostAsync($"/api/ingest-submissions/{httpTaskId}/retrigger", content: null);
        Assert.Equal(HttpStatusCode.Conflict, httpResponse.StatusCode);
        using var httpBody = JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync());
        Assert.Equal($"Task '{httpTaskId}' is not in the queue (completed).", httpBody.RootElement.GetProperty("message").GetString());

        // --- CLI path ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunRetriggerCommandAsync(cliTaskId);

        // --- Parity assertions ---
        Assert.Equal((int)CliExitCode.StateConflict, cliExitCode);
        Assert.Equal($"Ingest task {cliTaskId} is not in the queue (completed).", cliStdout.Trim());
    }

    [Fact]
    public async Task IngestResume_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        var simulatedDuration = TimeSpan.FromMilliseconds(50);

        using var httpHarness = await IngestEndpointHostHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));
        const string httpTaskId1 = "2026-08-01-ingest-parityresume-http1";
        const string httpTaskId2 = "2026-08-01-ingest-parityresume-http2";
        await httpHarness.EnqueueQueuedTaskAsync(httpTaskId1);
        await httpHarness.EnqueueQueuedTaskAsync(httpTaskId2, pauseFirst: false);

        using var cliHarness = HubCliIngestTestHarness.Create(
            new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));
        const string cliTaskId1 = "2026-08-01-ingest-parityresume-cli1";
        const string cliTaskId2 = "2026-08-01-ingest-parityresume-cli2";
        await cliHarness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await cliHarness.EnqueueAsync(cliTaskId1);
        await cliHarness.EnqueueAsync(cliTaskId2);

        // --- HTTP path ---
        var httpResponse = await httpHarness.Client.PostAsync("/api/ingest-queue/resume", content: null);
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        using var httpBody = JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync());
        var httpQueuedTasks = httpBody.RootElement.GetProperty("queuedTasks").GetInt32();
        Assert.False(httpBody.RootElement.GetProperty("queuePaused").GetBoolean());
        await httpHarness.Fixture.WaitForStatusAsync(httpTaskId1, status => status is "completed" or "failed");
        await httpHarness.Fixture.WaitForStatusAsync(httpTaskId2, status => status is "completed" or "failed");

        // --- CLI path ---
        var (cliExitCode, cliStdout, cliStderr) = await cliHarness.RunResumeCommandAsync();

        // --- Parity assertions ---
        // Both harnesses start from the identical shape (queue paused, one task already
        // enqueued before the pause, resume called with nothing else running): the HTTP
        // response's queuedTasks count and the CLI's own status line must agree.
        Assert.Equal(1, httpQueuedTasks);
        Assert.Contains("Ingest queue resumed: 1 task(s) queued.", cliStderr, StringComparison.Ordinal);

        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Equal("Ingest queue drained: 2 task(s) processed, 0 failed.", cliStdout.Trim());
    }

    // ── query (T035, US4, SC-005/ADR-014) ───────────────────────────────────────
    // Turn submission performed once via the HTTP endpoint handler
    // (QuerySubmissionEndpoints.PostTurnAsync) and once via QueryCommand, against
    // identically seeded harnesses (same scripted answer + terminal metadata). Both
    // paths call the exact same QueryRunCoordinator.SubmitTurnAsync method (verified by
    // reading QuerySubmissionEndpoints.PostTurnAsync and QueryCommand side by side,
    // T033) — this is the regression guard SC-005 asks for, not a discovery mechanism.
    // Parity is asserted at the terminal transition's own durable artifact — the
    // Conversation Record (ADR-014) — since that, not any HTTP/CLI response shape, is
    // the state both entry points actually produce.

    [Fact]
    public async Task QuerySubmission_TriggeredViaHttpEndpoint_AndViaCliCommand_ProduceIndistinguishableOutcomes()
    {
        // Identical seed for both harnesses: same scripted answer and terminal metadata.
        var scriptedMetadata = new Dictionary<string, object?>
        {
            ["systemPromptSha256"] = "query-parity-sha256",
            ["policyPath"] = "agents/query/policy.json",
            ["policyVersion"] = 1,
            ["policySha256"] = "query-parity-policy-sha256",
            ["model"] = "claude-parity-test",
            ["turnsUsed"] = 2,
        };
        (string Text, TimeSpan Delay)[] answerChunks = [("The parity answer.", TimeSpan.Zero)];
        const string prompt = "What is the parity answer?";
        const string httpConversationId = "2026-08-01-query-parityhttp";
        const string cliConversationId = "2026-08-01-query-paritycli";

        var httpRoot = QueryTurnSubmissionApiTests.CreateTempRoot();
        using var httpHost = await QueryTurnSubmissionApiTests.BuildHostAsync(
            new FakeAgentProcessLauncher(autoPlay: true)
            {
                ScriptedAnswerChunks = answerChunks,
                ScriptedQueryTerminalMetadata = scriptedMetadata,
            },
            root: httpRoot);

        using var cliHarness = await HubCliQueryTestHarness.CreateAsync(new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = answerChunks,
            ScriptedQueryTerminalMetadata = scriptedMetadata,
        });

        // --- HTTP path: POST to submit, GET to poll for the terminal state. ---
        var httpClient = httpHost.GetTestClient();
        var submitResponse = await httpClient.PostAsJsonAsync(
            $"/api/query-conversations/{httpConversationId}/turns", new { prompt });
        Assert.Equal(HttpStatusCode.Accepted, submitResponse.StatusCode);
        using var submitBody = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var httpTurnId = submitBody.RootElement.GetProperty("turnId").GetString()!;

        var httpState = "running";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (httpState == "running" && DateTime.UtcNow < deadline)
        {
            var turnResponse = await httpClient.GetAsync($"/api/query-turns/{httpTurnId}");
            using var turnBody = JsonDocument.Parse(await turnResponse.Content.ReadAsStringAsync());
            httpState = turnBody.RootElement.GetProperty("state").GetString()!;
            if (httpState == "running")
            {
                await Task.Delay(25);
            }
        }

        // --- CLI path: the production command, invoked exactly as HubCliApp would. ---
        var (cliExitCode, cliStdout, _) = await cliHarness.RunQueryCommandAsync(prompt, cliConversationId);
        var cliTurnId = Assert.Single(cliHarness.Launcher.QueryRequests).TurnId;

        // --- Parity assertions: both entry paths reach the identical outcome. ---
        Assert.Equal("completed", httpState);
        Assert.Equal((int)CliExitCode.Success, cliExitCode);
        Assert.Equal(
            $"Query turn {cliTurnId} in conversation {cliConversationId}: completed{Environment.NewLine}The parity answer.",
            cliStdout.TrimEnd('\r', '\n'));

        // The Conversation Record (ADR-014) is the durable state both entry points
        // produce — parity is checked there, not on the differing HTTP/CLI response
        // shapes themselves.
        var httpRecordPath = QueryTurnSubmissionApiTests.BuildResolvedPaths(httpRoot).ConversationRecordPathFor(httpConversationId);
        var cliRecordPath = cliHarness.Paths.ConversationRecordPathFor(cliConversationId);
        await WaitForFileAsync(httpRecordPath);
        await WaitForFileAsync(cliRecordPath);

        var httpParsed = Assert.IsType<ConversationRecordParseResult.Parsed>(
            ConversationRecordFormat.Parse(await File.ReadAllTextAsync(httpRecordPath)));
        var cliParsed = Assert.IsType<ConversationRecordParseResult.Parsed>(
            ConversationRecordFormat.Parse(await File.ReadAllTextAsync(cliRecordPath)));
        var httpTurn = Assert.Single(httpParsed.Turns);
        var cliTurn = Assert.Single(cliParsed.Turns);

        Assert.Equal("completed", httpTurn.State);
        Assert.Equal("completed", cliTurn.State);
        Assert.Equal(prompt, httpTurn.Prompt);
        Assert.Equal(prompt, cliTurn.Prompt);
        Assert.Equal("The parity answer.", httpTurn.Answer);
        Assert.Equal("The parity answer.", cliTurn.Answer);
        Assert.Equal(httpTurn.Model, cliTurn.Model);
        Assert.Equal(httpTurn.PolicyPath, cliTurn.PolicyPath);
        Assert.Equal(httpTurn.PolicySha256, cliTurn.PolicySha256);
        Assert.Equal(httpTurn.InstructionFileSha256, cliTurn.InstructionFileSha256);
        Assert.Equal(httpTurn.TurnsUsed, cliTurn.TurnsUsed);
    }

    private static async Task WaitForFileAsync(string path, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"File '{path}' did not appear within the timeout.");
    }
}

/// <summary>
/// HTTP test host wiring the Ingest submission/queue endpoint group
/// (<see cref="IngestSubmissionEndpoints.MapIngestSubmissionEndpoints"/> +
/// <see cref="IngestSubmissionEndpoints.MapIngestQueueEndpoints"/>), mirroring
/// <c>IngestTaskRecordApiTests.BuildHostAsync</c>'s DI wiring but as a reusable named
/// harness (018-hub-cli-commands T031), the shape <c>LintTriggerHostHarness</c>/
/// <c>RemediationEndpointHostHarness</c> already use for this file's other parity tests.
/// Wraps an <see cref="IngestSubmissionPipelineFixture"/> — the same "real composed
/// service graph" <see cref="HubCliIngestTestHarness"/> (CLI side) and the pre-existing
/// Ingest HTTP tests already use — registering the exact same
/// Coordinator/BoardStore/ContentPaths instances the fixture built, so both the endpoint
/// handlers and the fixture's own polling helpers observe one shared state.
/// </summary>
internal sealed class IngestEndpointHostHarness : IDisposable
{
    private readonly IHost _host;

    private IngestEndpointHostHarness(IngestSubmissionPipelineFixture fixture, IHost host)
    {
        Fixture = fixture;
        _host = host;
        Client = host.GetTestClient();
    }

    public IngestSubmissionPipelineFixture Fixture { get; }
    public HttpClient Client { get; }

    public static async Task<IngestEndpointHostHarness> CreateAsync(FakeAgentProcessLauncher? launcher = null)
    {
        var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(fixture.Validator);
                    services.AddSingleton(fixture.Pipeline);
                    services.AddSingleton(fixture.BoardStore);
                    services.AddSingleton(fixture.ContentPaths);
                    services.AddSingleton(fixture.SourceArtifactStore);
                    services.AddSingleton(fixture.Coordinator);
                    services.AddSingleton(new TaskRecordReadModel(fixture.ResolvedPaths));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
                        endpoints.MapGroup("/api/ingest-queue").MapIngestQueueEndpoints();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return new IngestEndpointHostHarness(fixture, host);
    }

    /// <summary>
    /// Seeds a task straight into the "queued" state exactly like
    /// <see cref="HubCliIngestTestHarness.EnqueueAsync"/> does on the CLI side: writes the
    /// "queued"-stage Task Artifact <see cref="IngestSubmissionPipeline"/> would have
    /// written, then hands the task to the coordinator's queue — so both parity paths seed
    /// identically without going through the full fetch/convert submission flow.
    /// </summary>
    public async Task EnqueueQueuedTaskAsync(string taskId, bool pauseFirst = true)
    {
        if (pauseFirst)
        {
            await Fixture.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        }

        var sourceRef = Path.Combine(Fixture.Root, $"{taskId}.md");
        var artifactPath = Path.Combine(Fixture.ContentPaths.TasksDir, $"{taskId}.md");
        await new HubTaskArtifactWriter().WriteAsync(
            artifactPath,
            new HubTaskArtifactDocument(
                TaskId: taskId,
                Status: "queued",
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                SourceRef: sourceRef,
                OriginalRef: null,
                FailureReason: null,
                Narrative: "Queued for ingest.",
                UserPromptSource: "default",
                UserPrompt: null));

        await Fixture.Coordinator.EnqueueAsync(taskId, sourceRef, null);
    }

    public void Dispose()
    {
        _host.Dispose();
        Fixture.Dispose();
    }
}

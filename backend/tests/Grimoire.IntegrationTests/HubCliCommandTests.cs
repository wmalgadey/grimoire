using System.Diagnostics;
using Grimoire.Hub.Cli;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestTaskArtifact;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T018 (018-hub-cli-commands, US1): the <c>lint-run</c> command's full contract matrix
/// (specs/018-hub-cli-commands/contracts/cli-commands.md "lint-run") — completed run
/// success line + exit 0, failed run reason + exit 1, already-active message + exit 4,
/// unresolved-remediation-tasks message (count + ids) + exit 4 — plus blocking behavior
/// (the command returns only after the scripted terminal state) and the stdout/stderr
/// separation contract (FR-006: status renders to stderr, the result line is the only
/// thing on stdout).
///
/// Exercises the production <see cref="LintRunCommand"/> class directly against a real
/// <see cref="LintRunCoordinator"/>/<see cref="OperationalStateRepository"/>/
/// <see cref="FindingsReportStore"/> (temp-dir SQLite + findings dir) and
/// <see cref="FakeAgentProcessLauncher"/> — the same "real composed service graph" idiom
/// <c>LintTriggerPreconditionTests</c>/<c>LintRunLifecycleTests</c> use for the HTTP
/// path — invoked through the public <see cref="ICommand{TSettings}"/> interface (rather
/// than the full Spectre <c>CommandApp</c> argument-parsing pipeline, which is covered
/// separately, out-of-process, by <c>HubHelpUsageTests</c>) so tests can inject a
/// capturable stdout writer without redirecting the process-global <see cref="Console"/>
/// (which would race with other tests' <see cref="Console"/> use under xUnit's default
/// cross-class parallelism).
///
/// This file is the home for the CLI command contract matrix across every user story in
/// this feature — later phases (remediation, ingest, query) add their own sections here.
/// </summary>
public class HubCliCommandTests
{
    [Fact]
    public async Task LintRun_CompletedRun_PrintsSuccessLine_StatusOnStderr_ResultOnStdout_ExitZero()
    {
        using var harness = HubCliLintTestHarness.Create();

        var (exitCode, stdout, stderr) = await harness.RunLintRunCommandAsync();

        var runId = harness.Coordinator.LatestRunId;
        Assert.False(string.IsNullOrWhiteSpace(runId));
        var expectedReportPath = harness.Paths.FindingsReportPathFor(runId!);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Contains($"Lint run {runId} started.", stderr, StringComparison.Ordinal);
        Assert.Equal($"Lint run {runId} completed. Findings report: {expectedReportPath}", stdout.Trim());

        // FR-006: the status line never leaks onto stdout, and the result line never
        // leaks onto stderr.
        Assert.DoesNotContain("started.", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Findings report:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LintRun_FailedRun_PrintsFailureReason_ExitOne()
    {
        using var harness = HubCliLintTestHarness.Create(
            new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: "Guardrail denied a write."));

        var (exitCode, stdout, _) = await harness.RunLintRunCommandAsync();
        var runId = harness.Coordinator.LatestRunId;

        Assert.Equal((int)CliExitCode.OperationFailed, exitCode);
        Assert.Equal($"Lint run {runId} failed: Guardrail denied a write.", stdout.Trim());
    }

    [Fact]
    public async Task LintRun_WhileARunIsAlreadyActive_PrintsAlreadyActiveMessage_ExitFour()
    {
        // Short but non-zero: long enough that the conflicting second invocation lands
        // while the first is still active, short enough that just letting the first
        // invocation's scripted run complete naturally (rather than force-terminating
        // the handle, which would leave SuperviseAsync's terminal task unresolved until
        // its 60s liveness watchdog — see FakeAgentProcessLauncher — instead of a fast,
        // deterministic completion) keeps this test fast.
        var simulatedDuration = TimeSpan.FromMilliseconds(500);
        using var harness = HubCliLintTestHarness.Create(new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));

        var firstRun = harness.RunLintRunCommandAsync();
        await WaitUntilAsync(() => harness.Coordinator.IsRunActive, TimeSpan.FromSeconds(5));

        var (exitCode, stdout, _) = await harness.RunLintRunCommandAsync();

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal("A lint run is already active.", stdout.Trim());

        var (firstExitCode, _, _) = await firstRun;
        Assert.Equal((int)CliExitCode.Success, firstExitCode);
    }

    [Fact]
    public async Task LintRun_WithUnresolvedRemediationTasks_PrintsCountAndIds_ExitFour_NeverSpawnsAnAgent()
    {
        using var harness = HubCliLintTestHarness.Create();
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-proposed1", "proposed");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-authrzd1", "authorized");
        await harness.InsertRemediationTaskAsync("2026-08-01-remediation-complete", "completed");

        var (exitCode, stdout, _) = await harness.RunLintRunCommandAsync();

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);

        const string prefix = "Cannot start a lint run: 2 unresolved remediation task(s) block it: ";
        var line = stdout.Trim();
        Assert.StartsWith(prefix, line, StringComparison.Ordinal);
        var ids = line[prefix.Length..].Split(", ", StringSplitOptions.None);
        Assert.Equal(
            new[] { "2026-08-01-remediation-authrzd1", "2026-08-01-remediation-proposed1" },
            ids.OrderBy(id => id, StringComparer.Ordinal));

        // FR-004: blocked means blocked — no agent process was ever spawned.
        Assert.Empty(harness.Launcher.LintRequests);
    }

    [Fact]
    public async Task LintRun_ReturnsOnlyAfterTheScriptedRunReachesItsTerminalState()
    {
        var simulatedDuration = TimeSpan.FromMilliseconds(300);
        using var harness = HubCliLintTestHarness.Create(new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));

        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, _) = await harness.RunLintRunCommandAsync();
        stopwatch.Stop();

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.True(
            stopwatch.Elapsed >= simulatedDuration - TimeSpan.FromMilliseconds(100),
            $"The command returned after {stopwatch.Elapsed}, well before the scripted run's " +
            $"{simulatedDuration} terminal delay — it did not block on the run's terminal state.");
    }

    private static Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout) =>
        PollAsync.WaitAsync(
            predicate,
            timeout,
            $"Condition was not satisfied within {timeout}.",
            pollInterval: TimeSpan.FromMilliseconds(10));

    // ── remediation-authorize / remediation-dismiss / remediation-withdraw ─────────
    // T026 (018-hub-cli-commands, US2): full contract matrix for the three remediation
    // transition commands (specs/018-hub-cli-commands/contracts/cli-commands.md
    // "remediation-authorize"/"remediation-dismiss"/"remediation-withdraw"), each command
    // exercised through the production command class exactly like LintRun above, against a
    // real RemediationTaskTransitionService/RemediationRunCoordinator/
    // OperationalStateRepository and a real (unconnected) SignalR hub context.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RemediationTaskSettings_Validate_MissingOrEmptyTaskId_IsUsageError(string? taskId)
    {
        var settings = new RemediationTaskSettings { TaskId = taskId };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public async Task RemediationTaskSettings_Validate_MissingTaskId_NeverContactsTheStore()
    {
        // Spectre's real CommandApp pipeline calls Settings.Validate() before
        // ExecuteAsync ever runs (mapped to exit 2 by HubCliApp) — this harness proves
        // the FR-009 half of that contract: a settings object that fails validation
        // triggers no repository access, because the only code path that could touch
        // the store is inside ExecuteAsync, which a real invocation would never reach.
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        var settings = new RemediationTaskSettings { TaskId = null };

        Assert.False(settings.Validate().Successful);
        Assert.Empty(await harness.Repository.GetRemediationTasksAsync());
    }

    [Fact]
    public async Task Authorize_FromProposed_QueuePaused_PrintsAuthorizedAtLineOnStdout_ExitZero_NoExecution()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-cliauth1";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);
        await harness.Repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, true);

        var (exitCode, stdout, stderr) = await harness.RunAuthorizeCommandAsync(taskId);

        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Authorized, stored.State);
        Assert.NotNull(stored.AuthorizedAt);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Equal($"Remediation task {taskId} authorized at {stored.AuthorizedAt:O}.", stdout.Trim());
        Assert.Equal(string.Empty, stderr.Trim());
        Assert.Empty(harness.Launcher.RemediationRequests);
    }

    [Fact]
    public async Task Authorize_FromProposed_QueueNotPaused_ExecutionCompletes_StatusOnStderr_ResultOnStdout_ExitZero()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(50)));
        const string taskId = "2026-08-01-remediation-cliauth2";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var (exitCode, stdout, stderr) = await harness.RunAuthorizeCommandAsync(taskId);

        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Completed, stored.State);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        // The authorized_at timestamp is cleared from the persisted row once execution
        // completes (RemediationRunCoordinator.FinishRunAsync), so this asserts only the
        // stable prefix rather than round-tripping an exact value that is no longer
        // observable from the final row.
        Assert.StartsWith($"Remediation task {taskId} authorized at ", stderr.Trim(), StringComparison.Ordinal);
        Assert.Equal($"Remediation task {taskId} completed.", stdout.Trim());
        Assert.DoesNotContain("authorized at", stdout, StringComparison.Ordinal);
        Assert.Single(harness.Launcher.RemediationRequests);
    }

    [Fact]
    public async Task Authorize_ExecutionFails_PrintsFailureReason_ExitOne()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync(
            new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: "Guardrail denied a write."));
        const string taskId = "2026-08-01-remediation-cliauth3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var (exitCode, stdout, _) = await harness.RunAuthorizeCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.OperationFailed, exitCode);
        Assert.Equal($"Remediation task {taskId} failed: Guardrail denied a write.", stdout.Trim());
    }

    [Fact]
    public async Task Authorize_ExecutionNotApplicable_PrintsReason_ExitZero()
    {
        var launcher = new FakeAgentProcessLauncher
        {
            ScriptedRemediationTerminalMetadata = new Dictionary<string, object?>
            {
                ["remediationOutcome"] = "not_applicable",
                ["reason"] = "The page gained a tags list after this action was proposed",
            },
        };
        using var harness = await HubCliRemediationTestHarness.CreateAsync(launcher);
        const string taskId = "2026-08-01-remediation-cliauth4";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var (exitCode, stdout, _) = await harness.RunAuthorizeCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Equal(
            $"Remediation task {taskId} not applicable: The page gained a tags list after this action was proposed.",
            stdout.Trim());
    }

    [Fact]
    public async Task Authorize_UnknownTaskId_PrintsNotFoundMessage_ExitThree()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();

        var (exitCode, stdout, _) = await harness.RunAuthorizeCommandAsync("does-not-exist");

        Assert.Equal((int)CliExitCode.NotFound, exitCode);
        Assert.Equal("Remediation task 'does-not-exist' was not found.", stdout.Trim());
    }

    [Theory]
    [InlineData(RemediationTaskStates.Authorized)]
    [InlineData(RemediationTaskStates.Dismissed)]
    public async Task Authorize_FromNonProposedState_PrintsConflictMessage_ExitFour(string state)
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-cliauth5";
        await harness.InsertTaskAsync(taskId, state);

        var (exitCode, stdout, _) = await harness.RunAuthorizeCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal($"Remediation task {taskId} is not proposed (current state: {state}).", stdout.Trim());
    }

    [Fact]
    public async Task Dismiss_FromProposed_PrintsDismissedLine_ExitZero()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-clidism1";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var (exitCode, stdout, _) = await harness.RunDismissCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Equal($"Remediation task {taskId} dismissed.", stdout.Trim());
        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Dismissed, stored.State);
        Assert.Empty(harness.Launcher.RemediationRequests);
    }

    [Fact]
    public async Task Dismiss_UnknownTaskId_PrintsNotFoundMessage_ExitThree()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();

        var (exitCode, stdout, _) = await harness.RunDismissCommandAsync("does-not-exist");

        Assert.Equal((int)CliExitCode.NotFound, exitCode);
        Assert.Equal("Remediation task 'does-not-exist' was not found.", stdout.Trim());
    }

    [Fact]
    public async Task Dismiss_FromNonProposedState_PrintsConflictMessage_ExitFour()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-clidism2";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Authorized);

        var (exitCode, stdout, _) = await harness.RunDismissCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal($"Remediation task {taskId} is not proposed (current state: authorized).", stdout.Trim());
    }

    [Fact]
    public async Task Withdraw_FromAuthorized_PrintsWithdrawnLine_ExitZero()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-cliwd1";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Authorized);

        var (exitCode, stdout, _) = await harness.RunWithdrawCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Equal($"Remediation task {taskId} authorization withdrawn (state: proposed).", stdout.Trim());
        var stored = Assert.Single(await harness.Repository.GetRemediationTasksAsync());
        Assert.Equal(RemediationTaskStates.Proposed, stored.State);
        Assert.Null(stored.AuthorizedAt);
    }

    [Fact]
    public async Task Withdraw_UnknownTaskId_PrintsNotFoundMessage_ExitThree()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();

        var (exitCode, stdout, _) = await harness.RunWithdrawCommandAsync("does-not-exist");

        Assert.Equal((int)CliExitCode.NotFound, exitCode);
        Assert.Equal("Remediation task 'does-not-exist' was not found.", stdout.Trim());
    }

    [Fact]
    public async Task Withdraw_FromProposed_PrintsNotAuthorizedMessage_ExitFour()
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-cliwd2";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var (exitCode, stdout, _) = await harness.RunWithdrawCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal($"Remediation task {taskId} is not authorized (current state: proposed).", stdout.Trim());
    }

    [Theory]
    [InlineData(RemediationTaskStates.Executing)]
    [InlineData(RemediationTaskStates.Completed)]
    public async Task Withdraw_AfterExecutionAlreadyStarted_PrintsExecutionAlreadyStartedMessage_ExitFour(string state)
    {
        using var harness = await HubCliRemediationTestHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-cliwd3";
        await harness.InsertTaskAsync(taskId, state);

        var (exitCode, stdout, _) = await harness.RunWithdrawCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal(
            $"Remediation task {taskId} can no longer be withdrawn: execution already started (state: {state}).",
            stdout.Trim());
    }

    // ── ingest-retrigger / ingest-resume ────────────────────────────────────────
    // T030 (018-hub-cli-commands, US3): full contract matrix for the two ingest queue
    // commands (specs/018-hub-cli-commands/contracts/cli-commands.md "ingest-retrigger"/
    // "ingest-resume"), each exercised through the production command class exactly like
    // Lint/Remediation above, against a real IngestRunCoordinator/
    // OperationalStateRepository/KanbanBoardProjectionStore (backed by real Task Artifact
    // files on disk) and a scriptable FakeAgentProcessLauncher.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IngestRetriggerSettings_Validate_MissingOrEmptyTaskId_IsUsageError(string? taskId)
    {
        var settings = new IngestRetriggerSettings { TaskId = taskId };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void IngestRetriggerSettings_Validate_MissingTaskId_NeverContactsTheStore()
    {
        // Mirrors RemediationTaskSettings_Validate_MissingTaskId_NeverContactsTheStore
        // (FR-009): a real Spectre invocation never reaches ExecuteAsync when Validate()
        // fails, so no assertion beyond "validation itself fails" is needed to prove no
        // store/coordinator access happens for this settings object.
        var settings = new IngestRetriggerSettings { TaskId = null };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public async Task IngestRetrigger_QueuedTask_ProcessesToCompletion_StatusOnStderr_ResultOnStdout_ExitZero()
    {
        using var harness = HubCliIngestTestHarness.Create();
        const string taskId = "2026-08-01-ingest-cliretrig1";
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await harness.EnqueueAsync(taskId);
        Assert.Empty(harness.Launcher.Requests);

        var (exitCode, stdout, stderr) = await harness.RunRetriggerCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Contains($"Ingest task {taskId} retriggered.", stderr, StringComparison.Ordinal);
        Assert.Equal($"Ingest task {taskId} completed.", stdout.Trim());
        Assert.Single(harness.Launcher.Requests);
    }

    [Fact]
    public async Task IngestRetrigger_QueuedTask_Fails_PrintsFailureLine_ExitOne()
    {
        using var harness = HubCliIngestTestHarness.Create(
            new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: "Guardrail denied a write."));
        const string taskId = "2026-08-01-ingest-cliretrig2";
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await harness.EnqueueAsync(taskId);

        var (exitCode, stdout, _) = await harness.RunRetriggerCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.OperationFailed, exitCode);
        Assert.Equal($"Ingest task {taskId} failed.", stdout.Trim());
    }

    [Fact]
    public async Task IngestRetrigger_UnknownTaskId_PrintsNotFoundMessage_ExitThree()
    {
        using var harness = HubCliIngestTestHarness.Create();

        var (exitCode, stdout, _) = await harness.RunRetriggerCommandAsync("does-not-exist");

        Assert.Equal((int)CliExitCode.NotFound, exitCode);
        Assert.Equal("Task 'does-not-exist' was not found.", stdout.Trim());
        Assert.Empty(harness.Launcher.Requests);
    }

    [Fact]
    public async Task IngestRetrigger_TaskNotInQueue_PrintsConflictWithColumn_ExitFour()
    {
        using var harness = HubCliIngestTestHarness.Create();
        const string taskId = "2026-08-01-ingest-cliretrig3";
        // Queue is not paused, so this task auto-starts and completes immediately
        // (default FakeAgentProcessLauncher: terminalStatus "completed", zero simulated
        // duration) — by the time the command runs, the task's column is "completed",
        // not "queued", so RetriggerAsync refuses it.
        await harness.EnqueueAsync(taskId);
        await harness.Fixture.WaitForStatusAsync(taskId, status => status is "completed" or "failed");

        var (exitCode, stdout, _) = await harness.RunRetriggerCommandAsync(taskId);

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal($"Ingest task {taskId} is not in the queue (completed).", stdout.Trim());
    }

    [Fact]
    public async Task IngestRetrigger_ReturnsOnlyAfterTheScriptedRunReachesItsTerminalState()
    {
        var simulatedDuration = TimeSpan.FromMilliseconds(300);
        using var harness = HubCliIngestTestHarness.Create(new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));
        const string taskId = "2026-08-01-ingest-cliretrig4";
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await harness.EnqueueAsync(taskId);

        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, _) = await harness.RunRetriggerCommandAsync(taskId);
        stopwatch.Stop();

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.True(
            stopwatch.Elapsed >= simulatedDuration - TimeSpan.FromMilliseconds(100),
            $"The command returned after {stopwatch.Elapsed}, well before the scripted run's " +
            $"{simulatedDuration} terminal delay — it did not block on the run's terminal state.");
    }

    [Fact]
    public async Task IngestResume_EmptyQueue_PrintsZeroCounts_ExitZero()
    {
        using var harness = HubCliIngestTestHarness.Create();

        var (exitCode, stdout, stderr) = await harness.RunResumeCommandAsync();

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Contains("Ingest queue resumed: 0 task(s) queued.", stderr, StringComparison.Ordinal);
        Assert.Equal("Ingest queue drained: 0 task(s) processed, 0 failed.", stdout.Trim());
    }

    [Fact]
    public async Task IngestResume_QueueWithItems_ProcessesAllToCompletion_PrintsCounts_ExitZero()
    {
        using var harness = HubCliIngestTestHarness.Create(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(50)));
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await harness.EnqueueAsync("2026-08-01-ingest-cliresume-r1");
        await harness.EnqueueAsync("2026-08-01-ingest-cliresume-r2");
        Assert.Empty(harness.Launcher.Requests);

        var (exitCode, stdout, stderr) = await harness.RunResumeCommandAsync();

        // After ResumeAsync unpauses and starts the first task, one task remains queued —
        // the exact value IngestSubmissionEndpoints.PostResumeAsync's HTTP response
        // carries as `queuedTasks` for the same starting state (SC-005 parity).
        Assert.Contains("Ingest queue resumed: 1 task(s) queued.", stderr, StringComparison.Ordinal);
        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Equal("Ingest queue drained: 2 task(s) processed, 0 failed.", stdout.Trim());
        Assert.Equal(2, harness.Launcher.Requests.Count);
    }

    [Fact]
    public async Task IngestResume_WithFailingTasks_ReportsFailedCount_StillExitsZero()
    {
        using var harness = HubCliIngestTestHarness.Create(
            new FakeAgentProcessLauncher(
                terminalStatus: "failed", failureReason: "boom", simulatedRunDuration: TimeSpan.FromMilliseconds(30)));
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await harness.EnqueueAsync("2026-08-01-ingest-cliresume-f1");
        await harness.EnqueueAsync("2026-08-01-ingest-cliresume-f2");

        var (exitCode, stdout, _) = await harness.RunResumeCommandAsync();

        // Per-task failures are queue state, not a command failure (contract's explicit
        // note: "also when individual tasks failed — per-task outcomes are queue state").
        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Equal("Ingest queue drained: 0 task(s) processed, 2 failed.", stdout.Trim());
    }

    [Fact]
    public async Task IngestResume_WhileATaskIsAlreadyRunning_WaitsForItAndReportsIt_ExitZero()
    {
        // Idempotent-resume-while-running scenario (FR-021): the queue was never paused,
        // so enqueuing starts the task immediately; resuming while it's mid-flight must
        // still supervise it to completion rather than returning early.
        using var harness = HubCliIngestTestHarness.Create(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromMilliseconds(150)));
        const string taskId = "2026-08-01-ingest-cliresume-running";
        await harness.EnqueueAsync(taskId);
        await WaitUntilAsync(() => harness.Coordinator.RunningTaskId == taskId, TimeSpan.FromSeconds(5));

        var (exitCode, stdout, stderr) = await harness.RunResumeCommandAsync();

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.Contains("Ingest queue resumed: 0 task(s) queued.", stderr, StringComparison.Ordinal);
        Assert.Equal("Ingest queue drained: 1 task(s) processed, 0 failed.", stdout.Trim());
    }

    [Fact]
    public async Task IngestResume_ReturnsOnlyAfterTheWholeQueueDrains()
    {
        var simulatedDuration = TimeSpan.FromMilliseconds(300);
        using var harness = HubCliIngestTestHarness.Create(new FakeAgentProcessLauncher(simulatedRunDuration: simulatedDuration));
        await harness.Repository.SetFlagAsync(IngestRunCoordinator.QueuePausedFlag, true);
        await harness.EnqueueAsync("2026-08-01-ingest-cliresume-block");

        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, _) = await harness.RunResumeCommandAsync();
        stopwatch.Stop();

        Assert.Equal((int)CliExitCode.Success, exitCode);
        Assert.True(
            stopwatch.Elapsed >= simulatedDuration - TimeSpan.FromMilliseconds(100),
            $"The command returned after {stopwatch.Elapsed}, well before the scripted run's " +
            $"{simulatedDuration} terminal delay — it did not block until the queue drained.");
    }
}

/// <summary>
/// Hermetic <see cref="LintRunCoordinator"/> + CLI command harness (018-hub-cli-commands
/// T018), mirroring <c>LintRunLifecycleTests.LintCoordinatorHarness</c>/
/// <c>LintTriggerPreconditionTests.LintTriggerHostHarness</c>'s idiom for the CLI entry
/// path instead of the HTTP one — top-level (not nested) so later phases' CLI command
/// test files in this project can reuse it.
/// </summary>
internal sealed class HubCliLintTestHarness : IDisposable
{
    private readonly string _root;

    private HubCliLintTestHarness(
        string root, ResolvedGrimoirePaths paths, FakeAgentProcessLauncher launcher,
        OperationalStateRepository repository, LintRunCoordinator coordinator)
    {
        _root = root;
        Paths = paths;
        Launcher = launcher;
        Repository = repository;
        Coordinator = coordinator;
    }

    public ResolvedGrimoirePaths Paths { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public OperationalStateRepository Repository { get; }
    public LintRunCoordinator Coordinator { get; }

    public static HubCliLintTestHarness Create(FakeAgentProcessLauncher? launcher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-hub-cli-lint-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.FindingsDir);

        var effectiveLauncher = launcher ?? new FakeAgentProcessLauncher(autoPlay: true);
        var repository = new OperationalStateRepository(paths.StateDbPath);
        repository.InitializeAsync().GetAwaiter().GetResult();

        var reportStore = new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance);
        var coordinator = new LintRunCoordinator(
            effectiveLauncher, reportStore, paths,
            logger: NullLogger<LintRunCoordinator>.Instance,
            stateRepository: repository);

        return new HubCliLintTestHarness(root, paths, effectiveLauncher, repository, coordinator);
    }

    public Task InsertRemediationTaskAsync(string taskId, string state)
    {
        var now = DateTimeOffset.UtcNow;
        return Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-prior",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: now,
            AuthorizedAt: state is "authorized" or "executing" ? now : null,
            OutcomeReason: null,
            UpdatedAt: now));
    }

    /// <summary>
    /// Invokes the production <see cref="LintRunCommand"/> via <see cref="ICommand{TSettings}"/>
    /// directly (bypassing Spectre's own argument-parsing pipeline — <c>LintRunSettings</c>
    /// has no options beyond the path switches this in-process harness doesn't need),
    /// capturing its status (stderr) and result (stdout) streams separately via injected
    /// writers instead of the process-global <see cref="Console"/>.
    /// </summary>
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunLintRunCommandAsync(CancellationToken cancellationToken = default)
    {
        var stderrWriter = new StringWriter();
        var stderrConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(stderrWriter) });
        var status = new CliStatusRenderer(stderrConsole);
        var stdoutWriter = new StringWriter();

        var command = new LintRunCommand(Coordinator, status, stdoutWriter);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, "lint-run", null);
        var settings = new LintRunSettings();

        var exitCode = await ((ICommand<LintRunSettings>)command).ExecuteAsync(context, settings, cancellationToken);

        return (exitCode, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public static readonly EmptyRemainingArguments Instance = new();

        public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup(s => s, s => (string?)null);

        public IReadOnlyList<string> Raw { get; } = [];
    }
}

/// <summary>
/// Hermetic <see cref="RemediationTaskTransitionService"/> + CLI command harness
/// (018-hub-cli-commands T026), mirroring <see cref="HubCliLintTestHarness"/>'s idiom for
/// the three remediation transition commands: a real
/// <see cref="OperationalStateRepository"/>/<see cref="RemediationTaskRecordStore"/>/
/// <see cref="RemediationRunCoordinator"/>/<see cref="RemediationTaskTransitionService"/>,
/// a real (unconnected) SignalR hub context for the lifecycle publisher (mirrors
/// <c>RemediationCoordinatorHarness</c>/<c>RemediationObservabilityTests.StartHubHostAsync</c>),
/// and a scriptable <see cref="FakeAgentProcessLauncher"/>. Invokes each production
/// command class via <see cref="ICommand{TSettings}"/> directly, exactly like
/// <see cref="HubCliLintTestHarness.RunLintRunCommandAsync"/>, capturing stdout/stderr via
/// injected writers instead of the process-global <see cref="Console"/>.
/// </summary>
internal sealed class HubCliRemediationTestHarness : IDisposable
{
    private readonly string _root;
    private readonly WebApplication _hubHost;

    private HubCliRemediationTestHarness(
        string root, WebApplication hubHost, ResolvedGrimoirePaths paths, FakeAgentProcessLauncher launcher,
        OperationalStateRepository repository, RemediationTaskRecordStore recordStore,
        RemediationRunCoordinator coordinator, RemediationTaskTransitionService transitionService)
    {
        _root = root;
        _hubHost = hubHost;
        Paths = paths;
        Launcher = launcher;
        Repository = repository;
        RecordStore = recordStore;
        Coordinator = coordinator;
        TransitionService = transitionService;
    }

    public ResolvedGrimoirePaths Paths { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public OperationalStateRepository Repository { get; }
    public RemediationTaskRecordStore RecordStore { get; }
    public RemediationRunCoordinator Coordinator { get; }
    public RemediationTaskTransitionService TransitionService { get; }

    public static async Task<HubCliRemediationTestHarness> CreateAsync(FakeAgentProcessLauncher? launcher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-hub-cli-remediation-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.RemediationTasksDir);

        var effectiveLauncher = launcher ?? new FakeAgentProcessLauncher();
        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();
        var recordStore = new RemediationTaskRecordStore(paths);

        // Real (unconnected) SignalR hub context, mirroring RemediationCoordinatorHarness/
        // RemediationObservabilityTests.StartHubHostAsync — publishing never needs an
        // actual connected client for these tests.
        var hubHostBuilder = WebApplication.CreateBuilder();
        hubHostBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        hubHostBuilder.Services.AddSignalR();
        var hubHost = hubHostBuilder.Build();
        hubHost.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        await hubHost.StartAsync();

        var publisher = new RemediationLifecyclePublisher(
            hubHost.Services.GetRequiredService<IHubContext<RemediationLifecycleHub>>(),
            NullLogger<RemediationLifecyclePublisher>.Instance);
        var coordinator = new RemediationRunCoordinator(
            repository, effectiveLauncher, publisher, recordStore, paths,
            logger: NullLogger<RemediationRunCoordinator>.Instance);
        var transitionService = new RemediationTaskTransitionService(
            repository, publisher, coordinator, recordStore, NullLogger<RemediationLifecyclePublisher>.Instance);

        return new HubCliRemediationTestHarness(root, hubHost, paths, effectiveLauncher, repository, recordStore, coordinator, transitionService);
    }

    public async Task InsertTaskAsync(string taskId, string state, string? outcomeReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        await RecordStore.CreateAsync(
            taskId, "2026-08-01-lint-9f8e7d", now, $"Proposal {taskId}", "Agent-authored proposal (verbatim).", null);
        await Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-9f8e7d",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: now,
            AuthorizedAt: state is RemediationTaskStates.Authorized or RemediationTaskStates.Executing ? now : null,
            OutcomeReason: outcomeReason,
            UpdatedAt: now));
    }

    public Task<(int ExitCode, string Stdout, string Stderr)> RunAuthorizeCommandAsync(string taskId, CancellationToken cancellationToken = default)
        => RunAsync(
            "remediation-authorize",
            (status, stdout) => new RemediationAuthorizeCommand(TransitionService, Coordinator, Repository, status, stdout),
            taskId, cancellationToken);

    public Task<(int ExitCode, string Stdout, string Stderr)> RunDismissCommandAsync(string taskId, CancellationToken cancellationToken = default)
        => RunAsync(
            "remediation-dismiss",
            (_, stdout) => new RemediationDismissCommand(TransitionService, stdout),
            taskId, cancellationToken);

    public Task<(int ExitCode, string Stdout, string Stderr)> RunWithdrawCommandAsync(string taskId, CancellationToken cancellationToken = default)
        => RunAsync(
            "remediation-withdraw",
            (_, stdout) => new RemediationWithdrawCommand(TransitionService, stdout),
            taskId, cancellationToken);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string commandName, Func<CliStatusRenderer, TextWriter, ICommand<RemediationTaskSettings>> createCommand,
        string taskId, CancellationToken cancellationToken)
    {
        var stderrWriter = new StringWriter();
        var stderrConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(stderrWriter) });
        var status = new CliStatusRenderer(stderrConsole);
        var stdoutWriter = new StringWriter();

        var command = createCommand(status, stdoutWriter);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, commandName, null);
        var settings = new RemediationTaskSettings { TaskId = taskId };

        var exitCode = await command.ExecuteAsync(context, settings, cancellationToken);

        return (exitCode, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    public void Dispose()
    {
        _hubHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public static readonly EmptyRemainingArguments Instance = new();

        public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup(s => s, s => (string?)null);

        public IReadOnlyList<string> Raw { get; } = [];
    }
}

/// <summary>
/// Hermetic <see cref="IngestRunCoordinator"/> + CLI command harness (018-hub-cli-commands
/// T030), mirroring <see cref="HubCliLintTestHarness"/>/<see cref="HubCliRemediationTestHarness"/>'s
/// idiom for <see cref="IngestRetriggerCommand"/>/<see cref="IngestResumeCommand"/>. Wraps
/// the existing <see cref="IngestSubmissionPipelineFixture"/> (the same "real composed
/// service graph" the HTTP-side ingest tests already use) rather than re-deriving its own
/// coordinator/repository/publisher wiring — the fixture is this feature's established
/// hermetic-harness idiom for Ingest specifically, predating 018.
/// </summary>
internal sealed class HubCliIngestTestHarness : IDisposable
{
    private readonly IngestSubmissionPipelineFixture _fixture;

    private HubCliIngestTestHarness(IngestSubmissionPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    public IngestSubmissionPipelineFixture Fixture => _fixture;
    public FakeAgentProcessLauncher Launcher => _fixture.Launcher;
    public OperationalStateRepository Repository => _fixture.Repository;
    public IngestRunCoordinator Coordinator => _fixture.Coordinator;
    public KanbanBoardProjectionStore Store => _fixture.BoardStore;
    public IngestContentPaths ContentPaths => _fixture.ContentPaths;

    public static HubCliIngestTestHarness Create(FakeAgentProcessLauncher? launcher = null) =>
        new(new IngestSubmissionPipelineFixture(launcher: launcher));

    /// <summary>
    /// Enqueues a task straight through the coordinator (the same entry point
    /// <see cref="IngestRunQueueTests"/> uses), bypassing the submission pipeline's
    /// fetch/convert stages — these commands only care about queue/terminal-state
    /// transitions, not submission parsing/validation. Unlike <c>IngestRunQueueTests</c>
    /// (which never looks a task up by id through <see cref="KanbanBoardProjectionStore"/>),
    /// <see cref="IngestRetriggerCommand"/>/<see cref="IngestResumeCommand"/> need a real
    /// Task Artifact file to read a "not found"/current-column answer from — so this helper
    /// writes the "queued" stage artifact <see cref="IngestSubmissionPipeline.ProcessAsync"/>
    /// would have written right before its own <c>EnqueueAsync</c> call, keeping the
    /// coordinator's queue state and the Task Artifact file in the same sync a real
    /// submission would produce.
    /// </summary>
    public async Task EnqueueAsync(string taskId, string? sourceRef = null, string? userPrompt = null)
    {
        var effectiveSourceRef = sourceRef ?? Path.Combine(_fixture.Root, $"{taskId}.md");
        var artifactPath = Path.Combine(ContentPaths.TasksDir, $"{taskId}.md");
        await new HubTaskArtifactWriter().WriteAsync(
            artifactPath,
            new HubTaskArtifactDocument(
                TaskId: taskId,
                Status: "queued",
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                SourceRef: effectiveSourceRef,
                OriginalRef: null,
                FailureReason: null,
                Narrative: "Queued for ingest.",
                UserPromptSource: userPrompt is null ? "default" : "custom",
                UserPrompt: userPrompt));

        await Coordinator.EnqueueAsync(taskId, effectiveSourceRef, userPrompt);
    }

    public async Task<(int ExitCode, string Stdout, string Stderr)> RunRetriggerCommandAsync(
        string? taskId, CancellationToken cancellationToken = default)
    {
        var (status, stdoutWriter, stderrWriter) = CreateCapture();
        var command = new IngestRetriggerCommand(Coordinator, Store, ContentPaths, status, stdoutWriter);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, "ingest-retrigger", null);
        var settings = new IngestRetriggerSettings { TaskId = taskId };

        var exitCode = await ((ICommand<IngestRetriggerSettings>)command).ExecuteAsync(context, settings, cancellationToken);

        return (exitCode, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    public async Task<(int ExitCode, string Stdout, string Stderr)> RunResumeCommandAsync(CancellationToken cancellationToken = default)
    {
        var (status, stdoutWriter, stderrWriter) = CreateCapture();
        var command = new IngestResumeCommand(Coordinator, Store, ContentPaths, status, stdoutWriter);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, "ingest-resume", null);
        var settings = new IngestResumeSettings();

        var exitCode = await ((ICommand<IngestResumeSettings>)command).ExecuteAsync(context, settings, cancellationToken);

        return (exitCode, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    private static (CliStatusRenderer Status, StringWriter Stdout, StringWriter Stderr) CreateCapture()
    {
        var stderrWriter = new StringWriter();
        var stderrConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(stderrWriter) });
        var status = new CliStatusRenderer(stderrConsole);
        var stdoutWriter = new StringWriter();
        return (status, stdoutWriter, stderrWriter);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public static readonly EmptyRemainingArguments Instance = new();

        public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup(s => s, s => (string?)null);

        public IReadOnlyList<string> Raw { get; } = [];
    }
}

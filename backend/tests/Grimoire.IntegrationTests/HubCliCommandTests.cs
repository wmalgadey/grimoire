using System.Diagnostics;
using Grimoire.Hub.Cli;
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

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(predicate(), $"Condition was not satisfied within {timeout}.");
    }

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

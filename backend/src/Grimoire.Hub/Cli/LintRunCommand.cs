using Grimoire.Hub.LintDispatch;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Settings for <see cref="LintRunCommand"/> (018-hub-cli-commands T017,
/// contracts/cli-commands.md "lint-run"): no options beyond the inherited ADR-009 path
/// switches.
/// </summary>
public sealed class LintRunSettings : HubPathSettings
{
}

/// <summary>
/// Triggers a Lint Run via <see cref="LintRunCoordinator.TriggerAsync"/> — the same
/// in-process call the (never-mapped-in-the-CLI-process) HTTP trigger endpoint makes —
/// and blocks until the run reaches its terminal state, printing the exact contract
/// lines and mapping to the exact exit codes in
/// specs/018-hub-cli-commands/contracts/cli-commands.md "lint-run" (US1, T017).
///
/// The "run id at start" status line and any future live-status rendering go to
/// <b>stderr</b> via <see cref="CliStatusRenderer"/> (FR-006); the terminal result line
/// goes to <b>stdout</b> via the injectable <see cref="_stdout"/> writer — kept separate
/// from <see cref="Console.Out"/> at the field level (defaulting to it) so tests can
/// capture the exact contract line without redirecting the process-global
/// <see cref="Console"/> (which would race with other tests' Console use under xUnit's
/// default cross-class parallelism).
/// </summary>
public sealed class LintRunCommand : AsyncCommand<LintRunSettings>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// <see cref="LintRunCoordinator"/> flips a run's terminal status
    /// (<see cref="LintRunState.IsTerminal"/>) slightly before it finishes writing (and
    /// recording the path of) the Findings Report — this bounds the extra wait for
    /// <see cref="LintRunState.FindingsReportPath"/> to populate so the completed-run
    /// result line reliably carries the path (contracts/cli-commands.md: "Findings
    /// report: {path}"), without risking an unbounded hang if the write itself failed
    /// (the coordinator logs and moves on in that case, leaving the path null forever).
    /// Mirrors the equivalent bounded wait in the coordinator's own test harness
    /// (LintRunLifecycleTests.LintCoordinatorHarness.WaitForTerminalAsync).
    /// </summary>
    private static readonly TimeSpan ReportPathGraceWindow = TimeSpan.FromSeconds(5);

    private readonly LintRunCoordinator _coordinator;
    private readonly CliStatusRenderer _status;
    private readonly TextWriter _stdout;

    public LintRunCommand(LintRunCoordinator coordinator)
        : this(coordinator, new CliStatusRenderer(), Console.Out)
    {
    }

    /// <summary>Test seam: inject a status renderer / stdout writer instead of the real stderr/stdout streams.</summary>
    public LintRunCommand(LintRunCoordinator coordinator, CliStatusRenderer status, TextWriter stdout)
    {
        _coordinator = coordinator;
        _status = status;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, LintRunSettings settings, CancellationToken cancellationToken)
    {
        var result = await _coordinator.TriggerAsync(cancellationToken);

        switch (result)
        {
            case LintSubmissionResult.Busy:
                _stdout.WriteLine("A lint run is already active.");
                return (int)CliExitCode.StateConflict;

            case LintSubmissionResult.Blocked blocked:
                _stdout.WriteLine(
                    $"Cannot start a lint run: {blocked.UnresolvedTaskIds.Count} unresolved remediation task(s) " +
                    $"block it: {string.Join(", ", blocked.UnresolvedTaskIds)}");
                return (int)CliExitCode.StateConflict;

            case LintSubmissionResult.Accepted accepted:
                return await SuperviseToTerminalAsync(accepted.Run.RunId, cancellationToken);

            default:
                throw new InvalidOperationException($"Unhandled {nameof(LintSubmissionResult)}: {result.GetType()}.");
        }
    }

    private async Task<int> SuperviseToTerminalAsync(string runId, CancellationToken cancellationToken)
    {
        _status.WriteLine($"Lint run {runId} started.");

        var run = await WaitForTerminalAsync(runId, cancellationToken);

        if (run.Status == LintRunStatus.Completed)
        {
            _stdout.WriteLine($"Lint run {runId} completed. Findings report: {run.FindingsReportPath}");
            return (int)CliExitCode.Success;
        }

        _stdout.WriteLine($"Lint run {runId} failed: {run.FailureReason}");
        return (int)CliExitCode.OperationFailed;
    }

    /// <summary>
    /// Polls <see cref="LintRunCoordinator.GetRun"/> until the run's terminal state
    /// (mirrors <c>LintRunLifecycleTests.LintCoordinatorHarness.WaitForTerminalAsync</c>'s
    /// test-side polling idiom — no completion signal exists on <see cref="LintRunState"/>
    /// beyond its own mutable <see cref="LintRunState.IsTerminal"/> flag, so polling it is
    /// the coordinator's own supported read-side contract). Honors
    /// <paramref name="cancellationToken"/> so Ctrl-C during the wait unwinds promptly
    /// (<see cref="HubCliApp"/> maps the resulting <see cref="OperationCanceledException"/>
    /// to <see cref="CliExitCode.Cancelled"/>).
    /// </summary>
    private async Task<LintRunState> WaitForTerminalAsync(string runId, CancellationToken cancellationToken)
    {
        LintRunState run;
        while (true)
        {
            run = _coordinator.GetRun(runId)
                ?? throw new InvalidOperationException(
                    $"Lint run {runId} was accepted but is no longer known to the coordinator.");

            if (run.IsTerminal)
            {
                break;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        if (run.Status == LintRunStatus.Completed)
        {
            var deadline = DateTime.UtcNow + ReportPathGraceWindow;
            while (run.FindingsReportPath is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        return run;
    }
}

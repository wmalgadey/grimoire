using System.Collections.Concurrent;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.LintDispatch;

/// <summary>Result of a trigger attempt (mirrors <c>QuerySubmissionResult</c>'s shape).</summary>
public abstract record LintSubmissionResult
{
    public sealed record Accepted(LintRunState Run) : LintSubmissionResult;

    /// <summary>FR-003/SC-003: a Lint Run is already active — rejected immediately, never queued.</summary>
    public sealed record Busy : LintSubmissionResult;
}

/// <summary>
/// Immediate-rejection, single-active-run dispatch and supervision of Lint Runs
/// (FR-003/FR-004/FR-005, ADR-016). Copies <c>QueryRunCoordinator</c>'s
/// <see cref="SemaphoreSlim"/>(1,1) non-blocking-acquire shape (research.md R3) — not
/// <c>IngestRunCoordinator</c>'s persisted-FIFO-queue shape — because a second trigger
/// while a run is active must be rejected immediately, not queued for later
/// (plan.md "Considered dispatch precedent"). No persisted operational state: a rejected
/// trigger is simply rejected, nothing to resume after a Hub restart.
/// </summary>
public sealed class LintRunCoordinator
{
    private readonly AgentDispatch.IAgentProcessLauncher _launcher;
    private readonly FindingsReportStore _reportStore;
    private readonly ResolvedGrimoirePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessWindow;
    private readonly ILogger<LintRunCoordinator> _logger;
    private readonly SemaphoreSlim _slot = new(1, 1);

    private readonly ConcurrentDictionary<string, LintRunState> _runs = new();
    private readonly ConcurrentDictionary<string, AgentDispatch.IAgentProcessHandle> _handles = new();
    private volatile string? _latestRunId;

    public LintRunCoordinator(
        AgentDispatch.IAgentProcessLauncher launcher,
        FindingsReportStore reportStore,
        ResolvedGrimoirePaths paths,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<LintRunCoordinator>? logger = null)
    {
        _launcher = launcher;
        _reportStore = reportStore;
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _logger = logger ?? NullLogger<LintRunCoordinator>.Instance;
    }

    public LintRunState? GetRun(string runId) => _runs.TryGetValue(runId, out var run) ? run : null;

    /// <summary>The most recently triggered run's id, or null if none has ever been triggered on this Hub instance.</summary>
    public string? LatestRunId => _latestRunId;

    /// <summary>Whether a Lint Run is currently active (diagnostic only — the actual 409/503 guard below uses the semaphore itself).</summary>
    public bool IsRunActive => _slot.CurrentCount == 0;

    /// <summary>
    /// Accepts and immediately dispatches one Lint Run, or rejects it over the
    /// single-active-run limit (FR-003/SC-003) — there is no queue to wait in either way.
    /// </summary>
    public async Task<LintSubmissionResult> TriggerAsync(CancellationToken cancellationToken = default)
    {
        if (!await _slot.WaitAsync(0, cancellationToken))
        {
            HubMetrics.RecordLintTriggerRejected();
            LintLifecycleLogEvents.LogRunRejected(_logger);
            return new LintSubmissionResult.Busy();
        }

        var triggeredAt = _timeProvider.GetUtcNow();
        var runId = $"{triggeredAt:yyyy-MM-dd}-lint-{Guid.NewGuid():N}"[..40];
        var run = new LintRunState(runId, triggeredAt);
        _runs[runId] = run;
        _latestRunId = runId;

        using var triggerSpan = HubTracing.ActivitySource.StartActivity("hub.lint.trigger");
        triggerSpan?.SetTag("run_id", runId);
        triggerSpan?.SetTag("outcome", "accepted");

        LintLifecycleLogEvents.LogRunTriggered(_logger, runId);

        var request = new LintAgentRequest(
            RunId: runId,
            WikiRoot: _paths.ContentRoot,
            SystemPromptPath: _paths.LintSystemPromptPath,
            PolicyPath: _paths.LintPolicyPath,
            WriteLocksDir: _paths.WriteLocksDir);

        AgentDispatch.IAgentProcessHandle handle;
        try
        {
            handle = await _launcher.StartAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinishRunAsync(runId, LintRunStatus.Failed,
                $"Lint agent process could not be started: {ex.Message}", narrative: null, systemPromptSha256: null,
                deniedActions: [], touchedPaths: [], CancellationToken.None);
            return new LintSubmissionResult.Accepted(run);
        }

        _handles[runId] = handle;

        _ = Task.Run(() => SuperviseAsync(runId, handle, CancellationToken.None), CancellationToken.None);

        return new LintSubmissionResult.Accepted(run);
    }

    private async Task SuperviseAsync(string runId, AgentDispatch.IAgentProcessHandle handle, CancellationToken cancellationToken)
    {
        using var supervisionSpan = HubTracing.ActivitySource.StartActivity("hub.lint.run_supervision");
        supervisionSpan?.SetTag("run_id", runId);

        var lastEventTicks = _timeProvider.GetUtcNow().UtcTicks;
        var terminal = new TaskCompletionSource<AgentDispatch.AgentRunEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var checkInterval = TimeSpan.FromMilliseconds(Math.Min(1_000, _livenessWindow.TotalMilliseconds / 4));
        using var watchdog = _timeProvider.CreateTimer(_ =>
        {
            var silence = TimeSpan.FromTicks(_timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref lastEventTicks));
            if (silence > _livenessWindow)
            {
                terminal.TrySetResult(null);
            }
        }, null, checkInterval, checkInterval);

        var readLoop = Task.Run(async () =>
        {
            await foreach (var line in handle.ReadStdoutLinesAsync(cancellationToken))
            {
                var runEvent = AgentDispatch.AgentRunEventParser.TryParse(line);
                if (runEvent is null)
                {
                    continue;
                }

                if (!terminal.Task.IsCompleted)
                {
                    Interlocked.Exchange(ref lastEventTicks, _timeProvider.GetUtcNow().UtcTicks);
                }

                if (runEvent.IsTerminal)
                {
                    terminal.TrySetResult(runEvent);
                }
            }
        }, cancellationToken);

        var terminalEvent = await terminal.Task;

        if (terminalEvent is null)
        {
            supervisionSpan?.SetTag("outcome", "liveness_failed");
            HubMetrics.RecordLivenessFailure();
            handle.Terminate();
            var reason = $"Lint agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds and was terminated.";
            await FinishRunAsync(runId, LintRunStatus.Failed, reason, narrative: null, systemPromptSha256: null,
                deniedActions: [], touchedPaths: [], CancellationToken.None);
        }
        else
        {
            var status = terminalEvent.Type == AgentDispatch.AgentRunEvent.TypeCompleted
                ? LintRunStatus.Completed
                : LintRunStatus.Failed;
            supervisionSpan?.SetTag("outcome", status.ToString().ToLowerInvariant());

            var deniedActions = (terminalEvent.DeniedActions ?? [])
                .Select(d => new FindingsDeniedAction(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn))
                .ToList();

            await FinishRunAsync(
                runId, status, terminalEvent.Reason, terminalEvent.Summary, terminalEvent.SystemPromptSha256, deniedActions,
                terminalEvent.CreatedPages ?? [], CancellationToken.None);
        }

        await handle.DisposeAsync();
        _ = readLoop;
    }

    private async Task FinishRunAsync(
        string runId,
        LintRunStatus status,
        string? failureReason,
        string? narrative,
        string? systemPromptSha256,
        IReadOnlyList<FindingsDeniedAction> deniedActions,
        IReadOnlyList<string> touchedPaths,
        CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            return;
        }

        var completedAt = _timeProvider.GetUtcNow();
        if (!run.TryTransitionTo(status, failureReason, completedAt))
        {
            // Idempotence: only the first terminal transition wins.
            return;
        }

        _handles.TryRemove(runId, out _);
        _slot.Release();

        var outcome = status.ToString().ToLowerInvariant();
        HubMetrics.RecordLintRun(outcome);

        // A failed run (incl. liveness failure) never produced a final narrative — the
        // report is still persisted, clearly marked partial (spec edge case: "What
        // happens when a lint run dies or hangs?").
        var partial = status == LintRunStatus.Failed;
        var effectiveNarrative = narrative ?? $"Run failed before completion. Reason: {failureReason ?? "unknown"}.";
        var findingsCount = FindingsNarrativeStats.CountFindings(effectiveNarrative);

        if (status == LintRunStatus.Completed)
        {
            LintLifecycleLogEvents.LogRunCompleted(_logger, runId, findingsCount);
        }
        else
        {
            LintLifecycleLogEvents.LogRunFailed(_logger, runId, failureReason ?? "unknown");
        }

        var report = new FindingsReport(
            RunId: runId,
            TriggeredAt: run.TriggeredAt,
            CompletedAt: completedAt,
            OutcomeState: outcome,
            FailureReason: failureReason,
            Partial: partial,
            InstructionFilePath: systemPromptSha256 is null ? null : "agents/lint/system-prompt.md",
            InstructionFileSha256: systemPromptSha256,
            DeniedActions: deniedActions,
            InboundLinksRefreshed: touchedPaths.Count,
            Narrative: effectiveNarrative);

        try
        {
            using var writeSpan = HubTracing.ActivitySource.StartActivity("hub.lint.write_findings_report");
            writeSpan?.SetTag("run_id", runId);

            var path = await _reportStore.WriteAsync(report, CancellationToken.None);
            writeSpan?.SetTag("path", path);
            run.SetFindingsReportPath(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write Findings Report for lint run {RunId}.", runId);
        }
    }
}

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

    /// <summary>
    /// 015-lint-board-parity T017 (FR-004/SC-004): rejected because remediation action
    /// tasks from a prior run are still unresolved (`proposed|authorized|executing`).
    /// Carries the blocking task ids so the board can link straight to the cards that
    /// need a decision (contracts/lint-board-api.md).
    /// </summary>
    public sealed record Blocked(string Reason, IReadOnlyList<string> UnresolvedTaskIds) : LintSubmissionResult;
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
    private readonly LintReviewWindowOptions _reviewWindowOptions;
    private readonly ILogger<LintRunCoordinator> _logger;
    private readonly Realtime.LintLifecyclePublisher? _lifecyclePublisher;
    private readonly OperationalState.OperationalStateRepository? _stateRepository;
    private readonly RemediationTasks.RemediationTaskRecordStore? _remediationRecordStore;
    private readonly RemediationTasks.RemediationLifecyclePublisher? _remediationLifecyclePublisher;
    private readonly SemaphoreSlim _slot = new(1, 1);

    /// <summary>
    /// 018-hub-cli-commands (ADR-020, research.md D1a): the exclusive cross-process
    /// <c>lint.pid</c> lock, acquired in <see cref="TriggerAsync"/> alongside
    /// <see cref="_slot"/> and released wherever <see cref="_slot"/> is released below —
    /// same lifecycle, so a Lint Run's full duration is covered on both entry paths. Only
    /// ever written while <see cref="_slot"/> is held (by this coordinator instance), so
    /// no additional synchronization is needed for the field itself.
    /// </summary>
    private LintPidLock? _pidLock;

    /// <summary>"Unresolved" = not yet at a terminal outcome (data-model.md, FR-004).</summary>
    private static readonly string[] _unresolvedRemediationStates = ["proposed", "authorized", "executing"];

    private readonly ConcurrentDictionary<string, LintRunState> _runs = new();
    private readonly ConcurrentDictionary<string, AgentDispatch.IAgentProcessHandle> _handles = new();
    private volatile string? _latestRunId;

    public LintRunCoordinator(
        AgentDispatch.IAgentProcessLauncher launcher,
        FindingsReportStore reportStore,
        ResolvedGrimoirePaths paths,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        LintReviewWindowOptions? reviewWindowOptions = null,
        ILogger<LintRunCoordinator>? logger = null,
        Realtime.LintLifecyclePublisher? lifecyclePublisher = null,
        OperationalState.OperationalStateRepository? stateRepository = null,
        RemediationTasks.RemediationTaskRecordStore? remediationRecordStore = null,
        RemediationTasks.RemediationLifecyclePublisher? remediationLifecyclePublisher = null)
    {
        _launcher = launcher;
        _reportStore = reportStore;
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _reviewWindowOptions = reviewWindowOptions ?? new LintReviewWindowOptions();
        _logger = logger ?? NullLogger<LintRunCoordinator>.Instance;
        // 015-lint-board-parity T011: optional so pre-015 wiring/tests keep working;
        // when absent, runs simply publish no board lifecycle events.
        _lifecyclePublisher = lifecyclePublisher;
        // 015-lint-board-parity T017: optional for the same reason; when absent, the
        // FR-004 unresolved-remediation-tasks precondition is not evaluated.
        _stateRepository = stateRepository;
        // 015-lint-board-parity T022 (FR-007): optional so pre-015 wiring/tests keep
        // working; materialization requires both the state repository (rows) and the
        // record store (task records) — when either is absent, completed runs simply
        // materialize no proposals.
        _remediationRecordStore = remediationRecordStore;
        _remediationLifecyclePublisher = remediationLifecyclePublisher;
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

        // 018-hub-cli-commands (ADR-020, research.md D1a): cross-process "already
        // active" detection — the in-process `_slot` above only ever sees this
        // coordinator instance's own runs, so a second Hub/CLI process against the same
        // data directory would otherwise never observe the conflict. Acquired
        // immediately after `_slot` (no cross-process caller can hold `_slot`, so this
        // is purely an additional gate, never a substitute) and evaluated before the
        // unresolved-remediation-tasks check below so `lint_run_active` wins when both
        // conditions hold, matching the existing in-process precedence comment.
        var pidLock = LintPidLock.TryAcquire(_paths.LintPidPath);
        if (pidLock is null)
        {
            _slot.Release();
            HubMetrics.RecordLintTriggerRejected();
            LintLifecycleLogEvents.LogRunRejected(_logger);
            return new LintSubmissionResult.Busy();
        }
        _pidLock = pidLock;

        // 015-lint-board-parity T017 (FR-004/SC-004): a new run is also rejected while
        // any remediation action task from a prior run has not reached a terminal
        // outcome. Evaluated after the slot acquire so `lint_run_active` wins when both
        // conditions hold (contracts/lint-board-api.md), and the slot semaphore stays
        // the single first-transition-wins arbiter for the trigger-at-completion race.
        if (_stateRepository is not null)
        {
            var unresolvedTaskIds = (await _stateRepository.GetRemediationTasksAsync(cancellationToken: cancellationToken))
                .Where(row => _unresolvedRemediationStates.Contains(row.State, StringComparer.Ordinal))
                .Select(row => row.TaskId)
                .ToList();

            if (unresolvedTaskIds.Count > 0)
            {
                _pidLock?.Dispose();
                _pidLock = null;
                _slot.Release();
                HubMetrics.RecordLintTriggerRejected();
                LintLifecycleLogEvents.LogRunBlockedByUnresolvedTasks(_logger, unresolvedTaskIds.Count);
                return new LintSubmissionResult.Blocked("unresolved_remediation_tasks", unresolvedTaskIds);
            }
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

        // 018-hub-cli-commands T041: guards the lock-acquisition-to-terminal-state
        // critical section below. If anything up to and including the launcher-start
        // try block throws (e.g. a failing PublishRunChangedAsync call), the finally
        // releases `_pidLock`/`_slot` instead of leaking them for the rest of the
        // process's lifetime — a permanent cross-process lint-run lockout (D1a). Once
        // ownership is handed to `FinishRunAsync` (launcher failure) or `SuperviseAsync`
        // (dispatched run), `dispatchStarted` stops the finally from double-releasing.
        var dispatchStarted = false;
        try
        {
            // 015-lint-board-parity T011 (SC-001): the board sees the run as running the
            // moment it is accepted, however it was triggered (board or /lint page).
            if (_lifecyclePublisher is not null)
            {
                await _lifecyclePublisher.PublishRunChangedAsync(
                    runId, fromStatus: null, toStatus: "running", failureReason: null, cancellationToken);
            }

            var request = new LintAgentRequest(
                RunId: runId,
                WikiRoot: _paths.WikiDir,
                SystemPromptPath: _paths.Lint.SystemPromptPath,
                PolicyPath: _paths.Lint.PolicyPath,
                WriteLocksDir: _paths.WriteLocksDir,
                ReviewWindowDays: _reviewWindowOptions.LintReviewWindowDays);

            AgentDispatch.IAgentProcessHandle handle;
            try
            {
                handle = await _launcher.StartAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                dispatchStarted = true;
                await FinishRunAsync(runId, LintRunStatus.Failed,
                    $"Lint agent process could not be started: {ex.Message}", narrative: null, systemPromptSha256: null,
                    deniedActions: [], touchedPaths: [], CancellationToken.None);
                return new LintSubmissionResult.Accepted(run);
            }

            _handles[runId] = handle;
            dispatchStarted = true;

            _ = Task.Run(() => SuperviseAsync(runId, handle, CancellationToken.None), CancellationToken.None);

            return new LintSubmissionResult.Accepted(run);
        }
        finally
        {
            if (!dispatchStarted)
            {
                _pidLock?.Dispose();
                _pidLock = null;
                _slot.Release();
            }
        }
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
                terminalEvent.CreatedPages ?? [], CancellationToken.None,
                // T022 (FR-007): proposals ride the terminal event verbatim (research.md R3).
                terminalEvent.ProposedActions);
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
        CancellationToken cancellationToken,
        IReadOnlyList<AgentDispatch.AgentRunEventProposedAction>? proposedActions = null)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            return;
        }

        // T022 (FR-007, data-model.md "Proposal materialization gates completion"): one
        // Proposed row + one task record per proposedActions entry, all committed BEFORE
        // the run's terminal transition and lifecycle broadcast below — "completed" on
        // the board already implies every proposed card exists. The two call sites
        // (supervision terminal, spawn failure — the latter always with no proposals)
        // are mutually exclusive per run, so materializing ahead of the
        // first-terminal-transition-wins arbiter cannot double-materialize.
        if (status == LintRunStatus.Completed && _stateRepository is not null && _remediationRecordStore is not null)
        {
            try
            {
                await MaterializeProposedActionsAsync(runId, proposedActions ?? [], cancellationToken);
            }
            catch (Exception ex)
            {
                // FR-007: a run must never show completed without its cards — a failed
                // materialization fails the run, with the reason surfaced (FR-005).
                status = LintRunStatus.Failed;
                failureReason = $"Proposed remediation tasks could not be materialized: {ex.Message}";
            }
        }

        var completedAt = _timeProvider.GetUtcNow();
        if (!run.TryTransitionTo(status, failureReason, completedAt))
        {
            // Idempotence: only the first terminal transition wins.
            return;
        }

        _handles.TryRemove(runId, out _);
        // 018-hub-cli-commands (ADR-020, research.md D1a): release the cross-process
        // lock at the same point the in-process slot releases — the lock's lifetime is
        // the run's full duration on both entry paths.
        _pidLock?.Dispose();
        _pidLock = null;
        _slot.Release();

        var outcome = status.ToString().ToLowerInvariant();
        HubMetrics.RecordLintRun(outcome);

        // A failed run (incl. liveness failure) never produced a final narrative — the
        // report is still persisted, clearly marked partial (spec edge case: "What
        // happens when a lint run dies or hangs?").
        var partial = status == LintRunStatus.Failed;
        var effectiveNarrative = narrative ?? $"Run failed before completion. Reason: {failureReason ?? "unknown"}.";
        var findingsCount = FindingsNarrativeStats.CountFindings(effectiveNarrative);

        // T037 (013-lint-agent, US2, plan.md ## Observability): mechanical counting only
        // (Constitution Principle V) — the per-category tallies are headings the agent
        // already wrote (FindingsNarrativeStats), and the refreshed-page count is the
        // harness's own journal (touchedPaths, ADR-016's sole write rule), never a
        // judgment about wiki content. Emitted across every terminal outcome (including a
        // partial/failed run's narrative-so-far) — "across all runs" per plan.md's metric
        // description.
        foreach (var (category, count) in FindingsNarrativeStats.CountByCategory(effectiveNarrative))
        {
            HubMetrics.RecordLintFindings(category, count);
        }
        HubMetrics.RecordLintInboundLinksRefreshed(touchedPaths.Count);

        if (status == LintRunStatus.Completed)
        {
            LintLifecycleLogEvents.LogRunCompleted(_logger, runId, findingsCount);
        }
        else
        {
            LintLifecycleLogEvents.LogRunFailed(_logger, runId, failureReason ?? "unknown");
        }

        // 015-lint-board-parity T011 (SC-002, FR-005): terminal broadcast after the
        // first-terminal-transition-wins arbiter committed — the board reflects
        // completed/failed (incl. the failure reason) without a reload. T022 (US3) will
        // materialize proposed remediation tasks *before* this publish (FR-007 ordering).
        if (_lifecyclePublisher is not null)
        {
            await _lifecyclePublisher.PublishRunChangedAsync(
                runId, fromStatus: "running", toStatus: outcome, failureReason, CancellationToken.None);
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

    /// <summary>
    /// T022 (US3, FR-007): materializes one <c>proposed</c> remediation task per
    /// agent-proposed action — row (ADR-003 operational state), task record (ADR-014
    /// shape), lifecycle broadcast (contracts/remediation-lifecycle-events.md
    /// <c>fromState: null → "proposed"</c>) — in the order the agent reported them.
    /// Title/description/targetPath are stored verbatim: the harness never filters,
    /// merges, rewrites, or scope-checks proposals (Principle V, research.md R3 — an
    /// over-scope proposal simply fails later at the write guard). Runs inside the
    /// <c>hub.lint.run_supervision</c> span's async context, so the
    /// <c>hub.lint.propose_remediation_tasks</c> span parents there (plan.md
    /// ## Observability).
    /// </summary>
    private async Task MaterializeProposedActionsAsync(
        string runId,
        IReadOnlyList<AgentDispatch.AgentRunEventProposedAction> proposedActions,
        CancellationToken cancellationToken)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.lint.propose_remediation_tasks");
        span?.SetTag("run_id", runId);
        span?.SetTag("proposed_count", proposedActions.Count);

        foreach (var action in proposedActions)
        {
            var proposedAt = _timeProvider.GetUtcNow();
            // Task-id shape per data-model.md, mirroring the lint run-id truncation above.
            var taskId = $"{proposedAt:yyyy-MM-dd}-remediation-{Guid.NewGuid():N}"[..44];

            await _stateRepository!.InsertRemediationTaskAsync(new OperationalState.RemediationTaskRow(
                TaskId: taskId,
                RunId: runId,
                Title: action.Title,
                Description: action.Description,
                TargetPath: action.TargetPath,
                State: RemediationTasks.RemediationTaskStates.Proposed,
                ProposedAt: proposedAt,
                AuthorizedAt: null,
                OutcomeReason: null,
                UpdatedAt: proposedAt), cancellationToken);

            await _remediationRecordStore!.CreateAsync(
                taskId, runId, proposedAt, action.Title, action.Description, action.TargetPath, cancellationToken);

            HubMetrics.RecordRemediationTaskProposed(runId);
            LintLifecycleLogEvents.LogRemediationTaskProposed(_logger, runId, taskId);

            if (_remediationLifecyclePublisher is not null)
            {
                await _remediationLifecyclePublisher.PublishTaskChangedAsync(
                    taskId, runId, fromState: null, toState: RemediationTasks.RemediationTaskStates.Proposed,
                    queuePosition: null, outcomeReason: null, cancellationToken);
            }
        }
    }
}

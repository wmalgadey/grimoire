using System.Diagnostics;
using Grimoire.Hub.HarnessSurfaces;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// Persisted-FIFO, single-execution-slot dispatch and supervision of Remediation Action
/// Task execution (015-lint-board-parity T032, ADR-018, data-model.md "Authorization
/// gate = dispatch precondition"). Copies <c>IngestRunCoordinator</c>'s
/// <see cref="SemaphoreSlim"/>(1,1) persisted-FIFO shape (research.md R2/R4) — ordered by
/// <c>authorized_at</c> rather than enqueue order — not <c>LintRunCoordinator</c>'s
/// reject-immediately shape, since authorized tasks queue instead of being rejected
/// (FR-017).
///
/// This is the <b>only</b> type in <c>Grimoire.Hub.RemediationTasks</c> permitted to spawn
/// the authorization-gated <see cref="RemediationExecutionAgentRequest"/>-shaped run
/// (<c>Grimoire.ArchTests.RemediationExecutionDispatchRuleTests</c>, T002): the CAS
/// <c>Authorized → Executing</c> on the persisted row commits <em>inside</em> the slot
/// lock, <em>before</em> the process is spawned — an unauthorized execution would
/// require a call site that does not exist (SC-005/FR-008).
/// <see cref="RemediationMessageTurnCoordinator"/> (T042) is a second, independently
/// allow-listed type in this namespace that also reaches
/// <see cref="AgentDispatch.IAgentProcessLauncher"/> — for its own, differently-shaped
/// overload (message turns carry no wiki-write risk and never touch this state machine),
/// not a weakening of the rule this type enforces.
/// </summary>
public sealed class RemediationRunCoordinator
{
    private readonly OperationalStateRepository _repository;
    private readonly AgentDispatch.IAgentProcessLauncher _launcher;
    private readonly RemediationLifecyclePublisher _publisher;
    private readonly RemediationTaskRecordStore _recordStore;
    private readonly ResolvedGrimoirePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessWindow;
    private readonly ILogger<RemediationRunCoordinator> _logger;
    private readonly HarnessSurfaceReadOptions _harnessSurfaceReadOptions;

    private readonly SemaphoreSlim _slotLock = new(1, 1);
    private string? _runningTaskId;

    public RemediationRunCoordinator(
        OperationalStateRepository repository,
        AgentDispatch.IAgentProcessLauncher launcher,
        RemediationLifecyclePublisher publisher,
        RemediationTaskRecordStore recordStore,
        ResolvedGrimoirePaths paths,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<RemediationRunCoordinator>? logger = null,
        // ADR-023 (022-align-wiki-structure, Phase 5): defaults to a fresh (deny-by-
        // default) options instance so every pre-existing call site keeps compiling.
        HarnessSurfaceReadOptions? harnessSurfaceReadOptions = null)
    {
        _repository = repository;
        _launcher = launcher;
        _publisher = publisher;
        _recordStore = recordStore;
        _paths = paths;
        _harnessSurfaceReadOptions = harnessSurfaceReadOptions ?? new HarnessSurfaceReadOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _logger = logger ?? NullLogger<RemediationRunCoordinator>.Instance;
    }

    public string? RunningTaskId => _runningTaskId;

    /// <summary>
    /// Startup rule (ADR-003/ADR-018, T034/data-model.md "Restart reconciliation"):
    /// <c>Authorized</c> rows survive a restart still authorized, but the execution queue
    /// starts paused until explicitly resumed — the same rule as
    /// <c>IngestRunCoordinator.InitializeAsync</c>'s <c>queue_paused</c> flag, using its
    /// own key (<see cref="OperationalStateRepository.RemediationQueuePausedFlag"/>) so
    /// the two domains' pause lifecycles stay independent (FR-015). Call after
    /// <c>RestartReconciler</c> has already failed any stale <c>Executing</c> rows.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var authorized = await _repository.GetRemediationTasksAsync(RemediationTaskStates.Authorized, cancellationToken);
        await _repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, authorized.Count > 0, cancellationToken);
        HubMetrics.RecordRemediationQueueDepth(authorized.Count);
    }

    public Task<bool> IsQueuePausedAsync(CancellationToken cancellationToken = default)
        => _repository.GetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, cancellationToken);

    /// <summary>1-based FIFO position (by <c>authorized_at</c>) of every currently-waiting task (FR-017).</summary>
    public async Task<IReadOnlyDictionary<string, int>> GetQueuePositionsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        return RemediationTaskEndpoints.ComputeQueuePositions(rows);
    }

    /// <summary>Whole-queue resume after a restart (ADR-003/ADR-018); idempotent.</summary>
    public async Task<int> ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _repository.SetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, false, cancellationToken);
        await TryStartNextAsync(cancellationToken);
        var authorized = await _repository.GetRemediationTasksAsync(RemediationTaskStates.Authorized, cancellationToken);
        return authorized.Count;
    }

    /// <summary>
    /// Starts the next authorized task iff the slot is free and the queue is not paused.
    /// Dequeues exclusively <c>Authorized</c> rows ordered by <c>authorized_at</c>
    /// (FR-017); the CAS to <c>Executing</c> happens inside the slot lock, before
    /// anything is spawned (ADR-018, SC-005/FR-008). A lost CAS race (the withdrawal
    /// endpoint won for that specific row, spec Edge Cases) retries with the refreshed
    /// oldest-authorized row rather than giving up on the whole queue.
    /// </summary>
    public async Task TryStartNextAsync(CancellationToken cancellationToken = default)
    {
        RemediationTaskRow? next = null;

        await _slotLock.WaitAsync(cancellationToken);
        try
        {
            if (_runningTaskId is not null)
            {
                return;
            }

            if (await _repository.GetFlagAsync(OperationalStateRepository.RemediationQueuePausedFlag, cancellationToken))
            {
                return;
            }

            while (true)
            {
                var authorized = await _repository.GetRemediationTasksAsync(RemediationTaskStates.Authorized, cancellationToken);
                if (authorized.Count == 0)
                {
                    return;
                }

                var candidate = authorized
                    .OrderBy(r => r.AuthorizedAt)
                    .ThenBy(r => r.TaskId, StringComparer.Ordinal)
                    .First();

                var committed = await _repository.TryTransitionRemediationTaskAsync(
                    candidate.TaskId, RemediationTaskStates.Authorized, RemediationTaskStates.Executing,
                    outcomeReason: null, authorizedAt: null, updatedAt: _timeProvider.GetUtcNow(), cancellationToken);

                if (committed)
                {
                    next = candidate;
                    _runningTaskId = candidate.TaskId;
                    break;
                }

                // Lost the race (e.g. withdraw-authorization committed first, spec Edge
                // Cases): that row is no longer Authorized — retry with the refreshed set.
            }
        }
        finally
        {
            _slotLock.Release();
        }

        if (next is not null)
        {
            await StartRunAsync(next, cancellationToken);
        }
    }

    private async Task StartRunAsync(RemediationTaskRow row, CancellationToken cancellationToken)
    {
        using var dispatchSpan = HubTracing.ActivitySource.StartActivity("hub.remediation.execution_dispatch");
        dispatchSpan?.SetTag("task_id", row.TaskId);
        var dispatchContext = dispatchSpan?.Context;

        RemediationLifecycleLogEvents.LogExecutionStarted(_logger, row.TaskId);

        await _publisher.PublishTaskChangedAsync(
            row.TaskId, row.RunId, fromState: RemediationTaskStates.Authorized, toState: RemediationTaskStates.Executing,
            cancellationToken: cancellationToken);

        // FR-017/contracts/remediation-lifecycle-events.md: once the queue advances, each
        // remaining waiting task gets a fresh authorized → authorized event with its
        // updated queuePosition.
        await PublishRemainingQueuePositionsAsync(cancellationToken);
        await RecordQueueDepthAsync(cancellationToken);

        // T041 (US5, FR-011): attached context settles before authorization freezes what
        // execution will see — read whatever the record holds at dispatch time (a task
        // can only be attached-to while Proposed, so this is exactly the context that was
        // visible when the human authorized it) and carry it as the ADR-007 user-prompt
        // override. A missing/unreadable record yields no context rather than failing the
        // dispatch — the SQLite row remains the state authority.
        string? attachedContext = null;
        if (await _recordStore.ReadAsync(row.TaskId, cancellationToken) is RemediationTaskRecordParseResult.Parsed parsed)
        {
            attachedContext = RemediationTaskRecordContext.BuildAttachedContext(parsed.Entries);
        }

        var request = new RemediationExecutionAgentRequest(
            TaskId: row.TaskId,
            RunId: row.RunId,
            Title: row.Title,
            Description: row.Description,
            TargetPath: row.TargetPath,
            WikiRoot: _paths.WikiDir,
            SystemPromptPath: _paths.Lint.SystemPromptPath,
            PolicyPath: _paths.Lint.PolicyPath,
            WriteLocksDir: _paths.WriteLocksDir,
            AttachedContext: attachedContext,
            GrantedHarnessSurfaces: HarnessSurfaceGrantResolver.ResolveGranted(_harnessSurfaceReadOptions));

        AgentDispatch.IAgentProcessHandle handle;
        try
        {
            handle = await _launcher.StartAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinishRunAsync(row.TaskId, row.RunId, RemediationTaskStates.Failed,
                $"Remediation agent process could not be started: {ex.Message}", CancellationToken.None);
            return;
        }

        // Fire-and-forget supervision; the coordinator is re-entered via events.
        _ = Task.Run(() => SuperviseAsync(row.TaskId, row.RunId, handle, dispatchContext, CancellationToken.None), CancellationToken.None);
    }

    private async Task SuperviseAsync(
        string taskId, string runId, AgentDispatch.IAgentProcessHandle handle, ActivityContext? parentContext,
        CancellationToken cancellationToken)
    {
        using var supervisionSpan = parentContext is { } context
            ? HubTracing.ActivitySource.StartActivity("hub.remediation.run_supervision", ActivityKind.Internal, context)
            : HubTracing.ActivitySource.StartActivity("hub.remediation.run_supervision");
        supervisionSpan?.SetTag("task_id", taskId);

        var lastEventTicks = _timeProvider.GetUtcNow().UtcTicks;
        var terminal = new TaskCompletionSource<AgentDispatch.AgentRunEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Liveness watchdog: event silence beyond the window is the sole failure
        // authority (ADR-008), unchanged from Ingest/Lint.
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
            // Pipe closed (with or without a terminal event already seen): no further
            // transition here — the watchdog decides for the no-terminal-ever case.
        }, cancellationToken);

        var terminalEvent = await terminal.Task;

        if (terminalEvent is null)
        {
            supervisionSpan?.SetTag("outcome", "liveness_failed");
            HubMetrics.RecordLivenessFailure();
            handle.Terminate();
            var reason = $"Remediation agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds and was terminated.";
            await FinishRunAsync(taskId, runId, RemediationTaskStates.Failed, reason, CancellationToken.None);
        }
        else
        {
            // Hub mapping (contracts/remediation-lifecycle-events.md "remediationOutcome"):
            // completed + remediationOutcome:not_applicable ⇒ NotApplicable; completed
            // otherwise ⇒ Completed; failed ⇒ Failed. FR-005/SC-007: a reason is always
            // surfaced, even if the agent's event omitted one.
            string status;
            string? reason;
            if (terminalEvent.Type == AgentDispatch.AgentRunEvent.TypeCompleted)
            {
                // T035 (ADR-018, plan.md ## Observability): hub.remediation.re_verify is
                // emitted here, Hub-side, purely from the terminal event's own metadata —
                // the re-verification judgment itself happened agent-side (FR-018,
                // Principle V); this span only records that a completed terminal event
                // carried a verdict and what it was, for correlation with the run.
                var stillApplicable = terminalEvent.RemediationOutcome != AgentDispatch.AgentRunEvent.RemediationOutcomeNotApplicable;
                using (var reverifySpan = supervisionSpan is { Context: var supervisionContext }
                    ? HubTracing.ActivitySource.StartActivity("hub.remediation.re_verify", ActivityKind.Internal, supervisionContext)
                    : HubTracing.ActivitySource.StartActivity("hub.remediation.re_verify"))
                {
                    reverifySpan?.SetTag("task_id", taskId);
                    reverifySpan?.SetTag("still_applicable", stillApplicable);
                }

                if (!stillApplicable)
                {
                    status = RemediationTaskStates.NotApplicable;
                    reason = terminalEvent.Reason ?? "Agent judged the proposal no longer applicable.";
                }
                else
                {
                    status = RemediationTaskStates.Completed;
                    reason = null;
                }
            }
            else
            {
                status = RemediationTaskStates.Failed;
                reason = terminalEvent.Reason ?? "Remediation agent run failed.";
            }

            supervisionSpan?.SetTag("outcome", status);
            await FinishRunAsync(taskId, runId, status, reason, CancellationToken.None);
        }

        await handle.DisposeAsync();
        _ = readLoop; // read loop ends with the pipe; nothing to await after termination
    }

    private async Task FinishRunAsync(string taskId, string runId, string status, string? outcomeReason, CancellationToken cancellationToken)
    {
        // Idempotence: only the first terminal transition wins (mirrors IngestRunCoordinator).
        await _slotLock.WaitAsync(cancellationToken);
        try
        {
            if (_runningTaskId != taskId)
            {
                return;
            }

            _runningTaskId = null;
        }
        finally
        {
            _slotLock.Release();
        }

        var now = _timeProvider.GetUtcNow();
        var committed = await _repository.TryTransitionRemediationTaskAsync(
            taskId, RemediationTaskStates.Executing, status, outcomeReason, authorizedAt: null, updatedAt: now, cancellationToken);

        if (committed)
        {
            try
            {
                await _recordStore.AppendOutcomeAsync(taskId, status, outcomeReason, now, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to append the outcome entry to remediation task record {TaskId}.", taskId);
            }

            HubMetrics.RecordRemediationTaskExecuted(status);
            RemediationLifecycleLogEvents.LogExecutionCompleted(_logger, taskId, status, outcomeReason);

            await _publisher.PublishTaskChangedAsync(
                taskId, runId, fromState: RemediationTaskStates.Executing, toState: status, outcomeReason: outcomeReason,
                cancellationToken: cancellationToken);
        }

        await TryStartNextAsync(cancellationToken);
    }

    private async Task PublishRemainingQueuePositionsAsync(CancellationToken cancellationToken)
    {
        var remaining = await _repository.GetRemediationTasksAsync(RemediationTaskStates.Authorized, cancellationToken);
        var ordered = remaining.OrderBy(r => r.AuthorizedAt).ThenBy(r => r.TaskId, StringComparer.Ordinal).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var row = ordered[i];
            await _publisher.PublishTaskChangedAsync(
                row.TaskId, row.RunId, fromState: RemediationTaskStates.Authorized, toState: RemediationTaskStates.Authorized,
                queuePosition: i + 1, cancellationToken: cancellationToken);
        }
    }

    private async Task RecordQueueDepthAsync(CancellationToken cancellationToken)
    {
        var remaining = await _repository.GetRemediationTasksAsync(RemediationTaskStates.Authorized, cancellationToken);
        HubMetrics.RecordRemediationQueueDepth(remaining.Count);
    }
}

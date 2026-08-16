using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.IngestTaskArtifact;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Grimoire.Hub.AgentDispatch;

namespace Grimoire.Hub.IngestDispatch;

/// <summary>Latest loop-activity snapshot for a running task (data-model.md Agent Run Event).</summary>
public sealed record RunActivitySnapshot(
    int ModelTurns,
    int ToolCalls,
    IReadOnlyDictionary<string, int> ToolCallsByName,
    string CurrentAction,
    DateTimeOffset LastEventAt);

/// <summary>
/// Queue-driven, non-blocking dispatch and supervision of Ingest agent runs (ADR-008,
/// FR-016..FR-022). Replaces 003's blocking <c>IngestRunGate</c>: exactly one agent
/// process at a time, further accepted submissions wait in the persistent FIFO queue,
/// run outcome arrives via Agent Run Events, and event silence beyond the liveness
/// window is the sole failure authority. After a Hub restart with queued rows the queue
/// is paused until the user explicitly resumes (FR-021).
/// </summary>
public sealed class IngestRunCoordinator
{
    public const string QueuePausedFlag = "queue_paused";

    private readonly OperationalStateRepository _repository;
    private readonly IAgentProcessLauncher _launcher;
    private readonly IngestLifecyclePublisher _publisher;
    private readonly HubTaskArtifactWriter _taskArtifactWriter;
    private readonly IngestContentPaths _contentPaths;
    private readonly ResolvedGrimoirePaths _resolvedPaths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessWindow;
    private readonly IReadOnlyList<TimeSpan> _reactivationDelays;
    private readonly SourceArtifactStore? _sourceArtifactStore;
    private readonly ILogger<IngestRunCoordinator> _logger;

    private readonly SemaphoreSlim _slotLock = new(1, 1);
    private readonly ConcurrentDictionary<string, RunActivitySnapshot> _activity = new();
    private volatile string? _runningTaskId;
    /// <summary>
    /// 023-task-ui-improvements (ADR-025, research.md R2): the bounded automatic
    /// reactivation schedule. Three attempts spaced by an increasing wait — operational
    /// tuning values, so they live here as constructor defaults beside
    /// <c>livenessWindow</c> rather than widening ADR-022's deliberately minimal,
    /// path-scoped configuration surface.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultReactivationDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90),
    ];

    public IngestRunCoordinator(
        OperationalStateRepository repository,
        IAgentProcessLauncher launcher,
        IngestLifecyclePublisher publisher,
        HubTaskArtifactWriter taskArtifactWriter,
        IngestContentPaths contentPaths,
        ResolvedGrimoirePaths resolvedPaths,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<IngestRunCoordinator>? logger = null,
        IReadOnlyList<TimeSpan>? reactivationDelays = null,
        SourceArtifactStore? sourceArtifactStore = null)
    {
        _repository = repository;
        _launcher = launcher;
        _publisher = publisher;
        _taskArtifactWriter = taskArtifactWriter;
        _contentPaths = contentPaths;
        _resolvedPaths = resolvedPaths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _reactivationDelays = reactivationDelays ?? DefaultReactivationDelays;
        _sourceArtifactStore = sourceArtifactStore;
        _logger = logger ?? NullLogger<IngestRunCoordinator>.Instance;
    }

    public string? RunningTaskId => _runningTaskId;

    public RunActivitySnapshot? GetActivity(string taskId)
        => _activity.TryGetValue(taskId, out var snapshot) ? snapshot : null;

    /// <summary>
    /// Startup rule (FR-021): queued rows surviving a restart pause the queue until the
    /// user explicitly resumes; nothing starts automatically after a restart.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var queued = await _repository.GetQueuedAsync(cancellationToken);
        if (queued.Count > 0)
        {
            await _repository.SetFlagAsync(QueuePausedFlag, true, cancellationToken);
            IngestSubmissionLogEvents.LogQueuePausedAfterRestart(_logger, queued.Count);
        }
        else
        {
            await _repository.SetFlagAsync(QueuePausedFlag, false, cancellationToken);
        }
    }

    public Task<bool> IsQueuePausedAsync(CancellationToken cancellationToken = default)
        => _repository.GetFlagAsync(QueuePausedFlag, cancellationToken);

    /// <summary>FIFO position (1-based) of a queued task, or null when not queued.</summary>
    public async Task<IReadOnlyDictionary<string, int>> GetQueuePositionsAsync(CancellationToken cancellationToken = default)
    {
        var queued = await _repository.GetQueuedAsync(cancellationToken);
        return queued.Select((run, index) => (run.TaskId, Position: index + 1))
            .ToDictionary(x => x.TaskId, x => x.Position);
    }

    /// <summary>
    /// Accepts a task into the Run Queue (non-blocking, FR-016/FR-019) and starts it
    /// immediately when the agent slot is free and the queue is not paused.
    /// </summary>
    public async Task EnqueueAsync(string taskId, string sourceRef, string? userPrompt, CancellationToken cancellationToken = default)
    {
        await _repository.EnqueueAsync(
            new QueuedIngestRun(taskId, _timeProvider.GetUtcNow(), sourceRef, userPrompt), cancellationToken);

        var queued = await _repository.GetQueuedAsync(cancellationToken);
        HubMetrics.RecordQueueDepth(queued.Count);
        var position = queued.ToList().FindIndex(q => q.TaskId == taskId) + 1;
        IngestSubmissionLogEvents.LogQueueEnqueued(_logger, taskId, position);

        await TryStartNextAsync(cancellationToken);
    }

    /// <summary>Whole-queue resume after a restart (FR-021); idempotent.</summary>
    public async Task<int> ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _repository.SetFlagAsync(QueuePausedFlag, false, cancellationToken);
        IngestSubmissionLogEvents.LogQueueResumed(_logger, taskId: "", scope: "queue");
        await TryStartNextAsync(cancellationToken);
        var queued = await _repository.GetQueuedAsync(cancellationToken);
        return queued.Count;
    }

    /// <summary>
    /// Per-task re-trigger after a restart (FR-021). The task keeps its FIFO position —
    /// re-arming resumes automatic processing, it never jumps the queue (spec edge case).
    /// Returns false when the task is not currently queued (endpoint answers 409).
    /// </summary>
    public async Task<bool> RetriggerAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var queued = await _repository.GetQueuedAsync(cancellationToken);
        if (queued.All(q => q.TaskId != taskId))
        {
            return false;
        }

        await _repository.SetFlagAsync(QueuePausedFlag, false, cancellationToken);
        IngestSubmissionLogEvents.LogQueueResumed(_logger, taskId, scope: "task");
        await TryStartNextAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// 023-task-ui-improvements T029 (US5, FR-010..FR-013, SC-007/SC-008, research.md R3):
    /// manually re-enters a finally-failed task into the queue under the same task id.
    /// Race-safe by construction: a failed task holds no <c>operational_task_state</c> row
    /// (deleted by <see cref="FinishRunAsync"/>), so an <c>INSERT ... ON CONFLICT DO
    /// NOTHING</c> claim is the CAS arbiter for concurrent duplicate requests (ADR-018's
    /// withdrawal-race idiom) — the first caller to insert wins, every other caller's
    /// insert affects zero rows and this method returns <see langword="false"/> for them.
    /// The endpoint is responsible for the failed-status precondition (FR-011) and the
    /// normalized-source-exists precondition before calling this.
    /// </summary>
    public async Task<bool> RestartFailedAsync(
        string taskId, string normalizedSourceRef, string? userPrompt, CancellationToken cancellationToken = default)
    {
        var claimed = await _repository.TryClaimTaskStateAsync(
            new OperationalTaskState(taskId, "restarting", null, _timeProvider.GetUtcNow(), Attempt: 0), cancellationToken);
        if (!claimed)
        {
            return false;
        }

        // The publisher is the Hub's single history-recording choke point (T004) — it
        // appends the `restarted` row itself as part of publishing the transition, so
        // there is nothing further to write here.
        await _publisher.PublishAsync(taskId, "failed", IngestHistoryStatuses.Restarted, cancellationToken: cancellationToken);

        // Board reads the artifact frontmatter, not the history table — this is the write
        // that actually moves the task off the failed column (data-model.md §1: the
        // `restarted` entry precedes `queued`, mirroring every other stage transition).
        await WriteRestartArtifactAsync(taskId, userPrompt, cancellationToken);
        await _publisher.PublishAsync(taskId, IngestHistoryStatuses.Restarted, "queued", cancellationToken: cancellationToken);

        await EnqueueAsync(taskId, normalizedSourceRef, userPrompt, cancellationToken);
        return true;
    }

    private async Task WriteRestartArtifactAsync(string taskId, string? userPrompt, CancellationToken cancellationToken)
    {
        var artifactPath = Path.Combine(_contentPaths.TasksDir, $"{taskId}.md");
        TaskArtifactFrontmatter? existing = null;
        if (File.Exists(artifactPath))
        {
            existing = TaskArtifactFrontmatter.TryParse(await File.ReadAllTextAsync(artifactPath, cancellationToken));
        }

        var document = new HubTaskArtifactDocument(
            TaskId: taskId,
            Status: "queued",
            StartedAt: existing?.StartedAt ?? _timeProvider.GetUtcNow(),
            CompletedAt: null,
            SourceRef: existing?.SourceRef,
            OriginalRef: existing?.OriginalRef,
            FailureReason: null,
            Narrative: "Task restarted; queued for ingest.",
            UserPromptSource: existing?.UserPromptSource,
            UserPrompt: userPrompt,
            ConvertSteps: existing?.ConvertSteps,
            Title: await ResolveTitleAsync(taskId, existing, cancellationToken));

        await _taskArtifactWriter.WriteAsync(artifactPath, document, cancellationToken);
    }

    /// <summary>
    /// 023 T045 (FR-003): the human-readable label, resolved through the same manifest chain
    /// the board and detail views use, so no Hub write can disagree with what the UI shows.
    /// When no manifest store is wired (board-only test compositions) the label already on the
    /// artifact is carried forward rather than dropped, with the task id as the last resort —
    /// the same terminal fallback the chain itself has.
    /// </summary>
    private async Task<string> ResolveTitleAsync(
        string taskId, TaskArtifactFrontmatter? existing, CancellationToken cancellationToken)
    {
        if (_sourceArtifactStore is null)
        {
            return string.IsNullOrWhiteSpace(existing?.Title) ? taskId : existing.Title;
        }

        var manifest = await _sourceArtifactStore.TryReadMetadataAsync(taskId, cancellationToken);
        return KanbanBoardProjectionStore.ResolveTitle(taskId, manifest);
    }

    /// <summary>Starts the next queued task iff the slot is free and the queue is not paused (FIFO).</summary>
    public async Task TryStartNextAsync(CancellationToken cancellationToken = default)
    {
        QueuedIngestRun? next = null;

        // The lock guards only the slot/queue decision; the actual start happens outside
        // so failure handling (which re-takes the lock) cannot deadlock.
        await _slotLock.WaitAsync(cancellationToken);
        try
        {
            if (_runningTaskId is not null)
            {
                return;
            }

            if (await _repository.GetFlagAsync(QueuePausedFlag, cancellationToken))
            {
                return;
            }

            var queued = await _repository.GetQueuedAsync(cancellationToken);
            if (queued.Count == 0)
            {
                return;
            }

            next = queued[0];
            await _repository.RemoveQueuedAsync(next.TaskId, cancellationToken);
            HubMetrics.RecordQueueDepth(queued.Count - 1);
            _runningTaskId = next.TaskId;
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

    private async Task StartRunAsync(QueuedIngestRun run, CancellationToken cancellationToken)
    {
        IngestSubmissionLogEvents.LogQueueAdvanced(_logger, run.TaskId);

        var queuedDurationMs = (long)(_timeProvider.GetUtcNow() - run.AcceptedAt).TotalMilliseconds;
        HubMetrics.RecordIngestSubmissionQueueWait(run.TaskId, queuedDurationMs / 1000.0);
        IngestSubmissionLogEvents.LogRunTriggered(_logger, run.TaskId, queuedDurationMs);

        // A fresh run occupancy starts with no reactivation attempts spent (data-model.md §2).
        await _repository.UpsertAsync(
            new OperationalTaskState(run.TaskId, "running", null, _timeProvider.GetUtcNow(), Attempt: 0), cancellationToken);
        await _publisher.PublishAsync(run.TaskId, "queued", "running", cancellationToken: cancellationToken);

        await LaunchAgentAsync(run, attempt: 0, cancellationToken);
    }

    /// <summary>
    /// Starts the agent process for <paramref name="run"/> and hands it to supervision.
    /// Shared by the first start and by every reactivation (023 T013), so a re-launch is
    /// byte-for-byte the same request through the same <see cref="IAgentProcessLauncher"/>
    /// port — the only difference is the attempt number carried into supervision.
    /// </summary>
    private async Task LaunchAgentAsync(QueuedIngestRun run, int attempt, CancellationToken cancellationToken)
    {
        var request = new IngestAgentRequest(
            TaskId: run.TaskId,
            SourceRef: run.SourceRef,
            SourceKind: "file",
            WikiRoot: _contentPaths.Root,
            ContentRoot: _contentPaths.Root,
            TasksDir: _contentPaths.TasksDir,
            IndexPath: _contentPaths.IndexPath,
            LogPath: _contentPaths.LogPath,
            PastedText: null,
            SystemPromptPath: _resolvedPaths.Ingest.SystemPromptPath,
            DefaultUserPromptPath: _resolvedPaths.Ingest.DefaultUserPromptPath!,
            PolicyPath: _resolvedPaths.Ingest.PolicyPath,
            WriteLocksDir: _contentPaths.WriteLocksDir,
            UserPrompt: run.UserPrompt,
            // 023 T046 (FR-003): the label travels with the launch so the agent's own artifact
            // writes keep it, rather than the Hub having to re-touch an agent-owned file.
            Title: await ResolveTitleAsync(run.TaskId, existing: null, cancellationToken));

        IAgentProcessHandle handle;
        try
        {
            handle = await _launcher.StartAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinishRunAsync(run.TaskId, "failed", $"Ingest agent process could not be started: {ex.Message}",
                writeFailureArtifact: true, CancellationToken.None);
            return;
        }

        // Fire-and-forget supervision; the coordinator is re-entered via events.
        _ = Task.Run(() => SuperviseAsync(run, handle, attempt, CancellationToken.None), CancellationToken.None);
    }

    private async Task SuperviseAsync(QueuedIngestRun run, IAgentProcessHandle handle, int attempt, CancellationToken cancellationToken)
    {
        var taskId = run.TaskId;
        using var supervisionSpan = HubTracing.ActivitySource.StartActivity("ingest_hub.run_supervision");
        supervisionSpan?.SetTag("task_id", taskId);

        var lastEventTicks = _timeProvider.GetUtcNow().UtcTicks;
        string lastEventType = "none";
        var terminal = new TaskCompletionSource<AgentRunEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Liveness watchdog: event silence beyond the window is the sole failure
        // authority (ADR-008). Checked on a coarse tick so a hung process cannot park
        // the run in `running` forever.
        var checkInterval = TimeSpan.FromMilliseconds(Math.Min(1_000, _livenessWindow.TotalMilliseconds / 4));
        using var watchdog = _timeProvider.CreateTimer(_ =>
        {
            var silence = TimeSpan.FromTicks(_timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref lastEventTicks));
            if (silence > _livenessWindow)
            {
                terminal.TrySetResult(null);
            }
        }, null, checkInterval, checkInterval);

        // Keeps draining stdout past the terminal event (not returning early): events
        // that arrive after this task's terminal state are still recorded as
        // diagnostics via HandleEventAsync's late-event check (FR-022). The loop ends
        // naturally when the pipe closes.
        var readLoop = Task.Run(async () =>
        {
            await foreach (var line in handle.ReadStdoutLinesAsync(cancellationToken))
            {
                var runEvent = AgentRunEventParser.TryParse(line);
                if (runEvent is null)
                {
                    continue;
                }

                if (!terminal.Task.IsCompleted)
                {
                    Interlocked.Exchange(ref lastEventTicks, _timeProvider.GetUtcNow().UtcTicks);
                    lastEventType = runEvent.Type;
                }

                await HandleEventAsync(taskId, runEvent, cancellationToken);

                if (runEvent.IsTerminal)
                {
                    terminal.TrySetResult(runEvent);
                }
            }
            // Pipe closed (with or without a terminal event already seen): no further
            // transition — the watchdog decides for the no-terminal-ever case.
        }, cancellationToken);

        var terminalEvent = await terminal.Task;
        supervisionSpan?.SetTag("last_event_type", lastEventType);

        if (terminalEvent is null)
        {
            // Liveness silence remains the detection authority (ADR-008) — 023/ADR-025 only
            // changes its consequence from "immediately terminal" to "bounded re-entry".
            var silentSeconds = TimeSpan.FromTicks(_timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref lastEventTicks)).TotalSeconds;
            handle.Terminate();
            await handle.DisposeAsync();

            if (attempt < _reactivationDelays.Count)
            {
                supervisionSpan?.SetTag("outcome", "liveness_interrupted");
                await ScheduleReactivationAsync(run, attempt + 1, CancellationToken.None);
                _ = readLoop;
                return;
            }

            // Attempts exhausted: the pre-existing final-failure path, unchanged — except
            // that this interruption is recorded too. SC-005 asks for *every* liveness
            // interruption to be a distinct history entry "rather than an unexplained jump
            // to a final failed state", and the last one is exactly the jump that would
            // otherwise be unexplained.
            supervisionSpan?.SetTag("outcome", "liveness_failed");
            HubMetrics.RecordLivenessFailure();
            HubMetrics.RecordReactivation("exhausted");
            IngestSubmissionLogEvents.LogRunLivenessFailed(_logger, taskId, (long)silentSeconds, (long)_livenessWindow.TotalSeconds);
            IngestSubmissionLogEvents.LogReactivationExhausted(_logger, taskId, _reactivationDelays.Count);

            await _publisher.PublishAsync(
                taskId, "running", IngestHistoryStatuses.LivenessInterrupted,
                cancellationToken: CancellationToken.None,
                historyDetail: $"no attempts remaining after {_reactivationDelays.Count} reactivations");

            var reason = _reactivationDelays.Count == 0
                ? $"Agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds and was terminated."
                : $"Agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds after "
                  + $"{_reactivationDelays.Count} automatic reactivation attempts and was terminated.";
            await FinishRunAsync(taskId, "failed", reason, writeFailureArtifact: true, CancellationToken.None);
            _ = readLoop;
            return;
        }

        var status = terminalEvent.Type == AgentRunEvent.TypeCompleted ? "completed" : "failed";
        supervisionSpan?.SetTag("outcome", status);
        await FinishRunAsync(taskId, status, terminalEvent.Reason, writeFailureArtifact: false, CancellationToken.None);

        await handle.DisposeAsync();
        _ = readLoop; // read loop ends with the pipe; nothing to await after termination
    }

    /// <summary>
    /// Records the interruption and arms the backoff timer for the next attempt
    /// (023 T013, FR-007/FR-008). The run slot is deliberately NOT released: holding it
    /// keeps the FIFO single-slot model intact, so the queue neither advances nor reorders
    /// while a reactivation is pending (research.md R2).
    /// </summary>
    private async Task ScheduleReactivationAsync(QueuedIngestRun run, int attempt, CancellationToken cancellationToken)
    {
        var delay = _reactivationDelays[attempt - 1];

        IngestSubmissionLogEvents.LogRunLivenessInterrupted(_logger, run.TaskId, attempt, (long)delay.TotalSeconds);
        await _repository.UpsertAsync(
            new OperationalTaskState(run.TaskId, "running", null, _timeProvider.GetUtcNow(), Attempt: attempt),
            cancellationToken);
        await _publisher.PublishAsync(
            run.TaskId, "running", IngestHistoryStatuses.LivenessInterrupted,
            cancellationToken: cancellationToken,
            historyDetail: $"attempt {attempt}; next retry in {(long)delay.TotalSeconds}s");

        // Scheduled on the injected clock, so tests drive the schedule with virtual time
        // instead of waiting for it (ADR-021). The timer roots itself via its own closure
        // and disposes on the single fire.
        ITimer? timer = null;
        timer = _timeProvider.CreateTimer(
            _ =>
            {
                timer?.Dispose();
                _ = Task.Run(() => ReactivateAsync(run, attempt), CancellationToken.None);
            },
            state: null,
            dueTime: delay,
            period: Timeout.InfiniteTimeSpan);
    }

    private async Task ReactivateAsync(QueuedIngestRun run, int attempt)
    {
        // The backoff window is wide open: a manual restart or a Hub-level failure may have
        // moved on in the meantime. Re-launching then would run a task nobody is waiting for.
        if (_runningTaskId != run.TaskId)
        {
            return;
        }

        // Root span (plan.md ## Observability): this runs on a timer callback, not inside a
        // request or the original supervision scope, so an explicit default parent context
        // keeps it a root rather than silently inheriting whatever Activity happens to flow
        // here — correlation to the logs/metrics below is via task_id.
        using var span = HubTracing.ActivitySource.StartActivity(
            "ingest_hub.reactivation", ActivityKind.Internal, parentContext: default);
        span?.SetTag("task_id", run.TaskId);
        span?.SetTag("attempt", attempt);
        span?.SetTag("delay_seconds", (long)_reactivationDelays[attempt - 1].TotalSeconds);

        HubMetrics.RecordReactivation("attempted");
        IngestSubmissionLogEvents.LogRunReactivated(_logger, run.TaskId, attempt);

        // Loop activity belongs to the process that produced it; the re-launched run starts
        // its own count (contracts/signalr-events.md: "activity resets on re-launch").
        _activity.TryRemove(run.TaskId, out _);

        await _publisher.PublishAsync(
            run.TaskId, IngestHistoryStatuses.LivenessInterrupted, IngestHistoryStatuses.Reactivated,
            cancellationToken: CancellationToken.None, historyDetail: $"attempt {attempt}");
        await _publisher.PublishAsync(
            run.TaskId, IngestHistoryStatuses.Reactivated, "running", cancellationToken: CancellationToken.None);

        await LaunchAgentAsync(run, attempt, CancellationToken.None);
    }

    private async Task HandleEventAsync(string taskId, AgentRunEvent runEvent, CancellationToken cancellationToken)
    {
        using var span = HubTracing.ActivitySource.StartActivity("ingest_hub.handle_run_event");
        span?.SetTag("task_id", taskId);
        span?.SetTag("event_type", runEvent.Type);

        HubMetrics.RecordRunEvent(runEvent.Type);

        if (_runningTaskId != taskId)
        {
            // Terminal state already reached (e.g. liveness failure raced a late event):
            // record for diagnostics, change nothing (FR-022).
            IngestSubmissionLogEvents.LogRunLateEvent(_logger, taskId, runEvent.Type);
            return;
        }

        if (runEvent.Type == AgentRunEvent.TypeActivity)
        {
            var snapshot = new RunActivitySnapshot(
                ModelTurns: runEvent.ModelTurns ?? 0,
                ToolCalls: runEvent.ToolCalls ?? 0,
                ToolCallsByName: runEvent.ToolCallsByName ?? new Dictionary<string, int>(),
                CurrentAction: runEvent.CurrentAction ?? "unknown",
                LastEventAt: _timeProvider.GetUtcNow());
            _activity[taskId] = snapshot;
            await _publisher.PublishRunActivityAsync(taskId, snapshot, cancellationToken);
        }
    }

    private async Task FinishRunAsync(string taskId, string status, string? failureReason, bool writeFailureArtifact, CancellationToken cancellationToken)
    {
        // Idempotence: only the first terminal transition wins (FR-022).
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

        if (writeFailureArtifact)
        {
            await WriteHubFailureArtifactAsync(taskId, failureReason ?? "Ingest run failed.", cancellationToken);
        }

        await _repository.DeleteAsync(taskId, cancellationToken);
        _activity.TryRemove(taskId, out _);
        await _publisher.PublishAsync(taskId, "running", status, failureReason, cancellationToken);

        await TryStartNextAsync(cancellationToken);
    }

    /// <summary>
    /// Liveness/start failures happen outside the agent process, so the Hub records the
    /// terminal artifact itself, preserving the fields the pipeline already wrote.
    /// </summary>
    private async Task WriteHubFailureArtifactAsync(string taskId, string failureReason, CancellationToken cancellationToken)
    {
        var artifactPath = Path.Combine(_contentPaths.TasksDir, $"{taskId}.md");
        TaskArtifactFrontmatter? existing = null;
        string? userPrompt = null;
        if (File.Exists(artifactPath))
        {
            var markdown = await File.ReadAllTextAsync(artifactPath, cancellationToken);
            existing = TaskArtifactFrontmatter.TryParse(markdown);
            userPrompt = TaskArtifactFrontmatter.TryExtractUserPrompt(markdown);
        }

        var document = new HubTaskArtifactDocument(
            TaskId: taskId,
            Status: "failed",
            StartedAt: existing?.StartedAt ?? _timeProvider.GetUtcNow(),
            CompletedAt: _timeProvider.GetUtcNow(),
            SourceRef: existing?.SourceRef,
            OriginalRef: existing?.OriginalRef,
            FailureReason: failureReason,
            Narrative: $"Ingest failed: {failureReason}",
            UserPromptSource: existing?.UserPromptSource,
            UserPrompt: userPrompt,
            ConvertSteps: existing?.ConvertSteps,
            Title: await ResolveTitleAsync(taskId, existing, cancellationToken));

        await _taskArtifactWriter.WriteAsync(artifactPath, document, cancellationToken);
    }
}

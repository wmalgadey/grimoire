using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.TaskArtifact;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.AgentDispatch;

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
    private readonly ContentRootPaths _contentPaths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessWindow;
    private readonly ILogger<IngestRunCoordinator> _logger;

    private readonly SemaphoreSlim _slotLock = new(1, 1);
    private readonly ConcurrentDictionary<string, RunActivitySnapshot> _activity = new();
    private string? _runningTaskId;

    public IngestRunCoordinator(
        OperationalStateRepository repository,
        IAgentProcessLauncher launcher,
        IngestLifecyclePublisher publisher,
        HubTaskArtifactWriter taskArtifactWriter,
        ContentRootPaths contentPaths,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<IngestRunCoordinator>? logger = null)
    {
        _repository = repository;
        _launcher = launcher;
        _publisher = publisher;
        _taskArtifactWriter = taskArtifactWriter;
        _contentPaths = contentPaths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
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

        await _repository.UpsertAsync(
            new OperationalTaskState(run.TaskId, "running", null, _timeProvider.GetUtcNow()), cancellationToken);
        await _publisher.PublishAsync(run.TaskId, "queued", "running", cancellationToken: cancellationToken);

        var request = new IngestAgentRequest(
            TaskId: run.TaskId,
            SourceRef: run.SourceRef,
            SourceKind: "file",
            PagesDir: _contentPaths.PagesDir,
            TasksDir: _contentPaths.TasksDir,
            IndexPath: _contentPaths.IndexPath,
            LogPath: _contentPaths.LogPath,
            PastedText: null,
            SystemPromptPath: _contentPaths.SystemPromptPath,
            DefaultUserPromptPath: _contentPaths.DefaultUserPromptPath,
            PolicyPath: _contentPaths.PolicyPath,
            UserPrompt: run.UserPrompt);

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
        _ = Task.Run(() => SuperviseAsync(run.TaskId, handle, CancellationToken.None), CancellationToken.None);
    }

    private async Task SuperviseAsync(string taskId, IAgentProcessHandle handle, CancellationToken cancellationToken)
    {
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
            // Liveness window expired (FR-020): fail, terminate leftovers, advance.
            var silentSeconds = TimeSpan.FromTicks(_timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref lastEventTicks)).TotalSeconds;
            supervisionSpan?.SetTag("outcome", "liveness_failed");
            HubMetrics.RecordLivenessFailure();
            IngestSubmissionLogEvents.LogRunLivenessFailed(_logger, taskId, (long)silentSeconds, (long)_livenessWindow.TotalSeconds);

            handle.Terminate();
            var reason = $"Agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds and was terminated.";
            await FinishRunAsync(taskId, "failed", reason, writeFailureArtifact: true, CancellationToken.None);
        }
        else
        {
            var status = terminalEvent.Type == AgentRunEvent.TypeCompleted ? "completed" : "failed";
            supervisionSpan?.SetTag("outcome", status);
            await FinishRunAsync(taskId, status, terminalEvent.Reason, writeFailureArtifact: false, CancellationToken.None);
        }

        await handle.DisposeAsync();
        _ = readLoop; // read loop ends with the pipe; nothing to await after termination
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
            ConvertSteps: existing?.ConvertSteps);

        await _taskArtifactWriter.WriteAsync(artifactPath, document, cancellationToken);
    }
}

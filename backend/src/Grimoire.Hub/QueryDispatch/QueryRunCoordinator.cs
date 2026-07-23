using System.Collections.Concurrent;
using Grimoire.Hub.QueryRunArtifact;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.QueryDispatch;

/// <summary>Result of a submission attempt (contracts/query-conversation-api.md).</summary>
public abstract record QuerySubmissionResult
{
    public sealed record Accepted(QueryTurnState Turn) : QuerySubmissionResult;

    /// <summary>FR-017: the configured concurrency limit was already reached — rejected immediately, never queued.</summary>
    public sealed record ConcurrencyLimitReached : QuerySubmissionResult;
}

/// <summary>
/// Bounded-concurrency, non-blocking dispatch and supervision of Query agent runs
/// (ADR-011, FR-002/FR-005/FR-015/FR-016/FR-017). Deliberately independent of
/// <c>IngestRunCoordinator</c>: a counting semaphore (not a single slot + FIFO queue)
/// sized by <see cref="QueryConcurrencyOptions.QueryConcurrencyLimit"/>, no persisted
/// operational state (Query runs are not queued the way Ingest runs are, R7), and no
/// artifact write path in the agent process — the Hub finalizes every Query Run
/// Artifact itself via <see cref="QueryRunArtifactWriter"/> (R3).
/// </summary>
public sealed class QueryRunCoordinator
{
    private readonly AgentDispatch.IAgentProcessLauncher _launcher;
    private readonly QueryLifecyclePublisher _publisher;
    private readonly QueryRunArtifactWriter _artifactWriter;
    private readonly ResolvedGrimoirePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessWindow;
    private readonly ILogger<QueryRunCoordinator> _logger;
    private readonly SemaphoreSlim _concurrencySlots;

    private readonly ConcurrentDictionary<string, QueryTurnState> _turns = new();
    private readonly ConcurrentDictionary<string, string> _activeTurnByConversation = new();
    private readonly ConcurrentDictionary<string, AgentDispatch.IAgentProcessHandle> _handles = new();

    public QueryRunCoordinator(
        AgentDispatch.IAgentProcessLauncher launcher,
        QueryLifecyclePublisher publisher,
        QueryRunArtifactWriter artifactWriter,
        ResolvedGrimoirePaths paths,
        QueryConcurrencyOptions concurrencyOptions,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<QueryRunCoordinator>? logger = null)
    {
        _launcher = launcher;
        _publisher = publisher;
        _artifactWriter = artifactWriter;
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _logger = logger ?? NullLogger<QueryRunCoordinator>.Instance;
        _concurrencySlots = new SemaphoreSlim(concurrencyOptions.QueryConcurrencyLimit, concurrencyOptions.QueryConcurrencyLimit);
    }

    public QueryTurnState? GetTurn(string turnId) => _turns.TryGetValue(turnId, out var turn) ? turn : null;

    /// <summary>Whether the conversation already has a running turn (FR-008, wired by US3's 409 guard).</summary>
    public bool IsConversationActive(string conversationId) => _activeTurnByConversation.ContainsKey(conversationId);

    /// <summary>
    /// Accepts and immediately dispatches one Query Turn, or rejects it over the
    /// concurrency limit (FR-017) — there is no queue to wait in either way.
    /// </summary>
    public async Task<QuerySubmissionResult> SubmitTurnAsync(
        string conversationId,
        int position,
        string prompt,
        IReadOnlyList<AgentDispatch.QueryPriorTurn> priorTurns,
        CancellationToken cancellationToken = default)
    {
        if (!await _concurrencySlots.WaitAsync(0, cancellationToken))
        {
            return new QuerySubmissionResult.ConcurrencyLimitReached();
        }

        var turnId = $"{_timeProvider.GetUtcNow():yyyy-MM-dd}-query-{Guid.NewGuid():N}"[..40];

        using var submitSpan = HubTracing.ActivitySource.StartActivity("hub.query.submit");
        submitSpan?.SetTag("turn_id", turnId);
        submitSpan?.SetTag("conversation_id", conversationId);

        var turn = new QueryTurnState(turnId, conversationId, position, prompt, _timeProvider.GetUtcNow());
        _turns[turnId] = turn;
        _activeTurnByConversation[conversationId] = turnId;

        QueryLifecycleLogEvents.LogTurnCreated(_logger, conversationId, turnId);

        var request = new AgentDispatch.QueryAgentRequest(
            TurnId: turnId,
            ConversationId: conversationId,
            Prompt: prompt,
            PriorTurns: priorTurns,
            WikiRoot: _paths.ContentRoot,
            PagesDir: _paths.PagesDir,
            IndexPath: _paths.IndexPath,
            LogPath: _paths.LogPath,
            SystemPromptPath: _paths.QuerySystemPromptPath,
            PolicyPath: _paths.QueryPolicyPath);

        AgentDispatch.IAgentProcessHandle handle;
        try
        {
            using var spawnSpan = HubTracing.ActivitySource.StartActivity("hub.query.spawn_agent");
            spawnSpan?.SetTag("turn_id", turnId);
            spawnSpan?.SetTag("agent", "query");

            handle = await _launcher.StartAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinishTurnAsync(turnId, QueryTurnStatus.Failed,
                $"Query agent process could not be started: {ex.Message}", metadata: null, CancellationToken.None);
            return new QuerySubmissionResult.Accepted(turn);
        }

        _handles[turnId] = handle;

        // Fire-and-forget supervision; the coordinator is re-entered via events.
        _ = Task.Run(() => SuperviseAsync(turnId, handle, CancellationToken.None), CancellationToken.None);

        return new QuerySubmissionResult.Accepted(turn);
    }

    /// <summary>
    /// Interrupts an in-progress turn (FR-006/FR-007): terminates the agent process and
    /// transitions the turn to <see cref="QueryTurnStatus.Interrupted"/> immediately,
    /// rather than waiting on <see cref="SuperviseAsync"/>'s liveness watchdog — the user
    /// asked for this, so there is nothing to wait to detect (SC-004). Interrupting an
    /// already-terminal turn is a no-op that returns the turn's actual current state
    /// (contract: 200, not 404/409). Returns null only if the turn is unknown.
    /// </summary>
    public async Task<QueryTurnState?> InterruptAsync(string turnId, CancellationToken cancellationToken = default)
    {
        if (!_turns.TryGetValue(turnId, out var turn))
        {
            return null;
        }

        if (turn.IsTerminal)
        {
            return turn;
        }

        if (_handles.TryGetValue(turnId, out var handle))
        {
            handle.Terminate();
        }

        QueryLifecycleLogEvents.LogTurnInterrupted(_logger, turnId);
        await FinishTurnAsync(turnId, QueryTurnStatus.Interrupted, failureReason: null, metadata: null, cancellationToken);
        return turn;
    }

    private async Task SuperviseAsync(string turnId, AgentDispatch.IAgentProcessHandle handle, CancellationToken cancellationToken)
    {
        using var supervisionSpan = HubTracing.ActivitySource.StartActivity("hub.query.run_supervision");
        supervisionSpan?.SetTag("turn_id", turnId);

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

                await HandleEventAsync(turnId, runEvent, cancellationToken);

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
            var reason = $"Query agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds and was terminated.";
            await FinishTurnAsync(turnId, QueryTurnStatus.Failed, reason, null, CancellationToken.None);
        }
        else
        {
            var status = terminalEvent.Type == AgentDispatch.AgentRunEvent.TypeCompleted
                ? QueryTurnStatus.Completed
                : QueryTurnStatus.Failed;
            supervisionSpan?.SetTag("outcome", status.ToString().ToLowerInvariant());
            var metadata = new QueryTurnCompletionMetadata(
                terminalEvent.SystemPromptSha256,
                terminalEvent.PolicyPath,
                terminalEvent.PolicyVersion,
                terminalEvent.PolicySha256,
                terminalEvent.Model,
                terminalEvent.TurnsUsed,
                terminalEvent.DeniedActions ?? []);
            await FinishTurnAsync(turnId, status, terminalEvent.Reason, metadata, CancellationToken.None);
        }

        await handle.DisposeAsync();
        _ = readLoop;
    }

    private async Task HandleEventAsync(string turnId, AgentDispatch.AgentRunEvent runEvent, CancellationToken cancellationToken)
    {
        using var span = HubTracing.ActivitySource.StartActivity("hub.query.handle_run_event");
        span?.SetTag("turn_id", turnId);
        span?.SetTag("event_type", runEvent.Type);

        if (!_turns.TryGetValue(turnId, out var turn) || turn.IsTerminal)
        {
            // Terminal state already reached — diagnostic only, no state change (FR-007).
            return;
        }

        if (runEvent.Type == AgentDispatch.AgentRunEvent.TypeAnswerChunk && !string.IsNullOrEmpty(runEvent.Text))
        {
            var sequence = turn.AppendAnswerChunk(runEvent.Text);
            HubMetrics.RecordQueryAnswerChunk();
            await _publisher.PublishAnswerChunkAsync(turnId, sequence, runEvent.Text, cancellationToken);
        }
    }

    private async Task FinishTurnAsync(
        string turnId, QueryTurnStatus status, string? failureReason, QueryTurnCompletionMetadata? metadata, CancellationToken cancellationToken)
    {
        if (!_turns.TryGetValue(turnId, out var turn))
        {
            return;
        }

        var completedAt = _timeProvider.GetUtcNow();
        if (!turn.TryTransitionTo(status, failureReason, completedAt, metadata))
        {
            // Idempotence: only the first terminal transition wins (FR-007).
            return;
        }

        _activeTurnByConversation.TryRemove(new KeyValuePair<string, string>(turn.ConversationId, turnId));
        _handles.TryRemove(turnId, out _);
        _concurrencySlots.Release();

        var durationMs = (long)(completedAt - turn.StartedAt).TotalMilliseconds;
        var outcome = status.ToString().ToLowerInvariant();
        HubMetrics.RecordQueryTurn(outcome, durationMs / 1000.0);

        if (status == QueryTurnStatus.Completed)
        {
            QueryLifecycleLogEvents.LogTurnCompleted(_logger, turnId, durationMs);
        }
        else if (status == QueryTurnStatus.Failed)
        {
            QueryLifecycleLogEvents.LogTurnFailed(_logger, turnId, failureReason ?? "unknown");
        }

        await _artifactWriter.WriteAsync(_paths, turn, cancellationToken);

        var fromState = "running";
        await _publisher.PublishTurnChangedAsync(turnId, fromState, outcome, failureReason, cancellationToken);
    }
}

using System.Collections.Concurrent;
using Grimoire.Hub.QueryConversations;
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

    /// <summary>FR-008: the conversation already has a running turn — at most one active turn per conversation.</summary>
    public sealed record ConversationAlreadyActive : QuerySubmissionResult;

    /// <summary>
    /// FR-006 (011-query-conversations): the conversation's record exists but is
    /// structurally unreadable — rejected fail-closed, no turn created, no agent
    /// spawned (<c>conversation_record_unreadable</c>).
    /// </summary>
    public sealed record RecordUnreadable(string Reason) : QuerySubmissionResult;
}

/// <summary>
/// Bounded-concurrency, non-blocking dispatch and supervision of Query agent runs
/// (ADR-011, FR-002/FR-005/FR-015/FR-016/FR-017). Deliberately independent of
/// <c>IngestRunCoordinator</c>: a counting semaphore (not a single slot + FIFO queue)
/// sized by <see cref="QueryConcurrencyOptions.QueryConcurrencyLimit"/>, no persisted
/// operational state (Query runs are not queued the way Ingest runs are, R7), and no
/// artifact write path in the agent process — on every terminal transition the Hub
/// appends the turn to the Conversation Record via
/// <see cref="ConversationRecordStore"/> (ADR-014), which is also the single source of
/// the prior-turn context handed to the agent on follow-ups (research.md R1).
/// </summary>
public sealed class QueryRunCoordinator
{
    private readonly AgentDispatch.IAgentProcessLauncher _launcher;
    private readonly QueryLifecyclePublisher _publisher;
    private readonly ConversationRecordStore _recordStore;
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
        ConversationRecordStore recordStore,
        ResolvedGrimoirePaths paths,
        QueryConcurrencyOptions concurrencyOptions,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<QueryRunCoordinator>? logger = null)
    {
        _launcher = launcher;
        _publisher = publisher;
        _recordStore = recordStore;
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _logger = logger ?? NullLogger<QueryRunCoordinator>.Instance;
        _concurrencySlots = new SemaphoreSlim(concurrencyOptions.QueryConcurrencyLimit, concurrencyOptions.QueryConcurrencyLimit);
    }

    public QueryTurnState? GetTurn(string turnId) => _turns.TryGetValue(turnId, out var turn) ? turn : null;

    /// <summary>
    /// Whether the conversation already has a running turn (FR-008). Diagnostic/read-only —
    /// <see cref="SubmitTurnAsync"/>'s actual 409 guard uses an atomic
    /// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> rather than this check, to
    /// avoid a TOCTOU race between two concurrent submissions for the same conversation.
    /// </summary>
    public bool IsConversationActive(string conversationId) => _activeTurnByConversation.ContainsKey(conversationId);

    /// <summary>
    /// Accepts and immediately dispatches one Query Turn, or rejects it over the
    /// concurrency limit (FR-017) — there is no queue to wait in either way. The
    /// prior-turn context comes from the Conversation Record (ADR-014): the Hub assigns
    /// <c>position</c> = recorded turns + 1 and rejects fail-closed
    /// (<see cref="QuerySubmissionResult.RecordUnreadable"/>) when the record exists
    /// but cannot be parsed — no turn created, no agent spawned (FR-006).
    /// </summary>
    public async Task<QuerySubmissionResult> SubmitTurnAsync(
        string conversationId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (!await _concurrencySlots.WaitAsync(0, cancellationToken))
        {
            return new QuerySubmissionResult.ConcurrencyLimitReached();
        }

        var turnId = $"{_timeProvider.GetUtcNow():yyyy-MM-dd}-query-{Guid.NewGuid():N}"[..40];

        // Atomic reservation (FR-008): TryAdd is the actual race-free guard against two
        // concurrent submissions for the same conversation both passing a separate
        // check-then-set; a plain IsConversationActive() check followed by a later
        // assignment would leave a window where both requests observe "no active turn".
        if (!_activeTurnByConversation.TryAdd(conversationId, turnId))
        {
            _concurrencySlots.Release();
            return new QuerySubmissionResult.ConversationAlreadyActive();
        }

        using var submitSpan = HubTracing.ActivitySource.StartActivity("hub.query.submit");
        submitSpan?.SetTag("turn_id", turnId);
        submitSpan?.SetTag("conversation_id", conversationId);

        // Record-sourced context (ADR-014, SC-005): served from the in-memory cache,
        // hydrated from the record file after a Hub restart, empty for a new
        // conversation. The one-active-turn guard above means every prior turn is
        // terminal and therefore already recorded (research.md R1).
        IReadOnlyList<AgentDispatch.QueryPriorTurn> priorTurns;
        using (var loadSpan = HubTracing.ActivitySource.StartActivity("hub.query.load_conversation_context"))
        {
            loadSpan?.SetTag("conversation_id", conversationId);

            var contextResult = await _recordStore.LoadContextAsync(conversationId, cancellationToken);
            if (contextResult is ConversationContextResult.Unreadable unreadable)
            {
                loadSpan?.SetTag("source", "unreadable");
                _activeTurnByConversation.TryRemove(new KeyValuePair<string, string>(conversationId, turnId));
                _concurrencySlots.Release();
                return new QuerySubmissionResult.RecordUnreadable(unreadable.Reason);
            }

            var loaded = (ConversationContextResult.Loaded)contextResult;
            priorTurns = loaded.Turns;
            loadSpan?.SetTag("turn_count", priorTurns.Count);
            loadSpan?.SetTag("source", loaded.Source);
        }

        var position = priorTurns.Count + 1;
        var turn = new QueryTurnState(turnId, conversationId, position, prompt, _timeProvider.GetUtcNow());
        _turns[turnId] = turn;
        HubMetrics.AdjustQueryConcurrentRuns(1);

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
        HubMetrics.AdjustQueryConcurrentRuns(-1);

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

        // Guarded record append (ADR-014, research.md R6): a record-write failure is
        // logged and counted but never alters the turn's outcome nor suppresses the
        // queryTurnChanged broadcast below — deliberately isolated (own try/catch, own
        // non-cancellable token), fixing in passing 008's unguarded artifact write that
        // would have skipped the publish on throw.
        try
        {
            using var recordSpan = HubTracing.ActivitySource.StartActivity("hub.query.record_turn");
            recordSpan?.SetTag("conversation_id", turn.ConversationId);
            recordSpan?.SetTag("turn_id", turnId);
            recordSpan?.SetTag("outcome", outcome);

            await _recordStore.AppendTurnAsync(turn.ConversationId, BuildRecordedTurn(turn, outcome), CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConversationRecordLogEvents.LogRecordAppendFailed(_logger, turn.ConversationId, turnId, ex.Message);
            HubMetrics.RecordConversationRecordAppendFailure();
        }

        var fromState = "running";
        await _publisher.PublishTurnChangedAsync(turnId, fromState, outcome, failureReason, cancellationToken);
    }

    /// <summary>
    /// Maps a terminal turn's full data (prompt, accumulated answer buffer, state,
    /// failure reason, timestamps from <see cref="QueryTurnState"/>, instruction/policy
    /// identity, model, turns used, denied actions from the ADR-006 terminal-event
    /// metadata) to its Recorded Turn (data-model.md Turn Bookkeeping).
    /// </summary>
    private static RecordedTurn BuildRecordedTurn(QueryTurnState turn, string outcome)
    {
        var metadata = turn.CompletionMetadata;
        return new RecordedTurn(
            TurnId: turn.TurnId,
            Position: turn.Position,
            State: outcome,
            FailureReason: turn.FailureReason,
            StartedAt: turn.StartedAt,
            CompletedAt: turn.CompletedAt,
            Model: metadata?.Model,
            TurnsUsed: metadata?.TurnsUsed,
            InstructionFilePath: metadata?.SystemPromptSha256 is null ? null : "agents/query/system-prompt.md",
            InstructionFileSha256: metadata?.SystemPromptSha256,
            PolicyPath: metadata?.PolicyPath,
            PolicyVersion: metadata?.PolicyVersion,
            PolicySha256: metadata?.PolicySha256,
            DeniedActions: [.. (metadata?.DeniedActions ?? []).Select(d =>
                new RecordedDeniedAction(d.Action, d.RequestedTarget, d.CanonicalTarget, d.Reason, d.Turn))],
            Prompt: turn.Prompt,
            Answer: turn.Answer);
    }
}

using System.Collections.Concurrent;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>Result of a message-turn submission attempt (contracts/remediation-task-api.md).</summary>
public abstract record RemediationMessageTurnSubmissionResult
{
    public sealed record Accepted(string MessageTurnId, DateTimeOffset AcceptedAt) : RemediationMessageTurnSubmissionResult;

    /// <summary>One message turn at a time per task (mirrors Query's <c>conversation_already_active</c>).</summary>
    public sealed record TurnActive : RemediationMessageTurnSubmissionResult;
}

/// <summary>
/// Bounded, non-blocking dispatch and supervision of Remediation Task message turns
/// (015-lint-board-parity T041/T042, US5, FR-012, ADR-018 "Message-turn mode": a bounded,
/// read-only single exchange reusing the Query-turn shape, ADR-011). Deliberately
/// independent of <see cref="RemediationRunCoordinator"/> — a message turn is advisory
/// Q&amp;A about a <c>Proposed</c> task, not execution: it is never gated by authorization,
/// never transitions the task's execution state machine, and applies no wiki write (its
/// spawn request carries a deny-by-default, no-write policy — see
/// <c>Grimoire.LintAgent</c>'s message-turn mode). At most one turn runs per task at a
/// time (atomic reservation, mirrors <c>QueryRunCoordinator</c>'s
/// <c>_activeTurnByConversation</c>).
///
/// This is the <b>second</b> type in <c>Grimoire.Hub.RemediationTasks</c> permitted to
/// reference <see cref="AgentDispatch.IAgentProcessLauncher"/>
/// (<c>Grimoire.ArchTests.RemediationExecutionDispatchRuleTests</c>'s allow-list, extended
/// alongside <see cref="RemediationRunCoordinator"/> for T042) — see that rule's doc
/// comment and <see cref="AgentDispatch.IAgentProcessLauncher.StartAsync(RemediationMessageTurnAgentRequest, CancellationToken)"/>
/// for why this second call site does not weaken SC-005.
/// </summary>
public sealed class RemediationMessageTurnCoordinator
{
    private readonly AgentDispatch.IAgentProcessLauncher _launcher;
    private readonly RemediationLifecyclePublisher _publisher;
    private readonly RemediationTaskRecordStore _recordStore;
    private readonly ResolvedGrimoirePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessWindow;
    private readonly ILogger<RemediationMessageTurnCoordinator> _logger;

    private readonly ConcurrentDictionary<string, string> _activeTurnByTask = new();

    public RemediationMessageTurnCoordinator(
        AgentDispatch.IAgentProcessLauncher launcher,
        RemediationLifecyclePublisher publisher,
        RemediationTaskRecordStore recordStore,
        ResolvedGrimoirePaths paths,
        TimeProvider? timeProvider = null,
        TimeSpan? livenessWindow = null,
        ILogger<RemediationMessageTurnCoordinator>? logger = null)
    {
        _launcher = launcher;
        _publisher = publisher;
        _recordStore = recordStore;
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _livenessWindow = livenessWindow ?? TimeSpan.FromSeconds(60);
        _logger = logger ?? NullLogger<RemediationMessageTurnCoordinator>.Instance;
    }

    /// <summary>Whether a message turn is currently running for this task (contract `messageTurnActive`).</summary>
    public bool IsTurnActive(string taskId) => _activeTurnByTask.ContainsKey(taskId);

    /// <summary>
    /// Accepts and immediately dispatches one message turn for <paramref name="row"/>
    /// (contract: caller has already verified the task is <c>Proposed</c>— this method's
    /// own concern is only the one-turn-at-a-time invariant, atomically). Appends the
    /// human message to the record before spawning (contract: "appended to the record
    /// immediately"); the context handed to the agent is everything the record held
    /// <em>before</em> that append (R6 record-as-context).
    /// </summary>
    public async Task<RemediationMessageTurnSubmissionResult> SubmitMessageTurnAsync(
        RemediationTaskRow row, string content, CancellationToken cancellationToken = default)
    {
        var turnId = $"{_timeProvider.GetUtcNow():yyyy-MM-dd}-remtask-msg-{Guid.NewGuid():N}"[..44];

        if (!_activeTurnByTask.TryAdd(row.TaskId, turnId))
        {
            return new RemediationMessageTurnSubmissionResult.TurnActive();
        }

        using var span = HubTracing.ActivitySource.StartActivity("hub.remediation.message_turn");
        span?.SetTag("task_id", row.TaskId);

        var acceptedAt = _timeProvider.GetUtcNow();

        var entries = await _recordStore.ReadAsync(row.TaskId, cancellationToken) is RemediationTaskRecordParseResult.Parsed parsed
            ? parsed.Entries
            : [];
        var attachedContext = RemediationTaskRecordContext.BuildAttachedContext(entries);
        var priorMessages = RemediationTaskRecordContext.BuildPriorMessages(entries);

        await _recordStore.AppendMessageAsync(row.TaskId, RemediationTaskRecordFormat.SenderHuman, content, acceptedAt, cancellationToken);
        RemediationLifecycleLogEvents.LogMessageRecorded(_logger, row.TaskId, RemediationTaskRecordFormat.SenderHuman);

        await _publisher.PublishMessageTurnChangedAsync(row.TaskId, turnId, "running", cancellationToken: cancellationToken);

        var request = new RemediationMessageTurnAgentRequest(
            TaskId: row.TaskId,
            RunId: row.RunId,
            Title: row.Title,
            Description: row.Description,
            TargetPath: row.TargetPath,
            WikiRoot: _paths.WikiDir,
            FoundationPromptPath: _paths.ResolveEffectiveFoundationPrompt(_paths.Lint).Path,
            SystemPromptPath: _paths.Lint.SystemPromptPath,
            PolicyPath: _paths.Lint.PolicyPath,
            WriteLocksDir: _paths.WriteLocksDir,
            AttachedContext: attachedContext,
            Message: content,
            PriorMessages: priorMessages);

        AgentDispatch.IAgentProcessHandle handle;
        try
        {
            handle = await _launcher.StartAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinishTurnAsync(row.TaskId, turnId, answered: false,
                failureReason: $"Message-turn agent process could not be started: {ex.Message}", replyText: null, CancellationToken.None);
            return new RemediationMessageTurnSubmissionResult.Accepted(turnId, acceptedAt);
        }

        // Fire-and-forget supervision; the coordinator is re-entered via events.
        _ = Task.Run(() => SuperviseAsync(row.TaskId, turnId, handle, CancellationToken.None), CancellationToken.None);

        return new RemediationMessageTurnSubmissionResult.Accepted(turnId, acceptedAt);
    }

    private async Task SuperviseAsync(string taskId, string turnId, AgentDispatch.IAgentProcessHandle handle, CancellationToken cancellationToken)
    {
        var lastEventTicks = _timeProvider.GetUtcNow().UtcTicks;
        var terminal = new TaskCompletionSource<AgentDispatch.AgentRunEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Liveness watchdog, unchanged shape from Query/Remediation execution (ADR-008):
        // event silence beyond the window is the sole failure authority. No streaming
        // events are expected in this mode (contract: "no answer_chunk streaming") — only
        // started/heartbeat/activity/terminal.
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
            HubMetrics.RecordLivenessFailure();
            handle.Terminate();
            var reason = $"Message-turn agent run showed no liveness for {(long)_livenessWindow.TotalSeconds} seconds and was terminated.";
            await FinishTurnAsync(taskId, turnId, answered: false, failureReason: reason, replyText: null, CancellationToken.None);
        }
        else if (terminalEvent.Type == AgentDispatch.AgentRunEvent.TypeCompleted)
        {
            // contracts/remediation-lifecycle-events.md "Message-turn mode terminal event":
            // the reply travels on the existing `text` field — Principle V: the Hub only
            // ever transports this verbatim, never composes or edits it.
            var reply = terminalEvent.Text ?? terminalEvent.Summary ?? string.Empty;
            await FinishTurnAsync(taskId, turnId, answered: true, failureReason: null, replyText: reply, CancellationToken.None);
        }
        else
        {
            var reason = terminalEvent.Reason ?? "Message-turn agent run failed.";
            await FinishTurnAsync(taskId, turnId, answered: false, failureReason: reason, replyText: null, CancellationToken.None);
        }

        await handle.DisposeAsync();
        _ = readLoop;
    }

    private async Task FinishTurnAsync(
        string taskId, string turnId, bool answered, string? failureReason, string? replyText, CancellationToken cancellationToken)
    {
        _activeTurnByTask.TryRemove(new KeyValuePair<string, string>(taskId, turnId));

        if (answered)
        {
            var now = _timeProvider.GetUtcNow();
            try
            {
                await _recordStore.AppendMessageAsync(taskId, RemediationTaskRecordFormat.SenderAgent, replyText ?? string.Empty, now, cancellationToken);
                RemediationLifecycleLogEvents.LogMessageRecorded(_logger, taskId, RemediationTaskRecordFormat.SenderAgent);
            }
            catch (Exception ex)
            {
                // A record-write failure here must not silently claim success to the
                // client (mirrors QueryRunCoordinator.FinishTurnAsync's isolated
                // try/catch around its own record append) — but the agent did answer, so
                // still report "answered" for the metric; the failed record append is its
                // own diagnostic concern.
                _logger.LogError(ex, "Failed to append the agent's reply to remediation task record {TaskId}.", taskId);
            }

            HubMetrics.RecordRemediationMessageTurn("answered");
            await _publisher.PublishMessageTurnChangedAsync(taskId, turnId, "completed", cancellationToken: cancellationToken);
        }
        else
        {
            // A failed turn appends no agent entry (contract: "the failure is surfaced via
            // the remediationMessageTurnChanged broadcast (with reason), not silently
            // dropped") — the human message appended before spawn stays in the record.
            HubMetrics.RecordRemediationMessageTurn("failed");
            await _publisher.PublishMessageTurnChangedAsync(taskId, turnId, "failed", failureReason, cancellationToken);
        }
    }
}

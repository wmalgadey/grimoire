using Grimoire.Hub.OperationalState;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>Request body for POST /{taskId}/context (contracts/remediation-task-api.md).</summary>
internal sealed record RemediationAttachContextRequest(string? Content);

/// <summary>Request body for POST /{taskId}/messages (contracts/remediation-task-api.md).</summary>
internal sealed record RemediationSendMessageRequest(string? Content);

/// <summary>
/// HTTP endpoints for the Remediation Action Task workflow (015-lint-board-parity T024/
/// T033/T041, contracts/remediation-task-api.md; mirrors <c>LintSubmissionEndpoints</c>'
/// Minimal-API route-group pattern). US3 shipped the read surface — list (the board's
/// initial-state recovery source for remediation entries) and detail (including
/// record-derived attached context, FR-011/FR-014). US4 (T033) added the CAS-backed
/// authorize/dismiss/withdraw-authorization transitions. US5 (T041) adds attach-context
/// (FR-011), send-message (FR-012), and get-history (FR-014). There is deliberately no
/// execution endpoint — dispatch happens only via
/// <c>RemediationRunCoordinator.TryStartNextAsync</c> (ADR-018, SC-005): these endpoints
/// never reference <c>IAgentProcessLauncher</c> directly, only the coordinators' own
/// submission methods (<see cref="RemediationRunCoordinator.TryStartNextAsync"/>,
/// <see cref="RemediationMessageTurnCoordinator.SubmitMessageTurnAsync"/>).
/// </summary>
public static class RemediationTaskEndpoints
{
    private const int ContentMaxLength = 8000;

    public static RouteGroupBuilder MapRemediationTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{taskId}", GetDetailAsync);
        group.MapPost("/{taskId}/authorize", AuthorizeAsync);
        group.MapPost("/{taskId}/dismiss", DismissAsync);
        group.MapPost("/{taskId}/withdraw-authorization", WithdrawAuthorizationAsync);
        group.MapPost("/{taskId}/context", AttachContextAsync);
        group.MapPost("/{taskId}/messages", SendMessageAsync);
        group.MapGet("/{taskId}/messages", GetMessagesAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        OperationalStateRepository repository,
        string? runId,
        CancellationToken cancellationToken)
    {
        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var queuePositions = ComputeQueuePositions(rows);

        var tasks = rows
            .Where(row => runId is null || row.RunId == runId)
            .Select(row => ToListEntry(row, queuePositions))
            .ToList();

        return Results.Ok(new { tasks });
    }

    private static async Task<IResult> GetDetailAsync(
        string taskId,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        RemediationMessageTurnCoordinator messageTurnCoordinator,
        CancellationToken cancellationToken)
    {
        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        var row = rows.FirstOrDefault(r => r.TaskId == taskId);
        if (row is null)
        {
            return Results.NotFound(new { message = $"Remediation task '{taskId}' was not found." });
        }

        // Record-derived history (FR-011/FR-014): attached context in append order,
        // readable in every state including terminal ones. A missing/unreadable record
        // yields an empty history, never a failed detail read — the SQLite row is the
        // state authority (data-model.md).
        var attachedContext = new List<object>();
        if (await recordStore.ReadAsync(taskId, cancellationToken) is RemediationTaskRecordParseResult.Parsed parsed)
        {
            foreach (var entry in parsed.Entries)
            {
                if (entry is RemediationTaskRecordEntry.Context context)
                {
                    attachedContext.Add(new { content = context.Text, attachedAt = context.AttachedAt });
                }
            }
        }

        var queuePositions = ComputeQueuePositions(rows);
        return Results.Ok(new
        {
            taskId = row.TaskId,
            runId = row.RunId,
            title = row.Title,
            description = row.Description,
            targetPath = row.TargetPath,
            state = row.State,
            proposedAt = row.ProposedAt,
            authorizedAt = row.AuthorizedAt,
            queuePosition = queuePositions.TryGetValue(row.TaskId, out var position) ? (int?)position : null,
            outcomeReason = row.OutcomeReason,
            updatedAt = row.UpdatedAt,
            attachedContext,
            messageTurnActive = messageTurnCoordinator.IsTurnActive(taskId),
        });
    }

    /// <summary>
    /// POST /{taskId}/authorize (T033, FR-009; 018-hub-cli-commands T022): thin wrapper
    /// over <see cref="RemediationTaskTransitionService.AuthorizeAsync"/> — the CAS,
    /// publish, metrics, log events, and eager dispatch kick all now live there (shared
    /// with <c>RemediationAuthorizeCommand</c>, FR-005/SC-005); this handler only
    /// translates the result union to the same <see cref="IResult"/> shapes it always
    /// returned.
    /// </summary>
    private static async Task<IResult> AuthorizeAsync(
        string taskId,
        RemediationTaskTransitionService transitionService,
        CancellationToken cancellationToken)
    {
        var result = await transitionService.AuthorizeAsync(taskId, cancellationToken);
        return result switch
        {
            RemediationTransitionResult.NotFound => NotFound(taskId),
            RemediationTransitionResult.Conflict conflict => ToConflictResult(conflict),
            RemediationTransitionResult.Ok ok => Results.Ok(new
            {
                taskId = ok.TaskId,
                state = ok.NewState,
                authorizedAt = ok.AuthorizedAt,
                queuePosition = ok.QueuePosition,
            }),
            _ => throw new InvalidOperationException($"Unknown {nameof(RemediationTransitionResult)}: {result.GetType()}."),
        };
    }

    /// <summary>
    /// POST /{taskId}/dismiss (T033, FR-010; 018-hub-cli-commands T022): thin wrapper over
    /// <see cref="RemediationTaskTransitionService.DismissAsync"/> (see
    /// <see cref="AuthorizeAsync"/>'s doc comment for the extraction rationale).
    /// </summary>
    private static async Task<IResult> DismissAsync(
        string taskId,
        RemediationTaskTransitionService transitionService,
        CancellationToken cancellationToken)
    {
        var result = await transitionService.DismissAsync(taskId, cancellationToken);
        return result switch
        {
            RemediationTransitionResult.NotFound => NotFound(taskId),
            RemediationTransitionResult.Conflict conflict => ToConflictResult(conflict),
            RemediationTransitionResult.Ok ok => Results.Ok(new { taskId = ok.TaskId, state = ok.NewState, dismissedAt = ok.AuthorizedAt }),
            _ => throw new InvalidOperationException($"Unknown {nameof(RemediationTransitionResult)}: {result.GetType()}."),
        };
    }

    /// <summary>
    /// POST /{taskId}/withdraw-authorization (T033, FR-016; 018-hub-cli-commands T022):
    /// thin wrapper over <see cref="RemediationTaskTransitionService.WithdrawAuthorizationAsync"/>
    /// (see <see cref="AuthorizeAsync"/>'s doc comment for the extraction rationale).
    /// </summary>
    private static async Task<IResult> WithdrawAuthorizationAsync(
        string taskId,
        RemediationTaskTransitionService transitionService,
        CancellationToken cancellationToken)
    {
        var result = await transitionService.WithdrawAuthorizationAsync(taskId, cancellationToken);
        return result switch
        {
            RemediationTransitionResult.NotFound => NotFound(taskId),
            RemediationTransitionResult.Conflict conflict => ToConflictResult(conflict),
            RemediationTransitionResult.Ok ok => Results.Ok(new { taskId = ok.TaskId, state = ok.NewState }),
            _ => throw new InvalidOperationException($"Unknown {nameof(RemediationTransitionResult)}: {result.GetType()}."),
        };
    }

    /// <summary>
    /// POST /{taskId}/context (T041, FR-011): attach additional information/instructions
    /// to a task. Allowed only while <c>proposed</c> — once authorized, what was
    /// authorized is fixed (withdraw first to add more context). Appended verbatim to the
    /// Remediation Task Record; reaches the execution run as the ADR-007 user-prompt
    /// override the moment the coordinator next dispatches this task
    /// (<see cref="RemediationRunCoordinator.StartRunAsync"/>).
    /// </summary>
    private static async Task<IResult> AttachContextAsync(
        string taskId,
        RemediationAttachContextRequest? body,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        CancellationToken cancellationToken)
    {
        var validation = ValidateContent(body?.Content);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        var row = await FindTaskAsync(repository, taskId, cancellationToken);
        if (row is null)
        {
            return NotFound(taskId);
        }

        if (row.State != RemediationTaskStates.Proposed)
        {
            return TaskNotProposedConflict(row);
        }

        var attachedAt = DateTimeOffset.UtcNow;
        await recordStore.AppendContextAsync(taskId, validation.Trimmed!, attachedAt, cancellationToken);

        return Results.Ok(new { taskId, attachedAt });
    }

    /// <summary>
    /// POST /{taskId}/messages (T041, FR-012): sends the agent a message about this task.
    /// Non-blocking — the human message is appended to the record immediately, then a
    /// bounded message turn is dispatched via
    /// <see cref="RemediationMessageTurnCoordinator.SubmitMessageTurnAsync"/>; the reply is
    /// appended and broadcast when the turn completes. Allowed only while <c>proposed</c>
    /// (messaging exists to steer the proposal before authorization, US5); at most one
    /// turn at a time per task.
    /// </summary>
    private static async Task<IResult> SendMessageAsync(
        string taskId,
        RemediationSendMessageRequest? body,
        OperationalStateRepository repository,
        RemediationMessageTurnCoordinator messageTurnCoordinator,
        CancellationToken cancellationToken)
    {
        var validation = ValidateContent(body?.Content);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        var row = await FindTaskAsync(repository, taskId, cancellationToken);
        if (row is null)
        {
            return NotFound(taskId);
        }

        if (row.State != RemediationTaskStates.Proposed)
        {
            return TaskNotProposedConflict(row);
        }

        var result = await messageTurnCoordinator.SubmitMessageTurnAsync(row, validation.Trimmed!, cancellationToken);

        return result switch
        {
            RemediationMessageTurnSubmissionResult.Accepted accepted => Results.Accepted(value: new
            {
                taskId,
                messageTurnId = accepted.MessageTurnId,
                state = "running",
                acceptedAt = accepted.AcceptedAt,
            }),
            RemediationMessageTurnSubmissionResult.TurnActive => Results.Conflict(new
            {
                reason = "message_turn_active",
                state = row.State,
                message = "A message turn is already running for this task; wait for it to finish before sending another.",
            }),
            _ => throw new InvalidOperationException($"Unknown message-turn submission result: {result.GetType().Name}"),
        };
    }

    /// <summary>
    /// GET /{taskId}/messages (T041, FR-014): full message history, available in every
    /// state including terminal ones — never 409. A task with no messages yet returns an
    /// empty array, not 404 (only an unknown task id is 404).
    /// </summary>
    private static async Task<IResult> GetMessagesAsync(
        string taskId,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        RemediationMessageTurnCoordinator messageTurnCoordinator,
        CancellationToken cancellationToken)
    {
        var row = await FindTaskAsync(repository, taskId, cancellationToken);
        if (row is null)
        {
            return NotFound(taskId);
        }

        var messages = new List<object>();
        if (await recordStore.ReadAsync(taskId, cancellationToken) is RemediationTaskRecordParseResult.Parsed parsed)
        {
            foreach (var entry in parsed.Entries)
            {
                if (entry is RemediationTaskRecordEntry.Message message)
                {
                    messages.Add(new { sender = message.Sender, content = message.Text, timestamp = message.Timestamp });
                }
            }
        }

        return Results.Ok(new
        {
            taskId,
            messageTurnActive = messageTurnCoordinator.IsTurnActive(taskId),
            messages,
        });
    }

    /// <summary>
    /// Server-side re-validation of attach-context/message content (mirrors
    /// <c>QuerySubmissionValidator.ValidatePrompt</c>): required, non-empty after trim,
    /// ≤ <see cref="ContentMaxLength"/> characters (contracts/remediation-task-api.md).
    /// </summary>
    private static (bool IsValid, string? ErrorMessage, string? Trimmed) ValidateContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (false, "content must not be empty or whitespace-only.", null);
        }

        var trimmed = content.Trim();
        if (trimmed.Length > ContentMaxLength)
        {
            return (false, $"content exceeds the maximum of {ContentMaxLength} characters.", null);
        }

        return (true, null, trimmed);
    }

    private static async Task<RemediationTaskRow?> FindTaskAsync(
        OperationalStateRepository repository, string taskId, CancellationToken cancellationToken)
    {
        var rows = await repository.GetRemediationTasksAsync(cancellationToken: cancellationToken);
        return rows.FirstOrDefault(r => r.TaskId == taskId);
    }

    private static IResult NotFound(string taskId)
        => Results.NotFound(new { message = $"Remediation task '{taskId}' was not found." });

    private static IResult TaskNotProposedConflict(RemediationTaskRow row)
        => Results.Conflict(new
        {
            reason = "task_not_proposed",
            state = row.State,
            message = $"Only a proposed task can be authorized or dismissed. This task is {row.State}.",
        });

    /// <summary>
    /// 018-hub-cli-commands T022: translates a
    /// <see cref="RemediationTaskTransitionService"/> conflict (reason + current state
    /// only) back into the exact 409 body shape (incl. the human-readable <c>message</c>)
    /// the pre-extraction inline handlers returned, for all three reasons the service can
    /// produce across authorize/dismiss/withdraw:
    /// <c>task_not_proposed</c> (authorize/dismiss), <c>task_not_authorized</c> and
    /// <c>execution_already_started</c> (withdraw — contracts/remediation-task-api.md
    /// "withdraw-authorization error shapes").
    /// </summary>
    private static IResult ToConflictResult(RemediationTransitionResult.Conflict conflict) => conflict.Reason switch
    {
        "task_not_proposed" => Results.Conflict(new
        {
            reason = conflict.Reason,
            state = conflict.CurrentState,
            message = $"Only a proposed task can be authorized or dismissed. This task is {conflict.CurrentState}.",
        }),
        "task_not_authorized" => Results.Conflict(new
        {
            reason = conflict.Reason,
            state = conflict.CurrentState,
            message = $"Only an authorized task can have its authorization withdrawn. This task is {conflict.CurrentState}.",
        }),
        "execution_already_started" => Results.Conflict(new
        {
            reason = conflict.Reason,
            state = conflict.CurrentState,
            message = "The agent already began executing this task; it will run to a terminal outcome and can no longer be cancelled.",
        }),
        _ => throw new InvalidOperationException($"Unknown remediation transition conflict reason: {conflict.Reason}"),
    };

    private static object ToListEntry(RemediationTaskRow row, IReadOnlyDictionary<string, int> queuePositions) => new
    {
        taskId = row.TaskId,
        runId = row.RunId,
        title = row.Title,
        description = row.Description,
        targetPath = row.TargetPath,
        state = row.State,
        proposedAt = row.ProposedAt,
        authorizedAt = row.AuthorizedAt,
        queuePosition = queuePositions.TryGetValue(row.TaskId, out var position) ? (int?)position : null,
        outcomeReason = row.OutcomeReason,
        updatedAt = row.UpdatedAt,
    };

    /// <summary>
    /// 1-based FIFO positions among tasks waiting to execute, ordered by
    /// <c>authorized_at</c> (FR-017, ADR-018 — <c>authorized_at</c> is the FIFO order
    /// authority; present only for <c>authorized</c> rows, contract `queuePosition`).
    /// Mechanical ranking of persisted rows — US4's coordinator dequeues in exactly this
    /// order.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ComputeQueuePositions(IReadOnlyList<RemediationTaskRow> rows)
        => rows
            .Where(row => row.State == RemediationTaskStates.Authorized && row.AuthorizedAt is not null)
            .OrderBy(row => row.AuthorizedAt)
            .ThenBy(row => row.TaskId, StringComparer.Ordinal)
            .Select((row, index) => (row.TaskId, Position: index + 1))
            .ToDictionary(entry => entry.TaskId, entry => entry.Position);
}

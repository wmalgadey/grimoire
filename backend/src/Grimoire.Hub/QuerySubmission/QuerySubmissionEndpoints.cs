using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.QueryDispatch;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.QuerySubmission;

internal sealed record QueryPriorTurnRequest(int Position, string Prompt, string Answer, string State);

internal sealed record QueryTurnSubmissionRequest(string Prompt, IReadOnlyList<QueryPriorTurnRequest>? PriorTurns);

/// <summary>
/// HTTP endpoints for Query Turn submission and status (contracts/query-conversation-api.md).
/// </summary>
public static class QuerySubmissionEndpoints
{
    public static RouteGroupBuilder MapQueryConversationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{conversationId}/turns", PostTurnAsync);
        return group;
    }

    public static RouteGroupBuilder MapQueryTurnEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{turnId}", GetTurnAsync);
        group.MapPost("/{turnId}/interrupt", PostInterruptAsync);
        return group;
    }

    private static async Task<IResult> PostTurnAsync(
        string conversationId,
        QueryTurnSubmissionRequest? body,
        QuerySubmissionValidator validator,
        QueryRunCoordinator coordinator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Results.BadRequest(new { message = "Request body is required." });
        }

        var validation = validator.ValidatePrompt(body.Prompt);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        var priorTurns = (body.PriorTurns ?? [])
            .Select(t => new QueryPriorTurn(t.Position, t.Prompt, t.Answer, t.State))
            .ToList();
        var position = priorTurns.Count + 1;

        var result = await coordinator.SubmitTurnAsync(
            conversationId, position, body.Prompt.Trim(), priorTurns, cancellationToken);

        switch (result)
        {
            case QuerySubmissionResult.Accepted accepted:
                return Results.Accepted(value: new
                {
                    turnId = accepted.Turn.TurnId,
                    conversationId,
                    position = accepted.Turn.Position,
                    state = "running",
                    acceptedAt = accepted.Turn.StartedAt,
                });

            case QuerySubmissionResult.ConcurrencyLimitReached:
            {
                HubMetrics.RecordQuerySubmissionRejected();
                var logger = loggerFactory.CreateLogger(typeof(QuerySubmissionEndpoints));
                QueryLifecycleLogEvents.LogSubmissionRejected(logger, conversationId);
                return Results.Json(new { reason = "query_concurrency_limit_reached" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            case QuerySubmissionResult.ConversationAlreadyActive:
            {
                var logger = loggerFactory.CreateLogger(typeof(QuerySubmissionEndpoints));
                QueryLifecycleLogEvents.LogSubmissionRejected(logger, conversationId);
                return Results.Conflict(new { reason = "conversation_already_active" });
            }

            default:
                throw new InvalidOperationException($"Unknown submission result: {result.GetType().Name}");
        }
    }

    private static Task<IResult> GetTurnAsync(string turnId, QueryRunCoordinator coordinator)
    {
        var turn = coordinator.GetTurn(turnId);
        if (turn is null)
        {
            return Task.FromResult(Results.NotFound(new { message = $"Query turn '{turnId}' was not found." }));
        }

        return Task.FromResult(Results.Ok(new
        {
            turnId = turn.TurnId,
            conversationId = turn.ConversationId,
            position = turn.Position,
            prompt = turn.Prompt,
            answer = turn.Answer,
            state = turn.Status.ToString().ToLowerInvariant(),
            failureReason = turn.FailureReason,
        }));
    }

    private static async Task<IResult> PostInterruptAsync(
        string turnId, QueryRunCoordinator coordinator, CancellationToken cancellationToken)
    {
        var turn = await coordinator.InterruptAsync(turnId, cancellationToken);
        if (turn is null)
        {
            return Results.NotFound(new { message = $"Query turn '{turnId}' was not found." });
        }

        return Results.Ok(new { turnId = turn.TurnId, state = turn.Status.ToString().ToLowerInvariant() });
    }
}

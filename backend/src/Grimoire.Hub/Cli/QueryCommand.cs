using System.Text.RegularExpressions;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.QuerySubmission;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Settings for <see cref="QueryCommand"/> (018-hub-cli-commands T032, data-model.md
/// "QuerySettings", contracts/cli-commands.md "query"): a required positional
/// <c>&lt;prompt&gt;</c> (non-empty after trim, ≤ <see cref="QuerySubmissionValidator.PromptMaxLength"/>
/// chars — reusing the HTTP submission path's own limit), an optional
/// <c>--conversation-id</c> (ADR-014 <c>^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$</c>), and an
/// optional <c>--timeout</c> in seconds (positive integer, default 300 — FR-015). All
/// four checks run in <see cref="Validate"/>, which Spectre calls before
/// <c>ExecuteAsync</c> runs — a missing/malformed value never submits a turn
/// (FR-009/FR-018).
/// </summary>
public sealed partial class QuerySettings : HubPathSettings
{
    [CommandArgument(0, "<prompt>")]
    public string? Prompt { get; set; }

    [CommandOption("--conversation-id <ID>")]
    public string? ConversationId { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    public int Timeout { get; set; } = 300;

    // ADR-014: the same conversationId path-safety pattern QuerySubmissionValidator
    // enforces on the HTTP path (source-generated: no runtime regex compilation).
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ConversationIdPattern();

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            return ValidationResult.Error("<prompt> is required and must not be empty.");
        }

        if (Prompt.Trim().Length > QuerySubmissionValidator.PromptMaxLength)
        {
            return ValidationResult.Error(
                $"<prompt> must not exceed {QuerySubmissionValidator.PromptMaxLength} characters.");
        }

        if (ConversationId is not null && !ConversationIdPattern().IsMatch(ConversationId))
        {
            return ValidationResult.Error("--conversation-id must match ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$.");
        }

        if (Timeout <= 0)
        {
            return ValidationResult.Error("--timeout must be a positive integer.");
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Submits a query turn via <see cref="QueryRunCoordinator.SubmitTurnAsync"/> — the same
/// in-process call the HTTP turn-submission endpoint makes (018-hub-cli-commands T033,
/// contracts/cli-commands.md "query", FR-005/FR-014/SC-005) — and blocks until the turn
/// reaches a terminal state, streaming the accumulating answer to stderr while waiting
/// (FR-006). On <c>--timeout</c> expiry or an OS interrupt signal (Ctrl-C — wired by
/// <see cref="HubCliApp"/> into the <see cref="CancellationToken"/> this command
/// receives) the CLI calls <see cref="QueryRunCoordinator.InterruptAsync"/> for the turn
/// before exiting — the same action the HTTP interrupt endpoint uses — so no agent work
/// continues unsupervised past the CLI's own exit (FR-015/FR-016).
///
/// Polls <see cref="QueryTurnState.IsTerminal"/>/<see cref="QueryTurnState.Answer"/>
/// directly on the live <see cref="QueryTurnState"/> instance <c>SubmitTurnAsync</c>
/// returns (mirrors <see cref="LintRunCommand.WaitForTerminalAsync"/>'s polling idiom —
/// no push/completion signal exists beyond the state object itself) rather than a
/// <c>Task.WhenAny</c> race over three separate tasks: each loop iteration streams any
/// newly appended answer text, then checks cancellation, then the timeout deadline,
/// before sleeping one poll interval — the three exits (terminal reached naturally /
/// timeout / cancellation) are therefore mutually exclusive and checked in the same
/// bounded loop, with identical responsiveness (one <see cref="PollInterval"/>, 50 ms) to
/// every other blocking command in this feature.
/// </summary>
public sealed class QueryCommand : AsyncCommand<QuerySettings>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly QueryRunCoordinator _coordinator;
    private readonly CliStatusRenderer _status;
    private readonly TextWriter _stdout;
    private readonly TimeProvider _timeProvider;

    public QueryCommand(QueryRunCoordinator coordinator)
        : this(coordinator, new CliStatusRenderer(), Console.Out, TimeProvider.System)
    {
    }

    /// <summary>
    /// Test seam: inject a status renderer / stdout writer / time provider instead of the
    /// real stderr/stdout streams and the system clock (so a short <c>--timeout</c> can be
    /// driven deterministically without a real wall-clock wait).
    /// </summary>
    public QueryCommand(QueryRunCoordinator coordinator, CliStatusRenderer status, TextWriter stdout, TimeProvider timeProvider)
    {
        _coordinator = coordinator;
        _status = status;
        _stdout = stdout;
        _timeProvider = timeProvider;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, QuerySettings settings, CancellationToken cancellationToken)
    {
        var prompt = settings.Prompt!.Trim();
        var conversationId = settings.ConversationId ?? GenerateConversationId();

        var result = await _coordinator.SubmitTurnAsync(conversationId, prompt, cancellationToken);

        switch (result)
        {
            case QuerySubmissionResult.ConcurrencyLimitReached:
                _stdout.WriteLine("The Hub is at its query concurrency limit; try again later.");
                return (int)CliExitCode.StateConflict;

            case QuerySubmissionResult.ConversationAlreadyActive:
                _stdout.WriteLine($"Conversation {conversationId} already has an active turn.");
                return (int)CliExitCode.StateConflict;

            case QuerySubmissionResult.RecordUnreadable recordUnreadable:
                _stdout.WriteLine($"Conversation record for {conversationId} is unreadable: {recordUnreadable.Reason}");
                return (int)CliExitCode.OperationFailed;

            case QuerySubmissionResult.Accepted accepted:
                return await SuperviseToTerminalAsync(accepted.Turn, conversationId, settings.Timeout, cancellationToken);

            default:
                throw new InvalidOperationException($"Unhandled {nameof(QuerySubmissionResult)}: {result.GetType()}.");
        }
    }

    private async Task<int> SuperviseToTerminalAsync(
        QueryTurnState turn, string conversationId, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var turnId = turn.TurnId;
        var deadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(timeoutSeconds);
        var streamedLength = 0;

        while (!turn.IsTerminal)
        {
            streamedLength = StreamAnswerDelta(turn, streamedLength);

            if (cancellationToken.IsCancellationRequested)
            {
                // No agent work may continue unsupervised after the CLI exits
                // (FR-015/FR-016): the interrupt is awaited to completion — including its
                // partial-answer record append — before this method returns.
                await _coordinator.InterruptAsync(turnId, CancellationToken.None);
                _stdout.WriteLine($"Cancelled: query turn {turnId} interrupted.");
                return (int)CliExitCode.Cancelled;
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                await _coordinator.InterruptAsync(turnId, CancellationToken.None);
                _stdout.WriteLine(
                    $"Timed out after {timeoutSeconds}s waiting for query turn {turnId}; " +
                    "the turn was interrupted and its partial answer persisted.");
                return (int)CliExitCode.WaitTimeout;
            }

            await Task.Delay(PollInterval, CancellationToken.None);
        }

        // The turn reached its terminal state naturally between two polls: flush any
        // answer text appended in that final window before printing the result.
        StreamAnswerDelta(turn, streamedLength);

        if (turn.Status == QueryTurnStatus.Completed)
        {
            _stdout.WriteLine($"Query turn {turnId} in conversation {conversationId}: completed");
            _stdout.WriteLine(turn.Answer);
            return (int)CliExitCode.Success;
        }

        // Failed, or interrupted by some other actor (e.g. a concurrently running Hub's
        // own interrupt endpoint against the same data directory, ADR-020's no-global-guard
        // model) while this command was waiting — this command's own timeout/cancellation
        // paths above already return directly from within the loop, so any other terminal
        // state reaching here is reported the same way a genuine failure is
        // (contracts/cli-commands.md has no separate row for an externally triggered
        // interrupt).
        _stdout.WriteLine($"Query turn {turnId} failed: {turn.FailureReason ?? "the turn was interrupted."}");
        return (int)CliExitCode.OperationFailed;
    }

    /// <summary>Writes any answer text appended since <paramref name="alreadyStreamedLength"/> to stderr and returns the new streamed length.</summary>
    private int StreamAnswerDelta(QueryTurnState turn, int alreadyStreamedLength)
    {
        var answer = turn.Answer;
        if (answer.Length > alreadyStreamedLength)
        {
            _status.WriteLine(answer[alreadyStreamedLength..]);
            return answer.Length;
        }

        return alreadyStreamedLength;
    }

    /// <summary>ADR-014-conformant id (contracts/cli-commands.md "query"): <c>{yyyy-MM-dd}-conv-{guid}</c>, truncated to 40 chars.</summary>
    private string GenerateConversationId()
    {
        var candidate = $"{_timeProvider.GetUtcNow():yyyy-MM-dd}-conv-{Guid.NewGuid():N}";
        return candidate.Length > 40 ? candidate[..40] : candidate;
    }
}

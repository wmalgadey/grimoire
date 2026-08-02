using Grimoire.Hub.OperationalState;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// Remediation Action Task states (015-lint-board-parity data-model.md, ADR-018
/// normative). Terminal: <see cref="Completed"/>, <see cref="Failed"/>,
/// <see cref="NotApplicable"/>, <see cref="Dismissed"/>.
/// </summary>
public enum RemediationTaskState
{
    Proposed,
    Authorized,
    Executing,
    Completed,
    Failed,
    NotApplicable,
    Dismissed,
}

/// <summary>Wire/persistence names for <see cref="RemediationTaskState"/> (lowercase snake per contracts/remediation-task-api.md and the SQLite row).</summary>
public static class RemediationTaskStates
{
    public const string Proposed = "proposed";
    public const string Authorized = "authorized";
    public const string Executing = "executing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string NotApplicable = "not_applicable";
    public const string Dismissed = "dismissed";

    public static string ToWireFormat(this RemediationTaskState state) => state switch
    {
        RemediationTaskState.Proposed => Proposed,
        RemediationTaskState.Authorized => Authorized,
        RemediationTaskState.Executing => Executing,
        RemediationTaskState.Completed => Completed,
        RemediationTaskState.Failed => Failed,
        RemediationTaskState.NotApplicable => NotApplicable,
        RemediationTaskState.Dismissed => Dismissed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown remediation task state."),
    };

    public static bool TryParse(string wireState, out RemediationTaskState state)
    {
        switch (wireState)
        {
            case Proposed: state = RemediationTaskState.Proposed; return true;
            case Authorized: state = RemediationTaskState.Authorized; return true;
            case Executing: state = RemediationTaskState.Executing; return true;
            case Completed: state = RemediationTaskState.Completed; return true;
            case Failed: state = RemediationTaskState.Failed; return true;
            case NotApplicable: state = RemediationTaskState.NotApplicable; return true;
            case Dismissed: state = RemediationTaskState.Dismissed; return true;
            default: state = default; return false;
        }
    }
}

/// <summary>
/// One agent-proposed fix arising from a lint run's findings assessment (FR-007),
/// data-model.md "RemediationActionTask". <see cref="Title"/>/<see cref="Description"/>/
/// <see cref="TargetPath"/> are agent-authored verbatim — never edited by the harness
/// (Principle V). The in-memory state machine mirrors the persisted row's CAS discipline
/// (<c>OperationalStateRepository.TryTransitionRemediationTaskAsync</c>): transitions
/// valid only along the ADR-018 edges, terminal transitions idempotent,
/// first-transition-wins — the same shape as <c>LintRunState.TryTransitionTo</c>.
/// The persisted row remains the cross-process arbiter for races (ADR-018); this type
/// carries the edge validity and reason invariants.
/// </summary>
public sealed class RemediationActionTask
{
    /// <summary>Materialization (creation is not a transition — data-model.md): a task always starts <c>Proposed</c>.</summary>
    public RemediationActionTask(
        string taskId, string runId, string title, string description, string? targetPath, DateTimeOffset proposedAt)
    {
        TaskId = taskId;
        RunId = runId;
        Title = title;
        Description = description;
        TargetPath = targetPath;
        ProposedAt = proposedAt;
        State = RemediationTaskState.Proposed;
        UpdatedAt = proposedAt;
    }

    public string TaskId { get; }
    public string RunId { get; }
    public string Title { get; }
    public string Description { get; }
    public string? TargetPath { get; }
    public DateTimeOffset ProposedAt { get; }
    public RemediationTaskState State { get; private set; }

    /// <summary>Set on <c>Proposed → Authorized</c>, cleared on withdrawal; defines FIFO execution order (FR-017).</summary>
    public DateTimeOffset? AuthorizedAt { get; private set; }

    /// <summary>Mandatory for <see cref="RemediationTaskState.Failed"/>/<see cref="RemediationTaskState.NotApplicable"/> (FR-005/FR-018/SC-007); null otherwise.</summary>
    public string? OutcomeReason { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => State is RemediationTaskState.Completed
        or RemediationTaskState.Failed
        or RemediationTaskState.NotApplicable
        or RemediationTaskState.Dismissed;

    /// <summary>
    /// Attempts one transition along the ADR-018 edges. Returns <c>false</c> for every
    /// off-edge attempt — including any transition out of a terminal state, so terminal
    /// transitions are idempotent, first-transition-wins. Throws
    /// <see cref="ArgumentException"/> when <paramref name="outcomeReason"/> violates the
    /// reason invariant (missing for <c>Failed</c>/<c>NotApplicable</c>, present for any
    /// other target) — an invariant breach in the caller, not a lost race.
    /// </summary>
    public bool TryTransitionTo(RemediationTaskState toState, DateTimeOffset at, string? outcomeReason = null)
    {
        var requiresReason = toState is RemediationTaskState.Failed or RemediationTaskState.NotApplicable;
        if (requiresReason && string.IsNullOrWhiteSpace(outcomeReason))
        {
            throw new ArgumentException(
                $"outcome_reason is mandatory for the '{toState.ToWireFormat()}' state (FR-005/FR-018/SC-007).",
                nameof(outcomeReason));
        }

        if (!requiresReason && outcomeReason is not null)
        {
            throw new ArgumentException(
                $"outcome_reason must be null for the '{toState.ToWireFormat()}' state.",
                nameof(outcomeReason));
        }

        var isValidEdge = (State, toState) switch
        {
            (RemediationTaskState.Proposed, RemediationTaskState.Authorized) => true,   // authorize (FR-009)
            (RemediationTaskState.Proposed, RemediationTaskState.Dismissed) => true,    // dismiss (FR-010)
            (RemediationTaskState.Authorized, RemediationTaskState.Proposed) => true,   // withdraw (FR-016)
            (RemediationTaskState.Authorized, RemediationTaskState.Executing) => true,  // dispatch (FR-008/SC-005)
            (RemediationTaskState.Executing, RemediationTaskState.Completed) => true,   // agent terminal
            (RemediationTaskState.Executing, RemediationTaskState.Failed) => true,      // agent/liveness/spawn failure
            (RemediationTaskState.Executing, RemediationTaskState.NotApplicable) => true, // re-verification judged moot (FR-018)
            _ => false,
        };

        if (!isValidEdge)
        {
            return false;
        }

        switch (toState)
        {
            case RemediationTaskState.Authorized:
                AuthorizedAt = at;
                break;
            case RemediationTaskState.Proposed:
                // Withdrawal: re-authorizing later gets a fresh queue position (FR-016).
                AuthorizedAt = null;
                break;
        }

        State = toState;
        OutcomeReason = outcomeReason;
        UpdatedAt = at;
        return true;
    }

    /// <summary>Rehydrates the entity from its persisted row (ADR-003; the row is the state authority across restarts).</summary>
    public static RemediationActionTask FromRow(RemediationTaskRow row)
    {
        if (!RemediationTaskStates.TryParse(row.State, out var state))
        {
            throw new ArgumentException($"Unknown persisted remediation task state '{row.State}'.", nameof(row));
        }

        return new RemediationActionTask(row.TaskId, row.RunId, row.Title, row.Description, row.TargetPath, row.ProposedAt)
        {
            State = state,
            AuthorizedAt = row.AuthorizedAt,
            OutcomeReason = row.OutcomeReason,
            UpdatedAt = row.UpdatedAt,
        };
    }

    /// <summary>The persisted-row projection of the current in-memory state.</summary>
    public RemediationTaskRow ToRow() => new(
        TaskId, RunId, Title, Description, TargetPath,
        State.ToWireFormat(), ProposedAt, AuthorizedAt, OutcomeReason, UpdatedAt);
}

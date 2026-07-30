namespace Grimoire.Hub.LintDispatch;

/// <summary>Lint Run terminal outcome (data-model.md "Lint Run"). Terminal-only, mirrors <c>QueryTurnStatus</c>'s shape.</summary>
public enum LintRunStatus
{
    Running,
    Completed,
    Failed,
}

/// <summary>
/// In-memory view of one Lint Run (data-model.md "Lint Run"): trigger time, instruction
/// identity/hash, outcome state and reason, denied actions. Not itself a durable file —
/// its terminal facts are folded into the Findings Report's own bookkeeping block, so
/// there is exactly one artifact per run (data-model.md "Lint Run" note).
/// </summary>
public sealed class LintRunState
{
    public LintRunState(string runId, DateTimeOffset triggeredAt)
    {
        RunId = runId;
        TriggeredAt = triggeredAt;
        Status = LintRunStatus.Running;
    }

    public string RunId { get; }
    public DateTimeOffset TriggeredAt { get; }
    public LintRunStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FindingsReportPath { get; private set; }

    public bool IsTerminal => Status is LintRunStatus.Completed or LintRunStatus.Failed;

    /// <summary>Idempotent — only the first terminal transition wins (mirrors <c>QueryTurnState.TryTransitionTo</c>).</summary>
    public bool TryTransitionTo(LintRunStatus status, string? failureReason, DateTimeOffset completedAt)
    {
        if (IsTerminal)
        {
            return false;
        }

        Status = status;
        FailureReason = failureReason;
        CompletedAt = completedAt;
        return true;
    }

    public void SetFindingsReportPath(string path) => FindingsReportPath = path;
}

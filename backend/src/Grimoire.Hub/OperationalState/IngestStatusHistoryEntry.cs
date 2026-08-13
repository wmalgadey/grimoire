namespace Grimoire.Hub.OperationalState;

/// <summary>
/// One recorded ingest lifecycle transition (023-task-ui-improvements, data-model.md §1,
/// ADR-025). Append-only: rows are never updated or deleted, so a task's ordered entries
/// are the durable "path" the detail view renders — including entries a restart appends
/// after an earlier failure (FR-005/FR-013).
/// </summary>
/// <param name="Seq">Monotonic per <paramref name="TaskId"/>, starting at 1.</param>
/// <param name="Status">
/// One of the six board stages (<c>received</c>, <c>converting</c>, <c>queued</c>,
/// <c>running</c>, <c>completed</c>, <c>failed</c>) or one of the three history-only
/// statuses (<c>liveness_interrupted</c>, <c>reactivated</c>, <c>restarted</c>) that never
/// become a board column.
/// </param>
/// <param name="Detail">
/// Human-readable context: failure reason, attempt number for interruption/reactivation
/// entries, restart origin. Null when the transition needs no explanation.
/// </param>
public sealed record IngestStatusHistoryEntry(
    string TaskId,
    long Seq,
    string Status,
    DateTimeOffset EnteredAt,
    string? Detail);

/// <summary>
/// The history-only status values (data-model.md §1) — the three transitions that are
/// recorded and displayed but are deliberately NOT board columns (FR-007, clarification
/// 2026-08-13). Board stages keep their existing string values and live in the task
/// artifact's frontmatter, so they are not repeated here.
/// </summary>
public static class IngestHistoryStatuses
{
    public const string LivenessInterrupted = "liveness_interrupted";
    public const string Reactivated = "reactivated";
    public const string Restarted = "restarted";
}

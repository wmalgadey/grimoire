namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Read model for frontend board grouping and card rendering (data-model.md
/// IngestKanbanBoardProjection). Each Task Artifact appears exactly once, grouped by
/// <see cref="Column"/>.
/// </summary>
public sealed record IngestKanbanBoardProjection(
    string TaskId,
    string Column,
    string Title,
    string? Subtitle,
    DateTimeOffset UpdatedAt,
    string? FailureReason,
    string TaskLink);

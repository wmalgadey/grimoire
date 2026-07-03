namespace Grimoire.IngestAgent.TaskArtifact;

public sealed record TaskArtifactDocument(
    string TaskId,
    string Type,
    string Status,
    string Agent,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string SourceRef,
    IReadOnlyList<string> PagesTouched,
    string? FailureReason,
    string Narrative);

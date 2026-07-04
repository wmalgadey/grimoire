namespace Grimoire.IngestAgent.TaskArtifact;

public sealed record TaskArtifactDocument(
    string TaskId,
    string Operation,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string SourceRef,
    IReadOnlyList<string> CreatedPaths,
    IReadOnlyList<string> UpdatedPaths,
    IReadOnlyList<string> SupersededPaths,
    IReadOnlyList<DeniedActionRecord> DeniedActions,
    IReadOnlyList<string> UserQuestions,
    string Summary,
    string? FailureReason,
    InstructionContextRecord? InstructionContext = null);

public sealed record DeniedActionRecord(string Action, string TargetPath, string Reason);

public sealed record InstructionContextRecord(string ClaudePath, IReadOnlyList<string> SkillPaths, string ContentHash);

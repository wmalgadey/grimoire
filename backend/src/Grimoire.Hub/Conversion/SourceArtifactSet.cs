namespace Grimoire.Hub.Conversion;

/// <summary>
/// Persisted provenance + ingest input produced by the ingest-submission pipeline
/// (data-model.md SourceArtifactSet). Recorded independently of the Task Artifact so the
/// board/detail view keeps original/normalized references even after the Ingest agent
/// overwrites the task-artifact file with its own (agent-owned) fields.
/// </summary>
public sealed record SourceArtifactSet(
    string TaskId,
    string OriginalPath,
    string OriginalContentType,
    long OriginalSizeBytes,
    string NormalizedMarkdownPath,
    string NormalizedChecksum,
    DateTimeOffset CreatedAt);

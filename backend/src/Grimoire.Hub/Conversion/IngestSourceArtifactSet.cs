namespace Grimoire.Hub.Conversion;

/// <summary>
/// Persisted provenance + ingest input produced by the ingest-submission pipeline
/// (data-model.md IngestSourceArtifactSet). Recorded independently of the Task Artifact so the
/// board/detail view keeps original/normalized references even after the Ingest agent
/// overwrites the task-artifact file with its own (agent-owned) fields.
/// </summary>
/// <param name="Title">
/// 023-task-ui-improvements (data-model.md §3, FR-003): the first ATX <c>#</c> heading of
/// the normalized markdown, trimmed and capped — the human-readable label the board and
/// detail view show instead of the raw task id. Null when the source carries no heading,
/// which the read-time fallback chain covers. Deterministic string extraction over output
/// the pipeline already produced: display metadata, never wiki-content judgment
/// (Principle V).
/// </param>
/// <param name="OriginalFileName">
/// The as-uploaded filename for file submissions (null for URL submissions) — the first
/// fallback when no heading exists.
/// </param>
/// <param name="SourceUrl">
/// The submitted absolute URL for URL submissions (null for file submissions). The task
/// artifact's <c>source_ref</c> points at the normalized markdown, so the address the
/// operator actually submitted has nowhere else to live — and both the label fallback
/// chain (FR-003) and the detail view's source link (FR-001) need it.
/// </param>
public sealed record IngestSourceArtifactSet(
    string TaskId,
    string OriginalPath,
    string OriginalContentType,
    long OriginalSizeBytes,
    string NormalizedMarkdownPath,
    string NormalizedChecksum,
    DateTimeOffset CreatedAt,
    string? Title = null,
    string? OriginalFileName = null,
    string? SourceUrl = null);

/// <summary>
/// What the submission itself told us about its source, carried from the pipeline into the
/// manifest (023-task-ui-improvements). Exactly one of the two is set: a file submission has
/// a filename, a URL submission has a URL.
/// </summary>
public sealed record IngestSourceSubmissionMetadata(string? OriginalFileName = null, string? SourceUrl = null);

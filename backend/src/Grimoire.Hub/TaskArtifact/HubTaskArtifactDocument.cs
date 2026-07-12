namespace Grimoire.Hub.TaskArtifact;

/// <summary>
/// Hub-owned view of a Task Artifact during the pre-agent ingest-submission phase
/// (`received` / `converting` / `queued` / `failed`). Written to the same
/// `&lt;content-root&gt;/tasks/&lt;task_id&gt;.md` file the Ingest agent later takes over — same
/// record, same path, per data-model.md TaskArtifact. Deliberately independent of
/// `Grimoire.IngestAgent.TaskArtifact.TaskArtifactDocument` (T001: Hub must not depend on
/// Grimoire.IngestAgent), but writes the identical v2 frontmatter shape from
/// contracts/task-artifact-format.md so the file is a valid artifact from the first write.
/// </summary>
public sealed record HubTaskArtifactDocument(
    string TaskId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? SourceRef,
    string? OriginalRef,
    string? FailureReason,
    string Narrative,
    // 004: effective steering prompt + applied convert-step configuration (FR-009, FR-014)
    string? UserPromptSource = null,
    string? UserPrompt = null,
    IReadOnlyDictionary<string, bool>? ConvertSteps = null);

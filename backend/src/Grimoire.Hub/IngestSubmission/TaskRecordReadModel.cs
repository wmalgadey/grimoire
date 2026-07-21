using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>Parsed frontmatter presented to the detail view (data-model.md TaskRecord).</summary>
public sealed record TaskRecordMetadata(
    string Status,
    string? Agent,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? SourceRef,
    string? OriginalRef,
    string? FailureReason);

/// <summary>The per-task markdown record, metadata parsed and body with frontmatter stripped.</summary>
public sealed record TaskRecord(string TaskId, TaskRecordMetadata Metadata, string Body);

public enum TaskRecordOutcome
{
    Ok,
    Missing,
    Unparseable,
}

public sealed record TaskRecordResult(TaskRecordOutcome Outcome, TaskRecord? Record)
{
    public static readonly TaskRecordResult Missing = new(TaskRecordOutcome.Missing, null);
    public static readonly TaskRecordResult Unparseable = new(TaskRecordOutcome.Unparseable, null);
}

/// <summary>
/// Reads and parses the per-task markdown record for the detail view (FR-006/FR-007).
/// Resolves the file path exclusively via <see cref="ResolvedGrimoirePaths"/> (ADR-009 —
/// no path re-derivation). A missing file or a frontmatter parse failure yields an
/// "unavailable" result rather than an exception (contracts/task-record-api.md); backend
/// code performs no wiki-content judgment here (Principle V) — it only parses and strips
/// the existing frontmatter block.
/// </summary>
public sealed class TaskRecordReadModel
{
    private readonly ResolvedGrimoirePaths _paths;

    public TaskRecordReadModel(ResolvedGrimoirePaths paths)
    {
        _paths = paths;
    }

    public async Task<TaskRecordResult> ReadAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var path = _paths.TaskArtifactPathFor(taskId);
        if (!File.Exists(path))
        {
            return TaskRecordResult.Missing;
        }

        string markdown;
        try
        {
            markdown = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (IOException)
        {
            // Concurrent delete/rename between the existence check and the read: treat as
            // missing rather than surfacing a 5xx (contracts/task-record-api.md).
            return TaskRecordResult.Missing;
        }

        var frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
        if (frontmatter is null)
        {
            return TaskRecordResult.Unparseable;
        }

        var record = new TaskRecord(
            TaskId: frontmatter.TaskId,
            Metadata: new TaskRecordMetadata(
                Status: frontmatter.Status,
                Agent: frontmatter.Agent,
                StartedAt: frontmatter.StartedAt,
                CompletedAt: frontmatter.CompletedAt,
                SourceRef: frontmatter.SourceRef,
                OriginalRef: frontmatter.OriginalRef,
                FailureReason: frontmatter.FailureReason),
            Body: StripFrontmatter(markdown));

        return new TaskRecordResult(TaskRecordOutcome.Ok, record);
    }

    private static string StripFrontmatter(string markdown)
    {
        var sections = markdown.Split("---", 3, StringSplitOptions.None);
        return sections.Length < 3 ? markdown.Trim() : sections[2].TrimStart('\r', '\n').TrimEnd();
    }
}

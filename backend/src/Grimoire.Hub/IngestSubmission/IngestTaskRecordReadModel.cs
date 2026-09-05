using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>Parsed frontmatter presented to the detail view (data-model.md IngestTaskRecord).</summary>
public sealed record IngestTaskRecordMetadata(
    string Status,
    string? Agent,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? SourceRef,
    string? OriginalRef,
    string? FailureReason);

/// <summary>The per-task markdown record, metadata parsed and body with frontmatter stripped.</summary>
public sealed record IngestTaskRecord(string TaskId, IngestTaskRecordMetadata Metadata, string Body);

public enum IngestTaskRecordOutcome
{
    Ok,
    Missing,
    Unparseable,
}

public sealed record IngestTaskRecordResult(IngestTaskRecordOutcome Outcome, IngestTaskRecord? Record)
{
    public static readonly IngestTaskRecordResult Missing = new(IngestTaskRecordOutcome.Missing, null);
    public static readonly IngestTaskRecordResult Unparseable = new(IngestTaskRecordOutcome.Unparseable, null);
}

/// <summary>
/// Reads and parses the per-task markdown record for the detail view (FR-006/FR-007).
/// Resolves the file path exclusively via <see cref="ResolvedGrimoirePaths"/> (ADR-009 —
/// no path re-derivation). A missing file or a frontmatter parse failure yields an
/// "unavailable" result rather than an exception (contracts/task-record-api.md); backend
/// code performs no wiki-content judgment here (Principle V) — it only parses and strips
/// the existing frontmatter block.
/// </summary>
public sealed class IngestTaskRecordReadModel
{
    private readonly ResolvedGrimoirePaths _paths;

    public IngestTaskRecordReadModel(ResolvedGrimoirePaths paths)
    {
        _paths = paths;
    }

    public async Task<IngestTaskRecordResult> ReadAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var path = _paths.TaskArtifactPathFor(taskId);
        if (!File.Exists(path))
        {
            return IngestTaskRecordResult.Missing;
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
            return IngestTaskRecordResult.Missing;
        }

        var frontmatter = IngestTaskArtifactFrontmatter.TryParse(markdown);
        if (frontmatter is null)
        {
            return IngestTaskRecordResult.Unparseable;
        }

        var record = new IngestTaskRecord(
            TaskId: frontmatter.TaskId,
            Metadata: new IngestTaskRecordMetadata(
                Status: frontmatter.Status,
                Agent: frontmatter.Agent,
                StartedAt: frontmatter.StartedAt,
                CompletedAt: frontmatter.CompletedAt,
                SourceRef: frontmatter.SourceRef,
                OriginalRef: frontmatter.OriginalRef,
                FailureReason: frontmatter.FailureReason),
            Body: StripFrontmatter(markdown));

        return new IngestTaskRecordResult(IngestTaskRecordOutcome.Ok, record);
    }

    private static string StripFrontmatter(string markdown)
    {
        var sections = markdown.Split("---", 3, StringSplitOptions.None);
        return sections.Length < 3 ? markdown.Trim() : sections[2].TrimStart('\r', '\n').TrimEnd();
    }
}

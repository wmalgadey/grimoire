namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Builds the <see cref="KanbanBoardProjection"/> read model from the Task Artifact files under
/// the content root's tasks directory (data-model.md KanbanBoardProjection, FR-007). Reads
/// whichever writer (Hub pre-agent stages or the Ingest agent's own writer) most recently wrote
/// each file — the board does not care which process owns the current stage, only what it is.
/// </summary>
public sealed class KanbanBoardProjectionStore
{
    public Task<IReadOnlyList<KanbanBoardProjection>> GetAllAsync(string tasksDir, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(tasksDir))
        {
            return Task.FromResult<IReadOnlyList<KanbanBoardProjection>>([]);
        }

        var projections = new List<KanbanBoardProjection>();
        foreach (var path in Directory.EnumerateFiles(tasksDir, "*.md"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var markdown = File.ReadAllText(path);
            var frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
            if (frontmatter is null)
            {
                continue;
            }

            projections.Add(ToProjection(frontmatter, File.GetLastWriteTimeUtc(path)));
        }

        return Task.FromResult<IReadOnlyList<KanbanBoardProjection>>(
            projections.OrderByDescending(p => p.UpdatedAt).ToList());
    }

    public async Task<KanbanBoardProjection?> GetByTaskIdAsync(string tasksDir, string taskId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(tasksDir, $"{taskId}.md");
        if (!File.Exists(path))
        {
            return null;
        }

        var markdown = await File.ReadAllTextAsync(path, cancellationToken);
        var frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
        return frontmatter is null ? null : ToProjection(frontmatter, File.GetLastWriteTimeUtc(path));
    }

    private static KanbanBoardProjection ToProjection(TaskArtifactFrontmatter frontmatter, DateTime lastWriteUtc)
    {
        var title = frontmatter.SourceRef is not null
            ? Path.GetFileName(frontmatter.SourceRef)
            : frontmatter.TaskId;
        var subtitle = frontmatter.OriginalRef is not null ? Path.GetFileName(frontmatter.OriginalRef) : null;

        return new KanbanBoardProjection(
            TaskId: frontmatter.TaskId,
            Column: frontmatter.Status,
            Title: title,
            Subtitle: subtitle,
            UpdatedAt: new DateTimeOffset(lastWriteUtc, TimeSpan.Zero),
            FailureReason: frontmatter.FailureReason,
            TaskLink: $"/api/ingest-submissions/{frontmatter.TaskId}");
    }
}

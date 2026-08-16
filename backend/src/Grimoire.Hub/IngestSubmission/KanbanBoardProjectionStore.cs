using Grimoire.Hub.Conversion;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// Builds the <see cref="KanbanBoardProjection"/> read model from the Task Artifact files under
/// the content root's tasks directory (data-model.md KanbanBoardProjection, FR-007). Reads
/// whichever writer (Hub pre-agent stages or the Ingest agent's own writer) most recently wrote
/// each file — the board does not care which process owns the current stage, only what it is.
/// </summary>
public sealed class KanbanBoardProjectionStore
{
    private readonly SourceArtifactStore? _sourceArtifactStore;

    /// <param name="sourceArtifactStore">
    /// 023-task-ui-improvements T021 (FR-003): supplies the Hub-owned manifest the human-readable
    /// label comes from. Optional so board tests that only care about columns need not wire it;
    /// without it every row falls through the chain to its task id.
    /// </param>
    public KanbanBoardProjectionStore(SourceArtifactStore? sourceArtifactStore = null)
    {
        _sourceArtifactStore = sourceArtifactStore;
    }

    public async Task<IReadOnlyList<KanbanBoardProjection>> GetAllAsync(string tasksDir, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(tasksDir))
        {
            return [];
        }

        var projections = new List<KanbanBoardProjection>();
        foreach (var path in Directory.EnumerateFiles(tasksDir, "*.md"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var markdown = await File.ReadAllTextAsync(path, cancellationToken);
            var frontmatter = TaskArtifactFrontmatter.TryParse(markdown);
            if (frontmatter is null)
            {
                continue;
            }

            projections.Add(await ToProjectionAsync(frontmatter, File.GetLastWriteTimeUtc(path), cancellationToken));
        }

        return projections.OrderByDescending(p => p.UpdatedAt).ToList();
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
        return frontmatter is null
            ? null
            : await ToProjectionAsync(frontmatter, File.GetLastWriteTimeUtc(path), cancellationToken);
    }

    private async Task<KanbanBoardProjection> ToProjectionAsync(
        TaskArtifactFrontmatter frontmatter, DateTime lastWriteUtc, CancellationToken cancellationToken)
    {
        var manifest = _sourceArtifactStore is null
            ? null
            : await _sourceArtifactStore.TryReadMetadataAsync(frontmatter.TaskId, cancellationToken);
        var subtitle = frontmatter.OriginalRef is not null ? Path.GetFileName(frontmatter.OriginalRef) : null;

        return new KanbanBoardProjection(
            TaskId: frontmatter.TaskId,
            Column: frontmatter.Status,
            Title: ResolveTitle(frontmatter.TaskId, manifest),
            Subtitle: subtitle,
            UpdatedAt: new DateTimeOffset(lastWriteUtc, TimeSpan.Zero),
            FailureReason: frontmatter.FailureReason,
            TaskLink: $"/api/ingest-submissions/{frontmatter.TaskId}");
    }

    /// <summary>
    /// The label fallback chain (023 data-model.md §3, FR-003): extracted content title →
    /// uploaded filename → submitted URL → task id. Applied at read time in one place, so
    /// board rows and the detail response cannot disagree about what a task is called. The
    /// result is never null or empty — the task id is always available as the last resort,
    /// which is what a task whose conversion failed before writing a manifest falls back to.
    /// </summary>
    internal static string ResolveTitle(string taskId, SourceArtifactSet? manifest) =>
        Coalesce(manifest?.Title, manifest?.OriginalFileName, manifest?.SourceUrl) ?? taskId;

    private static string? Coalesce(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
}

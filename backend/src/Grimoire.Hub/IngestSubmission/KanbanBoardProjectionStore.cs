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
            Title: ResolveTitle(frontmatter.TaskId, manifest, frontmatter.Title),
            Subtitle: subtitle,
            UpdatedAt: new DateTimeOffset(lastWriteUtc, TimeSpan.Zero),
            FailureReason: frontmatter.FailureReason,
            TaskLink: $"/api/ingest-submissions/{frontmatter.TaskId}");
    }

    /// <summary>
    /// The label fallback chain (023 data-model.md §3, FR-003): extracted content title →
    /// uploaded filename → submitted URL → the label already written on the Task Artifact →
    /// task id. Applied at read time in one place, so board rows and the detail response
    /// cannot disagree about what a task is called. The result is never null or empty — the
    /// task id is always available as the last resort.
    ///
    /// <para>
    /// <paramref name="artifactTitle"/> is the tier that makes a freshly submitted task
    /// readable (#130). Every tier above it comes from the manifest, which is only written
    /// once conversion has produced markdown — so for the whole `received` + `converting`
    /// window, which for a URL covers an outbound fetch plus a markitdown run, the chain had
    /// nothing left but the task id, and a submission whose conversion failed never got a
    /// manifest at all and stayed id-labelled permanently. The Hub knows the filename or URL
    /// at acceptance and now writes it onto the artifact from the first `received` write, so
    /// this tier carries it until the extracted heading supersedes it.
    /// </para>
    ///
    /// <para>
    /// It sits *below* the manifest tiers deliberately: the extracted heading is the better
    /// label once it exists, and an artifact written before this change carries its task id
    /// as the title, which this ordering collapses back onto the same last resort instead of
    /// pinning it.
    /// </para>
    /// </summary>
    internal static string ResolveTitle(string taskId, SourceArtifactSet? manifest, string? artifactTitle = null) =>
        Coalesce(manifest?.Title, manifest?.OriginalFileName, manifest?.SourceUrl, artifactTitle) ?? taskId;

    private static string? Coalesce(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
}

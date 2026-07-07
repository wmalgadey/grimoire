namespace Grimoire.Hub.ContentRoot;

/// <summary>
/// Raw-storage locations for the ingest-submission pipeline (contracts/source-artifact-reference.md).
/// Lives at the repository root, sibling to the content root (`wiki/` by default) rather than
/// inside it — these are pre-agent intake artifacts, not wiki content.
/// </summary>
public sealed record RawStoragePaths(string OriginalsDir, string SourcesDir)
{
    public static RawStoragePaths Resolve(string repoRoot)
    {
        var rawRoot = Path.Combine(repoRoot, "raw");
        return new RawStoragePaths(
            OriginalsDir: Path.Combine(rawRoot, "originals"),
            SourcesDir: Path.Combine(rawRoot, "sources"));
    }

    public string OriginalPathFor(string taskId, string extension) =>
        Path.Combine(OriginalsDir, $"{taskId}{extension}");

    public string NormalizedMarkdownPathFor(string taskId) =>
        Path.Combine(SourcesDir, $"{taskId}.md");
}

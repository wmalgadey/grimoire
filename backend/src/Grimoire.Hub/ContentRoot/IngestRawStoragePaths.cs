using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.ContentRoot;

/// <summary>
/// Raw-storage locations for the ingest-submission pipeline (contracts/source-artifact-reference.md).
/// Lives under the consolidated data directory (ADR-009), sibling to the wiki content root
/// rather than inside it — these are pre-agent intake artifacts, not wiki content. A flat
/// projection of the single-composition-point <see cref="ResolvedGrimoirePaths"/>.
/// </summary>
public sealed record IngestRawStoragePaths(string OriginalsDir, string SourcesDir)
{
    public static IngestRawStoragePaths FromResolved(ResolvedGrimoirePaths resolved) =>
        new(OriginalsDir: resolved.RawOriginalsDir, SourcesDir: resolved.RawSourcesDir);

    public string OriginalPathFor(string taskId, string extension) =>
        Path.Combine(OriginalsDir, $"{taskId}{extension}");

    public string NormalizedMarkdownPathFor(string taskId) =>
        Path.Combine(SourcesDir, $"{taskId}.md");
}

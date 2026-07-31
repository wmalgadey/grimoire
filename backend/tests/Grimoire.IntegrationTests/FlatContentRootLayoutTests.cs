using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T018 (014-wiki-storage-restructure, US1/SC-001) — quickstart.md Scenario 1: against a
/// fresh content root, a scripted article-creation write lands directly at
/// <c>&lt;content-root&gt;/&lt;category&gt;/&lt;article&gt;.md</c>, with zero "pages/"
/// wrapper segments; a top-level listing of the content root shows only <c>index.md</c>,
/// <c>log.md</c>, and topical category folders. Exercises the real guarded-write path
/// (<see cref="GuardedToolExecutor"/> + <see cref="WriteJournal"/>) against a temp
/// directory — no LLM call (mirrors <c>PathConfiguration/ArtifactRelativePathsTests</c>'
/// idiom).
/// </summary>
public class FlatContentRootLayoutTests
{
    [Fact]
    public async Task ArticleCreationWrite_LandsDirectlyUnderCategoryFolder_WithNoWrapperSegment()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"grimoire-flat-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            // The content root's own baseline artifacts — created ahead of the scripted
            // write, matching a real content root's steady state (GrimoirePathResolver
            // auto-creates the content root directory itself; index.md/log.md are the
            // agent's own responsibility to seed on first write).
            var indexPath = Path.Combine(contentRoot, "index.md");
            var logPath = Path.Combine(contentRoot, "log.md");
            await File.WriteAllTextAsync(indexPath, "# Index\n");
            await File.WriteAllTextAsync(logPath, string.Empty);

            // 014-wiki-storage-restructure R3: articles live directly under the content
            // root — the write scope is the content root itself, no "pages/" prefix.
            var policy = new SafetyPolicy(
                contentRoot,
                readPrefixes: [contentRoot + Path.DirectorySeparatorChar],
                writePrefixes: [contentRoot + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, contentRoot);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = "concepts/new-article.md", content = "# New Article\n" }),
                turn: 1, CancellationToken.None);

            Assert.False(writeResult.IsError);

            var expectedArticlePath = Path.Combine(contentRoot, "concepts", "new-article.md");
            Assert.True(File.Exists(expectedArticlePath));

            // Zero wrapper segments between the content root and the category folder.
            var relativeSegments = Path.GetRelativePath(contentRoot, expectedArticlePath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert.Equal(["concepts", "new-article.md"], relativeSegments);
            Assert.DoesNotContain("pages", relativeSegments);

            // Top-level listing of the content root shows only index.md, log.md, and
            // topical category folders — no wrapper directory (quickstart.md Scenario 1).
            var topLevelEntries = Directory.GetFileSystemEntries(contentRoot)
                .Select(Path.GetFileName)
                .ToHashSet();
            Assert.Equal(new HashSet<string?> { "index.md", "log.md", "concepts" }, topLevelEntries);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }
}

using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T046 (022-align-wiki-structure, US2, SC-002): hermetic regression test proving the
/// reported and now-fixed defect stays fixed — a scripted ingest run writes its new article
/// to <c>&lt;category&gt;/&lt;slug&gt;.md</c> directly under the content root, with no
/// <c>pages/</c> wrapper segment, and the catalog line it appends to <c>index.md</c> links
/// to that exact content-root-relative path, which resolves to an existing file on disk.
/// Mirrors <see cref="IngestRunLifecycleTests"/>'s <see cref="AgentLoop"/> +
/// <see cref="FakeModelClient"/> + <see cref="GuardedToolExecutor"/> harness pattern
/// against a real temp filesystem, with <c>indexPath</c> wired so ADR-017's catalog-entry
/// shape guard (<c>^- \[.+\]\(.+\) — .+ — .+$</c>) is live for the assertion.
/// </summary>
public class ArticlePlacementTests
{
    private const string SourceContent = "# Test Source\n\nSome test content about a technology.";

    [Fact]
    public async Task IngestRun_WritesArticle_WithNoPagesWrapperSegment_AndCatalogLinkResolvesToIt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"article-placement-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var wikiDir = Path.Combine(tempRoot, "wiki");
            Directory.CreateDirectory(wikiDir);
            var indexPath = Path.Combine(wikiDir, "index.md");
            const string existingIndex = "# Wiki Index\n\n## Tech\n\n";
            await File.WriteAllTextAsync(indexPath, existingIndex);

            const string articleRelativePath = "tech/example-technology.md";
            const string articleContent =
                "---\ntype: Technology\ntitle: Example Technology\ndescription: A technology used for testing.\n" +
                "timestamp: 2026-07-14T00:00:00Z\ntags:\n  - tech/ExampleTech\nconfidence: medium\n" +
                "confidence_reason: One source.\n---\n\nBody text about the example technology.\n";
            const string catalogLine =
                "- [Example Technology](tech/example-technology.md) — A technology used for testing — Stub — keine Quellen\n";

            var turns = new[]
            {
                FakeModelClient.ReadFileTurn("t1", "wiki/index.md"),
                FakeModelClient.WriteFileTurn("t2", $"wiki/{articleRelativePath}", articleContent),
                FakeModelClient.WriteFileTurn("t3", "wiki/index.md", existingIndex + catalogLine),
                FakeModelClient.FinalTurn("Created Example Technology and updated the catalog."),
            };

            var fake = new FakeModelClient(turns);
            var policy = new SafetyPolicy(
                tempRoot,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writePrefixes: [wikiDir + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, tempRoot, taskId: "task-article-placement",
                writeLocksDir: Path.Combine(tempRoot, "write-locks"),
                indexPath: indexPath);
            var loop = new AgentLoop(fake, executor);

            var result = await loop.RunAsync(
                systemPrompt: "Test ingest agent.",
                userPrompt: "Integrate the source.",
                taskId: "task-article-placement",
                sourceRef: "test://source",
                sourceContent: SourceContent,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(4, result.TurnsUsed);
            Assert.Empty(executor.Denials);

            // The article lands at <content root>/<category>/<slug>.md — no wrapper segment.
            var articleAbsolutePath = Path.Combine(wikiDir, "tech", "example-technology.md");
            Assert.True(File.Exists(articleAbsolutePath));
            var onDiskArticle = await File.ReadAllTextAsync(articleAbsolutePath);
            Assert.Contains("Body text about the example technology.", onDiskArticle);

            // No path segment anywhere between the content root and the category is "pages".
            var relativeSegments = Path.GetRelativePath(wikiDir, articleAbsolutePath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert.Equal("tech", relativeSegments[0]);
            Assert.DoesNotContain(relativeSegments, segment => segment.Equals("pages", StringComparison.OrdinalIgnoreCase));

            // The catalog entry appended to index.md links to that exact content-root-relative
            // path, and that path resolves to the file that was actually created.
            var indexContent = await File.ReadAllTextAsync(indexPath);
            Assert.Contains(catalogLine.TrimEnd('\n'), indexContent);

            var linkMatch = System.Text.RegularExpressions.Regex.Match(
                indexContent, @"\[Example Technology\]\(([^)]+)\)");
            Assert.True(linkMatch.Success, "Expected a markdown link for the new article in index.md.");
            var linkedRelativePath = linkMatch.Groups[1].Value;
            Assert.Equal(articleRelativePath, linkedRelativePath);

            var resolvedFromLink = Path.Combine(wikiDir, linkedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(resolvedFromLink), $"Catalog link target '{resolvedFromLink}' must resolve to an existing file.");
            Assert.Equal(articleAbsolutePath, resolvedFromLink);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}

using Grimoire.IngestAgent.Source;
using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class WikiWriteContractTests
{
    [Fact]
    public async Task WritesWikiPage_And_DoesNotModifySource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var sourcePath = Path.Combine(root, "source.md");
        var sourceContent = "# Source\n\nThis is the source content.";
        await File.WriteAllTextAsync(sourcePath, sourceContent);

        var pagesDir = Path.Combine(root, "pages");
        Directory.CreateDirectory(pagesDir);

        var sourceReader = new SourceReader();
        var source = await sourceReader.ReadAsync("file", sourcePath, null, CancellationToken.None);

        var writer = new WikiPageWriter();
        var pagePath = await writer.WriteAsync(pagesDir, "source-summary", "# Summary\n\n" + source.Content, CancellationToken.None);

        var sourceAfter = await File.ReadAllTextAsync(sourcePath);

        Assert.Equal(sourceContent, sourceAfter);
        Assert.True(File.Exists(pagePath));
    }
}

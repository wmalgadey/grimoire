using Grimoire.IngestAgent.WikiIndex;
using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class WikiIndexCoverageTests
{
    [Fact]
    public async Task IndexWriter_Includes_AllTouchedPages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"index-coverage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var indexPath = Path.Combine(root, "index.md");

        var writer = new WikiIndexWriter();
        await writer.UpdateFromActionsAsync(indexPath,
        [
            new IndexUpdateEntry(WikiPageKind.Source, "Sources", "Source A", "pages/sources/source-a.md", "Summary A"),
            new IndexUpdateEntry(WikiPageKind.Entity, "Entities", "Entity Alpha", "pages/entities/entity-alpha.md", "Summary B"),
            new IndexUpdateEntry(WikiPageKind.Concept, "Concepts", "Concept Beta", "pages/concepts/concept-beta.md", "Summary C"),
        ], CancellationToken.None);

        var text = await File.ReadAllTextAsync(indexPath);
        Assert.Contains("[Source A](pages/sources/source-a.md)", text);
        Assert.Contains("[Entity Alpha](pages/entities/entity-alpha.md)", text);
        Assert.Contains("[Concept Beta](pages/concepts/concept-beta.md)", text);
    }
}

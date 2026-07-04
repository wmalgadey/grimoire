using Grimoire.IngestAgent.WikiIndex;
using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class WikiIndexIdempotencyTests
{
    [Fact]
    public async Task IndexWriter_IsIdempotent_ForSameEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"index-idempotency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var indexPath = Path.Combine(root, "index.md");

        var entries = new List<IndexUpdateEntry>
        {
            new(WikiPageKind.Source, "Sources", "Source A", "pages/sources/source-a.md", "Summary A"),
            new(WikiPageKind.Entity, "Entities", "Entity Alpha", "pages/entities/entity-alpha.md", "Summary B"),
        };

        var writer = new WikiIndexWriter();
        await writer.UpdateFromActionsAsync(indexPath, entries, CancellationToken.None);
        var first = await File.ReadAllTextAsync(indexPath);

        await writer.UpdateFromActionsAsync(indexPath, entries, CancellationToken.None);
        var second = await File.ReadAllTextAsync(indexPath);

        Assert.Equal(first, second);
    }
}

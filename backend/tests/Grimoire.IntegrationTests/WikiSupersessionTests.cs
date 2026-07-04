using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class WikiSupersessionTests
{
    [Fact]
    public void SupersessionService_WritesSupersededByField_IntoFrontmatter()
    {
        var original = "---\ntitle: \"Entity\"\n---\n\n# Entity\n";
        var service = new WikiSupersessionService();

        var updated = service.ApplySupersededBy(original, "pages/entities/entity-v2.md");

        Assert.Contains("superseded_by: \"pages/entities/entity-v2.md\"", updated);
    }
}

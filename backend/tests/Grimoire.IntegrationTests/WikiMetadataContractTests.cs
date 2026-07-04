using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IntegrationTests;

public class WikiMetadataContractTests
{
    [Fact]
    public void FrontmatterBuilder_AddsRequiredNonSourceMetadataFields()
    {
        var builder = new WikiFrontmatterBuilder();
        var content = builder.Build(
            WikiPageKind.Entity,
            "Entity Alpha",
            "# Entity Alpha\n\nBody",
            ["sources/source-a.md"]);

        Assert.Contains("tags:", content);
        Assert.Contains("confidence:", content);
        Assert.Contains("confidence_reason:", content);
        Assert.Contains("inbound_links:", content);
        Assert.Contains("last_reviewed:", content);
        Assert.DoesNotContain("superseded_by:", content);
    }
}

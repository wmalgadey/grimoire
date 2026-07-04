using Grimoire.Domain.Ingest;

namespace Grimoire.IntegrationTests;

public class WikiDeduplicationTests
{
    [Fact]
    public void DecisionService_UpdatesExistingPage_WhenSlugMatches()
    {
        var index = "## Sources\n- [Existing Source](pages/sources/existing-source.md): Summary\n";
        var service = new UpdateOrCreateDecisionService();

        var decision = service.Decide("Existing Source", index);

        Assert.Equal(PageDecisionAction.Update, decision.Action);
        Assert.Equal("pages/sources/existing-source.md", decision.TargetPagePath);
    }
}

using Grimoire.Domain.Ingest;

namespace Grimoire.Domain.UnitTests;

public class UpdateOrCreateDecisionTests
{
    [Fact]
    public void ChoosesUpdate_WhenIndexContainsSameTitle()
    {
        var service = new UpdateOrCreateDecisionService();
        var indexMarkdown = "## Databases\n- [SQLite in Grimoire](pages/sqlite-in-grimoire.md): embedded storage";

        var decision = service.Decide("SQLite in Grimoire", indexMarkdown);

        Assert.Equal(PageDecisionAction.Update, decision.Action);
        Assert.Equal("pages/sqlite-in-grimoire.md", decision.TargetPagePath);
    }

    [Fact]
    public void ChoosesCreate_WhenTitleNotPresent()
    {
        var service = new UpdateOrCreateDecisionService();
        var indexMarkdown = "## Runtime\n- [SignalR Intro](pages/signalr-intro.md): intro";

        var decision = service.Decide("OpenTelemetry Basics", indexMarkdown);

        Assert.Equal(PageDecisionAction.Create, decision.Action);
        Assert.Equal("opentelemetry-basics.md", decision.TargetPagePath);
    }
}

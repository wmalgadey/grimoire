using Grimoire.IngestAgent.WikiWrite;

namespace Grimoire.IngestAgent.WikiIndex;

public sealed class WikiCatalogClassifier
{
    public string Classify(WikiPageKind kind)
    {
        return kind switch
        {
            WikiPageKind.Source => "Sources",
            WikiPageKind.Entity => "Entities",
            WikiPageKind.Concept => "Concepts",
            _ => "General",
        };
    }
}

using System.Diagnostics;

namespace Grimoire.IngestAgent;

public static class IngestAgentTracing
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.IngestAgent", "1.0.0");
}

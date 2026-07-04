using System.Diagnostics;

namespace Grimoire.Hub;

public static class HubTracing
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.Hub", "1.0.0");
}

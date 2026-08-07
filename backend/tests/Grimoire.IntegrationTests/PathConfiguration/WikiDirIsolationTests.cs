using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-004 (US2 AS1/AS2) — every location the resolver derives from <c>WikiDir</c> stays
/// under it, and none of them leaks under <c>DataDir</c>, even when the two roots point at
/// entirely unrelated temp trees.
/// </summary>
public class WikiDirIsolationTests
{
    [Fact]
    public void WikiDerivedLocations_AllResolveUnderWikiDir_AndNoneUnderDataDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-wiki-isolation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            // DataDir and WikiDir are unrelated sibling trees under root, not nested —
            // a location leaking onto the wrong root would still surface even if the
            // trees happened to share a path prefix.
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance);

            foreach (var wikiDerived in new[]
            {
                resolved.IndexPath,
                resolved.LogPath,
                resolved.TasksDir,
                resolved.ConversationsDir,
                resolved.FindingsDir,
                resolved.RemediationTasksDir,
            })
            {
                Assert.StartsWith(resolved.WikiDir, wikiDerived, StringComparison.Ordinal);
                Assert.DoesNotContain(resolved.DataDir, wikiDerived, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-001 (022-memory-directory-root, US1) — every agent-bookkeeping location (tasks,
/// conversations, findings, remediation tasks) resolves under <c>MemoryDir</c>, and
/// <b>none</b> of them leak under <c>WikiDir</c> or <c>DataDir</c>, even when all three
/// roots point at entirely unrelated temp trees. The wiki root itself keeps only its
/// content locations (<c>index.md</c>/<c>log.md</c>) — it is no longer a parent of any
/// bookkeeping folder (ADR-024).
/// </summary>
public class WikiDirIsolationTests
{
    [Fact]
    public void BookkeepingLocations_AllResolveUnderMemoryDir_AndNoneUnderWikiDirOrDataDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-wiki-isolation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            // DataDir, WikiDir and MemoryDir are unrelated sibling trees under root, not
            // nested — a location leaking onto the wrong root would still surface even if
            // the trees happened to share a path prefix.
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance);

            foreach (var bookkeepingLocation in new[]
            {
                resolved.TasksDir,
                resolved.ConversationsDir,
                resolved.FindingsDir,
                resolved.RemediationTasksDir,
            })
            {
                Assert.StartsWith(resolved.MemoryDir, bookkeepingLocation, StringComparison.Ordinal);
                Assert.DoesNotContain(resolved.WikiDir, bookkeepingLocation, StringComparison.Ordinal);
                Assert.DoesNotContain(resolved.DataDir, bookkeepingLocation, StringComparison.Ordinal);
            }

            // The wiki root itself now holds only content locations.
            Assert.StartsWith(resolved.WikiDir, resolved.IndexPath, StringComparison.Ordinal);
            Assert.StartsWith(resolved.WikiDir, resolved.LogPath, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(resolved.WikiDir, "tasks")));
            Assert.False(Directory.Exists(Path.Combine(resolved.WikiDir, "conversations")));
            Assert.False(Directory.Exists(Path.Combine(resolved.WikiDir, "findings")));
            Assert.False(Directory.Exists(Path.Combine(resolved.WikiDir, "remediation-tasks")));
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

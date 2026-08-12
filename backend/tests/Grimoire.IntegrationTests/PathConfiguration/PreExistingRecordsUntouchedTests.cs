using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// FR-011/SC-007 (022-memory-directory-root): tasks, conversations, findings, and
/// remediation task records that already exist on disk under the wiki folder from before
/// this feature are not moved or migrated automatically — the hub simply resolves new
/// activity against the newly configured memory folder location. Relocating pre-existing
/// records is a manual operator step.
/// </summary>
public class PreExistingRecordsUntouchedTests
{
    [Fact]
    public void PreExistingBookkeepingUnderWikiDir_IsNeitherDetectedNorMoved_AfterResolve()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-preexisting-records-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);

            // Legacy on-disk layout: bookkeeping under the wiki directory, as it was
            // before ADR-024 re-anchored these folders at MemoryDir.
            var legacyTasksDir = Path.Combine(seeded.WikiDir, "tasks");
            var legacyConversationsDir = Path.Combine(seeded.WikiDir, "conversations");
            var legacyFindingsDir = Path.Combine(seeded.WikiDir, "findings");
            var legacyRemediationTasksDir = Path.Combine(seeded.WikiDir, "remediation-tasks");
            Directory.CreateDirectory(legacyTasksDir);
            Directory.CreateDirectory(legacyConversationsDir);
            Directory.CreateDirectory(legacyFindingsDir);
            Directory.CreateDirectory(legacyRemediationTasksDir);

            var legacyTaskPath = Path.Combine(legacyTasksDir, "2026-08-01-legacy-a1b2c3d4.md");
            var legacyConversationPath = Path.Combine(legacyConversationsDir, "legacy-conversation.md");
            var legacyFindingsPath = Path.Combine(legacyFindingsDir, "2026-08-01-lint-legacy.md");
            var legacyRemediationPath = Path.Combine(legacyRemediationTasksDir, "2026-08-01-remediation-legacy.md");
            File.WriteAllText(legacyTaskPath, "legacy task content\n");
            File.WriteAllText(legacyConversationPath, "legacy conversation content\n");
            File.WriteAllText(legacyFindingsPath, "legacy findings content\n");
            File.WriteAllText(legacyRemediationPath, "legacy remediation content\n");
            var legacyTaskBytes = File.ReadAllBytes(legacyTaskPath);
            var legacyConversationBytes = File.ReadAllBytes(legacyConversationPath);
            var legacyFindingsBytes = File.ReadAllBytes(legacyFindingsPath);
            var legacyRemediationBytes = File.ReadAllBytes(legacyRemediationPath);

            var configRoot = new ConfigurationBuilder().Build();
            var resolved = GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance);

            // Every legacy file is still at its original path, byte-identical.
            Assert.True(File.Exists(legacyTaskPath));
            Assert.True(File.Exists(legacyConversationPath));
            Assert.True(File.Exists(legacyFindingsPath));
            Assert.True(File.Exists(legacyRemediationPath));
            Assert.Equal(legacyTaskBytes, File.ReadAllBytes(legacyTaskPath));
            Assert.Equal(legacyConversationBytes, File.ReadAllBytes(legacyConversationPath));
            Assert.Equal(legacyFindingsBytes, File.ReadAllBytes(legacyFindingsPath));
            Assert.Equal(legacyRemediationBytes, File.ReadAllBytes(legacyRemediationPath));

            // The resolved memory root contains none of the legacy files — nothing was
            // detected or copied across.
            Assert.True(Directory.Exists(resolved.MemoryDir));
            var memoryTreeFiles = Directory.EnumerateFiles(resolved.MemoryDir, "*", SearchOption.AllDirectories).ToList();
            Assert.DoesNotContain(memoryTreeFiles, f => Path.GetFileName(f) == Path.GetFileName(legacyTaskPath));
            Assert.DoesNotContain(memoryTreeFiles, f => Path.GetFileName(f) == Path.GetFileName(legacyConversationPath));
            Assert.DoesNotContain(memoryTreeFiles, f => Path.GetFileName(f) == Path.GetFileName(legacyFindingsPath));
            Assert.DoesNotContain(memoryTreeFiles, f => Path.GetFileName(f) == Path.GetFileName(legacyRemediationPath));
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

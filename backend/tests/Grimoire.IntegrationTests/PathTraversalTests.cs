using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent.Guardrails;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

public class PathTraversalTests
{
    [Fact]
    public async Task ReadFile_Denies_TraversalAbsoluteAndSymlinkEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiPages = Path.Combine(root, "wiki", "pages");
            Directory.CreateDirectory(wikiPages);

            var outsideFile = Path.Combine(root, "outside-read.txt");
            await File.WriteAllTextAsync(outsideFile, "outside");

            var symlinkPath = Path.Combine(wikiPages, "outside-link.md");
            File.CreateSymbolicLink(symlinkPath, outsideFile);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var dotDotResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "../outside-read.txt" }),
                turn: 1,
                CancellationToken.None);

            var absoluteResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = outsideFile }),
                turn: 2,
                CancellationToken.None);

            var symlinkResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "wiki/pages/outside-link.md" }),
                turn: 3,
                CancellationToken.None);

            Assert.True(dotDotResult.IsError);
            Assert.True(absoluteResult.IsError);
            Assert.True(symlinkResult.IsError);

            Assert.Equal(3, executor.Denials.Count);
            Assert.All(executor.Denials, denial =>
                Assert.True(denial.Reason is "traversal" or "out_of_scope" or "no_rule"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_Denies_TraversalAbsoluteAndSymlinkEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiPages = Path.Combine(root, "wiki", "pages");
            Directory.CreateDirectory(wikiPages);

            var outsideFile = Path.Combine(root, "outside-write.txt");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            var symlinkPath = Path.Combine(wikiPages, "outside-link.md");
            File.CreateSymbolicLink(symlinkPath, outsideFile);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "pages") + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var dotDotResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "../outside-write.txt", content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            var absoluteResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = outsideFile, content = "hijack" }),
                turn: 2,
                CancellationToken.None);

            var symlinkResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/pages/outside-link.md", content = "hijack" }),
                turn: 3,
                CancellationToken.None);

            Assert.True(dotDotResult.IsError);
            Assert.True(absoluteResult.IsError);
            Assert.True(symlinkResult.IsError);

            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));
            Assert.Equal(3, executor.Denials.Count);
            Assert.Empty(journal.JournaledPaths);
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

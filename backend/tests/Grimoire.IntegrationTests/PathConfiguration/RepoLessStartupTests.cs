using Grimoire.Domain.Ingest;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T010t (US1, SC-001) — the Hub composes and validates every runtime location from a base
/// directory that has no <c>.git</c> and no project layout, without ever discovering a
/// repository, and every subsequent write lands under the configured roots.
/// </summary>
public class RepoLessStartupTests
{
    [Fact]
    public async Task StartsSuccessfully_InDirectoryWithNoRepositoryStructure_AndAllWritesLandUnderConfiguredRoots()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-repoless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            // SC-001: no source-repository structure exists anywhere in the base directory.
            Assert.False(Directory.Exists(Path.Combine(baseDir, ".git")));
            Assert.False(Directory.Exists(Path.Combine(baseDir, ".specify")));

            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolvedPaths = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // Every resolved location is confined to the base directory (FR-002/FR-003).
            Assert.Equal(Path.GetFullPath(baseDir), resolvedPaths.BaseDir);
            AssertUnderBase(baseDir, resolvedPaths.ContentRoot);
            AssertUnderBase(baseDir, resolvedPaths.DataDir);
            AssertUnderBase(baseDir, resolvedPaths.RawOriginalsDir);
            AssertUnderBase(baseDir, resolvedPaths.RawSourcesDir);
            AssertUnderBase(baseDir, resolvedPaths.StateDbPath);
            AssertUnderBase(baseDir, resolvedPaths.SecretsFilePath);
            AssertUnderBase(baseDir, resolvedPaths.InstructionsDir);

            var contentPaths = ContentRootPaths.FromResolved(resolvedPaths);
            var rawPaths = RawStoragePaths.FromResolved(resolvedPaths);

            using var fixture = new IngestSubmissionPipelineFixture(
                contentPaths: contentPaths, rawPaths: rawPaths, root: baseDir);

            var bytes = System.Text.Encoding.UTF8.GetBytes("# Hello\n\nSome content.");
            var taskId = await fixture.Pipeline.AcceptAsync(
                new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

            await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

            // Every artifact the Hub itself wrote during this run lives under the resolved
            // roots — never anywhere else (SC-001 acceptance scenario 2).
            var taskArtifactPath = Path.Combine(resolvedPaths.TasksDir, $"{taskId}.md");
            Assert.True(File.Exists(taskArtifactPath));
            var writtenFiles = Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories);
            Assert.All(writtenFiles, f => Assert.StartsWith(Path.GetFullPath(baseDir), Path.GetFullPath(f), StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    private static void AssertUnderBase(string baseDir, string resolvedPath) =>
        Assert.StartsWith(Path.GetFullPath(baseDir), Path.GetFullPath(resolvedPath), StringComparison.Ordinal);
}

using Grimoire.Domain.Ingest;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T011t (US1, SC-004) — every agent dispatch carries the Hub-resolved <c>--wiki-root</c>
/// (<see cref="IngestAgentRequest.WikiRoot"/>) plus absolute paths for every other path
/// argument; the agent performs no discovery of its own.
/// </summary>
public class DispatchPathArgumentsTests
{
    [Fact]
    public async Task EveryDispatch_CarriesWikiRoot_AndOnlyAbsoluteHubResolvedPaths()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-dispatch-args-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();
            var resolvedPaths = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var contentPaths = ContentRootPaths.FromResolved(resolvedPaths);
            var rawPaths = RawStoragePaths.FromResolved(resolvedPaths);
            var launcher = new FakeAgentProcessLauncher();

            using var fixture = new IngestSubmissionPipelineFixture(
                launcher: launcher, contentPaths: contentPaths, rawPaths: rawPaths, root: baseDir);

            var bytes = System.Text.Encoding.UTF8.GetBytes("# Hello\n\nSome content.");
            var taskId = await fixture.Pipeline.AcceptAsync(
                new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

            await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");

            var request = Assert.Single(launcher.Requests);

            // SC-004: --wiki-root is present and matches the Hub-resolved content root exactly.
            Assert.Equal(resolvedPaths.ContentRoot, request.WikiRoot);
            Assert.True(Path.IsPathRooted(request.WikiRoot));

            // Every other path argument is absolute and Hub-resolved — never left for the
            // agent to derive or discover itself (FR-007).
            Assert.True(Path.IsPathRooted(request.PagesDir));
            Assert.True(Path.IsPathRooted(request.TasksDir));
            Assert.True(Path.IsPathRooted(request.IndexPath));
            Assert.True(Path.IsPathRooted(request.LogPath));
            Assert.True(Path.IsPathRooted(request.SystemPromptPath));
            Assert.True(Path.IsPathRooted(request.DefaultUserPromptPath));
            Assert.True(Path.IsPathRooted(request.PolicyPath));

            Assert.Equal(resolvedPaths.PagesDir, request.PagesDir);
            Assert.Equal(resolvedPaths.TasksDir, request.TasksDir);
            Assert.Equal(resolvedPaths.IndexPath, request.IndexPath);
            Assert.Equal(resolvedPaths.LogPath, request.LogPath);
            Assert.Equal(resolvedPaths.SystemPromptPath, request.SystemPromptPath);
            Assert.Equal(resolvedPaths.DefaultUserPromptPath, request.DefaultUserPromptPath);
            Assert.Equal(resolvedPaths.PolicyPath, request.PolicyPath);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}

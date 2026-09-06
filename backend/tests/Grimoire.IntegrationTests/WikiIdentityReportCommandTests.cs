using Grimoire.Hub.Cli;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T054-T055 (029-shared-foundation-prompt, US3, ADR-053, contracts/wiki-identity-cli.md):
/// <c>wiki-identity</c> with no action, against a real temp data root and the real
/// <c>ResolveEffectiveFoundationPrompt</c> resolution path — no test double beyond the
/// harness's direct-construction idiom (<see cref="WikiIdentityCommandTestHarness"/>).
/// </summary>
public class WikiIdentityReportCommandTests
{
    [Fact]
    public async Task DefaultDeployment_ReportsDefault_WithResolvedPathHashAndHeading()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-report-default-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            var (exitCode, stdout) = await WikiIdentityCommandTestHarness.RunReportAsync(paths);

            var expected = paths.ResolveEffectiveFoundationPrompt(paths.Ingest);

            Assert.Equal((int)CliExitCode.Success, exitCode);
            Assert.Equal("default", expected.Source);
            Assert.Contains("source: default", stdout, StringComparison.Ordinal);
            Assert.Contains($"resolved_path: {expected.Path}", stdout, StringComparison.Ordinal);
            Assert.Contains($"sha256: {expected.Sha256}", stdout, StringComparison.Ordinal);
            // TestResolvedGrimoirePathsFactory writes each agent's default foundation-prompt.md
            // as plain "test foundation" text with no markdown heading — the report line is
            // present but empty, which is itself the behavior worth pinning (no heading found).
            // Environment.NewLine, not a literal "\n": WikiIdentityCommand writes via
            // TextWriter.WriteLine, which terminates with the platform newline.
            Assert.Contains($"heading: {Environment.NewLine}", stdout, StringComparison.Ordinal);
            Assert.False(File.Exists(paths.InstanceFoundationPromptPath));
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
    public async Task SpecialisedInstance_ReportsInstance_DifferingFromTheDefaultInSourcePathHashAndHeading()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-report-instance-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);
        var draftedPath = Path.Combine(Path.GetTempPath(), $"wiki-identity-report-drafted-{Guid.NewGuid():N}.md");

        try
        {
            var (defaultExitCode, defaultStdout) = await WikiIdentityCommandTestHarness.RunReportAsync(paths);
            Assert.Equal((int)CliExitCode.Success, defaultExitCode);

            await File.WriteAllTextAsync(draftedPath, "# A Specialised Wiki\nDrafted content, verbatim.\n");
            var (setExitCode, _) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, fromFile: draftedPath);
            Assert.Equal((int)CliExitCode.Success, setExitCode);

            var (instanceExitCode, instanceStdout) = await WikiIdentityCommandTestHarness.RunReportAsync(paths);
            var expected = paths.ResolveEffectiveFoundationPrompt(paths.Ingest);

            Assert.Equal((int)CliExitCode.Success, instanceExitCode);
            Assert.Equal("instance", expected.Source);
            Assert.Contains("source: instance", instanceStdout, StringComparison.Ordinal);
            Assert.Contains($"resolved_path: {paths.InstanceFoundationPromptPath}", instanceStdout, StringComparison.Ordinal);
            Assert.Contains("heading: A Specialised Wiki", instanceStdout, StringComparison.Ordinal);

            // SC-007: the two reports differ in exactly the respect the instance document changed.
            Assert.NotEqual(defaultStdout, instanceStdout);
            Assert.Contains("source: default", defaultStdout, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(draftedPath))
            {
                File.Delete(draftedPath);
            }
        }
    }
}

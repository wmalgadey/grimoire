using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// FR-007 (clarification 2026-08-06) — the Findings Report storage directory resolves
/// correctly under the default layout (agent output, anchored at the wiki directory) and
/// under an explicit env-var override, mirroring <see cref="WriteLocksPathTests"/>'s
/// cases for <c>write_locks_dir</c> (single composition point, no ambient discovery). No
/// CLI switch exists for this sub-path (FR-015, rule R1).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class FindingsPathTests
{
    [Fact]
    public void ZeroFlags_ResolvesFindingsDir_BeneathWikiDir_AndAutoCreatesIt()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-findings-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "llm-wiki", "findings")), resolved.FindingsDir);
            Assert.True(Directory.Exists(resolved.FindingsDir));

            var location = resolved.Locations.Single(l => l.Name == "findings_dir");
            Assert.Equal("config-file", location.Source);
            Assert.Equal(PathLocationKind.WritableData, location.Kind);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
        }
    }

    [Fact]
    public void ExplicitWikiDirOverride_ResolvesFindingsDir_BeneathTheOverriddenWikiDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-findings-wikidir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "wiki-dir", "findings")), resolved.FindingsDir);
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
    public void EnvironmentVariableOverride_ForFindingsDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__FindingsDir";
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-findings-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

            Environment.SetEnvironmentVariable(envVarName, overrideDir);
            var configRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.FindingsDir);
            Assert.True(Directory.Exists(resolved.FindingsDir));
            var location = resolved.Locations.Single(l => l.Name == "findings_dir");
            Assert.Equal("environment", location.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            if (Directory.Exists(overrideDir))
            {
                Directory.Delete(overrideDir, recursive: true);
            }
        }
    }

    [Fact]
    public void FindingsReportPathFor_ComposesRunIdBeneathFindingsDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-findings-report-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var runId = "2026-07-30-lint-a1b2c3d4";
            Assert.Equal(
                Path.Combine(resolved.FindingsDir, $"{runId}.md"),
                resolved.FindingsReportPathFor(runId));
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

using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T014 (013-lint-agent, ADR-009/ADR-003) — the Findings Report storage directory
/// (<c>data/findings/</c>) resolves correctly under the default layout and under explicit
/// <c>--findings-dir</c>/env-var overrides, mirroring <see cref="WriteLocksPathTests"/>'s
/// cases for <c>write_locks_dir</c> (single composition point, no ambient discovery).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class FindingsPathTests
{
    [Fact]
    public void ZeroConfiguration_ResolvesFindingsDir_BeneathDataDir_AndAutoCreatesIt()
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

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "findings")), resolved.FindingsDir);
            Assert.True(Directory.Exists(resolved.FindingsDir));

            var location = resolved.Locations.Single(l => l.Name == "findings_dir");
            Assert.Equal("default", location.Source);
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
    public void ExplicitBaseOverride_ResolvesFindingsDir_BeneathTheOverriddenBase()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "data", "findings")), resolved.FindingsDir);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    [Fact]
    public void EnvironmentVariableOverride_ForFindingsDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__FindingsDir";
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

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
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
            if (Directory.Exists(overrideDir))
            {
                Directory.Delete(overrideDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CommandLineOverride_ForFindingsDir_WinsOverDefault_AndSourceReportsCommandLine()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-cli-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-cli-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            // Same switch mapping Program.cs would register for --findings-dir (ADR-009).
            var configRoot = new ConfigurationBuilder()
                .AddCommandLine(
                    ["--findings-dir", overrideDir],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["--findings-dir"] = "Grimoire:Paths:FindingsDir",
                    })
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.FindingsDir);
            var location = resolved.Locations.Single(l => l.Name == "findings_dir");
            Assert.Equal("command-line", location.Source);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
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
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-findings-report-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var runId = "2026-07-30-lint-a1b2c3d4";
            Assert.Equal(
                Path.Combine(resolved.FindingsDir, $"{runId}.md"),
                resolved.FindingsReportPathFor(runId));
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

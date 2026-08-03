using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T007 (012-query-synthesis-writes, ADR-015/ADR-009) — the cross-process
/// write-coordination lock directory (<c>data/write-locks/</c>) resolves correctly under
/// the default layout and under explicit <c>--write-locks-dir</c>/env-var overrides,
/// mirroring <see cref="QueryRuntimePathsTests"/>'s cases for <c>conversations_dir</c>
/// (single composition point, no ambient discovery).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class WriteLocksPathTests
{
    [Fact]
    public void ZeroConfiguration_ResolvesWriteLocksDir_BeneathDataDir_AndAutoCreatesIt()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "write-locks")), resolved.WriteLocksDir);
            Assert.True(Directory.Exists(resolved.WriteLocksDir));

            var location = resolved.Locations.Single(l => l.Name == "write_locks_dir");
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
    public void ExplicitBaseOverride_ResolvesWriteLocksDir_BeneathTheOverriddenBase()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "data", "write-locks")), resolved.WriteLocksDir);
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
    public void EnvironmentVariableOverride_ForWriteLocksDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__WriteLocksDir";
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            Environment.SetEnvironmentVariable(envVarName, overrideDir);
            var configRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.WriteLocksDir);
            Assert.True(Directory.Exists(resolved.WriteLocksDir));
            var location = resolved.Locations.Single(l => l.Name == "write_locks_dir");
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
    public void CommandLineOverride_ForWriteLocksDir_WinsOverDefault_AndSourceReportsCommandLine()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-cli-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-cli-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            // Same switch mapping Program.cs registers for --write-locks-dir (ADR-009).
            var configRoot = new ConfigurationBuilder()
                .AddCommandLine(
                    ["--write-locks-dir", overrideDir],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["--write-locks-dir"] = "Grimoire:Paths:WriteLocksDir",
                    })
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.WriteLocksDir);
            var location = resolved.Locations.Single(l => l.Name == "write_locks_dir");
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
}

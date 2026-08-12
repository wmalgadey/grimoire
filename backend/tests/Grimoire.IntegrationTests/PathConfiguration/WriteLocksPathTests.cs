using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// ADR-015 — the cross-process write-coordination lock directory resolves correctly
/// under the default layout (anchored at the data directory, unchanged by this feature)
/// and under an explicit env-var override, mirroring <see cref="QueryRuntimePathsTests"/>'s
/// cases for <c>conversations_dir</c> (single composition point, no ambient discovery).
/// No CLI switch exists for this sub-path (FR-015, rule R1).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class WriteLocksPathTests
{
    [Fact]
    public void ZeroFlags_ResolvesWriteLocksDir_BeneathDataDir_AndAutoCreatesIt()
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

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "write-locks")), resolved.WriteLocksDir);
            Assert.True(Directory.Exists(resolved.WriteLocksDir));

            var location = resolved.Locations.Single(l => l.Name == "write_locks_dir");
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
    public void ExplicitDataDirOverride_ResolvesWriteLocksDir_BeneathTheOverriddenDataDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-datadir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "data-dir", "write-locks")), resolved.WriteLocksDir);
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
    public void EnvironmentVariableOverride_ForWriteLocksDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__Data__WriteLocksDir";
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-write-locks-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

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
}

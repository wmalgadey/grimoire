using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T023 (008-query-agent, Phase 2 foundational) — the new Query runtime locations
/// (<c>agents/query/system-prompt.md</c>, <c>agents/query/policy.json</c>,
/// <c>data/query-runs/</c>) resolve correctly under the default layout and under
/// explicit <c>--base</c>/env-var overrides, mirroring DefaultLayoutTests/
/// PathPrecedenceTests for the Ingest paths (ADR-009: single composition point, no
/// ambient discovery).
/// </summary>
public class QueryRuntimePathsTests
{
    [Fact]
    public void ZeroConfiguration_ResolvesQueryInstructionsAndQueryRunsDir_BeneathDataDir()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-query-paths-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "agents", "query")), resolved.QueryInstructionsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "agents", "query", "system-prompt.md")), resolved.QuerySystemPromptPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "agents", "query", "policy.json")), resolved.QueryPolicyPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "query-runs")), resolved.QueryRunsDir);
            Assert.True(Directory.Exists(resolved.QueryRunsDir));

            // Query's instructions never nest inside Ingest's, and vice versa.
            Assert.NotEqual(resolved.InstructionsDir, resolved.QueryInstructionsDir);
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
    public void ExplicitBaseOverride_ResolvesQueryLocations_BeneathTheOverriddenBase()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-query-paths-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "data", "agents", "query")), resolved.QueryInstructionsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "data", "query-runs")), resolved.QueryRunsDir);
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
    public void EnvironmentVariableOverride_ForQueryRunsDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__QueryRunsDir";
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-query-paths-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-query-runs-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            Environment.SetEnvironmentVariable(envVarName, overrideDir);
            var configRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.QueryRunsDir);
            var location = resolved.Locations.Single(l => l.Name == "query_runs_dir");
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
}

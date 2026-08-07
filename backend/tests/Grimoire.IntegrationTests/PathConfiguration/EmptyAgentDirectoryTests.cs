using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-007/FR-013 (US1 AS2, US4 AS2) — an agent directory that is missing, present but
/// empty, or present but incomplete each fails startup naming the specific thing that is
/// wrong: the directory itself for the first two cases, the specific missing document or
/// worker DLL for the third.
/// </summary>
public class EmptyAgentDirectoryTests
{
    [Fact]
    public void MissingAgentDirectory_FailsNamingAgentDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-empty-agentdir-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            Directory.Delete(seeded.AgentDir, recursive: true);
            var configRoot = new ConfigurationBuilder().Build();

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal("agent_dir", exception.Location);
            Assert.Equal(seeded.AgentDir, exception.ResolvedPath);
            Assert.Contains("does not exist", exception.Reason, StringComparison.Ordinal);
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
    public void PresentButEmptyAgentDirectory_FailsNamingAgentDir_NotAnIndividualFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-empty-agentdir-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            Directory.Delete(seeded.AgentDir, recursive: true);
            Directory.CreateDirectory(seeded.AgentDir);
            var configRoot = new ConfigurationBuilder().Build();

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal("agent_dir", exception.Location);
            Assert.Contains("no agent runtime", exception.Reason, StringComparison.Ordinal);
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
    public void AgentDirectoryMissingOneAgentTypeSubfolder_FailsNamingThatSubfolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-empty-agentdir-subfolder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            Directory.Delete(seeded.LintDir, recursive: true);
            var configRoot = new ConfigurationBuilder().Build();

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal("lint_dir", exception.Location);
            Assert.Contains("does not exist", exception.Reason, StringComparison.Ordinal);
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
    public void AgentDirectoryMissingOneRequiredInstructionDocument_FailsNamingThatDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-empty-agentdir-doc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            File.Delete(seeded.QueryPolicyPath);
            var configRoot = new ConfigurationBuilder().Build();

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal("query_policy", exception.Location);
            Assert.Equal(seeded.QueryPolicyPath, exception.ResolvedPath);
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
    public void AgentDirectoryMissingOneWorkerDll_FailsNamingIt_AndTellsOperatorToBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-empty-agentdir-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(root);
            File.Delete(seeded.QueryAgentWorkerPath);
            var configRoot = new ConfigurationBuilder().Build();

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal("query_agent_worker", exception.Location);
            Assert.Contains("Grimoire.QueryAgent.dll not found in the agent directory.", exception.Reason, StringComparison.Ordinal);
            Assert.Contains("Build first: dotnet build backend/Grimoire.slnx", exception.Reason, StringComparison.Ordinal);
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

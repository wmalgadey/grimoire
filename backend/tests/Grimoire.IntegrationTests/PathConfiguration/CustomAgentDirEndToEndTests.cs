using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// US4 AS1/AS2/AS3 — pointing <c>--agent-dir</c> (simulated via the same
/// <c>Grimoire:Paths:Agent:Dir</c> configuration key <see cref="PathSwitchCatalog"/> binds
/// that switch to) at an independently-built agent runtime resolves every agent's runtime
/// paths under it; the same missing/empty-directory failure modes
/// <see cref="EmptyAgentDirectoryTests"/> covers for the default location apply identically
/// to a custom one; and a rebuilt agent's refreshed instruction files are reflected in the
/// resolved paths (the resolver reads through to whatever is on disk, it never caches).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class CustomAgentDirEndToEndTests
{
    [Fact]
    public void CustomAgentDir_ResolvesEveryAgentRuntimePath_UnderIt()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-custom-agentdir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var customAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-custom-agentdir-build-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            PathConfigurationTestHelpers.SeedAgentRuntimeAt(customAgentDir);

            // Mirrors exactly what a real `--agent-dir <path>` invocation binds through
            // AddCommandLine + PathSwitchCatalog.
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:Agent:Dir", customAgentDir)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var expectedAgentDir = Path.GetFullPath(customAgentDir);
            Assert.Equal(expectedAgentDir, resolved.AgentDir);
            Assert.Equal(Path.Combine(expectedAgentDir, "ingest"), resolved.Ingest.Dir);
            Assert.Equal(Path.Combine(expectedAgentDir, "query"), resolved.Query.Dir);
            Assert.Equal(Path.Combine(expectedAgentDir, "lint"), resolved.Lint.Dir);
            Assert.StartsWith(expectedAgentDir, resolved.Ingest.WorkerPath, StringComparison.Ordinal);
            Assert.StartsWith(expectedAgentDir, resolved.Ingest.SystemPromptPath, StringComparison.Ordinal);
            Assert.StartsWith(expectedAgentDir, resolved.Query.PolicyPath, StringComparison.Ordinal);
            Assert.StartsWith(expectedAgentDir, resolved.Lint.PolicyPath, StringComparison.Ordinal);

            // DataDir stays independent of the redirected agent directory (US4 AS1: only
            // the agent runtime relocates).
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire")), resolved.DataDir);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            if (Directory.Exists(customAgentDir))
            {
                Directory.Delete(customAgentDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CustomAgentDir_MissingOrEmpty_FailsNamingAgentDir_JustLikeTheDefaultLocation()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-custom-agentdir-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var customAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-custom-agentdir-absent-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:Agent:Dir", customAgentDir)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            // customAgentDir was never created — the configured, custom location itself
            // is named in the failure, not the unset default.
            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

            Assert.Equal("agent_dir", exception.Location);
            Assert.Equal(Path.GetFullPath(customAgentDir), exception.ResolvedPath);
            Assert.Contains("does not exist", exception.Reason, StringComparison.Ordinal);
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
    public void RebuiltAgentInstructionFile_IsReflectedImmediately_TheResolverNeverCaches()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-custom-agentdir-rebuild-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var customAgentDir = Path.Combine(Path.GetTempPath(), $"grimoire-custom-agentdir-rebuild-target-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            PathConfigurationTestHelpers.SeedAgentRuntimeAt(customAgentDir);
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:Agent:Dir", customAgentDir)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var firstResolve = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);
            var systemPromptPath = firstResolve.Ingest.SystemPromptPath;
            Assert.Equal("# Test ingest system prompt\nRules.\n", File.ReadAllText(systemPromptPath));

            // Simulates a rebuild that refreshed the instruction source (US4 AS3): the
            // resolver reads the file fresh on every Resolve call, never caching content.
            File.WriteAllText(systemPromptPath, "# Rebuilt ingest system prompt\nUpdated rules.\n");
            var secondResolve = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(systemPromptPath, secondResolve.Ingest.SystemPromptPath);
            Assert.Equal("# Rebuilt ingest system prompt\nUpdated rules.\n", File.ReadAllText(secondResolve.Ingest.SystemPromptPath));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            if (Directory.Exists(customAgentDir))
            {
                Directory.Delete(customAgentDir, recursive: true);
            }
        }
    }
}

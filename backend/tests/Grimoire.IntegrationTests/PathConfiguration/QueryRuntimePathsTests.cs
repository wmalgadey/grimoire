using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T023 (008-query-agent) / T005+T019 (011-query-conversations) — the Query runtime
/// locations (<c>agents/query/system-prompt.md</c>, <c>agents/query/policy.json</c>,
/// <c>conversations/</c>) resolve correctly under the default layout and under
/// explicit <c>--base</c>/<c>--conversations-dir</c>/env-var overrides, mirroring
/// DefaultLayoutTests/PathPrecedenceTests for the Ingest paths (ADR-009: single
/// composition point, no ambient discovery). The former <c>query-runs</c> location is
/// retired (ADR-014, SC-004) — its cases were rewritten to <c>conversations_dir</c>.
/// 014-wiki-storage-restructure moved <c>ConversationsDir</c>'s default anchor from the
/// data directory to the base directory (a sibling of the content root), so it no longer
/// nests beneath <c>data/</c> the way the Query instruction locations still do.
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class QueryRuntimePathsTests
{
    [Fact]
    public void ZeroConfiguration_ResolvesQueryInstructionsBeneathDataDir_AndConversationsDirBeneathBaseDirectory()
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

            // 011-query-conversations (T005, ADR-014/ADR-009), updated by
            // 014-wiki-storage-restructure: the Conversation Record location resolves as a
            // base-level sibling of the content root (not nested under data/) and is
            // auto-created as writable data.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "conversations")), resolved.ConversationsDir);
            Assert.True(Directory.Exists(resolved.ConversationsDir));
            var conversationsLocation = resolved.Locations.Single(l => l.Name == "conversations_dir");
            Assert.Equal("default", conversationsLocation.Source);
            Assert.Equal(PathLocationKind.WritableData, conversationsLocation.Kind);
            Assert.Equal(
                Path.Combine(resolved.ConversationsDir, "c-1.md"),
                resolved.ConversationRecordPathFor("c-1"));

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
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "conversations")), resolved.ConversationsDir);
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
    public void EnvironmentVariableOverride_ForConversationsDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__ConversationsDir";
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-conversations-paths-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-conversations-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            Environment.SetEnvironmentVariable(envVarName, overrideDir);
            var configRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.ConversationsDir);
            Assert.True(Directory.Exists(resolved.ConversationsDir));
            var location = resolved.Locations.Single(l => l.Name == "conversations_dir");
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
    public void CommandLineOverride_ForConversationsDir_WinsOverDefault_AndSourceReportsCommandLine()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-conversations-paths-cli-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-conversations-cli-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            // Same switch mapping Program.cs registers for --conversations-dir (ADR-009).
            var configRoot = new ConfigurationBuilder()
                .AddCommandLine(
                    ["--conversations-dir", overrideDir],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["--conversations-dir"] = "Grimoire:Paths:ConversationsDir",
                    })
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.ConversationsDir);
            var location = resolved.Locations.Single(l => l.Name == "conversations_dir");
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

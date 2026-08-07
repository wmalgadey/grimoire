using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// The Query runtime locations (<c>&lt;AgentDir&gt;/query/Instructions/system-prompt.md</c>,
/// <c>&lt;AgentDir&gt;/query/Instructions/policy.json</c>, <c>conversations/</c>) resolve
/// correctly under the default layout and under explicit overrides (ADR-022: single
/// composition point, no ambient discovery). <c>ConversationsDir</c> anchors at the wiki
/// directory (FR-007, clarification 2026-08-06: agent output), not the data directory.
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class QueryRuntimePathsTests
{
    [Fact]
    public void ZeroFlags_ResolvesQueryInstructionsBeneathAgentDir_AndConversationsDirBeneathWikiDir()
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

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents", "query", "Instructions")), resolved.Query.InstructionsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents", "query", "Instructions", "system-prompt.md")), resolved.Query.SystemPromptPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents", "query", "Instructions", "policy.json")), resolved.Query.PolicyPath);

            // Agent output — the Conversation Record location resolves as a wiki-directory
            // sibling (not nested under .grimoire/) and is auto-created as writable data.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "llm-wiki", "conversations")), resolved.ConversationsDir);
            Assert.True(Directory.Exists(resolved.ConversationsDir));
            var conversationsLocation = resolved.Locations.Single(l => l.Name == "conversations_dir");
            Assert.Equal("config-file", conversationsLocation.Source);
            Assert.Equal(PathLocationKind.WritableData, conversationsLocation.Kind);
            Assert.Equal(
                Path.Combine(resolved.ConversationsDir, "c-1.md"),
                resolved.ConversationRecordPathFor("c-1"));

            // Query's instructions never nest inside Ingest's, and vice versa.
            Assert.NotEqual(resolved.Ingest.InstructionsDir, resolved.Query.InstructionsDir);
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
    public void ExplicitAgentDirOverride_ResolvesQueryLocations_BeneathTheOverriddenAgentDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-query-paths-agentdir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "agent-dir", "query", "Instructions")), resolved.Query.InstructionsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "wiki-dir", "conversations")), resolved.ConversationsDir);
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
    public void EnvironmentVariableOverride_ForConversationsDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__ConversationsDir";
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-conversations-paths-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-conversations-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

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

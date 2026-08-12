using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// ADR-022, amended by ADR-024 (US3/SC-005) — with the four roots supplied only by the
/// configuration-file tier (simulating the shipped <c>appsettings.json</c>), the wiki
/// resolves to <c>&lt;cwd&gt;/llm-wiki</c>, the memory root resolves to
/// <c>&lt;cwd&gt;/memory</c> as a sibling of the other three defaults (FR-009), and every
/// internal data location beneath <c>&lt;cwd&gt;/.grimoire</c>; overriding a single
/// sub-path leaves every other default intact (US5 acceptance scenario 1).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class DefaultLayoutTests
{
    [Fact]
    public void ZeroFlags_ResolvesWikiDirAndDataLocations_BeneathProcessWorkingDirectory()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-default-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);

            // getcwd() resolves symlinks (macOS temp dirs: /var/folders → /private/var/
            // folders) and the resolver derives its roots from the process CWD, so the
            // expectations must be built from the same canonical form.
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "llm-wiki")), resolved.WikiDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire")), resolved.DataDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents")), resolved.AgentDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "memory")), resolved.MemoryDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "raw", "originals")), resolved.RawOriginalsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "raw", "sources")), resolved.RawSourcesDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "state", "operational-state.db")), resolved.StateDbPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".env")), resolved.SecretsFilePath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents", "ingest")), resolved.Ingest.Dir);

            // The four roots never nest inside one another (research R1/plan.md, ADR-024):
            // the wiki can be its own independent git repository, and relocating one never
            // drags another (US2 AS1-AS3).
            Assert.DoesNotContain(resolved.DataDir, resolved.WikiDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, resolved.DataDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.MemoryDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, resolved.MemoryDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.MemoryDir, resolved.WikiDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.MemoryDir, resolved.DataDir, StringComparison.Ordinal);

            // There is no third "code default" source tier any more (ADR-022): every
            // location traces to the config-file tier when no CLI/env override applies.
            foreach (var location in resolved.Locations)
            {
                Assert.Equal("config-file", location.Source);
            }
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
    public void OverridingOneSubPath_LeavesEveryOtherDefaultIntact()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-default-layout-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var externalStateDb = Path.Combine(Path.GetTempPath(), $"grimoire-external-state-{Guid.NewGuid():N}", "operational-state.db");

        try
        {
            Directory.SetCurrentDirectory(cwd);

            // Same canonicalization as ZeroFlags_… above (macOS /var symlink).
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);

            // Route the override through configuration (not a direct field assignment) so
            // PathLocation.Source correctly attributes it, exactly as HubHostComposition's
            // own Bind() call would (US5: internal sub-path customization via the config
            // file only — no CLI switch exists for StateDb, FR-015).
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:Data:StateDb", externalStateDb)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // The overridden location took the override...
            Assert.Equal(Path.GetFullPath(externalStateDb), resolved.StateDbPath);

            // ...while every other location still falls back to its documented default
            // beneath the (unconfigured) roots (US5 acceptance scenario 1).
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire")), resolved.DataDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "raw", "originals")), resolved.RawOriginalsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "agents")), resolved.AgentDir);

            var stateDbLocation = resolved.Locations.Single(l => l.Name == "state_db");
            Assert.Equal("config-file", resolved.Locations.Single(l => l.Name == "data_dir").Source);
            Assert.Equal("config-file", stateDbLocation.Source);
            Assert.NotEqual(Path.GetFullPath(Path.Combine(cwd, ".grimoire", "state", "operational-state.db")), resolved.StateDbPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            var externalStateDbDir = Path.GetDirectoryName(externalStateDb);
            if (externalStateDbDir is not null && Directory.Exists(externalStateDbDir))
            {
                Directory.Delete(externalStateDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Agent output (tasks/conversations) resolves as true siblings under the memory
    /// directory, not nested under <c>.grimoire/</c> or <c>llm-wiki/</c>
    /// (022-memory-directory-root FR-002: findings/remediation-tasks/conversations/tasks
    /// are agent output, re-anchored from the wiki directory to the memory directory).
    /// </summary>
    [Fact]
    public void ZeroFlags_ResolvesTasksAndConversationsDirs_AsMemoryDirSiblings()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-default-layout-siblings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "memory", "tasks")), resolved.TasksDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "memory", "conversations")), resolved.ConversationsDir);

            // Both are nested under the memory directory (not the wiki or data directory) —
            // the ADR-024 re-anchoring away from ADR-022's WikiDir placement.
            Assert.StartsWith(resolved.MemoryDir, resolved.TasksDir, StringComparison.Ordinal);
            Assert.StartsWith(resolved.MemoryDir, resolved.ConversationsDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.TasksDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.DataDir, resolved.ConversationsDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, resolved.TasksDir, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.WikiDir, resolved.ConversationsDir, StringComparison.Ordinal);

            Assert.True(Directory.Exists(resolved.TasksDir));
            Assert.True(Directory.Exists(resolved.ConversationsDir));

            Assert.Equal("config-file", resolved.Locations.Single(l => l.Name == "tasks_dir").Source);
            Assert.Equal("config-file", resolved.Locations.Single(l => l.Name == "conversations_dir").Source);
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

    /// <summary>
    /// <c>TasksDir</c> and <c>ConversationsDir</c> are independently overridable via
    /// <c>Grimoire:Paths:Memory:TasksDir</c>/<c>Grimoire:Paths:Memory:ConversationsDir</c>
    /// in the configuration file, each leaving the other at its own default (no CLI switch
    /// exists for either, ADR-024 rule M1).
    /// </summary>
    [Fact]
    public void OverridingTasksDirAndConversationsDir_AreIndependentlyConfigurable()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-default-layout-siblings-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var externalTasksDir = Path.Combine(Path.GetTempPath(), $"grimoire-external-tasks-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);

            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:Memory:TasksDir", externalTasksDir)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // TasksDir took the override...
            Assert.Equal(Path.GetFullPath(externalTasksDir), resolved.TasksDir);

            // ...while ConversationsDir stays at its own default beneath the memory directory.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "memory", "conversations")), resolved.ConversationsDir);

            var tasksDirLocation = resolved.Locations.Single(l => l.Name == "tasks_dir");
            Assert.Equal(Path.GetFullPath(externalTasksDir), tasksDirLocation.ResolvedPath);
            Assert.Equal("config-file", resolved.Locations.Single(l => l.Name == "conversations_dir").Source);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            if (Directory.Exists(externalTasksDir))
            {
                Directory.Delete(externalTasksDir, recursive: true);
            }
        }
    }
}

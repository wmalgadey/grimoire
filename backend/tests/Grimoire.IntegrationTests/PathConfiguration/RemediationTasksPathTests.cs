using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// ADR-018/FR-002 (022-memory-directory-root) — the Remediation Task Record storage
/// directory resolves correctly under the default layout (a sibling of <c>tasks/</c> and
/// <c>conversations/</c>, anchored at the memory directory as agent output, not the wiki
/// directory) and under an explicit env-var override, mirroring
/// <see cref="FindingsPathTests"/>'s cases (single composition point, no ambient
/// discovery). No CLI switch exists for this sub-path (ADR-024 rule M1) — only
/// <c>Grimoire:Paths:Memory:RemediationTasksDir</c> in the config file or its
/// environment-variable equivalent.
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class RemediationTasksPathTests
{
    [Fact]
    public void ZeroFlags_ResolvesRemediationTasksDir_BeneathMemoryDir_AndAutoCreatesIt()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // Sibling of tasks/ and conversations/, directly under the memory directory —
            // agent output, not the wiki or data directory.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "memory", "remediation-tasks")), resolved.RemediationTasksDir);
            Assert.Equal(Path.GetDirectoryName(resolved.TasksDir), Path.GetDirectoryName(resolved.RemediationTasksDir));
            Assert.Equal(Path.GetDirectoryName(resolved.ConversationsDir), Path.GetDirectoryName(resolved.RemediationTasksDir));
            Assert.True(Directory.Exists(resolved.RemediationTasksDir));

            var location = resolved.Locations.Single(l => l.Name == "remediation_tasks_dir");
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
    public void ExplicitMemoryDirOverride_ResolvesRemediationTasksDir_BeneathTheOverriddenMemoryDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-memorydir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "memory-dir", "remediation-tasks")), resolved.RemediationTasksDir);
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
    public void EnvironmentVariableOverride_ForRemediationTasksDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__Memory__RemediationTasksDir";
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);

            Environment.SetEnvironmentVariable(envVarName, overrideDir);
            var configRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.RemediationTasksDir);
            Assert.True(Directory.Exists(resolved.RemediationTasksDir));
            var location = resolved.Locations.Single(l => l.Name == "remediation_tasks_dir");
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
    public void RemediationTaskRecordPathFor_ComposesTaskIdBeneathRemediationTasksDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-record-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(root);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var taskId = "2026-08-01-remediation-a1b2c3d4";
            Assert.Equal(
                Path.Combine(resolved.RemediationTasksDir, $"{taskId}.md"),
                resolved.RemediationTaskRecordPathFor(taskId));
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

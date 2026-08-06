using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T003 (015-lint-board-parity, ADR-009/ADR-018) — the Remediation Task Record storage
/// directory resolves correctly under the default layout (a sibling of <c>tasks/</c> and
/// <c>conversations/</c>, anchored at the base directory per the ADR-009/014 layout) and
/// under explicit <c>--remediation-tasks-dir</c>/env-var overrides, mirroring
/// <see cref="FindingsPathTests"/>'s cases (single composition point, no ambient
/// discovery).
/// </summary>
[Collection("CurrentDirectoryMutation")]
public class RemediationTasksPathTests
{
    [Fact]
    public void ZeroConfiguration_ResolvesRemediationTasksDir_BeneathBaseDir_AndAutoCreatesIt()
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

            // Sibling of tasks/ and conversations/, directly under the base — not the data dir.
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "remediation-tasks")), resolved.RemediationTasksDir);
            Assert.Equal(Path.GetDirectoryName(resolved.TasksDir), Path.GetDirectoryName(resolved.RemediationTasksDir));
            Assert.Equal(Path.GetDirectoryName(resolved.ConversationsDir), Path.GetDirectoryName(resolved.RemediationTasksDir));
            Assert.True(Directory.Exists(resolved.RemediationTasksDir));

            var location = resolved.Locations.Single(l => l.Name == "remediation_tasks_dir");
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
    public void ExplicitBaseOverride_ResolvesRemediationTasksDir_BeneathTheOverriddenBase()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "remediation-tasks")), resolved.RemediationTasksDir);
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
    public void EnvironmentVariableOverride_ForRemediationTasksDir_WinsOverDefault_AndSourceReportsEnvironment()
    {
        const string envVarName = "Grimoire__Paths__RemediationTasksDir";
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-env-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        Environment.SetEnvironmentVariable(envVarName, null);
        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

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
    public void CommandLineOverride_ForRemediationTasksDir_WinsOverDefault_AndSourceReportsCommandLine()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-cli-{Guid.NewGuid():N}");
        var overrideDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-cli-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);

            // Same switch mapping Program.cs registers for --remediation-tasks-dir (ADR-009).
            var configRoot = new ConfigurationBuilder()
                .AddCommandLine(
                    ["--remediation-tasks-dir", overrideDir],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["--remediation-tasks-dir"] = "Grimoire:Paths:RemediationTasksDir",
                    })
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(overrideDir), resolved.RemediationTasksDir);
            var location = resolved.Locations.Single(l => l.Name == "remediation_tasks_dir");
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
    public void RemediationTaskRecordPathFor_ComposesTaskIdBeneathRemediationTasksDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-remtasks-record-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var taskId = "2026-08-01-remediation-a1b2c3d4";
            Assert.Equal(
                Path.Combine(resolved.RemediationTasksDir, $"{taskId}.md"),
                resolved.RemediationTaskRecordPathFor(taskId));
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

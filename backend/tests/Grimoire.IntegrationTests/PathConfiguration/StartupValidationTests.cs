using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T019t (US3, SC-002, FR-006) — a missing or wrong-kind required-input location fails
/// startup immediately, naming the logical location, the configured value, and the
/// resolved path; absent writable-data locations are instead created and reported
/// (US3 acceptance scenarios 1-2).
/// </summary>
public class StartupValidationTests
{
    [Fact]
    public void MissingSecretsFile_FailsBeforeServing_NamingLocationAndPaths()
    {
        RunFailureCase(seed => File.Delete(seed.SecretsFilePath), "secrets_file");
    }

    [Fact]
    public void SecretsFileIsADirectory_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            File.Delete(seed.SecretsFilePath);
            Directory.CreateDirectory(seed.SecretsFilePath);
        }, "secrets_file");
    }

    [Fact]
    public void MissingInstructionsDir_FailsBeforeServing_NamingLocationAndPaths()
    {
        RunFailureCase(seed => Directory.Delete(seed.InstructionsDir, recursive: true), "instructions_dir");
    }

    [Fact]
    public void InstructionsDirIsAFile_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            Directory.Delete(seed.InstructionsDir, recursive: true);
            File.WriteAllText(seed.InstructionsDir, "not a directory");
        }, "instructions_dir");
    }

    [Fact]
    public void MissingSystemPrompt_FailsNamingSystemPromptLocation()
    {
        RunFailureCase(seed => File.Delete(seed.SystemPromptPath), "system_prompt");
    }

    [Fact]
    public void MissingDefaultUserPrompt_FailsNamingDefaultUserPromptLocation()
    {
        RunFailureCase(seed => File.Delete(seed.DefaultUserPromptPath), "default_user_prompt");
    }

    [Fact]
    public void MissingPolicyFile_FailsNamingPolicyLocation()
    {
        RunFailureCase(seed => File.Delete(seed.PolicyPath), "policy");
    }

    [Fact]
    public void PolicyPathIsADirectory_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            File.Delete(seed.PolicyPath);
            Directory.CreateDirectory(seed.PolicyPath);
        }, "policy");
    }

    [Fact]
    public void MissingAgentWorker_FailsNamingAgentWorkerLocation()
    {
        RunFailureCase(seed => File.Delete(seed.AgentWorkerPath), "agent_worker");
    }

    [Fact]
    public void AgentWorkerIsADirectory_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            File.Delete(seed.AgentWorkerPath);
            Directory.CreateDirectory(seed.AgentWorkerPath);
        }, "agent_worker");
    }

    [Fact]
    public void ContentRootIsAFile_FailsCleanlyInsteadOfThrowingRawIOException()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-startup-wrongkind-writable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            // content_root is a writable-data location (auto-created) — but here it already
            // exists as a file, the exact FR-006 edge case: "A configured path points at a
            // file where a directory is expected ... startup validation fails with a message
            // naming the location." Must not surface as a raw System.IO.IOException.
            var contentRootPath = Path.Combine(baseDir, "wiki");
            File.WriteAllText(contentRootPath, "not a directory");

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

            Assert.Equal("content_root", exception.Location);
            Assert.Equal(contentRootPath, exception.ResolvedPath);
            Assert.Contains(exception.Location, exception.Message, StringComparison.Ordinal);
            Assert.Contains(exception.ResolvedPath, exception.Message, StringComparison.Ordinal);
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
    public void AbsentWritableDataLocations_AreCreated_AndSuccessReturnsResolvedPaths()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-startup-writable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            // None of the writable locations exist yet — only the required inputs seeded above do.
            var contentRoot = Path.Combine(baseDir, "wiki");
            Assert.False(Directory.Exists(contentRoot));
            Assert.False(Directory.Exists(Path.Combine(baseDir, "data", "raw")));
            Assert.False(Directory.Exists(Path.Combine(baseDir, "data", "state")));

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // US3 acceptance scenario 2: writable locations are created and the effective
            // (resolved) location is reported.
            Assert.True(Directory.Exists(resolved.ContentRoot));
            Assert.True(Directory.Exists(resolved.PagesDir));
            Assert.True(Directory.Exists(resolved.TasksDir));
            Assert.True(Directory.Exists(resolved.RawOriginalsDir));
            Assert.True(Directory.Exists(resolved.RawSourcesDir));
            Assert.True(Directory.Exists(Path.GetDirectoryName(resolved.StateDbPath)));

            // US3 acceptance scenario 3: every effective location is present in the report.
            var reportedNames = resolved.Locations.Select(l => l.Name).ToHashSet();
            Assert.Equal(
                new HashSet<string> { "base_dir", "data_dir", "content_root", "raw_dir", "state_db", "secrets_file", "instructions_dir", "agent_worker" },
                reportedNames);
            Assert.All(resolved.Locations, l => Assert.True(Path.IsPathRooted(l.ResolvedPath)));
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    private static void RunFailureCase(Action<SeededRequiredInputs> corrupt, string expectedLocation)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-startup-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            corrupt(seeded);

            var configRoot = new ConfigurationBuilder().Build();

            // US3 acceptance scenario 1: startup fails immediately (before serving any
            // request — resolution happens before Program.cs ever builds the host),
            // naming the offending location, its configured value, and its resolved path.
            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal(expectedLocation, exception.Location);
            Assert.True(Path.IsPathRooted(exception.ResolvedPath));
            Assert.Contains(exception.Location, exception.Message, StringComparison.Ordinal);
            Assert.Contains(exception.ResolvedPath, exception.Message, StringComparison.Ordinal);
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

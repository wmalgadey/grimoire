using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// SC-007, FR-013 — a missing or wrong-kind required-input location fails startup
/// immediately, naming the logical location, the configured value, and the resolved
/// path; absent writable-data locations are instead created and reported (US1 acceptance
/// scenarios).
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
        RunFailureCase(seed => Directory.Delete(Path.Combine(seed.IngestDir, "Instructions"), recursive: true), "ingest_instructions_dir");
    }

    [Fact]
    public void InstructionsDirIsAFile_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            var instructionsDir = Path.Combine(seed.IngestDir, "Instructions");
            Directory.Delete(instructionsDir, recursive: true);
            File.WriteAllText(instructionsDir, "not a directory");
        }, "ingest_instructions_dir");
    }

    [Fact]
    public void MissingSystemPrompt_FailsNamingSystemPromptLocation()
    {
        RunFailureCase(seed => File.Delete(seed.SystemPromptPath), "ingest_system_prompt");
    }

    [Fact]
    public void MissingDefaultUserPrompt_FailsNamingDefaultUserPromptLocation()
    {
        RunFailureCase(seed => File.Delete(seed.DefaultUserPromptPath), "ingest_default_user_prompt");
    }

    [Fact]
    public void MissingPolicyFile_FailsNamingPolicyLocation()
    {
        RunFailureCase(seed => File.Delete(seed.PolicyPath), "ingest_policy");
    }

    [Fact]
    public void PolicyPathIsADirectory_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            File.Delete(seed.PolicyPath);
            Directory.CreateDirectory(seed.PolicyPath);
        }, "ingest_policy");
    }

    [Fact]
    public void MissingAgentWorker_FailsNamingAgentWorkerLocation_AndTellsOperatorToBuild()
    {
        RunFailureCase(seed => File.Delete(seed.AgentWorkerPath), "ingest_agent_worker", exception =>
        {
            Assert.Contains("Build first: dotnet build backend/Grimoire.slnx", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AgentWorkerIsADirectory_FailsWithWrongKindReason()
    {
        RunFailureCase(seed =>
        {
            File.Delete(seed.AgentWorkerPath);
            Directory.CreateDirectory(seed.AgentWorkerPath);
        }, "ingest_agent_worker");
    }

    [Fact]
    public void WikiDirIsAFile_FailsCleanlyInsteadOfThrowingRawIOException()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-startup-wrongkind-writable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            // wiki_dir is a writable-data location (auto-created, and never pre-created by
            // the seeding helper) — but here it already exists as a file, the exact FR-010
            // edge case: "A configured path points at a file where a directory is expected
            // ... startup validation fails with a message naming the location." Must not
            // surface as a raw System.IO.IOException.
            File.WriteAllText(seeded.WikiDir, "not a directory");

            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal("wiki_dir", exception.Location);
            Assert.Equal(seeded.WikiDir, exception.ResolvedPath);
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
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            var options = seeded.Options;
            var configRoot = new ConfigurationBuilder().Build();

            // None of the writable locations exist yet — only the required inputs seeded above do.
            Assert.False(Directory.Exists(seeded.WikiDir));
            Assert.False(Directory.Exists(Path.Combine(seeded.DataDir, "raw")));
            Assert.False(Directory.Exists(Path.Combine(seeded.DataDir, "state")));

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // Writable locations are created and the effective (resolved) location is reported.
            Assert.True(Directory.Exists(resolved.WikiDir));
            Assert.True(Directory.Exists(resolved.TasksDir));
            Assert.True(Directory.Exists(resolved.RawOriginalsDir));
            Assert.True(Directory.Exists(resolved.RawSourcesDir));
            Assert.True(Directory.Exists(Path.GetDirectoryName(resolved.StateDbPath)));
            Assert.True(Directory.Exists(resolved.FindingsDir));

            // Every effective, independently-configured location is present in the report
            // (agent subfolders/instructions/workers are derived from agent_dir, not
            // independently configured, so they have no Locations entry of their own).
            var reportedNames = resolved.Locations.Select(l => l.Name).ToHashSet();
            Assert.Equal(
                new HashSet<string>
                {
                    "data_dir", "wiki_dir", "agent_dir", "raw_dir", "state_db", "write_locks_dir",
                    "tasks_dir", "conversations_dir", "findings_dir", "remediation_tasks_dir", "secrets_file",
                },
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

    /// <summary>
    /// SC-006: no configuration at all — an empty options instance, mirroring what a
    /// fully-empty <c>appsettings.json</c> (or a section missing all three roots) would
    /// bind to.
    /// </summary>
    [Fact]
    public void EmptyConfiguration_ThrowsConfigurationMissing_NamingAllThreeRoots_BeforeTouchingAnyDirectory()
    {
        // No temp root is created anywhere in this test — the options carry no path that
        // could be turned into a directory. A resolve call that created anything before
        // the mandatory-config gate would have nowhere valid to create it, and would
        // throw a different, unexpected exception instead of GrimoirePathConfigurationMissingException.
        var options = new GrimoirePathOptions();
        var configRoot = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<GrimoirePathConfigurationMissingException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

        Assert.Equal("appsettings.json", exception.ConfigurationFile);
        Assert.Contains("DataDir", exception.MissingKeys);
        Assert.Contains("WikiDir", exception.MissingKeys);
        Assert.Contains("AgentDir", exception.MissingKeys);
    }

    /// <summary>SC-006: a section missing exactly one of the three roots names only that one.</summary>
    [Fact]
    public void ConfigurationMissingOneRoot_ThrowsConfigurationMissing_NamingOnlyThatRoot()
    {
        var options = new GrimoirePathOptions { DataDir = "/tmp/data", WikiDir = "/tmp/wiki" };
        var configRoot = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<GrimoirePathConfigurationMissingException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

        Assert.Equal("appsettings.json", exception.ConfigurationFile);
        Assert.Equal(["AgentDir"], exception.MissingKeys);
    }

    /// <summary>SC-006: a whitespace-only root is treated as absent, not as a literal path value.</summary>
    [Fact]
    public void WhitespaceOnlyRoot_ThrowsConfigurationMissing_NamingIt()
    {
        var options = new GrimoirePathOptions { DataDir = "   ", WikiDir = "/tmp/wiki", AgentDir = "/tmp/agents" };
        var configRoot = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<GrimoirePathConfigurationMissingException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

        Assert.Equal(["DataDir"], exception.MissingKeys);
    }

    private static void RunFailureCase(Action<SeededRequiredInputs> corrupt, string expectedLocation, Action<GrimoirePathValidationException>? assertAdditional = null)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-startup-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            corrupt(seeded);

            var configRoot = new ConfigurationBuilder().Build();

            // Startup fails immediately (before serving any request — resolution happens
            // before Program.cs ever builds the host), naming the offending location, its
            // configured value, and its resolved path.
            var exception = Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            Assert.Equal(expectedLocation, exception.Location);
            Assert.True(Path.IsPathRooted(exception.ResolvedPath));
            Assert.Contains(exception.Location, exception.Message, StringComparison.Ordinal);
            Assert.Contains(exception.ResolvedPath, exception.Message, StringComparison.Ordinal);
            assertAdditional?.Invoke(exception);
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

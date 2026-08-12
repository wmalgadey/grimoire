using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// MANDATORY — Constitution IV logging contract: deterministic coverage for every row in
/// plan.md ## Observability &gt; Structured Log Events: <c>paths_resolved</c>,
/// <c>paths_location_created</c>, <c>paths_validation_failed</c>, and
/// <c>paths_configuration_missing</c> (ADR-022), driven through the real
/// <see cref="GrimoirePathResolver"/> trigger paths (not called in isolation), using the
/// same <c>CaptureLogger&lt;T&gt;</c> idiom as <c>IngestObservabilityLogTests</c> (ADR-005).
/// </summary>
public class PathLoggingContractTests
{
    [Fact]
    public void SuccessfulResolve_Emits_PathsResolved_WithAllMandatoryFields_AndSources()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-log-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();
            var logger = new CaptureLogger<PathLoggingContractTests>();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, logger);

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "paths_resolved"));
            Assert.Equal(LogLevel.Information, entry.Level);

            foreach (var field in new[]
            {
                "data_dir", "wiki_dir", "agent_dir", "memory_dir", "secrets_file", "state_db", "raw_dir", "sources",
            })
            {
                Assert.True(entry.Fields.ContainsKey(field), $"Missing mandatory field '{field}' on paths_resolved.");
            }

            Assert.Equal(resolved.DataDir, entry.Fields["data_dir"]?.ToString());
            Assert.Equal(resolved.WikiDir, entry.Fields["wiki_dir"]?.ToString());
            Assert.Equal(resolved.AgentDir, entry.Fields["agent_dir"]?.ToString());
            Assert.Equal(resolved.MemoryDir, entry.Fields["memory_dir"]?.ToString());
            Assert.Equal(resolved.SecretsFilePath, entry.Fields["secrets_file"]?.ToString());
            Assert.Equal(resolved.StateDbPath, entry.Fields["state_db"]?.ToString());
            Assert.Equal(resolved.RawOriginalsDir, entry.Fields["raw_dir"]?.ToString());
            Assert.Contains("memory_dir=", entry.Fields["sources"]?.ToString(), StringComparison.Ordinal);
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
    public void AutoCreatingAWritableLocation_Emits_PathsLocationCreated_WithLocationAndResolvedPath()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-log-contract-created-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();
            var logger = new CaptureLogger<PathLoggingContractTests>();

            // The wiki directory does not exist yet — this resolve call must auto-create
            // it and report the creation (FR-010, US1 acceptance scenario 2).
            var wikiDir = Path.Combine(baseDir, "wiki-dir");
            Assert.False(Directory.Exists(wikiDir));

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, logger);

            var entry = Assert.Single(logger.Entries.Where(
                e => e.EventName == "paths_location_created" && e.Fields["location"]?.ToString() == "wiki_dir"));
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.True(entry.Fields.ContainsKey("location"));
            Assert.True(entry.Fields.ContainsKey("resolved_path"));
            Assert.Equal(resolved.WikiDir, entry.Fields["resolved_path"]?.ToString());
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
    /// 022-memory-directory-root (FR-007/SC-005): a cold start with no <c>memory/</c> on
    /// disk auto-creates it and reports the creation with <c>location=memory_dir</c>.
    /// </summary>
    [Fact]
    public void AutoCreatingMemoryDir_Emits_PathsLocationCreated_WithMemoryDirLocation()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-log-contract-memory-created-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();
            var logger = new CaptureLogger<PathLoggingContractTests>();

            var memoryDir = Path.Combine(baseDir, "memory-dir");
            Assert.False(Directory.Exists(memoryDir));

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, logger);

            var entry = Assert.Single(logger.Entries.Where(
                e => e.EventName == "paths_location_created" && e.Fields["location"]?.ToString() == "memory_dir"));
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal(resolved.MemoryDir, entry.Fields["resolved_path"]?.ToString());
            Assert.True(Directory.Exists(resolved.MemoryDir));
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
    public void MissingRequiredInput_Emits_PathsValidationFailed_WithAllMandatoryFields()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-log-contract-failed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            File.Delete(seeded.SecretsFilePath);
            var configRoot = new ConfigurationBuilder().Build();
            var logger = new CaptureLogger<PathLoggingContractTests>();

            Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, logger));

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "paths_validation_failed"));
            Assert.Equal(LogLevel.Error, entry.Level);

            foreach (var field in new[] { "location", "configured_value", "resolved_path", "reason" })
            {
                Assert.True(entry.Fields.ContainsKey(field), $"Missing mandatory field '{field}' on paths_validation_failed.");
            }

            Assert.Equal("secrets_file", entry.Fields["location"]?.ToString());
            Assert.Equal(seeded.SecretsFilePath, entry.Fields["resolved_path"]?.ToString());
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
    /// FR-013/SC-007: an agent directory present but holding no agent runtime fails
    /// naming <c>agent_dir</c> itself (not an individual file inside it), with the
    /// distinct reason text (data-model.md §5).
    /// </summary>
    [Fact]
    public void EmptyAgentDirectory_Emits_PathsValidationFailed_NamingAgentDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-log-contract-empty-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            Directory.Delete(seeded.AgentDir, recursive: true);
            Directory.CreateDirectory(seeded.AgentDir);
            var configRoot = new ConfigurationBuilder().Build();
            var logger = new CaptureLogger<PathLoggingContractTests>();

            Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, logger));

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "paths_validation_failed"));
            Assert.Equal("agent_dir", entry.Fields["location"]?.ToString());
            Assert.Contains("no agent runtime", entry.Fields["reason"]?.ToString(), StringComparison.Ordinal);
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
    /// FR-005/SC-006 (ADR-022): a root absent from every configuration tier emits the new
    /// <c>paths_configuration_missing</c> ERROR event naming the configuration file and
    /// every missing key, before any location is touched.
    /// </summary>
    [Fact]
    public void MissingRoot_Emits_PathsConfigurationMissing_NamingConfigurationFileAndFullKeyPaths()
    {
        var options = new GrimoirePathOptions
        {
            Data = new DataPathOptions { Dir = "  " },
            Wiki = new WikiPathOptions { Dir = "llm-wiki" },
        };
        var configRoot = new ConfigurationBuilder().Build();
        var logger = new CaptureLogger<PathLoggingContractTests>();

        var ex = Assert.Throws<GrimoirePathConfigurationMissingException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, logger));

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "paths_configuration_missing"));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.True(entry.Fields.ContainsKey("configuration_file"));
        Assert.True(entry.Fields.ContainsKey("missing_keys"));
        Assert.Equal("appsettings.json", entry.Fields["configuration_file"]?.ToString());
        Assert.Contains("Grimoire:Paths:Data:Dir", entry.Fields["missing_keys"]?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Grimoire:Paths:Agent:Dir", entry.Fields["missing_keys"]?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Grimoire:Paths:Memory:Dir", entry.Fields["missing_keys"]?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Grimoire:Paths:Data:Dir", ex.MissingKeys);
        Assert.Contains("Grimoire:Paths:Agent:Dir", ex.MissingKeys);
        Assert.Contains("Grimoire:Paths:Memory:Dir", ex.MissingKeys);
        Assert.DoesNotContain("Grimoire:Paths:Wiki:Dir", ex.MissingKeys);
    }
}

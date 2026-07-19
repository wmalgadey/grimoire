using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T020 (MANDATORY — Constitution IV logging contract) — deterministic coverage for every
/// row in plan.md ## Observability &gt; Structured Log Events: <c>paths_resolved</c>,
/// <c>paths_location_created</c>, and <c>paths_validation_failed</c>, driven through the
/// real <see cref="GrimoirePathResolver"/> trigger paths (not called in isolation), using
/// the same <c>CaptureLogger&lt;T&gt;</c> idiom as <c>ObservabilityLogTests</c> (ADR-005).
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
                "base_dir", "data_dir", "content_root", "raw_dir", "state_db",
                "secrets_file", "instructions_dir", "agent_worker", "sources",
            })
            {
                Assert.True(entry.Fields.ContainsKey(field), $"Missing mandatory field '{field}' on paths_resolved.");
            }

            Assert.Equal(resolved.BaseDir, entry.Fields["base_dir"]?.ToString());
            Assert.Equal(resolved.DataDir, entry.Fields["data_dir"]?.ToString());
            Assert.Equal(resolved.ContentRoot, entry.Fields["content_root"]?.ToString());
            Assert.Equal(resolved.RawOriginalsDir, entry.Fields["raw_dir"]?.ToString());
            Assert.Equal(resolved.StateDbPath, entry.Fields["state_db"]?.ToString());
            Assert.Equal(resolved.SecretsFilePath, entry.Fields["secrets_file"]?.ToString());
            Assert.Equal(resolved.InstructionsDir, entry.Fields["instructions_dir"]?.ToString());
            Assert.Equal(resolved.AgentWorkerPath, entry.Fields["agent_worker"]?.ToString());
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

            // The content root does not exist yet — this resolve call must auto-create it
            // and report the creation (FR-006, US3 acceptance scenario 2).
            var contentRoot = Path.Combine(baseDir, "wiki");
            Assert.False(Directory.Exists(contentRoot));

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, logger);

            var entry = Assert.Single(logger.Entries.Where(
                e => e.EventName == "paths_location_created" && e.Fields["location"]?.ToString() == "content_root"));
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.True(entry.Fields.ContainsKey("location"));
            Assert.True(entry.Fields.ContainsKey("resolved_path"));
            Assert.Equal(resolved.ContentRoot, entry.Fields["resolved_path"]?.ToString());
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
}

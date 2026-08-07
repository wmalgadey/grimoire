using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// MANDATORY — Constitution IV/plan.md ## Observability > Business Metrics: deterministic
/// coverage for <c>grimoire.hub.path_resolution_failures_total</c> (ADR-022) — incremented
/// with the correct <c>reason</c> label on each of the three startup failure paths,
/// driven through the real <see cref="GrimoirePathResolver"/> trigger paths (not by
/// calling <see cref="HubMetrics"/> directly), same <c>MeterListener</c> idiom as
/// <c>QueryConversationMetricsTests</c>. Asserts <c>Assert.Contains</c> rather than
/// <c>Assert.Single</c> throughout — the listener is process-wide and this counter, unlike
/// most others in this codebase, is incremented by many other tests' own resolver-failure
/// calls running concurrently in the same process (xUnit's default cross-class
/// parallelism), so only "this measurement occurred at least once" is a safe assertion.
/// </summary>
public class PathMetricsContractTests
{
    [Fact]
    public void MissingRoot_Increments_PathResolutionFailuresTotal_WithConfigurationMissingReason()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = ListenTo("grimoire.hub.path_resolution_failures_total", measurements);

        var options = new GrimoirePathOptions { DataDir = "  ", WikiDir = "llm-wiki" };
        var configRoot = new ConfigurationBuilder().Build();

        Assert.Throws<GrimoirePathConfigurationMissingException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Value == 1L && TagValue(m, "reason") == "configuration_missing");
        }
    }

    [Fact]
    public void MissingRequiredInput_Increments_PathResolutionFailuresTotal_WithLocationInvalidReason()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-metrics-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
            using var listener = ListenTo("grimoire.hub.path_resolution_failures_total", measurements);

            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            File.Delete(seeded.SecretsFilePath);
            var configRoot = new ConfigurationBuilder().Build();

            Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            lock (measurements)
            {
                Assert.Contains(measurements, m => m.Value == 1L && TagValue(m, "reason") == "location_invalid");
            }
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
    public void EmptyAgentDirectory_Increments_PathResolutionFailuresTotal_WithAgentDirectoryEmptyReason()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-metrics-emptyagent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
            using var listener = ListenTo("grimoire.hub.path_resolution_failures_total", measurements);

            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            Directory.Delete(seeded.AgentDir, recursive: true);
            Directory.CreateDirectory(seeded.AgentDir);
            var configRoot = new ConfigurationBuilder().Build();

            Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            lock (measurements)
            {
                Assert.Contains(measurements, m => m.Value == 1L && TagValue(m, "reason") == "agent_directory_empty");
            }
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    private static MeterListener ListenTo(string instrumentName, List<(long Value, KeyValuePair<string, object?>[] Tags)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            lock (measurements)
            {
                measurements.Add((value, tags.ToArray()));
            }
        });
        listener.Start();
        return listener;
    }

    private static string? TagValue((long Value, KeyValuePair<string, object?>[] Tags) m, string key)
        => m.Tags.FirstOrDefault(t => t.Key == key).Value?.ToString();
}

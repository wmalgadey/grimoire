using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// MANDATORY — Constitution IV trace contract: deterministic coverage for every row in
/// plan.md ## Observability &gt; Distributed Trace Spans: <c>paths_resolved</c> and
/// <c>paths_configuration_missing</c> (ADR-022) — both root-parented (no parent activity
/// exists at startup), tagged <c>signal_type=log</c>, carrying the same correlation
/// attributes as their log-event counterpart. Same in-memory <see cref="ActivityListener"/>
/// idiom as <c>QueryConversationLogEventTests</c>.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class PathTracingContractTests
{
    [Fact]
    public void SuccessfulResolve_StartsPathsResolvedSpan_RootParented_WithCorrelationAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-trace-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var options = PathConfigurationTestHelpers.SeedRequiredInputs(baseDir);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            var span = Assert.Single(activities.Where(a => a.OperationName == "paths_resolved"));
            Assert.Null(span.ParentId);
            Assert.Equal("log", span.Tags.Single(t => t.Key == "signal_type").Value);
            Assert.Equal("paths_resolved", span.Tags.Single(t => t.Key == "event_name").Value);
            Assert.Equal("Information", span.Tags.Single(t => t.Key == "level").Value);
            Assert.Equal(resolved.DataDir, span.Tags.Single(t => t.Key == "data_dir").Value);
            Assert.Equal(resolved.WikiDir, span.Tags.Single(t => t.Key == "wiki_dir").Value);
            Assert.Equal(resolved.AgentDir, span.Tags.Single(t => t.Key == "agent_dir").Value);
            Assert.Equal(resolved.MemoryDir, span.Tags.Single(t => t.Key == "memory_dir").Value);
            Assert.Equal(resolved.SecretsFilePath, span.Tags.Single(t => t.Key == "secrets_file").Value);
            Assert.Equal(resolved.StateDbPath, span.Tags.Single(t => t.Key == "state_db").Value);
            Assert.Equal(resolved.RawOriginalsDir, span.Tags.Single(t => t.Key == "raw_dir").Value);
            Assert.True(span.Tags.Any(t => t.Key == "sources"));
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
    public void MissingRoot_StartsPathsConfigurationMissingSpan_RootParented_WithCorrelationAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var options = new GrimoirePathOptions
        {
            Data = new DataPathOptions { Dir = "  " },
            Wiki = new WikiPathOptions { Dir = "llm-wiki" },
        };
        var configRoot = new ConfigurationBuilder().Build();

        Assert.Throws<GrimoirePathConfigurationMissingException>(
            () => GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance));

        var span = Assert.Single(activities.Where(a => a.OperationName == "paths_configuration_missing"));
        Assert.Null(span.ParentId);
        Assert.Equal("log", span.Tags.Single(t => t.Key == "signal_type").Value);
        Assert.Equal("paths_configuration_missing", span.Tags.Single(t => t.Key == "event_name").Value);
        Assert.Equal("Error", span.Tags.Single(t => t.Key == "level").Value);
        Assert.Equal("appsettings.json", span.Tags.Single(t => t.Key == "configuration_file").Value);
        Assert.Contains("Grimoire:Paths:Data:Dir", span.Tags.Single(t => t.Key == "missing_keys").Value);
        Assert.Contains("Grimoire:Paths:Agent:Dir", span.Tags.Single(t => t.Key == "missing_keys").Value);
        Assert.Contains("Grimoire:Paths:Memory:Dir", span.Tags.Single(t => t.Key == "missing_keys").Value);
    }

    [Fact]
    public void ValidationFailure_StartsPathsValidationFailedSpan_RootParented_WithCorrelationAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var baseDir = Path.Combine(Path.GetTempPath(), $"grimoire-trace-contract-failed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);

        try
        {
            var seeded = PathConfigurationTestHelpers.SeedRequiredInputsWithPaths(baseDir);
            File.Delete(seeded.SecretsFilePath);
            var configRoot = new ConfigurationBuilder().Build();

            Assert.Throws<GrimoirePathValidationException>(
                () => GrimoirePathResolver.Resolve(seeded.Options, configRoot, NullLogger.Instance));

            var span = Assert.Single(activities.Where(a => a.OperationName == "paths_validation_failed"));
            Assert.Null(span.ParentId);
            Assert.Equal("log", span.Tags.Single(t => t.Key == "signal_type").Value);
            Assert.Equal("paths_validation_failed", span.Tags.Single(t => t.Key == "event_name").Value);
            Assert.Equal("Error", span.Tags.Single(t => t.Key == "level").Value);
            Assert.Equal("secrets_file", span.Tags.Single(t => t.Key == "location").Value);
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

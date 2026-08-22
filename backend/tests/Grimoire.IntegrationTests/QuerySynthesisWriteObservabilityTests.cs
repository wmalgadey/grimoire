using System.Diagnostics.Metrics;
using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T029 (012-query-synthesis-writes, US1) — validates the
/// <c>wiki.query.synthesis_page_created</c> log event's name/level/mandatory fields and
/// the <c>wiki.query.synthesis_pages_created_total</c> counter's increment
/// (plan.md ## Observability) on a successful create-only write; confirms neither fires
/// for an <c>index.md</c>/<c>log.md</c> write (not a create-only target) or a denied
/// write.
///
/// <para>
/// #152 — <c>wiki.query.synthesis_pages_created_total</c> is emitted untagged
/// (<c>QueryAgentMetrics.RecordSynthesisPageCreated</c>), so the exactly-once and
/// never-fired assertions below read the whole process's measurement stream and any other
/// test creating a Synthesis Page concurrently would falsify them. With no tag to filter
/// on, serialization is the only available lever. See
/// <see cref="HubActivityListenerObservabilityCollection"/>.
/// </para>
/// </summary>
[Collection("HubActivityListenerObservability")]
public class QuerySynthesisWriteObservabilityTests
{
    [Fact]
    public async Task CreateOnlyWriteSucceeds_EmitsLogEventAndIncrementsCounter_ExactlyOnce()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.query.synthesis_pages_created_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            lock (measurements) { measurements.Add(value); }
        });
        listener.Start();

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, canonicalPagePath, _, _) = BuildExecutor(root);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "concepts/new.md", content = "new page content" }),
                turn: 1,
                CancellationToken.None);

            Assert.False(writeResult.IsError);

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki.query.synthesis_page_created"));
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal("t-obs-1", entry.Fields["task_id"]?.ToString());
            Assert.Equal(canonicalPagePath, entry.Fields["path"]?.ToString());
            Assert.Equal("1", entry.Fields["turn"]?.ToString());

            lock (measurements)
            {
                Assert.Single(measurements);
                Assert.Equal(1L, measurements[0]);
            }
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
    public async Task ReadWriteTargetWrite_NeverFiresSynthesisSignal_EvenThoughTheWriteSucceeds()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.query.synthesis_pages_created_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            lock (measurements) { measurements.Add(value); }
        });
        listener.Start();

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, _, _, _) = BuildExecutor(root);

            // index.md does not exist yet — a run's first write to a path it never read is
            // always allowed (contract §3), but the matched rule is plain read-write, not
            // create-only, so this must not be mistaken for a Synthesis Page creation.
            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "index.md", content = "# Wiki Index" }),
                turn: 1,
                CancellationToken.None);

            Assert.False(writeResult.IsError);
            Assert.Empty(logger.Entries.Where(e => e.EventName == "wiki.query.synthesis_page_created"));
            lock (measurements)
            {
                Assert.Empty(measurements);
            }
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
    public async Task DeniedCreateOnlyWrite_NeverFiresSynthesisSignal()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.query.synthesis_pages_created_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            lock (measurements) { measurements.Add(value); }
        });
        listener.Start();

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, _, conceptsDir, _) = BuildExecutor(root);
            var existingPage = Path.Combine(conceptsDir, "existing.md");
            await File.WriteAllTextAsync(existingPage, "already here");

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "concepts/existing.md", content = "overwrite attempt" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Contains("create_only_target_exists", writeResult.Content, StringComparison.Ordinal);
            Assert.Empty(logger.Entries.Where(e => e.EventName == "wiki.query.synthesis_page_created"));
            lock (measurements)
            {
                Assert.Empty(measurements);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static (GuardedToolExecutor Executor, CaptureLogger<QuerySynthesisWriteObservabilityTests> Logger, string CanonicalPagePath, string ConceptsDir, string WriteLocksDir)
        BuildExecutor(string root)
    {
        var wikiRoot = Path.Combine(root, "wiki");
        var conceptsDir = Path.Combine(wikiRoot, "concepts");
        Directory.CreateDirectory(conceptsDir);
        var writeLocksDir = Path.Combine(root, "write-locks");

        var policy = new SafetyPolicy(
            wikiRoot,
            readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
            writeRules:
            [
                new WriteRule(conceptsDir + Path.DirectorySeparatorChar, CreateOnly: true),
                new WriteRule(Path.Combine(wikiRoot, "index.md"), CreateOnly: false),
            ]);

        var logger = new CaptureLogger<QuerySynthesisWriteObservabilityTests>();
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            policy,
            journal,
            wikiRoot,
            taskId: "t-obs-1",
            registry: QueryToolRegistry.Default,
            instrumentation: new QueryToolCallInstrumentation(logger),
            writeLocksDir: writeLocksDir);

        var canonicalPagePath = Path.GetFullPath(Path.Combine(conceptsDir, "new.md"));
        return (executor, logger, canonicalPagePath, conceptsDir, writeLocksDir);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"query-synthesis-observability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}

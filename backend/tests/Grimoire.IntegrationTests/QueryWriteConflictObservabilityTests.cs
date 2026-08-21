using System.Diagnostics.Metrics;
using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T035 (012-query-synthesis-writes, US2) — validates the <c>wiki.write_conflict.rejected</c>
/// log event's name/level/mandatory fields and the <c>wiki.write_conflict.rejections_total</c>
/// counter's increment (plan.md ## Observability), with the correct <c>reason</c> label, for
/// both new write-coordination denial kinds: <c>create_only_target_exists</c> and
/// <c>write_conflict_stale_read</c>. Confirms neither signal fires for a pre-existing
/// <c>out_of_scope</c> policy-scope denial (that reason already has its own established
/// signal via <see cref="IToolCallInstrumentation.RecordDenied"/>).
/// </summary>
[Collection("HubActivityListenerObservability")]
public class QueryWriteConflictObservabilityTests
{
    [Fact]
    public async Task DeniedCreateOnlyOverwrite_EmitsWriteConflictRejected_WithCreateOnlyReason()
    {
        var measurements = new List<(long Value, string Reason)>();
        using var listener = StartListener(measurements);

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, conceptsDir, _) = BuildExecutor(root);
            var existingPage = Path.Combine(conceptsDir, "existing.md");
            await File.WriteAllTextAsync(existingPage, "already here");

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "concepts/existing.md", content = "overwrite attempt" }),
                turn: 3,
                CancellationToken.None);

            Assert.True(writeResult.IsError);

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki.write_conflict.rejected"));
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("t-conflict-1", entry.Fields["task_id"]?.ToString());
            Assert.Equal(Path.GetFullPath(existingPage), entry.Fields["path"]?.ToString());
            Assert.Equal("create_only_target_exists", entry.Fields["reason"]?.ToString());
            Assert.Equal("3", entry.Fields["turn"]?.ToString());

            lock (measurements)
            {
                var measurement = Assert.Single(measurements);
                Assert.Equal(1L, measurement.Value);
                Assert.Equal("create_only_target_exists", measurement.Reason);
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
    public async Task DeniedStaleRead_EmitsWriteConflictRejected_WithStaleReadReason()
    {
        var measurements = new List<(long Value, string Reason)>();
        using var listener = StartListener(measurements);

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, _, indexPath) = BuildExecutor(root);
            await File.WriteAllTextAsync(indexPath, "# Wiki Index\n\noriginal");

            // This run never read index.md, so a read-write write to an existing target it
            // never read is a stale-read rejection (no baseline to safely compare against —
            // the guard fails closed rather than silently allowing a blind overwrite).
            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "index.md", content = "# Wiki Index\n\noverwritten" }),
                turn: 5,
                CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Contains("write_conflict_stale_read", writeResult.Content, StringComparison.Ordinal);

            var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki.write_conflict.rejected"));
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("t-conflict-1", entry.Fields["task_id"]?.ToString());
            Assert.Equal(Path.GetFullPath(indexPath), entry.Fields["path"]?.ToString());
            Assert.Equal("write_conflict_stale_read", entry.Fields["reason"]?.ToString());
            Assert.Equal("5", entry.Fields["turn"]?.ToString());

            lock (measurements)
            {
                var measurement = Assert.Single(measurements);
                Assert.Equal(1L, measurement.Value);
                Assert.Equal("write_conflict_stale_read", measurement.Reason);
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
    public async Task DeniedOutOfScopeWrite_NeverFiresWriteConflictSignal()
    {
        var measurements = new List<(long Value, string Reason)>();
        using var listener = StartListener(measurements);

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, _, _) = BuildExecutor(root);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "tasks/rogue.md", content = "out of scope" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Contains("out_of_scope", writeResult.Content, StringComparison.Ordinal);

            Assert.Empty(logger.Entries.Where(e => e.EventName == "wiki.write_conflict.rejected"));
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

    private static MeterListener StartListener(List<(long Value, string Reason)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.write_conflict.rejections_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var reason = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason")
                {
                    reason = tag.Value?.ToString() ?? string.Empty;
                }
            }

            lock (measurements) { measurements.Add((value, reason)); }
        });
        listener.Start();
        return listener;
    }

    private static (GuardedToolExecutor Executor, CaptureLogger<QueryWriteConflictObservabilityTests> Logger, string ConceptsDir, string IndexPath)
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

        var logger = new CaptureLogger<QueryWriteConflictObservabilityTests>();
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            policy,
            journal,
            wikiRoot,
            taskId: "t-conflict-1",
            registry: QueryToolRegistry.Default,
            instrumentation: new QueryToolCallInstrumentation(logger),
            writeLocksDir: writeLocksDir);

        return (executor, logger, conceptsDir, Path.Combine(wikiRoot, "index.md"));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"write-conflict-observability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}

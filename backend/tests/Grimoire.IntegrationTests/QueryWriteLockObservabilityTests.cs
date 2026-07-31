using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Guardrails.Coordination;
using Grimoire.Domain.Guardrails;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T043 (012-query-synthesis-writes, US3) — validates plan.md's
/// <c>guardrails.acquire_write_lock</c> span, <c>wiki.write_lock.acquisitions_total</c>
/// (<c>outcome=acquired|timeout</c>) counter, <c>wiki.write_lock.wait_seconds</c> histogram,
/// and the <c>wiki.write_lock.timeout</c> WARN log event (T042), for both the ordinary
/// acquired path and the timed-out path.
/// </summary>
public class QueryWriteLockObservabilityTests
{
    [Fact]
    public async Task SuccessfulAcquisition_EmitsAcquiredMetricAndSpan_NestedUnderTheAmbientActivity_NoTimeoutLogEvent()
    {
        var (measurements, waitSeconds) = StartCounterAndHistogramListeners();
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.QueryAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, conceptsDir, _) = BuildExecutor(root, "t-lock-acquired-1");
            var newPagePath = Path.Combine(conceptsDir, "new.md");

            // The `*_agent.tool_call` span for this same write is only created afterward,
            // once the allow/deny decision is known (RecordAllowed/RecordDenied) — at
            // acquisition time the ambient activity is whatever the caller's own model-turn
            // span is. Model that here explicitly, per IToolCallInstrumentation's
            // StartAcquireWriteLockActivity doc comment.
            Activity? ambientActivity;
            using (ambientActivity = QueryAgentTracing.ActivitySource.StartActivity("query_agent.model_turn"))
            {
                ambientActivity?.SetTag("turn_id", "t-lock-acquired-1");

                var result = await executor.ExecuteAsync(
                    ToolRegistry.WriteFile,
                    JsonSerializer.Serialize(new { path = "concepts/new.md", content = "# New synthesis page" }),
                    turn: 1,
                    CancellationToken.None);

                Assert.False(result.IsError);
            }

            Assert.True(File.Exists(newPagePath));

            var lockSpan = Assert.Single(activities, a => a.OperationName == "guardrails.acquire_write_lock");
            Assert.NotNull(ambientActivity);
            Assert.Equal(ambientActivity!.SpanId.ToHexString(), lockSpan.ParentSpanId.ToHexString());
            Assert.Equal(Path.GetFullPath(newPagePath), GetTag(lockSpan, "path"));
            Assert.Equal("acquired", GetTag(lockSpan, "outcome"));
            Assert.True(double.TryParse(GetTag(lockSpan, "wait_ms"), out var waitMs) && waitMs >= 0,
                $"Expected a non-negative numeric wait_ms tag, got '{GetTag(lockSpan, "wait_ms")}'.");

            lock (measurements)
            {
                var measurement = Assert.Single(measurements);
                Assert.Equal(1L, measurement.Value);
                Assert.Equal("acquired", measurement.Outcome);
            }
            lock (waitSeconds)
            {
                Assert.Single(waitSeconds);
                Assert.All(waitSeconds, v => Assert.True(v >= 0));
            }

            Assert.Empty(logger.Entries.Where(e => e.EventName == "wiki.write_lock.timeout"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LockHeldByAnotherWriter_PastTheBackoffCap_EmitsTimeoutMetricLogAndSpan()
    {
        var (measurements, waitSeconds) = StartCounterAndHistogramListeners();
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.QueryAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        var root = CreateTempRoot();
        try
        {
            var (executor, logger, _, indexPath) = BuildExecutor(
                root, "t-lock-timeout-1", writeLockBackoffCap: TimeSpan.FromMilliseconds(200));
            await File.WriteAllTextAsync(indexPath, "# Wiki Index");

            var writeLocksDir = Path.Combine(root, "write-locks");

            // Rig a held lock: another writer (or process) is mid-write on this exact
            // target when our attempt comes in.
            var holder = await CrossProcessFileLock.TryAcquireAsync(
                writeLocksDir, indexPath, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(holder);

            try
            {
                var result = await executor.ExecuteAsync(
                    ToolRegistry.WriteFile,
                    JsonSerializer.Serialize(new { path = "index.md", content = "# Wiki Index\n\nnever lands" }),
                    turn: 7,
                    CancellationToken.None);

                Assert.True(result.IsError);
                Assert.Contains("write_coordination_timeout", result.Content, StringComparison.Ordinal);
            }
            finally
            {
                holder!.Dispose();
            }

            var logEntry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki.write_lock.timeout"));
            Assert.Equal(LogLevel.Warning, logEntry.Level);
            Assert.Equal("t-lock-timeout-1", logEntry.Fields["task_id"]?.ToString());
            Assert.Equal(Path.GetFullPath(indexPath), logEntry.Fields["path"]?.ToString());
            Assert.True(double.TryParse(logEntry.Fields["wait_ms"]?.ToString(), out var loggedWaitMs) && loggedWaitMs >= 200,
                $"Expected wait_ms >= 200 (the configured backoff cap), got '{logEntry.Fields["wait_ms"]}'.");

            var lockSpan = Assert.Single(activities, a => a.OperationName == "guardrails.acquire_write_lock");
            Assert.Equal(Path.GetFullPath(indexPath), GetTag(lockSpan, "path"));
            Assert.Equal("timeout", GetTag(lockSpan, "outcome"));
            Assert.True(double.TryParse(GetTag(lockSpan, "wait_ms"), out var spanWaitMs) && spanWaitMs >= 200,
                $"Expected span wait_ms >= 200, got '{GetTag(lockSpan, "wait_ms")}'.");

            lock (measurements)
            {
                var measurement = Assert.Single(measurements);
                Assert.Equal(1L, measurement.Value);
                Assert.Equal("timeout", measurement.Outcome);
            }
            lock (waitSeconds)
            {
                var recorded = Assert.Single(waitSeconds);
                Assert.True(recorded >= 0.2, $"Expected wait_seconds >= 0.2, got {recorded}.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;

    private static (List<(long Value, string Outcome)> Counter, List<double> Histogram) StartCounterAndHistogramListeners()
    {
        var counterMeasurements = new List<(long Value, string Outcome)>();
        var histogramMeasurements = new List<double>();

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name != "Grimoire.QueryAgent")
                {
                    return;
                }

                if (instrument.Name is "wiki.write_lock.acquisitions_total" or "wiki.write_lock.wait_seconds")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name != "wiki.write_lock.acquisitions_total")
            {
                return;
            }

            var outcome = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString() ?? string.Empty;
                }
            }

            lock (counterMeasurements) { counterMeasurements.Add((value, outcome)); }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name != "wiki.write_lock.wait_seconds")
            {
                return;
            }

            lock (histogramMeasurements) { histogramMeasurements.Add(value); }
        });
        listener.Start();

        // Intentionally not disposed here — the caller's test method owns the listener's
        // lifetime implicitly via the process-wide MeterListener static registration used
        // elsewhere in this test suite (matches QueryWriteConflictObservabilityTests' idiom,
        // where the listener is also never explicitly stopped beyond GC).
        return (counterMeasurements, histogramMeasurements);
    }

    private static (GuardedToolExecutor Executor, CaptureLogger<QueryWriteLockObservabilityTests> Logger, string ConceptsDir, string IndexPath)
        BuildExecutor(string root, string taskId, TimeSpan? writeLockBackoffCap = null)
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

        var logger = new CaptureLogger<QueryWriteLockObservabilityTests>();
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            policy,
            journal,
            wikiRoot,
            taskId: taskId,
            registry: QueryToolRegistry.Default,
            instrumentation: new QueryToolCallInstrumentation(logger),
            writeLocksDir: writeLocksDir,
            writeLockBackoffCap: writeLockBackoffCap);

        return (executor, logger, conceptsDir, Path.Combine(wikiRoot, "index.md"));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"write-lock-observability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}

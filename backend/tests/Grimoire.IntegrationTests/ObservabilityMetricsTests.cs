using System.Diagnostics.Metrics;
using Grimoire.Domain.Guardrails;
using Grimoire.Hub;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T035 — Business metric emission via in-process MeterListener (ADR-005).
/// The meters are process-wide statics and other test classes run in parallel and emit
/// the same instruments, so every callback synchronizes its list, assertions run on a
/// snapshot, and expectations are containment-based rather than exact global counts.
/// </summary>
public class ObservabilityMetricsTests
{
    private static IReadOnlyList<T> Snapshot<T>(List<T> measurements)
    {
        lock (measurements)
        {
            return measurements.ToArray();
        }
    }

    private static void AddSynchronized<T>(List<T> measurements, T measurement)
    {
        lock (measurements)
        {
            measurements.Add(measurement);
        }
    }

    [Fact]
    public void HubMetrics_RecordTaskReconciled_Increments_TasksReconciledTotal()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" &&
                instrument.Name == "wiki.ingest.tasks_reconciled_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordTaskReconciled();

        Assert.Contains(Snapshot(measurements), v => v == 1L);
    }

    [Fact]
    public void IngestAgentMetrics_RecordIngest_Increments_OperationsTotal_WithOutcomeTag()
    {
        var measurements = new List<(long Value, string Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "wiki.ingest.operations_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, outcome));
        });
        listener.Start();

        IngestAgentMetrics.RecordIngest("completed", 2.0);

        Assert.Contains(Snapshot(measurements), m => m.Value == 1L && m.Outcome == "completed");
    }

    /// <summary>
    /// T050 — plan: Observability > Business Metrics. The real success path emits
    /// wiki.ingest.pages_touched_total with the action=created|updated|superseded
    /// split derived from the journal-backed outcome (never an out-of-contract label).
    /// </summary>
    [Fact]
    public async Task PagesTouchedTotal_EmitsPlanActionSplit_FromJournalBackedOutcome()
    {
        var measurements = new List<(long Value, string Action)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "wiki.ingest.pages_touched_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var action = tags.ToArray().FirstOrDefault(t => t.Key == "action").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, action));
        });
        listener.Start();

        var root = Path.Combine(Path.GetTempPath(), $"metrics-split-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var pagesDir = Path.Combine(wikiDir, "pages");
        Directory.CreateDirectory(pagesDir);
        await File.WriteAllTextAsync(Path.Combine(pagesDir, "existing.md"), "before");

        var policy = new SafetyPolicy(
            root,
            readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
            writePrefixes: [pagesDir + Path.DirectorySeparatorChar]);
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(policy, journal, root, taskId: "task-metrics");
        var fake = new FakeModelClient([
            FakeModelClient.WriteFileTurn("tool-1", "wiki/pages/existing.md", "after"),
            FakeModelClient.WriteFileTurn("tool-2", "wiki/pages/new.md", "# New page"),
            FakeModelClient.FinalTurn("Metrics split run complete.")]);
        var loop = new AgentLoop(fake, executor);

        await loop.RunAsync(
            systemPrompt: "You are a test agent.",
            taskId: "task-metrics",
            sourceRef: "source.md",
            sourceContent: "# source",
            cancellationToken: CancellationToken.None);

        IngestAgentMetrics.RecordPagesTouched(journal);

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Action == "created");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Action == "updated");
        Assert.All(snapshot, m =>
            Assert.Contains(m.Action, new[] { "created", "updated", "superseded" }));
    }

    /// <summary>T050 — the superseded label of the plan's action set is emittable and tagged correctly.</summary>
    [Fact]
    public void PagesTouchedTotal_SupersededAction_EmitsPlanLabel()
    {
        var measurements = new List<(long Value, string Action)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "wiki.ingest.pages_touched_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var action = tags.ToArray().FirstOrDefault(t => t.Key == "action").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, action));
        });
        listener.Start();

        IngestAgentMetrics.RecordPagesTouched("superseded", 3);

        Assert.Contains(Snapshot(measurements), m => m.Value == 3L && m.Action == "superseded");
    }

    [Fact]
    public void IngestAgentMetrics_RecordIngest_Records_DurationSeconds_Histogram()
    {
        var measurements = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "wiki.ingest.duration_seconds")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        IngestAgentMetrics.RecordIngest("failed", 3.14);

        Assert.Contains(Snapshot(measurements), v => Math.Abs(v - 3.14) < 1e-5);
    }

    [Fact]
    public void IngestAgentMetrics_RecordModelToolRequests_EmitsCount_WithStopReasonTag()
    {
        var measurements = new List<(long Value, string StopReason)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "wiki.ingest.model_tool_requests_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var stopReason = tags.ToArray().FirstOrDefault(t => t.Key == "stop_reason").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, stopReason));
        });
        listener.Start();

        IngestAgentMetrics.RecordModelToolRequests(3, ModelStopReason.ToolUse);

        Assert.Contains(Snapshot(measurements), m =>
            m.Value == 3L &&
            m.StopReason == "tool_use");
    }

    [Fact]
    public void IngestAgentMetrics_RecordNoToolTurn_EmitsCounter_WithOutcomeAndStopReason()
    {
        var measurements = new List<(long Value, string StopReason, string Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "wiki.ingest.no_tool_turns_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var stopReason = tags.ToArray().FirstOrDefault(t => t.Key == "stop_reason").Value?.ToString() ?? "";
            var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, stopReason, outcome));
        });
        listener.Start();

        IngestAgentMetrics.RecordNoToolTurn(ModelStopReason.StopSequence, "terminal");

        Assert.Contains(Snapshot(measurements), m =>
            m.Value == 1L &&
            m.StopReason == "stop_sequence" &&
            m.Outcome == "terminal");
    }
}

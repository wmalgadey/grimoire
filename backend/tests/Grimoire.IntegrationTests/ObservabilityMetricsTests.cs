using System.Diagnostics.Metrics;
using Grimoire.Domain.Guardrails;
using Grimoire.Hub;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>T035 — Business metric emission via in-process MeterListener (ADR-005).</summary>
public class ObservabilityMetricsTests
{
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
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        HubMetrics.RecordTaskReconciled();

        Assert.Single(measurements);
        Assert.Equal(1L, measurements[0]);
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
            measurements.Add((value, outcome));
        });
        listener.Start();

        IngestAgentMetrics.RecordIngest("completed", 2.0);

        Assert.Single(measurements);
        Assert.Equal(1L, measurements[0].Value);
        Assert.Equal("completed", measurements[0].Outcome);
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
            measurements.Add((value, action));
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

        Assert.Contains(measurements, m => m.Value == 1L && m.Action == "created");
        Assert.Contains(measurements, m => m.Value == 1L && m.Action == "updated");
        Assert.All(measurements, m =>
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
            measurements.Add((value, action));
        });
        listener.Start();

        IngestAgentMetrics.RecordPagesTouched("superseded", 3);

        Assert.Single(measurements);
        Assert.Equal(3L, measurements[0].Value);
        Assert.Equal("superseded", measurements[0].Action);
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
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        IngestAgentMetrics.RecordIngest("failed", 3.14);

        Assert.Single(measurements);
        Assert.Equal(3.14, measurements[0], precision: 5);
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
            measurements.Add((value, stopReason));
        });
        listener.Start();

        IngestAgentMetrics.RecordModelToolRequests(3, ModelStopReason.ToolUse);

        Assert.Contains(measurements, m =>
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
            measurements.Add((value, stopReason, outcome));
        });
        listener.Start();

        IngestAgentMetrics.RecordNoToolTurn(ModelStopReason.StopSequence, "terminal");

        Assert.Contains(measurements, m =>
            m.Value == 1L &&
            m.StopReason == "stop_sequence" &&
            m.Outcome == "terminal");
    }
}

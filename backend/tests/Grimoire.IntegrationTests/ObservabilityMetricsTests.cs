using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;

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

        IngestAgentMetrics.RecordIngest("completed", 1, "created", 2.0);

        Assert.Single(measurements);
        Assert.Equal(1L, measurements[0].Value);
        Assert.Equal("completed", measurements[0].Outcome);
    }

    [Fact]
    public void IngestAgentMetrics_RecordIngest_Increments_PagesTouchedTotal_WhenPagesWritten()
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

        IngestAgentMetrics.RecordIngest("completed", 2, "created", 1.5);

        Assert.Single(measurements);
        Assert.Equal(2L, measurements[0].Value);
        Assert.Equal("created", measurements[0].Action);
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

        IngestAgentMetrics.RecordIngest("failed", 0, "none", 3.14);

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

using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.QueryAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T046/T058/T075/T078 (008-query-agent) — business metric emission via in-process
/// MeterListener (mirrors ObservabilityMetricsTests.cs's pattern), for every metric row
/// in plan.md ## Observability > Business Metrics that this feature owns: the Hub-side
/// `query.turns_total`/`query.turn_duration_seconds`/`query.answer_chunks_total`/
/// `query.submissions_rejected_total` and the agent-side `query.tool_calls_total` (the
/// guarded tool executor runs inside `Grimoire.QueryAgent`, not the Hub).
/// </summary>
public class QueryLifecycleMetricsTests
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
    public void HubMetrics_RecordQueryTurn_Increments_TurnsTotal_WithOutcomeTag_ForEveryTerminalStatus()
    {
        var measurements = new List<(long Value, string Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "query.turns_total")
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

        HubMetrics.RecordQueryTurn("completed", 1.5);
        HubMetrics.RecordQueryTurn("interrupted", 0.5);
        HubMetrics.RecordQueryTurn("failed", 2.0);

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "completed");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "interrupted");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "failed");
    }

    [Fact]
    public void HubMetrics_RecordQueryTurn_Records_TurnDurationSeconds()
    {
        var measurements = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "query.turn_duration_seconds")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordQueryTurn("completed", 3.25);

        Assert.Contains(Snapshot(measurements), v => v == 3.25);
    }

    [Fact]
    public void HubMetrics_RecordQueryAnswerChunk_Increments_AnswerChunksTotal()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "query.answer_chunks_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordQueryAnswerChunk();

        Assert.Contains(Snapshot(measurements), v => v == 1L);
    }

    [Fact]
    public void HubMetrics_RecordQuerySubmissionRejected_Increments_SubmissionsRejectedTotal()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "query.submissions_rejected_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordQuerySubmissionRejected();

        Assert.Contains(Snapshot(measurements), v => v == 1L);
    }

    [Fact]
    public void QueryAgentMetrics_RecordToolCall_Increments_ToolCallsTotal_WithToolAndDecisionTags()
    {
        var measurements = new List<(long Value, string Tool, string Decision)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "query.tool_calls_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var tagArray = tags.ToArray();
            var tool = tagArray.FirstOrDefault(t => t.Key == "tool").Value?.ToString() ?? "";
            var decision = tagArray.FirstOrDefault(t => t.Key == "decision").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, tool, decision));
        });
        listener.Start();

        QueryAgentMetrics.RecordToolCall("read_file", "allowed");
        QueryAgentMetrics.RecordToolCall("read_file", "denied");

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Tool == "read_file" && m.Decision == "allowed");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Tool == "read_file" && m.Decision == "denied");
    }
}

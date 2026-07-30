using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T028 (013-lint-agent, US1) — business metric emission via in-process
/// <see cref="MeterListener"/> (mirrors QueryLifecycleMetricsTests.cs's pattern), for the
/// <c>wiki.lint.runs_total{outcome}</c> row of plan.md ## Observability > Business
/// Metrics this phase owns, plus the <c>wiki.lint.triggers_rejected_total</c> gap-fill
/// emitted at the same call site (T027's deviation note), and the agent-side
/// <c>lint.tool_calls_total</c> (the guarded tool executor runs inside
/// <c>Grimoire.LintAgent</c>, not the Hub).
/// </summary>
public class LintMetricsTests
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
    public void HubMetrics_RecordLintRun_Increments_RunsTotal_WithOutcomeTag_ForEveryTerminalStatus()
    {
        var measurements = new List<(long Value, string Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.runs_total")
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

        HubMetrics.RecordLintRun("completed");
        HubMetrics.RecordLintRun("failed");

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "completed");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "failed");
    }

    [Fact]
    public void HubMetrics_RecordLintTriggerRejected_Increments_TriggersRejectedTotal()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.triggers_rejected_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordLintTriggerRejected();

        Assert.Contains(Snapshot(measurements), v => v == 1L);
    }

    [Fact]
    public void LintAgentMetrics_RecordToolCall_Increments_ToolCallsTotal_WithToolAndDecisionTags()
    {
        var measurements = new List<(long Value, string Tool, string Decision)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.LintAgent" && instrument.Name == "lint.tool_calls_total")
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

        LintAgentMetrics.RecordToolCall("write_file", "allowed");
        LintAgentMetrics.RecordToolCall("write_file", "denied");

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Tool == "write_file" && m.Decision == "allowed");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Tool == "write_file" && m.Decision == "denied");
    }
}

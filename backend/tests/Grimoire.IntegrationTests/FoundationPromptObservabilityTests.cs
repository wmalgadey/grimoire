using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T032 + T033 (029-shared-foundation-prompt, US1, FR-018/SC-001): the
/// <c>wiki.identity.foundation_resolved_total{source}</c> counter and the
/// <c>wiki_identity_foundation_resolved</c> (INFO) log event — name, level, and every
/// mandatory field (<c>source</c>, <c>resolved_path</c>, <c>sha256</c>, <c>agent_id</c>).
/// Mirrors <c>LintMetricsTests</c>'s in-process <see cref="MeterListener"/> pattern and
/// <c>LintRemediationObservabilityTests</c>'s direct-call <see cref="CaptureLogger{T}"/>
/// pattern for a log-events helper invoked directly rather than through a full dispatch.
/// </summary>
public class FoundationPromptObservabilityTests
{
    [Fact]
    public void HubMetrics_RecordFoundationResolved_Increments_WithSourceTag_ForBothSources()
    {
        var measurements = new List<(long Value, string Source)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.identity.foundation_resolved_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var source = tags.ToArray().FirstOrDefault(t => t.Key == "source").Value?.ToString() ?? "";
            lock (measurements)
            {
                measurements.Add((value, source));
            }
        });
        listener.Start();

        HubMetrics.RecordFoundationResolved("default");
        HubMetrics.RecordFoundationResolved("instance");

        IReadOnlyList<(long Value, string Source)> snapshot;
        lock (measurements)
        {
            snapshot = measurements.ToArray();
        }

        Assert.Contains(snapshot, m => m.Value == 1L && m.Source == "default");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Source == "instance");
    }

    [Fact]
    public void LogFoundationResolved_EmitsExpectedNameLevelAndAllMandatoryFields()
    {
        var logger = new CaptureLogger<FoundationPromptObservabilityTests>();

        GrimoirePathLogEvents.LogFoundationResolved(
            logger, agentId: "query", source: "instance", resolvedPath: "/data/foundation-prompt.md", sha256: "abc123");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki_identity_foundation_resolved"));
        Assert.Equal(LogLevel.Information, entry.Level);

        Assert.True(entry.Fields.ContainsKey("agent_id"), "Missing mandatory field 'agent_id'.");
        Assert.True(entry.Fields.ContainsKey("source"), "Missing mandatory field 'source'.");
        Assert.True(entry.Fields.ContainsKey("resolved_path"), "Missing mandatory field 'resolved_path'.");
        Assert.True(entry.Fields.ContainsKey("sha256"), "Missing mandatory field 'sha256'.");

        Assert.Equal("query", entry.Fields["agent_id"]?.ToString());
        Assert.Equal("instance", entry.Fields["source"]?.ToString());
        Assert.Equal("/data/foundation-prompt.md", entry.Fields["resolved_path"]?.ToString());
        Assert.Equal("abc123", entry.Fields["sha256"]?.ToString());
    }
}

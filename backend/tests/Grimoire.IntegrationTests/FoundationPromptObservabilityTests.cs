using System.Diagnostics.Metrics;
using Grimoire.Hub;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T032 (029-shared-foundation-prompt, US1, FR-018): the
/// <c>wiki.identity.foundation_resolved_total{source}</c> counter. Mirrors
/// <c>LintMetricsTests</c>'s in-process <see cref="MeterListener"/> pattern. The
/// <c>wiki_identity_foundation_resolved</c> log event's own test (T033) now lives in
/// <see cref="WikiIdentityLoggingContractTests"/> alongside the wizard's four log events
/// (T052 folds the five-row Structured Log Events contract into one class).
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
}

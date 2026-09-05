using System.Diagnostics.Metrics;
using Grimoire.Hub;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T050 (029-shared-foundation-prompt, US2, FR-011): the
/// <c>wiki.identity.wizard_outcomes_total{outcome}</c> counter's label for each of the five
/// documented outcomes. Mirrors <c>FoundationPromptObservabilityTests</c>'s in-process
/// <see cref="MeterListener"/> pattern.
/// </summary>
public class WikiIdentityMetricsTests
{
    [Fact]
    public void HubMetrics_RecordWikiIdentityWizardOutcome_Increments_WithOutcomeTag_ForEveryDocumentedOutcome()
    {
        var measurements = new List<(long Value, string Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.identity.wizard_outcomes_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value?.ToString() ?? "";
            lock (measurements)
            {
                measurements.Add((value, outcome));
            }
        });
        listener.Start();

        string[] outcomes = ["default_kept", "brief_emitted", "document_persisted", "replace_refused", "rejected"];
        foreach (var outcome in outcomes)
        {
            HubMetrics.RecordWikiIdentityWizardOutcome(outcome);
        }

        IReadOnlyList<(long Value, string Outcome)> snapshot;
        lock (measurements)
        {
            snapshot = measurements.ToArray();
        }

        foreach (var outcome in outcomes)
        {
            Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == outcome);
        }
    }
}

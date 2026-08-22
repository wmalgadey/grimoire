using System.Diagnostics.Metrics;
using Grimoire.Hub;
using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T041 (Phase 6) - deterministic validation of the two 006 business metrics (plan.md
/// ## Observability > Business Metrics): hub.task_record_reads_total increments with the
/// correct outcome label per read path, hub.task_record_change_events_total increments
/// once per debounced publish.
///
/// <para>
/// #152 — both instruments are Hub-wide and carry no test-identifying tag
/// (<c>hub.task_record_reads_total</c> has only <c>outcome</c>, and
/// <c>hub.task_record_change_events_total</c> has none at all), so the assertions below —
/// <c>Assert.Single</c> over the whole measurement stream, and a running total across two
/// publishes — can only be made meaningful by keeping other emitters out of the process
/// while they run. Scoping by tag, the better fix where a tag exists, is not available
/// here. See <see cref="HubActivityListenerObservabilityCollection"/>.
/// </para>
/// </summary>
[Collection("HubActivityListenerObservability")]
public class IngestTaskRecordMetricsTests
{
    [Theory]
    [InlineData("ok")]
    [InlineData("missing")]
    [InlineData("unparseable")]
    public void RecordTaskRecordRead_IncrementsCounter_WithOutcomeLabel(string outcome)
    {
        var measurements = new List<(long Value, string? Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "hub.task_record_reads_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? tagOutcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome") tagOutcome = tag.Value as string;
            }
            measurements.Add((value, tagOutcome));
        });
        listener.Start();

        HubMetrics.RecordTaskRecordRead(outcome);

        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(outcome, measurement.Outcome);
    }

    [Fact]
    public async Task PublishTaskRecordChanged_IncrementsChangeEventsCounter_OncePerPublish()
    {
        var total = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "hub.task_record_change_events_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => total += value);
        listener.Start();

        var publisher = new IngestLifecyclePublisher(new NullHubContext());
        await publisher.PublishTaskRecordChangedAsync("task-metrics-1", DateTimeOffset.UtcNow);

        Assert.Equal(1, total);

        await publisher.PublishTaskRecordChangedAsync("task-metrics-1", DateTimeOffset.UtcNow);
        Assert.Equal(2, total);
    }
}

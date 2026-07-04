using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.IngestAgent;

namespace Grimoire.IntegrationTests;

public class ObservabilityGuardrailTests
{
    [Fact]
    public void Metrics_RecordGuardrailDeniedAction_WithReasonCodeTag()
    {
        var values = new List<(long Value, string ReasonCode)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.IngestAgent" &&
                instrument.Name == "ingest.guardrail.actions_denied_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var reasonCode = tags.ToArray().FirstOrDefault(x => x.Key == "reason_code").Value?.ToString() ?? string.Empty;
            values.Add((value, reasonCode));
        });

        listener.Start();
        IngestAgentMetrics.RecordGuardrailDecision("task-1", "write", allowed: false, reasonCode: "no-env-secret-dump");

        Assert.Single(values);
        Assert.Equal("no-env-secret-dump", values[0].ReasonCode);
    }

    [Fact]
    public void Tracing_ProvidesInstructionLoadSpan()
    {
        var spans = new List<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Grimoire.IngestAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add(activity.OperationName),
        };

        ActivitySource.AddActivityListener(listener);

        using (IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.instructions.load"))
        {
        }

        Assert.Contains("ingest_agent.instructions.load", spans);
    }
}

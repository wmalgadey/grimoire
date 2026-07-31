using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.WikiLog;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T060 (014-wiki-storage-restructure, /speckit-analyze finding G3): closes the business-
/// metric coverage gap T053's completeness audit found against this repo's own
/// <c>MeterListener</c>-based idiom (e.g. <see cref="IngestObservabilityMetricsTests"/>).
/// <see cref="WikiLogAppenderTests"/> already covers the backstop's log-event/trace-span
/// emission (T033) but not <c>wiki.log.backstop_appended_total</c>'s recorded value.
/// Cross-agent (<see cref="WikiLogAppender"/> is a shared <c>Grimoire.AgentRuntime</c>
/// component, not owned by any single agent), so this file stays unprefixed per
/// docs/conventions/agent-artifact-naming.md.
/// </summary>
public class WikiLogAppenderMetricsTests
{
    [Fact]
    public async Task AppendAsync_Increments_BackstopAppendedTotal_WithTypeLabel()
    {
        var measurements = new List<(long Value, string Type)>();
        using var listener = new MeterListener();
        var meter = new Meter("WikiLogAppenderMetricsTests.Backstop");
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == meter && instrument.Name == "wiki.log.backstop_appended_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var type = tags.ToArray().FirstOrDefault(t => t.Key == "type").Value?.ToString() ?? "";
            lock (measurements) { measurements.Add((value, type)); }
        });
        listener.Start();

        var root = Path.Combine(Path.GetTempPath(), $"backstop-metric-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "log.md");
            var appender = new WikiLogAppender(new ActivitySource("WikiLogAppenderMetricsTests"), meter);

            await appender.AppendAsync(
                logPath, "query", "completed", "source.md", "Detail text.", "turn-042", CancellationToken.None);

            lock (measurements)
            {
                Assert.Contains(measurements, m => m.Value == 1L && m.Type == "query");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

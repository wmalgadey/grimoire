using System.Diagnostics.Metrics;
using Grimoire.Domain.Ingest;
using Grimoire.Hub;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T062 - end-to-end verification that all 6 business metrics declared in
/// `plan.md ## Observability` are actually emitted by a real submission (not just the log/trace
/// contract rows, which have their own dedicated test files).
/// </summary>
public class IngestSubmissionMetricsTests
{
    [Fact]
    public async Task MarkdownFileSubmission_EmitsAllDeclaredCounters()
    {
        var seenInstruments = new HashSet<string>();
        var queueWaitSeen = false;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => seenInstruments.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            seenInstruments.Add(instrument.Name);
            if (instrument.Name == "hub.ingest_submission_queue_wait_seconds")
            {
                queueWaitSeen = true;
            }
        });
        listener.Start();

        using var fixture = new IngestSubmissionPipelineFixture();
        var bytes = System.Text.Encoding.UTF8.GetBytes("# Metrics fixture\n\nContent.");
        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.MarkdownFile, null, "note.md", bytes, "text/markdown"));

        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed");
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus is "completed" or "failed");

        Assert.Contains("hub.ingest_submissions_total", seenInstruments);
        Assert.Contains("hub.ingest_submission_conversions_total", seenInstruments);
        Assert.Contains("hub.ingest_submission_artifacts_persisted_total", seenInstruments);
        Assert.Contains("hub.ingest_lifecycle_updates_total", seenInstruments);
        Assert.True(queueWaitSeen, "hub.ingest_submission_queue_wait_seconds must be recorded once the run is triggered.");
    }

    [Fact]
    public async Task UrlSubmission_EmitsUrlFetchCounter()
    {
        var seenInstruments = new HashSet<string>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => seenInstruments.Add(instrument.Name));
        listener.Start();

        using var handler = new StaticHtmlHandler();
        using var fixture = new IngestSubmissionPipelineFixture(urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, "https://example.test/metrics", null, null, null));

        // T081 (Convergence) - the fixture's real `markitdown` subprocess is itself allowed up to
        // 30s (MarkItDownOptions.Timeout in IngestSubmissionPipelineFixture), which exceeds
        // WaitForStatusAsync's 10s default; under xUnit's full-suite parallelism the resulting
        // subprocess/CPU contention intermittently pushed conversion past that 10s window. Give
        // this wait real headroom instead of racing the converter's own timeout.
        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed", TimeSpan.FromSeconds(30));

        Assert.Contains("hub.ingest_submission_url_fetch_total", seenInstruments);
    }

    private sealed class StaticHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><p>Metrics fixture</p></body></html>", System.Text.Encoding.UTF8, "text/html"),
            };
            return Task.FromResult(response);
        }
    }
}

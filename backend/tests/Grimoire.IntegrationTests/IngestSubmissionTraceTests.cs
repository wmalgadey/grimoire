using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T038 (US1) - deterministic validation of span name, parent/child relationship, and correlation
/// attributes for the six US1 spans (plan.md ## Observability > Distributed Trace Spans).
/// </summary>
public class IngestSubmissionTraceTests
{
    [Fact]
    public async Task UrlSubmission_EmitsExpectedSpans_WithSubmitAsParent()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spans.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);

        using var handler = new StaticHtmlHandler();
        var dispatcher = new FakeIngestAgentDispatcher();
        using var fixture = new IngestSubmissionPipelineFixture(dispatcher: dispatcher, urlFetchHandler: handler);

        var taskId = await fixture.Pipeline.AcceptAsync(
            new IngestSubmissionInput(IngestSubmissionKind.Url, "https://example.test/article", null, null, null));

        // T081 (Convergence) - the fixture's real `markitdown` subprocess is itself allowed up to
        // 30s (MarkItDownOptions.Timeout in IngestSubmissionPipelineFixture), which exceeds
        // WaitForStatusAsync's 10s default; under xUnit's full-suite parallelism the resulting
        // subprocess/CPU contention intermittently pushed conversion past that 10s window. Give
        // this wait real headroom instead of racing the converter's own timeout.
        await fixture.WaitForStatusAsync(taskId, s => s is "completed" or "failed", TimeSpan.FromSeconds(30));
        // Allow the fire-and-forget trigger continuation's spans to finish stopping.
        await Task.Delay(200);

        var byName = spans.Where(a => a.GetTagItem("task_id") as string == taskId).ToLookup(a => a.OperationName);

        var submit = Assert.Single(byName["hub.ingest_submission.submit"]);
        Assert.Equal(taskId, submit.GetTagItem("task_id"));
        Assert.Equal("url", submit.GetTagItem("source_kind"));

        AssertChildOf(byName, submit, "hub.ingest_submission.fetch_url");
        AssertChildOf(byName, submit, "hub.ingest_submission.store_original");
        AssertChildOf(byName, submit, "hub.ingest_submission.convert_to_markdown");
        AssertChildOf(byName, submit, "hub.ingest_submission.store_normalized");
        AssertChildOf(byName, submit, "hub.ingest_run.trigger");
    }

    private static void AssertChildOf(ILookup<string, Activity> byName, Activity expectedParent, string childSpanName)
    {
        var child = Assert.Single(byName[childSpanName]);
        Assert.Equal(expectedParent.SpanId.ToHexString(), child.ParentSpanId.ToHexString());
    }

    private sealed class StaticHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><p>Trace fixture</p></body></html>", System.Text.Encoding.UTF8, "text/html"),
            };
            return Task.FromResult(response);
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Hub;
using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T049 (US2) - deterministic validation of span name and correlation attributes for
/// `hub.ingest_lifecycle.publish_update` (plan.md ## Observability > Distributed Trace Spans).
/// </summary>
public class IngestLifecycleTraceTests
{
    [Fact]
    public async Task PublishAsync_EmitsPublishUpdateSpan_WithTaskIdAndStageTags()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spans.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = new IngestLifecyclePublisher(new NullHubContext());
        await publisher.PublishAsync("task-trace-1", "queued", "running");

        var span = Assert.Single(spans, s => s.OperationName == "hub.ingest_lifecycle.publish_update" && s.GetTagItem("task_id") as string == "task-trace-1");
        Assert.Equal("running", span.GetTagItem("stage"));
    }

    /// <summary>
    /// C1 (analysis remediation) - plan.md declares `hub.ingest_lifecycle.publish_update`'s parent
    /// as `hub.ingest_submission.submit`, and Constitution IV / the DoD require the parent/child
    /// linkage — not just the span name — to be validated deterministically. The pipeline always
    /// calls `PublishAsync` within an ambient submit (or its trigger child) span; this reproduces
    /// that ambient context directly, without the pipeline's real `markitdown`/timing.
    /// </summary>
    [Fact]
    public async Task PublishAsync_NestsPublishUpdateSpan_UnderTheAmbientSpan()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spans.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = new IngestLifecyclePublisher(new NullHubContext());

        using var submitSpan = HubTracing.ActivitySource.StartActivity("hub.ingest_submission.submit");
        Assert.NotNull(submitSpan);
        await publisher.PublishAsync("task-parented-1", "converting", "queued");

        var publish = Assert.Single(spans, s =>
            s.OperationName == "hub.ingest_lifecycle.publish_update"
            && s.GetTagItem("task_id") as string == "task-parented-1");
        Assert.Equal(submitSpan!.SpanId.ToHexString(), publish.ParentSpanId.ToHexString());
        Assert.Equal(submitSpan.TraceId.ToHexString(), publish.TraceId.ToHexString());
        Assert.Equal("queued", publish.GetTagItem("stage"));
    }
}

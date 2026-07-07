using System.Collections.Concurrent;
using System.Diagnostics;
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
}

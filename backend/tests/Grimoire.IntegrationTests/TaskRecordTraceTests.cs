using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Hub;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.Realtime;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T040 (Phase 6) - deterministic validation of span name, parent/child relationship, and
/// correlation attributes for the two 006 spans (plan.md ## Observability > Distributed
/// Trace Spans): hub.task_record.serve (child of the ASP.NET Core request span) and
/// hub.task_record.publish_change (watcher-initiated root span).
/// </summary>
public class TaskRecordTraceTests
{
    [Fact]
    public async Task GetTaskRecord_EmitsServeSpan_AsChildOfAspNetCoreRequestSpan()
    {
        var exportedItems = new SynchronizedActivityCollection();
        using var fixture = new IngestSubmissionPipelineFixture();

        var taskId = "task-trace-1";
        await File.WriteAllTextAsync(Path.Combine(fixture.ContentPaths.TasksDir, $"{taskId}.md"),
            """
            ---
            task_id: task-trace-1
            status: completed
            agent: ingest
            started_at: 2026-07-19T10:00:00.0000000Z
            completed_at: 2026-07-19T10:01:00.0000000Z
            source_ref: null
            original_ref: null
            failure_reason: null
            ---

            Body.
            """);

        using var host = await BuildHostAsync(fixture, exportedItems);
        var client = host.GetTestClient();

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/task-record");
        response.EnsureSuccessStatusCode();

        var serveSpan = await WaitForSpanAsync(exportedItems,
            a => a.OperationName == "hub.task_record.serve", "hub.task_record.serve");
        Assert.Equal(taskId, serveSpan.GetTagItem("task_id"));
        Assert.Equal("ok", serveSpan.GetTagItem("outcome"));
        Assert.NotEqual(default, serveSpan.ParentSpanId);

        // AddAspNetCoreInstrumentation() (registered via BuildHostAsync's AddHubTelemetry
        // call) turns ASP.NET Core's own per-request Activity into a real, sampled OTel
        // span, so hub.task_record.serve's parent is that request span, not a synthetic
        // root (HubRequestTracingTests documents why this registration matters). The
        // request span stops (and exports) after its children, so it must be awaited too.
        await WaitForSpanAsync(exportedItems,
            a => a.SpanId == serveSpan.ParentSpanId, "ASP.NET Core parent request span");
    }

    [Fact]
    public async Task PublishTaskRecordChanged_EmitsRootSpan_WithTaskIdAndEventIdAttributes()
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

        await publisher.PublishTaskRecordChangedAsync("task-trace-2", DateTimeOffset.UtcNow);

        // ActivityListeners are process-global, so concurrent tests publishing other task
        // records land in `spans` too — every assertion is scoped to this test's task_id.
        var publishSpan = Assert.Single(spans, a => a.OperationName == "hub.task_record.publish_change"
            && Equals(a.GetTagItem("task_id"), "task-trace-2"));
        Assert.Null(publishSpan.ParentId);
        Assert.NotNull(publishSpan.GetTagItem("event_id"));

        // Correlation (Principle IV): the change_published log event nests under
        // publish_change and carries the same task_id.
        var logSpan = Assert.Single(spans, a => a.OperationName == "task_record.change_published"
            && Equals(a.GetTagItem("task_id"), "task-trace-2"));
        Assert.Equal(publishSpan.SpanId.ToHexString(), logSpan.ParentSpanId.ToHexString());
        Assert.Equal(publishSpan.GetTagItem("event_id"), logSpan.GetTagItem("event_id"));
    }

    private static async Task<Activity> WaitForSpanAsync(
        SynchronizedActivityCollection exportedItems, Func<Activity, bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var match = exportedItems.Snapshot().FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Span '{description}' was never exported.");
    }

    /// <summary>
    /// The in-memory exporter appends from the request-processing thread while the test
    /// thread polls; a plain List makes that enumeration race (and only under full-suite
    /// load, where late span stops overlap the polling).
    /// </summary>
    private sealed class SynchronizedActivityCollection : ICollection<Activity>
    {
        private readonly List<Activity> _items = [];
        private readonly object _gate = new();

        public int Count { get { lock (_gate) { return _items.Count; } } }
        public bool IsReadOnly => false;

        public void Add(Activity item) { lock (_gate) { _items.Add(item); } }
        public void Clear() { lock (_gate) { _items.Clear(); } }
        public bool Contains(Activity item) { lock (_gate) { return _items.Contains(item); } }
        public void CopyTo(Activity[] array, int arrayIndex) { lock (_gate) { _items.CopyTo(array, arrayIndex); } }
        public bool Remove(Activity item) { lock (_gate) { return _items.Remove(item); } }

        public Activity[] Snapshot() { lock (_gate) { return [.. _items]; } }

        public IEnumerator<Activity> GetEnumerator() => ((IEnumerable<Activity>)Snapshot()).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static async Task<IHost> BuildHostAsync(IngestSubmissionPipelineFixture fixture, ICollection<Activity> exportedItems)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHubTelemetry(tracing => tracing.AddInMemoryExporter(exportedItems));
                    services.AddSingleton(fixture.Validator);
                    services.AddSingleton(fixture.Pipeline);
                    services.AddSingleton(fixture.BoardStore);
                    services.AddSingleton(fixture.ContentPaths);
                    services.AddSingleton(fixture.SourceArtifactStore);
                    services.AddSingleton(fixture.Coordinator);
                    services.AddSingleton(new TaskRecordReadModel(fixture.ResolvedPaths));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints());
                });
            });

        return await hostBuilder.StartAsync();
    }
}

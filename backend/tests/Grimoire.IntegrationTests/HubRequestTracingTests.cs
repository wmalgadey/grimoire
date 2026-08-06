using System.Diagnostics;
using System.Net.Http.Json;
using Grimoire.Hub;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T075 (Convergence) - asserts Hub spans are actually exported under the production telemetry
/// registration (real ASP.NET Core request pipeline + default ParentBased(AlwaysOn) sampler), not
/// merely recorded via a test-only AllDataAndRecorded <see cref="ActivityListener"/> as the other
/// trace tests do. ASP.NET Core's hosting layer always starts an unsampled "recovery" Activity for
/// every request (for log-correlation purposes) independent of OpenTelemetry; without
/// AddAspNetCoreInstrumentation() that Activity is never handed to the OTel SDK, so it stays
/// unsampled and every Hub span created underneath it (via Activity.Current as parent) is dropped
/// by the ParentBased sampler. AddAspNetCoreInstrumentation() turns that recovery Activity into a
/// real, sampled OTel span, restoring the chain.
/// </summary>
public class HubRequestTracingTests
{
    [Fact]
    public async Task PostIngestSubmission_ExportsHubSubmitSpan_UnderProductionTelemetryRegistration()
    {
        var exportedItems = new SynchronizedActivityCollection();
        using var fixture = new IngestSubmissionPipelineFixture(urlFetchHandler: new StaticHtmlHandler());
        using var host = await BuildHostAsync(fixture, exportedItems);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/ingest-submissions", new { kind = "url", url = "https://example.test/article" });
        response.EnsureSuccessStatusCode();

        var submitSpan = await WaitForSpanAsync(exportedItems, "hub.ingest_submission.submit");

        Assert.True(submitSpan.Recorded);
    }

    private static async Task<Activity> WaitForSpanAsync(SynchronizedActivityCollection exportedItems, string operationName)
    {
        await PollAsync.WaitAsync(
            () => exportedItems.Snapshot().Any(a => a.OperationName == operationName),
            TimeSpan.FromSeconds(10),
            $"Span '{operationName}' was never exported.");

        var match = exportedItems.Snapshot().FirstOrDefault(a => a.OperationName == operationName);
        if (match is not null)
        {
            return match;
        }

        throw new TimeoutException($"Span '{operationName}' was never exported.");
    }

    /// <summary>
    /// The in-memory exporter appends from the request-processing thread while the test thread
    /// polls; a plain List makes that enumeration race (mirrors IngestTaskRecordTraceTests).
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

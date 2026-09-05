using System.Diagnostics;
using Grimoire.Hub;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Grimoire.IntegrationTests.TestSupport;

/// <summary>
/// 023-task-ui-improvements: the ingest HTTP surface hosted over a real ASP.NET Core
/// request pipeline, wired from an <see cref="IngestSubmissionPipelineFixture"/>'s real
/// collaborators (real temp filesystem, real SQLite file, existing port fake for the agent
/// process). Shared by the feature's endpoint and observability tests so both exercise the
/// same registration — the observability variant additionally installs the production
/// telemetry registration (<see cref="TelemetryExtensions.AddHubTelemetry"/>) with an
/// in-memory exporter, per Principle IV's "contract tests exercise the production wiring"
/// rule and the <c>HubRequestTracingTests</c> precedent.
/// </summary>
public static class IngestApiHost
{
    public static Task<IHost> BuildAsync(IngestSubmissionPipelineFixture fixture)
        => BuildAsync(fixture, exportedActivities: null);

    public static async Task<IHost> BuildAsync(
        IngestSubmissionPipelineFixture fixture,
        ICollection<Activity>? exportedActivities,
        List<Metric>? exportedMetrics = null)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    if (exportedActivities is not null || exportedMetrics is not null)
                    {
                        services.AddHubTelemetry(
                            tracing =>
                            {
                                if (exportedActivities is not null)
                                {
                                    tracing.AddInMemoryExporter(exportedActivities);
                                }
                            },
                            metrics =>
                            {
                                if (exportedMetrics is not null)
                                {
                                    metrics.AddInMemoryExporter(exportedMetrics);
                                }
                            });
                    }

                    services.AddSingleton(fixture.Validator);
                    services.AddSingleton(fixture.Pipeline);
                    services.AddSingleton(fixture.BoardStore);
                    services.AddSingleton(fixture.ContentPaths);
                    services.AddSingleton(fixture.RawPaths);
                    services.AddSingleton(fixture.ResolvedPaths);
                    services.AddSingleton(fixture.IngestSourceArtifactStore);
                    services.AddSingleton(fixture.Coordinator);
                    services.AddSingleton(fixture.Repository);
                    services.AddSingleton(new IngestTaskRecordReadModel(fixture.ResolvedPaths));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
                        endpoints.MapGroup("/api/ingest-queue").MapIngestQueueEndpoints();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }

    /// <summary>
    /// The in-memory exporter appends from the request-processing thread while the test
    /// thread polls; a plain <see cref="List{T}"/> makes that enumeration race (same reason
    /// <c>HubRequestTracingTests</c> carries its own synchronized collection).
    /// </summary>
    public sealed class SynchronizedActivityCollection : ICollection<Activity>
    {
        private readonly List<Activity> _items = [];
        private readonly Lock _gate = new();

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

        /// <summary>Waits for a span with <paramref name="operationName"/> to be exported and returns it.</summary>
        public async Task<Activity> WaitForSpanAsync(string operationName, TimeSpan? timeout = null)
        {
            await PollAsync.WaitAsync(
                () => Snapshot().Any(a => a.OperationName == operationName),
                timeout ?? TimeSpan.FromSeconds(10),
                $"Span '{operationName}' was never exported.");

            return Snapshot().First(a => a.OperationName == operationName);
        }
    }
}

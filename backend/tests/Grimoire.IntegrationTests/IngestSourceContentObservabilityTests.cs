using System.Text;
using Grimoire.Domain.Ingest;
using Grimoire.Hub;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T026 (023-task-ui-improvements, plan.md ## Observability): the source-content endpoint's
/// logging, metric and trace contracts, obtained through the production composition root
/// (<c>AddHubTelemetry</c> + in-memory exporters on the same provider builders the Hub uses),
/// per Principle IV's rule against test-only always-on listeners.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class IngestSourceContentObservabilityTests
{
    [Fact]
    public async Task Served_EmitsTheLogEvent_WithTaskIdAndContentType()
    {
        var logger = new CaptureLogger<IngestSourceContentObservabilityTests>();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, logger);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "note.md",
            Encoding.UTF8.GetBytes("# Note\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        var response = await client.GetAsync($"/api/ingest-submissions/{taskId}/source/original");
        response.EnsureSuccessStatusCode();

        var entry = Assert.Single(logger.Entries, e => e.EventName == "ingest.source.served");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(taskId, entry.Fields["task_id"]);
        Assert.Equal("text/markdown", entry.Fields["content_type"]);
    }

    [Fact]
    public async Task SourceContentReadsTotal_IsExported_ThroughProductionMeterRegistration_WithDeclaredLabelSetOnly()
    {
        var exportedMetrics = new List<Metric>();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, new CaptureLogger<IngestSourceContentObservabilityTests>(), exportedMetrics);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "note.md",
            Encoding.UTF8.GetBytes("# Note\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        (await client.GetAsync($"/api/ingest-submissions/{taskId}/source/original")).EnsureSuccessStatusCode();
        await client.GetAsync("/api/ingest-submissions/no-such-task/source/original");

        host.Services.GetRequiredService<MeterProvider>().ForceFlush();

        var metric = Assert.Single(exportedMetrics, m => m.Name == "hub.source_content_reads_total");
        var results = new List<string>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                Assert.Equal("result", tag.Key);
                results.Add((string)tag.Value!);
            }
        }

        Assert.All(results, r => Assert.Contains(r, new[] { "served", "not_found" }));
        Assert.Contains("served", results);
        Assert.Contains("not_found", results);
    }

    [Fact]
    public async Task ServeSpan_IsExportedAsAChildOfTheAspNetCoreRequestSpan_WithDeclaredAttributes()
    {
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture, exported);
        var client = host.GetTestClient();

        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "note.md",
            Encoding.UTF8.GetBytes("# Note\n\nBody.\n"), "text/markdown"));
        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "completed");

        (await client.GetAsync($"/api/ingest-submissions/{taskId}/source/original")).EnsureSuccessStatusCode();

        var span = await exported.WaitForSpanAsync("hub.ingest_source.serve");

        Assert.NotEqual(default, span.ParentSpanId);
        Assert.Equal(taskId, span.GetTagItem("task_id"));
        Assert.Equal("served", span.GetTagItem("result"));
    }

    private static async Task<IHost> BuildHostAsync(
        IngestSubmissionPipelineFixture fixture, CaptureLogger<IngestSourceContentObservabilityTests> logger, List<Metric>? exportedMetrics = null)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    if (exportedMetrics is not null)
                    {
                        services.AddHubTelemetry(configureMetrics: metrics => metrics.AddInMemoryExporter(exportedMetrics));
                    }
                    services.AddSingleton<ILoggerFactory>(new CaptureLoggerFactory(logger));
                    services.AddSingleton(fixture.Validator);
                    services.AddSingleton(fixture.Pipeline);
                    services.AddSingleton(fixture.BoardStore);
                    services.AddSingleton(fixture.ContentPaths);
                    services.AddSingleton(fixture.SourceArtifactStore);
                    services.AddSingleton(fixture.Coordinator);
                    services.AddSingleton(fixture.Repository);
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

    /// <summary>
    /// Routes only <see cref="IngestSubmissionEndpoints"/>'s own logger to the capturing
    /// sink; every other category (ASP.NET Core's internal hosting/routing diagnostics,
    /// emitted on background threads throughout the request) gets a no-op logger. Handing
    /// the framework's own chatter into a plain, unsynchronized <c>List</c>-backed capture
    /// races with the test's own enumeration of it.
    /// </summary>
    private sealed class CaptureLoggerFactory(ILogger logger) : ILoggerFactory
    {
        private static readonly string TargetCategory = typeof(IngestSubmissionEndpoints).FullName!;

        public ILogger CreateLogger(string categoryName) =>
            categoryName == TargetCategory ? logger : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }
}

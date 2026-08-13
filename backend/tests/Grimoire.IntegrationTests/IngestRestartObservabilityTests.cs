using System.Net;
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
/// T032 (023-task-ui-improvements, plan.md ## Observability): the restart endpoint's
/// logging, metric and trace contracts, obtained through the production composition root,
/// per Principle IV's rule against test-only always-on listeners.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class IngestRestartObservabilityTests
{
    [Fact]
    public async Task AcceptedRestart_EmitsTheRestartedLogEvent_WithTaskId()
    {
        var logger = new CaptureLogger<IngestRestartObservabilityTests>();
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await BuildHostAsync(fixture, logger);
        var client = host.GetTestClient();

        var taskId = await FailATaskAsync(fixture, launcher);

        (await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null)).EnsureSuccessStatusCode();

        var entry = Assert.Single(logger.Entries, e => e.EventName == "ingest.task.restarted");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(taskId, entry.Fields["task_id"]);
    }

    [Fact]
    public async Task RejectedRestart_EmitsTheRejectedLogEvent_WithTaskIdAndCurrentStatus()
    {
        var logger = new CaptureLogger<IngestRestartObservabilityTests>();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, logger);
        var client = host.GetTestClient();

        var taskId = "2026-08-13-ingest-notfailed";
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "running");

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var entry = Assert.Single(logger.Entries, e => e.EventName == "ingest.task.restart_rejected");
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(taskId, entry.Fields["task_id"]);
        Assert.Equal("running", entry.Fields["current_status"]);
    }

    [Fact]
    public async Task RestartsTotal_IsExported_ThroughProductionMeterRegistration_WithDeclaredLabelSetOnly()
    {
        var exportedMetrics = new List<Metric>();
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await BuildHostAsync(fixture, new CaptureLogger<IngestRestartObservabilityTests>(), exportedMetrics);
        var client = host.GetTestClient();

        var taskId = await FailATaskAsync(fixture, launcher);
        (await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null)).EnsureSuccessStatusCode();
        await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null); // now non-failed: rejected

        host.Services.GetRequiredService<MeterProvider>().ForceFlush();

        var metric = Assert.Single(exportedMetrics, m => m.Name == "wiki.ingest.restarts_total");
        var outcomes = new List<string>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                Assert.Equal("outcome", tag.Key);
                outcomes.Add((string)tag.Value!);
            }
        }

        Assert.All(outcomes, o => Assert.Contains(o, new[] { "accepted", "rejected" }));
        Assert.Contains("accepted", outcomes);
        Assert.Contains("rejected", outcomes);
    }

    [Fact]
    public async Task RestartSpan_IsExportedAsAChildOfTheAspNetCoreRequestSpan_WithDeclaredAttributes()
    {
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher);
        using var host = await IngestApiHost.BuildAsync(fixture, exported);
        var client = host.GetTestClient();

        var taskId = await FailATaskAsync(fixture, launcher);
        (await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null)).EnsureSuccessStatusCode();

        var span = await exported.WaitForSpanAsync("hub.ingest_task.restart");

        Assert.NotEqual(default, span.ParentSpanId);
        Assert.Equal(taskId, span.GetTagItem("task_id"));
        Assert.Equal("accepted", span.GetTagItem("outcome"));
    }

    private static async Task<string> FailATaskAsync(IngestSubmissionPipelineFixture fixture, FakeAgentProcessLauncher launcher)
    {
        var taskId = await fixture.Pipeline.AcceptAsync(new IngestSubmissionInput(
            IngestSubmissionKind.MarkdownFile, null, "restart-me.md",
            Encoding.UTF8.GetBytes("# Restart Me\n\nBody.\n"), "text/markdown"));

        await PollAsync.WaitAsync(() => launcher.Handles.Count > 0, TimeSpan.FromSeconds(10), "Agent process was never launched.");
        var handle = launcher.Handles[^1];
        handle.EmitEvent("started", taskId);
        handle.EmitEvent("failed", taskId, new { reason = "Agent run failed." });

        await fixture.WaitForPublishedEventAsync(taskId, e => e.ToStatus == "failed");
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "failed", failureReason: "Agent run failed.");
        return taskId;
    }

    private static async Task<IHost> BuildHostAsync(
        IngestSubmissionPipelineFixture fixture, CaptureLogger<IngestRestartObservabilityTests> logger, List<Metric>? exportedMetrics = null)
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

    /// <summary>Routes only <see cref="IngestSubmissionEndpoints"/>'s own logger to the capturing sink (see IngestSourceContentObservabilityTests for the rationale).</summary>
    private sealed class CaptureLoggerFactory(ILogger logger) : ILoggerFactory
    {
        private static readonly string TargetCategory = typeof(IngestSubmissionEndpoints).FullName!;

        public ILogger CreateLogger(string categoryName) =>
            categoryName == TargetCategory ? logger : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }
}

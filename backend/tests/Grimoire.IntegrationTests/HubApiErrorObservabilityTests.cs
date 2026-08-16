using System.Diagnostics;
using System.Net.Http.Json;
using Grimoire.Hub;
using Grimoire.Hub.ApiErrors;
using Grimoire.Hub.IngestSubmission;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// The observability contract for the HTTP failure envelope
/// (024-api-error-presentation, <c>plan.md ## Observability</c>; T054–T057).
///
/// <para>
/// Every signal here is obtained through the production composition root —
/// <c>AddHubTelemetry</c> with in-memory exporters attached to the same provider builders the Hub
/// itself configures. Not a hand-registered <c>ActivitySource</c>, not an always-on
/// <c>ActivityListener</c>. Constitution Principle IV requires that, and the reason is specific to
/// this code path: feature 003 shipped green trace tests while the Hub exported nothing, because
/// every request-path span was parented to an unsampled activity and the default
/// <c>ParentBased</c> sampler dropped it. An error envelope is composed on exactly that path.
/// </para>
///
/// <para>
/// Cross-agent (the envelope spans every endpoint family), so unprefixed per ADR-013 rule N1.
/// Joins the existing serialized collection because <c>AddHubTelemetry</c> installs process-wide
/// ASP.NET Core instrumentation.
/// </para>
/// </summary>
[Collection("HubActivityListenerObservability")]
public class HubApiErrorObservabilityTests
{
    // -----------------------------------------------------------------------
    // T054 — metric contract
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApiErrorsTotal_IsExported_ThroughProductionMeterRegistration_WithCodeAndStatus()
    {
        var exportedMetrics = new List<Metric>();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, exportedMetrics: exportedMetrics);
        var client = host.GetTestClient();

        await client.GetAsync("/api/ingest-submissions/no-such-task");

        host.Services.GetRequiredService<MeterProvider>().ForceFlush();

        var metric = Assert.Single(exportedMetrics, m => m.Name == "hub.api_errors_total");

        var tagKeys = new List<string>();
        var codes = new List<string>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                tagKeys.Add(tag.Key);
                if (tag.Key == "code")
                {
                    codes.Add((string)tag.Value!);
                }
            }
        }

        // The declared label set, and only it — a stray label is how 002's pages_touched_total
        // drifted outside its own contract.
        Assert.All(tagKeys, key => Assert.Contains(key, new[] { "code", "status" }));
        Assert.Contains(ApiErrorCatalogue.IngestTaskNotFound, codes);
    }

    // -----------------------------------------------------------------------
    // T055 — logging contract
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeclinedRequest_EmitsApiErrorDeclined_AtWarning_WithEveryMandatoryField()
    {
        var logger = new CaptureLogger<HubApiErrorObservabilityTests>();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, logger: logger);
        var client = host.GetTestClient();

        await client.GetAsync("/api/ingest-submissions/no-such-task");

        var entry = Assert.Single(logger.Entries, e => e.EventName == "api.error.declined");
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(ApiErrorCatalogue.IngestTaskNotFound, entry.Fields["code"]);
        Assert.Equal(404, entry.Fields["status"]);
        Assert.Equal("/api/ingest-submissions/no-such-task", entry.Fields["path"]);
        Assert.False(string.IsNullOrWhiteSpace(entry.Fields["trace_id"]?.ToString()));
    }

    [Fact]
    public async Task Fault_EmitsApiErrorFaulted_AtError_WithEveryMandatoryField()
    {
        var logger = new CaptureLogger<HubApiErrorObservabilityTests>();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, logger: logger);
        var client = host.GetTestClient();

        await client.GetAsync("/boom");

        var entry = Assert.Single(logger.Entries, e => e.EventName == "api.error.faulted");
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(ApiErrorCatalogue.InternalError, entry.Fields["code"]);
        Assert.Equal(500, entry.Fields["status"]);
        Assert.Equal("/boom", entry.Fields["path"]);
        Assert.False(string.IsNullOrWhiteSpace(entry.Fields["trace_id"]?.ToString()));

        // The exception's own text belongs here and nowhere else — the response body carries the
        // generic detail instead (024 FR-015, and HubApiErrorEnvelopeTests asserts the other half).
        Assert.Contains(ThrownMessage, entry.Fields["failure_reason"]?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // T056 — trace contract
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeclinedSpan_IsExportedUnderProductionRegistration_AsAChildOfTheRequestSpan()
    {
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, exportedActivities: exported);
        var client = host.GetTestClient();

        await client.GetAsync("/api/ingest-submissions/no-such-task");

        var span = await exported.WaitForSpanAsync("api.error.declined");

        // Parentage is the assertion that matters. A span that is emitted but orphaned is exactly
        // what 003 shipped, and it is invisible to an operator following a trace.
        Assert.NotEqual(default, span.ParentSpanId);
        Assert.True(span.Recorded);
        Assert.Equal("log", span.GetTagItem("signal_type"));
        Assert.Equal("api.error.declined", span.GetTagItem("event_name"));
        Assert.Equal("Warning", span.GetTagItem("level"));
        Assert.Equal(ApiErrorCatalogue.IngestTaskNotFound, span.GetTagItem("code"));
        Assert.Equal(404, span.GetTagItem("status"));
    }

    [Fact]
    public async Task FaultedSpan_IsExportedUnderProductionRegistration_AsAChildOfTheRequestSpan()
    {
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, exportedActivities: exported);
        var client = host.GetTestClient();

        await client.GetAsync("/boom");

        var span = await exported.WaitForSpanAsync("api.error.faulted");

        Assert.NotEqual(default, span.ParentSpanId);
        Assert.True(span.Recorded);
        Assert.Equal("log", span.GetTagItem("signal_type"));
        Assert.Equal("Error", span.GetTagItem("level"));
        Assert.Equal(ApiErrorCatalogue.InternalError, span.GetTagItem("code"));
        Assert.Equal(500, span.GetTagItem("status"));
    }

    // -----------------------------------------------------------------------
    // T057 — correlation
    // -----------------------------------------------------------------------

    /// <summary>
    /// The log's <c>trace_id</c>, the span's trace id, and the response body's <c>traceId</c> are
    /// one value. Without that, the identifier handed to a user in a screenshot joins nothing and
    /// the technical-detail disclosure is decoration.
    /// </summary>
    [Fact]
    public async Task TraceId_IsOneValue_AcrossTheLogEvent_TheSpan_AndTheResponseBody()
    {
        var logger = new CaptureLogger<HubApiErrorObservabilityTests>();
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await BuildHostAsync(fixture, logger: logger, exportedActivities: exported);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/no-such-task");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var bodyTraceId = body.GetProperty("traceId").GetString();

        var entry = Assert.Single(logger.Entries, e => e.EventName == "api.error.declined");
        Assert.Equal(bodyTraceId, entry.Fields["trace_id"]?.ToString());

        var span = await exported.WaitForSpanAsync("api.error.declined");
        Assert.Equal(bodyTraceId, span.TraceId.ToString());
    }

    // -----------------------------------------------------------------------

    private const string ThrownMessage = "deliberate fault for the observability contract";

    private static async Task<IHost> BuildHostAsync(
        IngestSubmissionPipelineFixture fixture,
        CaptureLogger<HubApiErrorObservabilityTests>? logger = null,
        List<Metric>? exportedMetrics = null,
        ICollection<Activity>? exportedActivities = null)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
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

                    if (logger is not null)
                    {
                        services.AddSingleton<ILoggerFactory>(new CaptureLoggerFactory(logger));
                    }

                    services.AddProblemDetails();
                    services.AddExceptionHandler<ApiErrorExceptionHandler>();

                    services.AddSingleton(fixture.Validator);
                    services.AddSingleton(fixture.Pipeline);
                    services.AddSingleton(fixture.BoardStore);
                    services.AddSingleton(fixture.ContentPaths);
                    services.AddSingleton(fixture.RawPaths);
                    services.AddSingleton(fixture.ResolvedPaths);
                    services.AddSingleton(fixture.SourceArtifactStore);
                    services.AddSingleton(fixture.Coordinator);
                    services.AddSingleton(fixture.Repository);
                    services.AddSingleton(new TaskRecordReadModel(fixture.ResolvedPaths));
                });
                webHost.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
                        endpoints.MapGet("/boom", void () => throw new InvalidOperationException(ThrownMessage));
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }

    /// <summary>
    /// Routes only the envelope's own logger category to the capturing sink; every other category
    /// (ASP.NET Core's hosting/routing diagnostics, emitted on background threads) gets a no-op
    /// logger, because handing that chatter to an unsynchronized capture races the test's own
    /// enumeration of it.
    /// </summary>
    private sealed class CaptureLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName == "Grimoire.Hub.ApiErrors"
                ? logger
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }
}

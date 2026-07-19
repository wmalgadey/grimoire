using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub;

public static class TelemetryExtensions
{
    private static ResourceBuilder CreateResource() => ResourceBuilder.CreateDefault().AddService("Grimoire.Hub");

    /// <summary>
    /// A standalone logger factory for the narrow window before <c>WebApplicationBuilder.Build()</c>
    /// (runtime path resolution, ADR-009) where the DI-registered logging pipeline from
    /// <see cref="AddHubTelemetry"/> is not yet available. Uses the same resource/exporter
    /// configuration so bootstrap-time log events still correlate with the rest of the telemetry.
    /// Callers own disposal.
    /// </summary>
    public static ILoggerFactory CreateBootstrapLoggerFactory() =>
        LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.SetResourceBuilder(CreateResource());
                logging.AddOtlpExporter();
            }));

    /// <summary>
    /// Registers the Hub's production telemetry pipeline. <paramref name="configureTracing"/> lets
    /// tests attach an additional exporter (e.g. <c>AddInMemoryExporter</c>) to the same
    /// <see cref="TracerProviderBuilder"/> the app uses, so tests observe span export decisions made
    /// under the real sampler/instrumentation instead of a test-only always-record listener.
    /// </summary>
    public static IServiceCollection AddHubTelemetry(
        this IServiceCollection services, Action<TracerProviderBuilder>? configureTracing = null)
    {
        var resource = CreateResource();

        services.AddLogging(builder =>
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.SetResourceBuilder(resource);
                logging.AddOtlpExporter();
            }));

        services.AddOpenTelemetry()
            .ConfigureResource(resourceBuilder => resourceBuilder.AddService("Grimoire.Hub"))
            .WithTracing(builder =>
            {
                builder
                    .AddSource("Grimoire.Hub")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
                configureTracing?.Invoke(builder);
            })
            .WithMetrics(builder => builder
                .AddMeter("Grimoire.Hub")
                .AddOtlpExporter());

        return services;
    }
}

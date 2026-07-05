using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub;

public static class TelemetryExtensions
{
    public static IServiceCollection AddHubTelemetry(this IServiceCollection services)
    {
        var resource = ResourceBuilder.CreateDefault().AddService("Grimoire.Hub");

        services.AddLogging(builder =>
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.SetResourceBuilder(resource);
                logging.AddOtlpExporter();
            }));

        services.AddOpenTelemetry()
            .ConfigureResource(resourceBuilder => resourceBuilder.AddService("Grimoire.Hub"))
            .WithTracing(builder => builder
                .AddSource("Grimoire.Hub")
                .AddOtlpExporter())
            .WithMetrics(builder => builder
                .AddMeter("Grimoire.Hub")
                .AddOtlpExporter());

        return services;
    }
}

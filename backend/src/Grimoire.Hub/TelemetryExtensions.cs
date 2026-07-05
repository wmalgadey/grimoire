using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Grimoire.Hub;

public static class TelemetryExtensions
{
    public static IServiceCollection AddHubTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Grimoire.Hub"))
            .WithLogging(builder => builder.AddOtlpExporter())
            .WithTracing(builder => builder
                .AddSource("Grimoire.Hub")
                .AddOtlpExporter())
            .WithMetrics(builder => builder
                .AddMeter("Grimoire.Hub")
                .AddOtlpExporter());

        return services;
    }
}

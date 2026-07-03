using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Grimoire.IngestAgent;

public static class TelemetryBootstrap
{
    public static IDisposable Build()
    {
        var provider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("Grimoire.IngestAgent")
            .ConfigureResource(resource => resource.AddService("Grimoire.IngestAgent"))
            .AddOtlpExporter()
            .Build();

        var meterProvider = OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddMeter("Grimoire.IngestAgent")
            .ConfigureResource(resource => resource.AddService("Grimoire.IngestAgent"))
            .AddOtlpExporter()
            .Build();

        return new CompositeDisposable(provider, meterProvider);
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }
}

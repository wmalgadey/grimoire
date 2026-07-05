using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry;

namespace Grimoire.IngestAgent;

public static class TelemetryBootstrap
{
    public static TelemetryBootstrapHandle Build()
    {
        var resource = ResourceBuilder.CreateDefault().AddService("Grimoire.IngestAgent");

        // Console agent process has no generic host lifecycle. Build providers explicitly
        // so ActivitySource listeners are active immediately.
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource("Grimoire.IngestAgent")
            .AddOtlpExporter()
            .Build();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter("Grimoire.IngestAgent")
            .AddOtlpExporter()
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.SetResourceBuilder(resource);
                logging.AddOtlpExporter();
            }));

        return new TelemetryBootstrapHandle(
            loggerFactory,
            tracerProvider,
            meterProvider);
    }

    public sealed class TelemetryBootstrapHandle : IDisposable
    {
        private readonly TracerProvider _tracerProvider;
        private readonly MeterProvider _meterProvider;

        public TelemetryBootstrapHandle(
            ILoggerFactory loggerFactory,
            TracerProvider tracerProvider,
            MeterProvider meterProvider)
        {
            LoggerFactory = loggerFactory;
            _tracerProvider = tracerProvider;
            _meterProvider = meterProvider;
        }

        public ILoggerFactory LoggerFactory { get; }

        public void Dispose()
        {
            LoggerFactory.Dispose();
            _meterProvider.Dispose();
            _tracerProvider.Dispose();
        }
    }
}

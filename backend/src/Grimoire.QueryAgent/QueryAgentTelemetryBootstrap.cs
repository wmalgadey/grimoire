using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry;

namespace Grimoire.QueryAgent;

public static class QueryAgentTelemetryBootstrap
{
    public static QueryAgentTelemetryBootstrapHandle Build()
    {
        var resource = ResourceBuilder.CreateDefault().AddService("Grimoire.QueryAgent");

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource("Grimoire.QueryAgent")
            .AddOtlpExporter()
            .Build();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter("Grimoire.QueryAgent")
            .AddOtlpExporter()
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.SetResourceBuilder(resource);
                logging.AddOtlpExporter();
            }));

        return new QueryAgentTelemetryBootstrapHandle(loggerFactory, tracerProvider, meterProvider);
    }

    public sealed class QueryAgentTelemetryBootstrapHandle : IDisposable
    {
        private readonly TracerProvider _tracerProvider;
        private readonly MeterProvider _meterProvider;

        public QueryAgentTelemetryBootstrapHandle(
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

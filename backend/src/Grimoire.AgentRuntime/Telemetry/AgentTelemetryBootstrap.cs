using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry;

namespace Grimoire.AgentRuntime.Telemetry;

/// <summary>
/// The one OTel provider bootstrap for agent host processes (ADR-013 D1; consolidates
/// the formerly duplicated Grimoire.IngestAgent/TelemetryBootstrap.cs and
/// Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs byte-compatibly: same resource,
/// source, meter, and OTLP exporter wiring). The frozen per-agent identities
/// (service/source/meter names, e.g. "Grimoire.IngestAgent") arrive as inputs from the
/// host's Agent Profile — never as literals in the platform (ADR-005/ADR-013).
/// </summary>
public static class AgentTelemetryBootstrap
{
    public static AgentTelemetryHandle Build(string serviceName, string activitySourceName, string meterName)
    {
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);

        // Console agent process has no generic host lifecycle. Build providers explicitly
        // so ActivitySource listeners are active immediately.
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(activitySourceName)
            .AddOtlpExporter()
            .Build();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(meterName)
            .AddOtlpExporter()
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.SetResourceBuilder(resource);
                logging.AddOtlpExporter();
            }));

        return new AgentTelemetryHandle(loggerFactory, tracerProvider, meterProvider);
    }

    public sealed class AgentTelemetryHandle : IDisposable
    {
        private readonly TracerProvider _tracerProvider;
        private readonly MeterProvider _meterProvider;

        public AgentTelemetryHandle(
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

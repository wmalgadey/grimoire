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
    /// <param name="configureTracing">
    /// 025-agent-owned-log (T029): an optional extra step applied to the <em>same</em>
    /// tracer-provider construction production uses — same resource, same registered
    /// source, same default sampler — so an observability contract test can attach an
    /// in-memory exporter without standing up a test-only provider. This mirrors the Hub's
    /// existing <c>AddHubTelemetry(configureTracing)</c> hook, and exists because
    /// Constitution Principle IV requires contract tests to obtain their signals from the
    /// production composition root: a hand-attached <c>ActivityListener</c> proves only
    /// that the emitting line ran, not that the signal reaches an observer in production
    /// (the Feature-003 false negative). <c>null</c> in production.
    /// </param>
    /// <param name="configureMetrics">The metric-provider counterpart of <paramref name="configureTracing"/>.</param>
    public static AgentTelemetryHandle Build(
        string serviceName,
        string activitySourceName,
        string meterName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);

        // Console agent process has no generic host lifecycle. Build providers explicitly
        // so ActivitySource listeners are active immediately.
        var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(activitySourceName);
        configureTracing?.Invoke(tracerBuilder);
        var tracerProvider = tracerBuilder
            .AddOtlpExporter()
            .Build();

        var meterBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(meterName);
        configureMetrics?.Invoke(meterBuilder);
        var meterProvider = meterBuilder
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

        /// <summary>
        /// Flushes pending spans to the registered exporters. Production relies on the
        /// provider's own batching and on <see cref="Dispose"/>; observability contract
        /// tests need the export to have happened before they assert on it.
        /// </summary>
        public bool ForceFlushTraces(int timeoutMilliseconds = 5000)
            => _tracerProvider.ForceFlush(timeoutMilliseconds);

        /// <summary>The metric counterpart of <see cref="ForceFlushTraces"/>.</summary>
        public bool ForceFlushMetrics(int timeoutMilliseconds = 5000)
            => _meterProvider.ForceFlush(timeoutMilliseconds);

        public void Dispose()
        {
            LoggerFactory.Dispose();
            _meterProvider.Dispose();
            _tracerProvider.Dispose();
        }
    }
}

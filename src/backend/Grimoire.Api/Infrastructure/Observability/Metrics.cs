using System.Diagnostics.Metrics;

namespace Grimoire.Api.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry metrics for Hub Foundation feature.
/// </summary>
public class HubMetrics
{
    private readonly Meter _meter;

    public Counter<int> AgentRegisteredTotal { get; }
    public Counter<int> AgentFailedTotal { get; }
    public Counter<int> JobQueuedTotal { get; }
    public Counter<int> JobCompletedTotal { get; }
    public Histogram<double> HealthCheckDurationMs { get; }

    public HubMetrics(string version = "1.0.0")
    {
        _meter = new Meter("Grimoire.Hub", version);

        AgentRegisteredTotal = _meter.CreateCounter<int>(
            "grimoire.hub.agent.registered_total",
            description: "Total agents registered since Hub startup");

        AgentFailedTotal = _meter.CreateCounter<int>(
            "grimoire.hub.agent.failed_total",
            description: "Total agents that transitioned to Faulted state");

        JobQueuedTotal = _meter.CreateCounter<int>(
            "grimoire.hub.job.queued_total",
            description: "Total jobs queued for dispatch");

        JobCompletedTotal = _meter.CreateCounter<int>(
            "grimoire.hub.job.completed_total",
            description: "Total jobs completed (success or failure)");

        HealthCheckDurationMs = _meter.CreateHistogram<double>(
            "grimoire.hub.health_check_duration_ms",
            unit: "ms",
            description: "Duration of health check operations in milliseconds");
    }

    public Meter Meter => _meter;
}


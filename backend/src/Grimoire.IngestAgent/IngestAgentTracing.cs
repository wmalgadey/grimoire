using System.Diagnostics;
using Grimoire.AgentRuntime.Telemetry;

namespace Grimoire.IngestAgent;

/// <summary>
/// Ingest's frozen tracing identities (ADR-005: source `Grimoire.IngestAgent`, root
/// span `ingest_agent.run`, correlation attribute `task_id`), delegating to the shared
/// <see cref="AgentTracing"/> scaffold (ADR-013 — the TRACEPARENT/TRACESTATE parenting
/// that links the Hub's `hub.ingest_run.trigger` span to this agent run into a single
/// end-to-end trace now lives once in the platform).
/// </summary>
public static class IngestAgentTracing
{
    private static readonly AgentTracing _tracing = new(
        "Grimoire.IngestAgent", "ingest_agent.run", "task_id");

    public static ActivitySource ActivitySource => _tracing.ActivitySource;

    public static Activity? StartRunActivity(string taskId)
        => _tracing.StartRunActivity(taskId);
}

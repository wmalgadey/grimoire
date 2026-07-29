using System.Diagnostics;
using Grimoire.AgentRuntime.Telemetry;

namespace Grimoire.QueryAgent;

/// <summary>
/// Query's frozen tracing identities (ADR-005: source `Grimoire.QueryAgent`, root span
/// `query_agent.run`, correlation attribute `turn_id`), delegating to the shared
/// <see cref="AgentTracing"/> scaffold (ADR-013 — the TRACEPARENT/TRACESTATE parenting
/// that links the Hub's `hub.query.spawn_agent` span to this agent run into a single
/// end-to-end trace now lives once in the platform).
/// </summary>
public static class QueryAgentTracing
{
    private static readonly AgentTracing _tracing = new(
        "Grimoire.QueryAgent", "query_agent.run", "turn_id");

    public static ActivitySource ActivitySource => _tracing.ActivitySource;

    public static Activity? StartRunActivity(string turnId)
        => _tracing.StartRunActivity(turnId);
}

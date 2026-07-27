using System.Diagnostics;

namespace Grimoire.QueryAgent;

public static class QueryAgentTracing
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.QueryAgent", "1.0.0");

    /// <summary>
    /// Starts the `query_agent.run` root span, parented to the `TRACEPARENT`/`TRACESTATE`
    /// environment variables the Hub propagates when it dispatches this process (mirrors
    /// `IngestAgentTracing.StartRunActivity`), so the Hub's `hub.query.spawn_agent` span
    /// and this agent run form a single end-to-end trace (Constitution IV).
    /// </summary>
    public static Activity? StartRunActivity(string turnId)
    {
        var traceParent = Environment.GetEnvironmentVariable("TRACEPARENT");
        var traceState = Environment.GetEnvironmentVariable("TRACESTATE");

        var activity = !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var parentContext)
            ? ActivitySource.StartActivity("query_agent.run", ActivityKind.Internal, parentContext)
            : ActivitySource.StartActivity("query_agent.run");

        activity?.SetTag("turn_id", turnId);
        return activity;
    }
}

using System.Diagnostics;

namespace Grimoire.IngestAgent;

public static class IngestAgentTracing
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.IngestAgent", "1.0.0");

    /// <summary>
    /// Starts the `ingest_agent.run` root span, parented to the `TRACEPARENT`/`TRACESTATE`
    /// environment variables the Hub propagates when it dispatches this process (see
    /// `IngestAgentDispatcher.BuildChildEnvironment`), so the Hub's `hub.ingest_run.trigger` span
    /// and this agent run form a single end-to-end trace instead of two disconnected trees
    /// (Constitution IV).
    /// </summary>
    public static Activity? StartRunActivity(string taskId)
    {
        var traceParent = Environment.GetEnvironmentVariable("TRACEPARENT");
        var traceState = Environment.GetEnvironmentVariable("TRACESTATE");

        var activity = !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var parentContext)
            ? ActivitySource.StartActivity("ingest_agent.run", ActivityKind.Internal, parentContext)
            : ActivitySource.StartActivity("ingest_agent.run");

        activity?.SetTag("task_id", taskId);
        return activity;
    }
}
